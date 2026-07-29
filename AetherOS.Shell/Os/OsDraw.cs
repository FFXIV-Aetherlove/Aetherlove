using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Os;

/// <summary>Shared draw helpers for the OS layer. The pure-ImGui primitives live in
/// <see cref="OsDrawShared"/> (AetherLove.AppKit) so the surface apps can share them; these forward to it.</summary>
internal static class OsDraw
{
    public static void RoundedGradient(ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding,
        Vector4 top, Vector4 bottom, float alpha = 1f) =>
        OsDrawShared.RoundedGradient(dl, tl, br, rounding, top, bottom, alpha);

    /// <summary>An app tile without label or badge, used by the grid, dock, shade rows, and transitions.</summary>
    public static void AppTile(ImDrawListPtr dl, IAetherApp app, Vector2 tl, Vector2 br, float alpha = 1f)
    {
        var size = br.X - tl.X;
        var rounding = size * 0.28f;
        var center = (tl + br) * 0.5f;
        // External apps carry their own icon; first-party apps get one from Media/appicons/<id>.png when present.
        if ((app.TileImage ?? AppIcons.Tile(app.Id)) is { } iconImage)
        {
            dl.AddImageRounded(iconImage, tl, br, Vector2.Zero, Vector2.One, White(alpha), rounding,
                ImDrawFlags.RoundCornersAll);
            dl.AddRect(tl + new Vector2(1f, 1f), br - new Vector2(1f, 1f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.20f * alpha)), rounding,
                ImDrawFlags.RoundCornersAll, 1f);
            return;
        }
        RoundedGradient(dl, tl, br, rounding, app.TileTop, app.TileBottom, alpha);
        dl.AddRect(tl + new Vector2(1f, 1f), br - new Vector2(1f, 1f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.20f * alpha)), rounding,
            ImDrawFlags.RoundCornersAll, 1f);
        UI.IconDraw.AddCentered(dl, app.Icon, size * 0.46f, center + new Vector2(0f, 1f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.30f * alpha)));
        UI.IconDraw.AddCentered(dl, app.Icon, size * 0.46f, center,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha)));
    }

    /// <summary>Red "new" pill overhanging a tile's top-left corner, deliberately English-only.</summary>
    public static void NewBadge(ImDrawListPtr dl, Vector2 corner)
    {
        const string label = "new";
        var fsz = ImGui.GetFontSize() * 0.72f;
        var tsz = ImGui.CalcTextSize(label) * (fsz / ImGui.GetFontSize());
        var padX = Px(5f);
        var h = tsz.Y + Px(4f);
        var tl = new Vector2(corner.X - padX, corner.Y - h * 0.5f);
        var br = tl + new Vector2(tsz.X + padX * 2f, h);
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(0.86f, 0.13f, 0.16f, 1f)), h * 0.5f);
        dl.AddRect(tl, br, White(0.55f), h * 0.5f, ImDrawFlags.None, 1f);
        dl.AddText(ImGui.GetFont(), fsz, new Vector2(tl.X + padX, tl.Y + (h - tsz.Y) * 0.5f), 0xFFFFFFFFu, label);
    }

    public static void Badge(ImDrawListPtr dl, Vector2 center, int count, float scale = 1f)
    {
        var label = count > 99 ? "99+" : count.ToString();
        var fsz = ImGui.GetFontSize() * 0.82f * scale;
        var tsz = ImGui.CalcTextSize(label) * (fsz / ImGui.GetFontSize());
        var badgeR = MathF.Max(Px(11f) * scale, tsz.X * 0.5f + Px(4f) * scale);
        dl.AddCircleFilled(center, badgeR, UI.UiColors.UnreadBadge);
        dl.AddText(ImGui.GetFont(), fsz, center - tsz * 0.5f, 0xFFFFFFFFu, label);
    }

    public static void CenteredText(ImDrawListPtr dl, string text, float centerX, float y, uint color,
        float sizeMul = 1f, float maxWidth = 0f) =>
        OsDrawShared.CenteredText(dl, text, centerX, y, color, sizeMul, maxWidth);

    public static uint White(float alpha) => OsDrawShared.White(alpha);
    public static uint Black(float alpha) => OsDrawShared.Black(alpha);

    public static (Vector2 uv0, Vector2 uv1) CoverUv(float texW, float texH, float destW, float destH) =>
        OsDrawShared.CoverUv(texW, texH, destW, destH);
}
