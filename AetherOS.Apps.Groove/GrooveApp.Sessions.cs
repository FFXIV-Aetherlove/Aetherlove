using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Groove;

public sealed partial class GrooveApp
{
    private static readonly Vector4 PanelFill = new(0.11f, 0.10f, 0.13f, 1f);

    /// <summary>The source picker, an in-phone overlay: dim the app content, centre a panel listing
    /// Automatic plus every live session. Panel controls are submitted before the scrim so they win
    /// clicks; the scrim closes on tap-outside.</summary>
    private void DrawSourcesOverlay(OsAppContext ctx)
    {
        var origin = ImGui.GetWindowPos();
        var avail = ImGui.GetWindowSize();
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##grooveSourcesOverlay", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, OsDrawShared.Black(0.72f));

        var t = ThemeService.Current;
        var sessions = _host.Sessions;
        var rowH = Px(52f);
        var headerH = Px(44f);
        var panelW = avail.X - Px(30f);
        var panelH = headerH + (sessions.Count + 1) * rowH + Px(18f);
        panelH = MathF.Min(panelH, avail.Y - Px(60f));
        var panelTl = origin + new Vector2((avail.X - panelW) * 0.5f, (avail.Y - panelH) * 0.5f);

        dl.AddRectFilled(panelTl, panelTl + new Vector2(panelW, panelH),
            ImGui.GetColorU32(PanelFill), Px(16f));
        dl.AddRect(panelTl, panelTl + new Vector2(panelW, panelH), OsDrawShared.White(0.10f), Px(16f),
            ImDrawFlags.None, Px(1f));

        using (UiFonts.H3?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                panelTl + new Vector2(Px(16f), Px(13f)), ImGui.GetColorU32(UiColors.Body),
                Loc.T("os.groove_sources_title"));
        }

        var y = panelTl.Y + headerH;
        var pinned = _host.PinnedSessionId;
        if (DrawSourceRow(dl, new Vector2(panelTl.X, y), panelW, rowH, FontAwesomeIcon.Magic,
                Loc.T("os.groove_sources_auto"), Loc.T("os.groove_sources_auto_hint"), pinned is null,
                isPlaying: false))
        {
            _host.PinnedSessionId = null;
            _sourcesOpen = false;
        }
        y += rowH;

        foreach (var session in sessions)
        {
            var subtitle = session.Title.Length > 0 ? session.Title : Loc.T("os.groove_empty_title");
            if (DrawSourceRow(dl, new Vector2(panelTl.X, y), panelW, rowH, FontAwesomeIcon.Music,
                    session.AppLabel, subtitle, pinned == session.SessionId, session.IsPlaying))
            {
                _host.PinnedSessionId = session.SessionId;
                _sourcesOpen = false;
            }
            y += rowH;
            if (y > panelTl.Y + panelH - rowH)
            {
                break;
            }
        }

        // Full-area scrim, last so every panel control wins its click first.
        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##grooveSourcesScrim", avail))
        {
            var mouse = ImGui.GetMousePos();
            var inPanel = mouse.X >= panelTl.X && mouse.X <= panelTl.X + panelW
                && mouse.Y >= panelTl.Y && mouse.Y <= panelTl.Y + panelH;
            if (!inPanel)
            {
                _sourcesOpen = false;
            }
        }
    }

    private bool DrawSourceRow(ImDrawListPtr dl, Vector2 tl, float panelW, float rowH, FontAwesomeIcon icon,
        string title, string subtitle, bool selected, bool isPlaying)
    {
        var t = ThemeService.Current;
        var pad = Px(14f);
        ImGui.SetCursorScreenPos(tl + new Vector2(pad * 0.5f, 0f));
        var clicked = ImGui.InvisibleButton($"##grooveSource{title}{tl.Y:F0}", new Vector2(panelW - pad, rowH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            dl.AddRectFilled(tl + new Vector2(pad * 0.5f, Px(2f)),
                tl + new Vector2(panelW - pad * 0.5f, rowH - Px(2f)), OsDrawShared.White(0.06f), Px(10f));
        }

        var chipR = Px(15f);
        var chipC = tl + new Vector2(pad + chipR, rowH * 0.5f);
        dl.AddCircleFilled(chipC, chipR, ImGui.GetColorU32(t.Accent with { W = selected ? 0.32f : 0.14f }));
        IconDraw.AddCentered(dl, icon, Px(13f), chipC, ImGui.GetColorU32(t.AccentLight));

        var textX = chipC.X + chipR + Px(11f);
        var lineH = ImGui.GetTextLineHeight();
        dl.AddText(new Vector2(textX, tl.Y + rowH * 0.5f - lineH - Px(1f)),
            ImGui.GetColorU32(UiColors.Body), TruncateToWidth(title, panelW - textX + tl.X - Px(60f)));
        dl.AddText(new Vector2(textX, tl.Y + rowH * 0.5f + Px(1f)),
            ImGui.GetColorU32(UiColors.Hint), TruncateToWidth(subtitle, panelW - textX + tl.X - Px(60f)));

        if (selected)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Check, Px(13f),
                new Vector2(tl.X + panelW - pad - Px(10f), tl.Y + rowH * 0.5f),
                ImGui.GetColorU32(t.AccentLight));
        }
        else if (isPlaying)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.VolumeUp, Px(12f),
                new Vector2(tl.X + panelW - pad - Px(10f), tl.Y + rowH * 0.5f),
                ImGui.GetColorU32(UiColors.Hint));
        }
        return clicked;
    }
}
