using System.Numerics;
using AetherLove.Services;

namespace AetherLove.UI;

/// <summary>Resolves the chat-bubble colours: the user's overrides from the configuration when set, otherwise
/// the live theme defaults. Own bubbles default to the theme accent; peer bubbles to a neutral grey; bubble text
/// defaults to white (near-black on the light-accent themes so it stays readable).</summary>
public static class ChatColors
{
    public static Vector4 OwnBgDefault => ThemeService.Current.Accent;

    public static Vector4 OwnFgDefault => ThemeService.CurrentTheme is AppTheme.VanillaSunrise or AppTheme.YorhaTypeAe
        ? new Vector4(0.13f, 0.10f, 0.03f, 1f)
        : new Vector4(1f, 1f, 1f, 1f);

    public static Vector4 PeerBgDefault => new(0.227f, 0.227f, 0.227f, 1f);

    public static Vector4 PeerFgDefault => new(1f, 1f, 1f, 1f);

    public static Vector4 OwnBg => Plugin.Configuration.OwnChatBg ?? OwnBgDefault;
    public static Vector4 OwnFg => Plugin.Configuration.OwnChatFg ?? OwnFgDefault;
    public static Vector4 PeerBg => Plugin.Configuration.PeerChatBg ?? PeerBgDefault;
    public static Vector4 PeerFg => Plugin.Configuration.PeerChatFg ?? PeerFgDefault;
}
