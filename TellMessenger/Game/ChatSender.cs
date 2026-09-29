using System;
using System.Collections.Generic;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
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
        Run(queue.Dequeue());
    }

    // The message as the game would send it, with <item>, <flag>, <pos>, <t>
    // and the other placeholders turned into links, without sending anything.
    public static unsafe SeString? Resolve(string text)
    {
        var pronouns = PronounModule.Instance();
        if (pronouns == null)
            return null;
        var input = Utf8String.FromString(text);
        try
        {
            input->Copy(pronouns->ProcessString(input, true, MaxMessageBytes));
            var output = pronouns->ProcessString(input, false, MaxMessageBytes);
            return output == null ? null : SeString.Parse(output->AsSpan().ToArray());
        }
        finally
        {
            input->Dtor(true);
        }
    }

    // Runs a chat line or text command as if typed into the chat box.
    public static unsafe void Run(string command)
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
