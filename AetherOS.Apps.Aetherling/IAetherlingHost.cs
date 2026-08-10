using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
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

    /// <summary>Turns a hub exception into a line the player can read, already localized.</summary>
    string DescribeError(Exception ex);

    /// <summary>Starts the loop at the tempo for a stage, and ducks the game's own music while it plays.
    /// Calling it again with a different tempo crossfades.</summary>
    void StartBgm(float speed);

    /// <summary>Stops the loop and gives the game its music back.</summary>
    void StopBgm();

    /// <summary>Plays the shell breaking, once.</summary>
    void PlayCrack();
}
