using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Wallet;

/// <summary>Who is logged in, as far as the wallet cares: the content id is the stable key, the name and
/// world are what the chip shows.</summary>
public sealed record WalletCharacterIdentity(ulong ContentId, string Name, string World);

/// <summary>One currency as remembered for a character who is not logged in. A stored twin of
/// <see cref="WalletCurrencyRow"/> with settable properties so it round-trips through the app storage's
/// JSON; <see cref="ToRow"/> hands back the live shape every screen already draws.</summary>
public sealed class WalletStoredCurrency
{
    public uint ItemId { get; set; }

    public uint IconId { get; set; }

    public string Name { get; set; } = "";

    public long Amount { get; set; }

    public int Section { get; set; }

    public long Cap { get; set; }

    public int? WeeklyCount { get; set; }

    public int? WeeklyCap { get; set; }

    public bool IsPrimary { get; set; }

    public static WalletStoredCurrency From(WalletCurrencyRow row) => new()
    {
        ItemId = row.ItemId,
        IconId = row.IconId,
        Name = row.Name,
        Amount = row.Amount,
        Section = (int)row.Section,
        Cap = row.Cap,
        WeeklyCount = row.WeeklyCount,
        WeeklyCap = row.WeeklyCap,
        IsPrimary = row.IsPrimary,
    };

    public WalletCurrencyRow ToRow() => new(ItemId, IconId, Name, Amount, (WalletCurrencySection)Section,
        Cap, WeeklyCount, WeeklyCap, IsPrimary);
}

/// <summary>A character's currencies as last seen. Kept per content id on this device only (the server
/// never learns a gil amount), refreshed every time the host reads that character live, and shown in the
/// wallet while somebody else is logged in. Nothing here is authoritative for anything but the display.</summary>
public sealed class WalletCharacterSnapshot
{
    public ulong ContentId { get; set; }

    public string Name { get; set; } = "";

    public string World { get; set; } = "";

    public DateTimeOffset TakenAtUtc { get; set; }

    public List<WalletStoredCurrency> Currencies { get; set; } = [];

    public WalletCharacterIdentity Identity => new(ContentId, Name, World);

    public IReadOnlyList<WalletCurrencyRow> Rows()
    {
        var rows = new List<WalletCurrencyRow>(Currencies.Count);
        foreach (var c in Currencies)
        {
            rows.Add(c.ToRow());
        }
        return rows;
    }
}
