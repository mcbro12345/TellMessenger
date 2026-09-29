using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TellMessenger.Model;

public enum ChannelKind
{
    Tell,
    Party,
    Alliance,
    FreeCompany,
    Linkshell,
    CrossLinkshell,
    NoviceNetwork,
    PvPTeam,
}

public enum SendState
{
    Sent,
    Pending,
    Failed,
}

public sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Time { get; set; } = DateTime.UtcNow; // UTC
    public bool Outgoing { get; set; }
    public string Sender { get; set; } = "";
    public string SenderWorld { get; set; } = "";
    public byte SenderJob { get; set; }
    public string Text { get; set; } = "";

    // The message as the game sent it (base64 SeString), kept only when it
    // has links: map flags, items, players, or links added by other plugins.
    public string? Raw { get; set; }
    public string? ReplyToSender { get; set; }
    public string? ReplyToText { get; set; }
    public bool Mention { get; set; }
    public SendState State { get; set; }

    public string? FailReason { get; set; }

    [JsonIgnore] public DateTime PendingSince { get; set; }

    // When the game echoed the send. It only counts as Sent once no error
    // has followed for a moment, since failures arrive after the echo.
    [JsonIgnore] public DateTime? EchoedAt { get; set; }

    // Sent with a reply quote in front, so the game's echo doesn't match the
    // bubble's text.
    [JsonIgnore] public bool SentQuoted { get; set; }
}

// One thread: a tell partner or a group channel, seen from one of the
// player's characters (Owner). History is shared across characters, but only
// the character that owns a thread can send in it.
public sealed class Conversation
{
    public string Key { get; set; } = "";
    public string Owner { get; set; } = "";
    public ChannelKind Kind { get; set; }
    public int Channel { get; set; } // linkshell number, 1-based
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = [];
    public int Unread { get; set; }
    public bool Pinned { get; set; }
    public int PinOrder { get; set; }
    public bool IsRequest { get; set; }
    public string Draft { get; set; } = "";
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    [JsonIgnore] public bool IsTell => Kind == ChannelKind.Tell;
    [JsonIgnore] public string ContactKey => $"{Name}@{World}";

    public static string TellKey(string owner, string name, string world) => $"{owner}|tell|{name}@{world}";

    public static string GroupKey(string owner, ChannelKind kind, int channel) => $"{owner}|{kind}|{channel}";
}

// Per-person settings shared by every thread with that person.
public sealed class ContactPrefs
{
    public string? Nickname { get; set; }
    public string? Note { get; set; }
    public bool Muted { get; set; }
    public byte Job { get; set; }
    public DateTime? LastSeenOnline { get; set; }
}

public sealed class HistoryData
{
    public int Version { get; set; } = 1;
    public Dictionary<string, Conversation> Conversations { get; set; } = [];
    public Dictionary<string, ContactPrefs> Contacts { get; set; } = [];
}
