using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;

namespace TellMessenger.Game;

// Chat 2 replaces the game's chat window, and its right-click "Send Tell"
// doesn't go through the game: it fills its own message box with
// "/tell Name@World " in one step. Chat 2 has no way for another plugin to
// change that menu item, so this watches Chat 2's message box instead. When
// that exact text appears all at once (not typed), it's cleared and the
// conversation opens in the messenger.
public sealed partial class ChatTwoIntegration : IDisposable
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Configuration config;
    private readonly Action<string, string> open;
    private readonly Func<bool> enabled;

    private object? chatWindow;
    private object? inputHandler;
    private FieldInfo? chatInputField;
    private FieldInfo? activateField;
    private DateTime nextLookup;
    private string lastText = "";

    public ChatTwoIntegration(Configuration config, Action<string, string> open, Func<bool> enabled)
    {
        this.config = config;
        this.open = open;
        this.enabled = enabled;
        Services.Framework.Update += OnUpdate;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;

    // True while Chat 2 is loaded but hasn't drawn its chat window for a
    // moment: hidden in battle or cutscenes, faded out when inactive, during
    // loading screens, and so on. Chat 2 records whether it drew each frame.
    // Chat 2 is loaded and its chat window was found.
    public bool Loaded => chatWindow != null;

    public bool Hidden => chatWindow != null && DateTime.UtcNow - lastDrawn > TimeSpan.FromMilliseconds(150);

    private DateTime lastDrawn = DateTime.UtcNow;

    private void OnUpdate(IFramework framework)
    {
        // Only while this session works with Chat 2.
        if (!enabled())
            return;
        try
        {
            // Chat 2 was unloaded: let go of it (and fall back to the game's chat).
            if (inputHandler != null && !Services.Commands.Commands.ContainsKey("/chat2"))
            {
                inputHandler = null;
                chatWindow = null;
            }
            if (inputHandler == null && !Find())
                return;
            if (chatWindow?.GetType().GetField("DrewThisFrame", Any)?.GetValue(chatWindow) is not false)
                lastDrawn = DateTime.UtcNow;
            if (!config.OpenOnGameSendTell)
                return;
            var text = chatInputField!.GetValue(inputHandler) as string ?? "";
            if (text == lastText)
                return;
            var before = lastText;
            lastText = text;

            // Typed by hand: the text grew one character at a time.
            if (text.Length == before.Length + 1 && text.StartsWith(before, StringComparison.Ordinal))
                return;
            var match = TellText().Match(text);
            if (!match.Success)
                return;
            var name = match.Groups[1].Value;
            var world = match.Groups[2].Value;
            if (FriendPresence.WorldId(world) == 0)
                return;

            chatInputField.SetValue(inputHandler, "");
            activateField?.SetValue(inputHandler, false);
            lastText = "";
            open(name, world);
        }
        catch (Exception e)
        {
            // Chat 2 was unloaded or changed; look it up again later.
            Services.Log.Debug(e, "Lost track of Chat 2's message box");
            inputHandler = null;
            chatWindow = null;
        }
    }

    // Chat 2's message box lives on its chat window, which registers /chat2:
    // command handler → Chat 2's Commands → the /chat2 wrapper → its Execute
    // listeners → the chat window → its InputHandler.
    private bool Find()
    {
        if (DateTime.UtcNow < nextLookup)
            return false;
        nextLookup = DateTime.UtcNow.AddSeconds(3);

        if (!Services.Commands.Commands.TryGetValue("/chat2", out var info))
            return false;
        var commands = info.Handler.Target;
        if (commands?.GetType().GetField("Registered", Any)?.GetValue(commands) is not IDictionary registered)
            return false;
        var wrapper = registered["/chat2"];
        if (wrapper?.GetType().GetField("Execute", Any)?.GetValue(wrapper) is not Delegate execute)
            return false;

        foreach (var listener in execute.GetInvocationList())
        {
            var window = listener.Target;
            var handler = window?.GetType().GetField("InputHandler", Any)?.GetValue(window);
            var input = handler?.GetType().GetField("ChatInput", Any);
            if (handler == null || input?.FieldType != typeof(string))
                continue;
            inputHandler = handler;
            chatWindow = window;
            chatInputField = input;
            activateField = handler.GetType().GetField("Activate", Any);
            lastText = input.GetValue(handler) as string ?? "";
            Services.Log.Information("Found Chat 2; its Send Tell opens the messenger.");
            return true;
        }
        return false;
    }

    // "/tell First Last@World " exactly as Chat 2's Send Tell writes it.
    [GeneratedRegex(@"^/tell ([^@\s]+ [^@\s]+)@([A-Za-z]+) $")]
    private static partial Regex TellText();
}
