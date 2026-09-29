using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.Utility.Raii;
using TellMessenger.Game;

namespace TellMessenger.UI;

// Other plugins' additions at the bottom of the player and item menus, as
// they'd show on the game's own menus. They're asked once when a menu opens.
public sealed partial class MessengerWindow
{
    private List<IMenuItem> pluginItems = [];
    private PluginMenuItems.Target? pluginTarget;
    private IReadOnlyList<IMenuItem>? pluginSubmenu;
    private bool openPluginSubmenu;

    private void GatherPluginItems(PluginMenuItems.Target? target)
    {
        pluginTarget = target;
        pluginItems = target != null ? PluginMenuItems.Collect(target) : [];
    }

    // A tell's contact menu is also a player menu.
    private void GatherPluginItemsFor(Model.Conversation conversation)
    {
        if (conversation.IsTell)
            GatherPluginItemsForPlayer(conversation.Name, conversation.World);
        else
            GatherPluginItems(null);
    }

    private void GatherPluginItemsForPlayer(string name, string world)
    {
        var worldId = PlayerActions.WorldId(world);
        if (worldId == 0 || name == Messenger.TestName)
        {
            GatherPluginItems(null);
            return;
        }
        var objectId = PlayerActions.Nearby(name, worldId)?.GameObjectId ?? 0xE0000000;
        GatherPluginItems(new PluginMenuItems.PlayerTarget(name, worldId, PlayerActions.ContentIdOf(name, world), objectId));
    }

    private void DrawPluginItems(IReadOnlyList<IMenuItem> items, bool separator = true)
    {
        if (items.Count == 0 || pluginTarget == null)
            return;
        if (separator)
            ImGui.Separator();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            using var id = ImRaii.PushId(i);
            var label = item.Name.TextValue;
            if (label.Length == 0)
                continue;
            if (!ImGui.MenuItem(item.IsSubmenu ? $"{label}  ›" : label, "", false, item.IsEnabled))
                continue;
            if (PluginMenuItems.Click(item, pluginTarget) is { Count: > 0 } submenu)
            {
                pluginSubmenu = submenu;
                openPluginSubmenu = true;
            }
        }
    }

    // A plugin item that opens a submenu shows it as its own menu.
    private void DrawPluginSubmenu()
    {
        if (openPluginSubmenu)
        {
            openPluginSubmenu = false;
            ImGui.OpenPopup("##pluginSubmenu");
        }
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Gfx.S(8, 8)).Push(ImGuiStyleVar.ItemSpacing, Gfx.S(8, 4));
        using var popup = ImRaii.Popup("##pluginSubmenu");
        if (popup.Success && pluginSubmenu != null)
            DrawPluginItems(pluginSubmenu, false);
    }
}
