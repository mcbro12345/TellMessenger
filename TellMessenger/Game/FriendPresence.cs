using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using Status = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCommonList.CharacterData.OnlineStatus;

namespace TellMessenger.Game;

public enum Presence
{
    Unknown,
    Offline,
    Online,
    Away,
    Busy,
}

public sealed record FriendInfo(string Name, string World, Presence Presence, byte Job, string Location, string CurrentWorld, uint CurrentWorldId);

// Online status, job and zone for people on the friend list, read from the
// game's friend list data a couple of times a second.
public sealed class FriendPresence
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(60);

    private Dictionary<string, FriendInfo> friends = new(StringComparer.OrdinalIgnoreCase);
    private DateTime lastPoll = DateTime.MinValue;
    private DateTime lastRequest = DateTime.MinValue;

    public bool IsFriend(string contactKey) => friends.ContainsKey(contactKey);

    public FriendInfo? Get(string contactKey) => friends.GetValueOrDefault(contactKey);

    public int Count => friends.Count;

    public IEnumerable<FriendInfo> All => friends.Values;

    // The friend list only refreshes when the game asks the server for it, which
    // normally happens when the Friend List window opens. Asked for at most once
    // a minute, and only while the messenger is open.
    public unsafe void RequestRefresh()
    {
        if (DateTime.UtcNow - lastRequest < RequestInterval)
            return;
        lastRequest = DateTime.UtcNow;
        var list = InfoProxyFriendList.Instance();
        if (list != null)
            list->RequestData();
    }

    public unsafe void Update()
    {
        if (DateTime.UtcNow - lastPoll < PollInterval)
            return;
        lastPoll = DateTime.UtcNow;

        var list = InfoProxyFriendList.Instance();
        if (list == null)
            return;

        var next = new Dictionary<string, FriendInfo>(StringComparer.OrdinalIgnoreCase);
        var count = list->GetEntryCount();
        for (var i = 0u; i < count; i++)
        {
            var entry = list->GetEntry(i);
            if (entry == null)
                continue;

            var name = entry->NameString;
            var world = WorldName(entry->HomeWorld);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(world))
                continue;

            var presence = ToPresence(entry->State);
            var location = presence == Presence.Offline ? "" : PlaceName(entry->Location);
            var info = new FriendInfo(name, world, presence, entry->Job, location, WorldName(entry->CurrentWorld), entry->CurrentWorld);
            var key = $"{name}@{world}";
            next[key] = info;
        }

        // An empty read usually means the list hasn't loaded yet, not that
        // everyone was removed; keep the last good data.
        if (next.Count > 0 || count == 0 && friends.Count == 0)
            friends = next;
    }

    private static Presence ToPresence(Status state)
    {
        if (!state.HasFlag(Status.Online))
            return Presence.Offline;
        if (state.HasFlag(Status.Busy))
            return Presence.Busy;
        if (state.HasFlag(Status.AwayFromKeyboard))
            return Presence.Away;
        return Presence.Online;
    }

    public static string WorldName(uint worldId) =>
        worldId != 0 && Services.Data.GetExcelSheet<World>().TryGetRow(worldId, out var world) ? world.Name.ExtractText() : "";

    // 0 when no public world has that name.
    public static uint WorldId(string name)
    {
        foreach (var world in Services.Data.GetExcelSheet<World>())
            if (world.IsPublic && world.Name.ExtractText().Equals(name, StringComparison.OrdinalIgnoreCase))
                return world.RowId;
        return 0;
    }

    public static uint DataCenterOf(uint worldId) =>
        worldId != 0 && Services.Data.GetExcelSheet<World>().TryGetRow(worldId, out var world) ? world.DataCenter.RowId : 0;

    public static string PlaceName(uint territoryId)
    {
        if (territoryId == 0 || !Services.Data.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territory))
            return "";

        var cfc = territory.ContentFinderCondition;
        if (cfc.RowId != 0 && cfc.IsValid)
        {
            var duty = cfc.Value.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(duty))
                return char.ToUpper(duty[0]) + duty[1..];
        }
        return territory.PlaceName.IsValid ? territory.PlaceName.Value.Name.ExtractText() : "";
    }
}
