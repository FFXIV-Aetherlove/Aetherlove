using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherLove.UI;

/// <summary>The spark coin, drawn wherever something was bought with sparks. Falls back to the bolt glyph
/// when the art is missing, so a stripped install still renders the badge.</summary>
public static class SparkIcon
{
    private static ISharedImmediateTexture? _texture;
    private static bool _loaded;

    public static void Draw(ImDrawListPtr dl, Vector2 center, float size)
    {
        if (Ensure()?.GetWrapOrDefault() is { } wrap)
        {
            var half = new Vector2(size * 0.5f);
            dl.AddImage(wrap.Handle, center - half, center + half);
            return;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, size * 0.85f, center,
            ImGui.ColorConvertFloat4ToU32(UiColors.SparkGold));
    }

    private static ISharedImmediateTexture? Ensure()
    {
        if (_loaded)
        {
            return _texture;
        }
        _loaded = true;
        try
        {
            var dir = Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? string.Empty;
            var path = Path.Combine(dir, "Media", "spark.png");
            if (File.Exists(path))
            {
                _texture = UiHost.TextureProvider.GetFromFile(path);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[SparkIcon] Failed to load the spark coin.");
        }
        return _texture;
    }
}
