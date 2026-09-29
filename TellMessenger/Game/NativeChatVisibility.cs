using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace TellMessenger.Game;

// Whether the game's own chat window is showing, for when Chat 2 isn't in
// use. It counts as hidden when the window is closed or faded out (the game
// hides it in cutscenes, during loading, with the UI hidden, and can fade it
// when inactive). Like the Chat 2 check, it has to stay hidden for a moment
// before it counts, so one odd frame doesn't make the button flicker.
public sealed unsafe class NativeChatVisibility : IDisposable
{
    private DateTime lastShown = DateTime.UtcNow;

    public NativeChatVisibility() => Services.Framework.Update += OnUpdate;

    public void Dispose() => Services.Framework.Update -= OnUpdate;

    public bool Hidden => DateTime.UtcNow - lastShown > TimeSpan.FromMilliseconds(150);

    private void OnUpdate(IFramework framework)
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return;
        var chat = manager->GetAddonByName("ChatLog", 1);
        if (chat == null)
            return;
        var alpha = chat->RootNode != null ? chat->RootNode->Color.A : chat->Alpha;
        if (chat->IsVisible && alpha > 8)
            lastShown = DateTime.UtcNow;
    }
}
