namespace AetherLove.Shared.Assets;

/// <summary>The pack names the server derives from its asset folders today, so client code can ask for a
/// pack by name without spelling the folder rule out. A folder over the split threshold becomes one pack
/// per heavy subfolder, named <c>parent-child</c>; the rest of the folder keeps the parent's name. Every
/// pack, the music and the Echo bundle included, is downloaded before the phone reaches Home, and the
/// update screen's bar covers all of them.</summary>
public static class AssetPacks
{
    public const string Emoji = "emoji";
    public const string Aetherling = "unknown";
    public const string AetherlingAccessories = "unknown-acc";
    public const string AetherlingGames = "unknown-games";
    public const string Racer = "unknown-racer";
    public const string Sounds = "sfx";
    public const string Notifications = "notifications";
    public const string Other = "other";
    public const string Wallpapers = "wallpapers";
    public const string Weather = "weather";
    public const string Stacker = "stacker";
    public const string Music = "bgm";
    public const string EchoHost = "echo-host";
    public const string RacingCards = "racing-cards";

    /// <summary>The localization key of a pack's display name; hyphens become underscores.</summary>
    public static string DisplayKey(string name) => "os.assets_pack_" + name.Replace('-', '_');
}
