using System;
using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using AetherLove.Shared.Wayfinder;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Wayfinder;

/// <summary>An active challenge: the target picture, the countdown, and the selfie button that IS the
/// attempt. The position snapshot is taken plugin-side at the selfie moment; this screen only ever sees the
/// verdict band that comes back.</summary>
internal sealed class ChallengeScreen
{
    private const float PadX = 16f;

    private static string CacheDir =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "WayfinderCache");

    private readonly IWayfinderHost _host;
    private readonly WayfinderHistory _history;
    private readonly Action _backToHome;
    private readonly EntranceAnimation _entrance = new();
    private readonly ConfettiBurst _confetti = new();
    private readonly ConcurrentQueue<Action> _uiActions = new();

    private WayfinderAssignmentDto? _assignment;
    private ISharedImmediateTexture? _imageTex;
    private DateTimeOffset _expiresAt;
    private int _attempts;
    private WayfinderVerdict? _verdict;
    private double _verdictAt;
    private volatile bool _submitting;
    private volatile string? _error;
    private bool _expired;
    private bool _viewerOpen;
    private bool _giveUpOpen;
    private FoundState? _found;

    private sealed record FoundState(int Attempts, int Seconds, string? SelfiePath);

    public ChallengeScreen(IWayfinderHost host, WayfinderHistory history, Action backToHome)
    {
        _host = host;
        _history = history;
        _backToHome = backToHome;
    }

    public void OnShow(WayfinderAssignmentDto assignment)
    {
        _entrance.Arm();
        _assignment = assignment;
        _imageTex = AvatarDiskCache.Store(CacheDir, $"ch_{assignment.ChallengeId:N}", assignment.ImageBytes);
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(assignment.RemainingSeconds);
        _attempts = assignment.AttemptCount;
        _verdict = assignment.LastVerdict is { } v ? (WayfinderVerdict)v : null;
        _verdictAt = double.MinValue;
        _submitting = false;
        _error = null;
        _expired = false;
        _viewerOpen = false;
        _giveUpOpen = false;
        _found = null;
    }

    public void Draw(OsAppContext ctx)
    {
        while (_uiActions.TryDequeue(out var action))
        {
            action();
        }
        if (_assignment is not { } assignment)
        {
            _backToHome();
            return;
        }

        _entrance.BeginFrame();
        DrawImageViewer();
        var winW = ImGui.GetWindowSize().X;
        var winH = ImGui.GetWindowSize().Y;

        if (_found is { } found)
        {
            DrawFound(ctx, assignment, found, winW, winH);
            _entrance.EndFrame();
            return;
        }

        var remaining = _expiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _expired = true;
        }

        ImGui.Dummy(new Vector2(0f, Px(10f)));
        DrawHeader(assignment, winW);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawPicture(winW);
        DrawTitle(assignment, winW);
        DrawTimerRow(remaining, winW);

        if (_expired)
        {
            DrawExpired(winW);
            _entrance.EndFrame();
            return;
        }

        DrawVerdictBanner(winW);

        if (_error is { } error)
        {
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Danger, error);
            ImGui.PopTextWrapPos();
        }

        DrawHint(winW);

        ImGui.SetCursorPos(new Vector2(0f, winH - Px(88f)));
        ImGui.SetCursorPosX(Px(PadX));
        if (_submitting)
        {
            var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(18f));
            LoadingSpinner.Draw(center, Px(12f), Px(3f), ImGui.GetColorU32(ThemeService.Current.Accent));
        }
        else if (ModalUi.Button(Loc.T("os.wf_selfie"), winW - Px(PadX) * 2f))
        {
            TakeSelfie(ctx);
        }

        DrawGiveUpLink(winW, winH);
        DrawGiveUpConfirm(winW, winH);

        _entrance.EndFrame();
    }

    private void DrawHeader(WayfinderAssignmentDto assignment, float winW)
    {
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetCursorPosX(Px(PadX));
        var backH = ImGui.GetTextLineHeight() + Px(10f);
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##wfBack", new Vector2(backH, backH));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        dl.AddCircleFilled(tl + new Vector2(backH * 0.5f, backH * 0.5f), backH * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.07f)));
        var arrowPx = ImGui.GetFontSize() * 0.7f;
        var arrowSz = IconDraw.Measure(FontAwesomeIcon.ArrowLeft, arrowPx);
        IconDraw.Add(dl, FontAwesomeIcon.ArrowLeft, arrowPx,
            tl + new Vector2((backH - arrowSz.X) * 0.5f, (backH - arrowSz.Y) * 0.5f),
            ImGui.GetColorU32(UiColors.Body));
        if (clicked)
        {
            _backToHome();
        }

        ImGui.SameLine();
        var badge = Px(26f);
        ExpansionBadge.Draw(dl, new Vector2(tl.X + winW - Px(PadX) * 2f - badge, tl.Y + (backH - badge) * 0.5f),
            badge, assignment.Expansion);
        ImGui.NewLine();
    }

    /// <summary>The wrapped challenge title, under the picture where long names have room.</summary>
    private static void DrawTitle(WayfinderAssignmentDto assignment, float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.PushTextWrapPos(winW - Px(PadX));
            ImGui.TextColored(UiColors.Body, assignment.ChallengeName);
            ImGui.PopTextWrapPos();
        }
    }

    private void DrawPicture(float winW)
    {
        var cardW = winW - Px(PadX) * 2f;
        var cardH = cardW * 0.62f;
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        var wrap = _imageTex?.GetWrapOrDefault();
        var clicked = false;
        var hovered = false;
        if (wrap is not null)
        {
            clicked = ImGui.InvisibleButton("##wfPicture", new Vector2(cardW, cardH));
            HandOnHover();
            hovered = ImGui.IsItemHovered();
            ImGui.SetCursorScreenPos(tl);

            // Cover-fit the picture into the rounded frame without distortion.
            var texW = (float)wrap.Width;
            var texH = (float)wrap.Height;
            var scale = Math.Max(cardW / texW, cardH / texH);
            var visW = cardW / (texW * scale);
            var visH = cardH / (texH * scale);
            var uv0 = new Vector2((1f - visW) * 0.5f, (1f - visH) * 0.5f);
            var uv1 = new Vector2(1f - uv0.X, 1f - uv0.Y);
            dl.AddImageRounded(wrap.Handle, tl, tl + new Vector2(cardW, cardH), uv0, uv1, 0xFFFFFFFFu, Px(16f));

            var expand = Px(24f);
            var expandTl = new Vector2(tl.X + cardW - expand - Px(8f), tl.Y + cardH - expand - Px(8f));
            dl.AddCircleFilled(expandTl + new Vector2(expand * 0.5f, expand * 0.5f), expand * 0.5f,
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, hovered ? 0.65f : 0.45f)));
            var expandIconPx = expand * 0.5f;
            var expandSz = IconDraw.Measure(FontAwesomeIcon.ExpandAlt, expandIconPx);
            IconDraw.Add(dl, FontAwesomeIcon.ExpandAlt, expandIconPx,
                expandTl + new Vector2((expand - expandSz.X) * 0.5f, (expand - expandSz.Y) * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 1f : 0.8f)));
            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("os.wf_view_full"));
            }
        }
        else
        {
            dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(16f));
            var iconSz = IconDraw.Measure(FontAwesomeIcon.Image, Px(30f));
            IconDraw.Add(dl, FontAwesomeIcon.Image, Px(30f),
                tl + new Vector2((cardW - iconSz.X) * 0.5f, (cardH - iconSz.Y) * 0.5f),
                ImGui.GetColorU32(UiColors.Hint));
        }
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f)), Px(16f),
            ImDrawFlags.None, Px(1.2f));
        ImGui.Dummy(new Vector2(0f, cardH + Px(10f)));
        if (clicked)
        {
            _viewerOpen = true;
        }
    }

    /// <summary>The full-size picture opens in its own free-floating, resizable ImGui window outside the
    /// phone bezel (a nested top-level Begin), so it can be sized freely or dragged to a second monitor.</summary>
    private void DrawImageViewer()
    {
        if (!_viewerOpen || _imageTex?.GetWrapOrDefault() is not { } wrap)
        {
            return;
        }
        var imgW = MathF.Max(1f, wrap.Width);
        var imgH = MathF.Max(1f, wrap.Height);

        var firstW = MathF.Min(imgW, Px(720f));
        ImGui.SetNextWindowSize(
            new Vector2(firstW + Px(12f), firstW * (imgH / imgW) + ImGui.GetFrameHeightWithSpacing() + Px(12f)),
            ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(Px(220f), Px(160f)),
            new Vector2(float.MaxValue, float.MaxValue));

        var open = true;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(6f), Px(6f)));
        if (ImGui.Begin($"{Loc.T("os.wf_image_viewer")}##wfImageViewer", ref open,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var avail = ImGui.GetContentRegionAvail();
            var scale = MathF.Min(avail.X / imgW, avail.Y / imgH);
            if (scale <= 0f || float.IsInfinity(scale))
            {
                scale = 1f;
            }
            var size = new Vector2(imgW * scale, imgH * scale);
            ImGui.SetCursorPos(ImGui.GetCursorPos()
                + new Vector2(MathF.Max(0f, (avail.X - size.X) * 0.5f), MathF.Max(0f, (avail.Y - size.Y) * 0.5f)));
            ImGui.Image(wrap.Handle, size);
            if (ImGui.IsWindowFocused() && ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                open = false;
            }
        }
        ImGui.End();
        ImGui.PopStyleVar();

        if (!open)
        {
            _viewerOpen = false;
        }
    }

    private void DrawTimerRow(TimeSpan remaining, float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var low = remaining < TimeSpan.FromMinutes(10);

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var clockPx = ImGui.GetFontSize() * 0.85f;
        var clockSz = IconDraw.Measure(FontAwesomeIcon.HourglassHalf, clockPx);
        var pulse = low && !AccessibilityService.ReduceMotion
            ? 0.7f + 0.3f * (float)Math.Sin(ImGui.GetTime() * 4.0)
            : 1f;
        var timeColor = low ? UiColors.WarningAccent with { W = pulse } : t.AccentLight;
        IconDraw.Add(dl, FontAwesomeIcon.HourglassHalf, clockPx, tl + new Vector2(0f, Px(1f)),
            ImGui.GetColorU32(timeColor));
        var timeLabel = HomeScreen.FormatSpan(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining);
        dl.AddText(new Vector2(tl.X + clockSz.X + Px(8f), tl.Y), ImGui.GetColorU32(timeColor), timeLabel);

        var attemptsLabel = Loc.T("os.wf_attempts", _attempts);
        var attemptsSz = ImGui.CalcTextSize(attemptsLabel);
        dl.AddText(new Vector2(tl.X + winW - Px(PadX) * 2f - attemptsSz.X, tl.Y),
            ImGui.GetColorU32(UiColors.Hint), attemptsLabel);

        ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() + Px(10f)));
    }

    /// <summary>The warmer/colder band from the last attempt, sliding in on arrival.</summary>
    private void DrawVerdictBanner(float winW)
    {
        if (_verdict is not { } verdict || verdict == WayfinderVerdict.Found)
        {
            return;
        }

        var (text, color) = verdict switch
        {
            WayfinderVerdict.WrongZone => (Loc.T("os.wf_v_wrongzone"), new Vector4(0.62f, 0.68f, 0.95f, 1f)),
            WayfinderVerdict.Far => (Loc.T("os.wf_v_far"), new Vector4(0.45f, 0.70f, 0.95f, 1f)),
            WayfinderVerdict.Close => (Loc.T("os.wf_v_close"), new Vector4(0.98f, 0.72f, 0.35f, 1f)),
            _ => (Loc.T("os.wf_v_veryclose"), new Vector4(0.98f, 0.45f, 0.30f, 1f)),
        };

        var slide = AccessibilityService.ReduceMotion
            ? 1f
            : (float)Math.Clamp((ImGui.GetTime() - _verdictAt) / 0.25, 0.0, 1.0);
        var eased = 1f - (1f - slide) * (1f - slide);

        var cardW = winW - Px(PadX) * 2f;
        var lineH = ImGui.GetTextLineHeight();
        var textSz = ImGui.CalcTextSize(text, false, cardW - Px(48f));
        var cardH = textSz.Y + Px(18f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos() + new Vector2((1f - eased) * Px(18f), 0f);
        var dl = ImGui.GetWindowDrawList();
        var alpha = eased;
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(color with { W = 0.14f * alpha }), Px(12f));
        dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(color with { W = 0.55f * alpha }), Px(12f),
            ImDrawFlags.None, Px(1.2f));
        var icon = verdict switch
        {
            WayfinderVerdict.WrongZone => FontAwesomeIcon.Map,
            WayfinderVerdict.Far => FontAwesomeIcon.Snowflake,
            WayfinderVerdict.Close => FontAwesomeIcon.Sun,
            _ => FontAwesomeIcon.Fire,
        };
        var iconPx = lineH * 0.9f;
        var iconSz = IconDraw.Measure(icon, iconPx);
        IconDraw.Add(dl, icon, iconPx, new Vector2(tl.X + Px(12f), tl.Y + (cardH - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(color with { W = alpha }));
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(tl.X + Px(12f) + iconSz.X + Px(10f), tl.Y + Px(9f)),
            ImGui.GetColorU32(UiColors.Body with { W = alpha }), text, cardW - Px(48f));

        ImGui.Dummy(new Vector2(0f, cardH + Px(8f)));
    }

    private void DrawGiveUpLink(float winW, float winH)
    {
        if (_submitting || _giveUpOpen)
        {
            return;
        }
        var label = Loc.T("os.wf_give_up");
        var sz = ImGui.CalcTextSize(label);
        ImGui.SetCursorPos(new Vector2((winW - sz.X) * 0.5f, winH - Px(32f)));
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##wfGiveUp", sz);
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        ImGui.GetWindowDrawList().AddText(tl,
            ImGui.GetColorU32(hovered ? UiColors.Body : UiColors.Hint), label);
        if (clicked)
        {
            _giveUpOpen = true;
        }
    }

    /// <summary>In-phone confirm: dims only the app content and centres the panel, never the whole screen.</summary>
    private void DrawGiveUpConfirm(float winW, float winH)
    {
        if (!_giveUpOpen)
        {
            return;
        }
        var t = ThemeService.Current;
        var winPos = ImGui.GetWindowPos();
        var dl = ImGui.GetWindowDrawList();

        var panelW = winW - Px(PadX) * 2f;
        var bodyWrap = panelW - Px(32f);
        var bodySz = ImGui.CalcTextSize(Loc.T("os.wf_give_up_body"), false, bodyWrap);
        var panelH = Px(150f) + bodySz.Y;
        var panelTL = new Vector2(winPos.X + Px(PadX), winPos.Y + (winH - panelH) * 0.5f);

        // Painted first so the buttons, appended after, land on top of the scrim.
        dl.AddRectFilled(winPos, winPos + new Vector2(winW, winH), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.62f)));
        dl.AddRectFilled(panelTL, panelTL + new Vector2(panelW, panelH),
            ImGui.GetColorU32(new Vector4(0.12f, 0.11f, 0.15f, 0.99f)), Px(18f));
        dl.AddRect(panelTL, panelTL + new Vector2(panelW, panelH), ImGui.GetColorU32(t.Accent with { W = 0.35f }),
            Px(18f), ImDrawFlags.None, Px(1.2f));

        using (UiFonts.H3?.Push())
        {
            var title = Loc.T("os.wf_give_up_title");
            var titleSz = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(panelTL.X + (panelW - titleSz.X) * 0.5f, panelTL.Y + Px(18f)),
                ImGui.GetColorU32(UiColors.Body), title);
        }
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(panelTL.X + Px(16f), panelTL.Y + Px(52f)),
            ImGui.GetColorU32(UiColors.Hint), Loc.T("os.wf_give_up_body"), bodyWrap);

        // Submitted before the scrim, so the first-submitted-wins rule gives them the click.
        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + Px(16f), panelTL.Y + panelH - Px(94f)));
        var confirm = ModalUi.Button(Loc.T("os.wf_give_up_confirm"), panelW - Px(32f));
        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + Px(16f), panelTL.Y + panelH - Px(48f)));
        var cancel = ModalUi.Button(Loc.T("os.wf_give_up_cancel"), panelW - Px(32f));

        ImGui.SetCursorScreenPos(winPos);
        var scrim = ImGui.InvisibleButton("##wfGiveUpScrim", new Vector2(winW, winH));

        var outside = scrim && !ImGui.IsMouseHoveringRect(panelTL, panelTL + new Vector2(panelW, panelH));
        if (cancel || outside)
        {
            _giveUpOpen = false;
        }
        else if (confirm)
        {
            _giveUpOpen = false;
            GiveUp();
        }
    }

    private void GiveUp()
    {
        _submitting = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.AbandonAsync().ConfigureAwait(false);
                _uiActions.Enqueue(_backToHome);
            }
            catch (Exception ex)
            {
                var message = HubErrorText.Localize(ex);
                _uiActions.Enqueue(() => _error = message);
            }
            finally
            {
                _submitting = false;
            }
        });
    }

    private void DrawHint(float winW)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.wf_challenge_hint"));
        ImGui.PopTextWrapPos();
    }

    private void DrawExpired(float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(UiColors.WarningAccent, Loc.T("os.wf_expired_title"));
        }
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Hint, Loc.T("os.wf_expired_body"));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        ImGui.SetCursorPosX(Px(PadX));
        if (ModalUi.Button(Loc.T("os.wf_back"), winW - Px(PadX) * 2f))
        {
            _backToHome();
        }
    }

    /// <summary>The Found celebration: a green wash, expanding shockwaves, a rotating starburst behind a
    /// popped check badge, sparkles, staggered copy, and confetti over the whole screen.</summary>
    private void DrawFound(OsAppContext ctx, WayfinderAssignmentDto assignment, FoundState found, float winW, float winH)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var winPos = ImGui.GetWindowPos();
        var still = AccessibilityService.ReduceMotion;
        var age = still ? 99f : (float)Math.Max(0.0, ImGui.GetTime() - _verdictAt);
        var green = UiColors.LiveGreen;
        var badgeY = Px(118f);
        var center = winPos + new Vector2(winW * 0.5f, badgeY);

        var wash = ImGui.GetColorU32(green with { W = 0.14f });
        dl.AddRectFilledMultiColor(winPos, winPos + new Vector2(winW, winH * 0.58f), wash, wash, 0u, 0u);

        DrawStarburst(dl, center, t, age, still);
        for (var i = 0; i < 5; i++)
        {
            var glowR = Px(56f) + i * Px(26f);
            dl.AddCircleFilled(center, glowR, ImGui.GetColorU32(t.Accent with { W = 0.075f * (1f - i * 0.17f) }), 64);
        }
        DrawShockwaves(dl, center, green, age);

        var pop = still ? 1f : Overshoot(age / 0.45f);
        var radius = Px(42f) * pop;
        var breathe = still ? 0f : MathF.Sin(age * 2.4f) * 0.5f + 0.5f;
        dl.AddCircleFilled(center, radius + Px(6f) + breathe * Px(4f),
            ImGui.GetColorU32(green with { W = 0.10f + breathe * 0.06f }), 64);
        dl.AddCircleFilled(center, radius, ImGui.GetColorU32(green with { W = 0.22f }), 64);
        dl.AddCircle(center, radius, ImGui.GetColorU32(green), 64, Px(2f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Check, radius * 0.9f, center, ImGui.GetColorU32(green));
        DrawSparkles(dl, center, radius, t, age, still);

        ImGui.Dummy(new Vector2(0f, badgeY + Px(52f)));

        var titleFade = Fade(age, 0.15f, still);
        using (UiFonts.H1?.Push())
        {
            var title = Loc.T("os.wf_found_title");
            var sz = ImGui.CalcTextSize(title);
            ImGui.SetCursorPos(new Vector2((winW - sz.X) * 0.5f, ImGui.GetCursorPosY() + (1f - titleFade) * Px(14f)));
            ImGui.TextColored(t.AccentLight with { W = titleFade }, title);
        }

        var bodyFade = Fade(age, 0.3f, still);
        var body = Loc.T("os.wf_found_body", assignment.ChallengeName,
            HomeScreen.FormatSpan(TimeSpan.FromSeconds(found.Seconds)), found.Attempts);
        var wrapW = winW - Px(PadX) * 3f;
        var bodySz = ImGui.CalcTextSize(body, false, wrapW);
        ImGui.SetCursorPos(new Vector2((winW - Math.Min(bodySz.X, wrapW)) * 0.5f,
            ImGui.GetCursorPosY() + Px(4f) + (1f - bodyFade) * Px(12f)));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
        ImGui.TextColored(UiColors.Body with { W = bodyFade }, body);
        ImGui.PopTextWrapPos();

        if (found.SelfiePath is { } selfie)
        {
            var shotFade = Fade(age, 0.45f, still);
            ImGui.Dummy(new Vector2(0f, Px(16f)));
            var thumb = Px(104f) * (still ? 1f : 0.9f + shotFade * 0.1f);
            var thumbTl = new Vector2(winPos.X + (winW - thumb) * 0.5f, ImGui.GetCursorScreenPos().Y);
            var thumbBr = thumbTl + new Vector2(thumb, thumb);
            if (ctx.Capabilities.Textures.Get(selfie) is { } tex)
            {
                dl.AddCircleFilled(thumbTl + new Vector2(thumb * 0.5f, thumb * 0.5f), thumb * (0.62f + breathe * 0.03f),
                    ImGui.GetColorU32(green with { W = 0.10f * shotFade }), 48);
                dl.AddImageRounded(tex, thumbTl, thumbBr, Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, shotFade)), Px(16f));
                dl.AddRect(thumbTl, thumbBr, ImGui.GetColorU32(green with { W = 0.75f * shotFade }),
                    Px(16f), ImDrawFlags.None, Px(1.6f));
                ImGui.Dummy(new Vector2(0f, thumb + Px(10f)));
            }
            var note = Loc.T("os.wf_found_saved");
            var noteSz = ImGui.CalcTextSize(note);
            ImGui.SetCursorPosX((winW - noteSz.X) * 0.5f);
            ImGui.TextColored(UiColors.Hint with { W = shotFade }, note);
        }

        ImGui.SetCursorPos(new Vector2(Px(PadX), winH - Px(58f)));
        if (ModalUi.Button(Loc.T("os.wf_done"), winW - Px(PadX) * 2f))
        {
            _backToHome();
        }

        if (!still)
        {
            _confetti.Draw(winPos, winPos + new Vector2(winW, winH));
        }
    }

    /// <summary>Rings that race outward from the badge and fade, three of them staggered half a second apart.</summary>
    private static void DrawShockwaves(ImDrawListPtr dl, Vector2 center, Vector4 color, float age)
    {
        for (var i = 0; i < 3; i++)
        {
            var life = age - i * 0.28f;
            if (life is < 0f or > 1.1f)
            {
                continue;
            }
            var progress = life / 1.1f;
            var radius = Px(42f) + progress * Px(120f);
            dl.AddCircle(center, radius, ImGui.GetColorU32(color with { W = 0.45f * (1f - progress) }), 64,
                Px(2.5f) * (1f - progress) + Px(0.5f));
        }
    }

    /// <summary>Slowly turning rays behind the badge, alternating long and short.</summary>
    private static void DrawStarburst(ImDrawListPtr dl, Vector2 center, ThemeDefinition t, float age, bool still)
    {
        const int rays = 14;
        var spin = still ? 0f : age * 0.22f;
        var reveal = still ? 1f : Math.Clamp(age / 0.6f, 0f, 1f);
        for (var i = 0; i < rays; i++)
        {
            var angle = spin + i * MathF.Tau / rays;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var inner = center + dir * Px(50f);
            var outer = center + dir * (Px(50f) + (i % 2 == 0 ? Px(78f) : Px(44f)) * reveal);
            dl.AddLine(inner, outer, ImGui.GetColorU32(t.AccentLight with { W = 0.16f * reveal }), Px(2.5f));
        }
    }

    /// <summary>Four twinkling points around the badge, each on its own beat.</summary>
    private static void DrawSparkles(ImDrawListPtr dl, Vector2 center, float radius, ThemeDefinition t, float age, bool still)
    {
        for (var i = 0; i < 4; i++)
        {
            var angle = MathF.PI * 0.25f + i * MathF.Tau / 4f;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var at = center + dir * (radius + Px(20f));
            var twinkle = still ? 0.7f : MathF.Max(0f, MathF.Sin(age * 3.2f + i * 1.7f));
            var arm = Px(4f) + twinkle * Px(5f);
            var color = ImGui.GetColorU32(t.AccentLight with { W = 0.25f + twinkle * 0.6f });
            dl.AddLine(at - new Vector2(arm, 0f), at + new Vector2(arm, 0f), color, Px(1.6f));
            dl.AddLine(at - new Vector2(0f, arm), at + new Vector2(0f, arm), color, Px(1.6f));
        }
    }

    /// <summary>Scale curve that overshoots past 1 before settling, for the badge pop.</summary>
    private static float Overshoot(float progress)
    {
        if (progress >= 1f)
        {
            return 1f;
        }
        var p = Math.Clamp(progress, 0f, 1f);
        return p < 0.7f ? p / 0.7f * 1.14f : 1.14f - (p - 0.7f) / 0.3f * 0.14f;
    }

    private static float Fade(float age, float delay, bool still) =>
        still ? 1f : Math.Clamp((age - delay) / 0.35f, 0f, 1f);

    private void TakeSelfie(OsAppContext ctx)
    {
        if (_assignment is not { } assignment || _submitting)
        {
            return;
        }

        ctx.Capabilities.Camera.Capture(new CameraRequest(1f, 128, FreeForm: true),
            shot => _uiActions.Enqueue(() => Submit(assignment, shot.Path, shot.Crop)));
    }

    private void Submit(WayfinderAssignmentDto assignment, string shotPath, Vector4 crop)
    {
        _submitting = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _host.SubmitAttemptAsync(assignment.AssignmentId).ConfigureAwait(false);
                _uiActions.Enqueue(() => ApplyResult(result, shotPath, crop));
            }
            catch (Exception ex)
            {
                var message = HubErrorText.Localize(ex);
                var expired = ex.Message.Contains(AetherLove.Shared.HubErrors.WayfinderExpired, StringComparison.Ordinal);
                _uiActions.Enqueue(() =>
                {
                    if (expired)
                    {
                        _expired = true;
                    }
                    else
                    {
                        _error = message;
                    }
                    DeleteQuietly(shotPath);
                });
            }
            finally
            {
                _submitting = false;
            }
        });
    }

    private void ApplyResult(WayfinderSubmitResultDto result, string shotPath, Vector4 crop)
    {
        if (_assignment is not { } assignment)
        {
            DeleteQuietly(shotPath);
            return;
        }

        _attempts = result.AttemptCount;
        _verdict = (WayfinderVerdict)result.Verdict;
        _verdictAt = ImGui.GetTime();

        if ((WayfinderVerdict)result.Verdict == WayfinderVerdict.Found)
        {
            _confetti.Reset();
            var saved = _host.SaveSelfie(shotPath, crop);
            var seconds = result.SecondsToFind ?? 0;
            _found = new FoundState(result.AttemptCount, seconds, saved);
            _history.Add(new WayfinderFoundRecord(
                assignment.ChallengeId,
                assignment.ChallengeName,
                assignment.Expansion,
                DateTime.UtcNow,
                result.AttemptCount,
                seconds,
                saved));
        }
        DeleteQuietly(shotPath);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
