using System.Numerics;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Doom;

/// <summary>The cabinet's own button chrome. The other arcade apps borrow <see cref="RetroLcd"/>'s green
/// handheld palette, which would look absurd wrapped around Doom, so this is the same shapes in the
/// cabinet's grimy red.
///
/// Every control here is hit-tested BY HAND rather than submitted as an ImGui item, because this app also
/// holds the keyboard. The capture that stops keystrokes reaching the game is a focused InputText, and a
/// focused InputText owns ImGui's active id: the very thing a button needs to keep between press and release,
/// and a drag needs to stay latched. Sharing it means buttons never fire and drags let go on the next frame,
/// whichever order the two are submitted in. Reading the mouse directly sidesteps the contest.</summary>
internal static class DoomChrome
{
    private static readonly Vector4 Ink = new(0.93f, 0.90f, 0.86f, 1f);
    private static readonly Vector4 Accent = new(0.62f, 0.12f, 0.09f, 1f);
    private static readonly Vector4 AccentHot = new(0.78f, 0.17f, 0.12f, 1f);
    private static readonly Vector4 Edge = new(0.36f, 0.11f, 0.09f, 1f);
    private static readonly Vector4 Face = new(0.12f, 0.09f, 0.09f, 1f);

    private static Vector2 pressOrigin;
    private static bool pressLive;

    /// <summary>Records where the current press began. Held controls test the ORIGIN rather than the current
    /// cursor, so dragging off a button releases it and dragging onto one does not press it.</summary>
    public static void BeginFrame()
    {
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            pressOrigin = ImGui.GetIO().MousePos;
            pressLive = true;
        }
        else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            pressLive = false;
        }
    }

    private static bool Contains(Vector2 point, Vector2 topLeft, Vector2 size) =>
        point.X >= topLeft.X && point.X < topLeft.X + size.X
        && point.Y >= topLeft.Y && point.Y < topLeft.Y + size.Y;

    public static bool Hovered(Vector2 topLeft, Vector2 size) =>
        Contains(ImGui.GetIO().MousePos, topLeft, size);

    /// <summary>True on the frame a click lands inside the rect.</summary>
    private static bool Clicked(Vector2 topLeft, Vector2 size) =>
        ImGui.IsMouseClicked(ImGuiMouseButton.Left) && Hovered(topLeft, size);

    /// <summary>True while the button is down AND the press started inside the rect.</summary>
    public static bool Held(Vector2 topLeft, Vector2 size) =>
        pressLive && ImGui.IsMouseDown(ImGuiMouseButton.Left) && Contains(pressOrigin, topLeft, size);

    public static bool Button(string label, Vector2 topLeft, Vector2 size, float rounding, bool filled)
    {
        var hovered = Hovered(topLeft, size);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var dl = ImGui.GetWindowDrawList();
        var fill = filled ? (hovered ? AccentHot : Accent) : Face;
        dl.AddRectFilled(topLeft, topLeft + size, ImGui.GetColorU32(fill), rounding);
        dl.AddRect(topLeft, topLeft + size, ImGui.GetColorU32(hovered ? AccentHot : Edge), rounding,
            ImDrawFlags.None, 2f);

        var textSize = ImGui.CalcTextSize(label);
        dl.AddText(topLeft + ((size - textSize) * 0.5f), ImGui.GetColorU32(Ink), label);
        return Clicked(topLeft, size);
    }

    /// <summary>A small square icon key, for the back, pause and mute affordances.</summary>
    public static bool Key(FontAwesomeIcon icon, Vector2 topLeft, float size)
    {
        var box = new Vector2(size, size);
        var hovered = Hovered(topLeft, box);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(topLeft, topLeft + box, ImGui.GetColorU32(hovered ? Accent : Face), size * 0.24f);
        DrawIcon(dl, icon, topLeft, size, ImGui.GetColorU32(Ink));
        return Clicked(topLeft, box);
    }

    /// <summary>Centres a FontAwesome glyph inside a key face at an EXPLICIT pixel size. The icon font is
    /// built at its own large size, so taking <c>ImGui.GetFontSize()</c> from it draws a glyph many times
    /// bigger than the key it is supposed to sit in.</summary>
    private static void DrawIcon(ImDrawListPtr dl, FontAwesomeIcon icon, Vector2 topLeft, float size, uint color)
    {
        var glyph = icon.ToIconString();
        var target = size * 0.44f;
        using (UiFonts.Icon?.Push())
        {
            var native = ImGui.GetFontSize();
            var measured = ImGui.CalcTextSize(glyph);
            var scaled = native > 0f ? measured * (target / native) : measured;
            dl.AddText(ImGui.GetFont(), target,
                topLeft + ((new Vector2(size, size) - scaled) * 0.5f), color, glyph);
        }
    }

    /// <summary>A key that reports being HELD rather than clicked, which is what a movement pad needs: a
    /// click edge would step the player one tic and stop.</summary>
    public static bool HeldKey(string label, Vector2 topLeft, float size)
    {
        var box = new Vector2(size, size);
        var hovered = Hovered(topLeft, box);
        var held = Held(topLeft, box);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(topLeft, topLeft + box,
            ImGui.GetColorU32(held ? AccentHot : (hovered ? Accent : Face)), size * 0.2f);
        dl.AddRect(topLeft, topLeft + box, ImGui.GetColorU32(Edge), size * 0.2f, ImDrawFlags.None, 1.5f);

        var textSize = ImGui.CalcTextSize(label);
        dl.AddText(topLeft + ((box - textSize) * 0.5f), ImGui.GetColorU32(Ink), label);
        return held;
    }
}
