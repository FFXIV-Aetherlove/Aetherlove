namespace AetherLove.Shared.Sparks;

/// <summary>Which earning pool a catalog action credits into. Routine counts against the weekly cap
/// (plus banked carry), Bonus against its own smaller cap, Exempt rides past the routine cap but stays
/// under the total weekly ceiling.</summary>
public enum SparkPool : short
{
    Routine = 0,
    Bonus = 1,
    Exempt = 2,
}

/// <summary>What a ledger entry did to the wallet. Stored per row so history stays renderable and
/// classifiable even after the action catalog changes.</summary>
public enum SparkTransactionKind : short
{
    RoutineEarn = 0,
    BonusEarn = 1,
    ExemptEarn = 2,
    Spend = 3,
    Adjustment = 4,

    /// <summary>A moderation debit that also reduces lifetime earned, unlike a plain adjustment.</summary>
    Clawback = 5,
}

/// <summary>The machine id behind every ledger entry. The client maps it to a localized label and the
/// admin panel to an English one, so the ledger never stores prose. Append-only forever.</summary>
public enum SparkAction : short
{
    Unknown = 0,
    AdminAdjust = 1,

    /// <summary>Connected to the server today; credited server-side, never client-reported.</summary>
    GameLogin = 2,

    /// <summary>Opened three different phone apps today; client-reported milestone.</summary>
    OpenedThreeApps = 3,

    /// <summary>Used the Market or Realtor app today; client-reported.</summary>
    MarketActivity = 4,

    /// <summary>Opened a venue detail today; credited server-side on the venue query.</summary>
    PlacesBrowsing = 5,

    /// <summary>Finished a round in an arcade app; client-reported, and the only action worth more than
    /// once a day.</summary>
    ArcadeGame = 6,

    /// <summary>Liked, reposted, quoted or bookmarked a yap; credited server-side.</summary>
    YapperEngage = 7,

    /// <summary>Posted a yap; credited server-side.</summary>
    YapperPost = 8,

    /// <summary>Replied to a yap; credited server-side.</summary>
    YapperReply = 9,

    /// <summary>Checked the Yapper feed today (opened the app); client-reported.</summary>
    YapperCheckFeed = 10,

    /// <summary>First Wayfinder find of the spark week; credited server-side on the winning submit.</summary>
    WayfinderFindFirst = 11,

    /// <summary>Second Wayfinder find of the spark week.</summary>
    WayfinderFindSecond = 12,

    /// <summary>Wayfinder finds three through five of the spark week.</summary>
    WayfinderFind = 13,

    /// <summary>A store checkout debit; RefId is the StorePurchase row id.</summary>
    StorePurchase = 14,

    /// <summary>Used the Groove remote today; client-reported, since Groove never touches the server.</summary>
    GrooveActivity = 15,

    /// <summary>Opened an Echo room; credited server-side, RefId is the room so one room pays once.</summary>
    EchoHosted = 16,

    /// <summary>Joined someone's Echo room; credited server-side, RefId is the room.</summary>
    EchoJoined = 17,

    /// <summary>Opened the Store today; client-reported.</summary>
    StoreVisit = 18,

    /// <summary>Opened the Wallet today; client-reported.</summary>
    WalletVisit = 19,

    /// <summary>Condensed an Aethercore. One per account for life; RefId is derived from the account so
    /// two clicks in the same instant can never book it twice.</summary>
    AetherlingAdopt = 20,

    /// <summary>Offered sparks to an Aethercore, moving it one stage up. RefId is derived from the
    /// account and the stage being left, so a stage can only ever be paid for once.</summary>
    AetherlingAttune = 21,

    /// <summary>Finished a round of one of the companion's own minigames today; client-reported.</summary>
    AetherlingGame = 22,
}
