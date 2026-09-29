using System;
using System.Linq;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using TellMessenger.Game;
using TellMessenger.UI;

namespace TellMessenger;

// A messenger window for tells, ported from the WoW addon WhisperMessenger.
public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/tmsg";
    private const string ReplyCommand = "/tmr";

    private readonly Configuration config;
    private readonly Messenger messenger;
    private readonly WindowSystem windows = new("TellMessenger");
    private readonly MessengerWindow messengerWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly ToggleWidget widget;
    private readonly ToastWindow toasts;
    private readonly SendTellHook sendTellHook;
    private readonly ChatChoice chatChoice;
    private readonly ChatTwoIntegration chatTwo;
    private readonly NativeChatVisibility nativeChat;
    private readonly GameMenuSendTell gameMenu;
    private IDtrBarEntry? dtrEntry;
    private bool toggleHeld;
    private bool replyHeld;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (config.Version < 2)
        {
            // Font size is now in points and snaps to the game font's sizes.
            config.FontSize = 12f;
            config.Version = 2;
            config.Save();
        }
        if (config.Version < 3)
        {
            // No default open/close key any more; the user picks one.
            if (config.ToggleKey is { Key: 0x4F, Ctrl: true, Alt: false, Shift: false })
                config.ToggleKey = new Keybind();
            // The second tab is for linkshells now.
            config.GroupParty = config.GroupAlliance = config.GroupFreeCompany = config.GroupPvPTeam = false;
            config.Version = 3;
            config.Save();
        }

        messenger = new Messenger(config);
        settingsWindow = new SettingsWindow(messenger, () => messengerWindow!.ResetPosition());
        messengerWindow = new MessengerWindow(messenger, () => settingsWindow.Toggle());
        // The button hides along with whichever chat this session uses:
        // Chat 2 or the game's own (see ChatChoice).
        widget = new ToggleWidget(messenger, ToggleMessenger, () => chatChoice?.UseChatTwo == true ? chatTwo?.Hidden == true : nativeChat?.Hidden == true);
        toasts = new ToastWindow(messenger);
        sendTellHook = new SendTellHook(config, OpenTell, () => messenger.SendingNow);
        chatChoice = new ChatChoice();
        chatTwo = new ChatTwoIntegration(config, OpenTell, () => chatChoice.UseChatTwo);
        nativeChat = new NativeChatVisibility();
        gameMenu = new GameMenuSendTell(config, OpenTell);
        windows.AddWindow(messengerWindow);
        windows.AddWindow(settingsWindow);
        windows.AddWindow(widget);
        windows.AddWindow(toasts);

        Services.Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open or close Tell Messenger. \"/tmsg First Last@World\" starts a conversation, \"/tmsg settings\" opens the settings.",
        });
        Services.Commands.AddHandler(ReplyCommand, new CommandInfo((_, _) => ReplyToLast())
        {
            HelpMessage = "Open Tell Messenger on the last person who sent you a tell.",
        });

        Services.PluginInterface.UiBuilder.Draw += OnDraw;
        Services.PluginInterface.UiBuilder.OpenMainUi += ToggleMessenger;
        Services.PluginInterface.UiBuilder.OpenConfigUi += settingsWindow.Toggle;
        Services.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;
        Services.PluginInterface.UiBuilder.Draw -= OnDraw;
        Services.PluginInterface.UiBuilder.OpenMainUi -= ToggleMessenger;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= settingsWindow.Toggle;
        Services.Commands.RemoveHandler(Command);
        Services.Commands.RemoveHandler(ReplyCommand);
        sendTellHook.Dispose();
        chatTwo.Dispose();
        chatChoice.Dispose();
        nativeChat.Dispose();
        gameMenu.Dispose();
        dtrEntry?.Remove();
        windows.RemoveAllWindows();
        messengerWindow.Dispose();
        messenger.Dispose();
        config.Save();
    }

    private void OpenTell(string name, string world)
    {
        if (messenger.Owner == null)
            return;
        var conversation = messenger.StartTell(name, world);
        messengerWindow.Open(conversation.Key);
    }

    private void ToggleMessenger()
    {
        if (messengerWindow.IsOpen)
            messengerWindow.IsOpen = false;
        else
            messengerWindow.Open(messenger.ActiveKey);
    }

    private void ReplyToLast()
    {
        if (messenger.ReplyToLastKey != null && messenger.Store.Get(messenger.ReplyToLastKey) != null)
            messengerWindow.Open(messenger.ReplyToLastKey);
        else
            messengerWindow.Open();
    }

    private void OnCommand(string command, string args)
    {
        args = args.Trim();
        if (args.Length == 0)
        {
            ToggleMessenger();
            return;
        }
        if (args.Equals("settings", StringComparison.OrdinalIgnoreCase) || args.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            settingsWindow.IsOpen = true;
            return;
        }

        var parts = args.Split('@', 2);
        var name = string.Join(' ', parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var world = parts.Length > 1 ? parts[1].Trim() : messenger.Owner?.Split('@')[1];
        if (messenger.Owner == null || name.Split(' ').Length != 2 || string.IsNullOrEmpty(world))
        {
            Services.Chat.PrintError("Usage: /tmsg First Last@World", "Tell Messenger");
            return;
        }
        var conversation = messenger.StartTell(Capitalize(name), Capitalize(world));
        messengerWindow.Open(conversation.Key);
    }

    private static string Capitalize(string text) =>
        string.Join(' ', text.Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    private void OnUpdate(IFramework framework)
    {
        messenger.Update();
        UpdateDtr();
    }

    private void OnDraw()
    {
        HandleKeybinds();
        windows.Draw();
    }

    // Keybinds fire once per press and are ignored while typing anywhere.
    private unsafe void HandleKeybinds()
    {
        var typing = Dalamud.Bindings.ImGui.ImGui.GetIO().WantTextInput;
        var atk = RaptureAtkModule.Instance();
        if (atk != null && atk->IsTextInputActive())
            typing = true;

        if (Pressed(config.ToggleKey, ref toggleHeld, typing))
            ToggleMessenger();
        if (Pressed(config.ReplyKey, ref replyHeld, typing))
            ReplyToLast();
    }

    private static bool Pressed(Keybind bind, ref bool held, bool typing)
    {
        if (bind.Key == 0)
            return false;
        var keys = Services.KeyState;
        var key = (VirtualKey)bind.Key;
        var down = keys[key] && keys[VirtualKey.CONTROL] == bind.Ctrl && keys[VirtualKey.MENU] == bind.Alt && keys[VirtualKey.SHIFT] == bind.Shift;
        if (!down)
        {
            held = false;
            return false;
        }
        if (held || typing)
            return false;
        held = true;
        keys[key] = false; // keep the game from also acting on it
        return true;
    }

    private void UpdateDtr()
    {
        var show = config.IconMode != IconMode.Widget;
        if (!show)
        {
            if (dtrEntry != null)
                dtrEntry.Shown = false;
            return;
        }

        dtrEntry ??= CreateDtrEntry();
        dtrEntry.Shown = true;
        var unread = messenger.UnreadCount(ContactsTab.Whispers) + messenger.UnreadCount(ContactsTab.Groups);
        // The game font has no chat bubble, so a boxed T stands in, with the
        // unread count in the game's boxed digits while it fits (0-31).
        var icon = SeIconChar.BoxedLetterT.ToIconString();
        var text = unread switch
        {
            0 => icon,
            <= 31 => $"{icon}{((SeIconChar)((int)SeIconChar.BoxedNumber0 + unread)).ToIconString()}",
            _ => $"{icon} {unread}",
        };
        if (dtrEntry.Text?.TextValue != text)
            dtrEntry.Text = text;
    }

    private IDtrBarEntry CreateDtrEntry()
    {
        var entry = Services.DtrBar.Get("Tell Messenger");
        entry.Tooltip = new SeStringBuilder().AddText(config.ToggleKey.Key == 0
            ? "Tell Messenger\nClick to open/close"
            : $"Tell Messenger\nOpen/close: {config.ToggleKey}").Build();
        entry.OnClick = _ => ToggleMessenger();
        return entry;
    }

}
