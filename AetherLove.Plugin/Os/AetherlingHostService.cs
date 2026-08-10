using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Shared.Aetherling;
using AetherOS.Apps.Aetherling;
using AetherOS.Apps.Groove;
using AetherOS.Sdk;
using Dalamud.Game.Config;
using Dalamud.Plugin.Services;

namespace AetherLove.Os;

/// <summary>The Aetherling app's host: hub passthroughs, the looping ceremony track, and the game-BGM duck
/// that goes with it.</summary>
public sealed class AetherlingHostService : IAetherlingHost, IDisposable
{
    private const string BgmFile = "crystallic.ogg";
    private const string CrackFile = "crack.ogg";

    private readonly AetherHubContext _hub;
    private readonly SessionBootstrapper _bootstrap;
    private readonly IAppCapabilities _capabilities;
    private readonly BgmPlayer _bgm = new();
    private readonly OneShotSound _sfx = new();

    /// <summary>How often the game's sound settings and the window's focus are re-read. Fast enough that
    /// alt-tabbing feels immediate, slow enough that it is free.</summary>
    private const double AudioPollSeconds = 0.25;

    private AetherlingDto? _snapshot;
    private bool _snapshotSeeded;
    private bool _mutedGameBgm;
    private bool _muted;
    private bool _silenced;
    private double _audioAccum;

    public AetherlingHostService(AetherHubContext hub, SessionBootstrapper bootstrap, IAppCapabilities capabilities)
    {
        _hub = hub;
        _bootstrap = bootstrap;
        _capabilities = capabilities;
        Plugin.ClientState.Logout += OnLogout;
        Plugin.Framework.Update += OnAudioTick;
    }

    public AetherlingDto? Snapshot
    {
        get
        {
            // The login snapshot is the first answer, so a returning owner sees their core without waiting
            // for a round trip.
            if (!_snapshotSeeded)
            {
                _snapshotSeeded = true;
                _snapshot = _bootstrap.LastConnection?.Aetherling;
            }
            return _snapshot;
        }
    }

    /// <summary>The name to show wherever the app is listed, or null while nothing has hatched. Reads the
    /// login snapshot, so the home tile is right at boot without anyone opening the app.</summary>
    public string? PetName => Snapshot is { HatchedAtUtc: not null, PetName: { Length: > 0 } name } ? name : null;

    public string AssetRoot =>
        Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Media", "unknown");

    /// <summary>Forgets the core entirely, for the staff reset. Seeded stays true, or the next read falls
    /// back to the login snapshot and resurrects the thing that was just deleted.</summary>
    public void ClearSnapshot()
    {
        _snapshotSeeded = true;
        _snapshot = null;
        StopBgm();
    }

    public IAetherlingOverlay? Overlay { get; set; }

    public ITextureCache Textures => _capabilities.Textures;

    public bool ReduceMotion => AccessibilityService.ReduceMotion;

    /// <summary>Handed over by the plugin at startup: this service is constructed before the windows are
    /// wired, so it cannot take one in its constructor.</summary>
    public Action? PhoneOpener { get; set; }

    public void OpenOnPhone() => PhoneOpener?.Invoke();

    public async Task<AetherlingDto?> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            _snapshotSeeded = true;
            _snapshot = await _hub.GetAetherlingAsync(ct).ConfigureAwait(false);
            return _snapshot;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Aetherling] Core fetch failed.");
            return null;
        }
    }

    public async Task<AetherlingDto> PurchaseAsync(CancellationToken ct = default)
    {
        var dto = await _hub.PurchaseAethercoreAsync(ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherlingDto> ChargeAsync(CancellationToken ct = default)
    {
        var dto = await _hub.ChargeAethercoreAsync(ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherlingDto> HatchAsync(CancellationToken ct = default)
    {
        var dto = await _hub.HatchAethercoreAsync(ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<AetherlingDto> NameAsync(string name, CancellationToken ct = default)
    {
        var dto = await _hub.NameAetherlingAsync(name, ct).ConfigureAwait(false);
        _snapshotSeeded = true;
        _snapshot = dto;
        return dto;
    }

    public async Task<long?> GetSparkBalanceAsync(CancellationToken ct = default)
    {
        try
        {
            return (await _hub.GetSparkWalletAsync(ct).ConfigureAwait(false)).Balance;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Aetherling] Balance fetch failed.");
            return null;
        }
    }

    public string DescribeError(Exception ex) => HubErrorText.Localize(ex);

    public bool BgmMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            ApplyAudioState();
        }
    }

    public void StartBgm(float speed)
    {
        if (_muted)
        {
            return;
        }
        var path = Path.Combine(AssetRoot, BgmFile);
        if (_bgm.IsPlaying)
        {
            _bgm.SetSpeed(speed);
        }
        else
        {
            _bgm.Play(path, speed);
        }
        _silenced = !ShouldBeAudible();
        ApplyAudioState();
    }

    /// <summary>Re-reads the game's sound settings and the window's focus. Cheap enough to run on a tick, and
    /// it has to be a tick: nothing raises an event when the player pulls a slider or alt-tabs away.</summary>
    private void OnAudioTick(IFramework framework)
    {
        if (!_bgm.IsPlaying)
        {
            return;
        }
        _audioAccum += framework.UpdateDelta.TotalSeconds;
        if (_audioAccum < AudioPollSeconds)
        {
            return;
        }
        _audioAccum = 0;

        // The player flipping BGM back on mid-duck takes the mute over: release the claim, so the checks
        // below read their real answer again and the app never writes over their choice on the way out.
        if (_mutedGameBgm
            && GameVolume.TryGetMuted(SystemConfigOption.IsSndBgm, out var bgmNow) && !bgmNow)
        {
            _mutedGameBgm = false;
        }

        var silenced = !ShouldBeAudible();
        if (silenced == _silenced)
        {
            return;
        }
        _silenced = silenced;
        ApplyAudioState();
    }

    /// <summary>The loop is the phone's own music, so it answers to the game's sound options: master or BGM
    /// muted or at zero silences it, and it goes quiet with the game when the window loses focus unless the
    /// player asked for sound while inactive.</summary>
    private bool ShouldBeAudible()
    {
        if (GameVolume.TryGetMuted(SystemConfigOption.IsSoundDisable, out var allOff) && allOff)
        {
            return false;
        }
        if (GameVolume.TryGetMuted(SystemConfigOption.IsSndMaster, out var masterMuted) && masterMuted)
        {
            return false;
        }
        if (GameVolume.TryGet(SystemConfigOption.SoundMaster, out var master) && master <= 0f)
        {
            return false;
        }

        // Our own duck sets IsSndBgm, so while the mute is ours the player's real answer is "not muted".
        if (!_mutedGameBgm
            && GameVolume.TryGetMuted(SystemConfigOption.IsSndBgm, out var bgmMuted) && bgmMuted)
        {
            return false;
        }
        if (GameVolume.TryGet(SystemConfigOption.SoundBgm, out var bgm) && bgm <= 0f)
        {
            return false;
        }

        if (WindowFocus.GameHasFocus())
        {
            return true;
        }
        return GameVolume.TryGetMuted(SystemConfigOption.IsSoundAlways, out var always) && always
            && GameVolume.TryGetMuted(SystemConfigOption.IsSoundBgmAlways, out var bgmAlways) && bgmAlways;
    }

    /// <summary>Silence is a ramp, never a stop: the track keeps running so coming back to the window picks it
    /// up where it would have been rather than restarting it. The game gets its own music back meanwhile,
    /// because there is nothing left to duck it for.</summary>
    private void ApplyAudioState()
    {
        var quiet = _muted || _silenced;
        _bgm.SetMuted(quiet);
        if (quiet || !_bgm.IsPlaying)
        {
            RestoreGameBgm();
            return;
        }
        DuckGameBgm();
    }

    public void StopBgm()
    {
        _bgm.Stop();
        RestoreGameBgm();
    }

    /// <summary>The shell giving way. Rides the mute toggle with the loop, because a player who silenced the
    /// ceremony does not want the one loud moment of it either.</summary>
    public void PlayCrack()
    {
        if (_muted)
        {
            return;
        }
        _sfx.Play(Path.Combine(AssetRoot, CrackFile));
    }

    /// <summary>Only ever claims a mute it made itself, so somebody who already plays with the music off is
    /// never unmuted behind their back when the app closes.</summary>
    private void DuckGameBgm()
    {
        if (_mutedGameBgm)
        {
            return;
        }
        if (GameVolume.TryGetMuted(SystemConfigOption.IsSndBgm, out var muted) && !muted)
        {
            GameVolume.SetMuted(SystemConfigOption.IsSndBgm, true);
            _mutedGameBgm = true;
        }
    }

    /// <summary>Gives the game's music back, keeping ownership of the mute until the flag reads unmuted:
    /// <see cref="GameVolume.SetMuted"/> swallows a failed write, and the flag is the only record that this
    /// mute is ours to undo.</summary>
    private void RestoreGameBgm()
    {
        if (!_mutedGameBgm)
        {
            return;
        }
        GameVolume.SetMuted(SystemConfigOption.IsSndBgm, false);
        if (GameVolume.TryGetMuted(SystemConfigOption.IsSndBgm, out var muted) && !muted)
        {
            _mutedGameBgm = false;
        }
    }

    private void OnLogout(int type, int code) => StopBgm();

    public void Dispose()
    {
        Plugin.Framework.Update -= OnAudioTick;
        Plugin.ClientState.Logout -= OnLogout;
        _sfx.Dispose();
        _bgm.Dispose();
        RestoreGameBgm();
    }
}
