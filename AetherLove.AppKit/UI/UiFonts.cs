using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace AetherLove.UI;

/// <summary>Per-size font handles for the phone UI, built at their real on-screen pixel size so text
/// stays sharp instead of being stretched. The user-picked family is the primary glyph layer; Noto and the
/// game's AXIS glyphs are merged behind it, so anything the family lacks (Cyrillic, Japanese, symbols)
/// falls back instead of rendering as boxes.</summary>
public static class UiFonts
{
    // Design pixel sizes at S = 1.0.
    private const float BodyPx = 17f;
    private const float ReaderPx = 19f;
    private const float H3Px   = 20f;
    private const float H2Px   = 26f;
    private const float H1Px   = 34f;

    /// <summary>Home screen clock.</summary>
    private const float ClockPx = 48f;

    /// <summary>Rasterised large so big icons downscale instead of upscaling from Dalamud's ~17px default.</summary>
    private const float IconPx = 56f;

    /// <summary>A pickable family: <paramref name="File"/> is a TTF under Media/fonts (null = built-in),
    /// <paramref name="Trim"/> scales the load size so every family's line height matches Noto's, and
    /// <paramref name="CyrillicFallback"/> merges Noto Cyrillic for a family that ships none.</summary>
    public sealed record FontFamilyDef(string Id, string? Label, string? File, float Trim, bool CyrillicFallback = false);

    /// <summary>Null label = the localized "Default" string. The default build already renders the game's
    /// AXIS glyphs over Noto, so a separate AXIS entry would be a duplicate of it.</summary>
    public static readonly FontFamilyDef[] Families =
    [
        new("default", null, null, 1f),
        new("inter", "Inter", "Inter.ttf", 1f),
        new("comfortaa", "Comfortaa", "Comfortaa.ttf", 0.94f),
        new("exo2", "Exo 2", "Exo2.ttf", 1f),
        new("caveat", "Caveat", "Caveat.ttf", 1.18f),
        new("pixel", "VT323", "VT323.ttf", 1.12f, CyrillicFallback: true),
    ];

    public static FontFamilyDef ActiveFamily
    {
        get
        {
            var id = UiHost.Configuration.Os.FontFamily;
            foreach (var family in Families)
            {
                if (family.Id == id)
                {
                    return family;
                }
            }
            return Families[0];
        }
    }

    private static string FontDir =>
        Path.Combine(Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? string.Empty,
            "Media", "fonts");

    // Bounding the Noto range is what keeps the atlas rebuild fast: with no GlyphRanges set, Dalamud bakes the
    // whole BMP (~20-40k CJK glyphs) at every size, which is the multi-second/multi-minute size-change stall.
    // This covers the app's own six languages (Latin + Cyrillic) plus punctuation; Japanese comes from the
    // game's pre-baked AXIS glyphs, merged in below.
    private static readonly ushort[] ChromeGlyphRanges =
    {
        0x0020, 0x00FF, // Basic Latin + Latin-1 Supplement
        0x0100, 0x017F, // Latin Extended-A (European diacritics: DE/ES/FR/PT)
        0x2000, 0x206F, // General Punctuation (dashes, curly quotes, ellipsis)
        0x0400, 0x04FF, // Cyrillic (Russian)
        0,
    };

    /// <summary>The clock only ever renders "HH:mm"; a digits-only range keeps the largest (96px) bake tiny.</summary>
    private static readonly ushort[] ClockGlyphRanges =
    {
        0x0020, 0x0020, // space
        0x0030, 0x003A, // digits 0-9 and ':'
        0,
    };

    /// <summary>Picker previews and the font debug window: Latin plus Cyrillic, no merge fallback, so a
    /// family missing Cyrillic shows fallback boxes there instead of hiding it.</summary>
    private static readonly ushort[] PreviewGlyphRanges =
    {
        0x0020, 0x00FF,
        0x0400, 0x04FF,
        0,
    };

    /// <summary>Merged sources OVERWRITE overlapping glyphs in this builder (they do not just fill gaps),
    /// so every merge below must use ranges that cannot collide with the primary family.</summary>
    private static readonly ushort[] PunctuationGlyphRanges =
    {
        0x2000, 0x206F,
        0,
    };

    private static readonly ushort[] CyrillicAndPunctuationGlyphRanges =
    {
        0x0400, 0x04FF,
        0x2000, 0x206F,
        0,
    };

    private static readonly ushort[] JapaneseGlyphRanges =
    {
        0x3000, 0x30FF, // CJK punctuation + kana
        0x31F0, 0x31FF, // katakana phonetic extensions
        0x4E00, 0x9FFF, // kanji
        0xFF00, 0xFFEF, // fullwidth forms
        0,
    };

    public static string DiagnosticsFontDir => FontDir;

    public static string? DiagnosticsResolve(FontFamilyDef family) => ResolveFile(family);

    public static IFontHandle? Body { get; private set; }

    /// <summary>Long-form reading surfaces (news articles): a step above Body for comfortable body copy.</summary>
    public static IFontHandle? Reader { get; private set; }

    public static IFontHandle? H1   { get; private set; }
    public static IFontHandle? H2   { get; private set; }
    public static IFontHandle? H3   { get; private set; }

    public static IFontHandle? Clock { get; private set; }

    public static IFontHandle? Icon { get; private set; }

    private static readonly Dictionary<string, IFontHandle> Previews = new();

    /// <summary>The atlas builds asynchronously; pushing an unfinished handle silently falls back to the
    /// default font.</summary>
    public static bool Ready =>
        Body is { Available: true }
        && Reader is { Available: true }
        && H1 is { Available: true }
        && H2 is { Available: true }
        && H3 is { Available: true }
        && Clock is { Available: true }
        && Icon is { Available: true };

    /// <summary>Call at startup and whenever the size preset or font family changes.</summary>
    public static void Rebuild()
    {
        Dispose();
        var family = ActiveFamily;
        UiHost.Log.Debug("[UiFonts] Rebuild: config='{Raw}' resolved='{Family}' file='{File}' scale={Scale}",
            UiHost.Configuration.Os.FontFamily, family.Id, DiagnosticsResolve(family) ?? "(built-in)", UiScale.S);
        var atlas = UiHost.PluginInterface.UiBuilder.FontAtlas;
        Body = Build(atlas, BodyPx);
        Reader = Build(atlas, ReaderPx);
        H3   = Build(atlas, H3Px);
        H2   = Build(atlas, H2Px);
        H1   = Build(atlas, H1Px);
        Clock = BuildClock(atlas, ClockPx);
        Icon = BuildIcon(atlas, IconPx);
    }

    /// <summary>A Body-sized handle in the given family, for the settings picker's live previews. Built
    /// lazily and kept until the next <see cref="Rebuild"/>.</summary>
    public static IFontHandle? Preview(FontFamilyDef family)
    {
        if (Previews.TryGetValue(family.Id, out var handle))
        {
            return handle;
        }
        var atlas = UiHost.PluginInterface.UiBuilder.FontAtlas;
        handle = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var px = BodyPx * UiScale.S;
            if (TryAddFamilyFile(tk, family, px, PreviewGlyphRanges) is not null)
            {
            }
            else
            {
                tk.AddDalamudAssetFont(
                    Dalamud.DalamudAsset.NotoSansCjkMedium,
                    new SafeFontConfig { SizePx = px, GlyphRanges = PreviewGlyphRanges });
            }
        }));
        Previews[family.Id] = handle;
        return handle;
    }

    private static Dalamud.Bindings.ImGui.ImFontPtr? TryAddFamilyFile(
        IFontAtlasBuildToolkitPreBuild tk, FontFamilyDef family, float px, ushort[] ranges)
    {
        if (ResolveFile(family) is not { } file)
        {
            return null;
        }
        try
        {
            return tk.AddFontFromFile(file, new SafeFontConfig
            {
                SizePx = px * family.Trim,
                GlyphRanges = ranges,
            });
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[UiFonts] Font file {File} failed to load; using the default font.", family.File ?? "");
            return null;
        }
    }

    private static string? ResolveFile(FontFamilyDef family)
    {
        if (family.File is null)
        {
            return null;
        }
        var path = Path.Combine(FontDir, family.File);
        return File.Exists(path) ? path : null;
    }

    private static IFontHandle Build(IFontAtlas atlas, float designPx) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var family = ActiveFamily;
            var px = designPx * UiScale.S;
            Dalamud.Bindings.ImGui.ImFontPtr font;
            if (TryAddFamilyFile(tk, family, px, ChromeGlyphRanges) is { } fromFile)
            {
                font = fromFile;
                // The bundled subsets carry full Latin + Cyrillic themselves; Noto only supplies punctuation,
                // and AXIS only Japanese, so neither merge can overwrite the family's own glyphs.
                tk.AddDalamudAssetFont(
                    Dalamud.DalamudAsset.NotoSansCjkMedium,
                    new SafeFontConfig
                    {
                        SizePx = px,
                        GlyphRanges = family.CyrillicFallback ? CyrillicAndPunctuationGlyphRanges : PunctuationGlyphRanges,
                        MergeFont = font,
                    });
                tk.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, px), JapaneseGlyphRanges, font);
                return;
            }
            font = tk.AddDalamudAssetFont(
                Dalamud.DalamudAsset.NotoSansCjkMedium,
                new SafeFontConfig { SizePx = px, GlyphRanges = ChromeGlyphRanges });
            // Merge the game's AXIS glyphs (full Japanese kana + kanji, copied pre-baked with no FreeType
            // rasterisation) so player names and messages still render without baking the whole CJK block.
            tk.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, px), null, font);
        }));

    private static IFontHandle BuildClock(IFontAtlas atlas, float designPx) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var family = ActiveFamily;
            var px = designPx * UiScale.S;
            if (TryAddFamilyFile(tk, family, px, ClockGlyphRanges) is not null)
            {
            }
            else
            {
                tk.AddDalamudAssetFont(
                    Dalamud.DalamudAsset.NotoSansCjkMedium,
                    new SafeFontConfig { SizePx = px, GlyphRanges = ClockGlyphRanges });
            }
        }));

    private static IFontHandle BuildIcon(IFontAtlas atlas, float designPx) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = designPx * UiScale.S })));

    public static void Dispose()
    {
        Body?.Dispose();
        Reader?.Dispose();
        H1?.Dispose();
        Clock?.Dispose();
        H2?.Dispose();
        H3?.Dispose();
        Icon?.Dispose();
        Body = Reader = H1 = H2 = H3 = Clock = Icon = null;
        foreach (var preview in Previews.Values)
        {
            preview.Dispose();
        }
        Previews.Clear();
    }
}
