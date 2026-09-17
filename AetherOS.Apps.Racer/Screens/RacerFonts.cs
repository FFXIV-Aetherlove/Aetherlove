using System;
using System.IO;
using AetherLove;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>Every text size the racer draws, each baked as its own font handle at the phone's current scale so
/// text is always drawn at the size it was rasterised at. <see cref="Preload"/> builds the set when the phone
/// loads, alongside the shared phone fonts, and rebuilds it with them whenever the phone size or font family
/// changes, so opening the app never waits on the atlas. The set lives as long as the plugin.
/// <para>With the default family the shared phone fonts take their Latin letters from the game's AXIS glyphs,
/// which are bitmaps at a few fixed sizes stretched to any other size and read blurry. The racer draws Latin
/// from Noto at the exact size instead, and AXIS only for Japanese. Noto runs smaller than AXIS at the same pixel
/// size, so the set first bakes a Noto probe, measures one sample line against the shared body font, and bakes
/// every size scaled by that ratio; until then the shared fonts stand in.</para></summary>
internal static class RacerFonts
{
    private const int SmallPx = 12;
    private const int CaptionPx = 14;
    private const int BodyPx = 17;
    private const int ButtonPx = 22;
    private const int HeadingPx = 26;
    private const string MetricSample = "Race cards give your Lumi bonuses during races.";

    /// <summary>Noto's lowercase letters are shorter than AXIS's, so text of equal width still reads small. Tuned by eye
    /// in game on 2026-09-17: the width match alone was too small and a ratio of 1.6 too big.</summary>
    private const float NotoReadingBoost = 1.2f;
    private const float MinMatch = 0.8f;
    private const float MaxMatch = 1.6f;

    /// <summary>The Russian card number, "№ 3". It sits outside every other range, and the merged Noto supplies it for
    /// a family that lacks it.</summary>
    private const ushort NumeroSign = 0x2116;

    private static bool _following;
    private static IFontHandle? _basis;
    private static float _scale;
    private static string? _family;
    private static IFontHandle? _probe;
    private static float _match;
    private static IFontHandle? _small;
    private static IFontHandle? _caption;
    private static IFontHandle? _body;
    private static IFontHandle? _button;
    private static IFontHandle? _heading;
    private static readonly ushort[] TextRanges = [0x0020, 0x017F, 0x0400, 0x04FF, 0x2000, 0x206F, NumeroSign, NumeroSign, 0];
    private static readonly ushort[] PunctuationRanges = [0x2000, 0x206F, NumeroSign, NumeroSign, 0];
    private static readonly ushort[] CyrillicRanges = [0x0400, 0x04FF, 0x2000, 0x206F, NumeroSign, NumeroSign, 0];
    private static readonly ushort[] JapaneseRanges = [0x3000, 0x30FF, 0x31F0, 0x31FF, 0x4E00, 0x9FFF, 0xFF00, 0xFFEF, 0];
    private static readonly ushort[] LatinRanges = [0x0020, 0x007E, 0];

    /// <summary>Follows every rebuild of the shared phone fonts and builds the whole set with them, so the atlas
    /// has baked each size long before the app first draws. Called once, when the app is built at phone load;
    /// when the shared fonts are not built yet it only subscribes, and their first build brings this set along.</summary>
    public static void Preload()
    {
        if (!_following)
        {
            _following = true;
            UiFonts.Rebuilt += Build;
        }
        if (UiFonts.Body is not null && IsStale())
        {
            Build();
        }
    }

    /// <summary>The baked handle for <paramref name="size"/>. While the atlas is still building it, or the Noto probe
    /// is still waiting to be measured, the nearest shared phone font stands in. Call from the draw thread.</summary>
    public static IFontHandle? Get(RacerTextSize size)
    {
        if (IsStale())
        {
            Build();
        }

        if (_match <= 0f)
        {
            TryMeasure();
        }

        return size switch
        {
            RacerTextSize.Small => Ready(_small) ?? Ready(_caption) ?? UiFonts.Body,
            RacerTextSize.Caption => Ready(_caption) ?? UiFonts.Body,
            RacerTextSize.Button => Ready(_button) ?? UiFonts.H3,
            RacerTextSize.Heading => Ready(_heading) ?? UiFonts.H2,
            _ => Ready(_body) ?? UiFonts.Body,
        };
    }

    private static IFontHandle? Ready(IFontHandle? handle) => handle is { Available: true } ? handle : null;

    private static bool IsStale() =>
        _basis is null
        || !ReferenceEquals(_basis, UiFonts.Body)
        || _scale != UiScale.S
        || _family != UiFonts.ActiveFamily.Id;

    private static void Build()
    {
        Dispose();
        _basis = UiFonts.Body;
        _scale = UiScale.S;
        _family = UiFonts.ActiveFamily.Id;
        if (FamilyFile() is null)
        {
            _probe = UiHost.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
                tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansCjkMedium,
                    new SafeFontConfig { SizePx = MathF.Round(BodyPx * UiScale.S), GlyphRanges = LatinRanges })));
            return;
        }

        CreateAll(1f);
    }

    /// <summary>Compares the sample line's width per pixel of font size in the shared body font and in the Noto probe,
    /// then bakes the set at that ratio times <see cref="NotoReadingBoost"/>. Glyph boxes cannot be compared instead:
    /// the game's glyphs are stored as full line-height cells. Waits, returning, until both fonts have built.</summary>
    private static void TryMeasure()
    {
        if (_probe is not { Available: true } probe || UiFonts.Body is not { Available: true } shared)
        {
            return;
        }

        float sharedWidth;
        float probeWidth;
        using (shared.Push())
        {
            sharedWidth = ImGui.CalcTextSize(MetricSample).X;
        }

        using (probe.Push())
        {
            probeWidth = ImGui.CalcTextSize(MetricSample).X;
        }

        var sharedPx = BodyPx * UiScale.S;
        var probePx = MathF.Round(BodyPx * UiScale.S);
        var widthMatch = probeWidth > 0f ? (sharedWidth / sharedPx) / (probeWidth / probePx) : 1f;
        var match = Math.Clamp(widthMatch * NotoReadingBoost, MinMatch, MaxMatch);
        _probe.Dispose();
        _probe = null;
        CreateAll(match);
    }

    private static void CreateAll(float match)
    {
        _match = match;
        _small = Create(SmallPx, match);
        _caption = Create(CaptionPx, match);
        _body = Create(BodyPx, match);
        _button = Create(ButtonPx, match);
        _heading = Create(HeadingPx, match);
    }

    /// <summary>The picked family's font file, or null when the family is built in or its file is missing, which is
    /// when the shared phone fonts fall back to Noto with the game's glyphs.</summary>
    private static string? FamilyFile()
    {
        var family = UiFonts.ActiveFamily;
        if (family.File is null)
        {
            return null;
        }

        var file = AetherLove.Services.Media.MediaPaths.Shipped(AetherLove.Services.Media.MediaPaths.Fonts, family.File);
        return File.Exists(file) ? file : null;
    }

    private static IFontHandle Create(int designPx, float match)
    {
        var family = UiFonts.ActiveFamily;
        var px = MathF.Round(designPx * UiScale.S * match);
        var file = FamilyFile();
        return UiHost.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            if (file is not null)
            {
                var font = tk.AddFontFromFile(file, new SafeFontConfig { SizePx = px * family.Trim, GlyphRanges = TextRanges });
                tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansCjkMedium, new SafeFontConfig
                {
                    SizePx = px,
                    GlyphRanges = family.CyrillicFallback ? CyrillicRanges : PunctuationRanges,
                    MergeFont = font,
                });
                tk.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, px), JapaneseRanges, font);
            }
            else
            {
                var font = tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansCjkMedium,
                    new SafeFontConfig { SizePx = px, GlyphRanges = TextRanges });
                tk.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, px), JapaneseRanges, font);
            }
        }));
    }

    private static void Dispose()
    {
        _probe?.Dispose();
        _small?.Dispose();
        _caption?.Dispose();
        _body?.Dispose();
        _button?.Dispose();
        _heading?.Dispose();
        _probe = null;
        _small = null;
        _caption = null;
        _body = null;
        _button = null;
        _heading = null;
        _basis = null;
        _match = 0f;
    }
}
