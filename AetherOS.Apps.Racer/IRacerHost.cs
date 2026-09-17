using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Racing;

namespace AetherOS.Apps.Racer;

/// <summary>Everything the racer app needs from the plugin: the hub passthroughs, the two asset roots,
/// and the course music. Declared here and implemented plugin-side, so the app references nothing above
/// the SDK.</summary>
public interface IRacerHost
{
    Task<LumiRaceStateDto> GetStateAsync(CancellationToken ct = default);

    /// <summary>The Starlight Cup: this week's reservation, or the older unfinished one that blocks it.</summary>
    Task<LumiCupStateDto> GetCupAsync(CancellationToken ct = default);

    /// <summary>Reserves this week's cup. The server picks the week, so a stale screen cannot enter a
    /// week that has already rolled over.</summary>
    Task<LumiCupDto> EnterCupAsync(CancellationToken ct = default);
    Task<LumiCupDto> StartCupRaceAsync(Guid cupId, int completed, CancellationToken ct = default);
    Task<LumiCupDto> FinishCupRaceAsync(Guid cupId, Guid raceId, CancellationToken ct = default);

    /// <summary>Races the caller's standing offer at that grade. <paramref name="courseKey"/> names the
    /// course the card showed, which the server checks against the offer it dealt.</summary>
    Task<LumiRaceStartResultDto> StartRaceAsync(short difficulty, string? courseKey = null,
        CancellationToken ct = default);

    /// <summary>The caller's own finished races, newest first, at most <paramref name="limit"/> of them.</summary>
    Task<LumiRaceLogEntryDto[]> GetRaceLogAsync(int limit, CancellationToken ct = default);

    Task<LumiRacePackDto> RevealPackAsync(Guid packId, CancellationToken ct = default);
    Task<LumiRacePackDto> ClaimStampCardAsync(int cardNumber, CancellationToken ct = default);

    Task<LumiRacePartyRunDto> StartPartyGatherAsync(CancellationToken ct = default);

    Task<LumiRacePartyRunDto> JoinPartyRunAsync(Guid runId, CancellationToken ct = default);

    Task<LumiRacePartyRunDto> BeginPartyRunAsync(Guid runId, CancellationToken ct = default);

    Task CancelPartyRunAsync(Guid runId, CancellationToken ct = default);

    Task<LumiRacePartyRunDto?> RefreshPartyRunAsync(CancellationToken ct = default);

    /// <summary>The party run the push channel last delivered, if any. The app reads it on draw; the
    /// plugin replaces it whole.</summary>
    LumiRacePartyRunDto? PartyRun { get; }

    /// <summary>A dealt pack's two prizes, with the copy and art its cards show.</summary>
    Task<LumiRacePrizeDto[]> GetPackPrizesAsync(Guid packId, CancellationToken ct = default);

    /// <summary>The racing card album: every owned card and the saved hand.</summary>
    Task<LumiRaceCardsDto> GetCardsAsync(CancellationToken ct = default);

    /// <summary>Saves the whole proposed hand. The server checks it as one and refuses the whole change on
    /// the first fault, leaving the stored hand as it was; the reply is the album after the save.</summary>
    Task<LumiRaceCardsDto> SetCardHandAsync(LumiRaceCardHandDto hand, CancellationToken ct = default);

    /// <summary>A store product's art, so a prize card can show the item rather than its name.</summary>
    Task<byte[]?> GetStoreProductImageAsync(Guid productId, CancellationToken ct = default);

    /// <summary>Where the creature sheets live (the Media/unknown tree PetKit loads from).</summary>
    string PetAssetRoot { get; }

    /// <summary>Where the one-shot sound effects live (Media/sfx).</summary>
    string SoundRoot { get; }

    /// <summary>Starts the course's track from Media/bgm. A missing file is silence, never an error.
    /// The game's own BGM is muted while ours plays, restored on stop, and only ever a mute we made
    /// ourselves.</summary>
    void StartCourseBgm(string courseKey);

    /// <summary>The app's own theme, played softer than the race tracks, on the home and stats pages.</summary>
    void StartMenuBgm();

    void StopBgm();

    /// <summary>Fades the loop out over the given seconds, for a scene that ends on its own beat.</summary>
    void FadeOutBgm(float seconds);

    /// <summary>How loud the course track plays, 0 to 1, under the music's own level. The racer keeps
    /// its own, stored beside its mute, so turning the race down leaves the pet's own loop alone.</summary>
    void SetBgmVolume(float volume);

    string DescribeError(Exception ex);
}
