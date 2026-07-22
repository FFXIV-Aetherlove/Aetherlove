namespace AetherOS.Sdk;

/// <summary>The platform capabilities every AetherOS app can reuse instead of declaring its own host
/// bridge for the same cross-cutting features. The shell supplies one implementation; apps receive it by
/// constructor injection and also via <see cref="OsAppContext.Capabilities"/>.</summary>
public interface IAppCapabilities
{
    /// <summary>The in-game selfie camera.</summary>
    ICameraService Camera { get; }

    /// <summary>Disk image picking, with or without a crop step.</summary>
    IImagePicker Images { get; }

    /// <summary>Cached disk-image textures.</summary>
    ITextureCache Textures { get; }

    /// <summary>Photo filters applied to disk images.</summary>
    IImageEffects Effects { get; }

    /// <summary>Host/system side effects (open a URL, write the clipboard).</summary>
    ISystemBridge System { get; }

    /// <summary>Offer content to whichever apps accept its type (the generic share sheet).</summary>
    IShareService Share { get; }

    /// <summary>Persistent storage scoped to <paramref name="appId"/>: a private folder plus a JSON key-value
    /// store. Pass your own app id.</summary>
    IAppStorage Storage(string appId);
}
