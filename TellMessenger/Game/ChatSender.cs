using System;
using System.Collections.Generic;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using TellMessenger.Model;

namespace TellMessenger.Game;

// Sends chat by running a chat command through the game's chat box, exactly
// as if it had been typed. Commands go out one at a time with a short gap so
// several quick sends don't trip the game's spam limit.
public sealed class ChatSender
{
    public const int MaxMessageBytes = 500;
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(600);

    private readonly Queue<string> queue = new();
    private DateTime lastSend = DateTime.MinValue;

    public static string CommandFor(Conversation conversation) => conversation.Kind switch
    {
        ChannelKind.Tell => $"/tell {conversation.Name}@{conversation.World}",
        ChannelKind.Party => "/p",
        ChannelKind.Alliance => "/a",
        ChannelKind.FreeCompany => "/fc",
        ChannelKind.Linkshell => $"/l{conversation.Channel}",
        ChannelKind.CrossLinkshell => $"/cwl{conversation.Channel}",
        ChannelKind.NoviceNetwork => "/n",
        ChannelKind.PvPTeam => "/pvpteam",
        _ => throw new ArgumentOutOfRangeException(nameof(conversation)),
    };

    // Newlines would split the command, and other control characters can
    // confuse the chat parser.
    public static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
            builder.Append(char.IsControl(c) ? ' ' : c);
        return builder.ToString().Trim();
    }

    public static int ByteCount(string text) => Encoding.UTF8.GetByteCount(text);

    public void Enqueue(Conversation conversation, string text) => queue.Enqueue($"{CommandFor(conversation)} {text}");

    // True while a send is queued or just went out; the game changes its
    // tell target for those too, which isn't the user pressing Send Tell.
    public bool Busy => queue.Count > 0 || DateTime.UtcNow - lastSend < TimeSpan.FromSeconds(3);

    public void Update()
    {
        if (queue.Count == 0 || DateTime.UtcNow - lastSend < Gap)
            return;
        lastSend = DateTime.UtcNow;
        Send(queue.Dequeue());
    }

    private static unsafe void Send(string command)
    {
        var ui = UIModule.Instance();
        if (ui == null)
            return;

        var message = Utf8String.FromString(command);
        try
        {
            ui->ProcessChatBoxEntry(message, IntPtr.Zero, false);
        }
        finally
        {
            message->Dtor(true);
        }
    }
}
