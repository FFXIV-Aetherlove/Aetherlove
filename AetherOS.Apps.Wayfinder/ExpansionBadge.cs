using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove;
using AetherLove.Services;
using AetherLove.UI;
using AetherLove.Shared.Profile.Enums;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Wayfinder;

/// <summary>The round expansion badge shown on challenge cards, loaded from
/// Media/icons/expansions/&lt;enum-name&gt;.png with a compass-glyph fallback. Hover shows the expansion name.</summary>
internal static class ExpansionBadge
{
    private static readonly Dictionary<short, ISharedImmediateTexture?> Cache = new();

    public static string Label(short expansion) => ProfileFields.ExpansionLabel((Expansion)expansion);

    /// <summary>Draws the badge at the given top-left and adds the hover tooltip; returns its size.</summary>
    public static float Draw(ImDrawListPtr dl, Vector2 tl, float size, short expansion)
    {
        var handle = Resolve(expansion);
        if (handle is { } tex)
        {
            dl.AddImageRounded(tex, tl, tl + new Vector2(size, size), Vector2.Zero, Vector2.One,
                0xFFFFFFFFu, size * 0.5f);
        }
        else
        {
            var t = ThemeService.Current;
            dl.AddCircleFilled(tl + new Vector2(size * 0.5f, size * 0.5f), size * 0.5f,
                ImGui.GetColorU32(t.Accent with { W = 0.25f }));
            var iconPx = size * 0.55f;
            var iconSz = IconDraw.Measure(FontAwesomeIcon.Compass, iconPx);
            IconDraw.Add(dl, FontAwesomeIcon.Compass, iconPx,
                tl + new Vector2((size - iconSz.X) * 0.5f, (size - iconSz.Y) * 0.5f),
                ImGui.GetColorU32(t.AccentLight));
        }

        if (ImGui.IsMouseHoveringRect(tl, tl + new Vector2(size, size)))
        {
            ImGui.SetTooltip(Label(expansion));
        }
        return size;
    }

    private static ImTextureID? Resolve(short expansion)
    {
        if (!Cache.TryGetValue(expansion, out var tex))
        {
            var dir = Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "";
            var name = ((Expansion)expansion).ToString().ToLowerInvariant();
            var path = Path.Combine(dir, "Media", "icons", "expansions", name + ".png");
            tex = File.Exists(path) ? UiHost.TextureProvider.GetFromFile(path) : null;
            Cache[expansion] = tex;
        }
        return tex?.GetWrapOrDefault()?.Handle;
    }
}
