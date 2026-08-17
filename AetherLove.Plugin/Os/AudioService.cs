using System;
using AetherLove.Services;
using AetherOS.Apps.Groove;
using AetherOS.Sdk;
using Dalamud.Game.Config;

namespace AetherLove.Os;

/// <summary>The <see cref="IAudioPlayer"/> capability. Every app's effects go through this one player, so
/// they share an output device and one sweep of finished voices.
///
/// <para>Nothing tells us when a slider moves, and our streams are our own rather than the game's mixer, so
/// the settings are read at the moment of the call. That is cheap enough: an effect fires on an event, not
/// on a frame.</para></summary>
public sealed class AudioService : IAudioPlayer, IDisposable
{
    private readonly OneShotSound _sfx = new();

    public bool EffectsAudible
    {
        get
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
            if (GameVolume.TryGetMuted(SystemConfigOption.IsSndSe, out var seMuted) && seMuted)
            {
                return false;
            }
            if (GameVolume.TryGet(SystemConfigOption.SoundSe, out var se) && se <= 0f)
            {
                return false;
            }
            if (WindowFocus.GameHasFocus())
            {
                return true;
            }

            // Not in front: only the pair that says to keep making noise anyway lets it through.
            return GameVolume.TryGetMuted(SystemConfigOption.IsSoundAlways, out var always) && always
                && GameVolume.TryGetMuted(SystemConfigOption.IsSoundSeAlways, out var seAlways) && seAlways;
        }
    }

    public void Play(string path, float volume = 1f, float pitch = 1f)
    {
        if (!EffectsAudible)
        {
            return;
        }
        _sfx.Play(path, volume, pitch);
    }

    public void Dispose() => _sfx.Dispose();
}
