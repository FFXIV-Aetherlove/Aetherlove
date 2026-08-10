using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>Ambient data for the currently-playing match effect; set once by the match host so effects
/// can read it without a context dependency.</summary>
internal static class MatchContent
{
    public static ISharedImmediateTexture? OwnAvatar { get; set; }
    public static ISharedImmediateTexture? PeerAvatar { get; set; }
    public static string OwnName { get; set; } = "You";
    public static string PeerName { get; set; } = string.Empty;
    public static string? OwnFrameRef { get; set; }
    public static string? PeerFrameRef { get; set; }
}
