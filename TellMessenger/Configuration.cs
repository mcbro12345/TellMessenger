using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace TellMessenger;

public enum IconMode
{
    Widget,
    ServerInfoBar,
    Both,
}

public enum PreviewSide
{
    Right,
    Left,
    Above,
    Below,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    // General
    public int MaxMessagesPerContact { get; set; } = 200;
    public int MaxContacts { get; set; } = 100;
    public int RetentionHours { get; set; } = 720; // 0 keeps messages forever
    public bool ClearOnLogout { get; set; }
    public bool HidePreview { get; set; }
    public bool Use24HourTime { get; set; }
    public bool UseServerTime { get; set; }

    // Appearance
    public string Theme { get; set; } = "midnight";
    public string BubblePreset { get; set; } = "theme";
    public string SentTextColor { get; set; } = "default";
    public float FontSize { get; set; } = 12f; // points: 12, 14 or 18
    public float WindowScale { get; set; } = 1f;
    public float OpacityActive { get; set; } = 1f;
    public float OpacityInactive { get; set; } = 0.9f;
    public float ContactsWidth { get; set; } = 192f;

    // Behavior
    public bool DimWhenMoving { get; set; }
    public bool AutoFocusInput { get; set; } = true;
    public bool HideTellsFromChat { get; set; }
    public bool AutoOpenIncoming { get; set; }
    public bool AutoOpenOutgoing { get; set; } = true;
    public bool HideOnCombat { get; set; }
    public bool ShowGroupChats { get; set; } = true;
    public bool GroupParty { get; set; }
    public bool GroupAlliance { get; set; }
    public bool GroupFreeCompany { get; set; }
    public bool GroupLinkshells { get; set; } = true;
    public bool GroupCrossLinkshells { get; set; } = true;
    public bool GroupNoviceNetwork { get; set; }
    public bool GroupPvPTeam { get; set; }
    public bool EnableRequests { get; set; }
    public bool QuoteRepliesInGame { get; set; }
    public bool OpenOnGameSendTell { get; set; } = true;
    public bool UseLodestonePortraits { get; set; } = true;
    public Keybind ToggleKey { get; set; } = new();
    public Keybind ReplyKey { get; set; } = new();

    // Notifications
    public bool PlaySound { get; set; } = true;
    public int SoundEffect { get; set; } = 5; // <se.5>
    public bool FlashTaskbar { get; set; } = true;
    public bool PlaySendSound { get; set; } = true;
    public int SendSoundEffect { get; set; } = 6; // game UI sound id
    public bool MentionAlerts { get; set; } = true;

    // Icons
    public IconMode IconMode { get; set; } = IconMode.Widget;
    public float IconSize { get; set; } = 40f;
    public bool LockIcon { get; set; }
    public bool DesaturateIdle { get; set; } = true;
    public float WidgetIdleOpacity { get; set; } = 0.8f;
    public bool ShowBadge { get; set; } = true;
    public bool BadgePulse { get; set; } = true;
    public bool ShowWidgetPreview { get; set; } = true;
    public int PreviewDismissSeconds { get; set; } = 8; // 0 keeps it until read
    public bool ShowToasts { get; set; }
    public int ToastSeconds { get; set; } = 6; // 0 keeps them until clicked
    public PreviewSide PreviewSide { get; set; } = PreviewSide.Right;
    public Vector2 WidgetPosition { get; set; } = new(120, 420);
    // The messenger window, restored on the next session (size unscaled).
    public Vector2? WindowPosition { get; set; }
    public Vector2? WindowSize { get; set; }

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}

[Serializable]
public sealed class Keybind
{
    public int Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }

    public override string ToString()
    {
        if (Key == 0) return "Not set";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(((Dalamud.Game.ClientState.Keys.VirtualKey)Key).ToString().Replace("KEY_", ""));
        return string.Join("+", parts);
    }
}
