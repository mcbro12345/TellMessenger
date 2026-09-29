using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace TellMessenger.UI;

// Color roles for the messenger, ported from WhisperMessenger's theme presets.
public sealed record Palette(
    string Key,
    string Label,
    string Description,
    Vector4 Surface,
    Vector4 SurfaceSecondary,
    Vector4 Chrome,
    Vector4 ContactHover,
    Vector4 ContactSelected,
    Vector4 ContactPinned,
    Vector4 BubbleIn,
    Vector4 BubbleOut,
    Vector4 BubbleSystem,
    Vector4 Input,
    Vector4 Text,
    Vector4 TextSoft,
    Vector4 TextSecondary,
    Vector4 TextEmphasis,
    Vector4 TextSystem,
    Vector4 Timestamp,
    Vector4 Accent,
    Vector4 ToggleOff,
    Vector4 ButtonHover,
    Vector4 Online,
    Vector4 Offline,
    Vector4 Away,
    Vector4 Busy)
{
    public Vector4 Divider => new(1, 1, 1, 0.08f);
    public Vector4 Border => new(1, 1, 1, 0.10f);
    public Vector4 Danger => new(0.85f, 0.25f, 0.25f, 1);
    public Vector4 Mention => Accent with { W = 0.22f };
}

public static class Theme
{
    private static Vector4 C(float r, float g, float b, float a = 1) => new(r, g, b, a);

    private static Vector4 A(Vector4 rgb, float a) => rgb with { W = a };

    private static readonly Vector4 Blue = C(0.12f, 0.72f, 0.96f);
    private static readonly Vector4 Steel = C(0.34f, 0.51f, 0.90f);
    private static readonly Vector4 Amber = C(0.88f, 0.56f, 0.22f);
    private static readonly Vector4 Jade = C(0.047f, 0.824f, 0.616f);
    private static readonly Vector4 Gold = C(1.00f, 0.82f, 0.00f);

    public static readonly Palette[] Presets =
    [
        new("midnight", "Midnight", "Default colors and contrasts.",
            C(0.07f, 0.07f, 0.10f, 0.97f), C(0.10f, 0.10f, 0.14f, 0.60f), C(0.09f, 0.09f, 0.13f, 0.60f),
            C(1, 1, 1, 0.05f), A(Blue, 0.16f), A(Blue, 0.05f),
            C(0.20f, 0.24f, 0.34f, 0.95f), C(0.05f, 0.30f, 0.40f, 0.90f), C(0.14f, 0.12f, 0.10f, 0.65f),
            C(0.17f, 0.19f, 0.26f), C(0.92f, 0.92f, 0.95f), C(0.84f, 0.86f, 0.92f), C(1, 1, 1, 0.55f),
            C(0.98f, 0.99f, 1.0f), C(0.65f, 0.58f, 0.40f), C(1, 1, 1, 0.40f),
            Blue, C(0.32f, 0.33f, 0.40f, 0.95f), C(0.30f, 0.32f, 0.38f),
            C(0.30f, 0.82f, 0.40f), C(0.45f, 0.45f, 0.50f), C(0.90f, 0.72f, 0.20f), C(0.85f, 0.25f, 0.25f)),
        new("void", "The Void", "Dark, flat style with a steel-blue accent.",
            C(0.03f, 0.03f, 0.03f, 0.98f), C(0.06f, 0.06f, 0.07f, 0.80f), C(0.05f, 0.05f, 0.06f, 0.90f),
            C(1, 1, 1, 0.05f), A(Steel, 0.16f), A(Steel, 0.05f),
            C(0.16f, 0.17f, 0.20f, 0.95f), C(0.13f, 0.16f, 0.20f, 0.88f), C(0.10f, 0.10f, 0.12f, 0.78f),
            C(0.09f, 0.10f, 0.12f), C(0.96f, 0.96f, 0.96f), C(0.84f, 0.86f, 0.90f), C(1, 1, 1, 0.55f),
            C(0.99f, 0.99f, 1.0f), C(0.78f, 0.80f, 0.86f), C(1, 1, 1, 0.40f),
            Steel, C(0.22f, 0.23f, 0.27f, 0.96f), C(0.30f, 0.32f, 0.38f),
            C(0.30f, 0.82f, 0.40f), C(0.45f, 0.45f, 0.50f), C(0.90f, 0.72f, 0.20f), C(0.85f, 0.25f, 0.25f)),
        new("thanalan", "Thanalan", "Warm tones with softer contrast.",
            C(0.12f, 0.10f, 0.08f, 0.97f), C(0.18f, 0.14f, 0.10f, 0.72f), C(0.16f, 0.12f, 0.09f, 0.76f),
            C(1, 1, 1, 0.05f), A(Amber, 0.16f), A(Amber, 0.05f),
            C(0.24f, 0.18f, 0.14f, 0.90f), C(0.55f, 0.30f, 0.18f, 0.84f), C(0.24f, 0.18f, 0.12f, 0.72f),
            C(0.31f, 0.22f, 0.16f, 0.98f), C(0.95f, 0.90f, 0.84f), C(0.95f, 0.90f, 0.84f), C(0.82f, 0.72f, 0.62f),
            C(1.0f, 0.97f, 0.92f), C(0.90f, 0.72f, 0.42f), C(0.72f, 0.60f, 0.48f),
            Amber, C(0.30f, 0.24f, 0.20f, 0.95f), C(0.40f, 0.30f, 0.22f),
            C(0.30f, 0.82f, 0.40f), C(0.45f, 0.45f, 0.50f), C(0.90f, 0.72f, 0.20f), C(0.85f, 0.25f, 0.25f)),
        new("gridania", "Gridania", "Near-black with a jade accent.",
            C(0.067f, 0.067f, 0.067f, 0.94f), C(0.09f, 0.09f, 0.09f, 0.82f), C(0.075f, 0.075f, 0.075f, 0.92f),
            C(1, 1, 1, 0.05f), A(Jade, 0.14f), A(Jade, 0.05f),
            C(0.14f, 0.14f, 0.14f, 0.95f), C(0.09f, 0.22f, 0.19f, 0.90f), C(0.10f, 0.10f, 0.10f, 0.78f),
            C(0.10f, 0.10f, 0.10f), C(0.96f, 0.96f, 0.96f), C(0.84f, 0.86f, 0.86f), C(1, 1, 1, 0.55f),
            C(0.99f, 1.0f, 0.99f), C(0.78f, 0.82f, 0.80f), C(1, 1, 1, 0.40f),
            Jade, C(0.20f, 0.20f, 0.20f, 0.96f), C(0.24f, 0.26f, 0.26f),
            C(0.30f, 0.82f, 0.40f), C(0.45f, 0.45f, 0.45f), C(0.90f, 0.72f, 0.20f), C(0.85f, 0.25f, 0.25f)),
        new("eorzea", "Eorzea", "Classic game colors with gold accents.",
            C(0.06f, 0.06f, 0.07f, 0.97f), C(0.10f, 0.09f, 0.08f, 0.60f), C(0.09f, 0.08f, 0.07f, 0.60f),
            C(1, 1, 1, 0.05f), A(Gold, 0.16f), A(Gold, 0.05f),
            C(0.18f, 0.20f, 0.26f, 0.95f), C(0.30f, 0.13f, 0.36f, 0.82f), C(0.14f, 0.12f, 0.08f, 0.65f),
            C(0.16f, 0.14f, 0.10f), C(1, 1, 1), C(0.90f, 0.90f, 0.90f), C(1, 1, 1, 0.55f),
            Gold, C(1.0f, 1.0f, 0.0f), C(1, 1, 1, 0.40f),
            Gold, C(0.25f, 0.25f, 0.28f, 0.95f), C(0.32f, 0.30f, 0.26f),
            C(0.10f, 1.0f, 0.10f), C(0.50f, 0.50f, 0.50f), C(1.0f, 0.50f, 0.25f), C(1.0f, 0.10f, 0.10f)),
    ];

    // Optional bubble colors that override the theme's (in, out).
    public static readonly (string Key, string Label, string Description, Vector4 In, Vector4 Out)[] BubblePresets =
    [
        ("theme", "Use theme colors", "Uses your theme's bubble colors.", default, default),
        ("shadow", "Shadow", "Muted dark tones.", C(0.16f, 0.17f, 0.20f, 0.95f), C(0.13f, 0.16f, 0.20f, 0.88f)),
        ("ember", "Ember", "Warm earthy tones.", C(0.24f, 0.18f, 0.14f, 0.90f), C(0.55f, 0.30f, 0.18f, 0.84f)),
        ("arcane", "Arcane", "Purple arcane-infused tones.", C(0.22f, 0.14f, 0.30f, 0.92f), C(0.36f, 0.18f, 0.48f, 0.85f)),
        ("frost", "Frost", "Cool steel-blue tones.", C(0.18f, 0.22f, 0.30f, 0.92f), C(0.28f, 0.42f, 0.58f, 0.82f)),
        ("aether", "Aether", "Eerie aetherial green tones.", C(0.10f, 0.22f, 0.12f, 0.90f), C(0.14f, 0.38f, 0.16f, 0.82f)),
    ];

    public static readonly (string Key, string Label, Vector4 Color)[] TextColors =
    [
        ("default", "Default", default),
        ("gold", "Gold", C(1.0f, 0.82f, 0.30f)),
        ("blue", "Blue", C(0.55f, 0.80f, 1.0f)),
        ("green", "Green", C(0.55f, 0.95f, 0.55f)),
        ("purple", "Purple", C(0.80f, 0.62f, 1.0f)),
        ("rose", "Rose", C(1.0f, 0.62f, 0.75f)),
    ];

    public static Palette Current { get; private set; } = Presets[0];
    public static Vector4 BubbleIn { get; private set; }
    public static Vector4 BubbleOut { get; private set; }
    public static Vector4 SentText { get; private set; }

    public static void Apply(Configuration config)
    {
        var preset = Array.Find(Presets, p => p.Key == config.Theme) ?? Presets[0];
        // Text blended with see-through white reads as soft on top of the game,
        // so every text color is made solid against the window's surface.
        Current = preset with
        {
            Text = Solid(preset.Text, preset.Surface),
            TextSoft = Solid(preset.TextSoft, preset.Surface),
            TextSecondary = Solid(preset.TextSecondary, preset.Surface),
            TextEmphasis = Solid(preset.TextEmphasis, preset.Surface),
            TextSystem = Solid(preset.TextSystem, preset.Surface),
            Timestamp = Solid(preset.Timestamp, preset.Surface),
        };
        var bubbles = Array.Find(BubblePresets, b => b.Key == config.BubblePreset);
        var useTheme = bubbles.Key is null or "theme";
        BubbleIn = useTheme ? Current.BubbleIn : bubbles.In;
        BubbleOut = useTheme ? Current.BubbleOut : bubbles.Out;
        var text = Array.Find(TextColors, t => t.Key == config.SentTextColor);
        SentText = text.Key is null or "default" ? Current.TextEmphasis : text.Color;
    }

    private static Vector4 Solid(Vector4 color, Vector4 background) =>
        new(background.X + (color.X - background.X) * color.W,
            background.Y + (color.Y - background.Y) * color.W,
            background.Z + (color.Z - background.Z) * color.W,
            1f);

    public static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    public static Vector4 Fade(Vector4 color, float alpha) => color with { W = color.W * alpha };

    // Pushes the palette onto ImGui's widgets for the messenger's windows.
    public static IDisposable Push(float opacity)
    {
        var p = Current;
        var colors = new List<(ImGuiCol, Vector4)>
        {
            (ImGuiCol.WindowBg, Fade(p.Surface, opacity)),
            (ImGuiCol.ChildBg, Vector4.Zero),
            (ImGuiCol.PopupBg, p.Surface with { W = 0.98f }),
            (ImGuiCol.Border, p.Border),
            (ImGuiCol.FrameBg, p.Input),
            (ImGuiCol.FrameBgHovered, p.ButtonHover),
            (ImGuiCol.FrameBgActive, p.ButtonHover),
            (ImGuiCol.Button, Vector4.Zero),
            (ImGuiCol.ButtonHovered, p.ContactHover with { W = 0.12f }),
            (ImGuiCol.ButtonActive, p.ContactSelected),
            (ImGuiCol.Header, p.ContactSelected),
            (ImGuiCol.HeaderHovered, p.ContactHover with { W = 0.10f }),
            (ImGuiCol.HeaderActive, p.ContactSelected),
            (ImGuiCol.Text, p.Text),
            (ImGuiCol.TextDisabled, p.TextSecondary),
            (ImGuiCol.CheckMark, p.Accent),
            (ImGuiCol.SliderGrab, p.Accent),
            (ImGuiCol.SliderGrabActive, p.Accent),
            (ImGuiCol.Separator, p.Divider),
            (ImGuiCol.ScrollbarBg, Vector4.Zero),
            (ImGuiCol.ScrollbarGrab, new Vector4(1, 1, 1, 0.14f)),
            (ImGuiCol.ScrollbarGrabHovered, new Vector4(1, 1, 1, 0.32f)),
            (ImGuiCol.ScrollbarGrabActive, new Vector4(1, 1, 1, 0.40f)),
            (ImGuiCol.Tab, Vector4.Zero),
            (ImGuiCol.TabHovered, p.ContactHover with { W = 0.12f }),
            (ImGuiCol.TabActive, p.ContactSelected),
            (ImGuiCol.TitleBg, p.Chrome with { W = 1 }),
            (ImGuiCol.TitleBgActive, p.Chrome with { W = 1 }),
            (ImGuiCol.TextSelectedBg, p.Accent with { W = 0.35f }),
            (ImGuiCol.NavHighlight, p.Accent),
            // No wash over the window behind pop-ups.
            (ImGuiCol.ModalWindowDimBg, Vector4.Zero),
        };

        var color = ImRaii.PushColor(ImGuiCol.WindowBg, colors[0].Item2);
        foreach (var (col, value) in colors.GetRange(1, colors.Count - 1))
            color.Push(col, value);

        var style = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 8f)
            .Push(ImGuiStyleVar.FrameRounding, 6f)
            .Push(ImGuiStyleVar.PopupRounding, 6f)
            .Push(ImGuiStyleVar.ChildRounding, 6f)
            .Push(ImGuiStyleVar.ScrollbarSize, 6f)
            .Push(ImGuiStyleVar.ScrollbarRounding, 3f)
            .Push(ImGuiStyleVar.GrabRounding, 6f)
            .Push(ImGuiStyleVar.WindowBorderSize, 1f)
            .Push(ImGuiStyleVar.PopupBorderSize, 1f);

        return new Both(color, style);
    }

    private sealed class Both(IDisposable a, IDisposable b) : IDisposable
    {
        public void Dispose()
        {
            b.Dispose();
            a.Dispose();
        }
    }
}
