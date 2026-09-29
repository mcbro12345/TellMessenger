using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using TellMessenger.Game;

namespace TellMessenger.UI;

// Right-clicking someone's name or picture in a conversation, or a player
// link in a message, shows the same player options as Chat 2.
public sealed partial class MessengerWindow
{
    private (string Name, string World)? menuPlayer;
    private bool openPlayerMenu;

    private void OpenPlayerMenu(string name, string world)
    {
        if (name.Length == 0 || name == Messenger.TestName)
            return;
        menuPlayer = (name, world);
        GatherPluginItemsForPlayer(name, world);
        openPlayerMenu = true;
    }

    // Opened from the window itself so popups from inside the child windows
    // share one ID.
    private void DrawPlayerMenu()
    {
        if (openPlayerMenu)
        {
            openPlayerMenu = false;
            ImGui.OpenPopup("##playerMenu");
        }

        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Gfx.S(8, 8)).Push(ImGuiStyleVar.ItemSpacing, Gfx.S(8, 4));
        using var popup = ImRaii.Popup("##playerMenu");
        if (!popup || menuPlayer is not var (name, world))
            return;

        ImGui.TextUnformatted(name);
        if (world.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(world);
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Send Tell"))
            OpenTellFromLink(name, world);
        DrawPlayerItems(name, world);
        DrawPluginItems(pluginItems);
    }

    // Chat 2's player options, in Chat 2's order and wording.
    private void DrawPlayerItems(string name, string world)
    {
        if (!PlayerActions.IsPublicWorld(world))
            return;

        var worldId = PlayerActions.WorldId(world);
        var contentId = PlayerActions.ContentIdOf(name, world);
        var member = PlayerActions.PartyMember(name, worldId);
        var inInstance = PlayerActions.InInstance;
        var inPartyInstance = PlayerActions.InPartyInstance;

        if (PlayerActions.IsPartyLeader())
        {
            if (member == null)
            {
                if (inInstance && inPartyInstance)
                {
                    if (contentId != 0 && ImGui.MenuItem("Invite to Party"))
                        PlayerActions.InviteInInstance(contentId);
                }
                else if (!inInstance)
                {
                    using var invite = ImRaii.Menu("Invite to Party");
                    if (invite)
                    {
                        if (ImGui.MenuItem("Same world"))
                            PlayerActions.InviteSameWorld(name, world, contentId);
                        if (contentId != 0 && ImGui.MenuItem("Different world"))
                            PlayerActions.InviteOtherWorld(contentId, world);
                    }
                }
            }
            else if (!inInstance || inPartyInstance)
            {
                if (ImGui.MenuItem("Promote"))
                    PlayerActions.Promote(name, (ulong)member.ContentId);
                if (ImGui.MenuItem("Kick from Party"))
                    PlayerActions.Kick(name, (ulong)member.ContentId);
            }
        }

        var players = messenger.Players;
        if (players.CanUseListCommands)
        {
            if (!messenger.Friends.IsFriend($"{name}@{world}") && ImGui.MenuItem("Send Friend Request"))
                players.SendFriendRequest(name, world);
            using var block = ImRaii.Menu("Block Functions");
            if (block.Success && ImGui.MenuItem("Add to Blacklist"))
                players.AddToBlacklist(name, world);
        }

        if (PlayerActions.IsMentor() && ImGui.MenuItem("Invite to Novice Network"))
            PlayerActions.InviteToNoviceNetwork(name, world);

        if (ImGui.MenuItem("Target", "", false, PlayerActions.Nearby(name, worldId) != null))
            PlayerActions.Target(name, world);

        if (contentId != 0 && ImGui.MenuItem("View Adventurer Plate"))
            PlayerActions.OpenAdventurerPlate(contentId);
    }
}
