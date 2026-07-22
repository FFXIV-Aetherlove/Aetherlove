namespace AetherOS.Sdk;

/// <summary>Per-app persistent storage, scoped to one app id: a private folder for files plus a small JSON
/// key-value store, both living under the host's config directory (<c>apps/&lt;appId&gt;</c>). Apps use this
/// instead of inventing their own paths or reaching for the host configuration.</summary>
public interface IAppStorage
{
    /// <summary>The app's private folder, created on first access.</summary>
    string Directory { get; }

    /// <summary>Reads a value from the app's key-value store; default when the key is absent or unreadable.</summary>
    T? Get<T>(string key);

    /// <summary>Writes a value to the app's key-value store and persists it.</summary>
    void Set<T>(string key, T value);
}
