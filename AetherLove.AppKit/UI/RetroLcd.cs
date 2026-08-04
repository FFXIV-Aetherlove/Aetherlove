using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>The shared handheld-LCD look every arcade app is built on: a fixed
/// monochrome palette, a 5x7 block alphabet drawn as pixels rather than glyphs so titles look identical
/// at any font or UI scale, and the chunky button/key chrome. Public because game apps reference AppKit
/// from outside its InternalsVisibleTo set.</summary>
public static class RetroLcd
{
    /// <summary>The lit panel and its "off" pixels. Deliberately fixed rather than themed: the joke is
    /// that these are 1998 handhelds running inside a 2026 phone.</summary>
    public static readonly Vector4 Panel = new(0.561f, 0.659f, 0.239f, 1f);
    public static readonly Vector4 Pixel = new(0.129f, 0.169f, 0.059f, 1f);
    public static readonly Vector4 PanelEdge = new(0.451f, 0.541f, 0.176f, 1f);

    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    private const int GlyphGap = 1;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
        ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
        ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
        ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['I'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
        ['J'] = ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
        ['K'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
        ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
        ['M'] = ["#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#", "#...#"],
        ['N'] = ["#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#"],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['Q'] = [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
        ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
        ['S'] = [".###.", "#...#", "#....", ".###.", "....#", "#...#", ".###."],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
        ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['V'] = ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
        ['W'] = ["#...#", "#...#", "#...#", "#...#", "#.#.#", "##.##", "#...#"],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['Y'] = ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
        ['Z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
        ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", "#####"],
        ['2'] = [".###.", "#...#", "....#", "..##.", ".#...", "#....", "#####"],
        ['3'] = ["#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###."],
        ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
        ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ['6'] = ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
        ['!'] = ["..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.."],
        ['.'] = [".....", ".....", ".....", ".....", ".....", ".....", "..#.."],
        [' '] = [".....", ".....", ".....", ".....", ".....", ".....", "....."],
    };

    private static int _blurFrame = -1;
    private static int _blurStreak;
    private static bool _blurred;

    /// <summary>True once the player's attention has demonstrably left the phone, which a live game treats as
    /// "pause". Two signals, because one is not enough: ImGui window focus catches a click on another plugin,
    /// but a click on the GAME never reaches ImGui at all and leaves our window nominally focused, so a click
    /// landing anywhere outside the phone counts too. The focus path is debounced a frame (submitting the
    /// keyboard-capture field can flicker it); a click away is acted on at once. Safe to call repeatedly.</summary>
    public static bool WindowBlurred()
    {
        var frame = ImGui.GetFrameCount();
        if (frame == _blurFrame)
        {
            return _blurred;
        }
        _blurFrame = frame;

        _blurStreak = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) ? 0 : _blurStreak + 1;
        var clickedAway = !ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)
            && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right));
        _blurred = clickedAway || _blurStreak >= 2;
        return _blurred;
    }

    /// <summary>Pixel columns the word occupies, for centring by the caller.</summary>
    public static int WordColumns(string word) =>
        word.Length == 0 ? 0 : (word.Length * GlyphWidth) + ((word.Length - 1) * GlyphGap);

    /// <summary>Draws a word as LCD pixels. <paramref name="litRows"/> reveals it row by row for boot
    /// animations; pass <see cref="GlyphHeight"/> for the whole thing.</summary>
    public static void DrawWord(ImDrawListPtr dl, string word, Vector2 origin, float pixel, uint color, int litRows)
    {
        var cursorX = 0;
        foreach (var raw in word)
        {
            var ch = char.ToUpperInvariant(raw);
            if (!Glyphs.TryGetValue(ch, out var glyph))
            {
                cursorX += GlyphWidth + GlyphGap;
                continue;
            }
            for (var row = 0; row < GlyphHeight && row < litRows; row++)
            {
                for (var col = 0; col < GlyphWidth; col++)
                {
                    if (glyph[row][col] != '#')
                    {
                        continue;
                    }
                    var tl = origin + new Vector2((cursorX + col) * pixel, row * pixel);
                    dl.AddRectFilled(tl, tl + new Vector2(pixel - 1f, pixel - 1f), color);
                }
            }
            cursorX += GlyphWidth + GlyphGap;
        }
    }

    /// <summary>Centres a pixel word horizontally in the given width and draws it.</summary>
    public static void DrawWordCentered(ImDrawListPtr dl, string word, Vector2 areaTL, float areaWidth,
        float pixel, uint color, int litRows)
    {
        var width = WordColumns(word) * pixel;
        DrawWord(dl, word, areaTL + new Vector2((areaWidth - width) * 0.5f, 0f), pixel, color, litRows);
    }

    /// <summary>A chunky LCD button. Returns true on click; <paramref name="filled"/> marks the primary.</summary>
    public static bool Button(string id, string label, Vector2 tl, Vector2 size, float rounding, bool filled)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        var held = ImGui.IsItemActive();

        var dl = ImGui.GetWindowDrawList();
        var solid = filled || held;
        if (solid)
        {
            dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(Pixel with { W = held ? 1f : 0.9f }), rounding);
        }
        else if (hovered)
        {
            dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(Pixel with { W = 0.12f }), rounding);
        }
        dl.AddRect(tl, tl + size, ImGui.GetColorU32(Pixel), rounding, ImDrawFlags.None, 2f);

        var textSize = ImGui.CalcTextSize(label);
        dl.AddText(tl + ((size - textSize) * 0.5f), ImGui.GetColorU32(solid ? Panel : Pixel), label);
        return clicked;
    }

    /// <summary>The rounded-square key face, shared by the icon and lettered variants.</summary>
    private static (bool Pressed, bool Held) KeyChrome(string id, Vector2 tl, float size)
    {
        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton(id, new Vector2(size, size));
        var pressed = ImGui.IsItemClicked();
        var held = ImGui.IsItemActive();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var dl = ImGui.GetWindowDrawList();
        var br = tl + new Vector2(size, size);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(Pixel with { W = held ? 0.85f : 0.14f }), size * 0.22f);
        dl.AddRect(tl, br, ImGui.GetColorU32(Pixel with { W = 0.65f }), size * 0.22f, ImDrawFlags.None, 2f);
        return (pressed, held);
    }

    /// <summary>A control-pad key: rounded square, icon, pressed state. Returns true on press (not
    /// release), so a quick tap registers on the very next game step.</summary>
    public static bool Key(string id, Dalamud.Interface.FontAwesomeIcon icon, Vector2 tl, float size)
    {
        var (pressed, held) = KeyChrome(id, tl, size);
        DrawIcon(ImGui.GetWindowDrawList(), icon, size * 0.4f, tl + new Vector2(size * 0.5f, size * 0.5f),
            ImGui.GetColorU32(held ? Panel : Pixel));
        return pressed;
    }

    /// <summary>A control-pad key wearing its keyboard letter in the LCD alphabet, so the binding is
    /// legible from the pad itself rather than from a legend nobody reads.</summary>
    public static bool KeyLabel(string id, string label, Vector2 tl, float size)
    {
        var (pressed, held) = KeyChrome(id, tl, size);
        var pixel = MathF.Max(1f, size * 0.42f / GlyphHeight);
        DrawWordCentered(ImGui.GetWindowDrawList(), label,
            tl + new Vector2(0f, (size - (GlyphHeight * pixel)) * 0.5f), size, pixel,
            ImGui.GetColorU32(held ? Panel : Pixel), GlyphHeight);
        return pressed;
    }

    /// <summary>Room a <see cref="PauseKey"/> eats out of the right edge of a HUD, sized to sit inside the HUD
    /// band rather than spilling over the board. Callers subtract it from their own right-aligned readouts
    /// whether or not the key is currently drawn, so the HUD never shifts.</summary>
    public static float PauseKeyWidth(float hudHeight) =>
        MathF.Min(Px(30f), MathF.Max(Px(15f), hudHeight - Px(6f)));

    /// <summary>The pause key every game wears in the top-right of its HUD, centred in the band. Returns true
    /// on press.</summary>
    public static bool PauseKey(Vector2 winPos, Vector2 winSize, float padX, float hudHeight)
    {
        var size = PauseKeyWidth(hudHeight);
        return Key("##lcdPause", Dalamud.Interface.FontAwesomeIcon.Pause,
            new Vector2(winPos.X + winSize.X - padX - size, winPos.Y + ((hudHeight - size) * 0.5f)), size);
    }

    /// <summary>True while a control-pad key is held down, for continuous movement (paddles).</summary>
    public static bool KeyHeld(string id, Dalamud.Interface.FontAwesomeIcon icon, Vector2 tl, float size)
    {
        Key(id, icon, tl, size);
        return ImGui.IsItemActive();
    }

    /// <summary>True while a lettered control-pad key is held down.</summary>
    public static bool KeyLabelHeld(string id, string label, Vector2 tl, float size)
    {
        KeyLabel(id, label, tl, size);
        return ImGui.IsItemActive();
    }

    /// <summary>Public passthrough to the AppKit icon helper, which game apps can't see directly.</summary>
    public static void DrawIcon(ImDrawListPtr dl, Dalamud.Interface.FontAwesomeIcon icon, float pixels,
        Vector2 center, uint color) => IconDraw.AddCentered(dl, icon, pixels, center, color);

    /// <summary>Faint dot grid, the way a cheap LCD never quite hides its cells.</summary>
    public static void DotGrid(ImDrawListPtr dl, Vector2 tl, int columns, int rows, float cell)
    {
        var color = ImGui.GetColorU32(PanelEdge);
        for (var x = 0; x < columns; x++)
        {
            for (var y = 0; y < rows; y++)
            {
                var dot = tl + new Vector2((x * cell) + (cell * 0.5f) - 0.5f, (y * cell) + (cell * 0.5f) - 0.5f);
                dl.AddRectFilled(dot, dot + new Vector2(1f, 1f), color);
            }
        }
    }

    /// <summary>One filled board cell with the standard inset, used for every block in every game.</summary>
    public static void Cell(ImDrawListPtr dl, Vector2 boardTL, int x, int y, float cell, uint color)
    {
        var gap = MathF.Max(1f, cell * 0.08f);
        var tl = boardTL + new Vector2(x * cell, y * cell);
        dl.AddRectFilled(tl + new Vector2(gap, gap), tl + new Vector2(cell - gap, cell - gap), color);
    }
}
