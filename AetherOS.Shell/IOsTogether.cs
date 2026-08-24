using System;
using System.Collections.Generic;

namespace AetherLove.Os;

/// <summary>One roster row for the shell's party surfaces. <see cref="AvatarImage"/> and
/// <see cref="FrameRef"/> come straight off the party snapshot so every surface can draw the member the
/// way the rest of the OS draws people: avatar disc, equipped ring, badges on top.</summary>
public sealed record OsPartyMember(
    Guid AccountId, string Name, bool IsHost, bool Connected,
    string? FrameRef = null, byte[]? AvatarImage = null);

/// <summary>The party's current activity for the shell's surfaces: the owning app, the activity's id in
/// that app, and an optional join code (an Echo room's code for the one-tap join).</summary>
public sealed record OsPartyActivity(string AppId, Guid RefId, string? Code);

/// <summary>One chat line for the dock. <see cref="Seq"/> increases monotonically per arriving line, so
/// the dock can stamp bubbles for lines it has not shown yet without comparing contents.
/// <see cref="IsSystem"/> is a server-authored notice; <see cref="Text"/> then carries only the subject's
/// name and the surface phrases the sentence itself. <see cref="Kind"/> picks the sentence (null is the
/// join notice, else the activity's app id), and <see cref="RefId"/> / <see cref="Code"/> say what a tap
/// opens.</summary>
public sealed record OsPartyChatLine(
    long Seq, Guid AccountId, string Name, string Text, bool IsOwn, bool IsSystem = false,
    string? Kind = null, Guid? RefId = null, string? Code = null);

/// <summary>The shell's window onto together mode, implemented plugin-side over the client party state and
/// the hub. The shell owns every party surface (status indicator, shade card, edge dock); apps only ever
/// see the read-only capability. Actions are fire-and-forget: <see cref="Busy"/> covers the in-flight gap
/// and <see cref="ErrorKey"/> carries the last failure as a localization key until the next action.</summary>
public interface IOsTogether
{
    /// <summary>Whether together mode is usable right now (connected, feature enabled).</summary>
    bool Available { get; }

    bool InParty { get; }

    /// <summary>True while a just-ended party's farewell should show; <see cref="DismissEnded"/> clears it.</summary>
    bool PartyEnded { get; }

    string? Code { get; }

    /// <summary>The live party's id, null while solo. Rides share items so a target app can name the party
    /// to the server (the hangout publish) without a second lookup.</summary>
    Guid? PartyId { get; }

    bool AmHost { get; }

    IReadOnlyList<OsPartyMember> Members { get; }

    /// <summary>What the party is doing right now, or null while it is idle.</summary>
    OsPartyActivity? Activity { get; }

    int MaxMembers { get; }

    bool Busy { get; }

    /// <summary>Localization key of the last failed action, null when the last action succeeded.</summary>
    string? ErrorKey { get; }

    /// <summary>The party chat, oldest first, capped client-side.</summary>
    IReadOnlyList<OsPartyChatLine> ChatLines { get; }

    /// <summary>Lines from others not yet shown by a chat surface; badges on the status light.</summary>
    int UnreadChat { get; }

    void SendChat(string text);

    void MarkChatRead();

    void Create();

    void Join(string code);

    void Leave();

    void End();

    void Kick(Guid accountId);

    void DismissEnded();

    /// <summary>Whether the together explainer has already been shown once; the widget sets it when the
    /// last page is dismissed.</summary>
    bool OnboardingSeen { get; set; }

    /// <summary>Whether this account has a hatched Aetherling, which is the only case where the pet half of
    /// the explainer means anything.</summary>
    bool HasPet { get; }

    /// <summary>Sending half of party pets: whether party members may see this account's Aetherling. Lives
    /// on the server with the pet, so it follows the account to every device. Setting it is
    /// fire-and-forget, like the party actions.</summary>
    bool ShareMyPet { get; set; }

    /// <summary>Receiving half: whether the party's Aetherlings gather around your own. Local and
    /// persisted.</summary>
    bool ShowPartyPets { get; set; }

    /// <summary>How big those pets stand, an index into the floating pet's size ladder.</summary>
    int PartyPetSize { get; set; }

    /// <summary>How many sizes that index may take.</summary>
    int PartyPetSizeCount { get; }
}
