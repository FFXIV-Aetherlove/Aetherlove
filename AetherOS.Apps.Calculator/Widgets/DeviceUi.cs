using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Calculator;

/// <summary>How a key face is tinted, which is the only thing that separates a digit from an operator on a
/// calculator body.</summary>
internal enum KeyTone
{
    Digit,
    Operator,
    Nav,
    Second,
    Alpha,
    Accent,
}

/// <summary>The calculator chassis, dressed as the TI-84 Plus CE it is imitating: the black body, the light
/// screen, white number keys with dark digits, grey operators, and the two coloured modifier keys the device
/// is recognisable by.</summary>
internal static class DeviceUi
{
    /// <summary>The two colours the whole device is organised around: 2nd is cyan and everything it reaches
    /// is printed in cyan, ALPHA is green and the same goes for its letters.</summary>
    public static readonly Vector4 TiCyan = new(0.090f, 0.671f, 0.871f, 1f);
    public static readonly Vector4 TiGreen = new(0.478f, 0.761f, 0.263f, 1f);

    public static readonly Vector4 Chassis = new(0.055f, 0.059f, 0.067f, 1f);
    public static readonly Vector4 ChassisEdge = new(1f, 1f, 1f, 0.07f);
    public static readonly Vector4 SecondLegend = TiCyan;
    public static readonly Vector4 AlphaLegend = TiGreen;
    public static readonly Vector4 Teal = TiCyan;
    public static readonly Vector4 PanelFill = new(0.129f, 0.141f, 0.157f, 1f);

    private static readonly Vector4 KeyWhite = new(0.898f, 0.910f, 0.918f, 1f);
    private static readonly Vector4 KeyGrey = new(0.353f, 0.376f, 0.404f, 1f);
    private static readonly Vector4 KeyDark = new(0.180f, 0.196f, 0.212f, 1f);
    private static readonly Vector4 KeyInk = new(0.067f, 0.078f, 0.090f, 1f);
    private static readonly Vector4 KeyText = new(0.940f, 0.949f, 0.957f, 1f);

    /// <summary>The LCD slab: a green panel, its cell grid, a thin inner shadow and a bezel.</summary>
    public static void Lcd(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size)
    {
        var rounding = ctx.Px(6f);
        var bezel = ctx.Px(3f);
        dl.AddRectFilled(tl - new Vector2(bezel, bezel), tl + size + new Vector2(bezel, bezel),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.043f, 0.051f, 0.055f, 1f)), rounding + bezel);
        dl.AddRectFilled(tl, tl + size, ImGui.ColorConvertFloat4ToU32(RetroLcd.Panel), rounding);

        dl.PushClipRect(tl, tl + size, true);
        var cell = ctx.Px(4f);
        var columns = (int)MathF.Ceiling(size.X / cell);
        var rows = (int)MathF.Ceiling(size.Y / cell);
        RetroLcd.DotGrid(dl, tl, columns, rows, cell);
        for (var i = 0; i < 3; i++)
        {
            var inset = i * ctx.Px(1f);
            var alpha = 0.16f - (i * 0.05f);
            dl.AddRect(tl + new Vector2(inset, inset), tl + size - new Vector2(inset, inset),
                ImGui.ColorConvertFloat4ToU32(RetroLcd.Pixel with { W = alpha }), rounding, ImDrawFlags.None,
                ctx.Px(1f));
        }
        dl.PopClipRect();
    }

    /// <summary>Toward white, keeping the key opaque. Raising alpha instead would let the black chassis
    /// through and darken a hovered white key rather than lifting it.</summary>
    private static Vector4 Lighten(Vector4 color, float amount) => new(
        color.X + (1f - color.X) * amount,
        color.Y + (1f - color.Y) * amount,
        color.Z + (1f - color.Z) * amount,
        color.W);

    /// <summary>Text in the LCD's own ink.</summary>
    public static uint Ink(float alpha = 1f) =>
        ImGui.ColorConvertFloat4ToU32(RetroLcd.Pixel with { W = alpha });

    /// <summary>One key face, with the 2nd legend above-left and the ALPHA letter above-right the way a TI
    /// silkscreens them. Returns true on click.</summary>
    public static bool Key(OsAppContext ctx, string id, Vector2 tl, Vector2 size, string face, string? second,
        string? alpha, KeyTone tone, bool latched)
    {
        var dl = ImGui.GetWindowDrawList();
        var legendH = size.Y * 0.30f;
        var faceTL = tl + new Vector2(0f, legendH);
        var faceSize = new Vector2(size.X, size.Y - legendH);

        if (second is not null)
        {
            var sz = ImGui.CalcTextSize(second);
            var scale = MathF.Min(1f, (size.X * 0.56f) / MathF.Max(1f, sz.X));
            DrawSmall(dl, second, tl + new Vector2(ctx.Px(1f), legendH - sz.Y * scale - ctx.Px(1f)), scale,
                ImGui.ColorConvertFloat4ToU32(SecondLegend));
        }
        if (alpha is not null)
        {
            var sz = ImGui.CalcTextSize(alpha);
            var scale = MathF.Min(1f, (size.X * 0.34f) / MathF.Max(1f, sz.X));
            DrawSmall(dl, alpha, tl + new Vector2(size.X - sz.X * scale - ctx.Px(1f),
                legendH - sz.Y * scale - ctx.Px(1f)), scale, ImGui.ColorConvertFloat4ToU32(AlphaLegend));
        }

        ImGui.SetCursorScreenPos(faceTL);
        var clicked = ImGui.InvisibleButton(id, faceSize);
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        var (baseFill, faceInk) = tone switch
        {
            // White with dark digits is the single most recognisable thing about the body, so the number
            // keys carry it and everything else stays out of their way.
            KeyTone.Digit => (KeyWhite, KeyInk),
            KeyTone.Operator => (KeyGrey, KeyText),
            KeyTone.Nav => (KeyDark, KeyText),
            KeyTone.Second => (TiCyan, KeyText),
            KeyTone.Alpha => (TiGreen, KeyText),
            KeyTone.Accent => (KeyGrey, TiCyan),
            _ => (KeyDark, KeyText),
        };
        if (latched)
        {
            baseFill = Lighten(baseFill, 0.22f);
        }
        var fill = held ? Lighten(baseFill, 0.14f) : hovered ? Lighten(baseFill, 0.07f) : baseFill;

        var rounding = MathF.Max(ctx.Px(2f), faceSize.Y * 0.16f);
        var br = faceTL + faceSize;
        dl.AddRectFilled(faceTL, br, ImGui.ColorConvertFloat4ToU32(fill), rounding);
        dl.AddLine(faceTL + new Vector2(rounding, ctx.Px(1f)), faceTL + new Vector2(faceSize.X - rounding, ctx.Px(1f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, held ? 0.05f : 0.14f)), ctx.Px(1f));
        dl.AddRect(faceTL, br, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)), rounding,
            ImDrawFlags.None, ctx.Px(1f));
        if (latched)
        {
            dl.AddRect(faceTL, br, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f)), rounding,
                ImDrawFlags.None, ctx.Px(1.6f));
        }

        var textColor = faceInk;
        var faceSz = ImGui.CalcTextSize(face);
        var faceScale = MathF.Min(1f, (faceSize.X - ctx.Px(4f)) / MathF.Max(1f, faceSz.X));
        DrawSmall(dl, face, faceTL + (faceSize - faceSz * faceScale) * 0.5f, faceScale,
            ImGui.ColorConvertFloat4ToU32(textColor));
        return clicked;
    }

    /// <summary>A rounded pill button used throughout the panels and the graph tool strip.</summary>
    public static bool Pill(OsAppContext ctx, string id, Vector2 tl, Vector2 size, string label, bool active,
        Vector4 accent)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        var rounding = size.Y * 0.5f;
        var fill = active ? accent with { W = 0.30f } : new Vector4(1f, 1f, 1f, hovered ? 0.12f : 0.06f);
        dl.AddRectFilled(tl, tl + size, ImGui.ColorConvertFloat4ToU32(fill), rounding);
        dl.AddRect(tl, tl + size,
            ImGui.ColorConvertFloat4ToU32(accent with { W = active ? 0.85f : hovered ? 0.45f : 0.20f }),
            rounding, ImDrawFlags.None, ctx.Px(1f));
        var sz = ImGui.CalcTextSize(label);
        var scale = MathF.Min(1f, (size.X - ctx.Px(8f)) / MathF.Max(1f, sz.X));
        DrawSmall(dl, label, tl + (size - sz * scale) * 0.5f, scale,
            ImGui.ColorConvertFloat4ToU32(active ? accent : UiColors.Body));
        return clicked;
    }

    /// <summary>A small square icon button, for the trace arrows and panel close.</summary>
    public static bool IconButton(OsAppContext ctx, string id, Vector2 tl, float size, FontAwesomeIcon icon,
        Vector4 tint)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        dl.AddRectFilled(tl, tl + new Vector2(size, size),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.07f)), size * 0.25f);
        IconDraw.AddCentered(dl, icon, size * 0.46f, tl + new Vector2(size * 0.5f, size * 0.5f),
            ImGui.ColorConvertFloat4ToU32(tint with { W = hovered ? 1f : 0.82f }));
        return clicked;
    }

    /// <summary>Labelled numeric field used by the WINDOW and TBLSET panels.</summary>
    public static bool NumberField(string id, string label, ref string buffer, float width)
    {
        ImGui.TextColored(UiColors.Hint, label);
        ImGui.SetNextItemWidth(width);
        return ImGui.InputText(id, ref buffer, 24);
    }

    /// <summary>An in-page overlay: dims only the app content, centres a panel and closes on a click outside.
    /// The body runs first so its own controls win the click; the scrim is submitted last.</summary>
    public static void Overlay(OsAppContext ctx, string id, float panelW, float panelH,
        Action<Vector2, Vector2> body, Action dismiss)
    {
        var origin = ImGui.GetWindowPos();
        var avail = ImGui.GetWindowSize();
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child($"##{id}Layer", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.72f)));

        var size = new Vector2(MathF.Min(panelW, avail.X - ctx.Px(20f)),
            MathF.Min(panelH, avail.Y - ctx.Px(24f)));
        var tl = origin + (avail - size) * 0.5f;
        var rounding = ctx.Px(14f);
        dl.AddRectFilled(tl, tl + size, ImGui.ColorConvertFloat4ToU32(PanelFill), rounding);
        dl.AddRect(tl, tl + size, ImGui.ColorConvertFloat4ToU32(Teal with { W = 0.35f }), rounding,
            ImDrawFlags.None, ctx.Px(1.2f));

        body(tl, size);

        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton($"##{id}Scrim", avail))
        {
            var mouse = ImGui.GetIO().MousePos;
            var inside = mouse.X >= tl.X && mouse.X <= tl.X + size.X && mouse.Y >= tl.Y && mouse.Y <= tl.Y + size.Y;
            if (!inside)
            {
                dismiss();
            }
        }
    }

    /// <summary>Panel heading plus its close button, sharing the top strip of an overlay panel.</summary>
    public static bool PanelHeader(OsAppContext ctx, string id, Vector2 tl, Vector2 size, string title)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = ctx.Px(14f);
        dl.AddText(tl + new Vector2(pad, pad), ImGui.ColorConvertFloat4ToU32(Teal), title);
        var btn = ctx.Px(24f);
        return IconButton(ctx, $"##{id}Close", tl + new Vector2(size.X - pad - btn, pad - ctx.Px(3f)), btn,
            FontAwesomeIcon.Times, UiColors.Body);
    }

    /// <summary>Text drawn below the body size, for axis labels and key faces that must not wrap.</summary>
    public static void SmallText(ImDrawListPtr dl, string text, Vector2 pos, float scale, uint color) =>
        DrawSmall(dl, text, pos, scale, color);

    /// <summary>ImGui has no per-draw text scale, so a shrunk label is drawn through a scoped font scale.</summary>
    private static void DrawSmall(ImDrawListPtr dl, string text, Vector2 pos, float scale, uint color)
    {
        if (scale >= 0.999f)
        {
            dl.AddText(pos, color, text);
            return;
        }
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, color, text);
    }
}
