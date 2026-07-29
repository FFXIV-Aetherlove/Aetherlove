using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Crypto;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Passphrase prompt for unwrapping the server-stored key bundle on a new device.</summary>
public sealed class PassphraseUnlockScreen
{
    private readonly ScreenRouter _router;
    private readonly SessionBootstrapper _bootstrap;
    private readonly AetherHubContext _hub;
    private readonly AccountUnlockService _unlock;
    private readonly KeyStorageService _keys;
    private readonly TokenService _tokens;

    private readonly PassphraseResetFlow _resetFlow;

    private KeyBundleDto? _bundle;
    private string _passphrase = string.Empty;
    private volatile string? _error;
    private volatile bool _fetching;
    private volatile bool _unlocking;
    private bool _showPassphrase;

    private bool _resetMode;
    private string _resetPass = string.Empty;
    private string _resetPass2 = string.Empty;
    private volatile string? _resetError;
    private volatile bool _resetting;

    public PassphraseUnlockScreen(
        ScreenRouter router,
        SessionBootstrapper bootstrap,
        AetherHubContext hub,
        AccountUnlockService unlock,
        KeyStorageService keys,
        TokenService tokens,
        PassphraseResetFlow resetFlow)
    {
        _router = router;
        _bootstrap = bootstrap;
        _hub = hub;
        _unlock = unlock;
        _keys = keys;
        _tokens = tokens;
        _resetFlow = resetFlow;
    }

    /// <summary>Armed by another screen (the recovery gate) to land directly on the reset panel.</summary>
    public bool OpenInResetMode { get; set; }

    public void OnShow()
    {
        _passphrase = string.Empty;
        _error = null;
        _bundle = null;
        _resetMode = OpenInResetMode;
        OpenInResetMode = false;
        _resetPass = string.Empty;
        _resetPass2 = string.Empty;
        _resetError = null;
        StartFetch();
    }

    private void StartFetch()
    {
        _fetching = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _bundle = await _hub.GetMyKeyBundleAsync(CancellationToken.None).ConfigureAwait(false);
                if (_bundle is null)
                {
                    _error = Loc.T("common.passphrase_bundle_load_failed");
                }
            }
            catch (Exception ex)
            {
                _error = Loc.T("common.server_unreachable_detail", HubErrorText.Localize(ex));
                Plugin.Log.Warning(ex, "[PassphraseUnlock] GetMyKeyBundleAsync failed.");
            }
            finally
            {
                _fetching = false;
            }
        });
    }

    public void Draw()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var scrollH = ImGui.GetContentRegionAvail().Y;
        var PadX = Px(16f);

        PushScrollbarStyle();

        using (var scroll = ImRaii.Child("##passphraseUnlock", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();

            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            ImGui.Spacing();

            if (_resetMode)
            {
                DrawResetPanel(t, winW, PadX);
                return;
            }

            ImGui.SetCursorPosX(PadX);
            ImGui.TextColored(t.AccentLight, Loc.T("common.passphrase_title"));
            ImGui.Spacing();

            ImGui.SetCursorPosX(PadX);
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(UiColors.Body,
                Loc.T("common.passphrase_intro"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            if (_fetching)
            {
                ImGui.SetCursorPosX(PadX);
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("common.loading"));
                return;
            }

            if (_bundle is null && _error is not null)
            {
                ImGui.SetCursorPosX(PadX);
                ImGui.TextColored(UiColors.Danger, _error);
                ImGui.Spacing();
                // The reset never needs the old bundle, so it stays reachable even when the fetch failed.
                ImGui.SetCursorPosX(PadX);
                if (ImGui.Button(Loc.T("common.passphrase_reset_button"), new Vector2(winW - PadX * 2f, Px(32f))))
                {
                    _resetMode = true;
                    _resetError = null;
                }
                return;
            }

            ImGui.SetCursorPosX(PadX);
            var eyeW = Px(28f);
            var inputW = winW - PadX * 2f - eyeW - Px(4f);
            ImGui.SetNextItemWidth(inputW);
            var flags = _showPassphrase ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
            ImGui.InputText("##passphrase", ref _passphrase, 256, flags);
            ImGui.SameLine(0, Px(4f));
            ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
            var eyeIcon = _showPassphrase
                ? Dalamud.Interface.FontAwesomeIcon.EyeSlash.ToIconString()
                : Dalamud.Interface.FontAwesomeIcon.Eye.ToIconString();
            if (ImGui.Button(eyeIcon + "##togglePass", new Vector2(eyeW, 0)))
            {
                _showPassphrase = !_showPassphrase;
            }
            ImGui.PopFont();
            ImGui.Spacing();

            // Read once per frame - the click handler can flip _unlocking mid-frame.
            var unlocking = _unlocking;

            if (_error is not null && !unlocking)
            {
                ImGui.SetCursorPosX(PadX);
                ImGui.PushTextWrapPos(winW - PadX);
                ImGui.TextColored(UiColors.Danger, _error);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
            }

            ImGui.SetCursorPosX(PadX);
            var btnLabel = unlocking ? Loc.T("common.unlocking") : Loc.T("common.unlock");
            if (unlocking)
            {
                ImGui.BeginDisabled();
            }
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(btnLabel, new Vector2(winW - PadX * 2f, Px(36f))))
            {
                StartUnlock();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
            if (unlocking)
            {
                ImGui.EndDisabled();
            }

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.SetCursorPosX(PadX);
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(UiColors.Muted,
                Loc.T("common.passphrase_forgot"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            ImGui.SetCursorPosX(PadX);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.30f, 0.30f, 0.32f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.40f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.26f, 0.26f, 0.28f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("common.passphrase_reset_button"), new Vector2(winW - PadX * 2f, Px(32f))))
            {
                _resetMode = true;
                _resetError = null;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
            ImGui.Spacing();

            ImGui.SetCursorPosX(PadX);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.30f, 0.30f, 0.32f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.40f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.26f, 0.26f, 0.28f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("common.sign_out"), new Vector2(winW - PadX * 2f, Px(32f))))
            {
                _tokens.Clear();
                _keys.Clear();
                _router.Navigate(Screen.Splash);
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }
    }

    private void StartUnlock()
    {
        if (_bundle is null || _unlocking)
        {
            return;
        }
        var bundle = _bundle;
        var passphrase = _passphrase;
        if (string.IsNullOrEmpty(passphrase))
        {
            _error = Loc.T("common.passphrase_empty");
            return;
        }

        _unlocking = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                switch (await _unlock.UnlockAsync(passphrase, bundle).ConfigureAwait(false))
                {
                    case AccountUnlockOutcome.Success:
                        NavigateToTarget();
                        return;
                    case AccountUnlockOutcome.Unrecoverable:
                        _error = Loc.T("common.passphrase_correct_unrecoverable");
                        return;
                    default:
                        _error = Loc.T("common.passphrase_incorrect");
                        return;
                }
            }
            catch (Exception ex)
            {
                _error = Loc.T("common.passphrase_unlock_failed", HubErrorText.Localize(ex));
                Plugin.Log.Warning(ex, "[PassphraseUnlock] Unlock failed.");
            }
            finally
            {
                _unlocking = false;
            }
        });
    }

    private void NavigateToTarget()
    {
        _router.Navigate(_bootstrap.ResolveNextStartupScreen());
    }

    /// <summary>The lost-passphrase reset: a new passphrase and fresh keys, with the destructive consequences
    /// spelled out. Everything before the reset stays encrypted forever for this user.</summary>
    private void DrawResetPanel(ThemeDefinition t, float winW, float PadX)
    {
        ImGui.SetCursorPosX(PadX);
        ImGui.TextColored(t.AccentLight, Loc.T("common.passphrase_reset_title"));
        ImGui.Spacing();

        ImGui.SetCursorPosX(PadX);
        ImGui.PushTextWrapPos(winW - PadX);
        ImGui.TextColored(UiColors.Danger, Loc.T("common.passphrase_reset_warning"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.SetCursorPosX(PadX);
        ImGui.TextColored(UiColors.Body, Loc.T("common.passphrase_reset_new"));
        ImGui.SetCursorPosX(PadX);
        ImGui.SetNextItemWidth(winW - PadX * 2f);
        ImGui.InputText("##resetPass", ref _resetPass, 256, ImGuiInputTextFlags.Password);
        ImGui.Spacing();

        ImGui.SetCursorPosX(PadX);
        ImGui.TextColored(UiColors.Body, Loc.T("common.passphrase_reset_repeat"));
        ImGui.SetCursorPosX(PadX);
        ImGui.SetNextItemWidth(winW - PadX * 2f);
        ImGui.InputText("##resetPass2", ref _resetPass2, 256, ImGuiInputTextFlags.Password);
        ImGui.Spacing();

        var resetting = _resetting;
        if (_resetError is not null && !resetting)
        {
            ImGui.SetCursorPosX(PadX);
            ImGui.PushTextWrapPos(winW - PadX);
            ImGui.TextColored(UiColors.Danger, _resetError);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        ImGui.SetCursorPosX(PadX);
        if (resetting)
        {
            ImGui.BeginDisabled();
        }
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.62f, 0.20f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.72f, 0.26f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.54f, 0.16f, 0.18f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        var label = resetting ? Loc.T("common.passphrase_reset_running") : Loc.T("common.passphrase_reset_go");
        if (ImGui.Button(label, new Vector2(winW - PadX * 2f, Px(36f))))
        {
            StartReset();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        if (resetting)
        {
            ImGui.EndDisabled();
        }

        ImGui.Spacing();
        ImGui.SetCursorPosX(PadX);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.30f, 0.30f, 0.32f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.40f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.26f, 0.26f, 0.28f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(Loc.T("common.cancel"), new Vector2(winW - PadX * 2f, Px(32f))) && !resetting)
        {
            _resetMode = false;
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    private void StartReset()
    {
        if (_resetting)
        {
            return;
        }
        var pass = _resetPass;
        if (pass.Length < 8)
        {
            _resetError = Loc.T("onboarding.pass_err_too_short", 8);
            return;
        }
        if (pass != _resetPass2)
        {
            _resetError = Loc.T("common.passphrase_reset_mismatch");
            return;
        }
        _resetting = true;
        _resetError = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await _resetFlow.RunAsync(pass, CancellationToken.None).ConfigureAwait(false);
                NavigateToTarget();
            }
            catch (Exception ex)
            {
                _resetError = HubErrorText.Localize(ex);
                Plugin.Log.Warning(ex, "[PassphraseUnlock] Passphrase reset failed.");
            }
            finally
            {
                _resetting = false;
            }
        });
    }
}
