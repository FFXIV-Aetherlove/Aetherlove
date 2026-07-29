using System;
using AetherLove.Services.Localization;
using AetherLove.Services.Market;
using AetherLove.Windows;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Microsoft.Extensions.DependencyInjection;

namespace AetherLove.Os;

/// <summary>Adds a "view on the market board" entry to the game's inventory context menus, opening the
/// Market app on that item. Only offered for marketable items once the item index is ready.</summary>
public sealed class MarketContextMenuService : IDisposable
{
    private const ushort PrefixBlue = 37;

    private readonly MarketItemIndex _index;
    private readonly IServiceProvider _services;

    public MarketContextMenuService(MarketItemIndex index, IServiceProvider services)
    {
        _index = index;
        _services = services;
        try
        {
            Plugin.ContextMenu.OnMenuOpened += OnMenuOpened;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[MarketContextMenu] Registration failed.");
        }
    }

    public void Dispose()
    {
        try
        {
            Plugin.ContextMenu.OnMenuOpened -= OnMenuOpened;
        }
        catch
        {
            // Already gone on shutdown.
        }
    }

    private static uint NormalizeItemId(uint itemId)
    {
        if (itemId >= 1_000_000)
        {
            return itemId - 1_000_000;
        }
        if (itemId >= 500_000)
        {
            return itemId - 500_000;
        }
        return itemId;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        try
        {
            if (args.MenuType != ContextMenuType.Inventory
                || args.Target is not MenuTargetInventory { TargetItem: { } item })
            {
                return;
            }
            var itemId = NormalizeItemId(item.ItemId);
            if (itemId == 0)
            {
                return;
            }
            if (!_index.Ready)
            {
                _index.EnsureBuildStarted();
                return;
            }
            if (!_index.TryGet(itemId, out _))
            {
                return;
            }

            args.AddMenuItem(new MenuItem
            {
                Name = Loc.T("chat.market_card_view"),
                Prefix = SeIconChar.BoxedLetterA,
                PrefixColor = PrefixBlue,
                OnClicked = _ => OpenItem(itemId),
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[MarketContextMenu] Menu handling failed: {ex.Message}");
        }
    }

    private void OpenItem(uint itemId)
    {
        try
        {
            _services.GetService<MainPluginWindow>()?.OpenToMarketItem(itemId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[MarketContextMenu] Open failed.");
        }
    }
}
