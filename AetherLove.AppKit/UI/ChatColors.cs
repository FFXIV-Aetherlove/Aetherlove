using System.Numerics;
using AetherLove.Services;

namespace AetherLove.UI;

/// <summary>Chat-bubble colours: the user's overrides when set, otherwise the live theme defaults.</summary>
public static class ChatColors
{
    public static Vector4 OwnBgDefault => ThemeService.Current.Accent;

    public static Vector4 OwnFgDefault => ThemeService.CurrentTheme
            is AppTheme.VanillaSunrise or AppTheme.YorhaTypeAe or AppTheme.WorldOfLovecraft
        ? new Vector4(0.13f, 0.10f, 0.03f, 1f)
        : new Vector4(1f, 1f, 1f, 1f);

    public static Vector4 PeerBgDefault => new(0.227f, 0.227f, 0.227f, 1f);

    public static Vector4 PeerFgDefault => new(1f, 1f, 1f, 1f);

    public static Vector4 OwnBg => UiHost.Configuration.OwnChatBg ?? OwnBgDefault;
    public static Vector4 OwnFg => UiHost.Configuration.OwnChatFg ?? OwnFgDefault;
    public static Vector4 PeerBg => UiHost.Configuration.PeerChatBg ?? PeerBgDefault;
    public static Vector4 PeerFg => UiHost.Configuration.PeerChatFg ?? PeerFgDefault;
}
