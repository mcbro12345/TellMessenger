using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using TellMessenger.Game;

namespace TellMessenger.UI;

// Right-clicking an item link: the options the game's chat gives an item
// (the same ones Chat 2 has).
public sealed partial class MessengerWindow
{
    private ItemPayload? menuItem;
    private bool openItemMenu;

    private void OpenItemMenu(ItemPayload item)
    {
        menuItem = item;
        GatherPluginItems(new PluginMenuItems.ItemTarget(item.RawItemId));
        openItemMenu = true;
    }

    private unsafe void DrawItemMenu()
    {
        if (openItemMenu)
        {
            openItemMenu = false;
            ImGui.OpenPopup("##itemMenu");
        }

        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Gfx.S(8, 8)).Push(ImGuiStyleVar.ItemSpacing, Gfx.S(8, 4));
        using var popup = ImRaii.Popup("##itemMenu");
        if (!popup || menuItem is not { } payload)
            return;

        var rawId = payload.RawItemId;
        string name;
        uint icon;
        var hq = payload.Kind == ItemKind.Hq;
        var eventItem = payload.Kind == ItemKind.EventItem;
        Item? row = null;
        if (eventItem)
        {
            if (Services.Data.GetExcelSheet<EventItem>().GetRowOrDefault(payload.ItemId) is not { } evt)
                return;
            name = evt.Name.ExtractText();
            icon = evt.Icon;
        }
        else
        {
            if (Services.Data.GetExcelSheet<Item>().GetRowOrDefault(payload.ItemId) is not { } item)
                return;
            row = item;
            name = item.Name.ExtractText();
            icon = item.Icon;
        }

        // Header: icon and name, like the tooltip.
        var iconSize = ImGui.GetTextLineHeight() * 1.4f;
        if (Services.Textures.GetFromGameIcon(new GameIconLookup(icon, hq)).GetWrapOrDefault() is { } wrap)
        {
            ImGui.Image(wrap.Handle, new System.Numerics.Vector2(iconSize, iconSize));
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (iconSize - ImGui.GetTextLineHeight()) / 2);
        }
        ImGui.TextUnformatted(hq ? $"{name} " : name);
        ImGui.Separator();

        if (row is { } itemRow)
        {
            if (itemRow.EquipSlotCategory.RowId != 0)
            {
                if (ImGui.MenuItem("Try On"))
                    AgentTryon.TryOn(0xFF, rawId, 0, 0, 0, false);
                if (ImGui.MenuItem("Item Comparison"))
                    AgentItemComp.Instance()->CompareItem(0x4D, rawId, 0, 0);
            }
            if (itemRow.ItemSearchCategory.ValueNullable?.Category == 3 && ImGui.MenuItem("Search Recipes Using This Material"))
                AgentRecipeProductList.Instance()->SearchForRecipesUsingItem(payload.ItemId);
            if (ImGui.MenuItem("Search for Item"))
                ItemFinderModule.Instance()->SearchForItem(rawId, true);
        }
        if (ImGui.MenuItem("Link"))
            AgentChatLog.Instance()->LinkItem(rawId);
        if (ImGui.MenuItem("Copy Item Name"))
            ImGui.SetClipboardText(name);
        DrawPluginItems(pluginItems);
    }
}
