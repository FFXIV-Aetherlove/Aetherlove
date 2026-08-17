namespace AetherOS.Sdk;

/// <summary>Sound effects for apps that need them: a game's blips, thuds and dings. One call plays one
/// file once, and calls stack, because two things happening together really are two sounds.
///
/// <para>This is a mechanism, not a policy. It has no mute of its own: an app that offers the player a
/// switch checks it before calling. What it DOES enforce is the game's own sound settings, which an
/// independent audio stream would otherwise ignore entirely.</para></summary>
public interface IAudioPlayer
{
    /// <summary>Whether the game's sound settings allow an effect to be heard at all right now: master and
    /// effects volumes, their mute flags, and the window's focus against the play-while-inactive pair. The
    /// player already answers this itself, so calling it is only useful to skip work behind a sound.</summary>
    bool EffectsAudible { get; }

    /// <summary>Plays an ogg once. <paramref name="pitch"/> is a playback rate, so it changes speed and
    /// pitch together, which is what a smaller or bigger version of the same sound wants. A missing or
    /// undecodable file is silence, never an error.</summary>
    void Play(string path, float volume = 1f, float pitch = 1f);
}
