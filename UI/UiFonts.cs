using Dalamud.Interface.ManagedFontAtlas;

namespace AetherLove.UI;

/// <summary>Per-size font handles for the phone UI, built at their real on-screen pixel size so text
/// stays sharp instead of being stretched.</summary>
public static class UiFonts
{
    // Design pixel sizes at S = 1.0.
    private const float BodyPx = 17f;
    private const float H3Px   = 20f;
    private const float H2Px   = 26f;
    private const float H1Px   = 34f;

    /// <summary>Rasterised large so big icons downscale instead of upscaling from Dalamud's ~17px default.</summary>
    private const float IconPx = 56f;

    public static IFontHandle? Body { get; private set; }
    public static IFontHandle? H1   { get; private set; }
    public static IFontHandle? H2   { get; private set; }
    public static IFontHandle? H3   { get; private set; }

    public static IFontHandle? Icon { get; private set; }

    /// <summary>The atlas builds asynchronously; pushing an unfinished handle silently falls back to the
    /// default font.</summary>
    public static bool Ready =>
        Body is { Available: true }
        && H1 is { Available: true }
        && H2 is { Available: true }
        && H3 is { Available: true }
        && Icon is { Available: true };

    /// <summary>Call at startup and whenever the size preset changes.</summary>
    public static void Rebuild()
    {
        Dispose();
        var atlas = Plugin.PluginInterface.UiBuilder.FontAtlas;
        Body = Build(atlas, BodyPx);
        H3   = Build(atlas, H3Px);
        H2   = Build(atlas, H2Px);
        H1   = Build(atlas, H1Px);
        Icon = BuildIcon(atlas, IconPx);
    }

    private static IFontHandle Build(IFontAtlas atlas, float designPx) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddDalamudAssetFont(
                Dalamud.DalamudAsset.NotoSansCjkMedium,
                new SafeFontConfig { SizePx = designPx * UiScale.S })));

    private static IFontHandle BuildIcon(IFontAtlas atlas, float designPx) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = designPx * UiScale.S })));

    public static void Dispose()
    {
        Body?.Dispose();
        H1?.Dispose();
        H2?.Dispose();
        H3?.Dispose();
        Icon?.Dispose();
        Body = H1 = H2 = H3 = Icon = null;
    }
}
