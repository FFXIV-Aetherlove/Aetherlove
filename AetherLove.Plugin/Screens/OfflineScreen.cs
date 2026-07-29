using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using AetherLove.Services.Signal;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using static AetherLove.UI.DrawFx;

namespace AetherLove.Screens;

/// <summary>Blocking screen shown while the hub connection is down; keeps retrying and clears once restored.</summary>
public sealed class OfflineScreen
{
    private const float DiscordGraceSeconds = 120f;

    /// <summary>Automatic reconnect gives up after roughly 30s, so nudge a fresh attempt on this cadence.</summary>
    private const float RetryIntervalSeconds = 5f;

    private readonly AetherSignalService _signal;
    private readonly SessionBootstrapper _bootstrap;
    private readonly ScreenRouter _router;
    private readonly MaintenanceNoticeService _maintenance;

    private float _elapsed;
    private float _retryTimer;
    private bool _startupMode;
    private bool _bootstrapRetryInFlight;

    public OfflineScreen(AetherSignalService signal, SessionBootstrapper bootstrap, ScreenRouter router,
                         MaintenanceNoticeService maintenance)
    {
        _signal = signal;
        _bootstrap = bootstrap;
        _router = router;
        _maintenance = maintenance;
    }

    public void OnShow()
    {
        _elapsed = 0f;
        _retryTimer = RetryIntervalSeconds;
        // Never connected this session = a startup failure needing a full bootstrap re-run; otherwise a
        // mid-session drop that only needs the hub back.
        _startupMode = _bootstrap.LastConnection is null;
    }

    public void Draw()
    {
        var dt = (float)ImGui.GetIO().DeltaTime;
        _elapsed += dt;
        _maintenance.Refresh();
        MaybeAutoRetry(dt);

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var cx = pos.X + size.X * 0.5f;

        dl.PushClipRect(pos, pos + size, true);

        DrawSeveredSignal(dl, pos, size, _elapsed);

        var bandTop = pos.Y + size.Y * 0.58f;
        var clear = U32(new Vector4(0.03f, 0.03f, 0.06f, 0f));
        var solid = U32(new Vector4(0.02f, 0.02f, 0.05f, 0.92f));
        dl.AddRectFilledMultiColor(
            new Vector2(pos.X, bandTop - Px(36f)), new Vector2(pos.X + size.X, pos.Y + size.Y),
            clear, clear, solid, solid);

        var titleBottom = bandTop + Px(34f);
        using (UiFonts.H2?.Push())
        {
            var lineH = ImGui.GetTextLineHeightWithSpacing();
            var lines = WrapLines(Loc.T("common.offline_title"), size.X - Px(40f));
            var col = U32(new Vector4(0.96f, 0.58f, 0.58f, 1f));
            for (var i = 0; i < lines.Count; i++)
            {
                CenterText(dl, cx, bandTop + i * lineH, lines[i], col);
            }
            titleBottom = bandTop + (lines.Count - 1) * lineH + Px(34f);
        }

        using (UiFonts.H3?.Push())
        {
            var lineH = ImGui.GetTextLineHeightWithSpacing();
            var notice = _maintenance.Notice;
            var body = string.IsNullOrEmpty(notice)
                ? Loc.T("common.offline_body")
                : $"{Loc.T("common.offline_maintenance")} {notice}";
            var lines = WrapLines(body, size.X - Px(56f));
            var col = U32(new Vector4(0.82f, 0.82f, 0.90f, 1f));
            for (var i = 0; i < lines.Count; i++)
            {
                CenterText(dl, cx, titleBottom + i * lineH, lines[i], col);
            }
        }

        dl.PopClipRect();

        DrawStatus(pos, size, cx);
    }

    private void MaybeAutoRetry(float dt)
    {
        if (_startupMode)
        {
            MaybeRetryBootstrap(dt);
            return;
        }

        if (_signal.State != SignalConnectionState.Disconnected)
        {
            _retryTimer = RetryIntervalSeconds;
            return;
        }
        _retryTimer -= dt;
        if (_retryTimer > 0f)
        {
            return;
        }
        _retryTimer = RetryIntervalSeconds;
        _ = Task.Run(async () =>
        {
            try { await _signal.EnsureConnectedAsync().ConfigureAwait(false); }
            catch (Exception ex) { Plugin.Log.Warning(ex, "[OfflineScreen] Auto-retry connect failed."); }
        });
    }

    private void MaybeRetryBootstrap(float dt)
    {
        if (_bootstrapRetryInFlight)
        {
            return;
        }
        _retryTimer -= dt;
        if (_retryTimer > 0f)
        {
            return;
        }
        _retryTimer = RetryIntervalSeconds;
        _bootstrapRetryInFlight = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _bootstrap.RunAsync().ConfigureAwait(false);
                if (result is not (SessionBootstrapResult.ServerUnreachable or SessionBootstrapResult.Pending))
                {
                    _bootstrap.ApplyDeferredStartupRouting();
                }
            }
            catch (Exception ex) { Plugin.Log.Warning(ex, "[OfflineScreen] Bootstrap retry failed."); }
            finally { _bootstrapRetryInFlight = false; }
        });
    }

    private void DrawStatus(Vector2 pos, Vector2 size, float cx)
    {
        var th = ThemeService.Current;

        if (_elapsed < DiscordGraceSeconds)
        {
            var y = pos.Y + size.Y - Px(64f);
            LoadingSpinner.Draw(new Vector2(cx - Px(52f), y + Px(8f)), Px(8f), Px(2.5f),
                ImGui.ColorConvertFloat4ToU32(th.AccentLight));
            ImGui.SetCursorScreenPos(new Vector2(cx - Px(36f), y));
            ImGui.TextColored(th.AccentLight, Loc.T("common.offline_reconnecting"));
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var msgY = pos.Y + size.Y - Px(108f);
        using (UiFonts.H3?.Push())
        {
            var lines = WrapLines(Loc.T("common.offline_taking_long"), size.X - Px(56f));
            var lineH = ImGui.GetTextLineHeightWithSpacing();
            var col = U32(Rgba(th.AccentLight, 1f));
            for (var i = 0; i < lines.Count; i++)
            {
                CenterText(dl, cx, msgY + i * lineH, lines[i], col);
            }
        }

        var btnW = Px(200f);
        ImGui.SetCursorScreenPos(new Vector2(cx - btnW * 0.5f, pos.Y + size.Y - Px(52f)));
        DrawDiscordButton($"{Loc.T("common.offline_join_discord")}##offlineDiscord", new Vector2(btnW, Px(34f)));
    }

    private static void DrawSeveredSignal(ImDrawListPtr dl, Vector2 pos, Vector2 size, float t)
    {
        var th = ThemeService.Current;
        var reduce = AccessibilityService.ReduceMotion;
        var center = new Vector2(pos.X + size.X * 0.5f, pos.Y + size.Y * 0.36f);

        var topU = U32(new Vector4(th.AccentDark.X * 0.40f, th.AccentDark.Y * 0.40f, th.AccentDark.Z * 0.50f, 1f));
        var botU = U32(new Vector4(0.03f, 0.03f, 0.06f, 1f));
        dl.AddRectFilledMultiColor(pos, pos + size, topU, topU, botU, botU);

        var gridCol = U32(Rgba(th.Accent, 0.06f));
        var step = Px(34f);
        for (var x = pos.X + step; x < pos.X + size.X; x += step)
        {
            dl.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + size.Y), gridCol, 1f);
        }
        for (var y = pos.Y + step; y < pos.Y + size.Y; y += step)
        {
            dl.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + size.X, y), gridCol, 1f);
        }

        var maxR = Px(130f);
        for (var i = 0; i < 3; i++)
        {
            var phase = reduce ? 0.45f : (t * 0.5f + i / 3f) % 1f;
            var r = Px(24f) + phase * maxR;
            dl.AddCircle(center, r, U32(Rgba(th.AccentLight, (1f - phase) * 0.5f)), 64, Px(2f));
        }

        var pulse = reduce ? 0.6f : 0.5f + 0.5f * MathF.Sin(t * 2.2f);
        for (var i = 5; i >= 1; i--)
        {
            var f = i / 5f;
            dl.AddCircleFilled(center, Px(46f) * (1f + f),
                U32(Rgba(th.Accent, 0.05f * (1f - f) * (0.6f + 0.4f * pulse))), 48);
        }

        dl.AddCircleFilled(center, Px(40f), U32(new Vector4(0.10f, 0.09f, 0.16f, 1f)), 48);
        dl.AddCircle(center, Px(40f), U32(Rgba(th.AccentLight, 0.85f)), 48, Px(2f));

        var shake = reduce ? 0f : MathF.Sin(t * 18f) * Px(1.4f);
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var render = ImGui.GetFontSize() * 2.4f;
        ImGui.PopFont();
        IconDraw.AddCentered(dl, FontAwesomeIcon.Plug, render,
            new Vector2(center.X + shake, center.Y), U32(new Vector4(0.95f, 0.45f, 0.45f, 1f)));
    }

    private static List<string> WrapLines(string text, float wrap)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var probe = line.Length == 0 ? word : line + " " + word;
                if (ImGui.CalcTextSize(probe).X > wrap && line.Length > 0)
                {
                    result.Add(line);
                    line = word;
                }
                else
                {
                    line = probe;
                }
            }
            result.Add(line);
        }
        return result;
    }
}
