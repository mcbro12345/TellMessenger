using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace TellMessenger.Game;

// The things Chat 2 offers when you right-click a player's name: invite,
// promote/kick, friend request, blacklist, Novice Network, target and
// adventurer plate. Most need the player's content ID, which comes from
// wherever the game has it: friend list, party, people nearby or tell history.
public sealed unsafe class PlayerActions : IDisposable
{
    // The game fills in "<t>"-style placeholders in text commands through
    // this. /friendlist add and /blist add only take a placeholder, not a
    // name, so a one-off placeholder is swapped for "Name@World" (the same
    // trick Chat 2 uses).
    private const string ResolvePlaceholderSig = "E8 ?? ?? ?? ?? 48 85 C0 0F 84 ?? ?? ?? ?? 48 8B D0 49 8D 4F";
    private delegate nint ResolvePlaceholderDelegate(nint a1, byte* placeholder, byte a3, byte a4);

    private readonly Hook<ResolvePlaceholderDelegate>? resolveHook;
    private readonly nint replacementBuffer = Marshal.AllocHGlobal(128);
    private readonly string placeholder = $"<{Guid.NewGuid():N}>";
    private string? replacement;

    public PlayerActions()
    {
        try
        {
            resolveHook = Services.GameInterop.HookFromSignature<ResolvePlaceholderDelegate>(ResolvePlaceholderSig, ResolveDetour);
            resolveHook.Enable();
        }
        catch (Exception e)
        {
            Services.Log.Warning(e, "Couldn't find the text command placeholder function; friend requests and blacklisting from the messenger are off");
        }
    }

    public void Dispose()
    {
        resolveHook?.Dispose();
        Marshal.FreeHGlobal(replacementBuffer);
    }

    public bool CanUseListCommands => resolveHook != null;

    public static bool IsPublicWorld(string world) => WorldRow(world) is { IsPublic: true };

    public static ushort WorldId(string world) => (ushort)(WorldRow(world)?.RowId ?? 0);

    private static World? WorldRow(string world) =>
        Services.Data.GetExcelSheet<World>().FirstOrDefault(w => w.Name.ExtractText().Equals(world, StringComparison.OrdinalIgnoreCase));

    public static ulong ContentIdOf(string name, string world)
    {
        var worldId = WorldId(world);
        if (worldId == 0)
            return 0;

        var friends = InfoProxyFriendList.Instance();
        if (friends != null)
        {
            var entry = friends->GetEntryByName(name, worldId);
            if (entry != null && entry->ContentId != 0)
                return entry->ContentId;
        }
        if (PartyMember(name, worldId) is { ContentId: not 0 } member)
            return (ulong)member.ContentId;
        if (Nearby(name, worldId) is { } character)
        {
            var id = ((Character*)character.Address)->ContentId;
            if (id != 0)
                return id;
        }
        var acquaintances = AcquaintanceModule.Instance();
        if (acquaintances != null)
        {
            foreach (ref var person in acquaintances->TellHistory)
            {
                if (person.ContentId != 0 && person.WorldId == worldId && person.Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return person.ContentId;
            }
        }
        return 0;
    }

    public static Dalamud.Game.ClientState.Party.IPartyMember? PartyMember(string name, ushort worldId) =>
        Services.Party.FirstOrDefault(m => m.Name.TextValue == name && m.World.RowId == worldId);

    public static IPlayerCharacter? Nearby(string name, ushort worldId) =>
        Services.Objects.OfType<IPlayerCharacter>().FirstOrDefault(c => c.Name.TextValue == name && c.HomeWorld.RowId == worldId);

    public static bool IsPartyLeader()
    {
        var party = Services.Party;
        if (party.Length == 0)
            return true;
        var leader = party[(int)party.PartyLeaderIndex];
        return leader != null && (ulong)leader.ContentId == Services.PlayerState.ContentId;
    }

    public static bool InInstance => Services.Condition[ConditionFlag.BoundByDuty56];

    // Duties where you can invite people already inside (deep dungeons,
    // Eureka, Bozja and the like).
    public static bool InPartyInstance =>
        Services.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(Services.ClientState.TerritoryType)?.TerritoryIntendedUse.RowId is 41 or 47 or 48 or 52 or 53 or 61;

    public static bool IsMentor()
    {
        var state = PlayerState.Instance();
        return state != null && state->IsMentor();
    }

    public static void InviteSameWorld(string name, string world, ulong contentId) =>
        InfoProxyPartyInvite.Instance()->InviteToParty(contentId, name, WorldId(world));

    public static void InviteOtherWorld(ulong contentId, string world) =>
        InfoProxyPartyInvite.Instance()->InviteToPartyContentId(contentId, WorldId(world));

    public static void InviteInInstance(ulong contentId) =>
        InfoProxyPartyInvite.Instance()->InviteToPartyInInstanceByContentId(contentId);

    public static void Promote(string name, ulong contentId) => AgentPartyMember.Instance()->Promote(name, 0, contentId);

    public static void Kick(string name, ulong contentId) => AgentPartyMember.Instance()->Kick(name, 0, contentId);

    public static void InviteToNoviceNetwork(string name, string world) =>
        InfoProxyNoviceNetwork.Instance()->InviteToNoviceNetwork(0, 0, WorldId(world), name);

    public static void OpenAdventurerPlate(ulong contentId) => AgentCharaCard.Instance()->OpenCharaCard(contentId);

    public static void Target(string name, string world)
    {
        if (Nearby(name, WorldId(world)) is { } character)
            Services.Targets.Target = character;
    }

    public void SendFriendRequest(string name, string world) => ListCommand("friendlist", name, world);

    public void AddToBlacklist(string name, string world) => ListCommand("blist", name, world);

    private void ListCommand(string command, string name, string world)
    {
        if (resolveHook == null)
            return;
        replacement = $"{name}@{world}";
        ChatSender.Run($"/{command} add {placeholder}");
    }

    private nint ResolveDetour(nint a1, byte* text, byte a3, byte a4)
    {
        if (replacement != null && Marshal.PtrToStringUTF8((nint)text) == placeholder)
        {
            var bytes = Encoding.UTF8.GetBytes(replacement + '\0');
            Marshal.Copy(bytes, 0, replacementBuffer, Math.Min(bytes.Length, 128));
            replacement = null;
            return replacementBuffer;
        }
        return resolveHook!.Original(a1, text, a3, a4);
    }
}
