using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherOS.Sdk;

namespace AetherOS.Apps.Aetherling;

/// <summary>What the app needs from the plugin. Declared here and implemented over there, so the app never
/// references the hub, the session or the audio stack.</summary>
public interface IAetherlingHost
{
    /// <summary>The core as of the last login or call, without a round trip. Null means the account has
    /// never bought one, which is the state almost everybody is in.</summary>
    AetherlingDto? Snapshot { get; }

    /// <summary>Whether the loop is silenced. Persisted by the app, honoured across restarts.</summary>
    bool BgmMuted { get; set; }

    /// <summary>Where the ceremony sheets live, so the app can hand paths to the texture cache.</summary>
    string AssetRoot { get; }

    /// <summary>The floating pet, handed over once at startup. The app cannot open a window over the game
    /// itself, and the host has no business knowing what is being drawn in it.</summary>
    IAetherlingOverlay? Overlay { get; set; }

    /// <summary>Textures for anything drawn outside the phone: the app's own draws get theirs from the frame
    /// context, which does not exist out there.</summary>
    ITextureCache Textures { get; }

    /// <summary>Whether the player has asked for less motion, read outside a frame context.</summary>
    bool ReduceMotion { get; }

    /// <summary>Brings the phone up full size on this app, restoring it from the minimised bubble if that is
    /// where it is. The floating creature lives outside the phone, so its menu needs a way back in.</summary>
    void OpenOnPhone();

    /// <summary>Re-reads the core. Null on a failure as well as on "no core", because the app treats both
    /// the same way: it shows the way in.</summary>
    Task<AetherlingDto?> RefreshAsync(CancellationToken ct = default);

    /// <summary>Buys the one Aethercore this account will ever have. Throws the server's refusal.</summary>
    Task<AetherlingDto> PurchaseAsync(CancellationToken ct = default);

    /// <summary>Moves the core one stage up. Throws the server's refusal, gate included.</summary>
    Task<AetherlingDto> ChargeAsync(CancellationToken ct = default);

    /// <summary>Breaks the core open once its last hold has elapsed. Free, and safe to call twice: the
    /// server answers the second one with the first one's result.</summary>
    Task<AetherlingDto> HatchAsync(CancellationToken ct = default);

    /// <summary>The one free naming. Throws the server's refusal, which covers a name that is empty, too
    /// long, or one the moderator would not have.</summary>
    Task<AetherlingDto> NameAsync(string name, CancellationToken ct = default);

    /// <summary>The caller's spark balance, null when it cannot be read.</summary>
    Task<long?> GetSparkBalanceAsync(CancellationToken ct = default);

    /// <summary>Feeds one crystal of an element. The host rides the player's current job along,
    /// because the feed that grows the pet up decides the arms card from it. Throws the server's
    /// refusal (gate, appetite, no crystal).</summary>
    Task<AetherlingDto> FeedAsync(short element, CancellationToken ct = default);

    /// <summary>Stores the whole look, validated server-side against ownership.</summary>
    Task<AetherlingDto> SetLookAsync(AetherlingLookDto look, CancellationToken ct = default);

    /// <summary>Scratches one of the three adulting cards; the reveal is the grant.</summary>
    Task<AetherlingDto> RevealScratchAsync(short slot, CancellationToken ct = default);

    /// <summary>Stamps the adult onboarding done, so a reinstall never replays it.</summary>
    Task<AetherlingDto> CompleteOnboardingAsync(CancellationToken ct = default);

    /// <summary>Everything the account owns in the store's inventory, crystals included. Null on
    /// failure, so a dropped connection reads as "unknown" rather than "nothing owned".</summary>
    Task<StoreInventoryItemDto[]?> GetOwnedItemsAsync(CancellationToken ct = default);

    /// <summary>The player's current job abbreviation, lowercase, empty when unknown. Read,
    /// never watched; empty is a gap in knowledge and never a change.</summary>
    string CurrentJobAbbreviation { get; }

    /// <summary>Turns a hub exception into a line the player can read, already localized.</summary>
    string DescribeError(Exception ex);

    /// <summary>Starts the loop at the tempo for a stage, and ducks the game's own music while it plays.
    /// Calling it again with a different tempo crossfades.</summary>
    void StartBgm(float speed);

    /// <summary>Starts a minigame's loop, named by its file under the plugin's bgm folder. Rides the same
    /// mute and the same duck as the ceremony's loop, and replaces whatever was playing.</summary>
    void StartGameBgm(string fileName);

    /// <summary>Stops the loop and gives the game its music back.</summary>
    void StopBgm();

    /// <summary>Plays the shell breaking, once.</summary>
    void PlayCrack();

    /// <summary>Whether the creature is allowed to make noises when it is poked or stroked. Persisted by
    /// the app, and separate from <see cref="BgmMuted"/>: the music and the voice are different wishes.</summary>
    bool SoundsMuted { get; set; }

    /// <summary>How loud those noises are, 0..1. Persisted by the app; the host clamps whatever it is
    /// handed, so a corrupt stored value can never blast anyone.</summary>
    float SoundVolume { get; set; }

    /// <summary>The level a fresh install starts at, so the app can seed its stored answer without
    /// naming a number of its own.</summary>
    static float DefaultSoundVolume => 0.2f;

    /// <summary>A quick noise for a poke. Ignored while one is still sounding, so hammering the creature
    /// is one chirp rather than ten on top of each other.</summary>
    void PlayChirp();

    /// <summary>A longer answer, for being stroked. Same one-at-a-time rule as the chirp.</summary>
    void PlayResponse();

    /// <summary>Where the shipped sound files live, so the app can hand paths to the audio capability. The
    /// creature's own voice is played by the host; everything else asks for it by path.</summary>
    string SoundRoot { get; }

    /// <summary>A round of one of the companion's minigames finished. The host relays it to the spark
    /// reporter; the app never names an amount, and the server owns every cap.</summary>
    void NoteGameFinished();

    /// <summary>True while a minigame round is in progress. The host holds the phone's battery away from
    /// empty while it is set, so the go-outside screen can never take over mid-run and cost the player
    /// the round.</summary>
    bool GameSessionActive { get; set; }
}
