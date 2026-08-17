using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetherLove.Screens;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Os;

/// <summary>The guided OS tour: a spotlight overlay that walks somebody through the phone shell, showing
/// each gesture rather than only describing it. Auto-started once on the first Home landing
/// (<see cref="AetherOS.Sdk.OsConfig.TourSeen"/>) and replayable from OS settings via
/// <see cref="AetherOS.Sdk.IOsShell.StartTour"/>. Drawn OUTSIDE the content clip so it can highlight bezel
/// elements (the home button, the status strip).
///
/// <para><b>The tour never asks the user to do anything.</b> Its own scrim is submitted over the whole
/// window every frame, so the phone underneath is inert for the duration: nothing can be dragged, removed
/// or renamed by accident while a step is up. Every gesture is therefore SHOWN, as a scripted loop drawn
/// over the real home screen by demo steps: a ghost cursor, a lifted copy of a real tile, and
/// mock menus whose rows read from the SAME localization keys the real menus use, so the two cannot drift
/// apart in wording. See the tour note in the vault before changing any of this.</para>
///
/// <para><b>Nothing here names an app.</b> Every target is resolved at tour time from what the home screen
/// actually drew (<see cref="HomeScreen.DrawnTiles"/>, <see cref="HomeScreen.GridSlots"/>), because the
/// grid belongs to the player and a phone being set up for the first time may hold almost nothing. A step
/// whose target cannot be resolved is dropped from the plan at <see cref="Start"/> rather than pointing a
/// ring at empty space, which is also why the progress counter is built per run.</para></summary>
public sealed class OsTour
{
    private const string TourTag = "os:tour";

    /// <summary>How long one demonstration takes before it starts over. Long enough to read the panel under
    /// it, short enough that a second viewing is not a wait.</summary>
    private const float DemoLoopSeconds = 5.2f;

    /// <summary>The pretend unread count on the badge step. The copy says out loud that it is not real.</summary>
    private const int DemoBadgeCount = 67;

    /// <summary>How long the tour waits for a home screen to draw before planning without one.</summary>
    private const double PlanWaitSeconds = 2.0;

    private enum Anchor
    {
        Auto,
        Bottom,
        Top,
        Center,
    }

    private sealed record Step(
        FontAwesomeIcon Icon,
        string TitleKey,
        string BodyKey,
        Func<OsTour, (Vector2 TL, Vector2 BR)?>? Spotlight = null,
        Anchor Panel = Anchor.Auto,
        float Dim = 0.62f,
        Action<OsTour>? Enter = null,
        Action<OsTour>? Exit = null,
        Action<OsTour, ImDrawListPtr>? Demo = null,
        Func<OsTour, bool>? Available = null);

    /// <summary>The full script. Order is the running order; `Available` decides whether a step survives
    /// into a given run. Adding one is a step here plus its two localization keys in all six tables.</summary>
    private static readonly Step[] Script =
    [
        new(FontAwesomeIcon.MobileAlt, "os.tour_welcome_title", "os.tour_welcome_body", Panel: Anchor.Center),

        new(FontAwesomeIcon.Home, "os.tour_homebtn_title", "os.tour_homebtn_body",
            t => t.HomeButtonRect(), Panel: Anchor.Bottom),

        new(FontAwesomeIcon.Clock, "os.tour_statusbar_title", "os.tour_statusbar_body",
            t => t.StatusStripRect()),

        new(FontAwesomeIcon.AngleDoubleDown, "os.tour_shade_title", "os.tour_shade_body",
            t => t.ShadePanelRect(), Panel: Anchor.Bottom, Dim: 0f,
            Enter: t =>
            {
                t._shell.PostNotification("messenger", Loc.T("os.tour_fake_notif_title"),
                    Loc.T("os.tour_fake_notif_body"), null, TourTag);
                t._shade.Open();
            },
            Exit: t =>
            {
                t._shade.Close();
                t._shell.DismissByTag(TourTag);
            }),

        new(FontAwesomeIcon.Magic, "os.tour_widgets_title", "os.tour_widgets_body",
            Panel: Anchor.Bottom, Dim: 0f,
            Enter: t => t._home.ShowPage(-1),
            Exit: t => t._home.ShowPage(0)),

        new(FontAwesomeIcon.Th, "os.tour_pages_title", "os.tour_pages_body",
            t => t.PageDotsRect(), Panel: Anchor.Top,
            Enter: t => t._home.ShowPage(0)),

        new(FontAwesomeIcon.Bell, "os.tour_badges_title", "os.tour_badges_body",
            t => t.TileRect(t._badgeAppId), Panel: Anchor.Bottom,
            Enter: t => t.ApplyDemoBadge(),
            Exit: t => t.ClearDemoBadge(),
            Available: t => t._badgeAppId != null),

        new(FontAwesomeIcon.ArrowsAlt, "os.tour_arrange_title", "os.tour_arrange_body",
            Panel: Anchor.Bottom, Dim: 0.45f,
            Enter: t => t._home.ShowPage(0),
            Demo: (t, dl) => t.DemoArrange(dl),
            Available: t => t._moveFrom != null && t._moveTo != null),

        new(FontAwesomeIcon.GripHorizontal, "os.tour_dock_title", "os.tour_dock_body",
            t => t.DockRect(), Panel: Anchor.Top, Dim: 0.45f,
            Demo: (t, dl) => t.DemoDock(dl),
            Available: t => t._moveFrom != null),

        new(FontAwesomeIcon.MousePointer, "os.tour_tilemenu_title", "os.tour_tilemenu_body",
            Panel: Anchor.Bottom, Dim: 0.45f,
            Enter: t => t._home.ShowPage(0),
            Demo: (t, dl) => t.DemoTileMenu(dl),
            Available: t => t._menuTile != null),

        new(FontAwesomeIcon.FolderPlus, "os.tour_homemenu_title", "os.tour_homemenu_body",
            Panel: Anchor.Bottom, Dim: 0.45f,
            Demo: (t, dl) => t.DemoHomeMenu(dl),
            Available: t => t.EmptySlot != null),

        new(FontAwesomeIcon.FolderOpen, "os.tour_folders_title", "os.tour_folders_body",
            Panel: Anchor.Bottom, Dim: 0.45f,
            Demo: (t, dl) => t.DemoFolder(dl),
            Available: t => t._moveFrom != null && t._moveTo != null),

        new(FontAwesomeIcon.Plus, "os.tour_addapps_title", "os.tour_addapps_body",
            t => t.AddAppsPanelRect(), Panel: Anchor.Bottom, Dim: 0f,
            Enter: t => t._home.SetAddAppsOpen(true),
            Exit: t => t._home.SetAddAppsOpen(false)),

        new(FontAwesomeIcon.Gift, "os.tour_newapps_title", "os.tour_newapps_body", Panel: Anchor.Center),

        new(FontAwesomeIcon.Share, "os.tour_share_title", "os.tour_share_body", Panel: Anchor.Center),

        new(FontAwesomeIcon.Palette, "os.tour_look_title", "os.tour_look_body",
            t => t.TileRect(t._settingsTile), Panel: Anchor.Bottom,
            Available: t => t._settingsTile != null),

        new(FontAwesomeIcon.Plane, "os.tour_offline_title", "os.tour_offline_body", Panel: Anchor.Center),

        new(FontAwesomeIcon.CheckCircle, "os.tour_done_title", "os.tour_done_body", Panel: Anchor.Center),
    ];

    private readonly OsShell _shell;
    private readonly NotificationShade _shade;
    private readonly HomeScreen _home;
    private readonly List<Step> _plan = [];

    private int _step;
    private int _enteredStep = -1;
    private double _stepEnteredAt;
    private double _startedAt;

    // Resolved once per run from whatever is actually on the grid, so no step ever names an app.
    private string? _badgeAppId;
    private string? _settingsTile;
    private string? _menuTile;
    private string? _moveFrom;
    private string? _moveTo;
    private int _emptySlotIndex = -1;
    private bool _badgeApplied;

    private Vector2 _winPos;
    private Vector2 _winSize;
    private Vector2 _contentTL;
    private Vector2 _contentBR;

    public OsTour(OsShell shell, NotificationShade shade, HomeScreen home)
    {
        _shell = shell;
        _shade = shade;
        _home = home;
    }

    public bool Active { get; private set; }

    /// <summary>The tour's emphasis color: per-theme override first, so gold-accent themes stay legible.</summary>
    private static Vector4 Emphasis => ThemeService.Current.TourAccent ?? ThemeService.Current.Accent;

    private (Vector2 TL, Vector2 BR)? EmptySlot =>
        _emptySlotIndex >= 0 && _emptySlotIndex < _home.GridSlots.Count
            ? (_home.GridSlots[_emptySlotIndex].TL, _home.GridSlots[_emptySlotIndex].BR)
            : null;

    /// <summary>Seconds the current step has been up, which is every demo's clock.</summary>
    private float DemoTime => (float)(ImGui.GetTime() - _stepEnteredAt);

    public void Start()
    {
        if (Active)
        {
            return;
        }
        _step = 0;
        _enteredStep = -1;
        _stepEnteredAt = ImGui.GetTime();
        _startedAt = ImGui.GetTime();
        _plan.Clear();
        _badgeApplied = false;
        Active = true;
    }

    /// <summary>Builds this run's plan, once the home screen has actually drawn a frame to read targets out
    /// of. A replay from Settings navigates home and the grid only exists a frame later, so planning at
    /// <see cref="Start"/> would resolve every target against a stale or empty cache and quietly drop the
    /// gesture steps. The wait gives up after <see cref="PlanWaitSeconds"/> so a home screen that never
    /// arrives leaves a short tour rather than an invisible one that never ends.</summary>
    private bool TryPlan()
    {
        if (_plan.Count > 0)
        {
            return true;
        }
        if (_home.DrawnTiles.Count == 0 && ImGui.GetTime() - _startedAt < PlanWaitSeconds)
        {
            return false;
        }
        ResolveTargets();
        _plan.AddRange(Script.Where(s => s.Available?.Invoke(this) ?? true));
        _stepEnteredAt = ImGui.GetTime();
        if (_plan.Count == 0)
        {
            // Unreachable while any step lacks an `Available`, and deliberately handled anyway: an empty
            // plan would otherwise hold Active forever, blocking the new-app offer and re-arming the tour
            // every session because TourSeen is only stamped by finishing.
            Finish();
            return false;
        }
        return true;
    }

    /// <summary>Picks this run's examples out of what the home screen last drew. Everything here can come
    /// back null, and every step that needs one says so through its own `Available`, which is what lets the
    /// tour run on a phone holding two apps and no folders.</summary>
    private void ResolveTargets()
    {
        var dock = UiHost.Configuration.Os.DockIds;
        var grid = _home.DrawnTiles.Where(id => !dock.Contains(id)).ToList();

        _settingsTile = _home.DrawnTiles.Contains("settings") ? "settings" : null;
        _badgeAppId = grid.FirstOrDefault(id => _shell.Find(id) is { UsesAccount: true })
            ?? grid.FirstOrDefault();
        _menuTile = grid.FirstOrDefault(id => _shell.IsNewApp(id)) ?? grid.FirstOrDefault();
        _moveFrom = grid.FirstOrDefault();
        _moveTo = grid.Skip(1).FirstOrDefault();

        _emptySlotIndex = -1;
        for (var i = 0; i < _home.GridSlots.Count; i++)
        {
            if (!_home.GridSlots[i].Occupied)
            {
                _emptySlotIndex = i;
                break;
            }
        }
    }

    public void Draw(Vector2 winPos, Vector2 winSize)
    {
        if (!Active || !TryPlan())
        {
            return;
        }

        var t = ThemeService.Current;
        _winPos = winPos;
        _winSize = winSize;
        _contentTL = winPos + Px(t.BezelLeft, t.BezelTop);
        _contentBR = winPos + new Vector2(winSize.X - Px(t.BezelRight), winSize.Y - Px(t.BezelBottom));

        _step = Math.Clamp(_step, 0, _plan.Count - 1);
        if (_step != _enteredStep)
        {
            if (_enteredStep >= 0)
            {
                _plan[_enteredStep].Exit?.Invoke(this);
            }
            _plan[_step].Enter?.Invoke(this);
            _enteredStep = _step;
            _stepEnteredAt = ImGui.GetTime();
        }

        var step = _plan[_step];
        var dl = ImGui.GetWindowDrawList();
        var size = _contentBR - _contentTL;
        var spot = step.Spotlight?.Invoke(this);

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
                ImGui.ColorConvertFloat4ToU32(Emphasis with { W = pulse }),
                Px(10f), ImDrawFlags.RoundCornersAll, Px(2.4f));
        }

        // Demos draw inside the content only: a ghost tile sliding over the bezel reads as a glitch.
        if (step.Demo is { } demo)
        {
            dl.PushClipRect(_contentTL, _contentBR, true);
            demo(this, dl);
            dl.PopClipRect();
        }

        var belowSpot = step.Panel switch
        {
            Anchor.Bottom => true,
            Anchor.Top => false,
            Anchor.Center => (bool?)null,
            _ => spot is { } sp ? (sp.TL.Y + sp.BR.Y) * 0.5f < _contentTL.Y + (size.Y * 0.5f) : null,
        };
        DrawPanel(dl, step, belowSpot);

        // Scrim last: with overlapping items the first-submitted one wins clicks, so the panel buttons stay
        // clickable. Covers the whole window, which is what makes the phone underneath inert.
        ImGui.SetCursorScreenPos(_winPos);
        ImGui.InvisibleButton("##osTourScrim", _winSize);
    }

    /// <summary>Dims the content around the spotlight cutout (clamped to the content rect; a spotlight fully
    /// in the bezel, like the home button, leaves the content uniformly dimmed).</summary>
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

    // ------------------------------------------------------------------ spotlight rects

    private (Vector2 TL, Vector2 BR)? HomeButtonRect()
    {
        var t = ThemeService.Current;
        var b = t.HomeButton;
        var center = new Vector2(
            _winPos.X + (_winSize.X * 0.5f) + Px(b.CenterXOffset),
            _winPos.Y + _winSize.Y - (Px(t.BezelBottom) * 0.5f) + Px(b.CenterYOffset));
        var half = (Px(b.HitSize.X, b.HitSize.Y) * 0.5f) + Px(4f, 4f);
        return (center - half, center + half);
    }

    private (Vector2 TL, Vector2 BR)? StatusStripRect()
    {
        var t = ThemeService.Current;
        return ((Vector2 TL, Vector2 BR)?)(
            new Vector2(_contentTL.X, _winPos.Y + Px(t.StatusBarTop)),
            new Vector2(_contentBR.X, _winPos.Y + Px(t.BezelTop)));
    }

    private (Vector2 TL, Vector2 BR)? ShadePanelRect() =>
        (_contentTL, new Vector2(_contentBR.X, _contentTL.Y + ((_contentBR.Y - _contentTL.Y) * 0.74f)));

    private (Vector2 TL, Vector2 BR)? PageDotsRect()
    {
        var y = _contentBR.Y - Px(76f) - Px(42f);
        return ((Vector2 TL, Vector2 BR)?)(
            new Vector2(_contentTL.X + ((_contentBR.X - _contentTL.X) * 0.28f), y),
            new Vector2(_contentBR.X - ((_contentBR.X - _contentTL.X) * 0.28f), y + Px(28f)));
    }

    /// <summary>The dock's real rect when the home screen has drawn one, so a themed or resized phone gets a
    /// ring around the bar that is actually there.</summary>
    private (Vector2 TL, Vector2 BR)? DockRect() =>
        _home.DockRect is { } d
            ? (d.TL - Px(4f, 4f), d.BR + Px(4f, 4f))
            : (new Vector2(_contentTL.X + Px(10f), _contentBR.Y - Px(96f)),
               new Vector2(_contentBR.X - Px(10f), _contentBR.Y - Px(8f)));

    /// <summary>The add-apps overlay panel, mirrored from <c>HomeScreen.DrawAddAppsOverlay</c> geometry.</summary>
    private (Vector2 TL, Vector2 BR)? AddAppsPanelRect()
    {
        var size = _contentBR - _contentTL;
        var panelW = size.X - Px(36f);
        var tl = _contentTL + new Vector2((size.X - panelW) * 0.5f, size.Y * 0.14f);
        return (tl, tl + new Vector2(panelW, size.Y * 0.64f));
    }

    private (Vector2 TL, Vector2 BR)? TileRect(string? id) =>
        id != null && _home.TryGetTileRect(id, out var tl, out var br)
            ? (tl - Px(4f, 4f), br + Px(4f, 20f))
            : null;

    // ------------------------------------------------------------------ demonstrations

    /// <summary>Lifting an icon out of its cell and putting it down in another one. Nothing on the phone
    /// moves: what travels is a copy, and the cell it left keeps a dashed outline so the gesture reads as a
    /// move rather than a duplication.</summary>
    private void DemoArrange(ImDrawListPtr dl)
    {
        if (TileRect(_moveFrom) is not { } from || TileRect(_moveTo) is not { } to)
        {
            return;
        }
        var phase = DemoTime % DemoLoopSeconds;
        var carry = CarryProgress(phase);
        var start = Centre(from);
        var end = Centre(to);
        var at = Vector2.Lerp(start, end, carry.Travel);

        DrawEmptyCell(dl, from, carry.Held ? 1f : 0f);
        DrawTrail(dl, start, end, carry.Travel);
        if (carry.Held)
        {
            DrawGhostTile(dl, _moveFrom!, at, Size(from) * (1f + (0.08f * carry.Lift)), 0.92f);
        }
        DrawCursor(dl, at, carry.Held);
    }

    /// <summary>The same gesture, ending on the dock, which is the one row that stays put on every page.</summary>
    private void DemoDock(ImDrawListPtr dl)
    {
        if (TileRect(_moveFrom) is not { } from || _home.DockRect is not { } dock)
        {
            return;
        }
        var phase = DemoTime % DemoLoopSeconds;
        var carry = CarryProgress(phase);
        var start = Centre(from);
        var end = new Vector2(dock.BR.X - ((dock.BR.X - dock.TL.X) * 0.12f), (dock.TL.Y + dock.BR.Y) * 0.5f);
        var at = Vector2.Lerp(start, end, carry.Travel);

        DrawEmptyCell(dl, from, carry.Held ? 1f : 0f);
        DrawTrail(dl, start, end, carry.Travel);
        if (carry.Held)
        {
            DrawGhostTile(dl, _moveFrom!, at, Size(from) * (1f + (0.08f * carry.Lift)), 0.92f);
        }
        DrawCursor(dl, at, carry.Held);
    }

    /// <summary>Two icons becoming a folder: carry one onto the other, and the target grows the little
    /// four-square preview a real folder tile wears.</summary>
    private void DemoFolder(ImDrawListPtr dl)
    {
        if (TileRect(_moveFrom) is not { } from || TileRect(_moveTo) is not { } to)
        {
            return;
        }
        var phase = DemoTime % DemoLoopSeconds;
        var carry = CarryProgress(phase);
        var start = Centre(from);
        var end = Centre(to);
        var at = Vector2.Lerp(start, end, carry.Travel);

        DrawEmptyCell(dl, from, carry.Held ? 1f : 0f);
        if (carry.Travel > 0.75f)
        {
            var merge = Math.Clamp((carry.Travel - 0.75f) / 0.25f, 0f, 1f);
            DrawGhostFolder(dl, Centre(to), Size(to), merge);
        }
        else
        {
            DrawTrail(dl, start, end, carry.Travel);
        }
        if (carry.Held && carry.Travel < 0.92f)
        {
            DrawGhostTile(dl, _moveFrom!, at, Size(from) * (1f + (0.08f * carry.Lift)), 0.92f);
        }
        DrawCursor(dl, at, carry.Held);
    }

    /// <summary>Right-clicking an icon. The rows are the REAL menu's keys, so the demonstration cannot end
    /// up promising something the menu no longer says.</summary>
    private void DemoTileMenu(ImDrawListPtr dl)
    {
        if (TileRect(_menuTile) is not { } tile)
        {
            return;
        }
        var rows = new List<(FontAwesomeIcon Icon, string Label)>();
        if (_menuTile != null && _shell.IsNewApp(_menuTile))
        {
            rows.Add((FontAwesomeIcon.Check, Loc.T("os.tile_menu_mark_seen")));
        }
        rows.Add((FontAwesomeIcon.FolderOpen, Loc.T("os.tile_menu_move_to_folder")));
        rows.Add((FontAwesomeIcon.TrashAlt, Loc.T("os.tile_menu_remove_app")));

        DrawMenuDemo(dl, Centre(tile), rows, rows.Count - 1);
    }

    /// <summary>Right-clicking bare wallpaper, which is where apps, folders and the wallpaper itself are
    /// reached from. Same rule: the rows read from the real menu's keys.</summary>
    private void DemoHomeMenu(ImDrawListPtr dl)
    {
        if (EmptySlot is not { } slot)
        {
            return;
        }
        List<(FontAwesomeIcon Icon, string Label)> rows =
        [
            (FontAwesomeIcon.Plus, Loc.T("os.home_menu_add_app")),
            (FontAwesomeIcon.FolderPlus, Loc.T("os.home_menu_add_folder")),
            (FontAwesomeIcon.CheckDouble, Loc.T("os.home_menu_mark_seen")),
            (FontAwesomeIcon.Image, Loc.T("os.home_menu_wallpaper")),
        ];
        DrawMenuDemo(dl, Centre(slot), rows, 1);
    }

    /// <summary>The shared shape of both menu demonstrations: the cursor arrives, right-clicks, the menu
    /// unfolds beside the point it was clicked, and the cursor settles on the row worth reading.</summary>
    private void DrawMenuDemo(ImDrawListPtr dl, Vector2 at,
        List<(FontAwesomeIcon Icon, string Label)> rows, int highlight)
    {
        const float ArriveEnd = 0.9f;
        const float ClickEnd = 1.25f;
        const float OpenEnd = 1.6f;
        const float PickEnd = 2.7f;

        var phase = DemoTime % DemoLoopSeconds;
        var from = at - Px(70f, 60f);
        var cursor = phase < ArriveEnd
            ? Vector2.Lerp(from, at, Ease(phase / ArriveEnd))
            : at;

        var open = Math.Clamp((phase - ClickEnd) / (OpenEnd - ClickEnd), 0f, 1f);
        if (open <= 0f)
        {
            DrawCursor(dl, cursor, phase is >= ArriveEnd and < ClickEnd, right: true);
            return;
        }

        var rowH = ImGui.GetTextLineHeight() + Px(14f);
        var width = Px(20f) + rows.Max(r => ImGui.CalcTextSize(r.Label).X) + Px(28f);
        var height = (rowH * rows.Count) + Px(12f);
        var menuTL = new Vector2(
            Math.Clamp(at.X + Px(6f), _contentTL.X + Px(6f), MathF.Max(_contentTL.X + Px(6f), _contentBR.X - width - Px(6f))),
            Math.Clamp(at.Y + Px(6f), _contentTL.Y + Px(6f), MathF.Max(_contentTL.Y + Px(6f), _contentBR.Y - height - Px(6f))));

        var shown = Ease(open) * height;
        dl.PushClipRect(menuTL, new Vector2(menuTL.X + width, menuTL.Y + shown), true);
        dl.AddRectFilled(menuTL, menuTL + new Vector2(width, height),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.09f, 0.12f, 0.98f)), Px(10f));
        dl.AddRect(menuTL, menuTL + new Vector2(width, height),
            ImGui.ColorConvertFloat4ToU32(Emphasis with { W = 0.35f }), Px(10f), ImDrawFlags.RoundCornersAll, Px(1f));

        var picked = phase >= PickEnd ? highlight : (phase > OpenEnd ? RowUnderCursor(phase, rows.Count, highlight) : -1);
        for (var i = 0; i < rows.Count; i++)
        {
            var rowTL = menuTL + new Vector2(Px(6f), Px(6f) + (rowH * i));
            var rowBR = rowTL + new Vector2(width - Px(12f), rowH);
            if (i == picked)
            {
                dl.AddRectFilled(rowTL, rowBR, ImGui.ColorConvertFloat4ToU32(Emphasis with { W = 0.22f }), Px(7f));
            }
            IconDraw.AddCentered(dl, rows[i].Icon, Px(12f),
                new Vector2(rowTL.X + Px(14f), (rowTL.Y + rowBR.Y) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.75f)));
            dl.AddText(new Vector2(rowTL.X + Px(30f), (rowTL.Y + rowBR.Y) * 0.5f - (ImGui.GetTextLineHeight() * 0.5f)),
                0xF0FFFFFFu, rows[i].Label);
        }
        dl.PopClipRect();

        var target = picked >= 0
            ? new Vector2(menuTL.X + Px(34f), menuTL.Y + Px(6f) + (rowH * (picked + 0.5f)))
            : menuTL + new Vector2(Px(20f), Px(14f));
        DrawCursor(dl, Vector2.Lerp(cursor, target, Ease(open)), false);
    }

    /// <summary>Which row the cursor is over while it walks down the menu, so the highlight follows it
    /// instead of jumping straight to the answer.</summary>
    private static int RowUnderCursor(float phase, int count, int highlight)
    {
        var walk = Math.Clamp((phase - 1.6f) / 1.1f, 0f, 1f);
        return Math.Clamp((int)MathF.Round(walk * highlight), 0, count - 1);
    }

    /// <summary>The one timeline every carry demonstration runs on: reach, press, travel, drop, rest.</summary>
    private static (bool Held, float Lift, float Travel) CarryProgress(float phase)
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

    private static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static Vector2 Centre((Vector2 TL, Vector2 BR) r) => (r.TL + r.BR) * 0.5f;

    private static float Size((Vector2 TL, Vector2 BR) r) => MathF.Min(r.BR.X - r.TL.X, r.BR.Y - r.TL.Y);

    /// <summary>A copy of a real tile, riding the cursor. It borrows the app's own tile art, so the thing
    /// being carried is recognisably the thing that was picked up.</summary>
    private void DrawGhostTile(ImDrawListPtr dl, string appId, Vector2 centre, float side, float alpha)
    {
        var half = new Vector2(side * 0.5f, side * 0.5f);
        var tl = centre - half;
        var br = centre + half;
        dl.AddRectFilled(tl + Px(0f, 6f), br + Px(0f, 6f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.35f * alpha)), side * 0.28f);
        if (_shell.Find(appId) is { } app)
        {
            OsDraw.AppTile(dl, app, tl, br, alpha);
            return;
        }
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(Emphasis with { W = 0.6f * alpha }), side * 0.28f);
    }

    /// <summary>The cell an icon was lifted out of, as a dashed hole, so a carried copy never reads as the
    /// icon having been duplicated.</summary>
    private void DrawEmptyCell(ImDrawListPtr dl, (Vector2 TL, Vector2 BR) rect, float alpha)
    {
        if (alpha <= 0f)
        {
            return;
        }
        var side = Size(rect);
        var centre = Centre(rect);
        var half = new Vector2(side * 0.5f, side * 0.5f);
        dl.AddRect(centre - half, centre + half,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.35f * alpha)),
            side * 0.28f, ImDrawFlags.RoundCornersAll, Px(1.6f));
    }

    /// <summary>The path a carried icon is taking, drawn as it is walked rather than all at once.</summary>
    private static void DrawTrail(ImDrawListPtr dl, Vector2 from, Vector2 to, float travel)
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

    /// <summary>A folder forming under a dropped icon: the four-square preview a real folder tile wears.</summary>
    private void DrawGhostFolder(ImDrawListPtr dl, Vector2 centre, float side, float t)
    {
        var half = new Vector2(side * 0.5f, side * 0.5f);
        var tl = centre - half;
        var br = centre + half;
        dl.AddRectFilled(tl, br,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.16f + (0.10f * t))), side * 0.28f);
        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(Emphasis with { W = 0.5f + (0.4f * t) }),
            side * 0.28f, ImDrawFlags.RoundCornersAll, Px(1.6f));

        var mini = side * 0.28f;
        var gap = side * 0.08f;
        var blockTL = centre - new Vector2(mini + (gap * 0.5f), mini + (gap * 0.5f));
        for (var i = 0; i < 4; i++)
        {
            var cell = blockTL + new Vector2((mini + gap) * (i % 2), (mini + gap) * (i / 2));
            var fill = i < 2 ? 0.55f : 0.25f * t;
            dl.AddRectFilled(cell, cell + new Vector2(mini, mini),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, fill)), mini * 0.3f);
        }
    }

    /// <summary>The pointer the demonstrations move around. Drawn rather than borrowed from the OS cursor,
    /// which sits wherever the player's hand actually is and would fight the script.</summary>
    private static void DrawCursor(ImDrawListPtr dl, Vector2 at, bool pressed, bool right = false)
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

    // ------------------------------------------------------------------ chrome

    private void DrawPanel(ImDrawListPtr dl, Step step, bool? belowSpot)
    {
        var accent = Emphasis;
        var size = _contentBR - _contentTL;
        var padIn = Px(16f);
        var panelW = size.X - Px(40f);
        var innerW = panelW - (padIn * 2f);
        var lineH = ImGui.GetTextLineHeight();
        var body = Loc.T(step.BodyKey);
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
            dl.PushClipRect(panelTL, panelBR, true);
            dl.AddText(new Vector2(iconC.X + iconR + Px(10f), iconC.Y - (ImGui.GetFontSize() * 0.5f)),
                0xFFFFFFFFu, Loc.T(step.TitleKey));
            dl.PopClipRect();
        }

        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), panelTL + new Vector2(padIn, padIn + headerH + Px(10f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f)), body, innerW);

        var footerY = panelBR.Y - padIn - btnH;
        var progress = $"{_step + 1} / {_plan.Count}";
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            new Vector2(panelTL.X + padIn, footerY + ((btnH - (lineH * 0.85f)) * 0.5f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.45f)), progress);

        var last = _step == _plan.Count - 1;
        var nextW = Px(84f);
        var backW = Px(64f);
        var skipW = Px(56f);
        var nextTL = new Vector2(panelBR.X - padIn - nextW, footerY);
        if (PanelButton("##osTourNext", Loc.T(last ? "os.tour_finish" : "os.tour_next"), nextTL, new Vector2(nextW, btnH), accent))
        {
            if (last)
            {
                Finish();
            }
            else
            {
                _step++;
            }
        }
        if (_step > 0
            && PanelButton("##osTourBack", Loc.T("os.tour_back"), nextTL - new Vector2(backW + Px(8f), 0f),
                new Vector2(backW, btnH), new Vector4(1f, 1f, 1f, 0.10f)))
        {
            _step--;
        }
        if (!last
            && PanelButton("##osTourSkip", Loc.T("os.tour_skip"),
                new Vector2(panelTL.X + padIn + ImGui.CalcTextSize(progress).X + Px(14f), footerY),
                new Vector2(skipW, btnH), new Vector4(1f, 1f, 1f, 0.06f)))
        {
            Finish();
        }
    }

    private void Finish()
    {
        if (_enteredStep >= 0)
        {
            _plan[_enteredStep].Exit?.Invoke(this);
            _enteredStep = -1;
        }
        CleanupAll();
        Active = false;
        UiHost.Configuration.Os.TourSeen = true;
        UiHost.Configuration.Save();
    }

    /// <summary>Defensive teardown of everything the tour can leave behind, whichever step it was on. Every
    /// entry here must stay idempotent: it also runs for steps that never started.</summary>
    private void CleanupAll()
    {
        _shade.Close();
        _shell.DismissByTag(TourTag);
        ClearDemoBadge();
        _home.SetAddAppsOpen(false);
        _home.ShowPage(0);
    }

    /// <summary>The pretend unread count. Added and subtracted as a DELTA rather than set and cleared,
    /// because the app it lands on may well have a real count of its own and the tour must hand that back
    /// untouched.</summary>
    private void ApplyDemoBadge()
    {
        _home.ShowPage(0);
        if (_badgeApplied || _badgeAppId is not { } id)
        {
            return;
        }
        _shell.AddBadge(id, DemoBadgeCount);
        _badgeApplied = true;
    }

    private void ClearDemoBadge()
    {
        if (!_badgeApplied || _badgeAppId is not { } id)
        {
            return;
        }
        _shell.AddBadge(id, -DemoBadgeCount);
        _badgeApplied = false;
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
