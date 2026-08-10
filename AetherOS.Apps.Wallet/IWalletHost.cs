using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Sparks;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Wallet;

/// <summary>Which group of the currencies tab a row belongs to; the host assigns it so the app never
/// needs to know item ids. Sections render in this order.</summary>
public enum WalletCurrencySection
{
    Common = 0,
    Tomestones = 1,
    Hunt = 2,
    Pvp = 3,
    Scrips = 4,
    Field = 5,

    /// <summary>Anything the game files under Currency that is not curated above, shown only once held.</summary>
    Other = 6,
}

/// <summary>One game currency. The name and icon come from the game's own localized data. <see cref="Cap"/>
/// is zero when the item's stack limit is too large to read as a goal (gil, MGP, cowries), so callers treat
/// zero as "no cap to show". The weekly fields are set only on the tomestone that carries a weekly
/// acquisition limit, and <see cref="IsPrimary"/> marks the headline currency the tab leads with.</summary>
public sealed record WalletCurrencyRow(
    uint ItemId,
    uint IconId,
    string Name,
    long Amount,
    WalletCurrencySection Section,
    long Cap = 0,
    int? WeeklyCount = null,
    int? WeeklyCap = null,
    bool IsPrimary = false)
{
    public bool HasCap => Cap > 0;

    public bool AtCap => Cap > 0 && Amount >= Cap;

    public bool HasWeekly => WeeklyCount is not null && WeeklyCap > 0;
}

/// <summary>The Wallet app's host bridge: sparks data over the hub, plus read-only in-game currency
/// amounts. The server owns every spark amount; the game owns every currency amount.</summary>
public interface IWalletHost
{
    /// <summary>Wallet snapshot with caps and the earning catalog; null on any hub failure.</summary>
    Task<SparkWalletDto?> GetSparkWalletAsync(CancellationToken ct = default);

    /// <summary>One keyset page of the spark ledger, newest first; null on any hub failure.</summary>
    Task<SparkLedgerPageDto?> GetSparkLedgerAsync(long? beforeSequence, int take, CancellationToken ct = default);

    bool InGame { get; }

    /// <summary>Bumped on login and logout. The phone survives a character switch without any app
    /// lifecycle callback firing, so a screen holding an older value must throw its snapshot away or it
    /// keeps showing the previous character's currencies.</summary>
    int SnapshotVersion { get; }

    /// <summary>Snapshot of the player's currency amounts, empty while logged out.</summary>
    Task<IReadOnlyList<WalletCurrencyRow>> ReadCurrenciesAsync();

    /// <summary>Resolves a game icon for the current frame; never cache the returned handle.</summary>
    ImTextureID? GetCurrencyIcon(uint iconId);
}
