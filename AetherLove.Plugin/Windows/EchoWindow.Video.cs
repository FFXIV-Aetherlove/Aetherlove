using System;
using System.Numerics;
using AetherLove.Config;
using AetherLove.Services;
using AetherLove.Services.Echo;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Windows;

/// <summary>The stage and its transport: an aspect-fit frame on a near-black plate, with a control bar that
/// fades in while the pointer is over the picture. In a room every control goes through the hub so the change
/// comes back as a push and everyone moves together; solo drives the playback host directly.</summary>
public sealed partial class EchoWindow
{
    private const float TransportH = 58f;
    private const float TransportInset = 10f;
    private const float TransportPadX = 10f;
    private const float SeekRowH = 16f;
    private const float ControlRowH = 28f;
    private const float TrackIdleH = 4f;
    private const float TrackHoverH = 6f;
    private const float KnobIdleR = 4.5f;
    private const float KnobHoverR = 7f;
    private const float KnobAnimSpeed = 8f;
    private const float VolumeSliderW = 78f;
    private const float TransportHoldSeconds = 2.2f;
    private const float TransportFadeInSeconds = 0.12f;
    private const float TransportFadeOutSeconds = 0.35f;

    private const uint TransportFill = 0xE6101010u;

    private float _transportAlpha;
    private float _transportIdle;
    private float _seekKnobAnim;
    private float _volumeKnobAnim;
    private bool _scrubbing;
    private float _scrubFraction;
    private bool _volumeDragging;
    private bool _settingsOpen;
    private float? _mutedFrom;

    private void DrawStage(ThemeDefinition t, Vector2 origin, Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + size, StageFill, Px(StageRounding));

        TrackStageSize(size);

        if (ResolveStageNotice() is { } notice)
        {
            DrawStageNotice(t, origin, size, notice);
            return;
        }

        if (_texture is { } wrap && _frameWidth > 0 && _frameHeight > 0)
        {
            DrawFrame(dl, wrap.Handle, origin, size);
        }
        else
        {
            DrawStagePlaceholder(origin, size);
        }

        var mouse = ImGui.GetMousePos();
        var hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
                      && mouse.X >= origin.X && mouse.X <= origin.X + size.X
                      && mouse.Y >= origin.Y && mouse.Y <= origin.Y + size.Y;
        DrawTransport(t, origin, size, hovered);
        DrawPlaybackSettings(t, origin, size);
    }

    /// <summary>The gear's panel, in its own layer so it lands over the stage placeholder's child rather
    /// than under it. Submitted after the transport, with the dismiss scrim last.</summary>
    private void DrawPlaybackSettings(ThemeDefinition t, Vector2 stageTL, Vector2 stageSize)
    {
        if (!_settingsOpen)
        {
            return;
        }

        ImGui.SetCursorScreenPos(stageTL);
        using var layer = ImRaii.Child("##echoSettingsLayer", stageSize, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var owner = _state.Room is { } room && room.OwnerAccountId == _myAccountId;
        var rows = owner ? 4 : 3;
        var panelW = MathF.Min(stageSize.X - Px(24f), Px(300f));
        var rowH = Px(30f);
        var pad = Px(12f);
        var panelH = Px(26f) + (rows * rowH) + pad;
        var panelTL = new Vector2(
            stageTL.X + stageSize.X - Px(TransportInset) - panelW,
            stageTL.Y + stageSize.Y - Px(TransportInset + TransportH + 8f) - panelH);
        var panelBR = panelTL + new Vector2(panelW, panelH);
        dl.AddRectFilled(panelTL, panelBR, TransportFill, Px(10f));
        dl.AddRect(panelTL, panelBR, t.AccentWithAlpha(0.35f), Px(10f), ImDrawFlags.None, 1f);

        var y = panelTL.Y + pad;
        dl.AddText(new Vector2(panelTL.X + pad, y), 0xFFDDDDDDu, Loc.T("echo.settings_quality"));
        y += ImGui.GetTextLineHeight() + Px(6f);

        var choices = new[]
        {
            (EchoRenderQuality.MatchStage, Loc.T("echo.quality_auto")),
            (EchoRenderQuality.Hd720, Loc.T("echo.quality_720")),
            (EchoRenderQuality.Hd1080, Loc.T("echo.quality_1080")),
        };
        var gap = Px(6f);
        var pillW = (panelW - (pad * 2f) - (gap * (choices.Length - 1))) / choices.Length;
        for (var i = 0; i < choices.Length; i++)
        {
            var (value, label) = choices[i];
            var pillTL = new Vector2(panelTL.X + pad + (i * (pillW + gap)), y);
            if (DrawSettingPill(t, dl, $"##echoQuality{i}", pillTL, new Vector2(pillW, rowH - Px(6f)),
                    label, _config.Echo.Quality == value))
            {
                _config.Echo.Quality = value;
                _config.Save();
                // Force the settle timer to re-fire so the browser repaints at the new size.
                _pendingStageSize = Vector2.Zero;
            }
        }
        y += rowH;

        if (DrawSettingToggle(t, dl, "##echoCaptions", new Vector2(panelTL.X + pad, y),
                panelW - (pad * 2f), rowH, Loc.T("echo.settings_captions"), _config.Echo.Captions))
        {
            _config.Echo.Captions = !_config.Echo.Captions;
            _config.Save();
            _host.SetCaptions(_config.Echo.Captions);
        }
        y += rowH;

        if (owner && DrawSettingToggle(t, dl, "##echoAutoNext", new Vector2(panelTL.X + pad, y),
                panelW - (pad * 2f), rowH, Loc.T("echo.settings_autoplay"), _config.Echo.AutoPlayNext))
        {
            _config.Echo.AutoPlayNext = !_config.Echo.AutoPlayNext;
            _config.Save();
        }

        // Last, so every control above wins the click; anywhere else closes the panel.
        ImGui.SetCursorScreenPos(stageTL);
        if (ImGui.InvisibleButton("##echoSettingsScrim", stageSize))
        {
            var mouse = ImGui.GetMousePos();
            var inside = mouse.X >= panelTL.X && mouse.X <= panelBR.X
                         && mouse.Y >= panelTL.Y && mouse.Y <= panelBR.Y;
            if (!inside)
            {
                _settingsOpen = false;
            }
        }
    }

    private static bool DrawSettingPill(
        ThemeDefinition t, ImDrawListPtr dl, string id, Vector2 tl, Vector2 size, string label, bool on)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        var fill = on ? t.AccentWithAlpha(0.55f) : hovered ? 0x33FFFFFFu : 0x1AFFFFFFu;
        dl.AddRectFilled(tl, tl + size, fill, size.Y * 0.5f);
        var textSize = ImGui.CalcTextSize(label);
        dl.AddText(tl + (size - textSize) * 0.5f, on ? 0xFFFFFFFFu : 0xFFCCCCCCu, label);
        return clicked;
    }

    private static bool DrawSettingToggle(
        ThemeDefinition t, ImDrawListPtr dl, string id, Vector2 tl, float width, float rowH, string label, bool on)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, rowH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        var knobH = rowH - Px(12f);
        var trackW = knobH * 1.9f;
        var trackTL = new Vector2(tl.X + width - trackW, tl.Y + (rowH - knobH) * 0.5f);
        dl.AddRectFilled(trackTL, trackTL + new Vector2(trackW, knobH),
            on ? t.AccentWithAlpha(0.75f) : 0x33FFFFFFu, knobH * 0.5f);
        var knobX = on ? trackTL.X + trackW - (knobH * 0.5f) : trackTL.X + (knobH * 0.5f);
        dl.AddCircleFilled(new Vector2(knobX, trackTL.Y + (knobH * 0.5f)), knobH * 0.38f, 0xFFFFFFFFu, 20);
        dl.AddText(new Vector2(tl.X, tl.Y + (rowH - ImGui.GetTextLineHeight()) * 0.5f),
            hovered ? 0xFFFFFFFFu : 0xFFDDDDDDu, label);
        return clicked;
    }

    private void DrawFrame(ImDrawListPtr dl, ImTextureID handle, Vector2 origin, Vector2 size)
    {
        var scale = MathF.Min(size.X / _frameWidth, size.Y / _frameHeight);
        var drawn = new Vector2(_frameWidth, _frameHeight) * MathF.Max(0.01f, scale);
        var tl = origin + (size - drawn) * 0.5f;
        dl.AddImageRounded(handle, tl, tl + drawn, Vector2.Zero, Vector2.One, 0xFFFFFFFFu,
            Px(StageRounding), ImDrawFlags.RoundCornersAll);
    }

    private void DrawStagePlaceholder(Vector2 origin, Vector2 size)
    {
        var inRoom = _state.CurrentRoomId is not null;
        var queueEmpty = inRoom && _state.CurrentEntry is null;
        var idle = !inRoom && _soloVideoId is null;

        // NoInputs, or this full-stage layer would swallow the hover the transport fades in on.
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##echoStagePlaceholder", size, false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs);
        if (!layer)
        {
            return;
        }
        if (queueEmpty || idle)
        {
            DrawCenteredHint(size, queueEmpty ? Loc.T("echo.queue_empty") : Loc.T("echo.solo_idle"));
            return;
        }
        LoadingIndicator.Draw(Loc.T("echo.buffering"));
    }

    private static void DrawCenteredHint(Vector2 size, string text)
    {
        var textSize = ImGui.CalcTextSize(text);
        ImGui.SetCursorPos((size - textSize) * 0.5f);
        ImGui.TextColored(UiColors.Muted, text);
    }

    /// <summary>Tells the browser what to repaint at, once the target has held still. Every change re-lays
    /// the page out, so following a resize drag frame by frame would stutter the video.</summary>
    private void TrackStageSize(Vector2 size)
    {
        _stageSize = new Vector2(
            Math.Clamp(size.X, 1f, MaxFrameWidth),
            Math.Clamp(size.Y, 1f, MaxFrameHeight));

        var target = RenderSize();
        if (Vector2.Distance(target, _pendingStageSize) > 1f)
        {
            _pendingStageSize = target;
            _resizeSettle = ResizeSettleSeconds;
            return;
        }
        if (_resizeSettle <= 0f)
        {
            return;
        }
        _resizeSettle -= ImGui.GetIO().DeltaTime;
        if (_resizeSettle <= 0f)
        {
            _host.SetSize((int)MathF.Round(target.X), (int)MathF.Round(target.Y));
        }
    }

    /// <summary>What the browser paints at. YouTube reads its stream quality off the player's own viewport,
    /// so following a small stage is exactly what pinned playback to a low resolution; a fixed size costs the
    /// same per-frame copy regardless of how small the window is, and the frame is scaled down on draw.</summary>
    private Vector2 RenderSize() => _config.Echo.Quality switch
    {
        EchoRenderQuality.Hd720 => new Vector2(Hd720Width, Hd720Height),
        EchoRenderQuality.Hd1080 => new Vector2(MaxFrameWidth, MaxFrameHeight),
        _ => _stageSize,
    };

    private void DrawTransport(ThemeDefinition t, Vector2 stageTL, Vector2 stageSize, bool stageHovered)
    {
        var dt = ImGui.GetIO().DeltaTime;
        if (stageHovered || _scrubbing || _volumeDragging)
        {
            _transportIdle = 0f;
        }
        else
        {
            _transportIdle += dt;
        }

        var target = _transportIdle < TransportHoldSeconds ? 1f : 0f;
        if (AccessibilityService.ReduceMotion)
        {
            _transportAlpha = target;
        }
        else
        {
            var step = dt / (target > _transportAlpha ? TransportFadeInSeconds : TransportFadeOutSeconds);
            _transportAlpha = Math.Clamp(_transportAlpha + (target > _transportAlpha ? step : -step), 0f, 1f);
        }
        if (_transportAlpha <= 0.02f)
        {
            return;
        }

        var alpha = _transportAlpha;
        var dl = ImGui.GetWindowDrawList();
        var barW = stageSize.X - Px(TransportInset) * 2f;
        var barH = Px(TransportH);
        var barTL = new Vector2(stageTL.X + Px(TransportInset), stageTL.Y + stageSize.Y - Px(TransportInset) - barH);
        var barBR = barTL + new Vector2(barW, barH);
        dl.AddRectFilled(barTL, barBR, Fade(TransportFill, alpha), Px(10f));
        dl.AddRect(barTL, barBR, Fade(t.AccentWithAlpha(0.30f), alpha), Px(10f), ImDrawFlags.None, 1f);

        var host = _host.LastState;
        var duration = host?.Duration ?? 0d;
        var position = CurrentPositionSeconds();
        var canControl = CanControl();
        var tip = canControl ? null : Loc.T("echo.host_only_tip");

        DrawSeekBar(t, new Vector2(barTL.X + Px(TransportPadX), barTL.Y + Px(6f)),
            barW - Px(TransportPadX) * 2f, position, duration, canControl, alpha);

        var rowY = barTL.Y + Px(6f + SeekRowH + 2f);
        var button = Px(ControlRowH);
        var x = barTL.X + Px(TransportPadX);

        var playing = _state.Room?.Playback.IsPlaying ?? host?.Playing ?? false;
        if (DrawIconButton("##echoPlayPause", new Vector2(x, rowY), button,
                playing ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, 0xFFFFFFFFu,
                tip ?? Loc.T(playing ? "echo.pause" : "echo.play"), canControl, alpha))
        {
            TogglePlayback();
        }
        x += button + Px(8f);

        var clock = $"{FormatTime(position)} / {FormatTime(duration)}";
        var clockSize = ImGui.CalcTextSize(clock);
        dl.AddText(new Vector2(x, rowY + (button - clockSize.Y) * 0.5f),
            Fade(0xFFDDDDDDu, alpha), clock);

        var right = barTL.X + barW - Px(TransportPadX);
        if (_state.CurrentRoomId is not null)
        {
            right -= button;
            if (DrawIconButton("##echoSkip", new Vector2(right, rowY), button, FontAwesomeIcon.StepForward,
                    0xFFFFFFFFu, tip ?? Loc.T("echo.skip_tip"), canControl, alpha))
            {
                SkipNext();
            }
            right -= Px(10f);
        }

        right -= Px(VolumeSliderW);
        DrawVolumeSlider(t, new Vector2(right, rowY), Px(VolumeSliderW), button, alpha);
        right -= Px(4f) + button;
        var volume = _config.Echo.Volume;
        if (DrawIconButton("##echoMute", new Vector2(right, rowY), button,
                volume <= 0.001f ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp, 0xFFFFFFFFu,
                Loc.T(volume <= 0.001f ? "echo.unmute" : "echo.mute"), true, alpha))
        {
            ToggleMute();
        }

        right -= Px(4f) + button;
        if (DrawIconButton("##echoSettings", new Vector2(right, rowY), button, FontAwesomeIcon.Cog,
                0xFFFFFFFFu, Loc.T("echo.settings_tip"), true, alpha))
        {
            _settingsOpen = !_settingsOpen;
        }
    }

    private void DrawSeekBar(ThemeDefinition t, Vector2 rowTL, float width, double position, double duration,
        bool canControl, float alpha)
    {
        var dl = ImGui.GetWindowDrawList();
        var rowH = Px(SeekRowH);
        var centerY = rowTL.Y + rowH * 0.5f;
        var seekable = duration > 0.5d && canControl;

        var hovered = false;
        double? preview = null;
        if (seekable)
        {
            ImGui.SetCursorScreenPos(rowTL);
            ImGui.InvisibleButton("##echoSeek", new Vector2(MathF.Max(1f, width), rowH));
            hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
            }
            var fraction = Math.Clamp((ImGui.GetMousePos().X - rowTL.X) / MathF.Max(1f, width), 0f, 1f);
            if (ImGui.IsItemActive())
            {
                _scrubbing = true;
                _scrubFraction = fraction;
            }
            if (ImGui.IsItemDeactivated() && _scrubbing)
            {
                _scrubbing = false;
                CommitSeek(_scrubFraction * duration);
            }
            if (hovered && !_scrubbing)
            {
                preview = fraction * duration;
            }
        }
        else
        {
            _scrubbing = false;
        }

        var grown = hovered || _scrubbing;
        if (AccessibilityService.ReduceMotion)
        {
            _seekKnobAnim = grown ? 1f : 0f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _seekKnobAnim, ImGui.GetIO().DeltaTime, KnobAnimSpeed, grown);
        }

        var trackH = AnimationHelper.Lerp(Px(TrackIdleH), Px(TrackHoverH), _seekKnobAnim);
        var trackTL = new Vector2(rowTL.X, centerY - trackH * 0.5f);
        var trackBR = new Vector2(rowTL.X + width, centerY + trackH * 0.5f);
        dl.AddRectFilled(trackTL, trackBR, Fade(0x59FFFFFFu, alpha), trackH * 0.5f);

        var progress = _scrubbing
            ? _scrubFraction
            : (duration > 0.5d ? (float)Math.Clamp(position / duration, 0d, 1d) : 0f);
        var fillX = rowTL.X + width * progress;
        dl.AddRectFilled(trackTL, new Vector2(fillX, trackBR.Y), Fade(t.AccentU32, alpha), trackH * 0.5f);

        if (!seekable)
        {
            return;
        }
        var knobR = AnimationHelper.Lerp(Px(KnobIdleR), Px(KnobHoverR), _seekKnobAnim);
        dl.AddCircleFilled(new Vector2(fillX, centerY), knobR, Fade(0xFFFFFFFFu, alpha), 24);
        if (preview is { } previewSeconds)
        {
            DrawSeekPreview(dl, rowTL, width, centerY, previewSeconds, alpha);
        }
    }

    private static void DrawSeekPreview(ImDrawListPtr dl, Vector2 rowTL, float width, float centerY,
        double seconds, float alpha)
    {
        var x = Math.Clamp(ImGui.GetMousePos().X, rowTL.X, rowTL.X + width);
        dl.AddLine(new Vector2(x, centerY - Px(7f)), new Vector2(x, centerY + Px(7f)), Fade(0x99FFFFFFu, alpha), 1f);

        var label = FormatTime(seconds);
        var labelSize = ImGui.CalcTextSize(label);
        var pad = Px(5f);
        var chipW = labelSize.X + pad * 2f;
        var chipX = Math.Clamp(x - chipW * 0.5f, rowTL.X, rowTL.X + width - chipW);
        var chipTL = new Vector2(chipX, centerY - Px(12f) - labelSize.Y - pad * 2f);
        var chipBR = chipTL + new Vector2(chipW, labelSize.Y + pad * 2f);
        dl.AddRectFilled(chipTL, chipBR, Fade(0xF0202020u, alpha), Px(4f));
        dl.AddText(chipTL + new Vector2(pad, pad), Fade(0xFFFFFFFFu, alpha), label);
    }

    private void DrawVolumeSlider(ThemeDefinition t, Vector2 tl, float width, float height, float alpha)
    {
        var dl = ImGui.GetWindowDrawList();
        var centerY = tl.Y + height * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y));
        ImGui.InvisibleButton("##echoVolume", new Vector2(MathF.Max(1f, width), height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        if (ImGui.IsItemActive())
        {
            _volumeDragging = true;
            SetVolume(Math.Clamp((ImGui.GetMousePos().X - tl.X) / MathF.Max(1f, width), 0f, 1f));
        }
        if (ImGui.IsItemDeactivated() && _volumeDragging)
        {
            _volumeDragging = false;
            _config.Save();
        }

        var grown = hovered || _volumeDragging;
        if (AccessibilityService.ReduceMotion)
        {
            _volumeKnobAnim = grown ? 1f : 0f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _volumeKnobAnim, ImGui.GetIO().DeltaTime, KnobAnimSpeed, grown);
        }

        var trackH = AnimationHelper.Lerp(Px(TrackIdleH), Px(TrackHoverH), _volumeKnobAnim);
        var trackTL = new Vector2(tl.X, centerY - trackH * 0.5f);
        var trackBR = new Vector2(tl.X + width, centerY + trackH * 0.5f);
        dl.AddRectFilled(trackTL, trackBR, Fade(0x59FFFFFFu, alpha), trackH * 0.5f);

        var fillX = tl.X + width * Math.Clamp(_config.Echo.Volume, 0f, 1f);
        dl.AddRectFilled(trackTL, new Vector2(fillX, trackBR.Y), Fade(t.AccentU32, alpha), trackH * 0.5f);
        var knobR = AnimationHelper.Lerp(Px(KnobIdleR), Px(KnobHoverR), _volumeKnobAnim);
        dl.AddCircleFilled(new Vector2(fillX, centerY), knobR, Fade(0xFFFFFFFFu, alpha), 24);
    }

    /// <summary>What the transport should read right now: the local player's own clock once it is playing,
    /// and the room's extrapolated position while it is still catching up.</summary>
    private double CurrentPositionSeconds()
    {
        if (_host.LastState is { Ready: true } host && host.Time > 0d)
        {
            return host.Time;
        }
        return _state.Playback is { } playback
            ? EchoSyncEngine.ExpectedPosition(playback, DateTimeOffset.UtcNow)
            : 0d;
    }

    private void TogglePlayback()
    {
        if (_state.Room is { } room)
        {
            var playing = room.Playback.IsPlaying;
            var position = CurrentPositionSeconds();
            RunHub(() => _hub.SetEchoPlaybackAsync(room.Id, !playing, position));
            return;
        }
        if (_host.LastState is { Playing: true })
        {
            _host.Pause();
        }
        else
        {
            _host.Play();
        }
    }

    private void CommitSeek(double seconds)
    {
        if (_state.Room is { } room)
        {
            RunHub(() => _hub.SetEchoPlaybackAsync(room.Id, room.Playback.IsPlaying, seconds));
            return;
        }
        _host.Seek(seconds);
    }

    /// <summary>The owner's auto-advance. Only their client acts on the end of a video, so the toggle means
    /// what it says: with it off the queue waits for a deliberate skip rather than another member's client
    /// advancing it out from under them.</summary>
    private bool ShouldAutoAdvance()
    {
        if (_state.Room is not { } room)
        {
            return false;
        }
        return room.OwnerAccountId == _myAccountId && _config.Echo.AutoPlayNext;
    }

    private void SkipNext()
    {
        if (_state.CurrentRoomId is not { } roomId || _state.CurrentEntry is not { } entry)
        {
            return;
        }
        RunHub(() => _hub.AdvanceEchoPlaylistAsync(roomId, entry.Id, false));
    }

    private void SetVolume(float value)
    {
        var clamped = Math.Clamp(value, 0f, 1f);
        _mutedFrom = null;
        _config.Echo.Volume = clamped;
        _host.SetVolume(clamped);
    }

    private void ToggleMute()
    {
        if (_config.Echo.Volume > 0.001f)
        {
            var previous = _config.Echo.Volume;
            SetVolume(0f);
            _mutedFrom = previous;
        }
        else
        {
            SetVolume(_mutedFrom ?? 1f);
        }
        _config.Save();
    }
}
