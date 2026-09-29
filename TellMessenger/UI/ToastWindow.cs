using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using TellMessenger.Model;

namespace TellMessenger.UI;

// New-tell pop-ups in the top-right corner of the screen. Click one to open
// the conversation; the × or a right-click dismisses it.
public sealed class ToastWindow : Window
{
    private const float Width = 320f;
    private const float FadeSeconds = 0.4f;

    private readonly Messenger messenger;
    private readonly Configuration config;

    public ToastWindow(Messenger messenger)
        : base("##TellMessengerToasts",
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.messenger = messenger;
        config = messenger.Config;
        IsOpen = true;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = false;
        PositionCondition = ImGuiCond.Always;
    }

    public override bool DrawConditions() =>
        messenger.Toasts.Count > 0 && Services.ClientState.IsLoggedIn && !Services.ClientState.IsGPosing;

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGuiHelpers.GlobalScale;
        // Below the server info bar, in from the right edge.
        Position = viewport.Pos + new Vector2(viewport.Size.X - (Width + 16) * scale, 40 * scale);
        Theme.Apply(config);
    }

    public override void Draw()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero).Push(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        var scale = ImGuiHelpers.GlobalScale;
        var toasts = messenger.Toasts;
        for (var i = 0; i < toasts.Count; i++)
        {
            if (i > 0)
                ImGui.Dummy(new Vector2(0, 8 * scale));
            if (DrawToast(toasts[i], scale))
                break; // the list changed
        }
    }

    // True when the toast was clicked or dismissed.
    private bool DrawToast(Toast toast, float scale)
    {
        var p = Theme.Current;
        var conversation = messenger.Store.Get(toast.ConversationKey);
        var lineHeight = ImGui.GetTextLineHeight();
        var avatarSize = 40 * scale;
        var pad = 12 * scale;
        var size = new Vector2(Width * scale, MathF.Max(avatarSize, lineHeight * 2 + 4 * scale) + pad * 2);

        // Fade in, and out at the end of its time.
        var age = (float)(DateTime.UtcNow - toast.Time).TotalSeconds;
        var alpha = MathF.Min(1f, age / FadeSeconds);
        if (config.ToastSeconds > 0)
            alpha = MathF.Min(alpha, (config.ToastSeconds - age) / FadeSeconds);
        alpha = Math.Clamp(alpha, 0f, 1f);

        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        var closeCenter = new Vector2(max.X - pad, min.Y + pad);
        var closeRadius = 8 * scale;

        ImGui.InvisibleButton($"##toast{toast.Id}", size);
        var hovered = ImGui.IsItemHovered();
        var overClose = hovered && Vector2.Distance(ImGui.GetMousePos(), closeCenter) <= closeRadius + 2 * scale;
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var dismissed = ImGui.IsItemClicked(ImGuiMouseButton.Right) || clicked && overClose;
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var list = ImGui.GetWindowDrawList();
        var rounding = 10 * scale;
        list.AddRectFilled(min, max, Theme.U32(Theme.Fade((hovered ? p.SurfaceSecondary : p.Surface) with { W = 0.97f }, alpha)), rounding);
        list.AddRect(min, max, Theme.U32(Theme.Fade(p.Accent with { W = 0.45f }, alpha)), rounding);

        // Avatar: portrait, job icon, or a glyph.
        var avatarMin = min + new Vector2(pad, (size.Y - avatarSize) / 2);
        if (conversation != null)
            Gfx.Avatar(list, avatarMin, avatarSize, messenger.JobOf(conversation), conversation.Kind, messenger.PortraitOf(conversation), alpha);

        var textX = avatarMin.X + avatarSize + 10 * scale;
        var textRight = closeCenter.X - closeRadius - 6 * scale;
        var textY = min.Y + (size.Y - lineHeight * 2 - 4 * scale) / 2;
        using (Gfx.Clip(new Vector2(textX, min.Y), new Vector2(textRight, max.Y), true))
        {
            Gfx.Text(list, new Vector2(textX, textY), Theme.U32(Theme.Fade(p.TextEmphasis, alpha)), Gfx.Fit(toast.Sender, textRight - textX));
            Gfx.Text(list, new Vector2(textX, textY + lineHeight + 4 * scale), Theme.U32(Theme.Fade(p.Text, alpha)), Gfx.Fit(toast.Text, textRight - textX));
        }

        // Close ×
        var closeColor = overClose ? p.Text : p.TextSecondary;
        var arm = 4 * scale;
        list.AddLine(closeCenter - new Vector2(arm, arm), closeCenter + new Vector2(arm, arm), Theme.U32(Theme.Fade(closeColor, alpha)), 1.5f * scale);
        list.AddLine(closeCenter + new Vector2(-arm, arm), closeCenter + new Vector2(arm, -arm), Theme.U32(Theme.Fade(closeColor, alpha)), 1.5f * scale);

        if (dismissed)
        {
            messenger.DismissToast(toast);
            return true;
        }
        if (clicked)
        {
            messenger.DismissToast(toast);
            messenger.DismissPreview();
            messenger.OpenConversation(toast.ConversationKey);
            return true;
        }
        return false;
    }
}
