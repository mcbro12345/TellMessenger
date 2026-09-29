using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace TellMessenger.Game;

// Character faces from the Lodestone, used as avatars. Each person is looked
// up once per session by name and home world on the Lodestone's character
// search. Nothing is written to disk: the picture is kept in memory, shrunk to
// 128×128, and each size it's drawn at gets its own small texture. Lookups run
// one at a time in the background.
public sealed class LodestonePortraits : IDisposable
{
    private const int KeepSize = 128;
    private static readonly TimeSpan RetryErrorAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RequestGap = TimeSpan.FromSeconds(2);

    // One search result: the character link, face image, name and home world.
    private static readonly Regex Entry = new(
        @"<a href=""/lodestone/character/(\d+)/"" class=""entry__link""><div class=""entry__chara__face""><img src=""([^""]+)""[^>]*></div>" +
        @"<div class=""entry__box[^""]*""><p class=""entry__name"">([^<]+)</p><p class=""entry__world"">(?:<i[^>]*></i>)?([^<\[]+)",
        RegexOptions.Compiled);

    private readonly Configuration config;
    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<string, State> states = new();
    private readonly object gate = new();
    private readonly ConcurrentQueue<(string Name, string World)> queue = new();
    private readonly SemaphoreSlim wake = new(0);
    private readonly Task worker;

    public LodestonePortraits(Configuration config, string configDirectory)
    {
        this.config = config;
        // Older versions saved pictures here.
        try
        {
            var old = Path.Combine(configDirectory, "portraits");
            if (Directory.Exists(old))
                Directory.Delete(old, true);
        }
        catch (Exception e)
        {
            Services.Log.Debug(e, "Could not remove the old portrait folder");
        }

        http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TellMessenger/0.1 (Dalamud plugin)");
        worker = Task.Run(RunAsync);
    }

    public void Dispose()
    {
        stop.Cancel();
        wake.Release();
        try { worker.Wait(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        http.Dispose();
        stop.Dispose();
        ClearFaces();
    }

    // This character's face, or null while it's being fetched or if they
    // have none. Asking queues a lookup the first time.
    public Face? FaceFor(string name, string world)
    {
        if (!config.UseLodestonePortraits || name.Length == 0 || world.Length == 0)
            return null;

        var key = $"{name}@{world}".ToLowerInvariant();
        lock (gate)
        {
            if (!states.TryGetValue(key, out var state))
            {
                states[key] = new State();
                queue.Enqueue((name, world));
                wake.Release();
                return null;
            }
            if (state.Face == null && state.FailedAt is { } failed && DateTime.UtcNow - failed > RetryErrorAfter)
            {
                state.FailedAt = null;
                queue.Enqueue((name, world));
                wake.Release();
            }
            return state.Face;
        }
    }

    // Looks everyone up again.
    public void ClearCache() => ClearFaces();

    private void ClearFaces()
    {
        lock (gate)
        {
            foreach (var state in states.Values)
                state.Face?.Dispose();
            states.Clear();
        }
    }

    private async Task RunAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await wake.WaitAsync(stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (queue.TryDequeue(out var next) && !stop.IsCancellationRequested)
            {
                var key = $"{next.Name}@{next.World}".ToLowerInvariant();
                try
                {
                    var face = await FetchAsync(next.Name, next.World);
                    lock (gate)
                    {
                        if (states.TryGetValue(key, out var state))
                            state.Face = face;
                        else
                            face?.Dispose(); // cleared meanwhile
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    Services.Log.Warning($"Lodestone lookup for {next.Name}@{next.World} failed: {e.Message}");
                    lock (gate)
                    {
                        if (states.TryGetValue(key, out var state))
                            state.FailedAt = DateTime.UtcNow;
                    }
                }

                try { await Task.Delay(RequestGap, stop.Token); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task<Face?> FetchAsync(string name, string world)
    {
        var url = $"https://na.finalfantasyxiv.com/lodestone/character/?q={Uri.EscapeDataString(name)}&worldname={Uri.EscapeDataString(world)}";
        var html = await http.GetStringAsync(url, stop.Token);

        string? imageUrl = null;
        foreach (Match match in Entry.Matches(html))
        {
            var foundName = WebUtility.HtmlDecode(match.Groups[3].Value).Trim();
            var foundWorld = WebUtility.HtmlDecode(match.Groups[4].Value).Trim();
            if (foundName.Equals(name, StringComparison.OrdinalIgnoreCase) && foundWorld.Equals(world, StringComparison.OrdinalIgnoreCase))
            {
                imageUrl = WebUtility.HtmlDecode(match.Groups[2].Value);
                break;
            }
        }
        if (imageUrl == null)
            return null;

        // Decode on the GPU, read the pixels back, and keep a 128×128 copy.
        var bytes = await http.GetByteArrayAsync(imageUrl, stop.Token);
        using var full = await Services.Textures.CreateFromImageAsync(bytes, $"TellMessenger portrait {name}", stop.Token);
        var (spec, data) = await Services.TextureReadback.GetRawImageAsync(full, default, true, stop.Token);
        if (spec.BitsPerPixel != 32)
            throw new NotSupportedException($"Unexpected pixel format {spec.DxgiFormat}");
        var size = Math.Min(KeepSize, Math.Min(spec.Width, spec.Height));
        var pixels = Face.AreaAverage(data, spec.Width, spec.Height, spec.Pitch, size);
        return new Face(pixels, size, spec.DxgiFormat);
    }

    private sealed class State
    {
        public Face? Face;
        public DateTime? FailedAt;
    }

    // One person's picture: the kept pixels, plus a texture for each size it
    // has been drawn at. Shrinking on the CPU with a proper average keeps it
    // sharp; the GPU's own scaling looks soft and grainy.
    public sealed class Face : IDisposable
    {
        private readonly byte[] pixels;
        private readonly int size;
        private readonly int format;
        private readonly Dictionary<int, IDalamudTextureWrap> textures = new();
        private readonly Dictionary<int, Task<IDalamudTextureWrap>> pending = new();
        private bool disposed;

        public Face(byte[] pixels, int size, int format)
        {
            this.pixels = pixels;
            this.size = size;
            this.format = format;
        }

        // A texture exactly `px` pixels wide, or null for a frame or two while
        // it's made. Called from the draw thread.
        public IDalamudTextureWrap? At(int px)
        {
            if (disposed || px <= 0)
                return null;
            px = Math.Min(px, size);
            if (textures.TryGetValue(px, out var wrap))
                return wrap;
            if (pending.TryGetValue(px, out var task))
            {
                if (!task.IsCompleted)
                    return null;
                pending.Remove(px);
                if (!task.IsCompletedSuccessfully)
                    return null;
                return textures[px] = task.Result;
            }
            pending[px] = Task.Run(() =>
            {
                var data = px == size ? pixels : AreaAverage(pixels, size, size, size * 4, px);
                return Services.Textures.CreateFromRaw(new RawImageSpecification(px, px, format, px * 4), data, "TellMessenger portrait");
            });
            return null;
        }

        public void Dispose()
        {
            disposed = true;
            foreach (var wrap in textures.Values)
                wrap.Dispose();
            textures.Clear();
            foreach (var task in pending.Values)
                task.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); });
            pending.Clear();
        }

        // Each output pixel is the average of the source pixels it covers,
        // weighted by how much of each one falls inside it.
        public static byte[] AreaAverage(byte[] src, int width, int height, int pitch, int outSize)
        {
            var dst = new byte[outSize * outSize * 4];
            var scaleX = (double)width / outSize;
            var scaleY = (double)height / outSize;
            for (var y = 0; y < outSize; y++)
            {
                var y0 = y * scaleY;
                var y1 = y0 + scaleY;
                for (var x = 0; x < outSize; x++)
                {
                    var x0 = x * scaleX;
                    var x1 = x0 + scaleX;
                    double c0 = 0, c1 = 0, c2 = 0, c3 = 0, total = 0;
                    for (var sy = (int)y0; sy < Math.Min(height, (int)Math.Ceiling(y1)); sy++)
                    {
                        var wy = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
                        for (var sx = (int)x0; sx < Math.Min(width, (int)Math.Ceiling(x1)); sx++)
                        {
                            var w = wy * (Math.Min(x1, sx + 1) - Math.Max(x0, sx));
                            var i = sy * pitch + sx * 4;
                            c0 += src[i] * w;
                            c1 += src[i + 1] * w;
                            c2 += src[i + 2] * w;
                            c3 += src[i + 3] * w;
                            total += w;
                        }
                    }
                    var o = (y * outSize + x) * 4;
                    dst[o] = (byte)Math.Round(c0 / total);
                    dst[o + 1] = (byte)Math.Round(c1 / total);
                    dst[o + 2] = (byte)Math.Round(c2 / total);
                    dst[o + 3] = (byte)Math.Round(c3 / total);
                }
            }
            return dst;
        }
    }
}
