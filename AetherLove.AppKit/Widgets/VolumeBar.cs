using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Widgets;

/// <summary>
/// The volume bar that drops out of a speaker chip. Every app that plays something keeps a mute
/// chip in its corner, and a chip is a yes-or-no answer to a question that has a hundred: this is
/// the rest of the answer, and it is one call so no surface has to invent its own.
///
/// <para><b>It draws the bar, never the chip.</b> The chips are already there and they do not look
/// alike: a translucent disc in the pet's header, a rounded plate on the race stage, a key on
/// Doom's cabinet. Taking the chip over would have meant one of them changing for a reason that has
/// nothing to do with volume, so the caller keeps drawing and clicking its own chip and hands this
/// the rectangle it drew.</para>
///
/// <para><b>Drawn, not submitted</b>, and hit-tested by hand off the raw mouse, which is the same
/// choice every corner chip in the app already made. Two reasons and both are load-bearing: a bar
/// that hangs over a screen's own content must not steal that content's clicks when it is closed,
/// and a live minigame holds ImGui's active id, which makes an ordinary item structurally unable to
/// report a hover at all.</para>
///
/// <para><b>Mute and level are separate values</b>, because they answer different questions and the
/// apps already store the first one. Muting keeps the level it was at, so unmuting returns to the
/// volume the owner chose rather than to full; dragging to zero mutes, and dragging up from zero
/// unmutes. That also means an app that has only ever stored a mute flag keeps working: its level
/// simply starts at one.</para>
/// </summary>
public static class VolumeBar
{
    /// <summary>How wide the bar is, as a multiple of the chip it hangs from, and how tall its
    /// grab area is. The grab is deliberately taller than the track it draws: a four-pixel line is
    /// the right thing to look at and the wrong thing to ask somebody to catch.</summary>
    private const float WidthInChips = 3.6f;

    private const float GrabHeight = 20f;

    private const float TrackHeight = 4f;

    private const float KnobRadius = 5.5f;

    /// <summary>The gap between the chip and the bar. Zero on purpose: the two rectangles have to
    /// touch, or the pointer leaves the chip on its way to the bar and the bar it was travelling to
    /// closes underneath it.</summary>
    private const float Gap = 0f;

    /// <summary>Which bar the pointer is dragging, if any. A drag survives the pointer leaving the
    /// bar, so a hand that slides off the end while pulling keeps the grip it had.</summary>
    private static string dragging = string.Empty;

    /// <summary>Bars that were open on the last frame, so one can stay open for the moment the
    /// pointer spends crossing its own chip's edge.</summary>
    private static readonly Dictionary<string, double> OpenUntil = [];

    /// <summary>Negative until a bar is first touched, and never <c>int.MinValue</c>: the frame count
    /// minus that overflows, which reported a hold on every frame of the session.</summary>
    private static int holdFrame = -1;

    /// <summary>True while the pointer is on an open bar or dragging one. The phone window reads it
    /// and refuses to move for that frame: nothing here is an ImGui item, so without it a pull on
    /// the knob is also a drag on the window's background and the whole phone follows the hand.
    /// It answers for the frame just drawn, which is the frame the window is about to decide on.
    /// </summary>
    public static bool HoldsWindowDrag => holdFrame >= 0 && ImGui.GetFrameCount() - holdFrame <= 1;

    /// <summary>Draws the bar under <paramref name="chipTl"/> while the pointer is on the chip or on
    /// the bar itself, and returns true on any frame the owner's values changed.</summary>
    /// <param name="id">Stable per chip: two bars on one screen must not share a drag.</param>
    /// <param name="muted">The chip's own mute, toggled by dragging to and away from zero.</param>
    /// <param name="volume01">The level, 0 to 1.</param>
    /// <param name="alignRight">Hang the bar from the chip's right edge rather than its left. True
    /// for a chip in the top-right corner, which is where all of them are, so a bar never runs off
    /// the side of the phone.</param>
    public static bool Draw(
        string id,
        ImDrawListPtr dl,
        Vector2 chipTl,
        Vector2 chipSize,
        ref bool muted,
        ref float volume01,
        uint fill,
        uint track,
        uint knob,
        float scale = 1f,
        bool alignRight = true)
    {
        var width = chipSize.X * WidthInChips;
        var grabH = GrabHeight * scale;
        var barTl = new Vector2(
            alignRight ? chipTl.X + chipSize.X - width : chipTl.X,
            chipTl.Y + chipSize.Y + (Gap * scale));
        var barBr = barTl + new Vector2(width, grabH);

        var mouse = ImGui.GetIO().MousePos;
        var onChip = In(mouse, chipTl, chipTl + chipSize);
        var onBar = In(mouse, barTl, barBr);
        var held = dragging == id;

        // A held drag ends with the button, wherever the pointer has got to by then.
        if (held && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            dragging = string.Empty;
            held = false;
        }

        var now = ImGui.GetTime();
        if (onChip || onBar || held)
        {
            OpenUntil[id] = now + 0.35;
        }

        if (!(OpenUntil.TryGetValue(id, out var until) && now <= until))
        {
            return false;
        }

        if (onBar && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            dragging = id;
            held = true;
        }

        var changed = false;
        var level = muted ? 0f : Math.Clamp(volume01, 0f, 1f);
        if (held)
        {
            var inner = width - (KnobRadius * 2f * scale);
            var at = Math.Clamp((mouse.X - barTl.X - (KnobRadius * scale)) / Math.Max(1f, inner), 0f, 1f);

            // Snapped at both ends, because silence and full are the two values anybody actually
            // aims for and a bar three chips wide cannot be aimed at more finely than this.
            level = at < 0.03f ? 0f : at > 0.97f ? 1f : at;
            var nowMuted = level <= 0f;
            changed = nowMuted != muted || (!nowMuted && Math.Abs(level - volume01) > 0.001f);
            muted = nowMuted;
            if (!nowMuted)
            {
                volume01 = level;
            }
        }

        if (onChip || onBar || held)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (onBar || held)
        {
            holdFrame = ImGui.GetFrameCount();
        }

        var mid = barTl.Y + (grabH * 0.5f);
        var trackH = TrackHeight * scale;
        var knobR = KnobRadius * scale;
        var left = barTl.X + knobR;
        var right = barBr.X - knobR;
        dl.AddRectFilled(
            new Vector2(barTl.X, mid - (trackH * 0.5f)),
            new Vector2(barBr.X, mid + (trackH * 0.5f)),
            track,
            trackH * 0.5f);

        var knobX = left + ((right - left) * level);
        if (level > 0f)
        {
            dl.AddRectFilled(
                new Vector2(barTl.X, mid - (trackH * 0.5f)),
                new Vector2(knobX, mid + (trackH * 0.5f)),
                fill,
                trackH * 0.5f);
        }

        dl.AddCircleFilled(new Vector2(knobX, mid), knobR, knob, 16);
        return changed;
    }

    /// <summary>Forgets a bar's open state, for a surface that is going away while the pointer is
    /// still on it.</summary>
    public static void Close(string id)
    {
        OpenUntil.Remove(id);
        if (dragging == id)
        {
            dragging = string.Empty;
        }
    }

    private static bool In(Vector2 p, Vector2 tl, Vector2 br) =>
        p.X >= tl.X && p.X <= br.X && p.Y >= tl.Y && p.Y <= br.Y;
}
