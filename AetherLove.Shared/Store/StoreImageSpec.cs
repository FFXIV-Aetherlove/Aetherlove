namespace AetherLove.Shared.Store;

/// <summary>How a product's art is sized. Most products are shelf thumbnails, but a phone skin's picture
/// IS the phone frame it will become, so it keeps its portrait shape and enough resolution to render at
/// full phone size. Shared so the server encodes, the admin form advises and the client previews against
/// one set of numbers.</summary>
public static class StoreImageSpec
{
    /// <summary>Shelf art: square-ish thumbnails, never shown larger than a card.</summary>
    public const int DefaultMaxWidth = 640;
    public const int DefaultQuality = 82;

    /// <summary>Phone-skin art. The bounds sit above every shipped frame so a well-sized upload passes
    /// through untouched; a larger one is scaled down rather than refused.</summary>
    public const int PhoneSkinMaxWidth = 1280;
    public const int PhoneSkinMaxHeight = 2048;
    public const int PhoneSkinQuality = 92;

    /// <summary>The shipped frames' shape: what an artist should target. Wider frames are fine, taller
    /// ones get letterboxed by the phone, so the height is the number that matters.</summary>
    public const int PhoneSkinWidth = 930;
    public const int PhoneSkinHeight = 1670;

    /// <summary>Editorial art: collection cards and category tiles, both wide banners behind text.</summary>
    public const int BannerMaxWidth = 1200;
    public const int BannerMaxHeight = 800;
    public const int BannerQuality = 85;

    /// <summary>Avatar-ring art: the product image IS the ring drawn around avatars, so it must keep
    /// its alpha channel end to end (PNG, never lossy WebP and never the JPEG transcode).</summary>
    public const int FrameMaxSize = 640;

    /// <summary>True when the product's picture is a phone frame rather than shelf art.</summary>
    public static bool IsPhoneSkin(StoreItemKind kind) => kind == StoreItemKind.ThemePack;

    /// <summary>True when the product's picture carries meaningful transparency and must be stored as
    /// PNG (avatar rings: transparent centre, ring band outside).</summary>
    public static bool KeepsAlpha(StoreItemKind kind) => kind == StoreItemKind.AvatarFrame;

    public static (int MaxWidth, int? MaxHeight, int Quality) For(StoreItemKind kind) =>
        IsPhoneSkin(kind)
            ? (PhoneSkinMaxWidth, PhoneSkinMaxHeight, PhoneSkinQuality)
            : (DefaultMaxWidth, null, DefaultQuality);
}
