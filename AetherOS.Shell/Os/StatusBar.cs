using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Os;

/// <summary>Clock, signal, notification count, and battery in the top bezel; dragging it down opens the shade.</summary>
public sealed class StatusBar
{
    private readonly OsShell _shell;
    private readonly NotificationShade _shade;

    private bool _dragging;
    private bool _dragEngaged;
    private float _dragStartY;

    public StatusBar(OsShell shell, NotificationShade shade)
    {
        _shell = shell;
        _shade = shade;
    }

    public void Draw(Vector2 winPos, Vector2 winSize, bool connected)
    {
        var theme = ThemeService.Current;
        var stripTL = winPos + new Vector2(Px(theme.BezelLeft), Px(theme.StatusBarTop));
        var stripBR = winPos + new Vector2(winSize.X - Px(theme.BezelRight), Px(theme.BezelTop));
        // Themes with top-bezel window buttons keep the icon cluster clear of them.
        var rightLimit = stripBR.X - Px(10f) - Px(theme.StatusBarRightInset);
        if (theme.MinimizeButtonTL.Y < theme.BezelTop)
        {
            rightLimit = Math.Min(rightLimit, winPos.X + Px(theme.MinimizeButtonTL.X) - Px(10f));
        }

        var dl = ImGui.GetWindowDrawList();
        var centerY = (stripTL.Y + stripBR.Y) * 0.5f;
        var fsz = ImGui.GetFontSize() * 0.76f;
        var tint = theme.StatusBarTint;

        // Right-hand cluster first, so the clock can be right-clamped against it.
        var x = rightLimit;
        x = DrawBattery(dl, x, centerY, tint);
        x -= Px(8f);
        x = DrawSignal(dl, x, centerY, connected, tint);
        if (!connected)
        {
            x -= Px(7f);
            IconDraw.AddCentered(dl, FontAwesomeIcon.ExclamationTriangle, Px(9f),
                new Vector2(x - Px(4.5f), centerY),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.38f, 0.32f, 0.95f)));
            x -= Px(9f);
        }
        var count = _shell.Notifications.Count;
        if (count > 0)
        {
            x -= Px(11f);
            var bellC = new Vector2(x - Px(5f), centerY);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Bell, Px(10f), bellC, Tint(tint, 0.9f));
            OsDraw.Badge(dl, bellC + new Vector2(Px(4.5f), -Px(4.5f)), count, 0.5f);
            x -= Px(10f);
        }

        // Clock: placed by the theme's align factor, clamped to clear the icon cluster and the left edge.
        var time = OsClock.Format(DateTime.Now);
        var timeSz = ImGui.CalcTextSize(time) * 0.76f;
        var usableW = stripBR.X - stripTL.X;
        var timeLeft = stripTL.X + (usableW - timeSz.X) * Math.Clamp(theme.StatusBarTimeAlign, 0f, 1f);
        timeLeft = Math.Min(timeLeft, x - Px(8f) - timeSz.X);
        timeLeft = Math.Max(timeLeft, stripTL.X);
        dl.AddText(ImGui.GetFont(), fsz, new Vector2(timeLeft, centerY - fsz * 0.5f), Tint(tint, 0.92f), time);

        HandleGesture(stripTL, stripBR, winSize);
    }

    private static uint Tint(Vector4 c, float a) => ImGui.ColorConvertFloat4ToU32(new Vector4(c.X, c.Y, c.Z, a));

    private static float DrawBattery(ImDrawListPtr dl, float rightX, float centerY, Vector4 tint)
    {
        // Playful fake: drains over the real day and recharges at midnight.
        var dayFrac = (float)DateTime.Now.TimeOfDay.TotalSeconds / 86400f;
        var level = 0.95f - 0.55f * dayFrac;

        var bodyW = Px(19f);
        var bodyH = Px(9f);
        var tl = new Vector2(rightX - bodyW, centerY - bodyH * 0.5f);
        var br = new Vector2(rightX, centerY + bodyH * 0.5f);
        dl.AddRect(tl, br, Tint(tint, 0.75f), Px(2.5f), ImDrawFlags.RoundCornersAll, Px(1f));
        dl.AddRectFilled(new Vector2(br.X + Px(1f), centerY - Px(2f)), new Vector2(br.X + Px(2.5f), centerY + Px(2f)),
            Tint(tint, 0.75f), Px(1f));
        var fillCol = level < 0.25f
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.35f, 0.30f, 0.95f))
            : Tint(tint, 0.9f);
        dl.AddRectFilled(tl + Px(1.6f, 1.6f),
            new Vector2(tl.X + Px(1.6f) + (bodyW - Px(3.2f)) * level, br.Y - Px(1.6f)), fillCol, Px(1.4f));

        var pct = $"{(int)(level * 100)}";
        var pctSz = ImGui.CalcTextSize(pct) * 0.68f;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.68f,
            new Vector2(tl.X - pctSz.X - Px(4f), centerY - pctSz.Y * 0.5f), Tint(tint, 0.75f), pct);
        return tl.X - pctSz.X - Px(6f);
    }

    private static float DrawSignal(ImDrawListPtr dl, float rightX, float centerY, bool connected, Vector4 tint)
    {
        var barW = Px(2.4f);
        var gap = Px(1.6f);
        var x = rightX;
        for (int i = 3; i >= 0; i--)
        {
            var h = Px(3.5f + i * 2.2f);
            x -= barW;
            var col = connected ? Tint(tint, 0.9f) : Tint(tint, i == 0 ? 0.9f : 0.25f);
            dl.AddRectFilled(new Vector2(x, centerY + Px(5f) - h), new Vector2(x + barW, centerY + Px(5f)), col, Px(1f));
            x -= gap;
        }
        return x;
    }

    private void HandleGesture(Vector2 stripTL, Vector2 stripBR, Vector2 winSize)
    {
        ImGui.SetCursorScreenPos(stripTL);
        ImGui.InvisibleButton("##osStatusStrip", stripBR - stripTL);
        if (ImGui.IsItemHovered())
        {
            SharedUiHelpers.HandOnHover();
        }

        if (ImGui.IsItemActivated())
        {
            _dragging = true;
            _dragEngaged = false;
            _dragStartY = ImGui.GetMousePos().Y;
        }
        if (_dragging)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var pull = ImGui.GetMousePos().Y - _dragStartY;
                if (pull > Px(6f))
                {
                    _dragEngaged = true;
                    _shade.DragTo(pull / (winSize.Y * 0.5f));
                }
            }
            else
            {
                _dragging = false;
                // An engaged drag must always resolve through EndDrag, whatever the final pull: deciding by
                // the release position alone left the shade's drag state stuck when a drag ended back up top.
                if (_dragEngaged)
                {
                    _shade.EndDrag();
                }
                else
                {
                    _shade.Toggle();
                }
            }
        }
    }
}
