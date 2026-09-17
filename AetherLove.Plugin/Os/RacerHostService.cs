using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Services;
using AetherLove.Shared.Racing;
using AetherOS.Apps.Racer;

namespace AetherLove.Os;

/// <summary>The racer app's window on the plugin: hub passthroughs, the party run the push channel
/// holds, and the course music. The music and the creature sheets ride the Aetherling host's own audio
/// and asset plumbing rather than growing a second copy of either.</summary>
public sealed class RacerHostService(
    AetherHubContext hub,
    AetherlingHostService aetherling,
    Services.Together.LumiRaceRunStateService runState) : IRacerHost
{

    public Task<LumiRaceStateDto> GetStateAsync(CancellationToken ct = default) =>
        hub.GetLumiRaceStateAsync(ct);

    public Task<LumiCupStateDto> GetCupAsync(CancellationToken ct = default) => hub.GetLumiCupAsync(ct);
    public Task<LumiCupDto> EnterCupAsync(CancellationToken ct = default) => hub.EnterLumiCupAsync(ct);
    public Task<LumiCupDto> StartCupRaceAsync(Guid cupId, int completed, CancellationToken ct = default) => hub.StartLumiCupRaceAsync(cupId, completed, ct);
    public Task<LumiCupDto> FinishCupRaceAsync(Guid cupId, Guid raceId, CancellationToken ct = default) => hub.FinishLumiCupRaceAsync(cupId, raceId, ct);

    public Task<LumiRaceStartResultDto> StartRaceAsync(short difficulty, string? courseKey = null,
        CancellationToken ct = default) =>
        hub.StartLumiRaceAsync(difficulty, courseKey, ct);

    public Task<LumiRaceLogEntryDto[]> GetRaceLogAsync(int limit, CancellationToken ct = default) =>
        hub.GetLumiRaceLogAsync(limit, ct);

    public Task<LumiRacePackDto> RevealPackAsync(Guid packId, CancellationToken ct = default) =>
        hub.RevealLumiRacePackAsync(packId, ct);

    public Task<LumiRacePackDto> ClaimStampCardAsync(int cardNumber, CancellationToken ct = default) =>
        hub.ClaimLumiRaceStampCardAsync(cardNumber, ct);

    public Task<LumiRacePartyRunDto> StartPartyGatherAsync(CancellationToken ct = default) =>
        hub.StartLumiRacePartyGatherAsync(ct);

    public Task<LumiRacePartyRunDto> JoinPartyRunAsync(Guid runId, CancellationToken ct = default) =>
        hub.JoinLumiRacePartyRunAsync(runId, ct);

    public Task<LumiRacePartyRunDto> BeginPartyRunAsync(Guid runId, CancellationToken ct = default) =>
        hub.BeginLumiRacePartyRunAsync(runId, ct);

    public Task CancelPartyRunAsync(Guid runId, CancellationToken ct = default) =>
        hub.CancelLumiRacePartyRunAsync(runId, ct);

    public async Task<LumiRacePartyRunDto?> RefreshPartyRunAsync(CancellationToken ct = default)
    {
        var run = await hub.GetLumiRacePartyRunAsync(ct).ConfigureAwait(false);
        if (run is not null)
        {
            runState.ApplyRun(run);
        }
        return run;
    }

    public LumiRacePartyRunDto? PartyRun => runState.Run;

    public Task<LumiRacePrizeDto[]> GetPackPrizesAsync(Guid packId, CancellationToken ct = default) =>
        hub.GetLumiRacePackPrizesAsync(packId, ct);

    public Task<LumiRaceCardsDto> GetCardsAsync(CancellationToken ct = default) =>
        hub.GetLumiRaceCardsAsync(ct);

    public Task<LumiRaceCardsDto> SetCardHandAsync(LumiRaceCardHandDto hand, CancellationToken ct = default) =>
        hub.SetLumiRaceCardHandAsync(hand, ct);

    public Task<byte[]?> GetStoreProductImageAsync(Guid productId, CancellationToken ct = default) =>
        hub.GetStoreProductImageAsync(productId, ct);

    public string PetAssetRoot => aetherling.AssetRoot;

    public string SoundRoot => aetherling.SoundRoot;

    private static string? CourseTrack(string courseKey) => courseKey switch
    {
        "ember-dash" => "race_ember.ogg",
        "gale-route" => "race_gale.ogg",
        "duskwind-journey" => "race_dusk.ogg",
        "quiet-mile" => "race_quiet_mile.ogg",
        "levin-run" => "race_levin.ogg",
        "stone-ladder" => "race_stone.ogg",
        "frostline" => "race_frost.ogg",
        "long-burn" => "race_long_burn.ogg",
        "the-knot" => "race_the_knot.ogg",
        "riven-straight" => "race_riven_straight.ogg",
        "hollow-way" => "race_hollow_way.ogg",
        "squall-line" => "race_squall_line.ogg",
        "millrace" => "race_millrace.ogg",
        "glassway" => "race_glassway.ogg",
        "the-ring" => "race_the_ring.ogg",
        "the-bellows" => "race_the_bellows.ogg",
        "double-helix" => "race_double_helix.ogg",
        "quarry-drop" => "race_quarry_drop.ogg",
        "spillway" => "race_spillway.ogg",
        "skybridge" => "race_skybridge.ogg",
        "looking-glass" => "race_looking_glass.ogg",
        "grandstand-dash" => "race_grandstand_dash.ogg",
        _ => null,
    };

    public void StartCourseBgm(string courseKey)
    {
        if (CourseTrack(courseKey) is { } file)
        {
            aetherling.StartGameBgm(file);
            aetherling.SetBgmLevel(_volume);
        }
    }

    // The two apps share one player, and only one of them is ever playing: whoever starts a track
    // asserts its OWN level after starting it, or the race would run at the level somebody set for
    // the pet's loop and the pet's loop would come back at the race's.
    public void StartMenuBgm()
    {
        aetherling.StartGameBgm("race_selector.ogg", 1f, 0.7f);
        aetherling.SetBgmLevel(_volume);
    }

    public void StopBgm() => aetherling.StopBgm();

    public void FadeOutBgm(float seconds) => aetherling.StopBgm(seconds);

    public void SetBgmVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
        aetherling.SetBgmLevel(_volume);
    }

    /// <summary>The racer's own music level, kept here rather than on the shared host so the pet's
    /// stored level is never overwritten by a race.</summary>
    private float _volume = 1f;

    public string DescribeError(Exception ex) => HubErrorText.Localize(ex);
}
