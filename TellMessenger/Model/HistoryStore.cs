using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TellMessenger.Model;

// Saved conversations and contact settings, kept in history.json next to the
// plugin config. Changes are written a few seconds after they happen so a
// burst of messages costs one write.
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Configuration config;
    private readonly string path;
    private HistoryData data = new();
    private DateTime dirtySince = DateTime.MaxValue;
    private DateTime lastPrune = DateTime.MinValue;
    private Task saving = Task.CompletedTask;

    public HistoryStore(Configuration config, string directory)
    {
        this.config = config;
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "history.json");
        Load();
    }

    public IEnumerable<Conversation> Conversations => data.Conversations.Values;

    public event Action? Changed;

    public Conversation? Get(string key) => data.Conversations.GetValueOrDefault(key);

    public Conversation GetOrCreateTell(string owner, string name, string world)
    {
        var key = Conversation.TellKey(owner, name, world);
        if (data.Conversations.TryGetValue(key, out var existing))
            return existing;

        var conversation = new Conversation { Key = key, Owner = owner, Kind = ChannelKind.Tell, Name = name, World = world };
        data.Conversations[key] = conversation;
        EnforceContactLimit();
        MarkDirty();
        return conversation;
    }

    public Conversation GetOrCreateGroup(string owner, ChannelKind kind, int channel, string label)
    {
        var key = Conversation.GroupKey(owner, kind, channel);
        if (data.Conversations.TryGetValue(key, out var existing))
        {
            // Names are kept up to date by Messenger.RefreshGroupLabels.
            return existing;
        }

        var conversation = new Conversation { Key = key, Owner = owner, Kind = kind, Channel = channel, Name = label };
        data.Conversations[key] = conversation;
        MarkDirty();
        return conversation;
    }

    public ContactPrefs Prefs(string contactKey)
    {
        if (!data.Contacts.TryGetValue(contactKey, out var prefs))
            data.Contacts[contactKey] = prefs = new ContactPrefs();
        return prefs;
    }

    public ContactPrefs? PeekPrefs(string contactKey) => data.Contacts.GetValueOrDefault(contactKey);

    public void Append(Conversation conversation, ChatMessage message)
    {
        conversation.Messages.Add(message);
        conversation.LastActivity = message.Time;
        var excess = conversation.Messages.Count - Math.Max(1, config.MaxMessagesPerContact);
        if (excess > 0)
            conversation.Messages.RemoveRange(0, excess);
        MarkDirty();
    }

    public void Remove(string key)
    {
        if (data.Conversations.Remove(key))
            MarkDirty();
    }

    public void ClearAll()
    {
        data.Conversations.Clear();
        MarkDirty();
    }

    public void MarkDirty()
    {
        if (dirtySince == DateTime.MaxValue)
            dirtySince = DateTime.UtcNow;
        Changed?.Invoke();
    }

    // Called every frame from the framework thread.
    public void Update()
    {
        var now = DateTime.UtcNow;
        if (now - lastPrune > TimeSpan.FromMinutes(10))
        {
            lastPrune = now;
            Prune();
        }

        if (now - dirtySince > TimeSpan.FromSeconds(3) && saving.IsCompleted)
            Save();
    }

    public void Save()
    {
        dirtySince = DateTime.MaxValue;
        var json = JsonSerializer.Serialize(data, JsonOptions);
        saving = Task.Run(() =>
        {
            try
            {
                var temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, true);
            }
            catch (Exception e)
            {
                Services.Log.Error(e, "Could not save message history");
            }
        });
    }

    public void Flush()
    {
        if (dirtySince != DateTime.MaxValue)
            Save();
        saving.Wait(TimeSpan.FromSeconds(5));
    }

    private void Load()
    {
        try
        {
            if (File.Exists(path))
                data = JsonSerializer.Deserialize<HistoryData>(File.ReadAllText(path), JsonOptions) ?? new HistoryData();
        }
        catch (Exception e)
        {
            Services.Log.Error(e, "Could not read message history, starting fresh");
            try { File.Copy(path, path + ".broken", true); } catch { /* best effort */ }
            data = new HistoryData();
        }

        foreach (var conversation in data.Conversations.Values)
        {
            // A message still pending when the game closed never went out.
            foreach (var message in conversation.Messages.Where(m => m.State == SendState.Pending))
                message.State = SendState.Failed;
        }
        Prune();
    }

    private void Prune()
    {
        if (config.RetentionHours <= 0)
            return;

        var cutoff = DateTime.UtcNow.AddHours(-config.RetentionHours);
        var changed = false;
        foreach (var conversation in data.Conversations.Values.ToList())
        {
            var removed = conversation.Messages.RemoveAll(m => m.Time < cutoff);
            changed |= removed > 0;
            if (conversation.Messages.Count == 0 && !conversation.Pinned && conversation.LastActivity < cutoff)
            {
                data.Conversations.Remove(conversation.Key);
                changed = true;
            }
        }
        if (changed)
            MarkDirty();
    }

    private void EnforceContactLimit()
    {
        var tells = data.Conversations.Values.Where(c => c.IsTell).ToList();
        var excess = tells.Count - Math.Max(1, config.MaxContacts);
        if (excess <= 0)
            return;

        foreach (var old in tells.Where(c => !c.Pinned).OrderBy(c => c.LastActivity).Take(excess))
            data.Conversations.Remove(old.Key);
    }
}
