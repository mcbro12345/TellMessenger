using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace TellMessenger.UI;

// The floating round button: opens and closes the messenger, shows an unread
// badge and a short preview of the newest message. Drag it to move it.
public sealed class ToggleWidget : Window
{
    private readonly Messenger messenger;
    private readonly Configuration config;
    private readonly Action toggle;
    private readonly Func<bool> chatHidden;
    private bool dragged;

    // The preview fades in and out over FadeSeconds; the last one shown is
    // kept while it fades out.
    private const float FadeSeconds = 0.18f;
    private IncomingPreview? shownPreview;
    private float previewFade;
    // While Chat 2 is hidden the button is too, except while a preview shows.
    private float buttonFade = 1f;

    // Where the button sits inside the window; the window moves so the button
    // stays put when a preview opens to its left or above it.
    private Vector2 buttonOffset;

    public ToggleWidget(Messenger messenger, Action toggle, Func<bool> chatHidden)
        : base("##TellMessengerWidget",
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.messenger = messenger;
        config = messenger.Config;
        this.chatHidden = chatHidden;
        this.toggle = toggle;
        IsOpen = true;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = false;
        PositionCondition = ImGuiCond.Always;
    }

    public override bool DrawConditions() =>
        config.IconMode != IconMode.ServerInfoBar && Services.ClientState.IsLoggedIn && !Services.ClientState.IsGPosing
        // Hidden along with Chat 2's chat window, unless a new message is
        // showing its preview (or either is still fading out).
        && (!chatHidden() || WantsPreview() || shownPreview != null || buttonFade > 0f);

    private bool WantsPreview() => config.ShowWidgetPreview && !messenger.WindowVisible && messenger.LatestPreview != null;

    public override void PreDraw()
    {
        Position = config.WidgetPosition - buttonOffset;
        Theme.Apply(config);
    }

    public override void Draw()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero).Push(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        var p = Theme.Current;
        var scale = ImGuiHelpers.GlobalScale;
        var size = config.IconSize * scale;
        var unread = messenger.UnreadCount(ContactsTab.Whispers) + messenger.UnreadCount(ContactsTab.Groups);
        var wanted = WantsPreview() ? messenger.LatestPreview : null;
        if (wanted != null)
            shownPreview = wanted;
        var step = ImGui.GetIO().DeltaTime / FadeSeconds;
        previewFade = Math.Clamp(previewFade + (wanted != null ? step : -step), 0f, 1f);
        if (wanted == null && previewFade <= 0f)
            shownPreview = null;
        var preview = shownPreview;
        var previewAlpha = previewFade * previewFade * (3 - 2 * previewFade); // smoothstep
        var previewLive = wanted != null;
        var showButton = !chatHidden() || wanted != null || previewFade > 0f;
        buttonFade = Math.Clamp(buttonFade + (showButton ? step : -step), 0f, 1f);
        var buttonAlpha = buttonFade * buttonFade * (3 - 2 * buttonFade);

        // Leave room for the preview on the chosen side.
        var previewSize = preview != null ? PreviewSize(preview, scale) : Vector2.Zero;
        var origin = ImGui.GetCursorScreenPos();
        // A margin around the button so the unread badge on its corner isn't
        // cut off by the window edge.
        var margin = 12 * scale;
        buttonOffset = new Vector2(margin, margin) + (preview == null ? Vector2.Zero : config.PreviewSide switch
        {
            PreviewSide.Left => new Vector2(previewSize.X + 8 * scale, 0),
            PreviewSide.Above => new Vector2(0, previewSize.Y + 8 * scale),
            _ => Vector2.Zero,
        });

        var buttonMin = origin + buttonOffset;
        ImGui.SetCursorScreenPos(buttonMin);
        ImGui.InvisibleButton("##toggle", new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        HandleDrag();

        var list = ImGui.GetWindowDrawList();
        var center = buttonMin + new Vector2(size / 2, size / 2);
        var idle = unread == 0 && config.DesaturateIdle;
        var alpha = (hovered ? 1f : config.WidgetIdleOpacity) * buttonAlpha;
        // Hover lights it up: a lighter face, a solid accent ring and a brighter icon.
        // While idle it stays grey, just a lighter grey on hover.
        var ring = idle
            ? new Vector4(0.5f, 0.5f, 0.5f, hovered ? 0.9f : 0.55f)
            : p.Accent with { W = hovered ? 1f : 0.55f };
        var face = hovered ? new Vector4(0.24f, 0.27f, 0.34f, 1) : new Vector4(0.15f, 0.17f, 0.22f, 1);
        var iconColor = idle ? new Vector4(0.6f, 0.6f, 0.6f, 1) : p.Accent;
        if (hovered)
            iconColor = Vector4.Min(iconColor + new Vector4(0.15f, 0.15f, 0.15f, 0), Vector4.One);
        list.AddCircleFilled(center, size / 2, Theme.U32(Theme.Fade(face, alpha)));
        list.AddCircle(center, size / 2 - scale, Theme.U32(Theme.Fade(ring, alpha)), 0, (hovered ? 2.5f : 2f) * scale);
        Gfx.Icon(list, FontAwesomeIcon.CommentDots, center, Theme.Fade(iconColor, alpha));

        if (config.ShowBadge && unread > 0)
        {
            var pulse = 0f;
            if (config.BadgePulse)
            {
                var since = (float)(DateTime.UtcNow - messenger.LastIncoming).TotalSeconds;
                if (since < 4)
                    pulse = (since * 1.5f % 1f) * 6 * scale;
            }
            Gfx.Badge(list, buttonMin + new Vector2(size - 4 * scale, 5 * scale), unread, pulse, buttonAlpha);
        }

        if (preview != null)
        {
            var previewMin = config.PreviewSide switch
            {
                PreviewSide.Left => new Vector2(origin.X + margin, buttonMin.Y + (size - previewSize.Y) / 2),
                PreviewSide.Above => new Vector2(buttonMin.X, origin.Y + margin),
                PreviewSide.Below => buttonMin + new Vector2(0, size + 8 * scale),
                _ => buttonMin + new Vector2(size + 8 * scale, (size - previewSize.Y) / 2),
            };
            DrawPreview(preview, previewMin, previewSize, scale, previewAlpha, previewLive);
        }

        // Grow the window past the button's right and bottom edges too.
        ImGui.SetCursorScreenPos(buttonMin + new Vector2(size + margin - 1, size + margin - 1));
        ImGui.Dummy(Vector2.One);
    }

    private void HandleDrag()
    {
        if (ImGui.IsItemActivated())
            dragged = false;

        if (!config.LockIcon && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 3))
        {
            dragged = true;
            config.WidgetPosition += ImGui.GetIO().MouseDelta;
            Position = config.WidgetPosition - buttonOffset;
        }

        if (ImGui.IsItemDeactivated())
        {
            if (dragged)
                config.Save();
            else if (ImGui.IsItemHovered())
                toggle();
        }
    }

    private static float AvatarSize(float height, float scale) => MathF.Round(height - 12 * scale);

    private static Vector2 PreviewSize(IncomingPreview preview, float scale)
    {
        var name = ImGui.CalcTextSize(preview.Sender);
        var text = ImGui.CalcTextSize(Messenger.Snippet(preview.Text, 48));
        var height = name.Y + text.Y + 16 * scale;
        // Room for their picture on the left.
        return new Vector2(MathF.Max(name.X, text.X) + 24 * scale + AvatarSize(height, scale) + 10 * scale, height);
    }

    // `live` is false while it fades out, when it no longer takes clicks.
    private void DrawPreview(IncomingPreview preview, Vector2 min, Vector2 size, float scale, float alpha, bool live)
    {
        var p = Theme.Current;
        ImGui.SetCursorScreenPos(min);
        var clicked = ImGui.InvisibleButton("##preview", size);
        if (live && clicked)
        {
            messenger.DismissPreview();
            messenger.OpenConversation(preview.ConversationKey);
        }
        if (live && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            messenger.DismissPreview();

        var list = ImGui.GetWindowDrawList();
        list.AddRectFilled(min, min + size, Theme.U32(Theme.Fade(p.Surface with { W = 0.96f }, alpha)), 10 * scale);
        list.AddRect(min, min + size, Theme.U32(Theme.Fade(p.Accent with { W = 0.4f }, alpha)), 10 * scale);
        var lineHeight = ImGui.GetTextLineHeight();
        var avatarSize = AvatarSize(size.Y, scale);
        var avatarMin = min + new Vector2(8 * scale, (size.Y - avatarSize) / 2);
        if (messenger.Store.Get(preview.ConversationKey) is { } conversation)
            Gfx.Avatar(list, avatarMin, avatarSize, messenger.JobOf(conversation), conversation.Kind, messenger.PortraitOf(conversation), alpha);
        var textX = avatarMin.X + avatarSize + 10 * scale;
        Gfx.Text(list, new Vector2(textX, min.Y + 8 * scale), Theme.U32(Theme.Fade(p.Accent, alpha)), preview.Sender);
        Gfx.Text(list, new Vector2(textX, min.Y + 8 * scale + lineHeight), Theme.U32(Theme.Fade(p.Text, alpha)), Messenger.Snippet(preview.Text, 48));
        if (live && ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to open, right-click to dismiss");
    }
}
