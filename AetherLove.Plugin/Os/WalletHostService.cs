using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Shared.Sparks;
using AetherOS.Apps.Wallet;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AetherLove.Os;

/// <summary>Plugin-side implementation of the Wallet app's host bridge: sparks calls delegate to the hub
/// and degrade to null offline; currency amounts are read from the game on the framework thread. Caps come
/// from the game's own sheets rather than a frozen table, so a retuned currency stays correct.</summary>
public sealed class WalletHostService : IWalletHost, IDisposable
{
    /// <summary>The game's own Currency item category, which is the discovery set for the long tail.</summary>
    private const uint CurrencyUiCategory = 100;

    /// <summary>Above this a stack limit is a storage ceiling rather than a goal (gil, MGP, cowries), so
    /// those rows show a bare amount instead of a progress bar that can never move.</summary>
    private const uint CapBarMaxStackSize = 65_000;

    private static class CurrencyIds
    {
        public const uint Gil = 1;
        public const uint StormSeal = 20;
        public const uint SerpentSeal = 21;
        public const uint FlameSeal = 22;
        public const uint WolfMarks = 25;
        public const uint AlliedSeals = 27;
        public const uint Mgp = 29;
        public const uint CenturioSeals = 10307;
        public const uint Ventures = 21072;
        public const uint SacksOfNuts = 26533;
        public const uint BicolorGemstones = 26807;
        public const uint SkybuildersScrips = 28063;
        public const uint PurpleCrafterScrips = 33913;
        public const uint PurpleGathererScrips = 33914;
        public const uint TrophyCrystals = 36656;
        public const uint SeafarersCowrie = 37549;
        public const uint IslandersCowrie = 37550;
        public const uint OrangeCrafterScrips = 41784;
        public const uint OrangeGathererScrips = 41785;
    }

    /// <summary>Always shown, even at zero, because an empty row is itself the answer to "how many do I
    /// have". Tomestones and the Grand Company seal are resolved live instead of listed here.</summary>
    private static readonly (uint ItemId, WalletCurrencySection Section)[] FixedCurrencies =
    [
        (CurrencyIds.Gil, WalletCurrencySection.Common),
        (CurrencyIds.Mgp, WalletCurrencySection.Common),
        (CurrencyIds.Ventures, WalletCurrencySection.Common),
        (CurrencyIds.AlliedSeals, WalletCurrencySection.Hunt),
        (CurrencyIds.CenturioSeals, WalletCurrencySection.Hunt),
        (CurrencyIds.SacksOfNuts, WalletCurrencySection.Hunt),
        (CurrencyIds.WolfMarks, WalletCurrencySection.Pvp),
        (CurrencyIds.TrophyCrystals, WalletCurrencySection.Pvp),
        (CurrencyIds.PurpleCrafterScrips, WalletCurrencySection.Scrips),
        (CurrencyIds.PurpleGathererScrips, WalletCurrencySection.Scrips),
        (CurrencyIds.OrangeCrafterScrips, WalletCurrencySection.Scrips),
        (CurrencyIds.OrangeGathererScrips, WalletCurrencySection.Scrips),
        (CurrencyIds.SkybuildersScrips, WalletCurrencySection.Scrips),
        (CurrencyIds.BicolorGemstones, WalletCurrencySection.Field),
    ];

    /// <summary>Currencies the game files outside its Currency category, so discovery misses them; shown
    /// once held rather than always, to keep the tab short for players who never touched that content.</summary>
    private static readonly (uint ItemId, WalletCurrencySection Section)[] HeldOnlyCurrencies =
    [
        (CurrencyIds.IslandersCowrie, WalletCurrencySection.Field),
        (CurrencyIds.SeafarersCowrie, WalletCurrencySection.Field),
    ];

    private readonly AetherHubContext _hubClient;
    private readonly IFramework _framework;
    private readonly Dictionary<uint, ItemInfo> _itemInfo = new();
    private readonly Dictionary<uint, ISharedImmediateTexture?> _iconCache = new();

    private volatile List<uint>? _discovered;
    private int _discoveryStarted;
    private volatile int _snapshotVersion;

    private readonly record struct ItemInfo(string Name, uint IconId, uint StackSize);

    public WalletHostService(AetherHubContext hubClient, IFramework framework)
    {
        _hubClient = hubClient;
        _framework = framework;
        Plugin.ClientState.Login += OnLogin;
        Plugin.ClientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        Plugin.ClientState.Login -= OnLogin;
        Plugin.ClientState.Logout -= OnLogout;
    }

    public async Task<SparkWalletDto?> GetSparkWalletAsync(CancellationToken ct = default)
    {
        try
        {
            return await _hubClient.GetSparkWalletAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Wallet] Spark wallet fetch failed.");
            return null;
        }
    }

    public async Task<SparkLedgerPageDto?> GetSparkLedgerAsync(long? beforeSequence, int take, CancellationToken ct = default)
    {
        try
        {
            return await _hubClient.GetSparkLedgerAsync(beforeSequence, take, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Wallet] Spark ledger fetch failed.");
            return null;
        }
    }

    public bool InGame => Plugin.ClientState.IsLoggedIn;

    public int SnapshotVersion => _snapshotVersion;

    public async Task<IReadOnlyList<WalletCurrencyRow>> ReadCurrenciesAsync()
    {
        try
        {
            return await _framework.RunOnFrameworkThread(BuildRows).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Wallet] Currency read failed.");
            return [];
        }
    }

    private void OnLogin() => Invalidate();

    private void OnLogout(int type, int code) => Invalidate();

    private void Invalidate() => _snapshotVersion++;

    private unsafe List<WalletCurrencyRow> BuildRows()
    {
        var inventory = InventoryManager.Instance();
        if (inventory is null || !Plugin.ClientState.IsLoggedIn)
        {
            return [];
        }

        var rows = new List<WalletCurrencyRow>(FixedCurrencies.Length + HeldOnlyCurrencies.Length + 8);
        foreach (var (itemId, section) in FixedCurrencies)
        {
            if (BuildRow(inventory, itemId, section) is { } row)
            {
                rows.Add(row);
            }
        }
        if (BuildSealRow(inventory) is { } seal)
        {
            rows.Add(seal);
        }
        AddTomestoneRows(inventory, rows);
        foreach (var (itemId, section) in HeldOnlyCurrencies)
        {
            if (BuildRow(inventory, itemId, section) is { Amount: > 0 } held)
            {
                rows.Add(held);
            }
        }
        // Null until the off-thread sheet scan finishes; the long tail simply appears a moment later.
        if (_discovered is { } discovered)
        {
            foreach (var itemId in discovered)
            {
                if (BuildRow(inventory, itemId, WalletCurrencySection.Other) is { Amount: > 0 } extra)
                {
                    rows.Add(extra);
                }
            }
        }
        else
        {
            StartDiscovery();
        }
        return rows;
    }

    private unsafe WalletCurrencyRow? BuildRow(InventoryManager* inventory, uint itemId, WalletCurrencySection section)
    {
        if (ResolveItem(itemId) is not { } info)
        {
            return null;
        }
        var isGil = itemId == CurrencyIds.Gil;
        var amount = isGil ? (long)inventory->GetGil() : inventory->GetInventoryItemCount(itemId);
        return new WalletCurrencyRow(itemId, info.IconId, info.Name, amount, section, CapFor(info.StackSize),
            IsPrimary: isGil);
    }

    /// <summary>The player's own Grand Company seal. The item's stack size is the rank 19 ceiling, so the
    /// cap that actually binds comes from the current rank instead.</summary>
    private unsafe WalletCurrencyRow? BuildSealRow(InventoryManager* inventory)
    {
        var player = PlayerState.Instance();
        if (player is null)
        {
            return null;
        }
        var itemId = player->GrandCompany switch
        {
            1 => CurrencyIds.StormSeal,
            2 => CurrencyIds.SerpentSeal,
            3 => CurrencyIds.FlameSeal,
            _ => 0u,
        };
        if (itemId == 0 || ResolveItem(itemId) is not { } info)
        {
            return null;
        }
        var cap = UiHost.DataManager.GetExcelSheet<GrandCompanyRank>()
            .GetRowOrDefault(player->GetGrandCompanyRank())?.MaxSeals ?? 0;
        return new WalletCurrencyRow(itemId, info.IconId, info.Name,
            inventory->GetInventoryItemCount(itemId), WalletCurrencySection.Common, cap);
    }

    /// <summary>Every tomestone the game still has switched on, newest first. A live tomestone is one whose
    /// sheet row links to a Tomestones entry, which is how retired ones drop out on their own at a patch.</summary>
    private unsafe void AddTomestoneRows(InventoryManager* inventory, List<WalletCurrencyRow> rows)
    {
        var weeklyLimit = InventoryManager.GetLimitedTomestoneWeeklyLimit();
        var weeklyEarned = inventory->GetWeeklyAcquiredTomestoneCount();
        var active = new List<(uint ItemId, bool Weekly)>(4);
        foreach (var row in UiHost.DataManager.GetExcelSheet<TomestonesItem>())
        {
            if (row.Tomestones.RowId == 0 || row.Item.ValueNullable is not { } item || item.RowId == 0)
            {
                continue;
            }
            active.Add((item.RowId, row.Tomestones.ValueNullable is { WeeklyLimit: > 0 }));
        }

        // Descending item id puts the current tier first and leaves Poetics, the oldest and least
        // interesting, at the bottom.
        active.Sort((a, b) => b.ItemId.CompareTo(a.ItemId));
        foreach (var (itemId, weekly) in active)
        {
            if (ResolveItem(itemId) is not { } info)
            {
                continue;
            }
            rows.Add(new WalletCurrencyRow(itemId, info.IconId, info.Name,
                inventory->GetTomestoneCount(itemId), WalletCurrencySection.Tomestones, CapFor(info.StackSize),
                weekly ? weeklyEarned : null,
                weekly ? weeklyLimit : null));
        }
    }

    /// <summary>Everything the game itself files under Currency and we do not curate by hand, resolved once
    /// off the game thread: the sheet cannot change while the client is running, but walking every item in it
    /// would cost a visible frame. This is what surfaces allied society tokens, variant coins and retired
    /// scrip tiers without hardcoding a list that rots every patch.</summary>
    private void StartDiscovery()
    {
        if (Interlocked.Exchange(ref _discoveryStarted, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var curated = new HashSet<uint>();
                foreach (var (itemId, _) in FixedCurrencies)
                {
                    curated.Add(itemId);
                }
                foreach (var (itemId, _) in HeldOnlyCurrencies)
                {
                    curated.Add(itemId);
                }

                var found = new List<uint>();
                foreach (var item in UiHost.DataManager.GetExcelSheet<Item>())
                {
                    if (item.ItemUICategory.RowId == CurrencyUiCategory && !curated.Contains(item.RowId))
                    {
                        found.Add(item.RowId);
                    }
                }
                _discovered = found;
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[Wallet] Currency discovery failed; the long tail stays hidden.");
                Interlocked.Exchange(ref _discoveryStarted, 0);
            }
        });
    }

    private static long CapFor(uint stackSize) => stackSize is > 0 and <= CapBarMaxStackSize ? stackSize : 0;

    private ItemInfo? ResolveItem(uint itemId)
    {
        if (_itemInfo.TryGetValue(itemId, out var cached))
        {
            return cached;
        }
        if (UiHost.DataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId) is not { } item)
        {
            UiHost.Log.Warning("[Wallet] Currency item {ItemId} missing from the Item sheet.", itemId);
            return null;
        }
        var info = new ItemInfo(item.Name.ExtractText(), item.Icon, item.StackSize);
        _itemInfo[itemId] = info;
        return info;
    }

    /// <summary>The shared texture is cached, never its wrap handle: shared wraps are only valid for the
    /// frame they were resolved in.</summary>
    public ImTextureID? GetCurrencyIcon(uint iconId)
    {
        if (iconId == 0)
        {
            return null;
        }
        if (!_iconCache.TryGetValue(iconId, out var tex))
        {
            try
            {
                tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, $"[Wallet] Failed to load icon {iconId}.");
                tex = null;
            }
            _iconCache[iconId] = tex;
        }
        return tex?.GetWrapOrDefault()?.Handle;
    }
}
