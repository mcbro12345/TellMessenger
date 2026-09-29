using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace TellMessenger.Game;

// What other plugins add to the game's right-click menus. They add items
// through Dalamud when the game opens a menu, looking at what was clicked in
// the game's own state (the chat's item for an item link, the menu target for
// a player). Our menus are drawn by us, so this sets up that same state, asks
// every plugin through Dalamud's own menu event, and hands back what they add.
// Clicking one runs it with that state set up again.
public static unsafe class PluginMenuItems
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // Who or what the menu is for.
    public abstract record Target;

    public sealed record ItemTarget(uint RawItemId) : Target;

    public sealed record PlayerTarget(string Name, ushort WorldId, ulong ContentId, ulong ObjectId) : Target;

    // True while other plugins are being asked, so our own menu listener
    // can tell this apart from the game opening a menu.
    public static bool Asking { get; private set; }

    public static List<IMenuItem> Collect(Target target)
    {
        var items = new List<IMenuItem>();
        try
        {
            if (Service() is not { } service)
                return items;
            var type = service.GetType();

            // Items added up front with AddMenuItem.
            var lockObject = type.GetProperty("MenuItemsLock", Any)?.GetValue(service);
            var registered = type.GetProperty("MenuItems", Any)?.GetValue(service) as Dictionary<ContextMenuType, List<IMenuItem>>;
            if (registered != null)
            {
                lock (lockObject ?? registered)
                {
                    if (registered.TryGetValue(ContextMenuType.Default, out var fixedItems))
                        items.AddRange(fixedItems);
                }
            }

            // Items added when a menu opens.
            if (type.GetField("OnMenuOpened", Any)?.GetValue(service) is not Delegate opened)
                return items;
            using var scope = new State(target);
            var args = Create("MenuOpenedArgs", new Action<MenuItem>(items.Add), scope.Addon, scope.Agent);
            if (args == null)
                return items;
            Asking = true;
            try
            {
                foreach (var handler in opened.GetInvocationList())
                {
                    try
                    {
                        handler.DynamicInvoke(args);
                    }
                    catch (Exception e)
                    {
                        Services.Log.Debug(e, $"A plugin's menu handler failed ({handler.Method.DeclaringType?.Assembly.GetName().Name})");
                    }
                }
            }
            finally
            {
                Asking = false;
            }
        }
        catch (Exception e)
        {
            Services.Log.Debug(e, "Couldn't ask other plugins for menu items");
        }
        return items.Where(i => i.OnClicked != null && !i.IsReturn).ToList();
    }

    // Runs a plugin's item. If it opens a submenu, that list comes back.
    public static IReadOnlyList<IMenuItem>? Click(IMenuItem item, Target target)
    {
        IReadOnlyList<IMenuItem>? submenu = null;
        try
        {
            using var scope = new State(target);
            var args = Create("MenuItemClickedArgs",
                new Action<SeString?, IReadOnlyList<IMenuItem>>((_, list) => submenu = list), scope.Addon, scope.Agent);
            if (args is IMenuItemClickedArgs clicked)
                item.OnClicked?.Invoke(clicked);
        }
        catch (Exception e)
        {
            Services.Log.Warning(e, $"A plugin's menu item failed: {item.Name.TextValue}");
        }
        return submenu?.Where(i => !i.IsReturn).ToList();
    }

    private static object? Service()
    {
        var assembly = typeof(IMenuItem).Assembly;
        var contextMenu = assembly.GetType("Dalamud.Game.Gui.ContextMenu.ContextMenu");
        var service = assembly.GetType("Dalamud.Service`1")?.MakeGenericType(contextMenu!);
        return service?.GetMethod("Get", Any, Type.EmptyTypes)?.Invoke(null, null);
    }

    private static object? Create(string typeName, Delegate callback, nint addon, nint agent)
    {
        var type = typeof(IMenuItem).Assembly.GetType($"Dalamud.Game.Gui.ContextMenu.{typeName}");
        var constructor = type?.GetConstructors(Any).FirstOrDefault(c => c.GetParameters().Length == 5);
        return constructor?.Invoke([
            callback,
            Pointer.Box((void*)addon, typeof(AtkUnitBase*)),
            Pointer.Box((void*)agent, typeof(AgentInterface*)),
            ContextMenuType.Default,
            new HashSet<nint>(),
        ]);
    }

    // Puts the game's menu state as it would be after right-clicking this in
    // the chat log, and puts it back afterwards.
    private sealed class State : IDisposable
    {
        private readonly AgentContext* context = AgentContext.Instance();
        private readonly AgentChatLog* chatLog = AgentChatLog.Instance();
        private readonly uint oldItem;
        private readonly string oldName;
        private readonly ulong oldContentId;
        private readonly ulong oldObjectId;
        private readonly short oldWorld;
        private readonly CharacterDataPointer oldCharacter;

        public State(Target target)
        {
            var manager = RaptureAtkUnitManager.Instance();
            Addon = manager != null ? (nint)manager->GetAddonByName("ChatLog") : 0;
            Agent = (nint)context;

            oldItem = chatLog->ContextItemId;
            oldName = context->TargetName.ToString();
            oldContentId = context->TargetContentId;
            oldObjectId = context->TargetObjectId;
            oldWorld = context->TargetHomeWorldId;
            oldCharacter = new CharacterDataPointer(context->CurrentContextMenuTarget);

            switch (target)
            {
                case ItemTarget item:
                    chatLog->ContextItemId = item.RawItemId;
                    Set("", 0, 0, 0xE0000000);
                    break;
                case PlayerTarget player:
                    chatLog->ContextItemId = 0;
                    Set(player.Name, player.WorldId, player.ContentId, player.ObjectId);
                    break;
            }
            context->CurrentContextMenuTarget = null;
        }

        public nint Addon { get; }

        public nint Agent { get; }

        public void Dispose()
        {
            chatLog->ContextItemId = oldItem;
            Set(oldName, (ushort)oldWorld, oldContentId, oldObjectId);
            context->CurrentContextMenuTarget = oldCharacter.Value;
        }

        private void Set(string name, ushort world, ulong contentId, ulong objectId)
        {
            context->TargetName.SetString(name);
            context->TargetHomeWorldId = (short)world;
            context->TargetContentId = contentId;
            context->TargetObjectId = objectId;
        }
    }

    private readonly struct CharacterDataPointer(FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCommonList.CharacterData* value)
    {
        public FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCommonList.CharacterData* Value { get; } = value;
    }
}
