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
    private readonly AetherLoveHubClient _hub;
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly TokenService _tokens;

    private KeyBundleDto? _bundle;
    private string _passphrase = string.Empty;
    private volatile string? _error;
    private volatile bool _fetching;
    private volatile bool _unlocking;
    private bool _showPassphrase;

    public PassphraseUnlockScreen(
        ScreenRouter router,
        SessionBootstrapper bootstrap,
        AetherLoveHubClient hub,
        CryptoService crypto,
        KeyStorageService keys,
        TokenService tokens)
    {
        _router = router;
        _bootstrap = bootstrap;
        _hub = hub;
        _crypto = crypto;
        _keys = keys;
        _tokens = tokens;
    }

    public void OnShow()
    {
        _passphrase = string.Empty;
        _error = null;
        _bundle = null;
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

            // Read once per frame — the click handler can flip _unlocking mid-frame.
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
        _ = Task.Run(() =>
        {
            try
            {
                var kek = _crypto.DeriveKEK(
                    passphrase,
                    bundle.KdfSalt,
                    bundle.KdfMemoryKb,
                    bundle.KdfIterations,
                    bundle.KdfParallelism);
                var privKey = _crypto.UnwrapPrivateKey(bundle.EncryptedPrivateKey, bundle.WrapNonce, kek);
                if (privKey is null)
                {
                    _error = Loc.T("common.passphrase_incorrect");
                    return;
                }
                _keys.Store(bundle.PublicKey, privKey);
                NavigateToTarget();
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
        // The key is stored, so the passphrase gate is satisfied; advance to the next gate (news → target).
        _router.Navigate(_bootstrap.ResolveNextStartupScreen());
    }
}
