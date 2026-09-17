using System.IO;
using AetherLove.Shared.Assets;

namespace AetherLove.Services.Media;

/// <summary>The two homes of the phone's media. Shipped files travel inside the plugin folder and are what
/// the first frame needs: shell art, fonts, app icons, flags. Downloaded files land under the config
/// directory, which survives a plugin update, and mirror the server's asset tree pack by pack.</summary>
public static class MediaPaths
{
    public const string ShippedFolder = "Media";
    public const string DownloadedFolder = "assets";

    public const string AppIcons = "appicons";
    public const string Icons = "icons";
    public const string Fonts = "fonts";

    public const string Emoji = AssetPacks.Emoji;
    public const string Aetherling = AssetPacks.Aetherling;
    public const string Sounds = AssetPacks.Sounds;
    public const string Music = AssetPacks.Music;
    public const string Notifications = AssetPacks.Notifications;
    public const string Other = AssetPacks.Other;
    public const string Wallpapers = AssetPacks.Wallpapers;
    public const string Weather = AssetPacks.Weather;
    public const string Stacker = AssetPacks.Stacker;
    public const string RacingCards = AssetPacks.RacingCards;

    public static string ShippedRoot =>
        Path.Combine(Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? string.Empty, ShippedFolder);

    public static string DownloadedRoot => Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, DownloadedFolder);

    public static string Shipped(params string[] parts) => Path.Combine(ShippedRoot, Path.Combine(parts));

    public static string Downloaded(params string[] parts) => Path.Combine(DownloadedRoot, Path.Combine(parts));
}
