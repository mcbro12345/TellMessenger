# Tell Messenger

**Never lose a tell again.** A Dalamud plugin for FFXIV that puts every tell in one messenger window, with history, online status and unread badges. It's a port of the World of Warcraft addon [WhisperMessenger](https://github.com/F0rty-Tw0/WhisperMessenger), adapted to how FFXIV works.

## Features

### Messenger-style conversations
- Each person gets their own thread with chat bubbles, timestamps, date separators and a "New messages" line.
- **Links work**
- **Drafts** stay with each chat, even after a restart.
- An emoticon picker shows text faces and symbols the game's font can display.

### Contact list
- Online status dots, job icons, last-message previews
- Pin contacts and reorder pinned ones. Search across names, nicknames, notes and message history.
- Right-click a contact to **mute** them, set a **nickname**, and add a private **note**.

### Tells, Linkshells and Requests
- **Tells** holds your private conversations.
- **Linkshells** collects your linkshells and cross-world linkshells under their real names. Party, alliance, Free Company, PvP team and Novice Network chat can be added in Settings. Messages that mention your name are highlighted.
- **Requests** (optional) holds tells from players who aren't your friends or party members until you accept or delete them.

### Warnings before you send
FFXIV only delivers tells to players who are online, not set to Busy, and on your data center. When the friend list shows any of those, the message box tells you before you send.

### Themes and size
Five themes: **Midnight**, **The Void**, **Thanalan**, **Gridania** and **Eorzea**. Separate bubble color presets and sent-text colors are available too. Text uses the game's own font at its built-in sizes (small, medium, large) so it stays sharp, and the window scales from 75% to 150%.

### Lodestone portraits
Avatars are each person's Lodestone face picture, in their current gear and headwear, and that includes you. Pictures are looked up by name and home world on the Lodestone once per session and kept in memory only (nothing is saved to disk). Each one is shrunk to the exact size it is drawn at so it stays sharp. Job icons show until a picture arrives, or if someone has no Lodestone profile. You can turn this off under Settings > Appearance.

### Notifications
- An optional pop-up in the top-right corner for each new tell (off by default). Click it to open the chat; it fades after a few seconds. Settings > Notifications has a **Send test tell** button to try it.
- Choose any of the game's `<se.1>` to `<se.16>` sounds.
- Optional taskbar flash when you're tabbed out.
- A floating button shows an unread badge and a preview of the newest message. The server info bar entry (the game's boxed T icon with a boxed unread count) does the same job if you prefer it.

### Auto-open
The game's own **Send Tell** (on a player's right-click menu, the friend and party lists, and Chat 2's name menu) opens the conversation in the messenger instead of the game's chat box. The messenger also opens when you send a tell from the game's chat box, and optionally when you get one, but never on its own during combat.

### Linking
Messages go through the game's own chat box, so FFXIV's placeholders work: use **Link** on an item and type `<item>`, or use `<flag>`, `<pos>`, `<t>` and the rest.

## Commands

- `/tmsg` - open or close the messenger (set your own key in Settings → Behavior → Keybinds)
- `/tmsg First Last@World` - start a conversation
- `/tmsg settings` - open settings
- `/tmr` - open the conversation with whoever last sent you a tell

## Installation Instructions

1. Open the game chat and type `/xlsettings`, then click the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL into the empty box at the bottom:
   ```
   https://raw.githubusercontent.com/mcbro12345/DalamudPlugins/main/pluginmaster.json
   ```
3. Click the **+** button to add it, then **Save and Close**.
4. Type `/xlplugins` to open the Plugin Installer, search for "Tell Messenger," and click **Install**.

That's it. Updates will show up in the Plugin Installer automatically.

## Changes from WhisperMessenger

| WhisperMessenger (WoW) | Tell Messenger (FFXIV) | Why |
| --- | --- | --- |
| Typing indicator, "Seen" receipts, reactions, "Uses WM" tag | Removed. Reply quotes are kept on your side, or optionally sent as text | WoW addons can send hidden addon messages. FFXIV plugins have no equivalent channel, so these would only be visible to you. |
| Battle.net friends and whispers | Removed | FFXIV has no cross-game friend system. |
| Class colors and faction | Job icons (or Lodestone portraits); names stay one color | |
| Online status from friends and Battle.net | Online, Away and Busy status, zone or duty, and "visiting" world from the friend list | The friend list only refreshes when the game requests it. The plugin asks at most once a minute while the window is open. |
| Shift-click linking | FFXIV placeholders: `<item>`, `<flag>`, `<pos>`, `<t>` | This is how the game links things. |
| Mythic+ and rated PvP lockdown | Removed | FFXIV doesn't block tells in content. |
| Filtered-message reveal and profanity toggle | Removed | FFXIV has its own profanity filter setting. |
| Guild, party, raid and instance chat | Party, alliance, Free Company, linkshells, cross-world linkshells, PvP team and Novice Network | |
| Minimap button | Server info bar entry | FFXIV has no minimap button area for plugins. |
| Native WoW HUD skin and SharedMedia fonts | Removed. The "Eorzea" theme uses gold accents instead | |
| Emoji picker | Emoticon picker with characters the game font can show | FFXIV chat can't display emoji. |
| 12 interface languages | English only | Job and zone names still come from the game in your client language. |
| Not in WhisperMessenger | Warnings for offline, Busy and other-data-center players, the game's error shown on failed sends, and cross-world `Name@World` addressing | These are FFXIV-specific delivery rules. |

## Credits

Tell Messenger wouldn't exist without:

- [WhisperMessenger](https://github.com/F0rty-Tw0/WhisperMessenger) by F0rty-Tw0 and contributors - MIT

Full details in `THIRD_PARTY_NOTICES.md`.

## Contributing

Issues and PRs are welcome - see `CONTRIBUTING.md`.

## AI-assisted development

Parts of this codebase were built with AI coding tools. See `AI-GENERATED-NOTICE.md` for details.

## Disclaimer

Tell Messenger is an unofficial, fan-made project. It's not affiliated with or endorsed by Square Enix or the Dalamud project. FINAL FANTASY XIV and related trademarks belong to their respective owners.

## License

MIT - see `LICENSE`. Third-party components keep their own licenses (see `THIRD_PARTY_NOTICES.md`).
