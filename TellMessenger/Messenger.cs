using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using TellMessenger.Game;
using TellMessenger.Model;
using TellMessenger.UI;

namespace TellMessenger;

public enum ContactsTab
{
    Whispers,
    Groups,
    Requests,
}

public sealed record IncomingPreview(string ConversationKey, string Sender, string Text, DateTime Time);

// A pop-up in the top-right corner for a new tell.
public sealed record Toast(long Id, string ConversationKey, string Sender, string Text, DateTime Time);

// The plugin's runtime: turns game chat into conversations, sends messages,
// tracks unread counts and decides when to alert or open the window.
public sealed class Messenger : IDisposable
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ConfirmDelay = TimeSpan.FromSeconds(3);

    public readonly Configuration Config;
    public readonly HistoryStore Store;
    public readonly FriendPresence Friends = new();
    public readonly LodestonePortraits Portraits;
    private readonly ChatSender sender = new();

    public bool SendingNow => sender.Busy;

    // Contacts' jobs seen this session, from nearby players and party lists.
    private readonly Dictionary<string, byte> seenJobs = new(StringComparer.OrdinalIgnoreCase);
    private bool wasInCombat;
    private DateTime lastLabelRefresh = DateTime.MinValue;

    public Messenger(Configuration config)
    {
        Config = config;
        Store = new HistoryStore(config, Services.PluginInterface.GetPluginConfigDirectory());
        Portraits = new LodestonePortraits(config, Services.PluginInterface.GetPluginConfigDirectory());
        Services.Chat.ChatMessage += OnChatMessage;
        Services.ClientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        Services.Chat.ChatMessage -= OnChatMessage;
        Services.ClientState.Logout -= OnLogout;
        Portraits.Dispose();
        Store.Flush();
    }

    // UI state shared by the windows.
    public string? ActiveKey { get; set; }
    public ContactsTab Tab { get; set; } = ContactsTab.Whispers;
    public bool WindowVisible { get; set; }
    public IncomingPreview? LatestPreview { get; private set; }
    public IReadOnlyList<Toast> Toasts => toasts;
    private readonly List<Toast> toasts = [];
    private long nextToastId;

    // The fake contact behind Settings → Notifications → Send test tell.
    public const string TestName = "Test Moogle";
    public static bool IsTest(Conversation conversation) => conversation.IsTell && conversation.Name == TestName;
    public DateTime LastIncoming { get; private set; } = DateTime.MinValue;
    public string? ReplyToLastKey { get; private set; }

    // Asks the UI to open the window, optionally on a conversation.
    public event Action<string?>? OpenRequested;
    public event Action? HideRequested;

    public string? Owner
    {
        get
        {
            var player = Services.PlayerState;
            if (!player.IsLoaded || string.IsNullOrEmpty(player.CharacterName))
                return null;
            return $"{player.CharacterName}@{FriendPresence.WorldName(player.HomeWorld.RowId)}";
        }
    }

    public string LocalName => Services.PlayerState.IsLoaded ? Services.PlayerState.CharacterName : "";

    public byte LocalJob => Services.PlayerState.IsLoaded ? (byte)Services.PlayerState.ClassJob.RowId : (byte)0;

    public Conversation? Active => ActiveKey == null ? null : Store.Get(ActiveKey);

    public bool CanSend(Conversation conversation) => conversation.Owner == Owner && !IsTest(conversation);

    public ContactPrefs? PrefsFor(Conversation conversation) => conversation.IsTell ? Store.PeekPrefs(conversation.ContactKey) : null;

    public string DisplayName(Conversation conversation)
    {
        var nickname = PrefsFor(conversation)?.Nickname;
        return string.IsNullOrWhiteSpace(nickname) ? conversation.Name : nickname;
    }

    public bool IsMuted(Conversation conversation) => PrefsFor(conversation)?.Muted == true;

    // Lodestone face for a thread's avatar (tells only; groups use a glyph).
    public LodestonePortraits.Face? PortraitOf(Conversation conversation) =>
        conversation.IsTell && !IsTest(conversation) ? Portraits.FaceFor(conversation.Name, conversation.World) : null;

    // Lodestone face beside a message: yours, the tell partner's, or the
    // group member who wrote it.
    public LodestonePortraits.Face? PortraitOf(Conversation conversation, ChatMessage message)
    {
        if (message.Outgoing)
        {
            var owner = conversation.Owner.Split('@', 2);
            return owner.Length == 2 ? Portraits.FaceFor(owner[0], owner[1]) : null;
        }
        return conversation.IsTell
            ? PortraitOf(conversation)
            : Portraits.FaceFor(message.Sender, message.SenderWorld);
    }

    public byte JobOf(Conversation conversation)
    {
        if (!conversation.IsTell)
            return 0;
        if (Friends.Get(conversation.ContactKey) is { Job: > 0 } friend)
            return friend.Job;
        if (seenJobs.TryGetValue(conversation.ContactKey, out var job))
            return job;
        return PrefsFor(conversation)?.Job ?? 0;
    }

    public IEnumerable<Conversation> Visible(ContactsTab tab)
    {
        var owner = Owner;
        return Store.Conversations.Where(c => tab switch
        {
            ContactsTab.Whispers => c.IsTell && !c.IsRequest,
            ContactsTab.Groups => !c.IsTell && Config.ShowGroupChats,
            ContactsTab.Requests => c.IsTell && c.IsRequest,
            _ => false,
        })
        // Threads from the current character first, then everything by recency.
        .OrderByDescending(c => c.Pinned)
        .ThenBy(c => c.Pinned ? c.PinOrder : 0)
        .ThenByDescending(c => c.Owner == owner)
        .ThenByDescending(c => c.LastActivity);
    }

    public int UnreadCount(ContactsTab tab) =>
        Visible(tab).Where(c => !IsMuted(c)).Sum(c => tab == ContactsTab.Requests ? (c.Unread > 0 ? 1 : 0) : c.Unread);

    public int RequestCount => Store.Conversations.Count(c => c.IsTell && c.IsRequest);

    // --- Game chat --------------------------------------------------------

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            switch (message.LogKind)
            {
                case XivChatType.TellIncoming:
                    OnTell(message, false);
                    break;
                case XivChatType.TellOutgoing:
                    OnTell(message, true);
                    break;
                case XivChatType.ErrorMessage or XivChatType.SystemMessage or (XivChatType)58:
                    OnError(message);
                    break;
                default:
                    if (Config.ShowGroupChats && GroupChannel(message.LogKind) is { } channel && GroupEnabled(channel.Kind))
                        OnGroup(message, channel.Kind, channel.Number);
                    break;
            }
        }
        catch (Exception e)
        {
            Services.Log.Error(e, "Could not record a chat message");
        }
    }

    private void OnTell(IHandleableChatMessage chat, bool outgoing)
    {
        var owner = Owner;
        if (owner == null)
            return;

        var (name, world) = ParseSender(chat.Sender);
        if (name.Length == 0)
            return;

        var text = chat.Message.TextValue;
        var raw = MessageLinks.RawIfLinked(chat.Message);
        var isNew = Store.Get(Conversation.TellKey(owner, name, world)) == null;
        var conversation = Store.GetOrCreateTell(owner, name, world);
        var prefs = Store.Prefs(conversation.ContactKey);
        if (FindJob(name, world) is > 0 and var job)
            prefs.Job = job;

        if (Config.HideTellsFromChat)
            chat.PreventOriginal();

        if (outgoing)
        {
            // Our own sends come back as an echo once the server accepts them.
            if (MatchEcho(conversation, text, raw))
                return;

            // Sent from the game's own chat box.
            conversation.IsRequest = false;
            Store.Append(conversation, new ChatMessage { Outgoing = true, Sender = LocalName, SenderJob = LocalJob, Text = text, Raw = raw });
            if (Config.AutoOpenOutgoing && !InCombat)
                OpenRequested?.Invoke(conversation.Key);
            return;
        }

        if (isNew && Config.EnableRequests && !Friends.IsFriend(conversation.ContactKey) && !InParty(name, world))
            conversation.IsRequest = true;

        Store.Append(conversation, new ChatMessage { Sender = name, SenderWorld = world, SenderJob = prefs.Job, Text = text, Raw = raw });
        ReplyToLastKey = conversation.Key;

        if (!IsReading(conversation))
            conversation.Unread++;

        // Requests wait quietly: no sound, no pop-up, no badge.
        if (conversation.IsRequest || prefs.Muted)
            return;

        LastIncoming = DateTime.UtcNow;
        LatestPreview = new IncomingPreview(conversation.Key, DisplayName(conversation), text, DateTime.UtcNow);
        if (!IsReading(conversation))
            AddToast(conversation, DisplayName(conversation), text);
        Alert();
        if (Config.AutoOpenIncoming && !InCombat && !WindowVisible)
            OpenRequested?.Invoke(conversation.Key);
    }

    private void OnGroup(IHandleableChatMessage chat, ChannelKind kind, int number)
    {
        var owner = Owner;
        if (owner == null)
            return;

        var (name, world) = ParseSender(chat.Sender);
        var text = chat.Message.TextValue;
        var raw = MessageLinks.RawIfLinked(chat.Message);
        var conversation = Store.GetOrCreateGroup(owner, kind, number, GroupLabel(kind, number));
        var outgoing = string.Equals(name, LocalName, StringComparison.OrdinalIgnoreCase);

        if (outgoing)
        {
            if (MatchEcho(conversation, text, raw))
                return;
            Store.Append(conversation, new ChatMessage { Outgoing = true, Sender = LocalName, SenderJob = LocalJob, Text = text, Raw = raw });
            return;
        }

        var mention = MentionsMe(text);
        Store.Append(conversation, new ChatMessage
        {
            Sender = name.Length > 0 ? name : chat.Sender.TextValue,
            SenderWorld = world,
            SenderJob = FindJob(name, world) ?? 0,
            Text = text,
            Raw = raw,
            Mention = mention,
        });

        if (!IsReading(conversation))
            conversation.Unread++;

        if (mention && Config.MentionAlerts)
        {
            LastIncoming = DateTime.UtcNow;
            LatestPreview = new IncomingPreview(conversation.Key, $"{name} in {conversation.Name}", text, DateTime.UtcNow);
            Alert();
        }
    }

    // Ties the echo to the oldest waiting send. It stays "Sending…" until
    // ConfirmDelay passes without an error, because the game echoes a tell
    // before it knows whether it arrived.
    private bool MatchEcho(Conversation conversation, string text, string? raw)
    {
        var pending = conversation.Messages.FirstOrDefault(m => m.Outgoing && m.State == SendState.Pending && m.EchoedAt == null);
        if (pending == null)
            return false;
        pending.EchoedAt = DateTime.UtcNow;
        pending.FailReason = null;
        pending.Time = DateTime.UtcNow;
        // The echo has placeholders like <item> and <flag> filled in.
        if (!pending.SentQuoted)
        {
            pending.Text = text;
            pending.Raw = raw;
        }
        Store.MarkDirty();
        return true;
    }

    // The game explains a failed tell (offline, busy, other data center,
    // restricted area…) with an error line right after the send. Attach it to
    // the send that's still waiting so the bubble can say why.
    // The game echoes a tell as soon as it's sent and only reports a failure
    // ("Message to Name could not be sent.") after the server answers, so the
    // bubble may already say Sent. The error names the recipient; match on
    // that, and fall back to the newest waiting send.
    private void OnError(IHandleableChatMessage chat)
    {
        var text = chat.Message.TextValue;
        var owner = Owner;
        var window = TimeSpan.FromSeconds(10);
        ChatMessage? newest = null;
        foreach (var conversation in Store.Conversations)
        {
            if (conversation.Owner != owner)
                continue;
            var named = conversation.IsTell && text.Contains(conversation.Name, StringComparison.OrdinalIgnoreCase);
            foreach (var message in conversation.Messages)
            {
                if (!message.Outgoing || message.State == SendState.Failed || DateTime.UtcNow - message.PendingSince > window)
                    continue;
                if (!named && !(message.State == SendState.Pending && DateTime.UtcNow - message.PendingSince < TimeSpan.FromSeconds(3)))
                    continue;
                if (newest == null || message.PendingSince > newest.PendingSince)
                    newest = message;
            }
        }
        if (newest == null)
            return;
        newest.State = SendState.Failed;
        newest.FailReason = text;
        Store.MarkDirty();
        // It's shown under the bubble instead of in the game's chat.
        chat.PreventOriginal();
    }

    // Why a tell to this person probably won't arrive, from what the friend
    // list knows. Null when there's nothing to warn about.
    public string? DeliveryWarning(Conversation conversation)
    {
        if (!conversation.IsTell || Friends.Get(conversation.ContactKey) is not { } friend)
            return null;
        if (friend.Presence == Presence.Offline)
            return "Offline, so tells can't be delivered.";
        if (friend.Presence == Presence.Busy)
            return "Set to Busy, so they may not get tells.";
        var localDc = Services.PlayerState.IsLoaded ? FriendPresence.DataCenterOf(Services.PlayerState.CurrentWorld.RowId) : 0;
        var theirDc = FriendPresence.DataCenterOf(friend.CurrentWorldId);
        if (localDc != 0 && theirDc != 0 && localDc != theirDc)
            return "On another data center, so tells can't reach them.";
        return null;
    }

    private void Alert()
    {
        if (Config.PlaySound)
            Notifier.PlaySound(Config.SoundEffect);
        if (Config.FlashTaskbar)
            Notifier.FlashTaskbar();
    }

    private bool IsReading(Conversation conversation) => WindowVisible && ActiveKey == conversation.Key;

    private void OnLogout(int type, int code)
    {
        if (Config.ClearOnLogout)
            Store.ClearAll();
        Store.Flush();
    }

    // --- Sending ----------------------------------------------------------

    public int MaxTextBytes(Conversation conversation) =>
        ChatSender.MaxMessageBytes - ChatSender.ByteCount(ChatSender.CommandFor(conversation)) - 1;

    public bool Send(Conversation conversation, string text, ChatMessage? replyTo = null)
    {
        text = ChatSender.Clean(text);
        if (text.Length == 0 || !CanSend(conversation))
            return false;

        var outgoingText = text;
        if (replyTo != null && Config.QuoteRepliesInGame)
            outgoingText = $"> {replyTo.Sender.Split(' ')[0]}: \"{Snippet(replyTo.Text, 40)}\" {text}";
        if (ChatSender.ByteCount(outgoingText) > MaxTextBytes(conversation))
            return false;

        var message = new ChatMessage
        {
            Outgoing = true,
            Sender = LocalName,
            SenderJob = LocalJob,
            Text = text,
            ReplyToSender = replyTo?.Outgoing == true ? "You" : replyTo?.Sender,
            ReplyToText = replyTo?.Text,
            State = SendState.Pending,
            PendingSince = DateTime.UtcNow,
            SentQuoted = outgoingText != text,
        };
        conversation.IsRequest = false;
        conversation.Draft = "";
        Store.Append(conversation, message);
        sender.Enqueue(conversation, outgoingText);
        if (Config.PlaySendSound)
            Notifier.PlayUiSound(Config.SendSoundEffect);
        return true;
    }

    public void Retry(Conversation conversation, ChatMessage message)
    {
        if (!CanSend(conversation))
            return;
        // Move it to the end so the echo matches it in order.
        conversation.Messages.Remove(message);
        message.State = SendState.Pending;
        message.FailReason = null;
        message.SentQuoted = false;
        message.PendingSince = DateTime.UtcNow;
        message.EchoedAt = null;
        message.Time = DateTime.UtcNow;
        Store.Append(conversation, message);
        sender.Enqueue(conversation, message.Text);
    }

    public void Discard(Conversation conversation, ChatMessage message)
    {
        conversation.Messages.Remove(message);
        Store.MarkDirty();
    }

    // --- Housekeeping -----------------------------------------------------

    public void MarkRead(Conversation conversation)
    {
        if (conversation.Unread == 0)
            return;
        conversation.Unread = 0;
        if (LatestPreview?.ConversationKey == conversation.Key)
            LatestPreview = null;
        toasts.RemoveAll(t => t.ConversationKey == conversation.Key);
        Store.MarkDirty();
    }

    public void MarkAllRead()
    {
        foreach (var conversation in Store.Conversations)
            conversation.Unread = 0;
        LatestPreview = null;
        toasts.Clear();
        Store.MarkDirty();
    }

    public void DismissPreview() => LatestPreview = null;

    public void DismissToast(Toast toast) => toasts.Remove(toast);

    private void AddToast(Conversation conversation, string sender, string text)
    {
        if (!Config.ShowToasts)
            return;
        // One per conversation, newest on top, three at most.
        toasts.RemoveAll(t => t.ConversationKey == conversation.Key);
        toasts.Insert(0, new Toast(++nextToastId, conversation.Key, sender, text, DateTime.UtcNow));
        if (toasts.Count > 3)
            toasts.RemoveRange(3, toasts.Count - 3);
    }

    private static readonly string[] TestLines =
    [
        "Kupo! This is a test tell from Tell Messenger.",
        "Hey, are you free for a roulette later?",
        "Did you see the new glamour set? It's so good.",
        "Test message, kupo. Click me to open the chat!",
    ];

    // A pretend incoming tell, for trying out sounds and pop-ups. It goes
    // through the same steps as a real one; the test contact can't be replied to.
    public void SendTestTell()
    {
        var owner = Owner;
        if (owner == null)
            return;
        var world = owner.Split('@', 2)[1];
        var conversation = Store.GetOrCreateTell(owner, TestName, world);
        conversation.IsRequest = false;
        var text = TestLines[Random.Shared.Next(TestLines.Length)];
        Store.Append(conversation, new ChatMessage { Sender = TestName, SenderWorld = world, Text = text });
        if (!IsReading(conversation))
        {
            conversation.Unread++;
            AddToast(conversation, TestName, text);
        }
        LastIncoming = DateTime.UtcNow;
        LatestPreview = new IncomingPreview(conversation.Key, TestName, text, DateTime.UtcNow);
        Alert();
    }

    public void OpenConversation(string? key) => OpenRequested?.Invoke(key);

    public void Update()
    {
        sender.Update();
        Friends.Update();
        Store.Update();
        if (DateTime.UtcNow - lastLabelRefresh > TimeSpan.FromSeconds(5))
        {
            lastLabelRefresh = DateTime.UtcNow;
            RefreshGroupLabels();
        }
        if (WindowVisible)
            Friends.RequestRefresh();

        var now = DateTime.UtcNow;
        foreach (var conversation in Store.Conversations)
        {
            foreach (var message in conversation.Messages)
            {
                if (message.State != SendState.Pending)
                    continue;
                if (message.EchoedAt is { } echoed)
                {
                    if (now - echoed > ConfirmDelay)
                    {
                        message.State = SendState.Sent;
                        Store.MarkDirty();
                    }
                }
                else if (now - message.PendingSince > SendTimeout)
                {
                    message.State = SendState.Failed;
                    Store.MarkDirty();
                }
            }
        }

        var inCombat = InCombat;
        if (inCombat && !wasInCombat && Config.HideOnCombat)
            HideRequested?.Invoke();
        wasInCombat = inCombat;

        if (Config.ToastSeconds > 0)
            toasts.RemoveAll(t => now - t.Time > TimeSpan.FromSeconds(Config.ToastSeconds));

        if (Config.PreviewDismissSeconds > 0 && LatestPreview != null
            && now - LatestPreview.Time > TimeSpan.FromSeconds(Config.PreviewDismissSeconds))
            LatestPreview = null;
    }

    public Conversation StartTell(string name, string world)
    {
        var owner = Owner ?? throw new InvalidOperationException("Not logged in");
        var conversation = Store.GetOrCreateTell(owner, name, world);
        conversation.IsRequest = false;
        conversation.LastActivity = DateTime.UtcNow;
        return conversation;
    }

    // --- Helpers ----------------------------------------------------------

    public static bool InCombat => Services.Condition[ConditionFlag.InCombat];

    public static string Snippet(string text, int length) => text.Length <= length ? text : text[..length].TrimEnd() + "...";

    private bool MentionsMe(string text)
    {
        var name = LocalName;
        if (name.Length == 0)
            return false;
        var first = name.Split(' ')[0];
        return Regex.IsMatch(text, $@"(?<![\p{{L}}]){Regex.Escape(first)}(?![\p{{L}}])", RegexOptions.IgnoreCase);
    }

    private static bool InParty(string name, string world)
    {
        foreach (var member in Services.Party)
        {
            if (member.Name.TextValue == name && FriendPresence.WorldName(member.World.RowId) == world)
                return true;
        }
        return false;
    }

    private byte? FindJob(string name, string world)
    {
        if (name.Length == 0)
            return null;
        var key = $"{name}@{world}";
        foreach (var player in Services.Objects.PlayerObjects)
        {
            if (player is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc
                && pc.Name.TextValue == name && FriendPresence.WorldName(pc.HomeWorld.RowId) == world)
                return seenJobs[key] = (byte)pc.ClassJob.RowId;
        }
        foreach (var member in Services.Party)
        {
            if (member.Name.TextValue == name && FriendPresence.WorldName(member.World.RowId) == world)
                return seenJobs[key] = (byte)member.ClassJob.RowId;
        }
        return seenJobs.TryGetValue(key, out var job) ? job : null;
    }

    public (string Name, string World) ParseSender(SeString senderText)
    {
        foreach (var payload in senderText.Payloads)
        {
            if (payload is PlayerPayload player && player.World.IsValid)
                return (player.PlayerName, player.World.Value.Name.ExtractText());
        }

        // Your own name, or a same-world player without a link: keep the
        // letters and use the world you are on.
        var builder = new StringBuilder();
        foreach (var c in senderText.TextValue)
        {
            if (char.IsLetter(c) || c is ' ' or '\'' or '-')
                builder.Append(c);
        }
        var name = builder.ToString().Trim();
        var world = Services.PlayerState.IsLoaded ? FriendPresence.WorldName(Services.PlayerState.CurrentWorld.RowId) : "";
        return (name, world);
    }

    private static (ChannelKind Kind, int Number)? GroupChannel(XivChatType type) => type switch
    {
        XivChatType.Party or XivChatType.CrossParty => (ChannelKind.Party, 0),
        XivChatType.Alliance => (ChannelKind.Alliance, 0),
        XivChatType.FreeCompany => (ChannelKind.FreeCompany, 0),
        XivChatType.NoviceNetwork => (ChannelKind.NoviceNetwork, 0),
        XivChatType.PvPTeam => (ChannelKind.PvPTeam, 0),
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => (ChannelKind.Linkshell, type - XivChatType.Ls1 + 1),
        XivChatType.CrossLinkShell1 => (ChannelKind.CrossLinkshell, 1),
        >= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8 => (ChannelKind.CrossLinkshell, type - XivChatType.CrossLinkShell2 + 2),
        _ => null,
    };

    private bool GroupEnabled(ChannelKind kind) => kind switch
    {
        ChannelKind.Party => Config.GroupParty,
        ChannelKind.Alliance => Config.GroupAlliance,
        ChannelKind.FreeCompany => Config.GroupFreeCompany,
        ChannelKind.Linkshell => Config.GroupLinkshells,
        ChannelKind.CrossLinkshell => Config.GroupCrossLinkshells,
        ChannelKind.NoviceNetwork => Config.GroupNoviceNetwork,
        ChannelKind.PvPTeam => Config.GroupPvPTeam,
        _ => false,
    };

    public static string GroupLabel(ChannelKind kind, int number) => kind switch
    {
        ChannelKind.Party => "Party",
        ChannelKind.Alliance => "Alliance",
        ChannelKind.FreeCompany => "Free Company",
        ChannelKind.Linkshell => LinkshellName(kind, number) ?? $"Linkshell {number}",
        ChannelKind.CrossLinkshell => LinkshellName(kind, number) ?? $"Cross-world Linkshell {number}",
        ChannelKind.NoviceNetwork => "Novice Network",
        ChannelKind.PvPTeam => "PvP Team",
        _ => kind.ToString(),
    };

    // "Linkshell 1" or "CWLS 1", shown under the linkshell's real name.
    public static string ChannelSlot(ChannelKind kind, int number) => kind switch
    {
        ChannelKind.Linkshell => $"Linkshell {number}",
        ChannelKind.CrossLinkshell => $"Cross-world Linkshell {number}",
        _ => "Group chat",
    };

    // The linkshell's name from the game, or null if it isn't loaded.
    private static unsafe string? LinkshellName(ChannelKind kind, int number)
    {
        try
        {
            string? name = null;
            if (kind == ChannelKind.Linkshell)
            {
                var chat = InfoProxyChat.Instance();
                if (chat != null)
                    name = chat->GetLinkShellName((uint)(number - 1)).ToString();
            }
            else if (kind == ChannelKind.CrossLinkshell)
            {
                var cwls = InfoProxyCrossWorldLinkshell.Instance();
                var text = cwls != null ? cwls->GetCrossworldLinkshellName((uint)(number - 1)) : null;
                if (text != null)
                    name = text->ToString();
            }
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception e)
        {
            Services.Log.Warning($"Could not read the name of {kind} {number}: {e.Message}");
            return null;
        }
    }

    // Picks up renamed or newly joined linkshells for the current character.
    private void RefreshGroupLabels()
    {
        var owner = Owner;
        foreach (var conversation in Store.Conversations)
        {
            if (conversation.Owner != owner || conversation.Kind is not (ChannelKind.Linkshell or ChannelKind.CrossLinkshell))
                continue;
            var label = GroupLabel(conversation.Kind, conversation.Channel);
            if (label != conversation.Name && !label.StartsWith("Linkshell ") && !label.StartsWith("Cross-world Linkshell "))
            {
                conversation.Name = label;
                Store.MarkDirty();
            }
        }
    }
}
