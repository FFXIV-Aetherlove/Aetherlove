using System;
using System.IO;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The racer's background pictures. Two shapes, and they want opposite treatment: race day
/// is a SCENE, so it covers and crops; the race card is a FRAME, so cropping it would cut the border
/// and the corner sparkles off, and it is three-sliced instead.</summary>
internal static class RacerBackdrop
{
    /// <param name="anchorY">Which part of the taller-than-the-screen picture to show: 0.5 centres it,
    /// 1 pins its bottom. A page that lays a paper sheet over the middle only ever shows the picture in
    /// the margin around it, and the top of this one is bunting, which reads as a stray red triangle
    /// poking out from behind the sheet.</param>
    public static void Draw(OsAppContext ctx, IRacerHost host, Vector2 origin, Vector2 size, float dim,
        float anchorY = 0.5f)
    {
        var dl = ImGui.GetWindowDrawList();
        var path = Path.Combine(host.PetAssetRoot, "racer", "race-day-bg.png");
        if (ctx.Capabilities.Textures.Get(path) is not { } art)
        {
            dl.AddRectFilled(origin, origin + size, 0xFF1A1626);
            return;
        }

        var texel = ctx.Capabilities.Textures.GetSize(path) ?? new Vector2(768f, 1536f);
        var scale = MathF.Max(size.X / texel.X, size.Y / texel.Y);

        // The picture and the phone are nearly the same shape, so covering it leaves almost no slack to
        // slide: an anchored page zooms a little to make some, or the anchor would do nothing at all.
        if (anchorY > 0.5f)
        {
            scale = MathF.Max(scale, size.Y * 1.18f / texel.Y);
        }
        var drawn = texel * scale;
        var at = origin + new Vector2((size.X - drawn.X) * 0.5f,
            (size.Y - drawn.Y) * Math.Clamp(anchorY, 0f, 1f));
        dl.PushClipRect(origin, origin + size, true);
        dl.AddImage(art, at, at + drawn);
        if (dim > 0f)
        {
            dl.AddRectFilled(origin, origin + size,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, Math.Clamp(dim, 0f, 1f))));
        }
        dl.PopClipRect();
    }

    /// <summary>The card frame, three-sliced: the banner and the podium keep their own proportions and
    /// only the ink-free middle stretches. Covering the art would crop the frame away.</summary>
    public static void DrawCard(OsAppContext ctx, IRacerHost host, Vector2 origin, Vector2 size, float dim)
    {
        var dl = ImGui.GetWindowDrawList();
        var path = Path.Combine(host.PetAssetRoot, "racer", "race-card-bg.png");
        if (ctx.Capabilities.Textures.Get(path) is not { } art)
        {
            dl.AddRectFilled(origin, origin + size, 0xFFF4F1E8);
            return;
        }

        var texel = ctx.Capabilities.Textures.GetSize(path) ?? new Vector2(904f, 1507f);
        var scale = size.X / texel.X;
        var topV = CardStretchTop / texel.Y;
        var bottomV = CardStretchBottom / texel.Y;
        var topH = CardStretchTop * scale;
        var bottomH = (texel.Y - CardStretchBottom) * scale;
        var middle = MathF.Max(0f, size.Y - topH - bottomH);

        dl.PushClipRect(origin, origin + size, true);
        dl.AddImage(art, origin, origin + new Vector2(size.X, topH),
            Vector2.Zero, new Vector2(1f, topV));
        dl.AddImage(art, origin + new Vector2(0f, topH), origin + new Vector2(size.X, topH + middle),
            new Vector2(0f, topV), new Vector2(1f, bottomV));
        dl.AddImage(art, origin + new Vector2(0f, topH + middle), origin + size,
            new Vector2(0f, bottomV), Vector2.One);
        if (dim > 0f)
        {
            dl.AddRectFilled(origin, origin + size,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, Math.Clamp(dim, 0f, 1f))));
        }
        dl.PopClipRect();
    }

    /// <summary>The ink-free band in race-card-bg.png, measured off the art: everything between these
    /// two rows is paper and the two straight side borders, so it is the only part safe to stretch.
    /// Re-measure them if the picture is ever redrawn.</summary>
    private const float CardStretchTop = 712f;

    private const float CardStretchBottom = 1239f;

    /// <summary>Where the banner art ends, in the same texture rows: content drawn above this sits on
    /// the flags and the ribbon.</summary>
    private const float CardHeaderEnd = 630f;

    private const float CardTexelWidth = 904f;

    private const float CardTexelHeight = 1507f;

    /// <summary>The vertical window the card frame leaves for content once <see cref="DrawCard"/> has
    /// drawn it at <paramref name="size"/>: below the banner, above the podium.</summary>
    public static (float Top, float Bottom) CardWindow(Vector2 size)
    {
        var scale = size.X / CardTexelWidth;
        return (CardHeaderEnd * scale, size.Y - ((CardTexelHeight - CardStretchBottom) * scale));
    }
}
