using System;
using System.Collections.Generic;
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
    private const int PageSize = 150;

    // Emoticons that the game's chat font can show.
    private static readonly string[] Emoticons =
    [
        ":)", ":D", ";)", ":P", ":(", ":O", "xD", "<3",
        "^^", "^_^", "o/", "\\o/", "T_T", ">_<", "-_-", "o7",
        "★", "☆", "♪", "♫", "→", "←", "…", "°",
    ];

    private ChatMessage? replyTo;
    private ChatMessage? menuMessage;
    private bool openMessageMenu;
    private bool focusComposer;
    private bool scrollToBottom;
    // After switching threads, the new one is laid out off-screen for a couple
    // of frames while its scroll settles at the bottom (ImGui applies a scroll
    // one frame late); the previous thread stays on screen meanwhile.
    private int settleFrames;
    private string? settlingFrom;
    private string? unreadAnchorId;
    private string? lastOutgoingId;
    private int shownCount = PageSize;
    private int lastMessageCount;
    private string? composerError;

    public void Select(string key)
    {
        var conversation = messenger.Store.Get(key);
        if (conversation == null)
            return;

        if (messenger.ActiveKey != key)
            settlingFrom = messenger.ActiveKey;
        if (messenger.ActiveKey != key)
        {
            replyTo = null;
            composerError = null;
            shownCount = PageSize;
            unreadAnchorId = conversation.Unread > 0 && conversation.Unread <= conversation.Messages.Count
                ? conversation.Messages[^conversation.Unread].Id
                : null;
        }

        messenger.ActiveKey = key;
        messenger.Tab = conversation.IsTell ? (conversation.IsRequest ? ContactsTab.Requests : ContactsTab.Whispers) : ContactsTab.Groups;
        scrollToBottom = true;
        settleFrames = 2;
        focusComposer = config.AutoFocusInput;
    }

    private void DrawConversation()
    {
        var conversation = messenger.Active;
        if (conversation == null)
        {
            DrawWelcome();
            return;
        }

        if (IsOpen)
            messenger.MarkRead(conversation);

        DrawHeader(conversation);

        var composerHeight = Gfx.S(52);
        var bannerHeight = replyTo != null ? Gfx.S(34) : 0;
        var requestHeight = conversation.IsRequest ? Gfx.S(40) : 0;
        var transcriptHeight = ImGui.GetContentRegionAvail().Y - composerHeight - bannerHeight - requestHeight;

        var previous = settleFrames > 0 && settlingFrom != null ? messenger.Store.Get(settlingFrom) : null;
        if (settleFrames > 0)
            settleFrames--;
        var transcriptPos = ImGui.GetCursorScreenPos();
        if (previous != null)
        {
            // Keep showing the old thread, and lay the new one out invisibly in the
            // same spot, so it appears already scrolled to the bottom.
            // Drawing the old thread mustn't use up the new one's scroll request.
            var (keepScroll, keepCount) = (scrollToBottom, lastMessageCount);
            DrawTranscriptChild(previous, transcriptHeight, false);
            (scrollToBottom, lastMessageCount) = (keepScroll, keepCount);
            var after = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(transcriptPos);
            DrawTranscriptChild(conversation, transcriptHeight, true);
            ImGui.SetCursorScreenPos(after);
        }
        else
        {
            DrawTranscriptChild(conversation, transcriptHeight, false);
        }

        if (conversation.IsRequest)
            DrawRequestBar(conversation);
        if (replyTo != null)
            DrawReplyBanner();
        DrawComposer(conversation, composerHeight);

        if (openMessageMenu)
        {
            openMessageMenu = false;
            ImGui.OpenPopup("##messageMenu");
        }
        DrawMessageMenu(conversation);
    }

    private void DrawWelcome()
    {
        var region = ImGui.GetContentRegionAvail();
        var groups = messenger.Tab == ContactsTab.Groups;
        var title = groups ? "Linkshells" : "Welcome to Tell Messenger";
        var body = groups
            ? "Party, alliance, Free Company and linkshell chats\nshow up here. Pick a chat on the left."
            : "Pick a conversation on the left, or start a new one.";

        var lines = body.Split('\n');
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        ImGui.SetCursorPosY(region.Y / 2 - lineHeight * (lines.Length + 3) / 2);

        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Current.TextEmphasis))
            Centered(title);
        ImGui.Dummy(Gfx.S(0, 6));
        foreach (var line in lines)
            CenteredDisabled(line);

        if (!groups)
        {
            ImGui.Dummy(Gfx.S(0, 12));
            var label = "Start New Tell";
            var size = ImGui.CalcTextSize(label) + Gfx.S(28, 14);
            ImGui.SetCursorPosX((region.X - size.X) / 2);
            using var color = ImRaii.PushColor(ImGuiCol.Button, Theme.Current.Accent with { W = 0.85f })
                .Push(ImGuiCol.ButtonHovered, Theme.Current.Accent)
                .Push(ImGuiCol.Text, Theme.Current.Surface with { W = 1 });
            if (ImGui.Button(label, size))
                openNewChat = true;
        }
    }

    private static void Centered(string text)
    {
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - size.X) / 2);
        ImGui.TextUnformatted(text);
    }

    // --- Header -----------------------------------------------------------------

    private void DrawHeader(Conversation conversation)
    {
        var p = Theme.Current;
        var height = Gfx.S(64);
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var list = ImGui.GetWindowDrawList();
        list.AddRectFilled(min, min + new Vector2(width, height), Theme.U32(p.Chrome));
        list.AddLine(min + new Vector2(0, height), min + new Vector2(width, height), Theme.U32(p.Divider));

        var job = messenger.JobOf(conversation);
        var friend = conversation.IsTell ? messenger.Friends.Get(conversation.ContactKey) : null;
        var avatarSize = Gfx.S(42);
        var avatarMin = min + new Vector2(Gfx.S(16), (height - avatarSize) / 2);
        Gfx.Avatar(list, avatarMin, avatarSize, job, conversation.Kind, messenger.PortraitOf(conversation));
        if (friend != null)
            Gfx.StatusDot(list, avatarMin + new Vector2(avatarSize - Gfx.S(6), avatarSize - Gfx.S(6)), Gfx.S(5), friend.Presence);

        var x = avatarMin.X + avatarSize + Gfx.S(12);
        var lineHeight = ImGui.GetTextLineHeight();
        var small = Gfx.SmallHeight;
        var y = min.Y + (height - lineHeight - small * 2 - Gfx.S(2)) / 2;

        var name = messenger.DisplayName(conversation);
        Gfx.Text(list, new Vector2(x, y), Theme.U32(p.TextEmphasis), name);
        var nameWidth = ImGui.CalcTextSize(name).X;
        var tagX = x + nameWidth + Gfx.S(8);
        var prefs = messenger.PrefsFor(conversation);
        if (prefs?.Nickname != null)
        {
            Gfx.Text(list, new Vector2(tagX, y), Theme.U32(p.TextSecondary), conversation.Name);
            tagX += ImGui.CalcTextSize(conversation.Name).X + Gfx.S(8);
        }
        if (friend != null)
            Gfx.Text(list, new Vector2(tagX, y), Theme.U32(p.Accent), "(Friend)");

        string line2, line3;
        if (conversation.IsTell)
        {
            var jobName = Gfx.JobName(job);
            line2 = jobName.Length > 0 ? $"{jobName}  ·  {conversation.World}" : conversation.World;
            if (friend != null)
            {
                line3 = Gfx.PresenceLabel(friend.Presence);
                if (friend.Presence != Presence.Offline && friend.Location.Length > 0)
                    line3 += $"  ·  {friend.Location}";
                if (friend.Presence != Presence.Offline && friend.CurrentWorld.Length > 0 && friend.CurrentWorld != conversation.World)
                    line3 += $"  ·  visiting {friend.CurrentWorld}";
            }
            else if (conversation.IsRequest)
            {
                line3 = "Not a friend or party member.";
            }
            else
            {
                line3 = "Not on your friend list";
            }
        }
        else
        {
            line2 = conversation.Kind switch
            {
                ChannelKind.Party or ChannelKind.Alliance when Services.Party.Length > 0 => $"{Services.Party.Length} members",
                ChannelKind.Party or ChannelKind.Alliance => "Not in a party",
                _ => Messenger.ChannelSlot(conversation.Kind, conversation.Channel),
            };
            line3 = $"as {conversation.Owner.Split('@')[0]}";
        }
        if (Messenger.IsTest(conversation))
            line3 = "Test contact  ·  can't be replied to";
        else if (!messenger.CanSend(conversation))
            line3 = $"via {conversation.Owner.Split('@')[0]}  ·  read-only";

        var presenceColor = friend != null ? Gfx.PresenceColor(friend.Presence) ?? p.TextSecondary : p.TextSecondary;
        Gfx.SmallText(list, new Vector2(x, y + lineHeight + Gfx.S(1)), Theme.U32(p.TextSecondary), line2);
        Gfx.SmallText(list, new Vector2(x, y + lineHeight + small + Gfx.S(2)),
            Theme.U32(friend != null ? presenceColor with { W = 0.9f } : p.TextSecondary), line3);

        if (prefs?.Note is { } note)
        {
            var noteText = $"Note: {Messenger.Snippet(note, 60)}";
            var noteSize = Gfx.SmallSize(noteText);
            var noteX = MathF.Max(x + Gfx.S(220), min.X + width - Gfx.S(70) - noteSize.X);
            if (noteX + noteSize.X < min.X + width - Gfx.S(64))
                Gfx.SmallText(list, new Vector2(noteX, y + lineHeight + Gfx.S(1)), Theme.U32(p.TextSystem), noteText);
        }

        // Pin and "more" buttons
        ImGui.SetCursorScreenPos(min + new Vector2(width - Gfx.S(66), (height - Gfx.S(26)) / 2));
        if (Gfx.IconButton("##pin", FontAwesomeIcon.Thumbtack, conversation.Pinned ? "Unpin" : "Pin to top",
                conversation.Pinned ? p.Accent : null))
        {
            conversation.Pinned = !conversation.Pinned;
            conversation.PinOrder = conversation.Pinned ? messenger.Store.Conversations.Select(c => c.PinOrder).DefaultIfEmpty(0).Max() + 1 : 0;
            messenger.Store.MarkDirty();
        }
        ImGui.SameLine(0, Gfx.S(4));
        if (Gfx.IconButton("##more", FontAwesomeIcon.EllipsisV, "More"))
        {
            menuContactKey = conversation.Key;
            ImGui.OpenPopup("##headerMenu");
        }
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Gfx.S(8, 8)).Push(ImGuiStyleVar.ItemSpacing, Gfx.S(8, 4)))
        using (var popup = ImRaii.Popup("##headerMenu"))
        {
            if (popup)
                DrawContactMenuItems(conversation);
        }

        ImGui.SetCursorScreenPos(min + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, 0));
    }

    // --- Transcript -------------------------------------------------------------

    // Each thread has its own scroll area, so each keeps its own position.
    // `hidden` lays it out without showing anything: a see-through scrollbar,
    // and an empty clip rect for the text and bubbles. (A child that is fully
    // off-screen would be skipped by ImGui and never lay out.)
    private void DrawTranscriptChild(Conversation conversation, float height, bool hidden)
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Gfx.S(14, 10));
        using var scrollbar = ImRaii.PushColor(ImGuiCol.ScrollbarBg, Vector4.Zero, hidden)
            .Push(ImGuiCol.ScrollbarGrab, Vector4.Zero, hidden)
            .Push(ImGuiCol.ScrollbarGrabHovered, Vector4.Zero, hidden)
            .Push(ImGuiCol.ScrollbarGrabActive, Vector4.Zero, hidden);
        using var child = ImRaii.Child($"##transcript{conversation.Key}", new Vector2(-1, height), false, ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (!child)
            return;
        if (!hidden)
        {
            DrawTranscript(conversation);
            return;
        }
        var origin = ImGui.GetCursorScreenPos();
        ImGui.PushClipRect(origin, origin, false);
        DrawTranscript(conversation);
        ImGui.PopClipRect();
    }

    private void DrawTranscript(Conversation conversation)
    {
        var messages = conversation.Messages;
        lastOutgoingId = messages.LastOrDefault(m => m.Outgoing)?.Id;
        var wasAtBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Gfx.S(8);
        if (messages.Count != lastMessageCount)
        {
            if (wasAtBottom || messages.LastOrDefault()?.Outgoing == true)
                scrollToBottom = true;
            lastMessageCount = messages.Count;
        }

        if (messages.Count == 0)
        {
            ImGui.Dummy(Gfx.S(0, 40));
            CenteredDisabled(messenger.CanSend(conversation) ? "No messages yet. Say hello!" : "No messages.");
            return;
        }

        var first = Math.Max(0, messages.Count - shownCount);
        if (first > 0)
        {
            var label = "Show older messages";
            var size = ImGui.CalcTextSize(label) + Gfx.S(20, 8);
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - size.X) / 2 + ImGui.GetCursorPosX());
            using (ImRaii.PushColor(ImGuiCol.Button, Theme.Current.Input))
            {
                if (ImGui.Button(label, size))
                    shownCount += PageSize;
            }
            ImGui.Dummy(Gfx.S(0, 6));
        }

        ChatMessage? previous = first > 0 ? messages[first - 1] : null;
        for (var i = first; i < messages.Count; i++)
        {
            var message = messages[i];
            if (previous == null || Gfx.ToDisplay(previous.Time, config).Date != Gfx.ToDisplay(message.Time, config).Date)
            {
                DrawSeparator(Gfx.DateLabel(message.Time, config), Theme.Current.TextSecondary);
                previous = null;
            }
            if (message.Id == unreadAnchorId)
            {
                DrawSeparator("New messages", Theme.Current.Accent);
                previous = null;
            }

            var startsGroup = previous == null || previous.Outgoing != message.Outgoing || previous.Sender != message.Sender
                              || message.Time - previous.Time > TimeSpan.FromMinutes(5);
            DrawMessage(conversation, message, startsGroup);
            previous = message;
        }

        ImGui.Dummy(Gfx.S(0, 4));
        if (scrollToBottom)
        {
            ImGui.SetScrollHereY(1f);
            scrollToBottom = false;
        }
    }

    // A player name link in a message: open a conversation with them.
    private void OpenTellFromLink(string name, string world)
    {
        if (messenger.Owner == null)
            return;
        Open(messenger.StartTell(name, world).Key);
    }

    private void DrawSeparator(string label, Vector4 color)
    {
        ImGui.Dummy(Gfx.S(0, 8));
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var size = Gfx.SmallSize(label);
        var height = size.Y + Gfx.S(4);
        var list = ImGui.GetWindowDrawList();
        var mid = min.Y + height / 2;
        var labelX = min.X + (width - size.X) / 2;
        list.AddLine(new Vector2(min.X, mid), new Vector2(labelX - Gfx.S(10), mid), Theme.U32(color with { W = color.W * 0.35f }));
        list.AddLine(new Vector2(labelX + size.X + Gfx.S(10), mid), new Vector2(min.X + width, mid), Theme.U32(color with { W = color.W * 0.35f }));
        Gfx.SmallText(list, new Vector2(labelX, min.Y + Gfx.S(2)), Theme.U32(color), label);
        ImGui.Dummy(new Vector2(width, height + Gfx.S(6)));
    }

    private void DrawMessage(Conversation conversation, ChatMessage message, bool startsGroup)
    {
        using var id = ImRaii.PushId(message.Id);
        var p = Theme.Current;
        var list = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var small = Gfx.SmallHeight;

        var region = ImGui.GetContentRegionAvail().X;
        var avatarSize = Gfx.S(28);
        var gutter = avatarSize + Gfx.S(10);
        var maxBubble = (region - gutter) * 0.75f;
        var padding = Gfx.S(12, 8);

        if (startsGroup)
        {
            ImGui.Dummy(Gfx.S(0, 8));
            var start = ImGui.GetCursorScreenPos();
            var time = Gfx.Clock(message.Time, config);
            if (message.Outgoing)
            {
                var status = message.State switch
                {
                    SendState.Pending => "Sending…  ",
                    _ => "",
                };
                var label = $"{status}{time}   You";
                var size = Gfx.SmallSize(label);
                Gfx.SmallText(list, new Vector2(start.X + region - gutter - size.X, start.Y), Theme.U32(p.Timestamp), label);
            }
            else
            {
                var sender = conversation.IsTell ? messenger.DisplayName(conversation) : message.Sender;
                var nameSize = Gfx.SmallSize(sender);
                Gfx.SmallText(list, new Vector2(start.X + gutter, start.Y), Theme.U32(p.TextSecondary), sender);
                Gfx.SmallText(list, new Vector2(start.X + gutter + nameSize.X + Gfx.S(8), start.Y), Theme.U32(p.Timestamp), time);
            }
            ImGui.Dummy(new Vector2(region, small + Gfx.S(3)));
        }
        else
        {
            ImGui.Dummy(Gfx.S(0, 3));
        }

        // Measure the bubble: optional quote line plus wrapped text.
        var pieces = MessageLinks.Layout(MessageLinks.RunsFor(message), maxBubble - padding.X * 2, out var textSize);
        var quote = message.ReplyToText != null ? $"{message.ReplyToSender}  {Messenger.Snippet(message.ReplyToText, 60)}" : null;
        var quoteHeight = 0f;
        var contentWidth = textSize.X;
        if (quote != null)
        {
            var quoteSize = Gfx.SmallSize(quote);
            contentWidth = MathF.Max(contentWidth, MathF.Min(quoteSize.X + Gfx.S(10), maxBubble - padding.X * 2));
            quoteHeight = small + Gfx.S(10);
        }

        var bubbleSize = new Vector2(contentWidth + padding.X * 2, textSize.Y + quoteHeight + padding.Y * 2);
        var rowStart = ImGui.GetCursorScreenPos();
        var bubbleMin = message.Outgoing
            ? new Vector2(rowStart.X + region - gutter - bubbleSize.X, rowStart.Y)
            : new Vector2(rowStart.X + gutter, rowStart.Y);
        var bubbleMax = bubbleMin + bubbleSize;

        ImGui.SetCursorScreenPos(bubbleMin);
        ImGui.InvisibleButton("##bubble", bubbleSize);
        var hovered = ImGui.IsItemHovered();
        var leftClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            menuMessage = message;
            openMessageMenu = true;
        }

        var background = message.Outgoing ? Theme.BubbleOut : Theme.BubbleIn;
        if (message.State == SendState.Pending)
            background = Theme.Fade(background, 0.6f);
        if (hovered)
            background = Vector4.Min(background + new Vector4(0.04f, 0.04f, 0.04f, 0), Vector4.One);
        list.AddRectFilled(bubbleMin, bubbleMax, Theme.U32(background), Gfx.S(12));
        if (message.Mention)
            list.AddRect(bubbleMin, bubbleMax, Theme.U32(p.Accent with { W = 0.8f }), Gfx.S(12), ImDrawFlags.None, Gfx.S(1.5f));
        if (message.State == SendState.Failed)
            list.AddRect(bubbleMin, bubbleMax, Theme.U32(p.Danger), Gfx.S(12), ImDrawFlags.None, Gfx.S(1.5f));

        var cursor = bubbleMin + padding;
        if (quote != null)
        {
            var quoteMin = cursor;
            var quoteMax = new Vector2(bubbleMax.X - padding.X, cursor.Y + small + Gfx.S(6));
            list.AddRectFilled(quoteMin, quoteMax, Theme.U32(new Vector4(0, 0, 0, 0.18f)), Gfx.S(4));
            list.AddRectFilled(quoteMin, new Vector2(quoteMin.X + Gfx.S(2), quoteMax.Y), Theme.U32(p.Accent));
            using (Gfx.Clip(quoteMin, quoteMax, true))
            {
                var senderText = message.ReplyToSender ?? "";
                Gfx.SmallText(list, quoteMin + Gfx.S(7, 3), Theme.U32(p.Accent), senderText);
                var senderWidth = Gfx.SmallSize(senderText).X;
                Gfx.SmallText(list, quoteMin + new Vector2(Gfx.S(7) + senderWidth + Gfx.S(6), Gfx.S(3)),
                    Theme.U32(p.TextSecondary), Messenger.Snippet(message.ReplyToText!, 60));
            }
            cursor.Y += quoteHeight;
        }

        var textColor = message.Outgoing ? Theme.SentText : p.Text;
        var linkColor = message.Outgoing ? Theme.SentText : p.Accent;
        var hoveredLink = MessageLinks.Draw(list, font, fontSize, cursor, pieces, Theme.U32(textColor), Theme.U32(linkColor), hovered);
        if (hoveredLink != null)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            MessageLinks.Tooltip(hoveredLink);
            if (leftClicked)
                MessageLinks.Activate(hoveredLink, OpenTellFromLink);
        }

        // Avatar beside the first bubble of a run.
        if (startsGroup)
        {
            var avatarMin = message.Outgoing
                ? new Vector2(rowStart.X + region - avatarSize, bubbleMin.Y)
                : new Vector2(rowStart.X, bubbleMin.Y);
            var job = message.Outgoing ? messenger.LocalJob : conversation.IsTell ? messenger.JobOf(conversation) : message.SenderJob;
            Gfx.Avatar(list, avatarMin, avatarSize, job, ChannelKind.Tell, messenger.PortraitOf(conversation, message));
        }

        ImGui.SetCursorScreenPos(new Vector2(rowStart.X, bubbleMax.Y));
        ImGui.Dummy(new Vector2(region, 0));

        if (message.Outgoing)
            DrawReceipt(conversation, message, region, gutter);
    }

    // A small line under an outgoing bubble, like a messenger read receipt:
    // "Sending…", "Sent" under your latest message, or why it failed.
    private void DrawReceipt(Conversation conversation, ChatMessage message, float region, float gutter)
    {
        var p = Theme.Current;
        var right = ImGui.GetCursorScreenPos().X + region - gutter;
        var list = ImGui.GetWindowDrawList();

        if (message.State == SendState.Failed)
        {
            ImGui.Dummy(Gfx.S(0, 3));
            var reason = message.FailReason ?? "Not sent.";
            var maxWidth = (region - gutter) * 0.75f;
            // Wrap the game's error onto as many lines as it needs.
            foreach (var line in WrapSmall($"Not sent · {reason}", maxWidth))
            {
                var y = ImGui.GetCursorScreenPos().Y;
                var size = Gfx.SmallSize(line);
                Gfx.SmallText(list, new Vector2(right - size.X, y), Theme.U32(p.Danger), line);
                ImGui.Dummy(new Vector2(region, size.Y));
            }

            var canRetry = messenger.CanSend(conversation);
            var retrySize = Gfx.SmallSize("Retry");
            var discardSize = Gfx.SmallSize("Discard");
            var dotSize = Gfx.SmallSize("  ·  ");
            var total = discardSize.X + (canRetry ? retrySize.X + dotSize.X : 0);
            var rowY = ImGui.GetCursorScreenPos().Y + Gfx.S(1);
            var x = right - total;
            if (canRetry)
            {
                if (SmallLink("##retry", "Retry", new Vector2(x, rowY)))
                    messenger.Retry(conversation, message);
                x += retrySize.X;
                Gfx.SmallText(list, new Vector2(x, rowY), Theme.U32(p.Timestamp), "  ·  ");
                x += dotSize.X;
            }
            if (SmallLink("##discard", "Discard", new Vector2(x, rowY)))
                messenger.Discard(conversation, message);
            ImGui.SetCursorScreenPos(new Vector2(right - region + gutter, rowY + retrySize.Y));
            ImGui.Dummy(new Vector2(region, 0));
            return;
        }

        var status = message.State == SendState.Pending ? "Sending…" : message.Id == lastOutgoingId ? "Sent" : null;
        if (status == null)
            return;
        ImGui.Dummy(Gfx.S(0, 2));
        var statusSize = Gfx.SmallSize(status);
        Gfx.SmallText(list, new Vector2(right - statusSize.X, ImGui.GetCursorScreenPos().Y), Theme.U32(p.Timestamp), status);
        ImGui.Dummy(new Vector2(region, statusSize.Y));
    }

    private bool SmallLink(string id, string label, Vector2 pos)
    {
        var size = Gfx.SmallSize(label);
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, size);
        var color = ImGui.IsItemHovered() ? Theme.Current.Text : Theme.Current.Accent;
        Gfx.SmallText(ImGui.GetWindowDrawList(), pos, Theme.U32(color), label);
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    private static IEnumerable<string> WrapSmall(string text, float maxWidth)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var next = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && Gfx.SmallSize(next).X > maxWidth)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = next;
            }
        }
        if (line.Length > 0)
            yield return line;
    }

    private void DrawMessageMenu(Conversation conversation)
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Gfx.S(8, 8)).Push(ImGuiStyleVar.ItemSpacing, Gfx.S(4, 4));
        using var popup = ImRaii.Popup("##messageMenu");
        if (!popup || menuMessage == null)
            return;
        var message = menuMessage;

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Gfx.S(8, 6)))
        {
            if (messenger.CanSend(conversation) && ImGui.MenuItem("Reply"))
            {
                replyTo = message;
                focusComposer = true;
            }
            if (ImGui.MenuItem("Copy text"))
                ImGui.SetClipboardText(message.Text);
            if (message.State == SendState.Failed && messenger.CanSend(conversation) && ImGui.MenuItem("Retry"))
                messenger.Retry(conversation, message);

            // FFXIV can't unsend a tell; this only clears it from your history.
            ImGui.Separator();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Current.Danger))
            {
                if (ImGui.MenuItem("Delete"))
                {
                    if (replyTo == message)
                        replyTo = null;
                    messenger.Discard(conversation, message);
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Removes it from your Tell Messenger history only.\nThe other person still has it.");
        }
    }

    // --- Composer ---------------------------------------------------------------

    private void DrawRequestBar(Conversation conversation)
    {
        var p = Theme.Current;
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Gfx.S(40);
        ImGui.GetWindowDrawList().AddRectFilled(min, min + new Vector2(width, height), Theme.U32(p.SurfaceSecondary));

        const string text = "Message request: they're not on your friend list.";
        var textSize = ImGui.CalcTextSize(text);
        Gfx.Text(ImGui.GetWindowDrawList(), min + new Vector2(Gfx.S(14), (height - textSize.Y) / 2), Theme.U32(p.TextSecondary), text);

        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, Gfx.S(12, 4)))
        {
            var buttonHeight = ImGui.GetFrameHeight();
            ImGui.SetCursorScreenPos(min + new Vector2(width - Gfx.S(170), (height - buttonHeight) / 2));
            using (ImRaii.PushColor(ImGuiCol.Button, p.Accent with { W = 0.85f }).Push(ImGuiCol.ButtonHovered, p.Accent)
                       .Push(ImGuiCol.Text, p.Surface with { W = 1 }))
            {
                if (ImGui.Button("Accept"))
                {
                    conversation.IsRequest = false;
                    messenger.Tab = ContactsTab.Whispers;
                    messenger.Store.MarkDirty();
                }
            }
            ImGui.SameLine(0, Gfx.S(8));
            using (ImRaii.PushColor(ImGuiCol.Button, p.Danger with { W = 0.5f }).Push(ImGuiCol.ButtonHovered, p.Danger with { W = 0.7f }))
            {
                if (ImGui.Button("Delete"))
                {
                    messenger.ActiveKey = null;
                    messenger.Store.Remove(conversation.Key);
                }
            }
        }

        ImGui.SetCursorScreenPos(min + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, 0));
    }

    private void DrawReplyBanner()
    {
        var p = Theme.Current;
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Gfx.S(34);
        var list = ImGui.GetWindowDrawList();
        list.AddRectFilled(min, min + new Vector2(width, height), Theme.U32(p.SurfaceSecondary));
        list.AddRectFilled(min + Gfx.S(14, 7), min + new Vector2(Gfx.S(16), height - Gfx.S(7)), Theme.U32(p.Accent));

        var who = replyTo!.Outgoing ? "yourself" : replyTo.Sender;
        var label = $"Replying to {who}";
        var y = min.Y + (height - ImGui.GetTextLineHeight()) / 2;
        Gfx.Text(list, new Vector2(min.X + Gfx.S(24), y), Theme.U32(p.Accent), label);
        using (Gfx.Clip(min, min + new Vector2(width - Gfx.S(40), height), true))
        {
            var labelWidth = ImGui.CalcTextSize(label).X;
            Gfx.Text(list, new Vector2(min.X + Gfx.S(34) + labelWidth, y), Theme.U32(p.TextSecondary), Messenger.Snippet(replyTo.Text, 80));
        }

        ImGui.SetCursorScreenPos(min + new Vector2(width - Gfx.S(34), (height - Gfx.S(26)) / 2));
        if (Gfx.IconButton("##cancelReply", FontAwesomeIcon.Times, "Cancel reply"))
            replyTo = null;

        ImGui.SetCursorScreenPos(min + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, 0));
    }

    private void DrawComposer(Conversation conversation, float height)
    {
        var p = Theme.Current;
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var list = ImGui.GetWindowDrawList();
        list.AddRectFilled(min, min + new Vector2(width, height), Theme.U32(p.Chrome), Gfx.S(8), ImDrawFlags.RoundCornersBottomRight);
        list.AddLine(min, min + new Vector2(width, 0), Theme.U32(p.Divider));

        var canSend = messenger.CanSend(conversation);
        var readOnlyReason = !canSend
            ? messenger.Owner == null ? "Log in to send." : "Another character's history, read-only."
            : conversation.Kind is ChannelKind.Party && Services.Party.Length == 0 ? "Not in a party, so this may not send." : null;
        var warning = canSend ? messenger.DeliveryWarning(conversation) : null;

        var gutter = Gfx.S(8);
        var buttonSize = Gfx.S(30);
        var inputWidth = width - gutter * 2 - (buttonSize + Gfx.S(4)) * 2;

        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Gfx.S(12), (height - gutter * 2 - ImGui.GetTextLineHeight()) / 2))
                   .Push(ImGuiStyleVar.FrameRounding, Gfx.S(10)))
        {
            ImGui.SetCursorScreenPos(min + new Vector2(gutter, gutter));
            ImGui.SetNextItemWidth(inputWidth);
            if (focusComposer && canSend)
            {
                ImGui.SetKeyboardFocusHere();
                focusComposer = false;
            }

            using (ImRaii.Disabled(!canSend))
            {
                var hint = readOnlyReason ?? warning ?? (replyTo != null ? "Write a reply" : "Enter to send");
                var draft = conversation.Draft;
                var enter = ImGui.InputTextWithHint("##composer", hint, ref draft, 1000, ImGuiInputTextFlags.EnterReturnsTrue);
                if (draft != conversation.Draft)
                {
                    conversation.Draft = draft;
                    composerError = null;
                    messenger.Store.MarkDirty();
                }
                if (enter)
                {
                    SendDraft(conversation);
                    focusComposer = true;
                }
            }
        }

        var bytes = Game.ChatSender.ByteCount(conversation.Draft);
        var limit = messenger.MaxTextBytes(conversation);
        // Error, then length counter near the limit, then a delivery warning
        // once the placeholder hint has been typed over.
        var note = composerError
                   ?? (bytes > limit - 80 ? $"{bytes}/{limit}" : null)
                   ?? (conversation.Draft.Length > 0 ? warning : null);
        if (note != null)
        {
            var size = Gfx.SmallSize(note);
            var color = composerError != null || bytes > limit ? p.Danger : note == warning ? p.Away : p.Timestamp;
            Gfx.SmallText(list,
                min + new Vector2(gutter + inputWidth - size.X - Gfx.S(10), height - gutter - size.Y - Gfx.S(2)),
                Theme.U32(color), note);
        }

        var buttonY = (height - Gfx.S(26)) / 2;
        ImGui.SetCursorScreenPos(min + new Vector2(gutter + inputWidth + Gfx.S(6), buttonY));
        using (ImRaii.Disabled(!canSend))
        {
            if (Gfx.IconButton("##emoji", FontAwesomeIcon.Smile, "Emoticons", new Vector4(1.0f, 0.82f, 0.30f, 1), 30))
                ImGui.OpenPopup("##emoticons");
            ImGui.SameLine(0, Gfx.S(4));
            if (Gfx.IconButton("##send", FontAwesomeIcon.PaperPlane, "Send", p.Accent, 30))
            {
                SendDraft(conversation);
                focusComposer = true;
            }
        }

        DrawEmoticons(conversation);

        ImGui.SetCursorScreenPos(min + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, 0));
    }

    private void SendDraft(Conversation conversation)
    {
        var text = conversation.Draft;
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (Game.ChatSender.ByteCount(Game.ChatSender.Clean(text)) > messenger.MaxTextBytes(conversation))
        {
            composerError = "Message too long";
            return;
        }
        if (!messenger.Send(conversation, text, replyTo))
        {
            composerError = replyTo != null && config.QuoteRepliesInGame ? "Too long with the quote" : "Could not send";
            return;
        }
        replyTo = null;
        composerError = null;
        scrollToBottom = true;
    }

    private void DrawEmoticons(Conversation conversation)
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Gfx.S(8, 8)).Push(ImGuiStyleVar.ItemSpacing, Gfx.S(4, 4));
        using var popup = ImRaii.Popup("##emoticons");
        if (!popup)
            return;

        for (var i = 0; i < Emoticons.Length; i++)
        {
            if (i % 8 != 0)
                ImGui.SameLine();
            if (ImGui.Button(Emoticons[i], Gfx.S(38, 28)))
            {
                var draft = conversation.Draft;
                conversation.Draft = draft.Length == 0 || draft.EndsWith(' ') ? draft + Emoticons[i] : $"{draft} {Emoticons[i]}";
                focusComposer = true;
                ImGui.CloseCurrentPopup();
            }
        }

        // FFXIV's own way to link things in chat works here too, because
        // messages go through the game's chat box.
        ImGui.Separator();
        ImGui.TextDisabled("Links: use \"Link\" on an item, then type <item>.");
        ImGui.TextDisabled("Also <flag>, <pos>, <t> and the other chat placeholders.");
        foreach (var placeholder in new[] { "<item>", "<flag>", "<pos>", "<t>" })
        {
            if (ImGui.Button(placeholder, new Vector2(0, Gfx.S(24))))
            {
                var draft = conversation.Draft;
                conversation.Draft = draft.Length == 0 || draft.EndsWith(' ') ? draft + placeholder : $"{draft} {placeholder}";
                focusComposer = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();
    }
}
