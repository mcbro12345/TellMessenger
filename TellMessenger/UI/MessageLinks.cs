using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using TellMessenger.Model;

namespace TellMessenger.UI;

// Links inside a message: map flags, items, players, web addresses, and links
// other plugins add to chat (like "Teleport to…"). Clicking one does what it
// does in the game's chat; plugin links go to the plugin that made them.
public static partial class MessageLinks
{
    // A clickable stretch of text.
    public sealed class Link
    {
        public Payload? Head;               // the payload that starts it
        public List<Payload> Span = [];     // everything up to its terminator
        public string? Url;                 // for web addresses in plain text
    }

    public readonly record struct Run(string Text, Link? Link);

    // One laid-out piece of text, relative to the text's top-left corner.
    public readonly record struct Piece(Vector2 Pos, string Text, Link? Link, float Width);

    // Links worth keeping the raw message for.
    private static bool IsLinkStart(Payload payload) => payload is MapLinkPayload or ItemPayload or DalamudLinkPayload
        or PlayerPayload or QuestPayload or StatusPayload or PartyFinderPayload;

    // The raw message, only when it has something clickable.
    public static string? RawIfLinked(SeString message) =>
        message.Payloads.Any(IsLinkStart) ? Convert.ToBase64String(message.Encode()) : null;

    private static readonly Dictionary<string, List<Run>> Cache = new();

    public static List<Run> RunsFor(ChatMessage message)
    {
        var key = message.Raw ?? message.Text;
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        if (Cache.Count > 500)
            Cache.Clear();

        var runs = new List<Run>();
        if (message.Raw != null)
        {
            try
            {
                Parse(SeString.Parse(Convert.FromBase64String(message.Raw)), runs);
            }
            catch
            {
                runs.Clear();
            }
        }
        if (runs.Count == 0)
            AddText(runs, message.Text, null);
        return Cache[key] = runs;
    }

    private static void Parse(SeString message, List<Run> runs)
    {
        Link? current = null;
        foreach (var payload in message.Payloads)
        {
            if (IsLinkStart(payload))
            {
                current = new Link { Head = payload };
                current.Span.Add(payload);
                continue;
            }
            if (payload is RawPayload raw && raw.Data.SequenceEqual(RawPayload.LinkTerminator.Data))
            {
                current?.Span.Add(payload);
                current = null;
                continue;
            }
            current?.Span.Add(payload);
            if (payload is ITextProvider text && text.Text.Length > 0)
                AddText(runs, text.Text, current);
        }
    }

    // Plain text, with any web addresses in it made clickable.
    private static void AddText(List<Run> runs, string text, Link? link)
    {
        if (link != null)
        {
            runs.Add(new Run(text, link));
            return;
        }
        var last = 0;
        foreach (Match match in UrlPattern().Matches(text))
        {
            if (match.Index > last)
                runs.Add(new Run(text[last..match.Index], null));
            runs.Add(new Run(match.Value, new Link { Url = match.Value }));
            last = match.Index + match.Length;
        }
        if (last < text.Length)
            runs.Add(new Run(text[last..], null));
    }

    [GeneratedRegex(@"https?://[^\s<>""]+[^\s<>"".,;:!?)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // Word-wraps the runs to `width` with the current font.
    public static List<Piece> Layout(List<Run> runs, float width, out Vector2 size)
    {
        var pieces = new List<Piece>();
        var lineHeight = ImGui.GetTextLineHeight();
        var x = 0f;
        var y = 0f;
        var widest = 0f;

        foreach (var run in runs)
        {
            foreach (var token in Tokens(run.Text))
            {
                if (token == "\n")
                {
                    widest = MathF.Max(widest, x);
                    x = 0;
                    y += lineHeight;
                    continue;
                }
                var tokenWidth = ImGui.CalcTextSize(token).X;
                var isSpace = token.Trim().Length == 0;
                if (x > 0 && x + tokenWidth > width && !isSpace)
                {
                    widest = MathF.Max(widest, x);
                    x = 0;
                    y += lineHeight;
                }
                if (x == 0 && isSpace)
                    continue; // no leading spaces on a wrapped line

                // A word longer than the whole line is broken by characters.
                if (tokenWidth > width)
                {
                    var start = 0;
                    for (var i = 1; i <= token.Length; i++)
                    {
                        var part = token[start..i];
                        var partWidth = ImGui.CalcTextSize(part).X;
                        if (x + partWidth > width && i - start > 1)
                        {
                            var fit = token[start..(i - 1)];
                            var fitWidth = ImGui.CalcTextSize(fit).X;
                            pieces.Add(new Piece(new Vector2(x, y), fit, run.Link, fitWidth));
                            widest = MathF.Max(widest, x + fitWidth);
                            x = 0;
                            y += lineHeight;
                            start = i - 1;
                        }
                    }
                    var rest = token[start..];
                    var restWidth = ImGui.CalcTextSize(rest).X;
                    pieces.Add(new Piece(new Vector2(x, y), rest, run.Link, restWidth));
                    x += restWidth;
                    continue;
                }

                pieces.Add(new Piece(new Vector2(x, y), token, run.Link, tokenWidth));
                x += tokenWidth;
            }
        }
        widest = MathF.Max(widest, x);
        size = new Vector2(widest, y + lineHeight);
        return pieces;
    }

    // Words, runs of spaces, and line breaks.
    private static IEnumerable<string> Tokens(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n' || c == '\r')
            {
                if (i > start)
                    yield return text[start..i];
                if (c == '\n')
                    yield return "\n";
                start = i + 1;
                continue;
            }
            if (i > start && char.IsWhiteSpace(c) != char.IsWhiteSpace(text[i - 1]))
            {
                yield return text[start..i];
                start = i;
            }
        }
        if (start < text.Length)
            yield return text[start..];
    }

    // Draws the pieces; returns the link under the mouse, if any.
    public static Link? Draw(ImDrawListPtr list, ImFontPtr font, float fontSize, Vector2 origin, List<Piece> pieces,
        uint color, uint linkColor, bool mouseInside)
    {
        var mouse = ImGui.GetMousePos();
        var lineHeight = ImGui.GetTextLineHeight();
        Link? hovered = null;
        if (mouseInside)
        {
            foreach (var piece in pieces)
            {
                if (piece.Link == null)
                    continue;
                var min = origin + piece.Pos;
                if (mouse.X >= min.X && mouse.X < min.X + piece.Width && mouse.Y >= min.Y && mouse.Y < min.Y + lineHeight)
                {
                    hovered = piece.Link;
                    break;
                }
            }
        }

        // A soft rounded box behind each link, one per line it covers, like
        // links in the game's chat; brighter while hovered.
        var linkTint = ImGui.ColorConvertU32ToFloat4(linkColor);
        var pad = new Vector2(3, 1) * ImGuiHelpers.GlobalScale;
        for (var i = 0; i < pieces.Count; i++)
        {
            var link = pieces[i].Link;
            if (link == null)
                continue;
            var start = pieces[i];
            var end = start;
            while (i + 1 < pieces.Count && pieces[i + 1].Link == link && pieces[i + 1].Pos.Y == start.Pos.Y)
                end = pieces[++i];
            var boxMin = Gfx.Round(origin + start.Pos) - pad;
            var boxMax = Gfx.Round(origin + end.Pos + new Vector2(end.Width, lineHeight)) + pad;
            var fill = linkTint with { W = link == hovered ? 0.34f : 0.16f };
            list.AddRectFilled(boxMin, boxMax, ImGui.ColorConvertFloat4ToU32(fill), 4 * ImGuiHelpers.GlobalScale);
        }

        foreach (var piece in pieces)
        {
            var pos = Gfx.Round(origin + piece.Pos);
            list.AddText(font, fontSize, pos, piece.Link != null ? linkColor : color, piece.Text);
        }
        return hovered;
    }


    // What clicking does, per link type (the same as in Chat 2).
    public static unsafe void Activate(Link link, Action<string, string> openTell)
    {
        try
        {
            switch (link.Head)
            {
                case null when link.Url != null:
                    Util.OpenLink(link.Url);
                    break;
                case MapLinkPayload map:
                    Services.GameGui.OpenMapWithMapLink(map);
                    break;
                case QuestPayload quest:
                    // Quest IDs look like "SubFst000_00123".
                    var parts = quest.Quest.ValueNullable?.Id.ExtractText().Split('_');
                    if (parts is { Length: 2 } && uint.TryParse(parts[1], out var questId))
                        AgentQuestJournal.Instance()->OpenForQuest(questId, 1);
                    break;
                case PartyFinderPayload listing when listing.LinkType == PartyFinderPayload.PartyFinderLinkType.NotSpecified:
                    AgentLookingForGroup.Instance()->OpenListing(listing.ListingId);
                    break;
                case DalamudLinkPayload plugin:
                    if (Services.Chat.RegisteredLinkHandlers.TryGetValue((plugin.Plugin, plugin.CommandId), out var handler))
                    {
                        var span = new SeString(link.Span);
                        // Plugins expect to run on the game's thread, as they do from chat.
                        Services.Framework.RunOnTick(() => handler(plugin.CommandId, span));
                    }
                    else
                    {
                        Services.Chat.PrintError($"That link's plugin ({plugin.Plugin}) isn't loaded.", "Tell Messenger");
                    }
                    break;
                case PlayerPayload player when player.World.IsValid:
                    openTell(player.PlayerName, player.World.Value.Name.ExtractText());
                    break;
            }
        }
        catch (Exception e)
        {
            Services.Log.Error(e, "Could not open that link");
        }
    }

    // Drawn by the plugin like Chat 2's, since the game's own tooltip would
    // show behind the messenger window.
    public static void Tooltip(Link link)
    {
        ImGui.SetNextWindowSize(new Vector2(350f * ImGuiHelpers.GlobalScale, -1f));
        using var tooltip = ImRaii.Tooltip();
        using var wrap = ImRaii.TextWrapPos(0f);
        switch (link.Head)
        {
            case null when link.Url != null:
                ImGui.TextUnformatted(Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) ? $"Opens {uri.Authority}" : link.Url);
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Only open links from people you trust.");
                break;
            case ItemPayload item:
                ItemTooltip(item);
                break;
            case StatusPayload status when status.Status.ValueNullable is { } row:
                IconAndName(row.Icon, false, row.Name.ExtractText());
                ImGui.Separator();
                ImGui.TextUnformatted(row.Description.ExtractText());
                break;
            case MapLinkPayload map:
                ImGui.TextUnformatted($"{map.PlaceName} {map.CoordinateString}");
                ImGui.TextDisabled("Click to open the map");
                break;
            case DalamudLinkPayload plugin:
                ImGui.TextUnformatted(new SeString(link.Span).TextValue.Trim());
                ImGui.TextDisabled(Services.Chat.RegisteredLinkHandlers.ContainsKey((plugin.Plugin, plugin.CommandId))
                    ? $"Click to use · from {plugin.Plugin}"
                    : $"From {plugin.Plugin}, which isn't loaded");
                break;
            case PlayerPayload player:
                ImGui.TextUnformatted(player.DisplayedName);
                ImGui.TextDisabled("Click to message them");
                break;
            default:
                ImGui.TextUnformatted(new SeString(link.Span).TextValue.Trim());
                break;
        }
    }

    private static void ItemTooltip(ItemPayload payload)
    {
        if (payload.Kind == ItemKind.EventItem)
        {
            if (!Services.Data.GetExcelSheet<EventItem>().TryGetRow(payload.RawItemId, out var eventItem))
                return;
            IconAndName(eventItem.Icon, false, eventItem.Name.ExtractText());
            ImGui.Separator();
            if (Services.Data.GetExcelSheet<EventItemHelp>().TryGetRow(payload.RawItemId, out var help))
                ImGui.TextUnformatted(help.Description.ExtractText());
            return;
        }

        if (!payload.Item.TryGetValue(out Item item))
            return;
        IconAndName(item.Icon, payload.IsHQ, item.Name.ExtractText());
        ImGui.Separator();
        ImGui.TextUnformatted(item.Description.ExtractText());
    }

    // A 32px icon with the name beside it, as Chat 2 lays it out.
    private static void IconAndName(uint iconId, bool hq, string name)
    {
        if (Services.Textures.GetFromGameIcon(new GameIconLookup(iconId, hq)).GetWrapOrDefault() is { } icon)
        {
            var cursor = ImGui.GetCursorPos();
            var ratio = icon.Size.X / icon.Size.Y;
            var size = ImGuiHelpers.ScaledVector2(MathF.Min(32, 32 * ratio), MathF.Min(32, 32 / ratio));
            ImGui.Image(icon.Handle, size);
            ImGui.SameLine();
            ImGui.SetCursorPos(cursor + new Vector2(size.X + 4, size.Y - ImGui.GetTextLineHeightWithSpacing()));
        }
        ImGui.TextUnformatted(name);
    }
}
