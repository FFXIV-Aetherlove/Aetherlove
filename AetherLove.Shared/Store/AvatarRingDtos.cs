using System;
using MessagePack;

namespace AetherLove.Shared.Store;

/// <summary>Which avatar an equip call targets. Wire + storage values, append-only, never renumber.</summary>
public enum AvatarRingSurface : short
{
    /// <summary>The OS avatar: Settings, messenger, hangouts, venue reviews, the shade.</summary>
    Os = 0,

    /// <summary>The acting AetherLove dating profile.</summary>
    Love = 1,

    /// <summary>The Yapper profile.</summary>
    Yapper = 2,
}

/// <summary>One owned avatar ring for the pickers: the stable item ref plus all six names (Flair
/// pattern, the client picks the language live). Delisted products are still included, since
/// purchases are permanent.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AvatarRingDto(
    string FrameRef,
    Guid ProductId,
    string NameEnglish,
    string? NameSpanish,
    string? NameFrench,
    string? NameRussian,
    string? NameGerman,
    string? NamePortuguese);
