namespace AetherLove.Shared.Store;

/// <summary>The central catalogue of first-party item refs. A product's identity is
/// (<see cref="StoreItemKind"/>, ref); refs listed here are the compile-time half of that contract for
/// items whose behavior this codebase will implement. Aetherling products use that catalogue's own keys
/// instead (palette names like "gloam", accessory slugs like "angel-halo", arms as "arm-&lt;job&gt;",
/// crystals as "crystal-&lt;element&gt;", shells by their skin folder key), so its future sync can map
/// (Kind, Ref) onto its ownership lists without translation. Refs are lowercase kebab-case, stable
/// forever once a product has shipped under them.</summary>
public static class StoreItemRefs
{
    public const string FrameSakura = "frame-sakura";
    public const string FrameGold = "frame-gold";
    public const string FrameNeon = "frame-neon";
    public const string FrameMoogle = "frame-moogle";

    public const string ThemeMidnight = "theme-midnight";
    public const string ThemeSolar = "theme-solar";
    public const string ThemeSakura = "theme-sakura";

    public const string PowerupVenueBoost = "powerup-venue-boost";
    public const string PowerupYapBoost = "powerup-yap-boost";
    public const string PowerupSparkCapBoost = "powerup-spark-cap-boost";
    public const string PowerupProfileBoost = "powerup-profile-boost";

    public const string BundleStarter = "bundle-starter";
    public const string BundleAetherlingCare = "bundle-aetherling-care";
}
