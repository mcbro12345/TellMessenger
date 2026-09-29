using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using TellMessenger.Game;
using TellMessenger.Model;

namespace TellMessenger.UI;

public sealed partial class MessengerWindow
{
    private string search = "";
    private string? menuContactKey;
    private bool openContactMenu;

    private void DrawContacts()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var tabsHeight = Gfx.S(34);

        // Search
        ImGui.SetCursorPos(ImGui.GetCursorPos() + Gfx.S(8, 8));
        ImGui.SetNextItemWidth(width - Gfx.S(16));
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, Gfx.S(10, 7)).Push(ImGuiStyleVar.FrameRounding, Gfx.S(8)))
            ImGui.InputTextWithHint("##search", "Search chats", ref search, 64);
        ImGui.Dummy(Gfx.S(0, 6));

        if (messenger.Tab == ContactsTab.Groups && !config.ShowGroupChats)
            messenger.Tab = ContactsTab.Whispers;
        if (messenger.Tab == ContactsTab.Requests && !ShowRequestsTab)
            messenger.Tab = ContactsTab.Whispers;

        var listHeight = ImGui.GetContentRegionAvail().Y - tabsHeight;
        using (var list = ImRaii.Child("##contactList", new Vector2(width, listHeight), false))
        {
            if (list)
                DrawContactRows();
        }

        DrawTabs(width, tabsHeight);

        if (openContactMenu)
        {
            openContactMenu = false;
            ImGui.OpenPopup("##contactMenu");
        }
        DrawContactMenu();
    }

    private bool ShowRequestsTab => config.EnableRequests || messenger.RequestCount > 0;

    private void DrawContactRows()
    {
        var rows = messenger.Visible(messenger.Tab).Where(Matches).ToList();
        if (rows.Count == 0)
        {
            ImGui.Dummy(Gfx.S(0, 20));
            var empty = messenger.Tab switch
            {
                ContactsTab.Groups => "No group chats yet.\nJoin a party or say something\nin a linkshell to see it here.",
                ContactsTab.Requests => "No message requests.",
                _ => search.Length > 0 ? "No matches." : "No conversations yet.",
            };
            foreach (var line in empty.Split('\n'))
                CenteredDisabled(line);
            return;
        }

        foreach (var conversation in rows)
            DrawContactRow(conversation);
    }

    private static void CenteredDisabled(string text)
    {
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - size.X) / 2);
        ImGui.TextDisabled(text);
    }

    private bool Matches(Conversation conversation)
    {
        if (search.Length == 0)
            return true;
        var prefs = messenger.PrefsFor(conversation);
        return conversation.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
               || prefs?.Nickname?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
               || prefs?.Note?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
               || conversation.Messages.Any(m => m.Text.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private void DrawContactRow(Conversation conversation)
    {
        using var id = ImRaii.PushId(conversation.Key);
        var p = Theme.Current;
        var width = ImGui.GetContentRegionAvail().X;
        var height = Gfx.S(58);
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        var list = ImGui.GetWindowDrawList();

        var clicked = ImGui.InvisibleButton("##row", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            menuContactKey = conversation.Key;
            openContactMenu = true;
        }
        if (clicked)
            Select(conversation.Key);

        var selected = messenger.ActiveKey == conversation.Key;
        if (selected)
            list.AddRectFilled(min, max, Theme.U32(p.ContactSelected));
        else if (conversation.Pinned)
            list.AddRectFilled(min, max, Theme.U32(p.ContactPinned));
        if (hovered && !selected)
            list.AddRectFilled(min, max, Theme.U32(p.ContactHover));
        if (selected)
            list.AddRectFilled(min, new Vector2(min.X + Gfx.S(2), max.Y), Theme.U32(p.Accent));

        // Avatar and status
        var avatarSize = Gfx.S(38);
        var avatarMin = min + new Vector2(Gfx.S(10), (height - avatarSize) / 2);
        var job = messenger.JobOf(conversation);
        Gfx.Avatar(list, avatarMin, avatarSize, job, conversation.Kind, messenger.PortraitOf(conversation));
        var friend = conversation.IsTell ? messenger.Friends.Get(conversation.ContactKey) : null;
        if (friend != null)
            Gfx.StatusDot(list, avatarMin + new Vector2(avatarSize - Gfx.S(5), avatarSize - Gfx.S(5)), Gfx.S(5), friend.Presence);

        var textX = avatarMin.X + avatarSize + Gfx.S(10);
        var rightEdge = max.X - Gfx.S(10);
        var lineHeight = ImGui.GetTextLineHeight();

        // Right column: age, pin/mute, unread badge
        var last = conversation.Messages.LastOrDefault();
        var age = Gfx.Age(last?.Time ?? conversation.LastActivity, config);
        var ageSize = ImGui.CalcTextSize(age);
        Gfx.Text(list, new Vector2(rightEdge - ageSize.X, min.Y + Gfx.S(8)), Theme.U32(conversation.Unread > 0 ? p.Accent : p.Timestamp), age);
        var iconX = rightEdge - Gfx.S(7);
        if (conversation.Pinned)
        {
            Gfx.Icon(list, FontAwesomeIcon.Thumbtack, new Vector2(iconX, min.Y + height - Gfx.S(16)), p.TextSecondary);
            iconX -= Gfx.S(18);
        }
        if (messenger.IsMuted(conversation))
        {
            Gfx.Icon(list, FontAwesomeIcon.BellSlash, new Vector2(iconX, min.Y + height - Gfx.S(16)), p.TextSecondary);
            iconX -= Gfx.S(18);
        }
        if (conversation.Unread > 0 && !messenger.IsMuted(conversation))
        {
            Gfx.Badge(list, new Vector2(iconX - Gfx.S(4), min.Y + height - Gfx.S(16)), conversation.Unread);
            iconX -= Gfx.S(28);
        }

        var textRight = MathF.Min(rightEdge - ageSize.X - Gfx.S(6), rightEdge);
        using (Gfx.Clip(new Vector2(textX, min.Y), new Vector2(textRight, max.Y), true))
        {
            var name = messenger.DisplayName(conversation);
            Gfx.Text(list, new Vector2(textX, min.Y + Gfx.S(6)), Theme.U32(p.TextEmphasis), Gfx.Fit(name, textRight - textX));
        }

        // Second line: where they are, or which of your characters this is.
        var subtitle = Subtitle(conversation, friend);
        using (Gfx.Clip(new Vector2(textX, min.Y), new Vector2(iconX, max.Y), true))
        {
            var small = Gfx.SmallHeight;
            Gfx.SmallText(list, new Vector2(textX, min.Y + Gfx.S(6) + lineHeight), Theme.U32(p.TextSecondary), Gfx.FitSmall(subtitle, iconX - textX - Gfx.S(4)));

            var (preview, previewColor) = Preview(conversation, last);
            Gfx.SmallText(list, new Vector2(textX, min.Y + Gfx.S(6) + lineHeight + small + Gfx.S(1)),
                Theme.U32(previewColor), Gfx.FitSmall(preview, iconX - textX - Gfx.S(4)));
        }
    }

    private string Subtitle(Conversation conversation, FriendInfo? friend)
    {
        var via = conversation.Owner != messenger.Owner ? $"via {conversation.Owner.Split('@')[0]}" : null;
        string where;
        if (!conversation.IsTell)
            where = conversation.Owner.Split('@')[0];
        else if (conversation.IsRequest)
            where = "Not a friend or party member";
        else if (friend is { Presence: not Presence.Offline } && friend.Location.Length > 0)
            where = friend.Location;
        else
            where = conversation.World;
        return via != null && conversation.IsTell ? $"{where} · {via}" : where;
    }

    private (string Text, Vector4 Color) Preview(Conversation conversation, ChatMessage? last)
    {
        var p = Theme.Current;
        if (conversation.Draft.Length > 0 && conversation.Key != messenger.ActiveKey)
            return ($"Draft: {conversation.Draft}", p.Accent);
        if (last == null)
            return ("", p.TextSecondary);
        if (config.HidePreview)
            return (conversation.Unread > 0 ? $"{conversation.Unread} unread" : "", p.TextSecondary);

        var text = last.Text;
        if (last.Outgoing)
            text = $"You: {text}";
        else if (!conversation.IsTell)
            text = $"{last.Sender.Split(' ')[0]}: {text}";
        return (text.Replace('\n', ' '), conversation.Unread > 0 ? p.Text : p.TextSecondary);
    }

    private void DrawTabs(float width, float height)
    {
        var p = Theme.Current;
        var min = ImGui.GetCursorScreenPos();
        var list = ImGui.GetWindowDrawList();
        list.AddLine(min, min + new Vector2(width, 0), Theme.U32(p.Divider));

        var tabs = new[] { ContactsTab.Whispers, ContactsTab.Groups, ContactsTab.Requests }
            .Where(t => t switch
            {
                ContactsTab.Groups => config.ShowGroupChats,
                ContactsTab.Requests => ShowRequestsTab,
                _ => true,
            }).ToList();
        var tabWidth = width / tabs.Count;

        foreach (var tab in tabs)
        {
            var tabMin = min + new Vector2(tabWidth * tabs.IndexOf(tab), 0);
            ImGui.SetCursorScreenPos(tabMin);
            if (ImGui.InvisibleButton($"##tab{tab}", new Vector2(tabWidth, height)))
                messenger.Tab = tab;
            var hovered = ImGui.IsItemHovered();
            var active = messenger.Tab == tab;

            var label = tab switch { ContactsTab.Whispers => "Tells", ContactsTab.Groups => "Linkshells", _ => tab.ToString() };
            var count = tab == ContactsTab.Requests ? messenger.RequestCount : messenger.UnreadCount(tab);
            var labelSize = ImGui.CalcTextSize(label);
            var badgeWidth = count > 0 ? Gfx.S(24) : 0;
            var start = tabMin + new Vector2((tabWidth - labelSize.X - badgeWidth) / 2, (height - labelSize.Y) / 2);
            Gfx.Text(list, start, Theme.U32(active ? p.Accent : hovered ? p.Text : p.TextSecondary), label);
            if (count > 0)
                Gfx.Badge(list, new Vector2(start.X + labelSize.X + Gfx.S(14), tabMin.Y + height / 2), count);
            if (active)
                list.AddRectFilled(new Vector2(start.X - Gfx.S(4), tabMin.Y + height - Gfx.S(3)),
                    new Vector2(start.X + labelSize.X + Gfx.S(4), tabMin.Y + height - Gfx.S(1)), Theme.U32(p.Accent), Gfx.S(1));
        }

        ImGui.SetCursorScreenPos(min + new Vector2(0, height));
        ImGui.Dummy(Vector2.Zero);
    }

    private void DrawContactMenu()
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Gfx.S(8, 8)).Push(ImGuiStyleVar.ItemSpacing, Gfx.S(8, 4));
        using var popup = ImRaii.Popup("##contactMenu");
        if (!popup || menuContactKey == null)
            return;
        var conversation = messenger.Store.Get(menuContactKey);
        if (conversation == null)
            return;

        DrawContactMenuItems(conversation);
    }

    // Shared by the contact list and the conversation header's "more" button.
    private void DrawContactMenuItems(Conversation conversation)
    {
        ImGui.TextDisabled(messenger.DisplayName(conversation));
        ImGui.Separator();

        if (ImGui.MenuItem(conversation.Pinned ? "Unpin" : "Pin to top"))
        {
            conversation.Pinned = !conversation.Pinned;
            conversation.PinOrder = conversation.Pinned
                ? messenger.Store.Conversations.Where(c => c.Pinned).Select(c => c.PinOrder).DefaultIfEmpty(0).Max() + 1
                : 0;
            messenger.Store.MarkDirty();
        }
        if (conversation.Pinned)
        {
            if (ImGui.MenuItem("Move up"))
                MovePinned(conversation, -1);
            if (ImGui.MenuItem("Move down"))
                MovePinned(conversation, 1);
        }

        if (conversation.IsTell)
        {
            var prefs = messenger.Store.Prefs(conversation.ContactKey);
            if (ImGui.MenuItem(prefs.Muted ? "Unmute" : "Mute"))
            {
                prefs.Muted = !prefs.Muted;
                messenger.Store.MarkDirty();
            }
            if (ImGui.MenuItem("Set nickname…"))
                BeginEdit(conversation, false);
            if (ImGui.MenuItem("Edit note…"))
                BeginEdit(conversation, true);
            if (conversation.IsRequest && ImGui.MenuItem("Accept"))
            {
                conversation.IsRequest = false;
                messenger.Store.MarkDirty();
            }
        }

        ImGui.Separator();
        if (conversation.Unread > 0)
        {
            if (ImGui.MenuItem("Mark as read"))
                messenger.MarkRead(conversation);
        }
        else if (conversation.Messages.Any(m => !m.Outgoing) && ImGui.MenuItem("Mark as unread"))
        {
            conversation.Unread = Math.Max(1, conversation.Messages.Count - conversation.Messages.FindLastIndex(m => m.Outgoing) - 1);
            if (messenger.ActiveKey == conversation.Key)
                messenger.ActiveKey = null;
            messenger.Store.MarkDirty();
        }

        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Current.Danger))
        {
            if (ImGui.MenuItem(conversation.IsRequest ? "Delete" : "Remove"))
            {
                if (messenger.ActiveKey == conversation.Key)
                    messenger.ActiveKey = null;
                messenger.Store.Remove(conversation.Key);
            }
        }
    }

    private void MovePinned(Conversation conversation, int direction)
    {
        var pinned = messenger.Visible(messenger.Tab).Where(c => c.Pinned).ToList();
        var index = pinned.IndexOf(conversation);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= pinned.Count)
            return;
        (pinned[index], pinned[target]) = (pinned[target], pinned[index]);
        for (var i = 0; i < pinned.Count; i++)
            pinned[i].PinOrder = i + 1;
        messenger.Store.MarkDirty();
    }
}
