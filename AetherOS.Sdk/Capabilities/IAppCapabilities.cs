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

    /// <summary>Read-only view of the together-mode party; the shell owns all party mutations.</summary>
    IPartyState Party { get; }

    /// <summary>Photo filters applied to disk images.</summary>
    IImageEffects Effects { get; }

    /// <summary>Host/system side effects (open a URL, write the clipboard).</summary>
    ISystemBridge System { get; }

    /// <summary>Live key state for keyboard-driven apps. Reading consumes the key for that frame.</summary>
    IKeyboardInput Keyboard { get; }

    /// <summary>One-shot sound effects, honouring the game's own sound settings.</summary>
    IAudioPlayer Audio { get; }

    /// <summary>Offer content to whichever apps accept its type (the generic share sheet).</summary>
    IShareService Share { get; }

    /// <summary>Sending the player somewhere in the world, when a transport plugin is installed.</summary>
    ITravelBridge Travel { get; }

    /// <summary>Per-message text translation (opt-in; see <see cref="ITranslationBridge"/>).</summary>
    ITranslationBridge Translation { get; }

    /// <summary>Persistent storage scoped to <paramref name="appId"/>: a private folder plus a JSON key-value
    /// store. Pass your own app id.</summary>
    IAppStorage Storage(string appId);

    /// <summary>The app's slice of FFXIV's server info bar. Publish text and the host owns every gate,
    /// including the player's per-app and per-entry toggles. Pass your own app id.</summary>
    IServerBar ServerBar(string appId);
}
