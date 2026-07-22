using System.Numerics;

namespace AetherLove.UI;

/// <summary>Colours (packed 0xAABBGGRR uints and Vector4s) shared by more than one screen.</summary>
internal static class UiColors
{
    /// <summary>Caution accent (amber) for warning text and warning-style modals.</summary>
    internal static readonly Vector4 Amber = new(0.95f, 0.65f, 0.14f, 1f);

    /// <summary>Patreon brand coral (#FF424D), for the Supporter settings row + link buttons.</summary>
    internal static readonly Vector4 Patreon = new(1.00f, 0.259f, 0.302f, 1f);

    /// <summary>Discord brand blurple: the Settings Discord row and the join buttons.</summary>
    internal static readonly Vector4 Discord = new(0.43f, 0.48f, 1.00f, 1f);

    /// <summary>Gold star badge marking a favorited emoji.</summary>
    internal const uint FavoriteStar = 0xFF3CC8FFu; // 0xAABBGGRR gold (R255 G200 B60)

    /// <summary>Error accent (red) for inline error text and failure-style modals.</summary>
    internal static readonly Vector4 Danger = new(0.95f, 0.45f, 0.45f, 1f);

    /// <summary>Live-now accent (green): hangout status cards, banners, and directory chips.</summary>
    internal static readonly Vector4 LiveGreen = new(0.35f, 0.85f, 0.45f, 1f);

    /// <summary>Account-warning accent (orange): the warning notice cards and the "My" hub warnings row.</summary>
    internal static readonly Vector4 WarningAccent = new(0.97f, 0.62f, 0.25f, 1f);

    /// <summary>Moderator-message accent (blue): the message notice cards and the "My" hub messages row.</summary>
    internal static readonly Vector4 MessageAccent = new(0.40f, 0.68f, 0.95f, 1f);

    /// <summary>Primary body text.</summary>
    internal static readonly Vector4 Body = new(0.85f, 0.85f, 0.85f, 1f);

    /// <summary>Secondary / detail body text.</summary>
    internal static readonly Vector4 Subtle = new(0.70f, 0.70f, 0.74f, 1f);

    /// <summary>Success / confirmation accent (green).</summary>
    internal static readonly Vector4 Success = new(0.35f, 0.85f, 0.45f, 1f);

    /// <summary>Softer success hint (light green), used for "photo set" style notes.</summary>
    internal static readonly Vector4 SuccessSoft = new(0.55f, 0.85f, 0.55f, 1f);

    /// <summary>Muted grey text.</summary>
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

    /// <summary>Profile bio body text and its empty-state placeholder.</summary>
    internal static readonly Vector4 BioText = new(0.88f, 0.88f, 0.88f, 1f);
    internal static readonly Vector4 BioPlaceholder = new(0.38f, 0.38f, 0.38f, 1f);

    /// <summary>Muted grey for draw-list text (placeholder labels, hints).</summary>
    internal const uint TextMuted = 0xFF888888u;

    /// <summary>Popup-menu row text for destructive entries (0xAABBGGRR red).</summary>
    internal const uint MenuDanger = 0xFF5050E0u;

    /// <summary>Popup-menu row text for report entries (0xAABBGGRR amber).</summary>
    internal const uint MenuReport = 0xFF23A6F5u;

    /// <summary>Even quieter than <see cref="TextMuted"/>: tertiary detail lines (addresses, footnotes).</summary>
    internal const uint TextFaint = 0xFF666666u;

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

    /// <summary>Soft grey 1px ring that contains the bottom-nav avatar against the dark background.</summary>
    internal const uint AvatarRing = 0x66FFFFFFu;

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

    /// <summary>Translucent fill for a caution/notice callout box - amber/orange at ~25% alpha.</summary>
    internal const uint WarningBoxFill = 0x402080FFu;

    /// <summary>Opaque border for a caution/notice callout box - amber/orange.</summary>
    internal const uint WarningBoxBorder = 0xFF2080FFu;

    /// <summary>Translucent fill for a hard-rule callout box - red at ~25% alpha.</summary>
    internal const uint DangerBoxFill = 0x403333FFu;

    /// <summary>Opaque border for a hard-rule callout box - red.</summary>
    internal const uint DangerBoxBorder = 0xFF3333FFu;

    /// <summary>Opaque dark fill for the deck-expiry warning banner; it overlays a card photo, so it can't be translucent.</summary>
    internal const uint DeckExpiryWarnFill = 0xE0181818u;

    /// <summary>Dark fill (0x00BBGGRR; alpha applied at draw time) for a disabled/greyed deck-card pill.</summary>
    internal const uint DisabledPillFillRgb = 0x00262626u;

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

    /// <summary>Preset swatches (0xAABBGGRR) for chat-category avatars.</summary>
    internal static readonly uint[] CategoryPalette =
    [
        0xFF755DE8u, // rose
        0xFF5C79F2u, // coral
        0xFF4AA6F2u, // amber
        0xFF52C4E8u, // gold
        0xFF4FC99Du, // lime
        0xFF77BF4Fu, // emerald
        0xFFB2BF3Fu, // teal
        0xFFDBAE45u, // cyan
        0xFFE88555u, // azure
        0xFFDE6871u, // indigo
        0xFFD0599Bu, // violet
        0xFFB65BC7u, // magenta
        0xFFA06FE3u, // pink
        0xFF99867Au, // slate
        0xFF78858Cu, // warm grey
        0xFF8C7A5Fu, // steel
    ];

    /// <summary>Slate swatch assigned to the "Archive" category created by the archived-chats migration.</summary>
    internal const uint CategoryArchiveColor = 0xFF99867Au;
}
