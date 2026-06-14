using System.Numerics;

namespace AetherLove.UI;

/// <summary>Colours (packed 0xAABBGGRR uints and Vector4s) that are shared by more than one screen,
/// named and documented so they aren't scattered as bare literals. Colours used in a single place stay
/// local to their call site.</summary>
internal static class UiColors
{
    /// <summary>Caution accent (amber) for warning text and warning-style modals.</summary>
    internal static readonly Vector4 Amber = new(0.95f, 0.65f, 0.14f, 1f);

    /// <summary>Error accent (red) for inline error text and failure-style modals.</summary>
    internal static readonly Vector4 Danger = new(0.95f, 0.45f, 0.45f, 1f);

    /// <summary>Primary body text.</summary>
    internal static readonly Vector4 Body = new(0.85f, 0.85f, 0.85f, 1f);

    /// <summary>Secondary / detail body text.</summary>
    internal static readonly Vector4 Subtle = new(0.70f, 0.70f, 0.74f, 1f);

    /// <summary>Success / confirmation accent (green).</summary>
    internal static readonly Vector4 Success = new(0.35f, 0.85f, 0.45f, 1f);

    /// <summary>Softer success hint (light green), used for "photo set" style notes.</summary>
    internal static readonly Vector4 SuccessSoft = new(0.55f, 0.85f, 0.55f, 1f);

    /// <summary>Muted grey text; alpha variants are derived via <c>with</c> at the call site.</summary>
    internal static readonly Vector4 Muted = new(0.55f, 0.55f, 0.55f, 1f);

    /// <summary>Faint hint / caption text (dimmer than <see cref="Muted"/>).</summary>
    internal static readonly Vector4 Hint = new(0.52f, 0.52f, 0.52f, 0.85f);

    /// <summary>Photo-moderation "in review" accent (orange).</summary>
    internal static readonly Vector4 ReviewOrange = new(0.95f, 0.55f, 0.30f, 1f);

    /// <summary>Bio character counter once the effective length exceeds the cap.</summary>
    internal static readonly Vector4 BioOverLimit = new(0.9f, 0.35f, 0.35f, 1f);

    /// <summary>Red frame styling applied to NSFW-flagged form controls.</summary>
    internal static readonly Vector4 NsfwFrameBg = new(0.55f, 0.10f, 0.10f, 0.90f);
    internal static readonly Vector4 NsfwFrameBgHovered = new(0.70f, 0.18f, 0.18f, 1.00f);
    internal static readonly Vector4 NsfwFrameBgActive = new(0.40f, 0.06f, 0.06f, 1.00f);

    /// <summary>Background of the profile-preview pane on the profile form.</summary>
    internal static readonly Vector4 PreviewPaneBg = new(0.07f, 0.07f, 0.07f, 0.60f);

    /// <summary>Muted grey for draw-list text (placeholder labels, hints).</summary>
    internal const uint TextMuted = 0xFF888888u;

    /// <summary>Translucent red rule under ban/warning headings.</summary>
    internal const uint DangerDivider = 0x88FF3333u;

    /// <summary>Photo-slot grid (profile images + onboarding photos): slot fill, stored-photo border,
    /// main-slot border, and the main-slot placeholder label.</summary>
    internal const uint PhotoSlotFill = 0x33000000u;
    internal const uint PhotoSlotStoredBorder = 0xFF44AA44u;
    internal const uint PhotoSlotMainBorder = 0xFF997733u;
    internal const uint PhotoSlotMainLabel = 0xFFBBAA44u;

    /// <summary>Neutral grey filled into an avatar circle/rect while its texture is still decoding.</summary>
    internal const uint AvatarFallback = 0xFF555555u;

    /// <summary>Unread / notification badge dot (red).</summary>
    internal const uint UnreadBadge = 0xFF2020E0u;

    /// <summary>Faint horizontal divider rule.</summary>
    internal const uint Divider = 0x30FFFFFFu;

    /// <summary>Spotify brand green for the now-playing pill, plus its hover tint.</summary>
    internal const uint SpotifyGreen = 0xFF1DB954u;
    internal const uint SpotifyGreenHover = 0xFF2EE86Bu;

    /// <summary>SoundCloud orange (#FF5500).</summary>
    internal const uint SoundCloudOrange = 0xFF0055FFu;
    internal const uint SoundCloudOrangeHover = 0xFF367AFFu;
    /// <summary>Apple Music pink-red (#FA2D48).</summary>
    internal const uint AppleMusicPink = 0xFF482DFAu;
    internal const uint AppleMusicPinkHover = 0xFF725AFFu;
    /// <summary>YouTube red (#FF0000).</summary>
    internal const uint YouTubeRed = 0xFF0000FFu;
    internal const uint YouTubeRedHover = 0xFF4D4DFFu;

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
