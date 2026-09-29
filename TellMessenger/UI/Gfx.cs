using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;
using TellMessenger.Game;
using TellMessenger.Model;

namespace TellMessenger.UI;

// Drawing helpers shared by the messenger's windows.
public static class Gfx
{
    public static float Scale { get; set; } = 1f;

    public static float S(float value) => value * Scale;

    public static Vector2 S(float x, float y) => new(x * Scale, y * Scale);

    public static bool IconButton(string id, FontAwesomeIcon icon, string? tooltip = null, Vector4? color = null, float size = 26f)
    {
        var box = new Vector2(S(size), S(size));
        var clicked = ImGui.InvisibleButton(id, box);
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var list = ImGui.GetWindowDrawList();
        if (hovered)
            list.AddRectFilled(min, min + box, Theme.U32(Theme.Current.ContactHover with { W = 0.12f }), S(6));

        var glyph = icon.ToIconString();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var textSize = ImGui.CalcTextSize(glyph);
            var tint = color ?? (hovered ? Theme.Current.Text : Theme.Current.TextSecondary);
            list.AddText(Round(min + (box - textSize) / 2), Theme.U32(tint), glyph);
        }

        if (tooltip != null && hovered)
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    public static void Icon(ImDrawListPtr list, FontAwesomeIcon icon, Vector2 center, Vector4 color)
    {
        var glyph = icon.ToIconString();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var size = ImGui.CalcTextSize(glyph);
            list.AddText(Round(center - size / 2), Theme.U32(color), glyph);
        }
    }

    public static Vector2 IconSize(FontAwesomeIcon icon)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGui.CalcTextSize(icon.ToIconString());
    }

    // Round avatar: the job icon when known, otherwise a glyph for the
    // channel or a generic person.
    public static void Avatar(ImDrawListPtr list, Vector2 min, float size, byte job, ChannelKind kind, LodestonePortraits.Face? portrait = null)
    {
        var max = min + new Vector2(size, size);
        var center = min + new Vector2(size / 2, size / 2);
        if (portrait != null)
        {
            // Lodestone face: their current look, gear and headwear included,
            // drawn at whole pixels from a copy made for exactly this size.
            var pixelMin = Round(min);
            var pixels = (int)MathF.Round(size);
            var face = portrait.At(pixels);
            if (face != null)
            {
                list.AddImageRounded(face.Handle, pixelMin, pixelMin + new Vector2(pixels, pixels), Vector2.Zero, Vector2.One, 0xFFFFFFFF, size * 0.18f);
                return;
            }
        }
        if (kind == ChannelKind.Tell && job > 0)
        {
            var wrap = Services.Textures.GetFromGameIcon(new GameIconLookup(62100u + job)).GetWrapOrDefault();
            if (wrap != null)
            {
                // The whole framed job icon, uncropped.
                list.AddImage(wrap.Handle, Round(min), Round(max));
                return;
            }
        }

        list.AddCircleFilled(center, size / 2, Theme.U32(Theme.Current.Input));
        Icon(list, KindIcon(kind), center, Theme.Current.TextSoft);
    }

    public static FontAwesomeIcon KindIcon(ChannelKind kind) => kind switch
    {
        ChannelKind.Party => FontAwesomeIcon.Users,
        ChannelKind.Alliance => FontAwesomeIcon.UsersCog,
        ChannelKind.FreeCompany => FontAwesomeIcon.Shield,
        ChannelKind.Linkshell => FontAwesomeIcon.Link,
        ChannelKind.CrossLinkshell => FontAwesomeIcon.Globe,
        ChannelKind.NoviceNetwork => FontAwesomeIcon.Leaf,
        ChannelKind.PvPTeam => FontAwesomeIcon.FistRaised,
        _ => FontAwesomeIcon.User,
    };

    public static void StatusDot(ImDrawListPtr list, Vector2 center, float radius, Presence presence)
    {
        var color = PresenceColor(presence);
        if (color == null)
            return;
        list.AddCircleFilled(center, radius + S(1.5f), Theme.U32(Theme.Current.Surface with { W = 1 }));
        list.AddCircleFilled(center, radius, Theme.U32(color.Value));
    }

    public static Vector4? PresenceColor(Presence presence) => presence switch
    {
        Presence.Online => Theme.Current.Online,
        Presence.Away => Theme.Current.Away,
        Presence.Busy => Theme.Current.Busy,
        Presence.Offline => Theme.Current.Offline,
        _ => null,
    };

    public static string PresenceLabel(Presence presence) => presence switch
    {
        Presence.Online => "Online",
        Presence.Away => "Away",
        Presence.Busy => "Busy",
        Presence.Offline => "Offline",
        _ => "",
    };

    // Accent circle with the count knocked out in the surface color.
    public static void Badge(ImDrawListPtr list, Vector2 center, int count, float pulse = 0f, float alpha = 1f)
    {
        if (count <= 0)
            return;
        var text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        var size = ImGui.CalcTextSize(text);
        var height = MathF.Max(size.Y + S(2), S(18));
        var width = MathF.Max(height, size.X + S(10));
        var min = center - new Vector2(width / 2, height / 2);
        if (pulse > 0)
            list.AddRectFilled(min - new Vector2(pulse, pulse), min + new Vector2(width, height) + new Vector2(pulse, pulse),
                Theme.U32(Theme.Current.Accent with { W = 0.35f * (1 - pulse / S(6)) * alpha }), height);
        list.AddRectFilled(min, min + new Vector2(width, height), Theme.U32(Theme.Fade(Theme.Current.Accent, alpha)), height / 2);
        list.AddText(Round(center - size / 2), Theme.U32(Theme.Current.Surface with { W = alpha }), text);
    }

    public static string JobName(byte job)
    {
        if (job == 0 || !Services.Data.GetExcelSheet<ClassJob>().TryGetRow(job, out var row))
            return "";
        var name = row.Name.ExtractText();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }

    // --- Text -----------------------------------------------------------------
    //
    // Glyphs are baked at one size and drawn 1:1, so text must land on whole
    // pixels and must not be drawn at a size other than its font's, or ImGui
    // smooths it and it looks blurry. Secondary text uses its own smaller font.

    private static ImFontPtr small;
    private static bool hasSmall;

    public static void SetSmallFont(ImFontPtr? font)
    {
        hasSmall = font.HasValue;
        if (font.HasValue)
            small = font.Value;
    }

    public static float SmallHeight => hasSmall ? small.FontSize : ImGui.GetFontSize() * 0.86f;

    public static Vector2 SmallSize(string text)
    {
        if (!hasSmall)
            return ImGui.CalcTextSize(text) * 0.86f;
        using (ImRaii.PushFont(small))
            return ImGui.CalcTextSize(text);
    }

    public static void SmallText(ImDrawListPtr list, Vector2 pos, uint color, string text)
    {
        if (hasSmall)
            list.AddText(small, small.FontSize, Round(pos), color, text);
        else
            list.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.86f, Round(pos), color, text);
    }

    public static void Text(ImDrawListPtr list, Vector2 pos, uint color, string text) => list.AddText(Round(pos), color, text);

    public static void Text(ImDrawListPtr list, ImFontPtr font, float size, Vector2 pos, uint color, string text, float wrapWidth) =>
        list.AddText(font, size, Round(pos), color, text, wrapWidth);

    // The text cut to fit `width`, ending in "..." when it doesn't.
    public static string Fit(string text, float width) => Fit(text, width, t => ImGui.CalcTextSize(t).X);

    public static string FitSmall(string text, float width) => Fit(text, width, t => SmallSize(t).X);

    private static string Fit(string text, float width, Func<string, float> measure)
    {
        text = text.ReplaceLineEndings(" ");
        if (width <= 0 || measure(text) <= width)
            return text;
        const string dots = "...";
        var room = width - measure(dots);
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (measure(text[..mid]) <= room)
                low = mid;
            else
                high = mid - 1;
        }
        return text[..low].TrimEnd() + dots;
    }

    public static Vector2 Round(Vector2 pos) => new(MathF.Round(pos.X), MathF.Round(pos.Y));

    // Clips drawing on the current window's draw list until disposed.
    public static IDisposable Clip(Vector2 min, Vector2 max, bool intersect)
    {
        var list = ImGui.GetWindowDrawList();
        list.PushClipRect(min, max, intersect);
        return new PopClip(list);
    }

    private sealed class PopClip(ImDrawListPtr list) : IDisposable
    {
        public void Dispose() => list.PopClipRect();
    }

    // --- Time ---------------------------------------------------------------

    public static DateTime ToDisplay(DateTime utc, Configuration config) => config.UseServerTime ? utc : utc.ToLocalTime();

    public static string Clock(DateTime utc, Configuration config)
    {
        var time = ToDisplay(utc, config);
        return config.Use24HourTime
            ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
            : time.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    // Short age for the contact list: now, 5m, 3h, Yesterday, Mon, Sep 3.
    public static string Age(DateTime utc, Configuration config)
    {
        var age = DateTime.UtcNow - utc;
        if (age < TimeSpan.FromMinutes(1)) return "now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes}m";
        var time = ToDisplay(utc, config);
        var today = ToDisplay(DateTime.UtcNow, config).Date;
        if (time.Date == today) return $"{(int)age.TotalHours}h";
        if (time.Date == today.AddDays(-1)) return "Yesterday";
        if (time.Date > today.AddDays(-7)) return time.ToString("ddd", CultureInfo.InvariantCulture);
        return time.ToString("MMM d", CultureInfo.InvariantCulture);
    }

    public static string DateLabel(DateTime utc, Configuration config)
    {
        var time = ToDisplay(utc, config).Date;
        var today = ToDisplay(DateTime.UtcNow, config).Date;
        if (time == today) return "Today";
        if (time == today.AddDays(-1)) return "Yesterday";
        return time.ToString(time.Year == today.Year ? "dddd, MMMM d" : "MMMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
