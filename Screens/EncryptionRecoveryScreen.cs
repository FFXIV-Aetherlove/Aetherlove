using System;
using System.Numerics;
using System.Security.Cryptography;
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

/// <summary>
/// One-time startup recovery for an Active account that has no server key bundle (e.g. one that
/// re-registered after deletion and reached Active without establishing encryption). Generates a fresh
/// X25519 identity from a new passphrase and publishes it, so messaging works again. Reached only via the
/// startup gate (<see cref="SessionBootstrapper.NeedsEncryptionRecovery"/>), never mid-session.
/// </summary>
public sealed class EncryptionRecoveryScreen
{
    private readonly ScreenRouter _router;
    private readonly SessionBootstrapper _bootstrap;
    private readonly AetherLoveHubClient _hub;
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly TokenService _tokens;

    private string _passphrase = string.Empty;
    private string _confirm = string.Empty;
    private bool _showPassphrase;
    private volatile bool _working;
    private volatile string? _error;

    private const int MinLength = 8;

    public EncryptionRecoveryScreen(
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
        _confirm = string.Empty;
        _error = null;
        _working = false;
        _showPassphrase = false;
    }

    public void Draw()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var scrollH = ImGui.GetContentRegionAvail().Y;
        var padX = Px(16f);

        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##encRecovery", new Vector2(0f, scrollH), false))
        {
            PopScrollbarStyle();
            if (!scroll.Success)
            {
                return;
            }

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX(padX);
            ImGui.TextColored(t.AccentLight, Loc.T("common.recovery_title"));
            ImGui.Spacing();

            ImGui.SetCursorPosX(padX);
            ImGui.PushTextWrapPos(winW - padX);
            ImGui.TextColored(UiColors.Body, Loc.T("common.recovery_intro"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            var working = _working;

            ImGui.SetCursorPosX(padX);
            ImGui.TextColored(t.AccentLight, Loc.T("onboarding.pass_label"));
            DrawPassphraseField("##recPass", ref _passphrase, winW, padX);
            ImGui.Spacing();

            ImGui.SetCursorPosX(padX);
            ImGui.TextColored(t.AccentLight, Loc.T("onboarding.pass_confirm_label"));
            DrawPassphraseField("##recConfirm", ref _confirm, winW, padX);
            ImGui.Spacing();

            if (_error is not null && !working)
            {
                ImGui.SetCursorPosX(padX);
                ImGui.PushTextWrapPos(winW - padX);
                ImGui.TextColored(UiColors.Danger, _error);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
            }
            if (working)
            {
                ImGui.SetCursorPosX(padX);
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("onboarding.pass_securing"));
                ImGui.Spacing();
            }

            ImGui.SetCursorPosX(padX);
            if (working)
            {
                ImGui.BeginDisabled();
            }
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("common.recovery_button"), new Vector2(winW - padX * 2f, Px(36f))))
            {
                StartSetup();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
            if (working)
            {
                ImGui.EndDisabled();
            }

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.SetCursorPosX(padX);
            ImGui.PushTextWrapPos(winW - padX);
            ImGui.TextColored(UiColors.Muted, Loc.T("common.recovery_support"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            ImGui.SetCursorPosX(padX);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.30f, 0.30f, 0.32f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.40f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.26f, 0.26f, 0.28f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("common.sign_out"), new Vector2(winW - padX * 2f, Px(32f))))
            {
                _tokens.Clear();
                _keys.Clear();
                _router.Navigate(Screen.Splash);
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }
    }

    private void DrawPassphraseField(string id, ref string buf, float winW, float padX)
    {
        var eyeW = Px(28f);
        var inputW = winW - padX * 2f - eyeW - Px(4f);
        ImGui.SetCursorPosX(padX);
        ImGui.SetNextItemWidth(inputW);
        var flags = _showPassphrase ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
        ImGui.InputText(id, ref buf, 256, flags);
        ImGui.SameLine(0, Px(4f));
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var icon = _showPassphrase ? FontAwesomeIcon.EyeSlash.ToIconString() : FontAwesomeIcon.Eye.ToIconString();
        if (ImGui.Button(icon + id + "Eye", new Vector2(eyeW, 0)))
        {
            _showPassphrase = !_showPassphrase;
        }
        ImGui.PopFont();
    }

    private void StartSetup()
    {
        if (_working)
        {
            return;
        }
        if (_passphrase.Length == 0)
        {
            _error = Loc.T("onboarding.pass_err_empty");
            return;
        }
        if (_passphrase.Length < MinLength)
        {
            _error = Loc.T("onboarding.pass_err_too_short", MinLength);
            return;
        }
        if (_passphrase != _confirm)
        {
            _error = Loc.T("onboarding.pass_err_mismatch");
            return;
        }

        _working = true;
        _error = null;
        var passphrase = _passphrase;
        _ = Task.Run(async () =>
        {
            try
            {
                var (pubKey, privKey) = _crypto.GenerateIdentityKeyPair();

                var salt = new byte[CryptoService.KdfSaltLength];
                RandomNumberGenerator.Fill(salt);

                const int MemoryKb = 64 * 1024;
                const int Iterations = 3;
                const int Parallelism = 1;

                var kek = _crypto.DeriveKEK(passphrase, salt, MemoryKb, Iterations, Parallelism);
                var (wrapped, wrapNonce) = _crypto.WrapPrivateKey(privKey, kek);

                var bundle = new KeyBundleDto(
                    PublicKey: pubKey,
                    EncryptedPrivateKey: wrapped,
                    KdfSalt: salt,
                    KdfMemoryKb: MemoryKb,
                    KdfIterations: Iterations,
                    KdfParallelism: Parallelism,
                    WrapNonce: wrapNonce);

                await _hub.UploadKeyBundleAsync(bundle, CancellationToken.None).ConfigureAwait(false);
                _keys.Store(pubKey, privKey);

                // Reflect the freshly-published bundle so the recovery gate clears, then walk the ladder onward.
                var snap = _bootstrap.LastConnection;
                if (snap is not null)
                {
                    _bootstrap.ReplaceConnectionSnapshot(snap with { HasKeyBundle = true });
                }
                _router.Navigate(_bootstrap.ResolveNextStartupScreen());
            }
            catch (Exception ex)
            {
                _error = HubErrorText.Localize(ex);
                Plugin.Log.Warning(ex, "[EncryptionRecovery] Key bundle setup failed.");
            }
            finally
            {
                _working = false;
            }
        });
    }
}
