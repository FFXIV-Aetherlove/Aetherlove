using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Messaging;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private readonly Services.Crypto.CryptoService _crypto;
    private readonly Services.Crypto.KeyStorageService _keyStorage;

    private string _passphrase = string.Empty;
    private string _passphraseConfirm = string.Empty;
    private bool _passphraseAcknowledged;
    private volatile bool _passphraseBundleUploaded;
    private volatile bool _passphraseProcessing;
    private volatile string? _passphraseError;
    /// <summary>Set by GoNext on an invalid form so inline errors surface.</summary>
    private bool _passphraseSubmitAttempted;
    private bool _showPassphrase;

    private const int PassphraseMinLength = 8;

    private void DrawPassphraseField(string id, ref string buf, float width)
    {
        var eyeW = Px(28f);
        ImGui.SetNextItemWidth(width - eyeW - Px(4f));
        var flags = _showPassphrase ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
        ImGui.InputText(id, ref buf, 256, flags);
        ImGui.SameLine(0, Px(4f));
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var icon = _showPassphrase
            ? FontAwesomeIcon.EyeSlash.ToIconString()
            : FontAwesomeIcon.Eye.ToIconString();
        if (ImGui.Button(icon + id + "Eye", new Vector2(eyeW, 0)))
            _showPassphrase = !_showPassphrase;
        ImGui.PopFont();
    }

    private void DrawStepPassphrase()
    {
        var t = ThemeService.Current;
        var availW = ImGui.GetContentRegionAvail().X;

        DrawSectionHeading(Loc.T("onboarding.pass_heading"), t);

        ImGui.PushTextWrapPos(0f);
        ImGui.TextWrapped(Loc.T("onboarding.pass_intro"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        var warningTL = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var warningPad = Px(10f, 8f);
        var warningTextW = availW - warningPad.X * 2f;

        ImGui.SetCursorScreenPos(warningTL + warningPad);
        // PushTextWrapPos wants a window-local X, not the screen position the cursor was just set to.
        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + warningTextW);
        var preTextY = ImGui.GetCursorScreenPos().Y;
        ImGui.TextColored(new Vector4(1f, 0.65f, 0.20f, 1f),
            Loc.T("onboarding.pass_warning"));
        ImGui.PopTextWrapPos();
        var postTextY = ImGui.GetCursorScreenPos().Y;
        var warningH = (postTextY - preTextY) + warningPad.Y * 2f;
        dl.AddRectFilled(warningTL, warningTL + new Vector2(availW, warningH),
            UiColors.WarningBoxFill, Px(6f));
        dl.AddRect(warningTL, warningTL + new Vector2(availW, warningH),
            UiColors.WarningBoxBorder, Px(6f), ImDrawFlags.None, 1.5f);
        ImGui.SetCursorScreenPos(new Vector2(warningTL.X, warningTL.Y + warningH + Px(8f)));

        ImGui.TextColored(t.AccentLight, Loc.T("onboarding.pass_label"));
        DrawPassphraseField("##passphrase", ref _passphrase, availW);

        var (strength, strengthLabel, strengthColor) = ScorePassphrase(_passphrase);
        DrawStrengthMeter(strength, strengthLabel, strengthColor, availW);

        ImGui.TextColored(t.AccentLight, Loc.T("onboarding.pass_confirm_label"));
        DrawPassphraseField("##passphraseConfirm", ref _passphraseConfirm, availW);
        ImGui.Spacing();

        var matches = _passphrase.Length > 0 && _passphrase == _passphraseConfirm;
        var lengthOk = _passphrase.Length >= PassphraseMinLength;
        var showLengthErr = !lengthOk && (_passphrase.Length > 0 || _passphraseSubmitAttempted);
        var showMatchErr = !matches && (_passphraseConfirm.Length > 0 || _passphraseSubmitAttempted);
        if (showLengthErr)
        {
            ImGui.TextColored(UiColors.Danger,
                _passphrase.Length == 0
                    ? Loc.T("onboarding.pass_err_empty")
                    : Loc.T("onboarding.pass_err_too_short", PassphraseMinLength));
        }
        if (showMatchErr)
        {
            ImGui.TextColored(UiColors.Danger,
                _passphraseConfirm.Length == 0
                    ? Loc.T("onboarding.pass_err_confirm_empty")
                    : Loc.T("onboarding.pass_err_mismatch"));
        }

        ImGui.Spacing();
        // ImGui's built-in Checkbox label ignores PushTextWrapPos, so the label is drawn separately.
        ImGui.Checkbox("##ackPassphrase", ref _passphraseAcknowledged);
        ImGui.SameLine();
        ImGui.PushTextWrapPos(availW);
        ImGui.TextUnformatted(Loc.T("onboarding.pass_ack"));
        ImGui.PopTextWrapPos();

        if (_passphraseError is not null)
        {
            ImGui.Spacing();
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Danger, _passphraseError);
            ImGui.PopTextWrapPos();
        }
        if (_passphraseProcessing)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("onboarding.pass_securing"));
        }

        ImGui.Spacing();
    }

    /// <summary>Strength is shown but never blocks advancing.</summary>
    private bool CanAdvancePassphrase()
    {
        return _passphrase.Length >= PassphraseMinLength
            && _passphrase == _passphraseConfirm
            && _passphraseAcknowledged
            && !_passphraseProcessing;
    }

    private void DrawStrengthMeter(int score, string label, Vector4 color, float widthPx)
    {
        var BarH = Px(8f);
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(widthPx, BarH);
        var fillW = widthPx * Math.Clamp(score / 12.0f, 0f, 1f);
        dl.AddRectFilled(tl, br, 0x202020FFu, Px(3f));
        dl.AddRectFilled(tl, new Vector2(tl.X + fillW, br.Y),
            ImGui.ColorConvertFloat4ToU32(color), Px(3f));
        ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y + Px(4f)));
        ImGui.TextColored(color, label);
        ImGui.Spacing();
    }

    /// <summary>Passphrase strength score 0..12.</summary>
    private static (int Score, string Label, Vector4 Color) ScorePassphrase(string p)
    {
        var score = 0;

        if (p.Length > 8)
        {
            score += Math.Min(8, p.Length - 8);
        }

        bool hasLower = false, hasUpper = false, hasDigit = false, hasSymbol = false;
        foreach (var c in p)
        {
            if (char.IsLower(c)) hasLower = true;
            else if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else hasSymbol = true;
        }
        if (hasLower) score++;
        if (hasUpper) score++;
        if (hasDigit) score++;
        if (hasSymbol) score++;

        // Penalty: any run of 4 identical chars.
        for (int i = 0; i + 3 < p.Length; i++)
        {
            if (p[i] == p[i + 1] && p[i] == p[i + 2] && p[i] == p[i + 3])
            {
                score -= 2;
                break;
            }
        }

        score = Math.Clamp(score, 0, 12);

        return score switch
        {
            <= 3 => (score, Loc.T("onboarding.pass_strength_too_weak"), new Vector4(0.85f, 0.30f, 0.30f, 1f)),
            <= 5 => (score, Loc.T("onboarding.pass_strength_weak"),     new Vector4(0.90f, 0.55f, 0.30f, 1f)),
            <= 7 => (score, Loc.T("onboarding.pass_strength_ok"),       new Vector4(0.85f, 0.80f, 0.30f, 1f)),
            <= 9 => (score, Loc.T("onboarding.pass_strength_good"),     new Vector4(0.50f, 0.80f, 0.40f, 1f)),
            _    => (score, Loc.T("onboarding.pass_strength_strong"),   new Vector4(0.30f, 0.80f, 0.40f, 1f)),
        };
    }

    /// <summary>Wraps the private key with a passphrase-derived KEK and uploads the bundle; the passphrase
    /// itself never leaves the device.</summary>
    private void StartPassphraseUpload()
    {
        if (_passphraseProcessing || _passphraseBundleUploaded)
        {
            return;
        }
        _passphraseProcessing = true;
        _passphraseError = null;

        var passphrase = _passphrase;

        _ = Task.Run(async () =>
        {
            try
            {
                var (pubKey, privKey) = _crypto.GenerateIdentityKeyPair();

                var salt = new byte[Services.Crypto.CryptoService.KdfSaltLength];
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

                await _hubClient.UploadKeyBundleAsync(bundle, CancellationToken.None).ConfigureAwait(false);
                _keyStorage.Store(pubKey, privKey);
                _passphraseBundleUploaded = true;
                Advance();
            }
            catch (Exception ex)
            {
                _passphraseError = HubErrorText.Localize(ex);
                Plugin.Log.Warning(ex, "[Onboarding] Passphrase upload failed.");
            }
            finally
            {
                _passphraseProcessing = false;
            }
        });
    }
}
