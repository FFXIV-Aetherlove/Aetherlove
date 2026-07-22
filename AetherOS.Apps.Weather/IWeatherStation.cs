using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Weather;

/// <summary><see cref="IconKey"/> is the semantic weather class ("clear", "levin", "arcane", ...) the host
/// derived from the English weather name; it names the icon file and drives the app's hero tint.</summary>
public sealed record WeatherInfo(byte Id, string Name, ImTextureID? Icon, string IconKey = "clouds");

/// <summary>One natural forecast window (8 Eorzea hours, 23m20s real).</summary>
public sealed record ForecastEntry(WeatherInfo Weather, DateTime StartLocal, bool Active);

/// <summary>Host bridge into the game: zone weather data, the forecast, and env overrides.</summary>
public interface IWeatherStation
{
    /// <summary>False on the title screen or before zone data is available.</summary>
    bool InGame { get; }

    string ZoneName { get; }

    /// <summary>The live weather from the environment manager.</summary>
    WeatherInfo? CurrentWeather { get; }

    /// <summary>Natural forecast for the next windows, first entry is the active one.</summary>
    IReadOnlyList<ForecastEntry> Forecast(int count);

    /// <summary>Weathers that can occur in the current zone.</summary>
    IReadOnlyList<WeatherInfo> ZoneWeathers { get; }

    /// <summary>False in combat; mutations are rejected while false.</summary>
    bool CanMutate { get; }

    byte? WeatherOverride { get; }
    void SetWeatherOverride(byte weatherId);
    void ClearWeatherOverride();

    TimeSpan EorzeaTime { get; }
    int? TimeOverrideMinutes { get; }
    void SetTimeOverride(int minuteOfDay);
    void ClearTimeOverride();
}
