using System;
using System.Collections.Generic;

namespace AetherOS.Sdk;

/// <summary>One person in the together-mode party. <see cref="Connected"/> is false while their account
/// holds no hub socket; rosters render them dimmed until the presence grace retracts them.</summary>
public sealed record PartyMemberInfo(Guid AccountId, string Name, bool IsHost, bool Connected);

/// <summary>What the party is doing right now: an app id, the activity's own id inside that app, and an
/// optional join code for it. Stamped server-side by the owning app's service; apps read it to know whether
/// the current activity is theirs.</summary>
public sealed record PartyActivityInfo(string AppId, Guid RefId, string? Code);

/// <summary>Read-only view of the OS-level together-mode party. Apps CONSUME the party (who is here, am I
/// the host); only the shell creates, joins, leaves or manages it. Everything here is safe to read on the
/// draw thread, and <see cref="Changed"/> is raised on the draw thread.</summary>
public interface IPartyState
{
    bool InParty { get; }

    Guid? PartyId { get; }

    /// <summary>The join code, for surfaces that show or share it. Null while not in a party.</summary>
    string? Code { get; }

    /// <summary>Whether the local account leads the party.</summary>
    bool AmHost { get; }

    /// <summary>The local account's id, for finding one's own seat in party rosters. Null before the
    /// session stamped it.</summary>
    Guid? OwnAccountId { get; }

    IReadOnlyList<PartyMemberInfo> Members { get; }

    /// <summary>The party's current activity, or null while it is idle. Null while not in a party.</summary>
    PartyActivityInfo? Activity { get; }

    /// <summary>Raised on the draw thread whenever the party changes in any way, including entering or
    /// leaving one.</summary>
    event Action? Changed;
}
