using System;
using MessagePack;

namespace AetherLove.Shared.Aetherling;

/// <summary>What a wedge on the daily wheel stands for. APPEND-ONLY: the value is stored on every spin row.</summary>
public enum AetherlingWheelEntryKind : short
{
    Unknown = 0,

    /// <summary>One crystal of the element named by the wedge's ref ("fire").</summary>
    Crystal = 1,

    /// <summary>One accessory the account does not own yet, from the store category named by the wedge's
    /// ref ("acc-head"). The item is chosen by the server at the spin.</summary>
    Category = 2,
}

/// <summary>One of the sixteen wedges. <see cref="Percent"/> is this wedge's share of the roll after the
/// day's normalisation, so the client and the admin page show the same number.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingWheelWedgeDto(short Index, short Kind, string Ref, double Percent);

/// <summary>Today's spin, once it has happened. The prize is already in the inventory: the reveal stamp is
/// bookkeeping for the scratch, never the grant.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingWheelResultDto(
    short WedgeIndex,
    short PrizeKind,
    string PrizeRef,
    DateTimeOffset RolledAtUtc,
    DateTimeOffset? RevealedAtUtc);

/// <summary>The wheel as this account sees it today. Wedges are composed per account per UTC day, so two
/// players hold different mystery categories and the same player holds the same wheel all day.
/// <para><see cref="Unlimited"/> is the staff carve-out: an admin may spin again at once, so the client
/// never greys the button for one and every spin is fresh.</para></summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingWheelDto(
    bool Enabled,
    DateTimeOffset ServerNowUtc,
    DateTimeOffset NextSpinAtUtc,
    AetherlingWheelWedgeDto[] Wedges,
    AetherlingWheelResultDto? Today,
    bool Unlimited = false);

/// <summary>The one bit the pet's home page needs without a round trip: whether today's spin is used, and
/// when the next one opens. Null on the core snapshot while the wheel is off or the pet is not grown.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingWheelStateDto(bool SpunToday, DateTimeOffset NextSpinAtUtc);
