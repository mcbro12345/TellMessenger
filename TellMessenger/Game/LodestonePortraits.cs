using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using TellMessenger.Model;

namespace TellMessenger.Game;

// Character faces from the Lodestone, used as avatars. Pictures are only
// kept in memory, shrunk to 128×128, never written to disk. What is saved is
// each person's picture address, so next time it loads straight away instead
// of searching the Lodestone by name and home world first. When the plugin
// loads, everyone from your history is loaded again in the background, the
// people you're looking at first. A picture loaded from a saved address is
// checked against the Lodestone once per session, in case their look changed.
public sealed class LodestonePortraits : IDisposable
{
    private const int KeepSize = 128;
    private const int Workers = 4;
    private static readonly TimeSpan RetryErrorAfter = TimeSpan.FromMinutes(10);
    // Searches are the slow, rate-limited part; pictures come from a CDN.
    private static readonly TimeSpan SearchGap = TimeSpan.FromMilliseconds(600);

    // One search result: the character link, face image, name and home world.
    private static readonly Regex Entry = new(
        @"<a href=""/lodestone/character/(\d+)/"" class=""entry__link""><div class=""entry__chara__face""><img src=""([^""]+)""[^>]*></div>" +
        @"<div class=""entry__box[^""]*""><p class=""entry__name"">([^<]+)</p><p class=""entry__world"">(?:<i[^>]*></i>)?([^<\[]+)",
        RegexOptions.Compiled);

    private readonly Configuration config;
    private readonly HistoryStore store;
    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<string, State> states = new();
    private readonly object gate = new();
    // Who's on screen first, then everyone from history, then re-checks.
    private readonly ConcurrentQueue<Job> urgent = new();
    private readonly ConcurrentQueue<Job> background = new();
    private readonly ConcurrentQueue<Job> rechecks = new();
    private readonly ConcurrentDictionary<string, string> urls;
    private readonly SemaphoreSlim wake = new(0);
    private readonly SemaphoreSlim searchLock = new(1);
    private DateTime lastSearch = DateTime.MinValue;
    private readonly Task[] workers;

    private sealed record Job(string Name, string World, string Key, bool Recheck = false);

    public LodestonePortraits(Configuration config, HistoryStore store, string configDirectory)
    {
        this.config = config;
        this.store = store;
        urls = new ConcurrentDictionary<string, string>(store.PortraitUrls());
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
        workers = Enumerable.Range(0, Workers).Select(_ => Task.Run(RunAsync)).ToArray();
    }

    public void Dispose()
    {
        stop.Cancel();
        wake.Release(Workers);
        try { Task.WaitAll(workers, TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        http.Dispose();
        stop.Dispose();
        ClearFaces();
        moogle?.Dispose();
    }

    private static string KeyOf(string name, string world) => $"{name}@{world}".ToLowerInvariant();

    // This character's face, or null while it's being fetched or if they
    // have none. Asking puts them at the front of the line.
    public Face? FaceFor(string name, string world)
    {
        if (!config.UseLodestonePortraits || name.Length == 0 || world.Length == 0)
            return null;

        var key = KeyOf(name, world);
        lock (gate)
        {
            if (!states.TryGetValue(key, out var state))
            {
                states[key] = state = new State { Urgent = true };
                Enqueue(urgent, new Job(name, world, key));
                return null;
            }
            if (state.Face == null && state.FailedAt is { } failed && DateTime.UtcNow - failed > RetryErrorAfter)
            {
                state.FailedAt = null;
                state.Started = false;
                Enqueue(urgent, new Job(name, world, key));
            }
            else if (!state.Started && !state.Urgent)
            {
                // Waiting in the background line: move them up.
                state.Urgent = true;
                Enqueue(urgent, new Job(name, world, key));
            }
            return state.Face;
        }
    }

    // Starts loading these people now, before anyone asks.
    public void Prefetch(IEnumerable<(string Name, string World)> people)
    {
        if (!config.UseLodestonePortraits)
            return;
        lock (gate)
        {
            foreach (var (name, world) in people)
            {
                var key = KeyOf(name, world);
                if (name.Length == 0 || world.Length == 0 || states.ContainsKey(key))
                    continue;
                states[key] = new State();
                Enqueue(background, new Job(name, world, key));
            }
        }
    }

    private void Enqueue(ConcurrentQueue<Job> queue, Job job)
    {
        queue.Enqueue(job);
        wake.Release();
    }

    // The test contact's picture: the wind-up moogle minion's icon from the
    // game files, on a soft lavender background.
    private const string MoogleIcon = "ui/icon/004000/004472_hr1.tex";
    private Face? moogle;
    private bool moogleTried;

    public Face? Moogle()
    {
        if (moogleTried)
            return moogle;
        moogleTried = true;
        try
        {
            var tex = Services.Data.GetFile<Lumina.Data.Files.TexFile>(MoogleIcon);
            if (tex == null)
                return null;
            int width = tex.Header.Width, height = tex.Header.Height;
            var size = Math.Min(width, height);
            var data = tex.ImageData; // BGRA
            var pixels = new byte[size * size * 4];
            ReadOnlySpan<byte> background = [0xE8, 0xB8, 0xC9]; // B, G, R
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var i = (y * width + x) * 4;
                    var o = (y * size + x) * 4;
                    var a = data[i + 3] / 255f;
                    for (var c = 0; c < 3; c++)
                        pixels[o + c] = (byte)MathF.Round(data[i + c] * a + background[c] * (1 - a));
                    pixels[o + 3] = 255;
                }
            }
            moogle = new Face(pixels, size, 87); // DXGI_FORMAT_B8G8R8A8_UNORM
        }
        catch (Exception e)
        {
            Services.Log.Debug(e, "Couldn't load the moogle icon");
        }
        return moogle;
    }
    // Looks everyone up again, forgetting saved addresses too.
    public void ClearCache()
    {
        ClearFaces();
        urls.Clear();
        store.ClearPortraitUrls();
    }

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

            if (!TryNext(out var job))
                continue;
            try
            {
                if (job.Recheck)
                    await RecheckAsync(job);
                else
                    await LoadAsync(job);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Services.Log.Warning($"Lodestone portrait for {job.Name}@{job.World} failed: {e.Message}");
                lock (gate)
                {
                    if (!job.Recheck && states.TryGetValue(job.Key, out var state))
                        state.FailedAt = DateTime.UtcNow;
                }
            }
        }
    }

    private bool TryNext(out Job job)
    {
        while (urgent.TryDequeue(out job!) || background.TryDequeue(out job!) || rechecks.TryDequeue(out job!))
        {
            if (job.Recheck)
                return true;
            lock (gate)
            {
                // Skip anyone already done, being fetched, or cleared since.
                if (!states.TryGetValue(job.Key, out var state) || state.Started)
                    continue;
                state.Started = true;
                return true;
            }
        }
        return false;
    }

    private async Task LoadAsync(Job job)
    {
        // A saved address: no search needed. Check it later in the session.
        if (urls.TryGetValue(job.Key, out var saved))
        {
            var fromSaved = await TryLoadImageAsync(saved, job.Name);
            if (fromSaved != null)
            {
                SetFace(job.Key, fromSaved);
                Enqueue(rechecks, job with { Recheck = true });
                return;
            }
        }

        var url = await SearchAsync(job.Name, job.World);
        Remember(job.Key, url);
        SetFace(job.Key, url != null ? await LoadImageAsync(url, job.Name) : null);
    }

    // Their look may have changed since the address was saved.
    private async Task RecheckAsync(Job job)
    {
        var url = await SearchAsync(job.Name, job.World);
        if (url == null || urls.TryGetValue(job.Key, out var saved) && saved == url)
            return;
        Remember(job.Key, url);
        SetFace(job.Key, await LoadImageAsync(url, job.Name));
    }

    private void SetFace(string key, Face? face)
    {
        Face? unused;
        lock (gate)
        {
            if (states.TryGetValue(key, out var state))
            {
                unused = state.Face;
                state.Face = face;
            }
            else
            {
                unused = face; // cleared meanwhile
            }
        }
        // Let go of the one no longer shown on the game's thread, between frames.
        if (unused != null)
            Services.Framework.RunOnTick(unused.Dispose);
    }

    private void Remember(string key, string? url)
    {
        if (url != null)
            urls[key] = url;
        else
            urls.TryRemove(key, out _);
        Services.Framework.RunOnTick(() => store.SetPortraitUrl(key, url));
    }

    private async Task<string?> SearchAsync(string name, string world)
    {
        await searchLock.WaitAsync(stop.Token);
        try
        {
            var wait = lastSearch + SearchGap - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, stop.Token);
            var url = $"https://na.finalfantasyxiv.com/lodestone/character/?q={Uri.EscapeDataString(name)}&worldname={Uri.EscapeDataString(world)}";
            var html = await http.GetStringAsync(url, stop.Token);
            foreach (Match match in Entry.Matches(html))
            {
                var foundName = WebUtility.HtmlDecode(match.Groups[3].Value).Trim();
                var foundWorld = WebUtility.HtmlDecode(match.Groups[4].Value).Trim();
                if (foundName.Equals(name, StringComparison.OrdinalIgnoreCase) && foundWorld.Equals(world, StringComparison.OrdinalIgnoreCase))
                    return WebUtility.HtmlDecode(match.Groups[2].Value);
            }
            return null;
        }
        finally
        {
            lastSearch = DateTime.UtcNow;
            searchLock.Release();
        }
    }

    private async Task<Face?> TryLoadImageAsync(string url, string name)
    {
        try
        {
            return await LoadImageAsync(url, name);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    // Decode on the GPU, read the pixels back, and keep a 128×128 copy.
    private async Task<Face?> LoadImageAsync(string url, string name)
    {
        var bytes = await http.GetByteArrayAsync(url, stop.Token);
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
        public bool Started;
        public bool Urgent;
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
