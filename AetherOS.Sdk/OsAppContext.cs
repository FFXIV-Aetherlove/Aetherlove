using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Interface.ManagedFontAtlas;

namespace AetherOS.Sdk;

/// <summary>Theme colors handed to app surfaces each frame.</summary>
public readonly record struct OsTheme(
    Vector4 Accent,
    Vector4 AccentLight,
    Vector4 AccentDark,
    Vector4 ChipFill,
    Vector4 SecondaryStart,
    Vector4 SecondaryEnd,
    Vector4 ButtonNormal,
    Vector4 ButtonHovered,
    Vector4 ButtonActive);

/// <summary>Per-frame environment passed to <see cref="IAetherApp.Draw"/>.</summary>
public sealed class OsAppContext
{
    public required float Scale { get; init; }
    public required Vector2 ContentSize { get; init; }
    public required OsTheme Theme { get; init; }
    public required Func<string, string> Localize { get; init; }

    /// <summary>Culture of the phone's selected language (not the game/OS culture); use it for any date or
    /// number formatting so month and weekday names follow the phone language.</summary>
    public required CultureInfo Culture { get; init; }

    public required IOsShell Shell { get; init; }
    public required bool ReduceMotion { get; init; }
    public IFontHandle? TitleFont { get; init; }
    public IFontHandle? HeadingFont { get; init; }

    /// <summary>Reusable platform capabilities (camera, image picking, textures, sharing, system side effects).</summary>
    public required IAppCapabilities Capabilities { get; init; }

    /// <summary>Design pixels to device pixels.</summary>
    public float Px(float v) => MathF.Round(v * Scale);
    public Vector2 Px(float x, float y) => new(MathF.Round(x * Scale), MathF.Round(y * Scale));
}
