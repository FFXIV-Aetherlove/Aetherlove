using System;
using System.Collections.Generic;
using AetherOS.Sdk;

namespace AetherOS.Apps.Together;

/// <summary>One party member as the app sees them. The avatar rides inline as bytes, the way the roster
/// card gets it.</summary>
public sealed record TogetherMember(
    Guid AccountId, string Name, bool IsHost, bool Connected, string? FrameRef, byte[]? AvatarImage,
    short? PetStage, string? PetPalette, IReadOnlyList<string>? PetAccessories);

public sealed record TogetherActivity(string AppId, Guid RefId, string? Code);

/// <summary>What the Together app needs from the plugin: the same window the shell's own party surfaces
/// use, plus the pet renderer for the tour. Declared here and implemented plugin-side, so the app never
/// references the shell or the hub. Actions are fire-and-forget; <see cref="Busy"/> covers the gap and
/// <see cref="ErrorKey"/> carries the last refusal as a localization key.</summary>
public interface ITogetherHost
{
    bool Available { get; }

    bool InParty { get; }

    bool PartyEnded { get; }

    string? Code { get; }

    Guid? PartyId { get; }

    bool AmHost { get; }

    Guid? OwnAccountId { get; }

    IReadOnlyList<TogetherMember> Members { get; }

    TogetherActivity? Activity { get; }

    int MaxMembers { get; }

    bool Busy { get; }

    string? ErrorKey { get; }

    void Create();

    void Join(string code);

    void Leave();

    void End();

    void Kick(Guid accountId);

    void DismissEnded();

    /// <summary>Opens the share sheet on the party invite, with the same target rules the widget card uses.</summary>
    void Invite();

    /// <summary>Follows the party's live activity: the room by its join intent, the hunt by its app.</summary>
    void OpenActivity(TogetherActivity activity);

    /// <summary>The shell's own explainer flag, so the widget card stops intercepting once the app's tour ran.</summary>
    bool OnboardingSeen { get; set; }

    bool HasPet { get; }

    bool ShareMyPet { get; set; }

    bool ShowPartyPets { get; set; }

    int PartyPetSize { get; set; }

    int PartyPetSizeCount { get; }

    /// <summary>The pet app's renderer, null until that app exists. Read per frame, never cached.</summary>
    IPetRenderer? Pets { get; }
}
