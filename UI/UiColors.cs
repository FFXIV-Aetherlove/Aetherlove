namespace AetherLove.UI;

/// <summary>Raw ImGui colours (packed 0xAABBGGRR) that are shared by more than one screen, named and
/// documented so they aren't scattered as bare hex literals. Colours used in a single place stay local
/// to their call site.</summary>
internal static class UiColors
{
    /// <summary>Neutral grey filled into an avatar circle/rect while its texture is still decoding.</summary>
    internal const uint AvatarFallback = 0xFF555555u;

    /// <summary>Unread / notification badge dot (red).</summary>
    internal const uint UnreadBadge = 0xFF2020E0u;

    /// <summary>Faint horizontal divider rule.</summary>
    internal const uint Divider = 0x30FFFFFFu;

    /// <summary>Spotify brand green for the now-playing pill, plus its hover tint.</summary>
    internal const uint SpotifyGreen = 0xFF1DB954u;
    internal const uint SpotifyGreenHover = 0xFF2EE86Bu;

    /// <summary>Translucent fill for a caution/notice callout box — amber/orange at ~25% alpha.</summary>
    internal const uint WarningBoxFill = 0x402080FFu;

    /// <summary>Opaque border for a caution/notice callout box — amber/orange.</summary>
    internal const uint WarningBoxBorder = 0xFF2080FFu;

    /// <summary>Festive confetti palette (0x00BBGGRR; per-particle alpha is applied at draw time).</summary>
    internal static readonly uint[] ConfettiPalette =
    [
        0x00B478FFu, // pink
        0x00FF64C8u, // purple
        0x0032C8FFu, // gold
        0x00BED250u, // teal
        0x0078E678u, // mint
        0x006482FFu, // coral
    ];
}
