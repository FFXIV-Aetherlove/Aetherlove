namespace AetherLove.Shared.Profile.Enums;

/// <summary>Music-link provider a "favourite song" pill links out to. Drives server-side resolution,
/// the live-preview hub call, and per-provider rendering (brand colour + icon).</summary>
public enum MusicProvider : short
{
    None = 0,
    Spotify = 1,
    SoundCloud = 2,
    AppleMusic = 3,
    YouTubeMusic = 4,
}
