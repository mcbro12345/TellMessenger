using System;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace TellMessenger.Game;

// Decides once per session whether to work with Chat 2 or the game's own chat.
// Plugins load one after another at startup, so Chat 2 may not be up yet when
// this plugin is. It checks now, then again every time the set of loaded
// plugins changes, and settles once nothing new has loaded for a few seconds.
// Until then it goes with what it has seen so far.
public sealed class ChatChoice : IDisposable
{
    private const string ChatTwoName = "ChatTwo";
    private static readonly TimeSpan QuietFor = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

    private DateTime lastChange = DateTime.UtcNow;
    private DateTime lastCheck = DateTime.MinValue;
    private int loadedCount = -1;

    public ChatChoice()
    {
        UseChatTwo = ChatTwoLoaded();
        Services.PluginInterface.ActivePluginsChanged += OnPluginsChanged;
        Services.Framework.Update += OnUpdate;
    }

    // Chat 2 for this session (so far, until Settled).
    public bool UseChatTwo { get; private set; }

    public bool Settled { get; private set; }

    public void Dispose()
    {
        Services.PluginInterface.ActivePluginsChanged -= OnPluginsChanged;
        Services.Framework.Update -= OnUpdate;
    }

    private void OnPluginsChanged(IActivePluginsChangedEventArgs args) => lastChange = DateTime.UtcNow;

    private void OnUpdate(IFramework framework)
    {
        if (Settled || DateTime.UtcNow - lastCheck < CheckEvery)
            return;
        lastCheck = DateTime.UtcNow;

        var count = Services.PluginInterface.InstalledPlugins.Count(p => p.IsLoaded);
        if (count != loadedCount)
        {
            loadedCount = count;
            lastChange = DateTime.UtcNow;
        }
        UseChatTwo = ChatTwoLoaded();

        if (DateTime.UtcNow - lastChange < QuietFor)
            return;
        Settled = true;
        Services.PluginInterface.ActivePluginsChanged -= OnPluginsChanged;
        Services.Framework.Update -= OnUpdate;
        Services.Log.Information(UseChatTwo
            ? "Chat 2 is loaded; working with Chat 2 this session."
            : "Chat 2 isn't loaded; working with the game's chat this session.");
    }

    // Installed or loaded as a dev plugin (dev plugins are listed too), or any
    // build that registers Chat 2's /chat2 command, in case a dev copy or fork
    // goes by another internal name.
    private static bool ChatTwoLoaded() =>
        Services.PluginInterface.InstalledPlugins.Any(p => p.IsLoaded && p.InternalName.Equals(ChatTwoName, StringComparison.OrdinalIgnoreCase))
        || Services.Commands.Commands.ContainsKey("/chat2");
}
