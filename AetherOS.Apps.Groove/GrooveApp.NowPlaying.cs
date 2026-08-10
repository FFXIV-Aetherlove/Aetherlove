using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Config;
using Dalamud.Interface;

namespace AetherOS.Apps.Groove;

public sealed partial class GrooveApp
{
    private const float ArtSize = 168f;
    private const float TransportRadius = 27f;
    private const float SideRadius = 19f;
    private const float VolumeCacheSeconds = 1f;
    private const float VolumeSweepPhase = 0f;
    private const float ArtSweepPhase = 2.6f;
    private const float GameMasterSweepPhase = 1.3f;
    private const float GameBgmSweepPhase = 3.9f;

    private bool _seeking;
    private float _seekRatio;
    private float _cachedVolume = -1f;
    private double _volumeReadAt = -10.0;
    private string _volumeSessionId = string.Empty;

    private void DrawPlayer(OsAppContext ctx)
    {
        var winW = ImGui.GetWindowSize().X;

        if (_host.BridgeUnavailable)
        {
            DrawBridgeUnavailable(ctx, winW);
            return;
        }

        if (!_host.Ready)
        {
            ImGui.Dummy(new Vector2(0f, Px(70f)));
            var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(16f));
            LoadingSpinner.Draw(center, Px(14f), Px(3f), ImGui.GetColorU32(ThemeService.Current.Accent));
            return;
        }

        if (_host.Current is not { } session)
        {
            DrawEmptyState(ctx, winW);
            return;
        }

        if (_host.ReducedTier)
        {
            DrawReducedBanner(winW);
        }

        DrawArt(ctx, session, winW);
        DrawTitles(session, winW);
        DrawProgress(ctx, session, winW);
        DrawTransport(session, winW);
        DrawTogglePills(session, winW);
        DrawVolume(ctx, session, winW);
        DrawGameVolumes(ctx, winW);
        ImGui.Dummy(new Vector2(0f, Px(16f)));
    }

    private void DrawEmptyState(OsAppContext ctx, float winW)
    {
        var t = ThemeService.Current;
        ImGui.Dummy(new Vector2(0f, Px(52f)));
        var dl = ImGui.GetWindowDrawList();
        var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(34f));
        dl.AddCircleFilled(center, Px(34f), ImGui.GetColorU32(t.Accent with { W = 0.14f }));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Music, Px(26f), center, ImGui.GetColorU32(t.AccentLight));
        ImGui.Dummy(new Vector2(0f, Px(76f)));

        CenteredText(Loc.T("os.groove_empty_title"), winW, UiColors.Body, winW - Px(PadX) * 2f, heading: true);
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        CenteredWrapped(Loc.T("os.groove_empty_body"), winW, UiColors.Hint);
    }

    private void DrawArt(OsAppContext ctx, GrooveSession session, float winW)
    {
        var artPx = Px(ArtSize);
        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX((winW - artPx) * 0.5f);
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(artPx, artPx);
        var rounding = Px(16f);

        if (_host.Art(session.SessionId) is { } art)
        {
            dl.AddImageRounded(art, tl, br, Vector2.Zero, Vector2.One, 0xFFFFFFFFu, rounding);
        }
        else
        {
            dl.AddRectFilled(tl, br, OsDrawShared.White(0.06f), rounding);
            var shimmer = ctx.ReduceMotion ? 0.10f : 0.08f + 0.05f * MathF.Sin((float)ImGui.GetTime() * 2.2f);
            IconDraw.AddCentered(dl, FontAwesomeIcon.Music, artPx * 0.34f, (tl + br) * 0.5f,
                OsDrawShared.White(shimmer + 0.12f));
        }
        // Offset from the volume slider's sweep so the two never flash together.
        GrooveFx.Sweep(dl, tl, br, rounding, ArtSweepPhase, ctx.ReduceMotion);
        dl.AddRect(tl, br, OsDrawShared.White(0.08f), rounding, ImDrawFlags.None, Px(1f));

        ImGui.Dummy(new Vector2(0f, artPx + Px(12f)));
    }

    private void DrawTitles(GrooveSession session, float winW)
    {
        var inner = winW - Px(PadX) * 2f;
        var title = session.Title.Length > 0 ? session.Title : session.AppLabel;
        CenteredText(title, winW, UiColors.Body, inner, heading: true);

        var subtitle = session.Artist.Length > 0 && session.Album.Length > 0
            ? $"{session.Artist} · {session.Album}"
            : session.Artist.Length > 0 ? session.Artist : session.Album;
        if (subtitle.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, Px(2f)));
            CenteredText(subtitle, winW, UiColors.Hint, inner);
        }

        ImGui.Dummy(new Vector2(0f, Px(1f)));
        CenteredText(session.AppLabel, winW, UiColors.Hint with { W = 0.6f }, inner);
    }

    private void DrawProgress(OsAppContext ctx, GrooveSession session, float winW)
    {
        if (session.Duration <= TimeSpan.Zero)
        {
            ImGui.Dummy(new Vector2(0f, Px(10f)));
            return;
        }

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var barW = winW - Px(PadX) * 2f - Px(8f);
        var barH = Px(5f);
        var hitH = Px(22f);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX((winW - barW) * 0.5f);

        var hitTl = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##grooveSeek", new Vector2(barW, hitH));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        if (session.CanSeek)
        {
            if (hovered || active)
            {
                HandOnHover();
            }
            if (active)
            {
                _seeking = true;
                _seekRatio = Math.Clamp((ImGui.GetMousePos().X - hitTl.X) / barW, 0f, 1f);
            }
            else if (_seeking)
            {
                _seeking = false;
                _host.SeekTo(session.SessionId, session.Duration * _seekRatio);
            }
        }

        var shownRatio = _seeking
            ? _seekRatio
            : (float)Math.Clamp(session.PositionNow().TotalSeconds / session.Duration.TotalSeconds, 0.0, 1.0);
        var barY = hitTl.Y + (hitH - barH) * 0.5f;
        var barTl = new Vector2(hitTl.X, barY);
        dl.AddRectFilled(barTl, barTl + new Vector2(barW, barH), OsDrawShared.White(0.12f), barH * 0.5f);
        dl.AddRectFilled(barTl, barTl + new Vector2(barW * shownRatio, barH),
            ImGui.GetColorU32(t.AccentLight), barH * 0.5f);
        if (session.CanSeek && (hovered || active))
        {
            dl.AddCircleFilled(new Vector2(barTl.X + barW * shownRatio, barY + barH * 0.5f), Px(6f),
                ImGui.GetColorU32(t.AccentLight));
        }

        var shownPosition = _seeking ? session.Duration * _seekRatio : session.PositionNow();
        var left = FormatTime(shownPosition);
        var right = FormatTime(session.Duration);
        var rightSz = ImGui.CalcTextSize(right);
        dl.AddText(new Vector2(hitTl.X, hitTl.Y + hitH - Px(2f)), ImGui.GetColorU32(UiColors.Hint), left);
        dl.AddText(new Vector2(hitTl.X + barW - rightSz.X, hitTl.Y + hitH - Px(2f)),
            ImGui.GetColorU32(UiColors.Hint), right);
        ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() + Px(4f)));
    }

    private void DrawTransport(GrooveSession session, float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var mainR = Px(TransportRadius);
        var sideR = Px(SideRadius);
        var gap = Px(30f);
        var centerX = ImGui.GetWindowPos().X + winW * 0.5f;
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        var rowTop = ImGui.GetCursorScreenPos().Y;
        var centerY = rowTop + mainR;

        DrawRoundButton(dl, new Vector2(centerX - mainR - gap - sideR, centerY), sideR,
            FontAwesomeIcon.StepBackward, Px(13f), false, session.CanControl,
            Loc.T("os.groove_tt_prev"), () => _host.Previous(session.SessionId));

        DrawRoundButton(dl, new Vector2(centerX, centerY), mainR,
            session.IsPlaying ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, Px(19f), true, session.CanControl,
            Loc.T(session.IsPlaying ? "os.groove_tt_pause" : "os.groove_tt_play"),
            () => _host.TogglePlayPause(session.SessionId));

        DrawRoundButton(dl, new Vector2(centerX + mainR + gap + sideR, centerY), sideR,
            FontAwesomeIcon.StepForward, Px(13f), false, session.CanControl,
            Loc.T("os.groove_tt_next"), () => _host.Next(session.SessionId));

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, rowTop + mainR * 2f));
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    private void DrawRoundButton(ImDrawListPtr dl, Vector2 center, float radius, FontAwesomeIcon icon,
        float iconPx, bool primary, bool enabled, string tooltip, Action onClick)
    {
        ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
        var clicked = ImGui.InvisibleButton($"##groove{icon}{center.X:F0}", new Vector2(radius * 2f, radius * 2f));
        var hovered = ImGui.IsItemHovered();
        if (enabled)
        {
            HandOnHover();
            if (hovered)
            {
                ImGui.SetTooltip(tooltip);
            }
        }

        if (primary)
        {
            OsDrawShared.RoundedGradient(dl, center - new Vector2(radius, radius), center + new Vector2(radius, radius),
                radius, TileTopColor, TileBottomColor, enabled ? (hovered ? 1f : 0.94f) : 0.4f);
        }
        else
        {
            dl.AddCircleFilled(center, radius,
                OsDrawShared.White(enabled ? (hovered ? 0.14f : 0.08f) : 0.04f));
        }
        var iconColor = primary
            ? OsDrawShared.White(enabled ? 0.98f : 0.55f)
            : ImGui.GetColorU32(enabled ? UiColors.Body : UiColors.Hint with { W = 0.4f });
        IconDraw.AddCentered(dl, icon, iconPx, center, iconColor);

        if (clicked && enabled)
        {
            onClick();
        }
    }

    private void DrawTogglePills(GrooveSession session, float winW)
    {
        if (!session.CanShuffle && !session.CanRepeat)
        {
            return;
        }

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pillH = Px(28f);
        var pillW = Px(52f);
        var gap = Px(14f);
        var count = (session.CanShuffle ? 1 : 0) + (session.CanRepeat ? 1 : 0);
        var totalW = count * pillW + (count - 1) * gap;
        var x = ImGui.GetWindowPos().X + (winW - totalW) * 0.5f;
        var y = ImGui.GetCursorScreenPos().Y;

        if (session.CanShuffle)
        {
            DrawPill(dl, new Vector2(x, y), new Vector2(pillW, pillH), FontAwesomeIcon.Random,
                session.ShuffleOn, null, Loc.T("os.groove_tt_shuffle"),
                () => _host.SetShuffle(session.SessionId, !session.ShuffleOn));
            x += pillW + gap;
        }
        if (session.CanRepeat)
        {
            DrawPill(dl, new Vector2(x, y), new Vector2(pillW, pillH), FontAwesomeIcon.Redo,
                session.Repeat != GrooveRepeat.Off, session.Repeat == GrooveRepeat.One ? "1" : null,
                Loc.T("os.groove_tt_repeat"), () => _host.CycleRepeat(session.SessionId));
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, y + pillH));
        ImGui.Dummy(new Vector2(0f, Px(12f)));
    }

    private void DrawPill(ImDrawListPtr dl, Vector2 tl, Vector2 size, FontAwesomeIcon icon, bool on,
        string? cornerTag, string tooltip, Action onClick)
    {
        var t = ThemeService.Current;
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##groovePill{icon}", size);
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetTooltip(tooltip);
        }

        var fill = on
            ? ImGui.GetColorU32(t.Accent with { W = hovered ? 0.4f : 0.3f })
            : OsDrawShared.White(hovered ? 0.12f : 0.06f);
        dl.AddRectFilled(tl, tl + size, fill, size.Y * 0.5f);
        var iconColor = on ? ImGui.GetColorU32(t.AccentLight) : ImGui.GetColorU32(UiColors.Hint);
        IconDraw.AddCentered(dl, icon, Px(13f), tl + size * 0.5f, iconColor);
        if (cornerTag is not null)
        {
            dl.AddText(new Vector2(tl.X + size.X - Px(14f), tl.Y + Px(2f)), iconColor, cornerTag);
        }

        if (clicked)
        {
            onClick();
        }
    }

    private void DrawVolume(OsAppContext ctx, GrooveSession session, float winW)
    {
        if (!session.HasVolume)
        {
            return;
        }

        var now = ImGui.GetTime();
        if (_volumeSessionId != session.SessionId || now - _volumeReadAt > VolumeCacheSeconds)
        {
            _volumeSessionId = session.SessionId;
            _volumeReadAt = now;
            _cachedVolume = _host.GetVolume(session.SessionId) ?? _cachedVolume;
        }
        if (_cachedVolume < 0f)
        {
            return;
        }

        if (DrawLabelledVolume(ctx, winW, "##grooveVolume", FontAwesomeIcon.VolumeUp,
                Loc.T("os.groove_vol_app"), _cachedVolume, VolumeSweepPhase, out var updated))
        {
            _cachedVolume = updated;
            _host.SetVolume(session.SessionId, updated);
            // Hold the next poll off while the knob is under the finger, or the refresh snaps it back.
            _volumeReadAt = now;
        }
    }

    /// <summary>The game's own levels, so the music remote can duck the game without opening its options.
    /// Rendered only when the game config actually answers.</summary>
    private void DrawGameVolumes(OsAppContext ctx, float winW)
    {
        var hasMaster = GameVolume.TryGet(SystemConfigOption.SoundMaster, out var master);
        var hasBgm = GameVolume.TryGet(SystemConfigOption.SoundBgm, out var bgm);
        if (!hasMaster && !hasBgm)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        ImGui.SetCursorPosX(Px(PadX));
        var dividerTl = ImGui.GetCursorScreenPos();
        var innerW = winW - Px(PadX) * 2f;
        dl.AddLine(dividerTl, dividerTl + new Vector2(innerW, 0f), OsDrawShared.White(0.10f), Px(1f));
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(ThemeService.Current.AccentLight, Loc.T("os.groove_game_volumes"));
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        if (hasMaster && DrawLabelledVolume(ctx, winW, "##grooveGameMaster", FontAwesomeIcon.Gamepad,
                Loc.T("os.groove_vol_master"), master, GameMasterSweepPhase, out var newMaster,
                SystemConfigOption.IsSndMaster))
        {
            GameVolume.Set(SystemConfigOption.SoundMaster, newMaster);
        }
        if (hasBgm && DrawLabelledVolume(ctx, winW, "##grooveGameBgm", FontAwesomeIcon.Music,
                Loc.T("os.groove_vol_bgm"), bgm, GameBgmSweepPhase, out var newBgm,
                SystemConfigOption.IsSndBgm))
        {
            GameVolume.Set(SystemConfigOption.SoundBgm, newBgm);
        }
    }

    /// <summary>The speaker toggle that flips one of the game's own IsSnd* mute flags, right-aligned on the
    /// caption line. Returns the flag's current state so the slider can render itself muted.</summary>
    private static bool DrawMuteToggle(ImDrawListPtr dl, string id, SystemConfigOption option, float rightX,
        float lineY)
    {
        if (!GameVolume.TryGetMuted(option, out var muted))
        {
            return false;
        }
        // Sized to the caption line so the hit box can never reach down into the slider's track.
        var side = ImGui.GetTextLineHeight();
        var tl = new Vector2(rightX - side, lineY);
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton(id, new Vector2(side, side)))
        {
            GameVolume.SetMuted(option, !muted);
            muted = !muted;
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(Loc.T(muted ? "os.groove_tt_unmute" : "os.groove_tt_mute"));
        }
        var tint = muted
            ? ImGui.GetColorU32(ThemeService.Current.AccentLight)
            : ImGui.GetColorU32(UiColors.Hint with { W = hovered ? 1f : 0.7f });
        IconDraw.AddCentered(dl, muted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp, Px(13f),
            tl + new Vector2(side * 0.5f, side * 0.5f), tint);
        return muted;
    }

    /// <summary>An icon, a caption and the themed slider on its own row, with an optional mute toggle on the
    /// caption line. Returns true while dragging.</summary>
    private static bool DrawLabelledVolume(OsAppContext ctx, float winW, string id, FontAwesomeIcon icon,
        string caption, float value01, float sweepPhase, out float newValue,
        SystemConfigOption? muteOption = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var iconPx = Px(13f);
        ImGui.SetCursorPosX(Px(PadX));
        var rowTl = ImGui.GetCursorScreenPos();
        IconDraw.Add(dl, icon, iconPx, rowTl + new Vector2(0f, Px(1f)), ImGui.GetColorU32(UiColors.Hint));
        dl.AddText(rowTl + new Vector2(Px(21f), 0f), ImGui.GetColorU32(UiColors.Hint), caption);

        var innerW = winW - Px(PadX) * 2f;
        // Submitted before the slider so the toggle keeps its clicks; the slider's track spans the full width.
        var muted = muteOption is { } option
            && DrawMuteToggle(dl, $"{id}Mute", option, rowTl.X + innerW, rowTl.Y);

        var sliderTl = rowTl + new Vector2(0f, ImGui.GetTextLineHeight() + Px(3f));
        var dragging = GrooveFx.VolumeSlider(id, sliderTl, innerW, value01, sweepPhase,
            ctx.ReduceMotion, out newValue, muted,
            muteOption is null ? 0f : ImGui.GetTextLineHeight() + Px(6f));

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, rowTl.Y));
        ImGui.Dummy(new Vector2(0f, GrooveFx.SliderRowHeight() + Px(6f)));
        return dragging;
    }

    private void DrawReducedBanner(float winW)
    {
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var text = Loc.T("os.groove_reduced_banner");
        var lineH = ImGui.GetTextLineHeight();
        var cardH = lineH + Px(14f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), UiColors.WarningBoxFill, Px(10f));
        dl.AddText(new Vector2(tl.X + Px(12f), tl.Y + Px(7f)), ImGui.GetColorU32(UiColors.Body),
            TruncateToWidth(text, cardW - Px(24f)));
        ImGui.Dummy(new Vector2(0f, cardH + Px(8f)));
    }

    private void DrawBridgeUnavailable(OsAppContext ctx, float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(46f)));
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(30f));
        dl.AddCircleFilled(center, Px(30f), ImGui.GetColorU32(t.Accent with { W = 0.12f }));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Plug, Px(22f), center, ImGui.GetColorU32(t.AccentLight));
        ImGui.Dummy(new Vector2(0f, Px(68f)));

        CenteredText(Loc.T("os.groove_bridge_title"), winW, UiColors.Body, winW - Px(PadX) * 2f, heading: true);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        CenteredWrapped(Loc.T(_host.ReducedTier ? "os.groove_bridge_body" : "os.groove_unavailable_body"),
            winW, UiColors.Hint);
        ImGui.Dummy(new Vector2(0f, Px(14f)));

        var btnW = Px(150f);
        ImGui.SetCursorPosX((winW - btnW) * 0.5f);
        if (ModalUi.Button(Loc.T("os.groove_bridge_retry"), btnW))
        {
            _host.RetryBridge();
        }
    }

    /// <summary>Centred one-liner, ellipsized to <paramref name="maxW"/>. The truncation happens INSIDE the
    /// font scope: measuring a heading in the body font under-truncates and the text runs off the screen.</summary>
    private static void CenteredText(string text, float winW, Vector4 color, float maxW, bool heading = false)
    {
        if (heading)
        {
            using (UiFonts.H3?.Push())
            {
                var clipped = TruncateToWidth(text, maxW);
                ImGui.SetCursorPosX((winW - ImGui.CalcTextSize(clipped).X) * 0.5f);
                ImGui.TextColored(color, clipped);
            }
            return;
        }
        var plain = TruncateToWidth(text, maxW);
        ImGui.SetCursorPosX((winW - ImGui.CalcTextSize(plain).X) * 0.5f);
        ImGui.TextColored(color, plain);
    }

    private static void CenteredWrapped(string text, float winW, Vector4 color)
    {
        var wrapW = winW - Px(PadX) * 2.5f;
        var sz = ImGui.CalcTextSize(text, false, wrapW);
        ImGui.SetCursorPosX((winW - Math.Min(sz.X, wrapW)) * 0.5f);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    internal static string FormatTime(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }
}
