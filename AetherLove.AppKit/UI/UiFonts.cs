using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace AetherLove.UI;

/// <summary>Per-size font handles for the phone UI, built at their real on-screen pixel size so text
/// stays sharp instead of being stretched.</summary>
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

    public static IFontHandle? Body { get; private set; }

    /// <summary>Long-form reading surfaces (news articles): a step above Body for comfortable body copy.</summary>
    public static IFontHandle? Reader { get; private set; }

    public static IFontHandle? H1   { get; private set; }
    public static IFontHandle? H2   { get; private set; }
    public static IFontHandle? H3   { get; private set; }

    public static IFontHandle? Clock { get; private set; }

    public static IFontHandle? Icon { get; private set; }

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

    /// <summary>Call at startup and whenever the size preset changes.</summary>
    public static void Rebuild()
    {
        Dispose();
        var atlas = UiHost.PluginInterface.UiBuilder.FontAtlas;
        Body = Build(atlas, BodyPx);
        Reader = Build(atlas, ReaderPx);
        H3   = Build(atlas, H3Px);
        H2   = Build(atlas, H2Px);
        H1   = Build(atlas, H1Px);
        Clock = BuildClock(atlas, ClockPx);
        Icon = BuildIcon(atlas, IconPx);
    }

    private static IFontHandle Build(IFontAtlas atlas, float designPx) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var px = designPx * UiScale.S;
            var font = tk.AddDalamudAssetFont(
                Dalamud.DalamudAsset.NotoSansCjkMedium,
                new SafeFontConfig { SizePx = px, GlyphRanges = ChromeGlyphRanges });
            // Merge the game's AXIS glyphs (full Japanese kana + kanji, copied pre-baked with no FreeType
            // rasterisation) so player names and messages still render without baking the whole CJK block.
            tk.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, px), null, font);
        }));

    private static IFontHandle BuildClock(IFontAtlas atlas, float designPx) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddDalamudAssetFont(
                Dalamud.DalamudAsset.NotoSansCjkMedium,
                new SafeFontConfig { SizePx = designPx * UiScale.S, GlyphRanges = ClockGlyphRanges })));

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
    }
}
