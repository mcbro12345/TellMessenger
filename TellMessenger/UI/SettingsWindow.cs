using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using TellMessenger.Game;

namespace TellMessenger.UI;

// Options, split into the same pages as WhisperMessenger's settings.
public sealed class SettingsWindow : Window
{
    private readonly Messenger messenger;
    private readonly Configuration config;
    private readonly Action resetWindow;
    private IDisposable? pushedTheme;
    private Keybind? capturing;

    public SettingsWindow(Messenger messenger, Action resetWindow)
        : base("Tell Messenger Settings###TellMessengerSettings")
    {
        this.messenger = messenger;
        config = messenger.Config;
        this.resetWindow = resetWindow;
        Size = new Vector2(560, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(440, 360), MaximumSize = new Vector2(1200, 1400) };
    }

    public override void PreDraw()
    {
        Theme.Apply(config);
        pushedTheme = Theme.Push(1f);
    }

    public override void PostDraw()
    {
        pushedTheme?.Dispose();
        pushedTheme = null;
    }

    public override void Draw()
    {
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6)).Push(ImGuiStyleVar.FramePadding, new Vector2(8, 4));
        using var tabs = ImRaii.TabBar("##settingsTabs");
        if (!tabs)
            return;

        Page("General", "Configure message storage, retention and time display.", General);
        Page("Appearance", "Customize theme presets, fonts and window opacity.", Appearance);
        Page("Behavior", "Control how the messenger window behaves.", Behavior);
        Page("Notifications", "Configure alerts for incoming messages.", Notifications);
        Page("Icons", "Configure the toggle button and the server info bar entry.", Icons);
        Page("About", "About Tell Messenger.", About);
    }

    private static void Page(string name, string description, Action body)
    {
        using var tab = ImRaii.TabItem(name);
        if (!tab)
            return;
        using var child = ImRaii.Child($"##{name}", new Vector2(-1, -1), false);
        if (!child)
            return;
        ImGui.Dummy(new Vector2(0, 2));
        ImGui.TextDisabled(description);
        ImGui.Spacing();
        body();
    }

    // --- Pages ----------------------------------------------------------------

    private void General()
    {
        Section("Storage");
        SliderInt("Max messages per contact", config.MaxMessagesPerContact, 50, 1000, v => config.MaxMessagesPerContact = v);
        SliderInt("Max contacts", config.MaxContacts, 10, 500, v => config.MaxContacts = v);
        SliderInt("Message retention (hours)", config.RetentionHours, 0, 2160, v => config.RetentionHours = v,
            "Messages older than this are deleted. 0 keeps them forever.");

        Section("Privacy");
        Toggle("Clear on logout", config.ClearOnLogout, v => config.ClearOnLogout = v,
            "Deletes all saved conversations when you log out.");
        Toggle("Hide message preview", config.HidePreview, v => config.HidePreview = v,
            "Hides the last message preview text in the contacts list.");

        Section("Time display");
        Radio("Time format", ["12-hour", "24-hour"], config.Use24HourTime ? 1 : 0, v => config.Use24HourTime = v == 1);
        Radio("Time source", ["Local time", "Server time"], config.UseServerTime ? 1 : 0, v => config.UseServerTime = v == 1,
            "Server time is the time shown in-game (UTC).");

        Section("Reset");
        ImGui.TextDisabled("Reset positions or clear all conversation history.");
        if (ImGui.Button("Reset window"))
            resetWindow();
        ImGui.SameLine();
        if (ImGui.Button("Reset icon"))
        {
            config.WidgetPosition = new Vector2(120, 420);
            config.Save();
        }
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, Theme.Current.Danger with { W = 0.45f })
                   .Push(ImGuiCol.ButtonHovered, Theme.Current.Danger with { W = 0.65f }))
        {
            if (ImGui.Button("Clear all chats"))
                ImGui.OpenPopup("##confirmClear");
        }

        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        using var popup = ImRaii.Popup("##confirmClear", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);
        if (popup)
        {
            ImGui.TextUnformatted("Are you sure you want to clear all chats?");
            ImGui.TextDisabled("This permanently deletes all conversation history.");
            ImGui.Spacing();
            if (ImGui.Button("Clear all"))
            {
                messenger.ActiveKey = null;
                messenger.Store.ClearAll();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
        }
    }

    private void Appearance()
    {
        Section("Theme preset");
        foreach (var preset in Theme.Presets)
        {
            var selected = config.Theme == preset.Key;
            DrawSwatch(preset.Accent, preset.Surface);
            ImGui.SameLine();
            if (ImGui.RadioButton($"{preset.Label}##{preset.Key}", selected))
            {
                config.Theme = preset.Key;
                config.Save();
            }
            ImGui.SameLine();
            ImGui.TextDisabled(preset.Description);
        }

        Section("Avatars");
        Toggle("Use Lodestone portraits", config.UseLodestonePortraits, v => config.UseLodestonePortraits = v,
            "Shows each person's Lodestone face picture, in their current gear and headwear, including yours. Looked up by name and home world on the Lodestone once per session and kept in memory only; nothing is saved to disk. Job icons show until a picture arrives or if someone has none.");
        using (ImRaii.Disabled(!config.UseLodestonePortraits))
        {
            if (ImGui.Button("Refresh portraits"))
                messenger.Portraits.ClearCache();
            ImGui.SameLine();
            ImGui.TextDisabled("Fetches everyone's picture again.");
        }

        Section("Bubble colors");
        Combo("Bubble color preset", Theme.BubblePresets.Select(b => (b.Key, b.Label)).ToArray(), config.BubblePreset, v => config.BubblePreset = v);
        Combo("Sent text color", Theme.TextColors.Select(t => (t.Key, t.Label)).ToArray(), config.SentTextColor, v => config.SentTextColor = v);

        Section("Size");
        Radio("Font size", ["Small", "Medium", "Large"], config.FontSize switch { >= 18 => 2, >= 14 => 1, _ => 0 },
            v => config.FontSize = v switch { 2 => 18f, 1 => 14f, _ => 12f },
            "Uses the game's own font at its built-in sizes, which keeps text sharp. Window scales other than 100% pick the nearest size.");
        SliderFloat("Window scale", config.WindowScale * 100, 75, 150, "%.0f%%", v => config.WindowScale = MathF.Round(v / 5) * 5 / 100);

        Section("Opacity");
        SliderFloat("Window opacity (active)", config.OpacityActive * 100, 20, 100, "%.0f%%", v => config.OpacityActive = v / 100);
        SliderFloat("Window opacity (inactive)", config.OpacityInactive * 100, 20, 100, "%.0f%%", v => config.OpacityInactive = v / 100);
    }

    private void Behavior()
    {
        Section("Window");
        Toggle("Auto-open on incoming tell", config.AutoOpenIncoming, v => config.AutoOpenIncoming = v,
            "Opens the messenger when you receive a tell. Not during combat.");
        Toggle("Auto-open on outgoing tell", config.AutoOpenOutgoing, v => config.AutoOpenOutgoing = v,
            "Opens the messenger when you send a tell from the game's chat box or use \"Send Tell\" on a player. Not during combat.");
        Toggle("Hide on entering combat", config.HideOnCombat, v => config.HideOnCombat = v,
            "Hides the messenger when combat starts. You can reopen it during combat; it doesn't reopen on its own afterwards.");
        Toggle("Dim when moving", config.DimWhenMoving, v => config.DimWhenMoving = v,
            "Makes the window see-through while your character moves, unless you're typing in it.");
        Toggle("Auto-focus chat input", config.AutoFocusInput, v => config.AutoFocusInput = v,
            "Puts the cursor in the message box when you open a conversation.");

        Section("Tells");
        Toggle("Hide tells from the game's chat", config.HideTellsFromChat, v => config.HideTellsFromChat = v,
            "Tells only show in the messenger. To hide them from just one chat tab, untick Tell in that tab's filters under Log Window Settings instead. /tmr replies to the last tell.");
        Toggle("Open the game's \"Send Tell\" in the messenger", config.OpenOnGameSendTell, v => config.OpenOnGameSendTell = v,
            "Send Tell on a player's right-click menu (and in Chat 2) opens the conversation here instead of in the chat box.");
        Toggle("Put tells from strangers in Requests", config.EnableRequests, v => config.EnableRequests = v,
            "Tells from players who aren't on your friend list or in your party wait quietly in a Requests tab: no sound, no pop-up, no badge.");
        Toggle("Include the quote when replying", config.QuoteRepliesInGame, v => config.QuoteRepliesInGame = v,
            "Adds a short quote of the message you're replying to in front of your reply, so the other player can see it too.");

        Section("Linkshells");
        Toggle("Show linkshell chats", config.ShowGroupChats, v => config.ShowGroupChats = v,
            "Shows a Linkshells tab with your linkshell and cross-world linkshell chat. When off, only tells appear.");
        using (ImRaii.Disabled(!config.ShowGroupChats))
        {
            ImGui.Indent();
            Toggle("Linkshells", config.GroupLinkshells, v => config.GroupLinkshells = v);
            ImGui.SameLine(150);
            Toggle("Cross-world LS", config.GroupCrossLinkshells, v => config.GroupCrossLinkshells = v);
            ImGui.TextDisabled("Also show in this tab:");
            Toggle("Party", config.GroupParty, v => config.GroupParty = v);
            ImGui.SameLine(150);
            Toggle("Alliance", config.GroupAlliance, v => config.GroupAlliance = v);
            ImGui.SameLine(300);
            Toggle("Free Company", config.GroupFreeCompany, v => config.GroupFreeCompany = v);
            Toggle("PvP Team", config.GroupPvPTeam, v => config.GroupPvPTeam = v);
            ImGui.SameLine(150);
            Toggle("Novice Network", config.GroupNoviceNetwork, v => config.GroupNoviceNetwork = v);
            ImGui.Unindent();
        }

        Section("Keybinds");
        KeybindRow("Open/close messenger", config.ToggleKey);
        KeybindRow("Reply to last tell", config.ReplyKey);
        ImGui.TextDisabled("Keybinds are ignored while you type in the game's chat. /tmsg also opens the messenger.");
    }

    private void Notifications()
    {
        Section("Pop-ups");
        Toggle("Show a pop-up for new tells", config.ShowToasts, v => config.ShowToasts = v,
            "A small card in the top-right corner of the screen. Click it to open the conversation; the × or a right-click dismisses it.");
        using (ImRaii.Disabled(!config.ShowToasts))
            SliderInt("Pop-up time (seconds)", config.ToastSeconds, 0, 30, v => config.ToastSeconds = v,
                "0 keeps each pop-up until you click it or read the message.");
        if (ImGui.Button("Send test tell"))
            messenger.SendTestTell();
        ImGui.SameLine();
        ImGui.TextDisabled($"A pretend tell from {Messenger.TestName}, to try the pop-up and sound.");

        Section("Sound");
        Toggle("Play sound on new tell", config.PlaySound, v => config.PlaySound = v,
            "Plays one of the game's <se> chat sounds when a tell arrives.");
        using (ImRaii.Disabled(!config.PlaySound))
        {
            ImGui.SetNextItemWidth(160);
            using (var combo = ImRaii.Combo("Notification sound", $"<se.{config.SoundEffect}>"))
            {
                if (combo)
                {
                    for (var i = 1; i <= Notifier.SoundCount; i++)
                    {
                        if (ImGui.Selectable($"<se.{i}>", config.SoundEffect == i))
                        {
                            config.SoundEffect = i;
                            config.Save();
                            Notifier.PlaySound(i);
                        }
                    }
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Test"))
                Notifier.PlaySound(config.SoundEffect);
        }

        Toggle("Play a sound when you send", config.PlaySendSound, v => config.PlaySendSound = v,
            "A small interface click when a message goes out.");
        using (ImRaii.Disabled(!config.PlaySendSound))
        {
            ImGui.SetNextItemWidth(160);
            using (var combo = ImRaii.Combo("Send sound", $"Interface sound {config.SendSoundEffect}"))
            {
                if (combo)
                {
                    for (var i = 1; i <= Notifier.UiSoundCount; i++)
                    {
                        if (ImGui.Selectable($"Interface sound {i}", config.SendSoundEffect == i))
                        {
                            config.SendSoundEffect = i;
                            config.Save();
                            Notifier.PlayUiSound(i);
                        }
                    }
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Test##send"))
                Notifier.PlayUiSound(config.SendSoundEffect);
        }

        Section("Alerts");
        Toggle("Flash taskbar on new tell", config.FlashTaskbar, v => config.FlashTaskbar = v,
            "Flashes the game's taskbar button when a tell or a mention arrives while you're tabbed out.");
        Toggle("Alert on mentions in group chats", config.MentionAlerts, v => config.MentionAlerts = v,
            "Highlights group messages that contain your first name and plays the tell sound.");
    }

    private void Icons()
    {
        Section("Icon mode");
        Radio("Show", ["Floating button", "Server info bar", "Both"], (int)config.IconMode, v => config.IconMode = (IconMode)v,
            "The server info bar is the strip of text at the top right of the screen.");

        Section("Floating button");
        using (ImRaii.Disabled(config.IconMode == IconMode.ServerInfoBar))
        {
            SliderFloat("Icon size", config.IconSize, 28, 72, "%.0f px", v => config.IconSize = MathF.Round(v));
            SliderFloat("Transparency when not hovered", config.WidgetIdleOpacity * 100, 20, 100, "%.0f%%", v => config.WidgetIdleOpacity = v / 100);
            Toggle("Lock icon position", config.LockIcon, v => config.LockIcon = v,
                "Stops the button from being dragged.");
            Toggle("Desaturate when idle", config.DesaturateIdle, v => config.DesaturateIdle = v,
                "Greys out the button when there are no unread messages.");
            Toggle("Show unread badge", config.ShowBadge, v => config.ShowBadge = v);
            Toggle("Badge pulse animation", config.BadgePulse, v => config.BadgePulse = v);
            Toggle("Show message preview", config.ShowWidgetPreview, v => config.ShowWidgetPreview = v,
                "Shows the sender and the newest message next to the button.");
            using (ImRaii.Disabled(!config.ShowWidgetPreview))
            {
                SliderInt("Auto-dismiss preview (seconds)", config.PreviewDismissSeconds, 0, 60, v => config.PreviewDismissSeconds = v,
                    "0 keeps the preview until you read the message.");
                Radio("Preview position", ["Right", "Left", "Above", "Below"], (int)config.PreviewSide, v => config.PreviewSide = (PreviewSide)v);
            }
        }
    }

    private static void About()
    {
        Section("Tell Messenger");
        ImGui.TextWrapped("Every tell in one messenger window, with history, online status and unread badges.");
        ImGui.Spacing();
        ImGui.TextWrapped("Based on WhisperMessenger, a World of Warcraft addon by the WhisperMessenger contributors (MIT License).");
        ImGui.Spacing();
        Section("Commands");
        ImGui.TextUnformatted("/tmsg                    Open or close the messenger");
        ImGui.TextUnformatted("/tmsg First Last@World  Start a conversation");
        ImGui.TextUnformatted("/tmsg settings           Open these settings");
        ImGui.TextUnformatted("/tmr                     Reply to the last tell");
        Section("Differences from WhisperMessenger");
        ImGui.TextWrapped("FFXIV has no hidden plugin-to-plugin channel, so typing indicators, \"Seen\" receipts and reactions aren't possible. Reply quotes are kept on your side unless you turn on \"Include the quote when replying\" under Behavior.");
        ImGui.Spacing();
        ImGui.TextWrapped("Tells can't reach players who are offline, set to Busy, or on another data center. The messenger warns you when the friend list shows this. If a send fails, the game's error shows under the message instead of in the game's chat.");
        ImGui.Spacing();
        ImGui.TextWrapped("Links work the FFXIV way: use \"Link\" on an item and type <item>, or use <flag>, <pos> and <t>. Players on your blacklist can't send you tells, so they never show up here.");
    }

    // --- Controls ---------------------------------------------------------------

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(Theme.Current.Accent, title.ToUpperInvariant());
        ImGui.Separator();
    }

    private void Toggle(string label, bool value, Action<bool> set, string? help = null)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            config.Save();
        }
        Help(help);
    }

    private void SliderInt(string label, int value, int min, int max, Action<int> set, string? help = null)
    {
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderInt(label, ref value, min, max))
            set(value);
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save();
        Help(help);
    }

    private void SliderFloat(string label, float value, float min, float max, string format, Action<float> set)
    {
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderFloat(label, ref value, min, max, format))
            set(value);
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save();
    }

    private void Radio(string label, string[] options, int value, Action<int> set, string? help = null)
    {
        ImGui.TextUnformatted(label);
        for (var i = 0; i < options.Length; i++)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{options[i]}##{label}", value == i))
            {
                set(i);
                config.Save();
            }
        }
        Help(help);
    }

    private void Combo(string label, (string Key, string Label)[] options, string value, Action<string> set)
    {
        ImGui.SetNextItemWidth(220);
        var current = options.FirstOrDefault(o => o.Key == value).Label ?? options[0].Label;
        using var combo = ImRaii.Combo(label, current);
        if (!combo)
            return;
        foreach (var (key, text) in options)
        {
            if (ImGui.Selectable(text, key == value))
            {
                set(key);
                config.Save();
            }
        }
    }

    private static void Help(string? help)
    {
        if (help == null)
            return;
        using (ImRaii.PushIndent(28f))
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Current.TextSecondary))
            ImGui.TextWrapped(help);
    }

    private static void DrawSwatch(Vector4 accent, Vector4 surface)
    {
        var min = ImGui.GetCursorScreenPos() + new Vector2(0, 3);
        var size = ImGui.GetTextLineHeight() - 2;
        var list = ImGui.GetWindowDrawList();
        list.AddRectFilled(min, min + new Vector2(size * 2, size), Theme.U32(surface with { W = 1 }), 4);
        list.AddCircleFilled(min + new Vector2(size * 1.4f, size / 2), size * 0.3f, Theme.U32(accent));
        list.AddRect(min, min + new Vector2(size * 2, size), Theme.U32(new Vector4(1, 1, 1, 0.2f)), 4);
        ImGui.Dummy(new Vector2(size * 2, size + 2));
    }

    private void KeybindRow(string label, Keybind bind)
    {
        using var id = ImRaii.PushId(label);
        if (capturing == bind)
        {
            ImGui.TextColored(Theme.Current.Accent, "Press a key... (Esc to cancel)");
            var keys = Services.KeyState;
            if (keys[VirtualKey.ESCAPE])
            {
                capturing = null;
            }
            else
            {
                foreach (var key in keys.GetValidVirtualKeys())
                {
                    if (key is VirtualKey.CONTROL or VirtualKey.MENU or VirtualKey.SHIFT or VirtualKey.LCONTROL or VirtualKey.RCONTROL
                        or VirtualKey.LMENU or VirtualKey.RMENU or VirtualKey.LSHIFT or VirtualKey.RSHIFT or VirtualKey.LBUTTON
                        or VirtualKey.RBUTTON or VirtualKey.MBUTTON)
                        continue;
                    if (!keys[key])
                        continue;
                    bind.Key = (int)key;
                    bind.Ctrl = keys[VirtualKey.CONTROL];
                    bind.Alt = keys[VirtualKey.MENU];
                    bind.Shift = keys[VirtualKey.SHIFT];
                    keys[key] = false;
                    capturing = null;
                    config.Save();
                    break;
                }
            }
        }
        else if (ImGui.Button($"{bind}##bind", new Vector2(140, 0)))
        {
            capturing = bind;
        }
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        if (bind.Key != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
            {
                bind.Key = 0;
                bind.Ctrl = bind.Alt = bind.Shift = false;
                config.Save();
            }
        }
    }
}
