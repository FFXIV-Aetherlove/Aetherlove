using System;
using System.Numerics;
using AetherLove.Screens;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Os;

/// <summary>The guided OS tour: a step-by-step spotlight overlay that walks the user through the phone shell
/// with live demonstrations (it opens the shade, posts a sample notification, turns on edit mode, conjures a
/// demo folder, ...). Auto-started once on the first Home landing (<see cref="AetherOS.Sdk.OsConfig.TourSeen"/>)
/// and replayable from the OS settings via <see cref="AetherOS.Sdk.IOsShell.StartTour"/>. Drawn OUTSIDE the
/// content clip so it can highlight bezel elements (home button, status strip).</summary>
public sealed class OsTour
{
    private const string TourTag = "os:tour";
    private const string FakeBadgeAppId = "hangouts";

    private enum Anchor
    {
        Auto,
        Bottom,
        Center,
    }

    private sealed record Step(FontAwesomeIcon Icon, string TitleKey, string BodyKey,
        Func<OsTour, (Vector2 TL, Vector2 BR)?>? Spotlight = null,
        Anchor Panel = Anchor.Auto,
        bool Dim = true,
        Action<OsTour>? Enter = null,
        Action<OsTour>? Exit = null);

    private static readonly Step[] Steps =
    [
        new(FontAwesomeIcon.MobileAlt, "os.tour_welcome_title", "os.tour_welcome_body", Panel: Anchor.Center),
        new(FontAwesomeIcon.Home, "os.tour_homebtn_title", "os.tour_homebtn_body",
            t => t.HomeButtonRect(), Panel: Anchor.Bottom),
        new(FontAwesomeIcon.Th, "os.tour_pages_title", "os.tour_pages_body",
            t => t.PageDotsRect(),
            Enter: t => t._home.ShowPage(0)),
        new(FontAwesomeIcon.Magic, "os.tour_widgets_title", "os.tour_widgets_body",
            Panel: Anchor.Bottom, Dim: false,
            Enter: t => t._home.ShowPage(-1),
            Exit: t => t._home.ShowPage(0)),
        new(FontAwesomeIcon.Clock, "os.tour_statusbar_title", "os.tour_statusbar_body",
            t => t.StatusStripRect()),
        new(FontAwesomeIcon.AngleDoubleDown, "os.tour_shade_title", "os.tour_shade_body",
            t => t.ShadePanelRect(), Panel: Anchor.Bottom, Dim: false,
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
        new(FontAwesomeIcon.Bell, "os.tour_badges_title", "os.tour_badges_body",
            t => t.TileRect(FakeBadgeAppId),
            Enter: t =>
            {
                t._home.ShowPage(0);
                t._shell.AddBadge(FakeBadgeAppId, 67);
            },
            Exit: t => t._shell.ClearBadge(FakeBadgeAppId)),
        new(FontAwesomeIcon.ArrowsAlt, "os.tour_edit_title", "os.tour_edit_body",
            t => t.DonePillRect(), Panel: Anchor.Bottom, Dim: false,
            Enter: t => t._home.SetEditMode(true),
            Exit: t => t._home.SetEditMode(false)),
        new(FontAwesomeIcon.GripHorizontal, "os.tour_dock_title", "os.tour_dock_body",
            t => t.DockRect()),
        new(FontAwesomeIcon.FolderOpen, "os.tour_folders_title", "os.tour_folders_body",
            t => t._demoFolderId == null ? null : t.TileRect(t._demoFolderId), Panel: Anchor.Bottom,
            Enter: t =>
            {
                t._home.ShowPage(0);
                t.AddDemoFolder();
            },
            Exit: t => t.RemoveDemoFolder()),
        new(FontAwesomeIcon.Plus, "os.tour_addapps_title", "os.tour_addapps_body",
            t => t.AddAppsPanelRect(), Panel: Anchor.Bottom, Dim: false,
            Enter: t => t._home.SetAddAppsOpen(true),
            Exit: t => t._home.SetAddAppsOpen(false)),
        new(FontAwesomeIcon.Share, "os.tour_share_title", "os.tour_share_body", Panel: Anchor.Center),
        new(FontAwesomeIcon.Palette, "os.tour_look_title", "os.tour_look_body",
            t => t.TileRect("settings")),
        new(FontAwesomeIcon.Plane, "os.tour_offline_title", "os.tour_offline_body", Panel: Anchor.Center),
        new(FontAwesomeIcon.CheckCircle, "os.tour_done_title", "os.tour_done_body", Panel: Anchor.Center),
    ];

    private readonly OsShell _shell;
    private readonly NotificationShade _shade;
    private readonly HomeScreen _home;

    private int _step;
    private int _enteredStep = -1;
    private bool _welcome;
    private string? _demoFolderId;

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

    public void Start()
    {
        if (Active)
        {
            return;
        }
        _step = 0;
        _enteredStep = -1;
        _welcome = !UiHost.Configuration.Os.Welcome20Seen;
        Active = true;
    }

    public void Draw(Vector2 winPos, Vector2 winSize)
    {
        if (!Active)
        {
            return;
        }

        var t = ThemeService.Current;
        _winPos = winPos;
        _winSize = winSize;
        _contentTL = winPos + Px(t.BezelLeft, t.BezelTop);
        _contentBR = winPos + new Vector2(winSize.X - Px(t.BezelRight), winSize.Y - Px(t.BezelBottom));

        if (_welcome)
        {
            DrawWelcome();
            return;
        }

        if (_step != _enteredStep)
        {
            if (_enteredStep >= 0)
            {
                Steps[_enteredStep].Exit?.Invoke(this);
            }
            Steps[_step].Enter?.Invoke(this);
            _enteredStep = _step;
        }

        var step = Steps[_step];
        var dl = ImGui.GetWindowDrawList();
        var size = _contentBR - _contentTL;
        var spot = step.Spotlight?.Invoke(this);

        if (step.Dim)
        {
            DrawDim(dl, spot);
        }
        if (spot is { } s)
        {
            // Clamped inside the window so edge-hugging spotlights (StatusBarTop 0 themes) keep a full ring.
            var ringTL = Vector2.Max(s.TL - Px(3f, 3f), _winPos + Px(2f, 2f));
            var ringBR = Vector2.Min(s.BR + Px(3f, 3f), _winPos + _winSize - Px(2f, 2f));
            var pulse = 0.55f + 0.45f * MathF.Sin((float)ImGui.GetTime() * 3.4f);
            dl.AddRect(ringTL, ringBR,
                ImGui.ColorConvertFloat4ToU32(Emphasis with { W = pulse }),
                Px(10f), ImDrawFlags.RoundCornersAll, Px(2.4f));
        }

        var belowSpot = step.Panel switch
        {
            Anchor.Bottom => true,
            Anchor.Center => (bool?)null,
            _ => spot is { } sp ? (sp.TL.Y + sp.BR.Y) * 0.5f < _contentTL.Y + size.Y * 0.5f : null,
        };
        DrawPanel(dl, step, belowSpot);

        // Scrim last: with overlapping items the first-submitted one wins clicks, so the panel buttons stay
        // clickable. Covers the whole window so bezel buttons pause during the tour.
        ImGui.SetCursorScreenPos(_winPos);
        ImGui.InvisibleButton("##osTourScrim", _winSize);
    }

    /// <summary>Dims the content around the spotlight cutout (clamped to the content rect; a spotlight fully
    /// in the bezel, like the home button, leaves the content uniformly dimmed).</summary>
    private void DrawDim(ImDrawListPtr dl, (Vector2 TL, Vector2 BR)? spot)
    {
        var dim = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.62f));
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

    private (Vector2 TL, Vector2 BR)? HomeButtonRect()
    {
        var t = ThemeService.Current;
        var b = t.HomeButton;
        var center = new Vector2(
            _winPos.X + _winSize.X * 0.5f + Px(b.CenterXOffset),
            _winPos.Y + _winSize.Y - Px(t.BezelBottom) * 0.5f + Px(b.CenterYOffset));
        var half = Px(b.HitSize.X, b.HitSize.Y) * 0.5f + Px(4f, 4f);
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
        (_contentTL, new Vector2(_contentBR.X, _contentTL.Y + (_contentBR.Y - _contentTL.Y) * 0.74f));

    private (Vector2 TL, Vector2 BR)? PageDotsRect()
    {
        var y = _contentBR.Y - Px(76f) - Px(42f);
        return ((Vector2 TL, Vector2 BR)?)(
            new Vector2(_contentTL.X + (_contentBR.X - _contentTL.X) * 0.28f, y),
            new Vector2(_contentBR.X - (_contentBR.X - _contentTL.X) * 0.28f, y + Px(28f)));
    }

    private (Vector2 TL, Vector2 BR)? DockRect() =>
        (new Vector2(_contentTL.X + Px(10f), _contentBR.Y - Px(96f)),
         new Vector2(_contentBR.X - Px(10f), _contentBR.Y - Px(8f)));

    /// <summary>The add-apps overlay panel, mirrored from <c>HomeScreen.DrawAddAppsOverlay</c> geometry.</summary>
    private (Vector2 TL, Vector2 BR)? AddAppsPanelRect()
    {
        var size = _contentBR - _contentTL;
        var panelW = size.X - Px(36f);
        var tl = _contentTL + new Vector2((size.X - panelW) * 0.5f, size.Y * 0.14f);
        return (tl, tl + new Vector2(panelW, size.Y * 0.64f));
    }

    private (Vector2 TL, Vector2 BR)? TileRect(string id) =>
        _home.TryGetTileRect(id, out var tl, out var br) ? (tl - Px(4f, 4f), br + Px(4f, 20f)) : null;

    private (Vector2 TL, Vector2 BR)? DonePillRect() =>
        _home.TryGetDonePillRect(out var tl, out var br) ? (tl - Px(4f, 4f), br + Px(4f, 4f)) : null;

    private void AddDemoFolder()
    {
        if (_demoFolderId != null)
        {
            return;
        }
        var os = UiHost.Configuration.Os;
        var folder = new OsFolder
        {
            Id = "folder:" + Guid.NewGuid().ToString("N"),
            Name = Loc.T("os.tour_demo_folder"),
        };
        // Grid apps only: foldering a dock app (clock lives there now) would blank its dock slot mid-tour.
        foreach (var appId in new[] { "weather", "photos" })
        {
            if (_shell.Find(appId) != null)
            {
                folder.AppIds.Add(appId);
            }
        }
        os.Folders.Add(folder);
        os.IconOrder.Insert(0, folder.Id);
        _demoFolderId = folder.Id;
    }

    private void RemoveDemoFolder()
    {
        if (_demoFolderId == null)
        {
            return;
        }
        var os = UiHost.Configuration.Os;
        os.Folders.RemoveAll(f => f.Id == _demoFolderId);
        os.IconOrder.Remove(_demoFolderId);
        _demoFolderId = null;
    }

    /// <summary>The one-time 2.0 splash shown before the first tour. Placeholder copy, deliberately
    /// English-only and outside the localization tables: Astraea rewrites it before release.</summary>
    private void DrawWelcome()
    {
        const string Title = "Welcome to AetherLove 2.0";
        const string Body = "This is a very big update and all of it will be explained now. Enjoy!";
        const string Button = "Let's go";

        var accent = Emphasis;
        var dl = ImGui.GetWindowDrawList();
        var size = _contentBR - _contentTL;
        dl.AddRectFilled(_contentTL, _contentBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.72f)));

        var padIn = Px(20f);
        var panelW = size.X - Px(48f);
        var innerW = panelW - padIn * 2f;
        var iconR = Px(26f);
        float titleH;
        using (UiFonts.H2?.Push())
        {
            titleH = ImGui.GetFontSize();
        }
        var bodyH = ImGui.CalcTextSize(Body, false, innerW).Y;
        var btnH = Px(34f);
        var panelH = padIn + iconR * 2f + Px(14f) + titleH + Px(10f) + bodyH + Px(18f) + btnH + padIn;
        var panelTL = _contentTL + (size - new Vector2(panelW, panelH)) * 0.5f;
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL + Px(0f, 5f), panelBR + Px(0f, 5f), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.40f)), Px(18f));
        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.11f, 0.10f, 0.13f, 0.98f)), Px(18f));
        dl.AddRect(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(accent with { W = 0.75f }), Px(18f), ImDrawFlags.RoundCornersAll, Px(1.6f));

        var iconC = new Vector2(panelTL.X + panelW * 0.5f, panelTL.Y + padIn + iconR);
        var pulse = 0.85f + 0.15f * MathF.Sin((float)ImGui.GetTime() * 2.4f);
        dl.AddCircleFilled(iconC, iconR, ImGui.ColorConvertFloat4ToU32(accent with { W = pulse }), 40);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Heart, Px(22f), iconC, 0xFFFFFFFFu);

        using (UiFonts.H2?.Push())
        {
            var sz = ImGui.CalcTextSize(Title);
            dl.AddText(new Vector2(panelTL.X + (panelW - sz.X) * 0.5f, iconC.Y + iconR + Px(14f)), 0xFFFFFFFFu, Title);
        }
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(panelTL.X + padIn, iconC.Y + iconR + Px(14f) + titleH + Px(10f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f)), Body, innerW);

        if (PanelButton("##osWelcomeGo", Button, new Vector2(panelTL.X + padIn, panelBR.Y - padIn - btnH),
                new Vector2(innerW, btnH), accent))
        {
            _welcome = false;
            UiHost.Configuration.Os.Welcome20Seen = true;
            UiHost.Configuration.Save();
        }

        // Scrim last: with overlapping items the first-submitted one wins clicks, so the button stays clickable.
        ImGui.SetCursorScreenPos(_winPos);
        ImGui.InvisibleButton("##osWelcomeScrim", _winSize);
    }

    private void DrawPanel(ImDrawListPtr dl, Step step, bool? belowSpot)
    {
        var accent = Emphasis;
        var size = _contentBR - _contentTL;
        var padIn = Px(16f);
        var panelW = size.X - Px(40f);
        var innerW = panelW - padIn * 2f;
        var lineH = ImGui.GetTextLineHeight();
        var body = Loc.T(step.BodyKey);
        var bodyH = ImGui.CalcTextSize(body, false, innerW).Y;
        var btnH = Px(30f);
        var headerH = Px(34f);
        var panelH = padIn + headerH + Px(10f) + bodyH + Px(16f) + btnH + padIn;

        var panelX = _contentTL.X + (size.X - panelW) * 0.5f;
        var panelY = belowSpot switch
        {
            true => _contentTL.Y + size.Y - panelH - Px(24f),
            false => _contentTL.Y + Px(56f),
            null => _contentTL.Y + (size.Y - panelH) * 0.5f,
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
            dl.AddText(new Vector2(iconC.X + iconR + Px(10f), iconC.Y - ImGui.GetFontSize() * 0.5f),
                0xFFFFFFFFu, Loc.T(step.TitleKey));
            dl.PopClipRect();
        }

        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), panelTL + new Vector2(padIn, padIn + headerH + Px(10f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f)), body, innerW);

        var footerY = panelBR.Y - padIn - btnH;
        var progress = $"{_step + 1} / {Steps.Length}";
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.85f,
            new Vector2(panelTL.X + padIn, footerY + (btnH - lineH * 0.85f) * 0.5f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.45f)), progress);

        var last = _step == Steps.Length - 1;
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
            Steps[_enteredStep].Exit?.Invoke(this);
            _enteredStep = -1;
        }
        CleanupAll();
        Active = false;
        UiHost.Configuration.Os.TourSeen = true;
        UiHost.Configuration.Save();
    }

    /// <summary>Defensive teardown of every demo the tour can leave behind, regardless of which step ran it.</summary>
    private void CleanupAll()
    {
        _shade.Close();
        _shell.DismissByTag(TourTag);
        _shell.ClearBadge(FakeBadgeAppId);
        _home.SetEditMode(false);
        _home.SetAddAppsOpen(false);
        _home.ShowPage(0);
        RemoveDemoFolder();
    }

    private static bool PanelButton(string id, string label, Vector2 tl, Vector2 size, Vector4 fill)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var col = hovered
            ? new Vector4(fill.X + (1f - fill.X) * 0.12f, fill.Y + (1f - fill.Y) * 0.12f, fill.Z + (1f - fill.Z) * 0.12f, MathF.Min(1f, fill.W + 0.08f))
            : fill;
        dl.AddRectFilled(tl, tl + size, ImGui.ColorConvertFloat4ToU32(col), Px(9f));
        var sz = ImGui.CalcTextSize(label);
        dl.PushClipRect(tl, tl + size, true);
        dl.AddText(tl + (size - sz) * 0.5f, 0xF2FFFFFFu, label);
        dl.PopClipRect();
        return clicked;
    }
}
