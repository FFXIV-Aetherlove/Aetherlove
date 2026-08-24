using System;
using System.Collections.Generic;
using AetherLove.Services.Together;
using AetherOS.Apps.Together;
using AetherOS.Sdk;

namespace AetherLove.Os;

/// <summary>The Together app's host bridge: the shell's own party window (<see cref="IOsTogether"/>)
/// handed down to an app, plus the member pets off the client party state, the pet renderer off the
/// Aetherling host, and the two shell-side actions an app cannot do itself (the invite sheet with its
/// host-only hangout rule, and the one-tap into a live activity).</summary>
public sealed class TogetherHostService(
    IOsTogether together,
    TogetherStateService state,
    AetherlingHostService aetherling,
    ShareService share,
    OsShell shell) : ITogetherHost
{
    private static readonly string[] HangoutsOnly = ["hangouts"];

    public bool Available => together.Available;

    public bool InParty => together.InParty;

    public bool PartyEnded => together.PartyEnded;

    public string? Code => together.Code;

    public Guid? PartyId => together.PartyId;

    public bool AmHost => together.AmHost;

    public Guid? OwnAccountId => state.OwnAccountId;

    public IReadOnlyList<TogetherMember> Members
    {
        get
        {
            var members = state.Members;
            var result = new TogetherMember[members.Count];
            for (var i = 0; i < members.Count; i++)
            {
                var m = members[i];
                result[i] = new TogetherMember(m.AccountId, m.DisplayName, m.IsHost, m.Connected, m.FrameRef,
                    m.AvatarImage, m.Pet?.Stage, m.Pet?.Palette, m.Pet?.Accessories);
            }
            return result;
        }
    }

    public TogetherActivity? Activity =>
        together.Activity is { } a ? new TogetherActivity(a.AppId, a.RefId, a.Code) : null;

    public int MaxMembers => together.MaxMembers;

    public bool Busy => together.Busy;

    public string? ErrorKey => together.ErrorKey;

    public void Create() => together.Create();

    public void Join(string code) => together.Join(code);

    public void Leave() => together.Leave();

    public void End() => together.End();

    public void Kick(Guid accountId) => together.Kick(accountId);

    public void DismissEnded() => together.DismissEnded();

    public void Invite()
    {
        if (together.Code is not { Length: > 0 } code)
        {
            return;
        }
        string? hostName = null;
        foreach (var member in together.Members)
        {
            if (member.IsHost)
            {
                hostName = member.Name;
                break;
            }
        }
        // Anyone may invite; only the host may publish the party as a hangout.
        var exclude = together.AmHost ? null : HangoutsOnly;
        share.Offer(new ShareItem
        {
            Type = ShareTypes.Party,
            RefId = together.PartyId?.ToString("D") ?? string.Empty,
            Title = hostName ?? string.Empty,
            Subtitle = code,
            SourceAppId = "together",
        }, Services.Localization.Loc.T("os.party_title"), exclude);
    }

    public void OpenActivity(TogetherActivity activity)
    {
        if (activity.AppId == "echo" && activity.Code is { Length: > 0 } code)
        {
            shell.SendIntent("echo", OsIntents.CreateRoomJoin(OsIntents.EchoJoin, activity.RefId, code));
            return;
        }
        shell.OpenApp(activity.AppId);
    }

    public bool OnboardingSeen
    {
        get => together.OnboardingSeen;
        set => together.OnboardingSeen = value;
    }

    public bool HasPet => together.HasPet;

    public bool ShareMyPet
    {
        get => together.ShareMyPet;
        set => together.ShareMyPet = value;
    }

    public bool ShowPartyPets
    {
        get => together.ShowPartyPets;
        set => together.ShowPartyPets = value;
    }

    public int PartyPetSize
    {
        get => together.PartyPetSize;
        set => together.PartyPetSize = value;
    }

    public int PartyPetSizeCount => together.PartyPetSizeCount;

    public IPetRenderer? Pets => aetherling.PetRenderer;
}
