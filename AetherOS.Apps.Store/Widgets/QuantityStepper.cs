using System;
using System.Globalization;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Store;

/// <summary>Minus / count / plus in one pill. Clamped segments dim and lose the hand cursor; the count
/// does a small pop when it changes, and tapping it turns it into a typed field. Returns true on the frame
/// the value changed.</summary>
internal static class QuantityStepper
{
    private static double _popStamp = -10.0;
    private static string? _editingId;
    private static string _editText = string.Empty;
    private static bool _focusPending;

    public static bool Draw(string id, Vector2 tl, int min, int max, bool reduceMotion, ref int value)
    {
        var dl = ImGui.GetWindowDrawList();
        var segW = Px(30f);
        var height = Px(28f);
        var size = new Vector2(segW * 3f, height);
        dl.AddRectFilled(tl, tl + size, OsDrawShared.White(0.08f), height * 0.5f);

        var changed = false;
        if (Segment(dl, $"{id}minus", tl, new Vector2(segW, height), FontAwesomeIcon.Minus, value > min))
        {
            value--;
            changed = true;
        }
        if (Segment(dl, $"{id}plus", tl + new Vector2(segW * 2f, 0f), new Vector2(segW, height),
            FontAwesomeIcon.Plus, value < max))
        {
            value++;
            changed = true;
        }
        if (changed)
        {
            _popStamp = ImGui.GetTime();
            _editingId = null;
        }

        var countTl = tl + new Vector2(segW, 0f);
        if (_editingId == id)
        {
            changed |= DrawEditor(id, countTl, new Vector2(segW, height), min, max, ref value);
        }
        else
        {
            DrawCount(dl, countTl, new Vector2(segW, height), reduceMotion, value);
            ImGui.SetCursorScreenPos(countTl);
            if (ImGui.InvisibleButton($"##{id}count", new Vector2(segW, height)))
            {
                _editingId = id;
                _editText = value.ToString(CultureInfo.InvariantCulture);
                _focusPending = true;
            }
            if (ImGui.IsItemHovered())
            {
                HandOnHover();
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + height));
        return changed;
    }

    private static bool Segment(
        ImDrawListPtr dl, string id, Vector2 tl, Vector2 size, FontAwesomeIcon icon, bool enabled)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##{id}", size) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            dl.AddRectFilled(tl, tl + size, OsDrawShared.White(0.08f), size.Y * 0.5f);
        }
        IconDraw.AddCentered(dl, icon, Px(10f), tl + size * 0.5f,
            ImGui.GetColorU32(enabled ? UiColors.Body : UiColors.Hint with { W = 0.35f }));
        return clicked;
    }

    private static void DrawCount(ImDrawListPtr dl, Vector2 tl, Vector2 size, bool reduceMotion, int value)
    {
        var pop = (float)(ImGui.GetTime() - _popStamp);
        var scale = !reduceMotion && pop < 0.15f ? 1f + 0.3f * MathF.Sin(pop / 0.15f * MathF.PI) : 1f;
        var text = value.ToString(CultureInfo.InvariantCulture);
        var textSz = ImGui.CalcTextSize(text);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
            tl + new Vector2(size.X * 0.5f - textSz.X * 0.5f * scale, (size.Y - textSz.Y * scale) * 0.5f),
            ImGui.GetColorU32(UiColors.Body), text);
    }

    /// <summary>The typed field the count becomes on a tap. Commits on Enter or on losing focus, clamped to
    /// the stepper's own range; an empty or unreadable entry falls back to the value already held.</summary>
    private static bool DrawEditor(string id, Vector2 tl, Vector2 size, int min, int max, ref int value)
    {
        ImGui.SetCursorScreenPos(tl);
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, size.Y * 0.5f))
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(Px(2f), Px(4f))))
        using (ImRaii.PushColor(ImGuiCol.FrameBg, OsDrawShared.White(0.14f)))
        {
            // Focus only lands on the frame AFTER the request, so the field cannot be judged idle yet.
            var justOpened = _focusPending;
            if (_focusPending)
            {
                ImGui.SetKeyboardFocusHere();
                _focusPending = false;
            }
            ImGui.SetNextItemWidth(size.X);
            var submitted = ImGui.InputText($"##{id}edit", ref _editText, 4,
                ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.AutoSelectAll
                | ImGuiInputTextFlags.EnterReturnsTrue);
            if (!submitted && (justOpened || ImGui.IsItemActive()))
            {
                return false;
            }
        }

        _editingId = null;
        if (!int.TryParse(_editText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed))
        {
            return false;
        }
        var clamped = Math.Clamp(typed, min, max);
        if (clamped == value)
        {
            return false;
        }
        value = clamped;
        _popStamp = ImGui.GetTime();
        return true;
    }

    /// <summary>The stepper's footprint, for layout.</summary>
    public static Vector2 Size() => new(Px(90f), Px(28f));
}
