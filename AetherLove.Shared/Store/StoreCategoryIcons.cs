namespace AetherLove.Shared.Store;

/// <summary>The icon keys a store category may carry. Stored as a STRING, never as a FontAwesome enum
/// value: that enum's numbering belongs to Dalamud and shifts between versions, so a persisted int would
/// silently start pointing at a different glyph after an update. The client maps a key to a glyph; the
/// admin only ever offers keys from this list, and anything unrecognised falls back rather than throws.</summary>
public static class StoreCategoryIcons
{
    /// <summary>Every key an admin may pick, in the order the dropdown shows them.</summary>
    public static readonly string[] Keys =
    [
        "phone",
        "ring",
        "gift",
        "palette",
        "wallpaper",
        "sparkle",
        "star",
        "crown",
        "shirt",
        "mask",
        "wand",
        "gem",
        "music",
        "gamepad",
        "tag",
        "box",
        "heart",
        "bolt",
        "rocket",
        "bug",
    ];

    /// <summary>True when the key is one this build knows how to draw. Null and blank are legal: they mean
    /// "no icon chosen", which the client renders with its own fallback.</summary>
    public static bool IsKnown(string? key) =>
        string.IsNullOrWhiteSpace(key) || System.Array.IndexOf(Keys, key) >= 0;
}
