using System;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace TellMessenger.Game;

// Other places the game starts a tell from (the friend list, party list,
// linkshell member lists) set a "context tell target". Those open in the
// messenger too. The player right-click menu is handled by GameMenuSendTell
// and Chat 2 by ChatTwoIntegration.
public sealed unsafe class SendTellHook : IDisposable
{
    private readonly Configuration config;
    private readonly Action<string, string> open;
    private readonly Func<bool> sentRecently;
    private readonly Hook<RaptureShellModule.Delegates.SetContextTellTarget> hook;

    public SendTellHook(Configuration config, Action<string, string> open, Func<bool> sentRecently)
    {
        this.config = config;
        this.open = open;
        this.sentRecently = sentRecently;
        hook = Services.GameInterop.HookFromAddress<RaptureShellModule.Delegates.SetContextTellTarget>(
            RaptureShellModule.MemberFunctionPointers.SetContextTellTarget, Detour);
        hook.Enable();
    }

    public void Dispose() => hook.Dispose();

    private bool Detour(RaptureShellModule* shell, Utf8String* playerName, Utf8String* worldName, ushort worldId,
        ulong accountId, ulong contentId, ushort reason, bool setChatType)
    {
        try
        {
            if (config.OpenOnGameSendTell && playerName != null && !sentRecently())
            {
                var name = playerName->ToString();
                var world = FriendPresence.WorldName(worldId);
                if (world.Length == 0 && worldName != null)
                    world = worldName->ToString();
                if (name.Length > 0 && world.Length > 0)
                {
                    open(name, world);
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Services.Log.Error(e, "Could not open Send Tell in the messenger");
        }
        return hook.Original(shell, playerName, worldName, worldId, accountId, contentId, reason, setChatType);
    }
}
