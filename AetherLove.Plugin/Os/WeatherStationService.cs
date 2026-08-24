using System;
using System.Collections.Generic;
using System.Linq;
using AetherOS.Apps.Weather;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.Sheets;

namespace AetherLove.Os;

/// <summary>Weatherman's bridge into the game: Lumina forecast data plus per-tick env overrides.</summary>
public sealed class WeatherStationService : IWeatherStation, IDisposable
{
    private readonly Dictionary<uint, ISharedImmediateTexture?> _iconCache = new();
    private uint _cachedTerritory;
    private List<WeatherInfo> _zoneWeathers = new();
    private (byte Id, uint Rate)[] _rates = [];

    private byte? _weatherOverride;
    private int? _timeOverrideMinutes;

    private const long EorzeaDaySeconds = 86400;
    private const long WindowRealSeconds = 1400;

    public WeatherStationService()
    {
        Plugin.Framework.Update += OnFrameworkUpdate;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public bool InGame => Plugin.ClientState.IsLoggedIn && RefreshZone();

    public string ZoneName
    {
        get
        {
            var row = TerritoryRow();
            return row?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
        }
    }

    public unsafe WeatherInfo? CurrentWeather
    {
        get
        {
            var env = EnvManager.Instance();
            if (env == null)
            {
                return null;
            }
            return MakeInfo(env->ActiveWeather);
        }
    }

    public IReadOnlyList<ForecastEntry> Forecast(int count)
    {
        var result = new List<ForecastEntry>(count);
        if (!RefreshZone() || _rates.Length == 0)
        {
            return result;
        }
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowStart = unix - unix % WindowRealSeconds;
        for (int i = 0; i < count; i++)
        {
            var start = windowStart + i * WindowRealSeconds;
            var target = CalculateForecastTarget(start);
            var id = PickWeather(target);
            var info = MakeInfo(id);
            if (info != null)
            {
                result.Add(new ForecastEntry(info, DateTimeOffset.FromUnixTimeSeconds(start).LocalDateTime, i == 0));
            }
        }
        return result;
    }

    public IReadOnlyList<WeatherInfo> ZoneWeathers
    {
        get
        {
            RefreshZone();
            // Texture loading is async, so handles resolve per access; the refresh-time list would freeze
            // the nulls captured before the icon files finished loading.
            var list = new List<WeatherInfo>(_zoneWeathers.Count);
            foreach (var weather in _zoneWeathers)
            {
                list.Add(weather with { Icon = ResolveIcon(weather.Id, weather.IconKey) });
            }
            return list;
        }
    }

    public bool CanMutate => Plugin.ClientState.IsLoggedIn && !Plugin.Condition[ConditionFlag.InCombat];

    public byte? WeatherOverride => _weatherOverride;

    public void SetWeatherOverride(byte weatherId)
    {
        if (CanMutate)
        {
            _weatherOverride = weatherId;
        }
    }

    public unsafe void ClearWeatherOverride()
    {
        if (_weatherOverride == null)
        {
            return;
        }
        _weatherOverride = null;
        NudgeEnvironment();
    }

    public unsafe TimeSpan EorzeaTime
    {
        get
        {
            var framework = Framework.Instance();
            if (framework == null)
            {
                return TimeSpan.Zero;
            }
            var time = framework->ClientTime.IsEorzeaTimeOverridden
                ? framework->ClientTime.EorzeaTimeOverride
                : framework->ClientTime.EorzeaTime;
            return TimeSpan.FromSeconds(time % EorzeaDaySeconds);
        }
    }

    public int? TimeOverrideMinutes => _timeOverrideMinutes;

    public void SetTimeOverride(int minuteOfDay)
    {
        if (CanMutate)
        {
            _timeOverrideMinutes = Math.Clamp(minuteOfDay, 0, 1439);
        }
    }

    /// <summary>Hands the sky back to the real clock. Clearing the flag alone was not enough: the
    /// environment keeps the lighting it last resolved and only recomputes when something makes it, so a
    /// midday sky stayed bright at 23:20. Writing the real time into the override slot before dropping the
    /// flag leaves nothing stale behind, and re-applying the forecast weather with a transition is what
    /// actually forces the recompute.</summary>
    public unsafe void ClearTimeOverride()
    {
        _timeOverrideMinutes = null;
        var framework = Framework.Instance();
        if (framework != null)
        {
            framework->ClientTime.EorzeaTimeOverride = framework->ClientTime.EorzeaTime;
            framework->ClientTime.IsEorzeaTimeOverridden = false;
        }
        NudgeEnvironment();
    }

    /// <summary>Re-applies whatever weather should be running, which makes the environment resolve its
    /// whole state again (lighting and sky included) instead of keeping what it last computed.</summary>
    private unsafe void NudgeEnvironment()
    {
        var env = EnvManager.Instance();
        if (env == null || _rates.Length == 0)
        {
            return;
        }
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        env->ActiveWeather = _weatherOverride
            ?? PickWeather(CalculateForecastTarget(unix - unix % WindowRealSeconds));
        env->TransitionTime = 0.5f;
    }

    private unsafe void OnFrameworkUpdate(IFramework _)
    {
        if (!PhonePower.IsOn)
        {
            return;
        }
        if (_weatherOverride == null && _timeOverrideMinutes == null)
        {
            return;
        }
        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            ClearWeatherOverride();
            ClearTimeOverride();
            return;
        }

        if (_weatherOverride is { } weather)
        {
            var env = EnvManager.Instance();
            if (env != null && env->ActiveWeather != weather)
            {
                env->ActiveWeather = weather;
                env->TransitionTime = 0.5f;
            }
        }

        if (_timeOverrideMinutes is { } minutes)
        {
            var framework = Framework.Instance();
            if (framework != null)
            {
                var days = framework->ClientTime.EorzeaTime / EorzeaDaySeconds;
                framework->ClientTime.IsEorzeaTimeOverridden = true;
                framework->ClientTime.EorzeaTimeOverride = days * EorzeaDaySeconds + minutes * 60L;
            }
        }
    }

    private void OnTerritoryChanged(uint _)
    {
        _weatherOverride = null;
        ClearTimeOverride();
    }

    private TerritoryType? TerritoryRow()
    {
        var id = Plugin.ClientState.TerritoryType;
        if (id == 0)
        {
            return null;
        }
        return Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(id);
    }

    private bool RefreshZone()
    {
        var territory = Plugin.ClientState.TerritoryType;
        if (territory == _cachedTerritory && _rates.Length > 0)
        {
            return true;
        }
        _zoneWeathers = new List<WeatherInfo>();
        _rates = [];
        var row = TerritoryRow();
        if (row == null)
        {
            return false;
        }
        var rateRow = row.Value.WeatherRate.ValueNullable;
        if (rateRow == null)
        {
            return false;
        }

        var rates = new List<(byte Id, uint Rate)>();
        uint cumulative = 0;
        for (int i = 0; i < rateRow.Value.Weather.Count; i++)
        {
            var weatherId = (byte)rateRow.Value.Weather[i].RowId;
            var rate = (uint)rateRow.Value.Rate[i];
            if (weatherId == 0 || rate == 0)
            {
                continue;
            }
            cumulative += rate;
            rates.Add((weatherId, cumulative));
            if (_zoneWeathers.All(w => w.Id != weatherId))
            {
                var info = MakeInfo(weatherId);
                if (info != null)
                {
                    _zoneWeathers.Add(info);
                }
            }
        }
        _rates = rates.ToArray();
        _cachedTerritory = territory;
        return _rates.Length > 0;
    }

    private byte PickWeather(byte target)
    {
        foreach (var (id, cumulative) in _rates)
        {
            if (target < cumulative)
            {
                return id;
            }
        }
        return _rates.Length > 0 ? _rates[^1].Id : (byte)0;
    }

    private WeatherInfo? MakeInfo(byte weatherId)
    {
        var weather = Plugin.DataManager.GetExcelSheet<Weather>().GetRowOrDefault(weatherId);
        if (weather == null)
        {
            return null;
        }
        var key = IconKeyFor(weatherId);
        return new WeatherInfo(weatherId, weather.Value.Name.ExtractText(), ResolveIcon(weatherId, key), key);
    }

    private ImTextureID? ResolveIcon(byte weatherId, string key)
    {
        if (GetKeyIcon(key) is { } custom)
        {
            return custom;
        }
        var weather = Plugin.DataManager.GetExcelSheet<Weather>().GetRowOrDefault(weatherId);
        return weather == null ? null : GetIcon((uint)weather.Value.Icon);
    }

    private static string MediaDir =>
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "",
            "Media", "weather");

    private readonly Dictionary<byte, string> _iconKeyCache = new();
    private readonly Dictionary<string, ISharedImmediateTexture?> _keyTextureCache = new();

    /// <summary>The classifier reads the ENGLISH sheet so the icon mapping is stable on every client language.</summary>
    private string IconKeyFor(byte weatherId)
    {
        if (_iconKeyCache.TryGetValue(weatherId, out var cached))
        {
            return cached;
        }
        var name = Plugin.DataManager.GetExcelSheet<Weather>(Dalamud.Game.ClientLanguage.English)
            .GetRowOrDefault(weatherId)?.Name.ExtractText() ?? "";
        var key = ClassifyIconKey(name);
        _iconKeyCache[weatherId] = key;
        return key;
    }

    /// <summary>Custom icon from Media/weather/{key}.png; null (falling back to the game icon) when the file
    /// is absent. The shared texture is cached, the wrap resolves per call because loading is async.</summary>
    private ImTextureID? GetKeyIcon(string key)
    {
        if (!_keyTextureCache.TryGetValue(key, out var tex))
        {
            var path = System.IO.Path.Combine(MediaDir, key + ".png");
            tex = System.IO.File.Exists(path) ? Plugin.TextureProvider.GetFromFile(path) : null;
            _keyTextureCache[key] = tex;
        }
        return tex?.GetWrapOrDefault()?.Handle;
    }

    /// <summary>Keyword ladder over the English weather name, most specific first. Every key names one icon
    /// file; unknown or future weathers degrade to a sensible family instead of a missing icon.</summary>
    private static string ClassifyIconKey(string englishName)
    {
        var n = englishName.ToLowerInvariant();
        if (n.Length == 0)
        {
            return "clouds";
        }
        if (n.Contains("astromagnetic") || n.Contains("levin") || n.Contains("static") || n.Contains("hyperelectricity"))
        {
            return "levin";
        }
        if (n.Contains("beyond time") || n.Contains("dimensional") || n.Contains("demonic") || n.Contains("revelstorm")
            || n.Contains("wyrmstorm") || n.Contains("multiplicity") || n.Contains("concordance"))
        {
            return "arcane";
        }
        if (n.Contains("thunderstorm"))
        {
            return "thunderstorm";
        }
        if (n.Contains("thunder"))
        {
            return "thunder";
        }
        if (n.Contains("clear") || n.Contains("sublime"))
        {
            return "clear";
        }
        if (n.Contains("fair"))
        {
            return "fair";
        }
        if (n.Contains("sand"))
        {
            return "sandstorm";
        }
        if (n.Contains("dust"))
        {
            return "dust";
        }
        if (n.Contains("shower") || n.Contains("torrential"))
        {
            return "showers";
        }
        if (n.Contains("rain"))
        {
            return "rain";
        }
        if (n.Contains("blizzard"))
        {
            return "blizzard";
        }
        if (n.Contains("snow"))
        {
            return "snow";
        }
        if (n.Contains("fog") || n.Contains("mist") || n.Contains("haze"))
        {
            return "fog";
        }
        if (n.Contains("gale") || n.Contains("cyclone") || n.Contains("wind unbound"))
        {
            return "gales";
        }
        if (n.Contains("wind"))
        {
            return "wind";
        }
        if (n.Contains("heat") || n.Contains("hot spell"))
        {
            return "heat";
        }
        if (n.Contains("aurora"))
        {
            return "aurora";
        }
        if (n.Contains("gloom") || n.Contains("louring"))
        {
            return "gloom";
        }
        if (n.Contains("darkness"))
        {
            return "darkness";
        }
        if (n.Contains("tension") || n.Contains("oppression"))
        {
            return "ominous";
        }
        if (n.Contains("eruption") || n.Contains("cataclysm") || n.Contains("purgatory") || n.Contains("flare"))
        {
            return "eruption";
        }
        if (n.Contains("irradiance") || n.Contains("radiation"))
        {
            return "radiation";
        }
        if (n.Contains("quake") || n.Contains("shifting altitudes"))
        {
            return "quake";
        }
        if (n.Contains("rough seas"))
        {
            return "waves";
        }
        if (n.Contains("cloud"))
        {
            return "clouds";
        }
        if (n.Contains("umbral"))
        {
            return "arcane";
        }
        if (n.Contains("storm"))
        {
            return "thunderstorm";
        }
        return "clouds";
    }

    /// <summary>The shared texture is cached, never its wrap handle: shared wraps are only valid for the
    /// frame they were resolved in, and a handle cached across a logout/zone flush dangles and crashes the
    /// renderer.</summary>
    private ImTextureID? GetIcon(uint iconId)
    {
        if (iconId == 0)
        {
            return null;
        }
        if (!_iconCache.TryGetValue(iconId, out var tex))
        {
            try
            {
                tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, $"[Weather] Failed to load icon {iconId}.");
                tex = null;
            }
            _iconCache[iconId] = tex;
        }
        return tex?.GetWrapOrDefault()?.Handle;
    }

    /// <summary>The well-known Eorzean forecast hash over 8-Eorzea-hour windows.</summary>
    private static byte CalculateForecastTarget(long unixSeconds)
    {
        var bell = unixSeconds / 175;
        var increment = (uint)((bell + 8 - bell % 8) % 24);
        var totalDays = (uint)(unixSeconds / 4200);
        var calcBase = totalDays * 100u + increment;
        var step1 = (calcBase << 11) ^ calcBase;
        var step2 = (step1 >> 8) ^ step1;
        return (byte)(step2 % 100);
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
        ClearWeatherOverride();
        ClearTimeOverride();
    }
}
