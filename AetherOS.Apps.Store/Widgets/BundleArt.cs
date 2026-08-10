using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Shared.Store;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Store;

/// <summary>A bundle's picture, dealt from its children rather than uploaded. Nobody wants to author a
/// composite every time a bundle changes hands, and a stored composite goes stale the moment a child's art
/// is replaced, so this fans the children's own pictures like a hand of cards at draw time. Free, always
/// current, and it works at every size the app draws a product at.</summary>
internal static class BundleArt
{
    /// <summary>The most cards the fan reads as; a fourth is a smear at phone size.</summary>
    private const int MaxCards = 3;

    /// <summary>Below this the fan is mush, so a small thumb shows the first child alone.</summary>
    private const float FanMinWidth = 34f;

    /// <summary>Draws the fan into the rect, rounded on the given corners. Returns false when none of the
    /// children have art yet, so the caller falls back to its own glyph.</summary>
    public static bool Draw(
        ImDrawListPtr dl, StoreMediaCache media, StoreProductDto product, Vector2 tl, Vector2 size,
        float rounding, ImDrawFlags corners = ImDrawFlags.RoundCornersAll, float alpha = 1f)
    {
        if (product.ItemKind != StoreItemKind.Bundle || product.BundleItems.Length == 0)
        {
            return false;
        }

        var wraps = new Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap?[MaxCards];
        var found = 0;
        for (var i = 0; i < product.BundleItems.Length && found < MaxCards; i++)
        {
            var child = product.BundleItems[i];
            if (media.Get(child.ChildProductId, child.ImageVersion)?.Tex?.GetWrapOrDefault() is { } wrap)
            {
                wraps[found++] = wrap;
            }
        }
        if (found == 0)
        {
            return false;
        }

        var tint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha));
        if (found == 1 || size.X < Px(FanMinWidth))
        {
            var only = wraps[0]!;
            var (uv0, uv1) = OsDrawShared.CoverUv(only.Width, only.Height, size.X, size.Y);
            dl.AddImageRounded(only.Handle, tl, tl + size, uv0, uv1, tint, rounding, corners);
            return true;
        }

        // A card per child, drawn back to front so the first one lands on top, each one rotated a little
        // further and nudged right so the stack reads as a fan rather than as a pile.
        dl.PushClipRect(tl, tl + size, true);
        var center = tl + size * 0.5f;
        var cardH = size.Y * 0.84f;
        var cardW = MathF.Min(cardH * 0.74f, size.X * 0.52f);
        // The step has to be a real fraction of a card or the fan collapses into a pile that reads as one
        // picture; it is also capped so the whole hand still fits the rect.
        var spread = MathF.Min(cardW * 0.62f, (size.X - cardW) / MathF.Max(1, found - 1));
        for (var i = found - 1; i >= 0; i--)
        {
            var wrap = wraps[i]!;
            var offset = (i - (found - 1) * 0.5f) * spread;
            var angle = (i - (found - 1) * 0.5f) * 0.20f;
            var cardCenter = new Vector2(center.X + offset, center.Y + MathF.Abs(offset) * 0.10f);
            var half = new Vector2(cardW * 0.5f, cardH * 0.5f);
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);

            Vector2 Corner(float sx, float sy)
            {
                var p = new Vector2(half.X * sx, half.Y * sy);
                return cardCenter + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
            }

            var p1 = Corner(-1f, -1f);
            var p2 = Corner(1f, -1f);
            var p3 = Corner(1f, 1f);
            var p4 = Corner(-1f, 1f);

            // The card's own drop shadow, so the overlap reads as depth.
            var lift = new Vector2(Px(1.5f), Px(2f));
            dl.AddQuadFilled(p1 + lift, p2 + lift, p3 + lift, p4 + lift,
                OsDrawShared.Black(0.45f * alpha));

            var (uv0, uv1) = OsDrawShared.CoverUv(wrap.Width, wrap.Height, cardW, cardH);
            dl.AddImageQuad(wrap.Handle, p1, p2, p3, p4,
                uv0, new Vector2(uv1.X, uv0.Y), uv1, new Vector2(uv0.X, uv1.Y), tint);
            dl.AddQuad(p1, p2, p3, p4,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.22f * alpha)), Px(1.2f));
        }
        dl.PopClipRect();
        return true;
    }
}
