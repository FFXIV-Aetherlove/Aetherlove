using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Services.Together;
using AetherLove.Shared;

namespace AetherLove.Os;

/// <summary>The shell's together-mode bridge: read view over the client party state, fire-and-forget
/// actions into the hub. Continuations only touch the state service (thread-safe by design) and the
/// volatile busy/error fields; nothing here reaches ImGui off the draw thread.</summary>
public sealed class OsTogetherService(
    TogetherStateService state,
    AetherHubContext hub,
    Config.Configuration config,
    AetherlingHostService aetherling) : IOsTogether
{
    private volatile bool _busy;
    private volatile string? _errorKey;

    public bool Available => hub.IsConnected;

    public bool InParty => state.EndReason is null && state.Party is not null;

    public bool PartyEnded => state.EndReason is not null;

    public string? Code => state.Party?.Code;

    public Guid? PartyId => state.Party?.Id;

    public bool AmHost => state.AmHost;

    public IReadOnlyList<OsPartyMember> Members
    {
        get
        {
            var members = state.Members;
            var result = new OsPartyMember[members.Count];
            for (var i = 0; i < members.Count; i++)
            {
                var m = members[i];
                result[i] = new OsPartyMember(m.AccountId, m.DisplayName, m.IsHost, m.Connected,
                    m.FrameRef, m.AvatarImage);
            }
            return result;
        }
    }

    public OsPartyActivity? Activity =>
        state.Activity is { } activity ? new OsPartyActivity(activity.AppId, activity.RefId, activity.Code) : null;

    public int MaxMembers => state.Party?.MaxMembers ?? Shared.Together.TogetherLimits.MaxMembers;

    public bool Busy => _busy;

    public string? ErrorKey => _errorKey;

    public bool OnboardingSeen
    {
        get => config.OsSettings.TogetherOnboardingSeen;
        set
        {
            config.OsSettings.TogetherOnboardingSeen = value;
            config.Save();
        }
    }

    public bool HasPet => aetherling.Snapshot is { HatchedAtUtc: not null };

    /// <summary>Reads the pet snapshot and writes through the hub, because this half of the switch belongs
    /// to the account rather than to the device: signing in elsewhere must not un-share a pet.
    /// <para>The switch moves the moment it is pressed and only falls back if the write fails, rather than
    /// waiting on a round trip: a toggle that stays where it was for half a second reads as broken. It also
    /// deliberately does NOT go through <see cref="Run"/>, which drops anything pressed while a party action
    /// is in flight.</para></summary>
    public bool ShareMyPet
    {
        get => _sharePending ?? aetherling.Snapshot?.SharesWithParty ?? true;
        set
        {
            _sharePending = value;
            _ = Task.Run(async () =>
            {
                try
                {
                    await aetherling.SetPartySharingAsync(value).ConfigureAwait(false);
                    _errorKey = null;
                }
                catch (Exception ex)
                {
                    _errorKey = MapError(ex);
                    UiHost.Log.Warning(ex, "[Together] Party pet sharing could not be saved.");
                }
                finally
                {
                    _sharePending = null;
                }
            });
        }
    }

    private volatile object? _sharePendingBox;

    private bool? _sharePending
    {
        get => _sharePendingBox is bool b ? b : null;
        set => _sharePendingBox = value;
    }

    public bool ShowPartyPets
    {
        get => config.OsSettings.PartyPetsShown;
        set
        {
            config.OsSettings.PartyPetsShown = value;
            config.Save();
        }
    }

    public int PartyPetSize
    {
        get => config.OsSettings.PartyPetSize;
        set
        {
            config.OsSettings.PartyPetSize = Math.Clamp(value, 0, PartyPetSizeCount - 1);
            config.Save();
        }
    }

    public int PartyPetSizeCount => 5;

    public void Create() => Run(async () => state.ApplySnapshot(await hub.CreateTogetherPartyAsync().ConfigureAwait(false)));

    public void Join(string code) => Run(async () => state.ApplySnapshot(await hub.JoinTogetherPartyAsync(code).ConfigureAwait(false)));

    public void Leave() => Run(async () =>
    {
        if (state.Party is { } party)
        {
            await hub.LeaveTogetherPartyAsync(party.Id).ConfigureAwait(false);
            state.Clear();
        }
    });

    public void End() => Run(async () =>
    {
        if (state.Party is { } party)
        {
            await hub.EndTogetherPartyAsync(party.Id).ConfigureAwait(false);
            state.Clear();
        }
    });

    public void Kick(Guid accountId) => Run(async () =>
    {
        if (state.Party is { } party)
        {
            await hub.KickTogetherMemberAsync(party.Id, accountId).ConfigureAwait(false);
        }
    });

    public void DismissEnded() => state.Clear();

    public IReadOnlyList<OsPartyChatLine> ChatLines
    {
        get
        {
            var entries = state.ChatLines;
            var own = state.OwnAccountId;
            var result = new OsPartyChatLine[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                var line = entries[i].Line;
                result[i] = new OsPartyChatLine(
                    entries[i].Seq, line.AccountId, line.DisplayName, line.Text, own == line.AccountId,
                    line.IsSystem, line.Kind, line.RefId, line.Code);
            }
            return result;
        }
    }

    public int UnreadChat => state.UnreadChat;

    public void MarkChatRead() => state.MarkChatRead();

    /// <summary>Chat rides its own in-flight flag rather than <see cref="Run"/>'s, so a slow send never
    /// greys out the party buttons (and vice versa). Failures are silent besides the log: a lost line in
    /// an ephemeral chat is not worth an error surface.</summary>
    public void SendChat(string text)
    {
        var trimmed = text.Trim();
        if (_chatSending || trimmed.Length == 0 || state.Party is not { } party)
        {
            return;
        }
        _chatSending = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await hub.SendTogetherChatAsync(party.Id, trimmed).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[Together] Chat send failed.");
            }
            finally
            {
                _chatSending = false;
            }
        });
    }

    private volatile bool _chatSending;

    private void Run(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        _errorKey = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorKey = MapError(ex);
                UiHost.Log.Warning(ex, "[Together] Party action failed.");
            }
            finally
            {
                _busy = false;
            }
        });
    }

    private static string MapError(Exception ex) => ex.Message switch
    {
        _ when ex.Message.Contains(HubErrors.TogetherPartyFull) => "os.party_error_full",
        _ when ex.Message.Contains(HubErrors.TogetherKicked) => "os.party_error_kicked",
        _ when ex.Message.Contains(HubErrors.TogetherAlreadyInParty) => "os.party_error_in_party",
        _ when ex.Message.Contains(HubErrors.TogetherLivePartyExists) => "os.party_error_in_party",
        _ when ex.Message.Contains(HubErrors.TogetherPartyNotFound) => "os.party_error_not_found",
        _ => "os.party_error_generic",
    };
}
