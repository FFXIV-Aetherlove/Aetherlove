using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>Ambient data for the currently-playing match effect: the two matched avatars and names. The
/// match host sets these once (from the real match) before delegating to a random effect's draw, so the
/// effects can read them without each taking a context dependency.</summary>
internal static class MatchContent
{
    public static ISharedImmediateTexture? OwnAvatar { get; set; }
    public static ISharedImmediateTexture? PeerAvatar { get; set; }
    public static string OwnName { get; set; } = "You";
    public static string PeerName { get; set; } = string.Empty;
}
