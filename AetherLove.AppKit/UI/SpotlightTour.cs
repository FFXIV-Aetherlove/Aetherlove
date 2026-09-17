using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.UI;

/// <summary>Where a tour step's explainer panel sits. <see cref="Auto"/> puts it on the far side of the
/// spotlight, or centred when the step has none.</summary>
public enum TourAnchor
{
    Auto,
    Bottom,
    Top,
    Center,
}

/// <summary>One step of a <see cref="SpotlightTour{TContext}"/>. Every callback receives the consumer's
/// context, so a script can be a static array and still resolve rects, run demos and toggle state on the
/// thing being toured. <paramref name="Available"/> decides whether the step survives into a given run.</summary>
public sealed record TourStep<TContext>(
    FontAwesomeIcon Icon,
    string TitleKey,
    string BodyKey,
    Func<TContext, (Vector2 TL, Vector2 BR)?>? Spotlight = null,
    TourAnchor Panel = TourAnchor.Auto,
    float Dim = 0.62f,
    Action<TContext>? Enter = null,
    Action<TContext>? Exit = null,
    Action<TContext, ImDrawListPtr>? Demo = null,
    Func<TContext, bool>? Available = null);

/// <summary>The generic spotlight-tour engine: a dimmed content area with a cutout, a pulsing ring around
/// the current target, an optional scripted demonstration clipped to the content, and an explainer panel
/// with progress, Back, Next or Finish, and Skip. A full-window scrim is submitted LAST every frame, so the
/// surface underneath is inert for the duration and the tour never asks the user to do anything.
///
/// <para>The engine owns the plan and the step clock only. The consumer owns the script, its targets, its
/// demos and whatever it must clean up when the tour ends, which it does from <see cref="Finished"/>.</para></summary>
public sealed class SpotlightTour<TContext>
{
    private readonly List<TourStep<TContext>> _plan = [];

    private int _step;
    private int _enteredStep = -1;
    private double _stepEnteredAt;

    private Vector2 _winPos;
    private Vector2 _winSize;
    private Vector2 _contentTL;
    private Vector2 _contentBR;

    public bool Active { get; private set; }

    public int Step => _step;

    public int Count => _plan.Count;

    public bool IsLast => _step == _plan.Count - 1;

    /// <summary>Seconds the current step has been up, which is every demo's clock.</summary>
    public float DemoTime => (float)(ImGui.GetTime() - _stepEnteredAt);

    /// <summary>Post-processes the localized title and body before they are drawn, for a consumer that
    /// substitutes something into its copy. Null draws the localized text as is.</summary>
    public Func<string, string>? Format { get; set; }

    /// <summary>Raised once per run when the tour ends, whether by Finish or Skip, after the current
    /// step's Exit has run.</summary>
    public event Action? Finished;

    /// <summary>Filters the script by each step's Available and starts from the first survivor. An empty
    /// plan leaves the tour inactive.</summary>
    public void Start(IEnumerable<TourStep<TContext>> script, TContext ctx)
    {
        if (Active)
        {
            return;
        }
        _plan.Clear();
        _plan.AddRange(script.Where(s => s.Available?.Invoke(ctx) ?? true));
        _step = 0;
        _enteredStep = -1;
        _stepEnteredAt = ImGui.GetTime();
        Active = _plan.Count > 0;
    }

    /// <summary>Draws the current step into <paramref name="dl"/>. The dim, the demo clip and the panel are
    /// bounded by the content rect; the ring and the scrim cover the whole window, so a consumer can
    /// spotlight chrome outside its content.</summary>
    public void Draw(TContext ctx, ImDrawListPtr dl, Vector2 winPos, Vector2 winSize,
        Vector2 contentTL, Vector2 contentBR, string scrimId)
    {
        if (!Active)
        {
            return;
        }

        _winPos = winPos;
        _winSize = winSize;
        _contentTL = contentTL;
        _contentBR = contentBR;

        _step = Math.Clamp(_step, 0, _plan.Count - 1);
        if (_step != _enteredStep)
        {
            if (_enteredStep >= 0)
            {
                _plan[_enteredStep].Exit?.Invoke(ctx);
            }
            _plan[_step].Enter?.Invoke(ctx);
            _enteredStep = _step;
            _stepEnteredAt = ImGui.GetTime();
        }

        var step = _plan[_step];
        var size = _contentBR - _contentTL;
        var spot = step.Spotlight?.Invoke(ctx);

        if (step.Dim > 0f)
        {
            DrawDim(dl, spot, step.Dim);
        }
        if (spot is { } s)
        {
            // Clamped inside the window so edge-hugging spotlights (StatusBarTop 0 themes) keep a full ring.
            var ringTL = Vector2.Max(s.TL - Px(3f, 3f), _winPos + Px(2f, 2f));
            var ringBR = Vector2.Min(s.BR + Px(3f, 3f), _winPos + _winSize - Px(2f, 2f));
            var pulse = 0.55f + (0.45f * MathF.Sin((float)ImGui.GetTime() * 3.4f));
            dl.AddRect(ringTL, ringBR,
                ImGui.ColorConvertFloat4ToU32(TourDraw.Emphasis with { W = pulse }),
                Px(10f), ImDrawFlags.RoundCornersAll, Px(2.4f));
        }

        // Demos draw inside the content only: a ghost sliding over the bezel reads as a glitch.
        if (step.Demo is { } demo)
        {
            dl.PushClipRect(_contentTL, _contentBR, true);
            demo(ctx, dl);
            dl.PopClipRect();
        }

        var belowSpot = step.Panel switch
        {
            TourAnchor.Bottom => true,
            TourAnchor.Top => false,
            TourAnchor.Center => (bool?)null,
            _ => spot is { } sp ? (sp.TL.Y + sp.BR.Y) * 0.5f < _contentTL.Y + (size.Y * 0.5f) : null,
        };
        DrawPanel(ctx, dl, step, belowSpot, scrimId);

        // Scrim last: with overlapping items the first-submitted one wins clicks, so the panel buttons stay
        // clickable. Covers the whole window, which is what makes the surface underneath inert.
        ImGui.SetCursorScreenPos(_winPos);
        ImGui.InvisibleButton(scrimId, _winSize);
    }

    /// <summary>Ends the run: the current step's Exit runs, the tour deactivates and <see cref="Finished"/>
    /// is raised. Safe to call while inactive.</summary>
    public void Finish(TContext ctx)
    {
        if (!Active)
        {
            return;
        }
        if (_enteredStep >= 0)
        {
            _plan[_enteredStep].Exit?.Invoke(ctx);
            _enteredStep = -1;
        }
        Active = false;
        Finished?.Invoke();
    }

    private string Text(string key)
    {
        var text = Loc.T(key);
        return Format?.Invoke(text) ?? text;
    }

    /// <summary>Dims the content around the spotlight cutout (clamped to the content rect; a spotlight fully
    /// outside it leaves the content uniformly dimmed).</summary>
    private void DrawDim(ImDrawListPtr dl, (Vector2 TL, Vector2 BR)? spot, float alpha)
    {
        var dim = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, alpha));
        var cut = spot is { } s
            ? (TL: Vector2.Max(s.TL, _contentTL), BR: Vector2.Min(s.BR, _contentBR))
            : default((Vector2 TL, Vector2 BR)?);
        if (cut is { } c && c.BR.X > c.TL.X && c.BR.Y > c.TL.Y)
        {
            dl.AddRectFilled(_contentTL, new Vector2(_contentBR.X, c.TL.Y), dim);
            dl.AddRectFilled(new Vector2(_contentTL.X, c.BR.Y), _contentBR, dim);
            dl.AddRectFilled(new Vector2(_contentTL.X, c.TL.Y), new Vector2(c.TL.X, c.BR.Y), dim);
            dl.AddRectFilled(new Vector2(c.BR.X, c.TL.Y), new Vector2(_contentBR.X, c.BR.Y), dim);
        }
        else
        {
            dl.AddRectFilled(_contentTL, _contentBR, dim);
        }
    }

    private const float MinTitleScale = 0.72f;

    private void DrawPanel(TContext ctx, ImDrawListPtr dl, TourStep<TContext> step, bool? belowSpot, string idPrefix)
    {
        var accent = TourDraw.Emphasis;
        var size = _contentBR - _contentTL;
        var padIn = Px(16f);
        var panelW = size.X - Px(40f);
        var innerW = panelW - (padIn * 2f);
        var lineH = ImGui.GetTextLineHeight();
        var body = Text(step.BodyKey);
        var bodyH = ImGui.CalcTextSize(body, false, innerW).Y;
        var btnH = Px(30f);
        var headerH = Px(34f);
        var panelH = padIn + headerH + Px(10f) + bodyH + Px(16f) + btnH + padIn;

        var panelX = _contentTL.X + ((size.X - panelW) * 0.5f);
        var panelY = belowSpot switch
        {
            true => _contentTL.Y + size.Y - panelH - Px(24f),
            false => _contentTL.Y + Px(56f),
            null => _contentTL.Y + ((size.Y - panelH) * 0.5f),
        };
        var panelTL = new Vector2(panelX, panelY);
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL + Px(0f, 4f), panelBR + Px(0f, 4f), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.35f)), Px(16f));
        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.11f, 0.10f, 0.13f, 0.98f)), Px(16f));
        dl.AddRect(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(accent with { W = 0.55f }), Px(16f), ImDrawFlags.RoundCornersAll, Px(1.2f));

        var iconR = Px(14f);
        var iconC = panelTL + new Vector2(padIn + iconR, padIn + iconR);
        dl.AddCircleFilled(iconC, iconR, ImGui.ColorConvertFloat4ToU32(accent), 28);
        IconDraw.AddCentered(dl, step.Icon, Px(13f), iconC, 0xFFFFFFFFu);
        using (UiFonts.H3?.Push())
        {
            // The title stays one line: a long one shrinks to the room left of the icon rather than clipping.
            var title = Text(step.TitleKey);
            var titleX = iconC.X + iconR + Px(10f);
            var titleRoom = panelBR.X - padIn - titleX;
            var titleSize = ImGui.GetFontSize();
            var titleW = ImGui.CalcTextSize(title).X;
            if (titleW > titleRoom && titleW > 0f)
            {
                titleSize *= MathF.Max(MinTitleScale, titleRoom / titleW);
            }
            dl.PushClipRect(panelTL, panelBR, true);
            dl.AddText(ImGui.GetFont(), titleSize, new Vector2(titleX, iconC.Y - (titleSize * 0.5f)),
                0xFFFFFFFFu, title);
            dl.PopClipRect();
        }

        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), panelTL + new Vector2(padIn, padIn + headerH + Px(10f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f)), body, innerW);

        var footerY = panelBR.Y - padIn - btnH;
        var progress = $"{_step + 1} / {_plan.Count}";
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            new Vector2(panelTL.X + padIn, footerY + ((btnH - (lineH * 0.85f)) * 0.5f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.45f)), progress);

        var last = IsLast;
        var nextW = Px(84f);
        var backW = Px(64f);
        var skipW = Px(56f);
        var nextTL = new Vector2(panelBR.X - padIn - nextW, footerY);
        if (PanelButton(idPrefix + "Next", Loc.T(last ? "os.tour_finish" : "os.tour_next"), nextTL, new Vector2(nextW, btnH), accent))
        {
            if (last)
            {
                Finish(ctx);
            }
            else
            {
                _step++;
            }
        }
        if (_step > 0
            && PanelButton(idPrefix + "Back", Loc.T("os.tour_back"), nextTL - new Vector2(backW + Px(8f), 0f),
                new Vector2(backW, btnH), new Vector4(1f, 1f, 1f, 0.10f)))
        {
            _step--;
        }
        if (!last
            && PanelButton(idPrefix + "Skip", Loc.T("os.tour_skip"),
                new Vector2(panelTL.X + padIn + ImGui.CalcTextSize(progress).X + Px(14f), footerY),
                new Vector2(skipW, btnH), new Vector4(1f, 1f, 1f, 0.06f)))
        {
            Finish(ctx);
        }
    }

    private static bool PanelButton(string id, string label, Vector2 tl, Vector2 size, Vector4 fill)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        var dl = ImGui.GetWindowDrawList();
        var col = hovered
            ? new Vector4(fill.X + ((1f - fill.X) * 0.12f), fill.Y + ((1f - fill.Y) * 0.12f),
                fill.Z + ((1f - fill.Z) * 0.12f), MathF.Min(1f, fill.W + 0.08f))
            : fill;
        dl.AddRectFilled(tl, tl + size, ImGui.ColorConvertFloat4ToU32(col), Px(9f));
        var sz = ImGui.CalcTextSize(label);
        dl.PushClipRect(tl, tl + size, true);
        dl.AddText(tl + ((size - sz) * 0.5f), 0xF2FFFFFFu, label);
        dl.PopClipRect();
        return clicked;
    }
}

/// <summary>Drawing and timing helpers shared by tour demonstrations: the scripted cursor, the trail a
/// carried thing leaves, and the one carry timeline every drag demo runs on.</summary>
public static class TourDraw
{
    /// <summary>The tour's emphasis color: per-theme override first, so gold-accent themes stay legible.</summary>
    public static Vector4 Emphasis => ThemeService.Current.TourAccent ?? ThemeService.Current.Accent;

    /// <summary>The pointer the demonstrations move around. Drawn rather than borrowed from the OS cursor,
    /// which sits wherever the player's hand actually is and would fight the script.</summary>
    public static void DrawCursor(ImDrawListPtr dl, Vector2 at, bool pressed, bool right = false)
    {
        var r = Px(pressed ? 13f : 10f);
        if (pressed)
        {
            dl.AddCircle(at, r + Px(6f), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.35f)), 24, Px(1.6f));
        }
        dl.AddCircleFilled(at, r, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, pressed ? 0.85f : 0.6f)), 24);
        dl.AddCircle(at, r, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.45f)), 24, Px(1.2f));
        if (right)
        {
            // A right-click reads as a right-click only if the demonstration says which button it was.
            dl.AddCircleFilled(at + new Vector2(r * 0.45f, -r * 0.45f), Px(4f),
                ImGui.ColorConvertFloat4ToU32(Emphasis with { W = 0.95f }), 12);
        }
    }

    /// <summary>The path a carried thing is taking, drawn as it is walked rather than all at once.</summary>
    public static void DrawTrail(ImDrawListPtr dl, Vector2 from, Vector2 to, float travel)
    {
        if (travel <= 0f)
        {
            return;
        }
        const int Dots = 9;
        var walked = Vector2.Lerp(from, to, travel);
        for (var i = 1; i <= Dots; i++)
        {
            var f = i / (float)(Dots + 1);
            var at = Vector2.Lerp(from, walked, f);
            dl.AddCircleFilled(at, Px(2.2f), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.28f)), 10);
        }
    }

    /// <summary>The one timeline every carry demonstration runs on: reach, press, travel, drop, rest.</summary>
    public static (bool Held, float Lift, float Travel) CarryProgress(float phase)
    {
        const float Reach = 0.85f;
        const float Press = 1.2f;
        const float Travel = 3.1f;
        const float Drop = 3.45f;

        if (phase < Reach)
        {
            return (false, 0f, 0f);
        }
        if (phase < Press)
        {
            return (true, Ease((phase - Reach) / (Press - Reach)), 0f);
        }
        if (phase < Travel)
        {
            return (true, 1f, Ease((phase - Press) / (Travel - Press)));
        }
        if (phase < Drop)
        {
            return (true, 1f - Ease((phase - Travel) / (Drop - Travel)), 1f);
        }
        return (false, 0f, 1f);
    }

    /// <summary>Which row the cursor is over while it walks down a menu, so the highlight follows it
    /// instead of jumping straight to the answer.</summary>
    public static int RowUnderCursor(float phase, int count, int highlight)
    {
        var walk = Math.Clamp((phase - 1.6f) / 1.1f, 0f, 1f);
        return Math.Clamp((int)MathF.Round(walk * highlight), 0, count - 1);
    }

    public static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    public static Vector2 Centre((Vector2 TL, Vector2 BR) r) => (r.TL + r.BR) * 0.5f;

    public static float Size((Vector2 TL, Vector2 BR) r) => MathF.Min(r.BR.X - r.TL.X, r.BR.Y - r.TL.Y);
}
