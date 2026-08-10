using System;
using System.Collections.Generic;

namespace AetherLove.Shared.Store;

/// <summary>The rules a purchased theme's geometry has to satisfy before it can ship. They exist because
/// the failure modes are silent rather than loud: a control rect that lands under the content is simply
/// never clickable, a collapsed status strip strands the notification shade behind an unhittable band, and
/// a home button that overflows its bezel disappears under the content child. Every measurement is a design
/// pixel against the fixed 835-tall canvas.</summary>
public static class StoreThemeGeometryRules
{
    /// <summary>The canvas height every theme is measured against; only the width varies.</summary>
    public const float CanvasHeight = 835f;

    public const float MinWindowWidth = 300f;
    public const float MinContentWidth = 240f;
    public const float MinContentHeight = 500f;
    public const float MinStatusBandHeight = 16f;
    public const float MinButtonSide = 18f;
    public const float MinButtonClearance = 2f;
    public const float MaxWindowWidth = 1200f;

    /// <summary>The neon home key's glow bleeds this far past its own edge, so the bottom band has to hold
    /// the glyph plus this much clearance above it.</summary>
    private const float HomeGlowBleed = 6f;

    /// <summary>Everything wrong with a geometry block, worst first; empty means it is safe to ship.</summary>
    public static IReadOnlyList<string> Validate(StoreThemeGeometryDto g)
    {
        var errors = new List<string>();
        var w = g.WindowWidth;

        if (w < MinWindowWidth || w > MaxWindowWidth)
        {
            errors.Add($"Window width {w:0.##} is outside {MinWindowWidth:0}..{MaxWindowWidth:0}.");
            return errors;
        }
        if (g.BezelTop < 0f || g.BezelBottom < 0f || g.BezelLeft < 0f || g.BezelRight < 0f)
        {
            errors.Add("Bezel insets cannot be negative.");
            return errors;
        }

        var contentW = w - g.BezelLeft - g.BezelRight;
        var contentH = CanvasHeight - g.BezelTop - g.BezelBottom;
        if (contentW < MinContentWidth)
        {
            errors.Add($"Content is only {contentW:0.##} wide; {MinContentWidth:0} is the minimum.");
        }
        if (contentH < MinContentHeight)
        {
            errors.Add($"Content is only {contentH:0.##} tall; {MinContentHeight:0} is the minimum.");
        }

        if (g.StatusBarTop < 0f || g.StatusBarTop >= g.BezelTop)
        {
            errors.Add("The status strip has to start above the content: 0 <= StatusBarTop < BezelTop.");
        }
        else if (g.BezelTop - g.StatusBarTop < MinStatusBandHeight)
        {
            errors.Add($"The status strip is only {g.BezelTop - g.StatusBarTop:0.##} tall; " +
                $"the clock and battery clip below {MinStatusBandHeight:0}.");
        }
        if (g.StatusBarTimeAlign < 0f || g.StatusBarTimeAlign > 1f)
        {
            errors.Add("StatusBarTimeAlign has to be between 0 and 1.");
        }
        if (g.StatusBarRightInset < 0f || (contentW > 0f && g.StatusBarRightInset > contentW * 0.5f))
        {
            errors.Add("StatusBarRightInset has to be between 0 and half the content width.");
        }

        ValidateButton(errors, "Close", g.CloseButtonX, g.CloseButtonY, g.CloseButtonWidth, g.CloseButtonHeight, g);
        ValidateButton(errors, "Minimize", g.MinimizeButtonX, g.MinimizeButtonY,
            g.MinimizeButtonWidth, g.MinimizeButtonHeight, g);
        if (Overlaps(
            g.CloseButtonX, g.CloseButtonY, g.CloseButtonWidth, g.CloseButtonHeight,
            g.MinimizeButtonX, g.MinimizeButtonY, g.MinimizeButtonWidth, g.MinimizeButtonHeight))
        {
            errors.Add("The close and minimize rects overlap each other.");
        }

        ValidateHome(errors, g);
        return errors;
    }

    /// <summary>Warnings a human should look at but that do not block a save.</summary>
    public static IReadOnlyList<string> Warnings(StoreThemeGeometryDto g)
    {
        var warnings = new List<string>();
        // The status bar walks its icons leftward from here, and auto-clamps against the MINIMIZE rect only.
        var clusterRight = g.WindowWidth - g.BezelRight - g.StatusBarRightInset - 10f + 2.5f;
        if (g.MinimizeButtonY < g.BezelTop)
        {
            warnings.Add("The minimize rect sits inside the top bezel, so the status bar clamps its icon " +
                "cluster to the left of it and StatusBarRightInset partly stops mattering.");
            clusterRight = Math.Min(clusterRight, g.MinimizeButtonX - 10f);
        }
        if (g.CloseButtonY < g.BezelTop && g.CloseButtonX < clusterRight)
        {
            warnings.Add("The close rect reaches into the status bar's icon cluster, so the battery will " +
                "draw over the close key. Move it right or raise StatusBarRightInset.");
        }
        if (g.MinimizeButtonY < g.BezelTop && g.MinimizeButtonX < clusterRight)
        {
            warnings.Add("The minimize rect reaches into the status bar's icon cluster.");
        }
        if (Math.Abs(g.BezelLeft - g.BezelRight) > 3f)
        {
            warnings.Add("The left and right insets differ by more than 3px; check the frame really is that " +
                "asymmetric.");
        }
        if (Math.Abs(g.HomeCenterXOffset) > g.WindowWidth * 0.1f)
        {
            warnings.Add("The home button is nudged more than a tenth of the window off centre.");
        }
        return warnings;
    }

    private static void ValidateButton(
        List<string> errors, string name, float x, float y, float bw, float bh, StoreThemeGeometryDto g)
    {
        if (bw < MinButtonSide || bh < MinButtonSide)
        {
            errors.Add($"The {name} rect is smaller than {MinButtonSide:0}x{MinButtonSide:0}; the drawn key " +
                "would be unhittable.");
            return;
        }
        if (x < 0f || y < 0f || x + bw > g.WindowWidth || y + bh > CanvasHeight)
        {
            errors.Add($"The {name} rect falls outside the window.");
            return;
        }
        // A parent-window hit area under the content child is never hovered, so it is dead rather than wrong.
        var contentL = g.BezelLeft - MinButtonClearance;
        var contentT = g.BezelTop - MinButtonClearance;
        var contentR = g.WindowWidth - g.BezelRight + MinButtonClearance;
        var contentB = CanvasHeight - g.BezelBottom + MinButtonClearance;
        if (x < contentR && x + bw > contentL && y < contentB && y + bh > contentT)
        {
            errors.Add($"The {name} rect overlaps the content area, where it can never be clicked. Keep it in " +
                $"the bezel with at least {MinButtonClearance:0}px of clearance.");
        }
    }

    private static void ValidateHome(List<string> errors, StoreThemeGeometryDto g)
    {
        if (g.HomePulseSeconds < 0.5f)
        {
            errors.Add("HomePulseSeconds under 0.5 is silently floored; set 0.5 or more.");
        }
        if (g.HomeHitWidth <= 0f || g.HomeHitHeight <= 0f)
        {
            errors.Add("The home button needs a positive hit size.");
            return;
        }
        if (g.BezelBottom < g.HomeHitHeight - 2f * g.HomeCenterYOffset)
        {
            errors.Add("The home hit area reaches past the bottom bezel into the content, where the content " +
                "child swallows its clicks. Deepen BezelBottom or shrink the hit height.");
        }

        var drawH = g.HomeShape == StoreThemeHomeShape.GoldenPill ? g.HomeHeight : g.HomeWidth;
        if (drawH <= 0f)
        {
            errors.Add("The home button needs a positive drawn size.");
            return;
        }
        var half = g.BezelBottom * 0.5f;
        if (half + g.HomeCenterYOffset < drawH * 0.5f + 2f)
        {
            errors.Add("The home button hangs below the frame's bottom edge.");
        }
        if (half - g.HomeCenterYOffset < drawH * 0.5f + HomeGlowBleed)
        {
            errors.Add("The home button's glow reaches up into the content area.");
        }
    }

    private static bool Overlaps(
        float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh) =>
        ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;
}
