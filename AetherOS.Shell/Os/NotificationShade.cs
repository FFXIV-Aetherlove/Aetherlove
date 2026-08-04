using System;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Os;

/// <summary>The pull-down notification center: quick toggles, brightness, and the notification list.</summary>
public sealed class NotificationShade
{
    private readonly OsShell _shell;
    private readonly WallpaperService _wallpapers;
    private readonly IOsAccountInfo _account;

    private float _openT;
    private bool _targetOpen;
    private bool _dragging;
    private float _dragT;
    /// <summary>Smoothed open-fraction velocity (per second); negative = flicking closed, positive = open.</summary>
    private float _dragVel;

    private Guid _swipeId = Guid.Empty;
    private float _swipeStartX;
    private float _swipeX;
    private bool _swipeMoved;
    private readonly System.Collections.Generic.Dictionary<Guid, float> _dismissing = new();

    private bool _closeDragging;
    private bool _closeDragMoved;
    private float _closeDragStartY;
    private float _closeDragStartOpen;

    private const float AnimSpeed = 7f;

    public NotificationShade(OsShell shell, WallpaperService wallpapers, IOsAccountInfo account)
    {
        _shell = shell;
        _wallpapers = wallpapers;
        _account = account;
    }

    public bool Visible => _openT > 0.001f || _targetOpen || _dragging;

    /// <summary>While the guided tour demos the shade, its widgets are not submitted at all (visuals only):
    /// even a disabled item claims hover and would swallow clicks meant for the tour's buttons beneath.</summary>
    public bool InputLocked { get; set; }

    public void Toggle() => _targetOpen = !_targetOpen;

    public void Open() => _targetOpen = true;

    public void Close() => _targetOpen = false;

    public void DragTo(float fraction)
    {
        var clamped = Math.Clamp(fraction, 0f, 1f);
        var dt = ImGui.GetIO().DeltaTime;
        if (!_dragging)
        {
            _dragVel = 0f;
        }
        else if (dt > 0f)
        {
            _dragVel = _dragVel * 0.4f + (clamped - _dragT) / dt * 0.6f;
        }
        _dragging = true;
        _dragT = clamped;
    }

    public void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        // A fast flick wins over release position: flick up (velocity falling) closes, flick down opens, even
        // from the middle. A slow drag still settles by position.
        const float FlickThreshold = 1.5f;
        if (_dragVel < -FlickThreshold)
        {
            _targetOpen = false;
        }
        else if (_dragVel > FlickThreshold)
        {
            _targetOpen = true;
        }
        else
        {
            _targetOpen = _dragT > 0.25f;
        }
        _dragVel = 0f;
    }

    public void Draw(Vector2 contentTL, Vector2 contentBR)
    {
        // Self-heal a missed release: the status bar owns the drag but may stop drawing mid-gesture
        // (combat hide, a gate screen), and a stuck _dragging keeps the input-stealing overlay alive forever.
        if (_dragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            EndDrag();
        }

        var dt = ImGui.GetIO().DeltaTime;
        if (_dragging)
        {
            // Exact finger position (not Max) so close-drag retracts instead of pinning open.
            _openT = _dragT;
        }
        else
        {
            var target = _targetOpen ? 1f : 0f;
            _openT += (target - _openT) * Math.Min(1f, dt * AnimSpeed);
            if (Math.Abs(_openT - target) < 0.004f)
            {
                _openT = target;
            }
        }
        if (_openT <= 0.001f && !_targetOpen)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var contentSize = contentBR - contentTL;
        dl.AddRectFilled(contentTL, contentBR, OsDraw.Black(0.50f * _openT));

        var panelH = contentSize.Y * 0.74f;
        var panelTop = contentTL.Y - panelH * (1f - _openT);
        var panelTL = new Vector2(contentTL.X, panelTop);
        var panelBR = new Vector2(contentBR.X, panelTop + panelH);
        var rounding = Px(20f);

        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.055f, 0.055f, 0.085f, 0.97f)),
            rounding, ImDrawFlags.RoundCornersBottom);
        dl.AddRect(panelTL, panelBR, OsDraw.White(0.08f), rounding, ImDrawFlags.RoundCornersBottom, Px(1f));

        DrawPanelContent(dl, panelTL, panelBR);

        var handleC = new Vector2((panelTL.X + panelBR.X) * 0.5f, panelBR.Y - Px(9f));
        dl.AddRectFilled(handleC - Px(24f, 2.2f), handleC + Px(24f, 2.2f), OsDraw.White(0.45f), Px(2.2f));
        if (InputLocked)
        {
            return;
        }

        // Header submitted after content so Clear/toggles stay clickable (first-submitted item wins).
        HandleCloseDrag("##shadeHandle", new Vector2(handleC.X - Px(60f), panelBR.Y - Px(20f)),
            Px(120f, 26f), contentTL.Y, panelH, closeOnTap: true, showHand: true);
        HandleCloseDrag("##shadeHeaderGrab", new Vector2(panelTL.X, panelTL.Y + Px(2f)),
            new Vector2(panelBR.X - panelTL.X, Px(46f)), contentTL.Y, panelH, closeOnTap: false);

        // Only the dimmed area below the panel closes on click.
        var scrimTL = new Vector2(contentTL.X, Math.Min(panelBR.Y + Px(2f), contentBR.Y - 1f));
        ImGui.SetCursorScreenPos(scrimTL);
        if (ImGui.InvisibleButton("##shadeScrim", contentBR - scrimTL) && _openT >= 0.99f)
        {
            Close();
        }
    }

    /// <summary>Grab zone for upward drag; drag is relative to grab point so no snap.</summary>
    private void HandleCloseDrag(string id, Vector2 zoneTL, Vector2 zoneSize, float contentTop, float panelH,
        bool closeOnTap, bool showHand = false)
    {
        ImGui.SetCursorScreenPos(zoneTL);
        ImGui.InvisibleButton(id, zoneSize);
        if (showHand && ImGui.IsItemHovered())
        {
            SharedUiHelpers.HandOnHover();
        }
        if (ImGui.IsItemActivated())
        {
            _closeDragging = true;
            _closeDragMoved = false;
            _closeDragStartY = ImGui.GetMousePos().Y;
            _closeDragStartOpen = _openT;
        }
        if (_closeDragging && ImGui.IsItemActive())
        {
            var delta = ImGui.GetMousePos().Y - _closeDragStartY;
            if (delta < -Px(5f))
            {
                _closeDragMoved = true;
            }
            if (_closeDragMoved)
            {
                DragTo(_closeDragStartOpen + delta / panelH);
            }
        }
        if (_closeDragging && ImGui.IsItemDeactivated())
        {
            _closeDragging = false;
            if (_closeDragMoved)
            {
                EndDrag();
            }
            else if (closeOnTap)
            {
                Close();
            }
        }
    }

    private void DrawPanelContent(ImDrawListPtr dl, Vector2 panelTL, Vector2 panelBR)
    {
        var padX = Px(18f);
        var innerTL = new Vector2(panelTL.X + padX, panelTL.Y + Px(16f));
        var innerW = panelBR.X - panelTL.X - padX * 2f;
        var y = innerTL.Y;

        y = DrawUserHeader(dl, innerTL.X, y, innerW);

        using (UiFonts.H3?.Push())
        {
            dl.AddText(new Vector2(innerTL.X, y), OsDraw.White(0.95f), Loc.T("os.notifications"));
            var clear = Loc.T("os.notifications_clear");
            if (_shell.Notifications.Count > 0)
            {
                var sz = ImGui.CalcTextSize(clear);
                var pos = new Vector2(panelBR.X - padX - sz.X, y + Px(3f));
                var clearHovered = false;
                if (!InputLocked)
                {
                    ImGui.SetCursorScreenPos(pos - Px(4f, 2f));
                    if (ImGui.InvisibleButton("##shadeClear", sz + Px(8f, 4f)))
                    {
                        _shell.ClearNotifications();
                    }
                    clearHovered = ImGui.IsItemHovered();
                    if (clearHovered)
                    {
                        SharedUiHelpers.HandOnHover();
                    }
                }
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.82f, pos,
                    clearHovered ? ThemeService.Current.AccentU32
                        : ImGui.ColorConvertFloat4ToU32(ThemeService.Current.AccentLight), clear);
            }
            y += ImGui.GetFontSize() + Px(14f);
        }

        y = DrawQuickToggles(dl, innerTL.X, y, innerW);
        y = DrawBrightness(dl, innerTL.X, y, innerW);
        DrawNotificationList(innerTL.X, y, innerW, panelBR.Y - Px(26f) - y);
    }

    /// <summary>Compact account chip at the top of the shade: the OS avatar + display name. Skipped until the
    /// account snapshot is present.</summary>
    private float DrawUserHeader(ImDrawListPtr dl, float x, float y, float w)
    {
        var name = _account.DisplayName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return y;
        }
        var avR = Px(17f);
        var avC = new Vector2(x + avR, y + avR);
        var tex = _account.Avatar?.GetWrapOrDefault();
        if (tex != null)
        {
            dl.AddImageRounded(tex.Handle, avC - new Vector2(avR, avR), avC + new Vector2(avR, avR),
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, avR);
        }
        else
        {
            dl.AddCircleFilled(avC, avR, ThemeService.Current.AccentU32, 32);
            var initial = char.ToUpperInvariant(name[0]).ToString();
            dl.AddText(avC - ImGui.CalcTextSize(initial) * 0.5f, 0xFFFFFFFF, initial);
        }
        dl.AddCircle(avC, avR, OsDraw.White(0.18f), 32, 1.5f);

        using (UiFonts.H3?.Push())
        {
            var tsz = ImGui.CalcTextSize(name);
            dl.AddText(new Vector2(avC.X + avR + Px(10f), avC.Y - tsz.Y * 0.5f), OsDraw.White(0.95f), name);
        }
        return y + avR * 2f + Px(14f);
    }

    private float DrawQuickToggles(ImDrawListPtr dl, float x, float y, float w)
    {
        var cfg = UiHost.Configuration;
        var toggles = new (FontAwesomeIcon icon, string tipKey, bool active, Action onClick)[]
        {
            (FontAwesomeIcon.Bell, "os.qt_notifications", cfg.EnableNotifications,
                () => { cfg.EnableNotifications = !cfg.EnableNotifications; cfg.Save(); }),
            (FontAwesomeIcon.VolumeUp, "os.qt_sounds", cfg.EnableNotificationSounds,
                () => { cfg.EnableNotificationSounds = !cfg.EnableNotificationSounds; cfg.Save(); }),
            (FontAwesomeIcon.Lock, "os.qt_lock", cfg.LockPhonePosition,
                () => { cfg.LockPhonePosition = !cfg.LockPhonePosition; cfg.Save(); }),
            (FontAwesomeIcon.Palette, "os.qt_theme", false, CycleTheme),
            (FontAwesomeIcon.Image, "os.qt_wallpaper", false, _wallpapers.CycleWallpaper),
        };

        var r = Px(21f);
        var gap = (w - toggles.Length * r * 2f) / (toggles.Length - 1);
        for (int i = 0; i < toggles.Length; i++)
        {
            var (icon, tipKey, active, onClick) = toggles[i];
            var center = new Vector2(x + r + i * (r * 2f + gap), y + r);
            var hovered = false;
            if (!InputLocked)
            {
                ImGui.SetCursorScreenPos(center - new Vector2(r, r));
                if (ImGui.InvisibleButton($"##qt_{i}", new Vector2(r * 2f, r * 2f)))
                {
                    onClick();
                }
                hovered = ImGui.IsItemHovered();
                if (hovered)
                {
                    ImGui.SetTooltip(Loc.T(tipKey));
                    SharedUiHelpers.HandOnHover();
                }
            }
            var fill = active
                ? ThemeService.Current.AccentU32
                : ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, hovered ? 0.18f : 0.10f));
            dl.AddCircleFilled(center, r, fill, 32);
            IconDraw.AddCentered(dl, icon, r * 0.85f, center, OsDraw.White(active ? 1f : 0.85f));
        }
        return y + r * 2f + Px(14f);
    }

    private float DrawBrightness(ImDrawListPtr dl, float x, float y, float w)
    {
        var os = UiHost.Configuration.Os;
        IconDraw.AddCentered(dl, FontAwesomeIcon.Sun, Px(15f), new Vector2(x + Px(9f), y + Px(10f)), OsDraw.White(0.8f));
        var sliderX = x + Px(26f);
        if (InputLocked)
        {
            var trackTL = new Vector2(sliderX, y + Px(2f));
            var trackBR = new Vector2(x + w, y + Px(20f));
            dl.AddRectFilled(trackTL, trackBR, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(9f));
            var frac = Math.Clamp(1f - os.WallpaperDim, 0.02f, 1f);
            var grabX = trackTL.X + (trackBR.X - trackTL.X - Px(12f)) * frac;
            dl.AddRectFilled(new Vector2(grabX, trackTL.Y + Px(2f)), new Vector2(grabX + Px(12f), trackBR.Y - Px(2f)),
                ThemeService.Current.AccentU32, Px(6f));
            return y + Px(30f);
        }
        ImGui.SetCursorScreenPos(new Vector2(sliderX, y));
        ImGui.SetNextItemWidth(w - Px(26f));
        var brightness = 1f - os.WallpaperDim;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(1f, 1f, 1f, 0.14f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(1f, 1f, 1f, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, ThemeService.Current.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, ThemeService.Current.AccentLight);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        if (ImGui.SliderFloat("##osBrightness", ref brightness, 0.02f, 1f, ""))
        {
            os.WallpaperDim = Math.Clamp(1f - brightness, 0f, 0.98f);
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            UiHost.Configuration.Save();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(5);
        return y + Px(30f);
    }

    private void DrawNotificationList(float x, float y, float w, float h)
    {
        if (h < Px(40f))
        {
            return;
        }

        if (InputLocked)
        {
            // Look-only for the tour: no child window (it would claim hover) and no per-row widgets.
            var ldl = ImGui.GetWindowDrawList();
            var lRowH = Px(74f);
            var lCardH = lRowH - Px(8f);
            ldl.PushClipRect(new Vector2(x, y), new Vector2(x + w, y + h), true);
            var rowY = y;
            foreach (var n in _shell.Notifications)
            {
                if (rowY > y + h)
                {
                    break;
                }
                DrawNotificationCard(ldl, n, new Vector2(x, rowY), w, lCardH, offsetX: 0f, hovered: false);
                rowY += lRowH;
            }
            ldl.PopClipRect();
            return;
        }

        ImGui.SetCursorScreenPos(new Vector2(x, y));
        using var list = ImRaii.Child("##shadeList", new Vector2(w, h), false, ImGuiWindowFlags.NoBackground);
        if (!list)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var notifications = _shell.Notifications;
        if (notifications.Count == 0)
        {
            var center = new Vector2(x + w * 0.5f, y + h * 0.42f);
            IconDraw.AddCentered(dl, FontAwesomeIcon.BellSlash, Px(30f), center, OsDraw.White(0.28f));
            OsDraw.CenteredText(dl, Loc.T("os.notifications_empty"), center.X, center.Y + Px(26f), OsDraw.White(0.45f), 0.9f);
            return;
        }

        var rowH = Px(74f);
        var gap = Px(8f);
        var cardH = rowH - gap;
        var dt = ImGui.GetIO().DeltaTime;
        var dismissWidth = w * 0.35f;

        foreach (var n in notifications)
        {
            var rowTL = ImGui.GetCursorScreenPos();

            var offsetX = 0f;
            var sliding = false;
            if (_dismissing.TryGetValue(n.Id, out var slideX))
            {
                slideX += MathF.Sign(slideX) * (w + Px(30f)) * dt / 0.18f;
                if (MathF.Abs(slideX) >= w + Px(20f))
                {
                    _dismissing.Remove(n.Id);
                    _shell.DismissNotification(n.Id);
                    continue;
                }
                _dismissing[n.Id] = slideX;
                offsetX = slideX;
                sliding = true;
            }

            ImGui.SetCursorScreenPos(rowTL);
            ImGui.InvisibleButton($"##notif_{n.Id}", new Vector2(w, cardH));
            var itemHovered = ImGui.IsItemHovered();
            if (itemHovered)
            {
                SharedUiHelpers.HandOnHover();
            }

            if (!sliding)
            {
                if (ImGui.IsItemActivated())
                {
                    _swipeId = n.Id;
                    _swipeStartX = ImGui.GetMousePos().X;
                    _swipeX = 0f;
                    _swipeMoved = false;
                }
                if (_swipeId == n.Id && ImGui.IsItemActive())
                {
                    _swipeX = ImGui.GetMousePos().X - _swipeStartX;
                    if (MathF.Abs(_swipeX) > Px(6f))
                    {
                        _swipeMoved = true;
                    }
                    offsetX = _swipeX;
                }
                if (_swipeId == n.Id && ImGui.IsItemDeactivated())
                {
                    if (MathF.Abs(_swipeX) > dismissWidth)
                    {
                        _dismissing[n.Id] = _swipeX;
                    }
                    else if (!_swipeMoved)
                    {
                        OpenNotification(n);
                        _swipeId = Guid.Empty;
                        return;
                    }
                    _swipeId = Guid.Empty;
                }
            }

            if (ImGui.BeginPopupContextItem($"##notifCtx_{n.Id}", ImGuiPopupFlags.MouseButtonRight))
            {
                if (ImGui.Selectable(Loc.T("os.notif_remove")))
                {
                    _dismissing[n.Id] = -(w * 0.5f);
                }
                ImGui.EndPopup();
            }

            DrawNotificationCard(dl, n, rowTL, w, cardH, offsetX, itemHovered && !sliding);

            ImGui.SetCursorScreenPos(rowTL + new Vector2(0f, rowH));
        }
    }

    private void DrawNotificationCard(ImDrawListPtr dl, AetherOS.Sdk.OsNotification n, Vector2 rowTL, float w,
        float cardH, float offsetX, bool hovered)
    {
        var cardTL = rowTL + new Vector2(offsetX, 0f);
        var cardBR = new Vector2(rowTL.X + w + offsetX, rowTL.Y + cardH);
        var slideAlpha = 1f - Math.Clamp(MathF.Abs(offsetX) / (w * 0.6f), 0f, 0.85f);

        if (MathF.Abs(offsetX) > Px(2f))
        {
            var trashOnRight = offsetX < 0f;
            var trashC = trashOnRight
                ? new Vector2(rowTL.X + w - Px(26f), rowTL.Y + cardH * 0.5f)
                : new Vector2(rowTL.X + Px(26f), rowTL.Y + cardH * 0.5f);
            dl.AddRectFilled(rowTL, new Vector2(rowTL.X + w, rowTL.Y + cardH),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.72f, 0.20f, 0.24f, 0.9f)), Px(14f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.TrashAlt, Px(20f), trashC, OsDraw.White(0.95f));
        }

        dl.AddRectFilled(cardTL, cardBR, OsDraw.White(hovered ? 0.12f : 0.07f), Px(14f));

        var app = _shell.Find(n.AppId);
        var tileSz = Px(44f);
        var tileTL = new Vector2(cardTL.X + Px(11f), cardTL.Y + (cardH - tileSz) * 0.5f);
        if (app != null)
        {
            OsDraw.AppTile(dl, app, tileTL, tileTL + new Vector2(tileSz, tileSz), slideAlpha);
        }

        var textX = tileTL.X + tileSz + Px(12f);
        var age = FormatAge(DateTime.UtcNow - n.PostedAtUtc);
        var ageFsz = ImGui.GetFontSize() * 0.86f;
        var ageSz = ImGui.CalcTextSize(age) * 0.86f;
        dl.AddText(ImGui.GetFont(), ageFsz,
            new Vector2(cardBR.X - ageSz.X - Px(12f), cardTL.Y + Px(11f)), OsDraw.White(0.5f * slideAlpha), age);

        // Ellipsized per line rather than clipped: the title shares its row with the age, and a hard clip
        // cuts it mid-glyph right against the timestamp. TruncateToWidth measures at the current font size,
        // so each line's budget is divided by its own scale.
        const float titleScale = 1.06f;
        const float bodyScale = 0.94f;
        var titleW = cardBR.X - textX - ageSz.X - Px(24f);
        var bodyW = cardBR.X - textX - Px(12f);
        var title = SharedUiHelpers.TruncateToWidth(n.Title, titleW / titleScale);
        var body = SharedUiHelpers.TruncateToWidth(n.Body, bodyW / bodyScale);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * titleScale,
            new Vector2(textX, cardTL.Y + Px(11f)), OsDraw.White(0.96f * slideAlpha), title);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * bodyScale,
            new Vector2(textX, cardTL.Y + Px(35f)), OsDraw.White(0.66f * slideAlpha), body);
    }

    private void OpenNotification(AetherOS.Sdk.OsNotification n)
    {
        _shell.DismissNotification(n.Id);
        Close();
        if (n.OnTap != null)
        {
            n.OnTap();
        }
        else
        {
            _shell.OpenApp(n.AppId);
        }
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return Loc.T("os.time_now");
        }
        if (age.TotalHours < 1)
        {
            return $"{(int)age.TotalMinutes}m";
        }
        if (age.TotalDays < 1)
        {
            return $"{(int)age.TotalHours}h";
        }
        return $"{(int)age.TotalDays}d";
    }

    private static void CycleTheme()
    {
        var themes = ThemeService.Themes.Keys.ToArray();
        var idx = Array.IndexOf(themes, ThemeService.CurrentTheme);
        ThemeService.SetTheme(themes[(idx + 1) % themes.Length]);
    }
}
