using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using TellMessenger.Model;

namespace TellMessenger.UI;

// The messenger: contact list on the left, the open conversation on the
// right, laid out like WhisperMessenger's window.
public sealed partial class MessengerWindow : Window, IDisposable
{
    private readonly Messenger messenger;
    private readonly Configuration config;
    private readonly Action openSettings;

    private IFontHandle? font;
    private IFontHandle? smallFont;
    private ILockedImFont? smallLock;
    private int fontIndex = -1;
    private IDisposable? pushedTheme;
    private IDisposable? pushedFont;
    private IDisposable? pushedAlpha;
    private Vector3 lastPlayerPosition;
    private DateTime lastMoved = DateTime.MinValue;

    // Popup state
    private string newChatText = "";
    private bool openNewChat;
    private string editText = "";
    private string? editKey;
    private bool editingNote;
    private bool openEdit;

    public MessengerWindow(Messenger messenger, Action openSettings)
        : base("Tell Messenger###TellMessengerMain",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse)
    {
        this.messenger = messenger;
        config = messenger.Config;
        this.openSettings = openSettings;
        Size = new Vector2(900, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(480, 320), MaximumSize = new Vector2(4000, 4000) };
        // Where it was last session.
        if (config.WindowPosition is { } position)
        {
            Position = position;
            PositionCondition = ImGuiCond.FirstUseEver;
        }
        if (config.WindowSize is { } size)
            Size = size;
        RespectCloseHotkey = false;
        messenger.OpenRequested += key => Open(key);
        messenger.HideRequested += () => IsOpen = false;
    }

    public void Dispose()
    {
        font?.Dispose();
        smallFont?.Dispose();
    }

    public void Open(string? key = null)
    {
        IsOpen = true;
        BringToFront();
        if (key != null)
            Select(key);
        else if (config.AutoFocusInput)
            focusComposer = true;
    }

    public void ResetPosition()
    {
        Position = new Vector2(200, 150);
        Size = new Vector2(900, 560);
        PositionCondition = ImGuiCond.Always;
        SizeCondition = ImGuiCond.Always;
        resetFrames = 2;
    }

    private int resetFrames;

    // Saves where the window is once you let go after moving or resizing it.
    private void RememberPlacement()
    {
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left) || resetFrames > 0)
            return;
        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize() / ImGuiHelpers.GlobalScale;
        if (config.WindowPosition is { } savedPosition && Vector2.Distance(savedPosition, position) < 1f
            && config.WindowSize is { } savedSize && Vector2.Distance(savedSize, size) < 1f)
            return;
        config.WindowPosition = position;
        config.WindowSize = size;
        config.Save();
    }

    public override void OnOpen() => messenger.WindowVisible = true;

    public override void OnClose() => messenger.WindowVisible = false;

    public override void Update()
    {
        // Track movement for "Dim when moving".
        var player = Services.Objects.LocalPlayer;
        if (player != null)
        {
            if (Vector3.DistanceSquared(player.Position, lastPlayerPosition) > 0.0004f)
                lastMoved = DateTime.UtcNow;
            lastPlayerPosition = player.Position;
        }
    }

    public override void PreDraw()
    {
        if (resetFrames > 0 && --resetFrames == 0)
        {
            PositionCondition = ImGuiCond.FirstUseEver;
            SizeCondition = ImGuiCond.FirstUseEver;
            Position = null;
        }

        Theme.Apply(config);
        Gfx.Scale = config.WindowScale * ImGuiHelpers.GlobalScale;
        EnsureFont();

        // If anything here fails, undo what was pushed: Dalamud doesn't call
        // PostDraw then, and leftover colors would restyle every other window.
        try
        {
            var opacity = IsFocused ? config.OpacityActive : config.OpacityInactive;
            pushedTheme = Theme.Push(opacity);
            pushedFont = font?.Push();
            if (smallFont is { Available: true })
            {
                // The atlas can be mid-rebuild even when the handle says it's
                // ready; skip the small font for that frame instead of failing.
                try
                {
                    smallLock = smallFont.Lock();
                    Gfx.SetSmallFont(smallLock.ImFont);
                }
                catch (InvalidOperationException)
                {
                    smallLock = null;
                }
            }
            var dim = config.DimWhenMoving && !IsFocused && DateTime.UtcNow - lastMoved < TimeSpan.FromMilliseconds(400);
            pushedAlpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, dim ? 0.35f : 1f)
                .Push(ImGuiStyleVar.WindowPadding, Vector2.Zero)
                .Push(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        }
        catch
        {
            PostDraw();
            throw;
        }
    }

    public override void PostDraw()
    {
        pushedAlpha?.Dispose();
        pushedFont?.Dispose();
        pushedTheme?.Dispose();
        pushedAlpha = pushedFont = pushedTheme = null;
        Gfx.SetSmallFont(null);
        smallLock?.Dispose();
        smallLock = null;
    }

    // The game's AXIS font only exists pre-rendered at these point sizes; any
    // other size gets stretched and looks blurry. Text and the smaller
    // secondary text each use the nearest real size.
    private static readonly (float Points, GameFontFamilyAndSize Font)[] AxisSizes =
    [
        (9.6f, GameFontFamilyAndSize.Axis96),
        (12f, GameFontFamilyAndSize.Axis12),
        (14f, GameFontFamilyAndSize.Axis14),
        (18f, GameFontFamilyAndSize.Axis18),
        (36f, GameFontFamilyAndSize.Axis36),
    ];

    private void EnsureFont()
    {
        var wanted = config.FontSize * config.WindowScale;
        var index = 0;
        for (var i = 1; i < AxisSizes.Length; i++)
        {
            if (MathF.Abs(AxisSizes[i].Points - wanted) < MathF.Abs(AxisSizes[index].Points - wanted))
                index = i;
        }
        if (font != null && fontIndex == index)
            return;

        font?.Dispose();
        smallFont?.Dispose();
        fontIndex = index;
        var atlas = Services.PluginInterface.UiBuilder.FontAtlas;
        font = atlas.NewGameFontHandle(new GameFontStyle(AxisSizes[index].Font));
        smallFont = atlas.NewGameFontHandle(new GameFontStyle(AxisSizes[Math.Max(0, index - 1)].Font));
    }

    public override void Draw()
    {
        RememberPlacement();
        DrawTitleBar();

        var body = ImGui.GetContentRegionAvail();
        var contactsWidth = Math.Clamp(config.ContactsWidth * Gfx.Scale, Gfx.S(180), Math.Max(Gfx.S(180), body.X - Gfx.S(340)));

        using (var child = ImRaii.Child("##contacts", new Vector2(contactsWidth, body.Y), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (child)
                DrawContacts();
        }

        ImGui.SameLine();
        DrawSplitter(body.Y);
        ImGui.SameLine();

        using (var child = ImRaii.Child("##conversation", new Vector2(ImGui.GetContentRegionAvail().X, body.Y), false,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (child)
                DrawConversation();
        }

        DrawPlayerMenu();
        DrawItemMenu();
        DrawPluginSubmenu();
        DrawPopups();
        DrawBorder();
    }

    private void DrawTitleBar()
    {
        var height = Gfx.S(34);
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var list = ImGui.GetWindowDrawList();
        list.AddRectFilled(min, min + new Vector2(width, height), Theme.U32(Theme.Current.Chrome with { W = 1 }),
            Gfx.S(8), ImDrawFlags.RoundCornersTop);
        list.AddLine(min + new Vector2(0, height), min + new Vector2(width, height), Theme.U32(Theme.Current.Divider));

        var buttonY = (height - Gfx.S(26)) / 2;
        ImGui.SetCursorScreenPos(min + new Vector2(Gfx.S(6), buttonY));
        if (Gfx.IconButton("##new", FontAwesomeIcon.Edit, "Start a new conversation"))
            openNewChat = true;
        ImGui.SameLine(0, Gfx.S(2));
        if (Gfx.IconButton("##readall", FontAwesomeIcon.CheckDouble, "Mark all as read"))
            messenger.MarkAllRead();

        const string title = "Tell Messenger";
        var titleSize = ImGui.CalcTextSize(title);
        Gfx.Text(list, min + new Vector2((width - titleSize.X) / 2, (height - titleSize.Y) / 2), Theme.U32(Theme.Current.TextEmphasis), title);

        ImGui.SetCursorScreenPos(min + new Vector2(width - Gfx.S(60), buttonY));
        if (Gfx.IconButton("##settings", FontAwesomeIcon.Cog, "Settings"))
            openSettings();
        ImGui.SetCursorScreenPos(min + new Vector2(width - Gfx.S(32), buttonY));
        if (Gfx.IconButton("##close", FontAwesomeIcon.Times, "Close"))
            IsOpen = false;

        ImGui.SetCursorScreenPos(min + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, 0));
    }

    private void DrawSplitter(float height)
    {
        var width = Gfx.S(5);
        ImGui.InvisibleButton("##splitter", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered() || ImGui.IsItemActive();
        var min = ImGui.GetItemRectMin();
        var x = min.X + width / 2;
        ImGui.GetWindowDrawList().AddLine(new Vector2(x, min.Y), new Vector2(x, min.Y + height),
            Theme.U32(hovered ? Theme.Current.Accent with { W = 0.5f } : Theme.Current.Divider), hovered ? 2 : 1);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0))
        {
            config.ContactsWidth = Math.Clamp(config.ContactsWidth + ImGui.GetIO().MouseDelta.X / Gfx.Scale, 180, 600);
        }
        if (ImGui.IsItemDeactivated())
            config.Save();
    }

    private void DrawBorder()
    {
        var min = ImGui.GetWindowPos();
        ImGui.GetForegroundDrawList().AddRect(min, min + ImGui.GetWindowSize(), Theme.U32(Theme.Current.Border), Gfx.S(8));
    }

    // --- Popups ---------------------------------------------------------------

    private void DrawPopups()
    {
        if (openNewChat)
        {
            openNewChat = false;
            newChatText = "";
            ImGui.OpenPopup("Start a new conversation##new");
        }
        if (openEdit)
        {
            openEdit = false;
            ImGui.OpenPopup("##editContact");
        }

        DrawNewChatPopup();
        DrawEditPopup();
    }

    // Dialogs are plain popups, not modals: ImGui draws a modal's screen dim
    // after this window's colors are gone, so it can't be themed away.
    private static void CenterNextPopup() =>
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

    private void DrawNewChatPopup()
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Gfx.S(14, 12)).Push(ImGuiStyleVar.ItemSpacing, Gfx.S(8, 8));
        ImGui.SetNextWindowSize(new Vector2(Gfx.S(340), 0));
        CenterNextPopup();
        using var popup = ImRaii.Popup("Start a new conversation##new", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);
        if (!popup)
            return;

        ImGui.TextUnformatted("Start a new conversation");
        ImGui.TextDisabled("Type a name, or pick a friend.");
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(-1);
        var submit = ImGui.InputTextWithHint("##who", "First Last@World", ref newChatText, 64, ImGuiInputTextFlags.EnterReturnsTrue);

        var owner = messenger.Owner;
        var world = owner?.Split('@').ElementAtOrDefault(1) ?? "";
        var (name, targetWorld) = ParseTarget(newChatText, world);

        using (var list = ImRaii.Child("##friends", new Vector2(-1, Gfx.S(140)), true))
        {
            if (list)
            {
                // Yourself first, for notes.
                var self = owner?.Split('@', 2);
                var search = newChatText.Split('@')[0].Trim();
                if (self is { Length: 2 } && (search.Length == 0
                        || "yourself".Contains(search, StringComparison.OrdinalIgnoreCase)
                        || self[0].Contains(search, StringComparison.OrdinalIgnoreCase)))
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, Theme.Current.Accent))
                        ImGui.TextUnformatted("●");
                    ImGui.SameLine(0, Gfx.S(6));
                    if (ImGui.Selectable("Yourself  ", false))
                    {
                        name = self[0];
                        targetWorld = self[1];
                        submit = true;
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("notes");
                }

                foreach (var friend in messenger.Friends.All
                             .Where(f => newChatText.Length == 0 || f.Name.Contains(newChatText.Split('@')[0], StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(f => f.Presence != Game.Presence.Offline).ThenBy(f => f.Name).Take(50))
                {
                    var dot = Gfx.PresenceColor(friend.Presence) ?? Theme.Current.Offline;
                    using (ImRaii.PushColor(ImGuiCol.Text, dot))
                        ImGui.TextUnformatted("●");
                    ImGui.SameLine(0, Gfx.S(6));
                    if (ImGui.Selectable($"{friend.Name}  ", false))
                    {
                        name = friend.Name;
                        targetWorld = friend.World;
                        submit = true;
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled(friend.World);
                }
                if (messenger.Friends.Count == 0)
                    ImGui.TextDisabled("Open your Friend List once to load it here.");
            }
        }

        var valid = owner != null && name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 2 && targetWorld.Length > 0;
        using (ImRaii.Disabled(!valid))
        {
            if ((ImGui.Button("Start") || submit) && valid)
            {
                var conversation = messenger.StartTell(name, targetWorld);
                Select(conversation.Key);
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
    }

    private static (string Name, string World) ParseTarget(string text, string defaultWorld)
    {
        var parts = text.Split('@', 2);
        var name = string.Join(' ', parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
        var world = parts.Length > 1 ? parts[1].Trim() : defaultWorld;
        if (world.Length > 0)
            world = char.ToUpperInvariant(world[0]) + world[1..].ToLowerInvariant();
        return (name, world);
    }

    private void BeginEdit(Conversation conversation, bool note)
    {
        var prefs = messenger.Store.Prefs(conversation.ContactKey);
        editKey = conversation.ContactKey;
        editingNote = note;
        editText = (note ? prefs.Note : prefs.Nickname) ?? "";
        openEdit = true;
    }

    private void DrawEditPopup()
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Gfx.S(14, 12)).Push(ImGuiStyleVar.ItemSpacing, Gfx.S(8, 8));
        ImGui.SetNextWindowSize(new Vector2(Gfx.S(340), 0));
        CenterNextPopup();
        using var popup = ImRaii.Popup("##editContact", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);
        if (!popup || editKey == null)
            return;

        var who = editKey.Split('@')[0];
        ImGui.TextWrapped(editingNote
            ? $"Note for {who}. Leave empty to remove it."
            : $"Set a nickname for {who}. Leave empty to remove it.");
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(-1);
        var submit = ImGui.InputText("##edit", ref editText, editingNote ? 200 : 32, ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.Button("Save") || submit)
        {
            var prefs = messenger.Store.Prefs(editKey);
            var value = string.IsNullOrWhiteSpace(editText) ? null : editText.Trim();
            if (editingNote) prefs.Note = value;
            else prefs.Nickname = value;
            messenger.Store.MarkDirty();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
    }
}
