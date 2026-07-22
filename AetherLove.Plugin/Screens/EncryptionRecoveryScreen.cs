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

/// <summary>Startup gate for an Active profile with no server key bundle. AetherLove is only a CONSUMER of
/// the account passphrase: when the account has one published, this screen asks the user to ENTER it and
/// provisions the profile's bundle under the account KEK; only an account that never published a passphrase
/// (legacy, and its last bundle is gone) gets the create form, whose passphrase then becomes the account
/// passphrase. A locally stored KEK never reaches this screen at all (the bootstrap provisions silently).</summary>
public sealed class EncryptionRecoveryScreen
{
    private readonly ScreenRouter _router;
    private readonly SessionBootstrapper _bootstrap;
    private readonly AetherHubContext _hub;
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly TokenService _tokens;

    private string _passphrase = string.Empty;
    private string _confirm = string.Empty;
    private bool _showPassphrase;
    private volatile bool _working;
    private volatile string? _error;
    private volatile bool _loadingAccountPass;
    private AetherLove.Shared.Profile.AccountPassphraseDto? _accountPass;

    private const int MinLength = 8;

    private readonly PassphraseUnlockScreen _unlockScreen;

    public EncryptionRecoveryScreen(
        ScreenRouter router,
        SessionBootstrapper bootstrap,
        AetherHubContext hub,
        CryptoService crypto,
        KeyStorageService keys,
        TokenService tokens,
        PassphraseUnlockScreen unlockScreen)
    {
        _router = router;
        _bootstrap = bootstrap;
        _hub = hub;
        _crypto = crypto;
        _keys = keys;
        _tokens = tokens;
        _unlockScreen = unlockScreen;
    }

    public void OnShow()
    {
        _passphrase = string.Empty;
        _confirm = string.Empty;
        _error = null;
        _working = false;
        _showPassphrase = false;
        _accountPass = null;
        _loadingAccountPass = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _accountPass = await _hub.GetAccountPassphraseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Falls back to the create form; the server's write-once guard protects a raced account.
                Plugin.Log.Warning(ex, "[EncryptionRecovery] GetAccountPassphraseAsync failed.");
            }
            finally
            {
                _loadingAccountPass = false;
            }
        });
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

            if (_loadingAccountPass)
            {
                ImGui.SetCursorPosX(padX);
                ImGui.TextColored(UiColors.Muted, Loc.T("common.loading"));
                return;
            }
            var enterExisting = _accountPass is not null;

            ImGui.SetCursorPosX(padX);
            ImGui.PushTextWrapPos(winW - padX);
            ImGui.TextColored(UiColors.Body,
                Loc.T(enterExisting ? "common.recovery_enter_intro" : "common.recovery_intro"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            var working = _working;

            ImGui.SetCursorPosX(padX);
            ImGui.TextColored(t.AccentLight, Loc.T("onboarding.pass_label"));
            DrawPassphraseField("##recPass", ref _passphrase, winW, padX);
            ImGui.Spacing();

            if (!enterExisting)
            {
                ImGui.SetCursorPosX(padX);
                ImGui.TextColored(t.AccentLight, Loc.T("onboarding.pass_confirm_label"));
                DrawPassphraseField("##recConfirm", ref _confirm, winW, padX);
                ImGui.Spacing();
            }

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
                if (enterExisting)
                {
                    StartProvisionWithExisting();
                }
                else
                {
                    StartSetup();
                }
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

            if (enterExisting)
            {
                ImGui.SetCursorPosX(padX);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.30f, 0.30f, 0.32f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.38f, 0.40f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.26f, 0.26f, 0.28f, 1f));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                if (ImGui.Button(Loc.T("common.passphrase_reset_button"), new Vector2(winW - padX * 2f, Px(32f))))
                {
                    _unlockScreen.OpenInResetMode = true;
                    _router.Navigate(Screen.PassphraseUnlock);
                }
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(3);
                ImGui.Spacing();
            }

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

    /// <summary>Consumer path: derives the account KEK from the EXISTING account passphrase, checks it against
    /// the account verifier, then provisions this profile's fresh keypair under it. Never invents key material
    /// beyond the profile's own keypair.</summary>
    private void StartProvisionWithExisting()
    {
        if (_working || _accountPass is not { } pass)
        {
            return;
        }
        if (_passphrase.Length == 0)
        {
            _error = Loc.T("onboarding.pass_err_empty");
            return;
        }

        _working = true;
        _error = null;
        var passphrase = _passphrase;
        _ = Task.Run(async () =>
        {
            try
            {
                var kek = _crypto.DeriveKEK(passphrase, pass.KdfSalt, pass.KdfMemoryKb, pass.KdfIterations, pass.KdfParallelism);
                if (!_crypto.CheckPassphraseVerifier(pass.Verifier, pass.VerifierNonce, kek))
                {
                    _error = Loc.T("common.passphrase_incorrect");
                    return;
                }
                _keys.StoreKek(kek);

                var (pubKey, privKey) = _crypto.GenerateIdentityKeyPair();
                var (wrapped, wrapNonce) = _crypto.WrapPrivateKey(privKey, kek);
                await _hub.UploadKeyBundleAsync(new KeyBundleDto(
                        pubKey, wrapped, pass.KdfSalt, pass.KdfMemoryKb, pass.KdfIterations, pass.KdfParallelism, wrapNonce),
                    CancellationToken.None).ConfigureAwait(false);
                _keys.Store(pubKey, privKey);

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
                Plugin.Log.Warning(ex, "[EncryptionRecovery] Bundle provisioning under the account KEK failed.");
            }
            finally
            {
                _working = false;
            }
        });
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
                // This create form only runs for an account with no published passphrase: what was just
                // typed becomes the ACCOUNT passphrase, so every later profile only ever re-enters it.
                try
                {
                    var (verifier, verifierNonce) = _crypto.CreatePassphraseVerifier(kek);
                    await _hub.SetAccountPassphraseAsync(
                        new AetherLove.Shared.Profile.AccountPassphraseDto(
                            salt, MemoryKb, Iterations, Parallelism, verifier, verifierNonce),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning(ex, "[EncryptionRecovery] SetAccountPassphrase failed; will backfill on the next unlock.");
                }
                _keys.Store(pubKey, privKey);
                _keys.StoreKek(kek);

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
