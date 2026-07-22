using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AetherOS.Sdk;

namespace AetherLove.Os;

/// <summary>Per-app storage scopes under <c>ConfigDirectory/apps/&lt;appId&gt;</c>. Each scope owns its folder
/// plus a single <c>settings.json</c> key-value file, loaded lazily and written through on every Set.</summary>
public sealed class AppStorageService
{
    private readonly ConcurrentDictionary<string, Scope> _scopes = new(StringComparer.OrdinalIgnoreCase);

    public IAppStorage For(string appId) => _scopes.GetOrAdd(appId, id => new Scope(id));

    private sealed class Scope : IAppStorage
    {
        private readonly string _dir;
        private readonly object _lock = new();
        private Dictionary<string, JsonElement>? _values;

        public Scope(string appId)
        {
            _dir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "apps", appId);
        }

        public string Directory
        {
            get
            {
                System.IO.Directory.CreateDirectory(_dir);
                return _dir;
            }
        }

        private string SettingsPath => Path.Combine(_dir, "settings.json");

        public T? Get<T>(string key)
        {
            lock (_lock)
            {
                Load();
                if (_values!.TryGetValue(key, out var element))
                {
                    try
                    {
                        return element.Deserialize<T>();
                    }
                    catch (JsonException)
                    {
                    }
                }
                return default;
            }
        }

        public void Set<T>(string key, T value)
        {
            lock (_lock)
            {
                Load();
                _values![key] = JsonSerializer.SerializeToElement(value);
                try
                {
                    System.IO.Directory.CreateDirectory(_dir);
                    File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_values));
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning(ex, $"[AppStorage] Saving settings for '{Path.GetFileName(_dir)}' failed.");
                }
            }
        }

        private void Load()
        {
            if (_values != null)
            {
                return;
            }
            try
            {
                _values = File.Exists(SettingsPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(SettingsPath))
                      ?? new Dictionary<string, JsonElement>()
                    : new Dictionary<string, JsonElement>();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, $"[AppStorage] Reading settings for '{Path.GetFileName(_dir)}' failed.");
                _values = new Dictionary<string, JsonElement>();
            }
        }
    }
}
