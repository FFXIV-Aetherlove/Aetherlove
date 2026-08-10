using AetherLove.Os;
using AetherOS.Apps.Groove;
using Dalamud.Game.Config;
using Dalamud.Plugin.Services;

namespace AetherLove.Services;

/// <summary>Silences the game's own BGM while the PC is playing music, using the same IsSndBgm flag the
/// game's sound options expose. Edge-triggered on the playback transition rather than held every tick, so
/// the player stays free to override the mute mid-track without the service fighting them back.</summary>
public sealed class GrooveAutoMuteService
{
    private const double PollSeconds = 0.5;

    private readonly GrooveHostService _host;
    private readonly GrooveSettings _settings;

    private double _accum;
    private bool _lastPlaying;
    private bool _mutedByUs;

    public GrooveAutoMuteService(GrooveHostService host, GrooveSettings settings)
    {
        _host = host;
        _settings = settings;
    }

    public void Initialize()
    {
        Plugin.Framework.Update += OnUpdate;
    }

    public void Shutdown()
    {
        Plugin.Framework.Update -= OnUpdate;
        Restore();
    }

    private void OnUpdate(IFramework framework)
    {
        if (!PhonePower.IsOn)
        {
            // Give the game its music back rather than just stopping: a duck we are holding when the phone
            // goes off would otherwise leave the player silent with nothing on screen to undo it.
            Restore();
            return;
        }
        _accum += framework.UpdateDelta.TotalSeconds;
        if (_accum < PollSeconds)
        {
            return;
        }
        _accum = 0;

        if (!_settings.AutoMuteBgm)
        {
            Restore();
            _lastPlaying = false;
            return;
        }

        IGrooveHost host = _host;
        var playing = host.Current is { IsPlaying: true };
        if (playing == _lastPlaying)
        {
            return;
        }
        _lastPlaying = playing;

        if (!playing)
        {
            Restore();
            return;
        }
        // A player who already had their BGM muted keeps ownership of it; we never claim a mute we didn't make.
        if (GameVolume.TryGetMuted(SystemConfigOption.IsSndBgm, out var muted) && !muted)
        {
            GameVolume.SetMuted(SystemConfigOption.IsSndBgm, true);
            _mutedByUs = true;
        }
    }

    private void Restore()
    {
        if (!_mutedByUs)
        {
            return;
        }
        _mutedByUs = false;
        GameVolume.SetMuted(SystemConfigOption.IsSndBgm, false);
    }
}
