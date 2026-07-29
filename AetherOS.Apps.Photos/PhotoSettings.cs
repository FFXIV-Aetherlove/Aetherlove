namespace AetherOS.Apps.Photos;

/// <summary>Keys in the Photos app's own <see cref="AetherOS.Sdk.IAppStorage"/> scope, shared with the host
/// services that honour them. Every toggle defaults to enabled, so a reader must use
/// <c>Get&lt;bool?&gt;(key) ?? true</c>; a plain <c>Get&lt;bool&gt;</c> reads a fresh install as disabled.</summary>
public static class PhotoSettings
{
    /// <summary>The app id, which is also the storage scope the settings live in.</summary>
    public const string ScopeId = "photos";

    /// <summary>Shots taken with the camera's own shutter land in the camera roll.</summary>
    public const string AutoImportCameraRoll = "autoImportCameraRoll";

    /// <summary>Captures taken for another app (profile photo, chat image, phone avatar) also land in the
    /// camera roll; the capture still reaches the app that asked for it either way.</summary>
    public const string AutoImportAppCaptures = "autoImportAppCaptures";

    /// <summary>Game print-screens land in the print-screens album.</summary>
    public const string AutoImportScreenshots = "autoImportScreenshots";
}
