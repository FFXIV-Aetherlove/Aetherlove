using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Echo;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.EchoVidya;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;
using static AetherLove.UI.SharedUiHelpers;
using static AetherLove.UI.UiScale;

namespace AetherOS.Apps.EchoVidya.Screens;

/// <summary>Echo's first-run flow: what it is, how watching together works, the one-time runtime download, and
/// a finale. The install itself runs in the plugin, so leaving and coming back shows live progress rather than
/// starting over, and the step index survives the trip.</summary>
internal sealed class SetupScreen
{
    /// <summary>The runtime download step; the home screen sends people straight back here.</summary>
    public const int DownloadStep = 2;

    private const int TotalSteps = 4;
    private const string StepKey = "setupStep";
    private const float TopBarHeight = 34f;
    private const float NavHeight = 62f;
    private const float NavHeightWithSecondary = 96f;
    private const float ProgressSmoothing = 8f;
    private const double BytesPerMegabyte = 1024d * 1024d;

    private readonly IAppStorage _storage;
    private readonly AetherHubContext _hub;
    private readonly EchoHostInstaller _installer;
    private readonly EchoHostLocator _locator;
    private readonly IEchoHost _host;
    private readonly Action _done;
    private readonly ConfettiBurst _confetti = new();

    private volatile EchoHostManifestDto? _manifest;
    private volatile bool _manifestRequested;
    private int _step;
    private float _shownProgress;

    public SetupScreen(IAppStorage storage, AetherHubContext hub, EchoHostInstaller installer,
        EchoHostLocator locator, IEchoHost host, Action done)
    {
        _storage = storage;
        _hub = hub;
        _installer = installer;
        _locator = locator;
        _host = host;
        _done = done;
    }

    /// <summary>Resumes at the step the user last reached.</summary>
    public void OnShow() => OnShow(_storage.Get<int?>(StepKey) ?? 0);

    public void OnShow(int step)
    {
        _step = Math.Clamp(step, 0, TotalSteps - 1);
        _shownProgress = 0f;
        if (_step == TotalSteps - 1)
        {
            _confetti.Reset();
        }
        Persist();
    }

    public void Draw(OsAppContext ctx)
    {
        if (DrawProgress(_step, TotalSteps, _step > 0))
        {
            GoTo(_step - 1);
        }

        var install = _installer.State;
        if (_step == DownloadStep)
        {
            EnsureManifest();
        }

        var secondary = SecondaryLabel(install);
        var winH = ImGui.GetWindowSize().Y;
        var contentH = winH - Px(TopBarHeight) - Px(secondary is null ? NavHeight : NavHeightWithSecondary);

        ImGui.SetCursorPos(new Vector2(0f, Px(TopBarHeight)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##echoSetupContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawWelcome();
                        break;
                    case 1:
                        DrawTogether();
                        break;
                    case DownloadStep:
                        DrawDownload(ctx, install);
                        break;
                    default:
                        DrawFinale(ctx);
                        break;
                }
            }
        }
        PopScrollbarStyle();

        if (secondary is not null)
        {
            ImGui.SetCursorPos(new Vector2(0f, winH - Px(88f)));
            if (DrawSecondaryButton(secondary))
            {
                if (install.Busy)
                {
                    _host.CancelInstall();
                }
                else
                {
                    GoTo(_step + 1);
                }
            }
        }

        ImGui.SetCursorPos(new Vector2(0f, winH - Px(54f)));
        if (DrawPrimaryButton(PrimaryLabel(install), !install.Busy))
        {
            Advance(install);
        }
    }

    private static void DrawWelcome()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("echo_setup_welcome", FontAwesomeIcon.Film, Loc.T("os.echo_setup_s0_title"),
            Loc.T("os.echo_setup_s0_body"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.PlayCircle, Loc.T("os.echo_setup_s0_f1"));
        DrawFeatureRow(FontAwesomeIcon.Users, Loc.T("os.echo_setup_s0_f2"));
    }

    private static void DrawTogether()
    {
        DrawHero("echo_setup_together", FontAwesomeIcon.Users, Loc.T("os.echo_setup_s1_title"),
            Loc.T("os.echo_setup_s1_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Hashtag, Loc.T("os.echo_setup_s1_f1"));
        DrawFeatureRow(FontAwesomeIcon.ListUl, Loc.T("os.echo_setup_s1_f2"));
        DrawFeatureRow(FontAwesomeIcon.Comments, Loc.T("os.echo_setup_s1_f3"));
        DrawFeatureRow(FontAwesomeIcon.Crown, Loc.T("os.echo_setup_s1_f4"));
    }

    private void DrawDownload(OsAppContext ctx, EchoInstallState install)
    {
        DrawHero("echo_setup_runtime", FontAwesomeIcon.CloudDownloadAlt, Loc.T("os.echo_setup_s2_title"),
            Loc.T("os.echo_setup_s2_body"), 30f);

        var winW = ImGui.GetWindowSize().X;
        var sizeLine = _manifest is { } manifest
            ? Loc.T("os.echo_setup_size_known", FormatMegabytes(manifest.SizeBytes, ctx.Culture))
            : Loc.T("os.echo_setup_size_unknown");
        DrawCenteredParagraph(sizeLine, winW - Px(48f), UiColors.Subtle);
        ImGui.Dummy(new Vector2(0f, Px(14f)));

        switch (install.Phase)
        {
            case EchoInstallPhase.Downloading:
                DrawProgressBar(ctx, install.Progress);
                DrawCenteredParagraph(
                    Loc.T("os.echo_setup_progress_mb",
                        FormatMegabytes(install.BytesDone, ctx.Culture),
                        FormatMegabytes(install.BytesTotal, ctx.Culture)),
                    winW - Px(48f), UiColors.Body);
                ImGui.Dummy(new Vector2(0f, Px(10f)));
                DrawCenteredParagraph(Loc.T("os.echo_setup_resume_note"), winW - Px(48f), UiColors.Hint);
                break;
            case EchoInstallPhase.Verifying:
            case EchoInstallPhase.Extracting:
                DrawProgressBar(ctx, 1f);
                DrawCenteredParagraph(
                    Loc.T(install.Phase == EchoInstallPhase.Verifying
                        ? "os.echo_setup_phase_verifying"
                        : "os.echo_setup_phase_extracting"),
                    winW - Px(48f), UiColors.Body);
                break;
            case EchoInstallPhase.Installed:
                DrawInfoCallout(Loc.T("os.echo_setup_phase_installed"), UiColors.Success, FontAwesomeIcon.CheckCircle);
                if (_locator.InstalledVersion is { Length: > 0 } version)
                {
                    ImGui.Dummy(new Vector2(0f, Px(10f)));
                    DrawCenteredParagraph(Loc.T("os.echo_setup_version", version), winW - Px(48f), UiColors.Hint);
                }
                break;
            case EchoInstallPhase.Failed:
                DrawInfoCallout(install.FailureReason is { Length: > 0 } reason
                    ? Loc.T("os.echo_setup_failed_reason", reason)
                    : Loc.T("os.echo_setup_phase_failed"), UiColors.Danger, FontAwesomeIcon.ExclamationTriangle);
                break;
            default:
                DrawFeatureRow(FontAwesomeIcon.ShieldAlt, Loc.T("os.echo_setup_s2_f1"));
                DrawFeatureRow(FontAwesomeIcon.Redo, Loc.T("os.echo_setup_s2_f2"));
                if (_manifest is null && !_hub.IsConnected)
                {
                    ImGui.Dummy(new Vector2(0f, Px(10f)));
                    DrawInfoCallout(Loc.T("os.echo_setup_offline"), UiColors.Amber, FontAwesomeIcon.ExclamationTriangle);
                }
                break;
        }
        ImGui.Dummy(new Vector2(0f, Px(16f)));
    }

    private void DrawFinale(OsAppContext ctx)
    {
        var t = ThemeService.Current;
        var wPos = ImGui.GetWindowPos();
        var wSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();

        if (!ctx.ReduceMotion)
        {
            var glowCenter = wPos + new Vector2(wSize.X * 0.5f, wSize.Y * 0.34f);
            var glowSpan = MathF.Min(wSize.X, wSize.Y);
            for (var i = 0; i < 5; i++)
            {
                var r = glowSpan * (0.14f + i * 0.11f);
                var a = 0.08f * (1f - i * 0.18f);
                dl.AddCircleFilled(glowCenter, r, ImGui.ColorConvertFloat4ToU32(t.Accent with { W = a }), 64);
            }
        }

        ImGui.Dummy(new Vector2(0f, wSize.Y * 0.13f));
        DrawHero("echo_setup_done", FontAwesomeIcon.CheckCircle, Loc.T("os.echo_setup_s3_title"),
            Loc.T("os.echo_setup_s3_body"), 42f);

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        var ready = _host.RuntimeReady;
        DrawCenteredParagraph(Loc.T(ready ? "os.echo_setup_s3_hint" : "os.echo_setup_s3_hint_pending"),
            wSize.X - Px(48f), ready ? UiColors.Success : UiColors.Amber);

        if (!ctx.ReduceMotion)
        {
            _confetti.Draw(wPos, wPos + wSize);
        }
    }

    private void DrawProgressBar(OsAppContext ctx, float target)
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var margin = Px(24f);
        var barW = winW - margin * 2f;
        var barH = Px(10f);

        _shownProgress = ctx.ReduceMotion
            ? target
            : AnimationHelper.Lerp(_shownProgress, target,
                1f - MathF.Exp(-ImGui.GetIO().DeltaTime * ProgressSmoothing));

        var tl = new Vector2(ImGui.GetWindowPos().X + margin, ImGui.GetCursorScreenPos().Y);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(barW, barH), OsDrawShared.White(0.10f), barH * 0.5f);

        var filled = MathF.Max(barH, barW * Math.Clamp(_shownProgress, 0f, 1f));
        OsDrawShared.RoundedGradient(dl, tl, tl + new Vector2(filled, barH), barH * 0.5f, t.AccentLight, t.Accent);
        if (!ctx.ReduceMotion)
        {
            var sheenX = tl.X + filled * (0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 2.2f));
            dl.AddCircleFilled(new Vector2(sheenX, tl.Y + barH * 0.5f), barH * 0.45f, OsDrawShared.White(0.28f));
        }

        ImGui.Dummy(new Vector2(barW, barH));
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        // Drawn through the draw list: ImGui's text helpers treat the string as a printf format, so a literal
        // percent sign cannot go through them.
        var percent = $"{(int)MathF.Round(target * 100f)}%";
        var percentPos = new Vector2(tl.X + (barW - ImGui.CalcTextSize(percent).X) * 0.5f,
            ImGui.GetCursorScreenPos().Y);
        dl.AddText(percentPos, ImGui.GetColorU32(t.AccentLight), percent);
        ImGui.Dummy(new Vector2(barW, ImGui.GetTextLineHeight()));
    }

    private static bool DrawSecondaryButton(string label)
    {
        var winW = ImGui.GetWindowSize().X;
        var margin = Px(20f);
        ImGui.SetCursorPosX(margin);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.11f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Subtle);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(11f));
        var clicked = Button(label, new Vector2(winW - margin * 2f, Px(30f)));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private string PrimaryLabel(EchoInstallState install)
    {
        if (_step == TotalSteps - 1)
        {
            return Loc.T("os.echo_setup_start_btn");
        }
        if (_step != DownloadStep)
        {
            return Loc.T("onboarding.next");
        }
        return install.Phase switch
        {
            EchoInstallPhase.Downloading => Loc.T("os.echo_setup_phase_downloading"),
            EchoInstallPhase.Verifying => Loc.T("os.echo_setup_phase_verifying"),
            EchoInstallPhase.Extracting => Loc.T("os.echo_setup_phase_extracting"),
            EchoInstallPhase.Installed => Loc.T("onboarding.next"),
            EchoInstallPhase.Failed => Loc.T("os.echo_setup_retry"),
            _ => Loc.T("os.echo_setup_dl_now"),
        };
    }

    private string? SecondaryLabel(EchoInstallState install)
    {
        if (_step != DownloadStep)
        {
            return null;
        }
        if (install.Busy)
        {
            return Loc.T("os.echo_setup_cancel");
        }
        return install.Phase == EchoInstallPhase.Installed ? null : Loc.T("os.echo_setup_dl_later");
    }

    private void Advance(EchoInstallState install)
    {
        if (_step == TotalSteps - 1)
        {
            _done();
            return;
        }
        if (_step == DownloadStep && install.Phase is EchoInstallPhase.NotInstalled or EchoInstallPhase.Failed)
        {
            _shownProgress = 0f;
            _host.BeginInstall();
            return;
        }
        GoTo(_step + 1);
    }

    private void GoTo(int step)
    {
        _step = Math.Clamp(step, 0, TotalSteps - 1);
        if (_step == TotalSteps - 1)
        {
            _confetti.Reset();
        }
        Persist();
    }

    private void Persist() => _storage.Set(StepKey, _step);

    /// <summary>The manifest is fetched only to name the download size honestly; the plugin fetches its own copy
    /// when the install actually starts.</summary>
    private void EnsureManifest()
    {
        if (_manifest is not null || _manifestRequested || !_hub.IsConnected)
        {
            return;
        }
        _manifestRequested = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _manifest = await _hub.GetEchoHostManifestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[EchoSetup] Could not read the playback host manifest.");
            }
        });
    }

    private static string FormatMegabytes(long bytes, CultureInfo culture) =>
        (bytes / BytesPerMegabyte).ToString("0.#", culture);

    /// <summary>The mandatory update gate, shown by the app in place of everything else while a newer
    /// playback host is published: the same hero and progress the onboarding download shows, with a retry
    /// pill on failure and no way past. The install starts itself, so this only ever renders progress.</summary>
    public void DrawUpdateGate(OsAppContext ctx)
    {
        var install = _host.InstallState;
        var winW = ImGui.GetWindowSize().X;
        ImGui.Dummy(new Vector2(0f, Px(26f)));
        DrawHero("echo_setup_runtime", FontAwesomeIcon.CloudDownloadAlt, Loc.T("os.echo_update_title"),
            Loc.T("os.echo_update_body"), 30f);
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        switch (install.Phase)
        {
            case EchoInstallPhase.Downloading:
                DrawProgressBar(ctx, install.Progress);
                DrawCenteredParagraph(
                    Loc.T("os.echo_setup_progress_mb",
                        FormatMegabytes(install.BytesDone, ctx.Culture),
                        FormatMegabytes(install.BytesTotal, ctx.Culture)),
                    winW - Px(48f), UiColors.Body);
                break;
            case EchoInstallPhase.Verifying:
            case EchoInstallPhase.Extracting:
                DrawProgressBar(ctx, 1f);
                DrawCenteredParagraph(
                    Loc.T(install.Phase == EchoInstallPhase.Verifying
                        ? "os.echo_setup_phase_verifying"
                        : "os.echo_setup_phase_extracting"),
                    winW - Px(48f), UiColors.Body);
                break;
            case EchoInstallPhase.Failed:
                DrawInfoCallout(install.FailureReason is { Length: > 0 } reason
                    ? Loc.T("os.echo_setup_failed_reason", reason)
                    : Loc.T("os.echo_setup_phase_failed"), UiColors.Danger, FontAwesomeIcon.ExclamationTriangle);
                ImGui.Dummy(new Vector2(0f, Px(14f)));
                if (DrawPrimaryButton(Loc.T("echo.retry"), enabled: true))
                {
                    _host.BeginInstall();
                }
                break;
            default:
                DrawProgressBar(ctx, 0f);
                DrawCenteredParagraph(Loc.T("os.echo_setup_phase_starting"), winW - Px(48f), UiColors.Body);
                break;
        }
    }
}
