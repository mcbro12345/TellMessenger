using System;
using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace TellMessenger.Game;

// The game's own "Send Tell" on a player's right-click menu stays where it
// is, but clicking it opens the conversation in the messenger instead of
// switching the chat box to Tell.
public sealed unsafe class GameMenuSendTell : IDisposable
{
    private readonly Configuration config;
    private readonly Action<string, string> open;
    private readonly Hook<AddonContextMenu.Delegates.OnMenuSelected> selectedHook;
    private readonly HashSet<string> labels = new(StringComparer.Ordinal);

    // The player the open menu is for.
    private string? targetName;
    private string? targetWorld;

    public GameMenuSendTell(Configuration config, Action<string, string> open)
    {
        this.config = config;
        this.open = open;
        LoadLabels();
        Services.ContextMenu.OnMenuOpened += OnMenuOpened;
        selectedHook = Services.GameInterop.HookFromAddress<AddonContextMenu.Delegates.OnMenuSelected>(
            (nint)AddonContextMenu.StaticVirtualTablePointer->OnMenuSelected, OnMenuSelected);
        selectedHook.Enable();
    }

    public void Dispose()
    {
        Services.ContextMenu.OnMenuOpened -= OnMenuOpened;
        selectedHook.Dispose();
    }

    // "Send Tell" in the game's language, from the same text table the menu uses.
    private void LoadLabels()
    {
        var english = Services.Data.GetExcelSheet<Addon>(ClientLanguage.English);
        var local = Services.Data.GetExcelSheet<Addon>();
        foreach (var row in english)
        {
            if (row.Text.ExtractText() != "Send Tell")
                continue;
            labels.Add("Send Tell");
            if (local.TryGetRow(row.RowId, out var localRow))
                labels.Add(localRow.Text.ExtractText());
        }
        if (labels.Count == 0)
            labels.Add("Send Tell");
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        targetName = targetWorld = null;
        if (args.MenuType != ContextMenuType.Default || args.Target is not MenuTargetDefault target)
            return;
        if (string.IsNullOrEmpty(target.TargetName) || !target.TargetHomeWorld.IsValid || target.TargetHomeWorld.RowId == 0)
            return;
        targetName = target.TargetName;
        targetWorld = target.TargetHomeWorld.Value.Name.ExtractText();
    }

    private bool OnMenuSelected(AddonContextMenu* addon, int selectedIdx, byte a3)
    {
        try
        {
            if (config.OpenOnGameSendTell && targetName != null && targetWorld != null && IsSendTell(addon, selectedIdx))
            {
                var name = targetName;
                var world = targetWorld;
                // Close the menu with the usual click sound.
                addon->AtkUnitBase.FireCallbackInt(-2);
                open(name, world);
                return false;
            }
        }
        catch (Exception e)
        {
            Services.Log.Error(e, "Could not open Send Tell in the messenger");
        }
        return selectedHook.Original(addon, selectedIdx, a3);
    }

    // The menu's values: a header of 8, then one name per item.
    private bool IsSendTell(AddonContextMenu* addon, int index)
    {
        var unit = &addon->AtkUnitBase;
        if (index < 0 || unit->AtkValues == null)
            return false;
        var slot = 8 + index;
        if (slot >= unit->AtkValuesCount || unit->AtkValues[0].UInt <= (uint)index)
            return false;
        var value = unit->AtkValues[slot];
        if (value.Type is not (AtkValueType.String
            or AtkValueType.ManagedString
            or AtkValueType.ConstString))
            return false;
        var text = Dalamud.Game.Text.SeStringHandling.SeString.Parse(value.String).TextValue.Trim();
        return labels.Contains(text);
    }
}
