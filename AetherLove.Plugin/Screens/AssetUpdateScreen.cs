using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Assets;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using AetherLove.Services.Media;
using AetherLove.Shared.Assets;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using static AetherLove.UI.DrawFx;

namespace AetherLove.Screens;

/// <summary>The "Processing phone update" gate: shown by the startup ladder when required packs are missing
/// (after sign-in, passphrase and setup, and it starts the download itself), and over a full-screen phone
/// when a reconnect finds the server's collection changed. Draws the shipped
/// loading art, whose painted frame near the bottom is where the progress fills in, and leaves the moment
/// nothing required is pending: back to where the user was for a live update, on down the startup ladder
/// for a boot.</summary>
public sealed class AssetUpdateScreen
{
    private const string BackgroundFile = "phone-update-bg.png";

    // Where the painted bar frame sits in the background art, as fractions of the image.
    private const float BarLeft = 0.158f;
    private const float BarRight = 0.845f;
    private const float BarTop = 0.862f;
    private const float BarBottom = 0.878f;

    private const float ProgressSmoothing = 8f;
    private const float StartHoldSeconds = 2f;
    private const float EndHoldSeconds = 3f;
    private const float FullBar = 0.995f;
    private const float TextBackingPadX = 18f;
    private const float TextBackingPadY = 8f;
    private const float TextBackingRounding = 12f;
    private static readonly Vector4 TextBacking = new(0f, 0f, 0f, 0.45f);

    private readonly AssetSyncService _assets;
    private readonly ScreenRouter _router;
    private readonly SessionBootstrapper _bootstrap;
    private readonly Os.OsShell _osShell;

    private ISharedImmediateTexture? _background;
    private bool _backgroundLoaded;
    private bool _pendingLive;
    private bool _liveReturn;
    private float _shownProgress;
    private float _elapsed;
    private float _fullFor;
    private bool _hasWork;

    public AssetUpdateScreen(AssetSyncService assets, ScreenRouter router, SessionBootstrapper bootstrap, Os.OsShell osShell)
    {
        _assets = assets;
        _router = router;
        _bootstrap = bootstrap;
        _osShell = osShell;
    }

    /// <summary>Marks the next showing as a mid-session update: on completion it returns to where the user
    /// was instead of re-running the startup ladder. Called before the navigation to this screen, so the
    /// router still points at the interrupted location.</summary>
    public void RequestLiveReturn()
    {
        _pendingLive = true;
        LiveGateReturn.Capture(_router, _osShell);
    }

    public void OnShow()
    {
        _liveReturn = _pendingLive;
        _pendingLive = false;
        _shownProgress = 0f;
        _elapsed = 0f;
        _fullFor = 0f;
        _assets.MarkShown();
        _hasWork = _assets.RequiredPending;
        if (_hasWork)
        {
            _assets.EnsureSyncing(_liveReturn ? AssetSyncReason.Reconnect : AssetSyncReason.Bootstrap);
        }
    }

    public void Draw()
    {
        var snapshot = _assets.Snapshot;
        var finished = !snapshot.RequiredPending;
        if (!_hasWork && finished)
        {
            Leave();
            return;
        }
        var dt = ImGui.GetIO().DeltaTime;
        _elapsed += dt;
        if (!finished)
        {
            _hasWork = true;
            _fullFor = 0f;
        }
        if (finished && _elapsed >= StartHoldSeconds && _shownProgress >= FullBar)
        {
            _fullFor += dt;
            if (_fullFor >= EndHoldSeconds)
            {
                Leave();
                return;
            }
        }

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var cx = pos.X + size.X * 0.5f;
        var reduceMotion = AccessibilityService.ReduceMotion;
        dl.PushClipRect(pos, pos + size, true);

        var (uv0, uv1) = DrawBackground(dl, pos, size);
        Vector2 Map(float fx, float fy) => new(
            pos.X + (fx - uv0.X) / (uv1.X - uv0.X) * size.X,
            pos.Y + (fy - uv0.Y) / (uv1.Y - uv0.Y) * size.Y);
        var barTL = Map(BarLeft, BarTop);
        var barBR = Map(BarRight, BarBottom);

        var total = snapshot.RequiredBytesTotal;
        var target = _elapsed < StartHoldSeconds ? 0f
            : finished ? 1f
            : total > 0 ? Math.Clamp((float)((double)snapshot.RequiredBytesDone / total), 0f, 1f)
            : 0f;
        _shownProgress = reduceMotion
            ? target
            : AnimationHelper.Lerp(_shownProgress, target, 1f - MathF.Exp(-dt * ProgressSmoothing));
        DrawProgressBar(dl, barTL, barBR, _shownProgress, reduceMotion);

        var white = U32(new Vector4(0.97f, 0.97f, 1f, 1f));
        var soft = U32(new Vector4(0.85f, 0.87f, 0.98f, 1f));
        var title = Loc.T("os.assets_update_title");
        var line = CurrentLine(snapshot, finished && _elapsed >= StartHoldSeconds, _elapsed < StartHoldSeconds);
        var titleY = barTL.Y - Px(74f);
        var lineY = barTL.Y - Px(38f);
        Vector2 titleSize;
        Vector2 lineSize;
        using (UiFonts.H2?.Push())
        {
            titleSize = ImGui.CalcTextSize(title);
        }
        using (UiFonts.H3?.Push())
        {
            lineSize = ImGui.CalcTextSize(line);
        }
        DrawTextBacking(dl, cx, titleY, lineY + lineSize.Y, MathF.Max(titleSize.X, lineSize.X));
        using (UiFonts.H2?.Push())
        {
            Shadowed(dl, cx, titleY, title, white);
        }
        using (UiFonts.H3?.Push())
        {
            Shadowed(dl, cx, lineY, line, soft);
        }

        if (snapshot.RequiredFailed && !finished)
        {
            DrawFailureRow(barTL, barBR);
        }
        else if (total > 0)
        {
            var culture = CultureInfo.CurrentCulture;
            var lineH = ImGui.GetTextLineHeightWithSpacing();
            var y = barBR.Y + Px(12f);
            Shadowed(dl, cx, y, Loc.T("os.assets_mb", FormatMegabytes(snapshot.RequiredBytesDone, culture), FormatMegabytes(total, culture)), soft);
            Shadowed(dl, cx, y + lineH, Loc.T("os.assets_packs", snapshot.RequiredDone, snapshot.RequiredTotal), soft);
        }

        dl.PopClipRect();
    }

    private void Leave()
    {
        if (_liveReturn)
        {
            _liveReturn = false;
            LiveGateReturn.Return(_router, _osShell);
            return;
        }
        var next = _bootstrap.ResolveNextStartupScreen();
        if (next == Screen.Home)
        {
            Os.OsBootIntro.PlayIfQueued();
        }
        _router.Navigate(next);
    }

    /// <summary>The line under the title. <paramref name="starting"/> is the opening hold, which reads as checking
    /// whatever the download is already doing, so the first thing a player sees is the phone looking.</summary>
    private static string CurrentLine(AssetSyncSnapshot snapshot, bool finished, bool starting)
    {
        if (finished)
        {
            return Loc.T("os.assets_done");
        }
        if (starting || snapshot.Phase is AssetSyncRunPhase.Checking or AssetSyncRunPhase.Idle)
        {
            return Loc.T("os.assets_checking");
        }
        if (snapshot.RequiredFailed && snapshot.Phase != AssetSyncRunPhase.Syncing)
        {
            return Loc.T("os.assets_failed");
        }
        var current = snapshot.Packs.FirstOrDefault(p => p.Required && p.State == AssetPackState.Active)
            ?? snapshot.Packs.FirstOrDefault(p => p.Required && p.State == AssetPackState.Pending);
        if (current is null)
        {
            return Loc.T("os.assets_checking");
        }
        var key = current.Phase is AssetSyncPhase.Extracting or AssetSyncPhase.Installing or AssetSyncPhase.Verifying
            ? "os.assets_installing"
            : "os.assets_current";
        return Loc.T(key, AssetPackNames.Display(current.Name));
    }

    private void DrawFailureRow(Vector2 barTL, Vector2 barBR)
    {
        var w = barBR.X - barTL.X;
        var h = Px(40f);
        var y = barBR.Y + Px(14f);
        var t = ThemeService.Current;

        ImGui.SetCursorScreenPos(new Vector2(barTL.X, y));
        ImGui.PushStyleColor(ImGuiCol.Button, t.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.AccentLight);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.AccentDark);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, h * 0.5f);
        if (Button($"{Loc.T("os.assets_retry")}##assetsRetry", new Vector2(w, h)))
        {
            _assets.RequestSync(AssetSyncReason.Manual);
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
    }

    private (Vector2 Uv0, Vector2 Uv1) DrawBackground(ImDrawListPtr dl, Vector2 pos, Vector2 size)
    {
        EnsureBackground();
        var wrap = _background?.GetWrapOrDefault();
        if (wrap is null)
        {
            var top = U32(new Vector4(0.10f, 0.08f, 0.24f, 1f));
            var bottom = U32(new Vector4(0.03f, 0.03f, 0.08f, 1f));
            dl.AddRectFilledMultiColor(pos, pos + size, top, top, bottom, bottom);
            return (Vector2.Zero, Vector2.One);
        }
        var uvs = CoverFitUvs(wrap.Width, wrap.Height, size.X, size.Y);
        dl.AddImage(wrap.Handle, pos, pos + size, uvs.Uv0, uvs.Uv1);
        return uvs;
    }

    private void EnsureBackground()
    {
        if (_backgroundLoaded)
        {
            return;
        }
        _backgroundLoaded = true;
        var path = MediaPaths.Shipped(BackgroundFile);
        if (File.Exists(path))
        {
            _background = Plugin.TextureProvider.GetFromFile(path);
        }
        else
        {
            Plugin.Log.Warning($"[AssetUpdateScreen] Background not found: {path}");
        }
    }

    /// <summary>A soft dark panel behind the title and the status line, so they read over the bright art.</summary>
    private static void DrawTextBacking(ImDrawListPtr dl, float cx, float top, float bottom, float textWidth)
    {
        var padX = Px(TextBackingPadX);
        var padY = Px(TextBackingPadY);
        var half = (textWidth * 0.5f) + padX;
        var tl = new Vector2(cx - half, top - padY);
        var br = new Vector2(cx + half, bottom + padY);
        dl.AddRectFilled(tl, br, U32(TextBacking), Px(TextBackingRounding));
    }

    private static void Shadowed(ImDrawListPtr dl, float cx, float y, string text, uint col)
    {
        var shadow = U32(new Vector4(0.02f, 0.02f, 0.08f, 0.75f));
        var offset = MathF.Max(1f, Px(1f));
        CenterText(dl, cx + offset, y + offset, text, shadow);
        CenterText(dl, cx, y, text, col);
    }
}
