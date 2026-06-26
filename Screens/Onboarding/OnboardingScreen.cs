using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Multi-step onboarding wizard rendered inside the phone shell.</summary>
public partial class OnboardingScreen
{
    private readonly ScreenRouter _router;
    private readonly AuthService _authService;
    private readonly AetherLoveHubClient _hubClient;
    private readonly SessionBootstrapper _bootstrap;
    private readonly RateLimitModal _rateLimitModal;
    private readonly SaveErrorModal _saveErrorModal;
    private readonly PendingImagePick _pendingPick;

    private volatile bool _saving;
    private volatile string? _saveError;
    private volatile bool _advancePending;

    private const int TotalDisplaySteps = 12;
    private OnboardingStep _step = OnboardingStep.Welcome;

    private readonly FileDialogManager _fileDialog = new();
    private readonly ImageCropPopup _cropPopup = new();
    private readonly SfwImageGateModal _sfwGate = new();
    private int _pickerTarget; // -1 = avatar, 0-3 = photo slot


    public OnboardingScreen(ScreenRouter router, AuthService authService,
                            AetherLoveHubClient hubClient, SessionBootstrapper bootstrap,
                            Services.Crypto.CryptoService crypto,
                            Services.Crypto.KeyStorageService keyStorage,
                            RateLimitModal rateLimitModal,
                            SaveErrorModal saveErrorModal,
                            ImageRequirementsModal imageReqModal)
    {
        _router = router;
        _authService = authService;
        _hubClient = hubClient;
        _bootstrap = bootstrap;
        _crypto = crypto;
        _keyStorage = keyStorage;
        _rateLimitModal = rateLimitModal;
        _saveErrorModal = saveErrorModal;
        _pendingPick = new PendingImagePick(imageReqModal);
    }


    public void OnShow()
    {
        _step                        = OnboardingStep.Welcome;
        _tosTimerStart               = DateTime.MinValue;
        _imageDisclaimerAcknowledged = false;

        _authService.Cancel();
        _authCompletedShownAt = DateTime.MinValue;

        AutoDetectDefaults();
        ResetFilters();

        if (_displayName.Length == 0)
        {
            var fullName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "";
            _displayName = fullName.Contains(' ')
                ? fullName[..fullName.IndexOf(' ')]
                : fullName;
        }

        if (_bootstrap.LastResult == SessionBootstrapResult.SignedInOnboarding)
        {
            var state = _bootstrap.ConsumeOnboardingState();
            if (state is not null)
            {
                HydrateFromOnboardingState(state);
            }
            _imageDisclaimerAcknowledged = true;
            _tosAccepted = true;

            _step = (_bootstrap.LastConnection?.HasKeyBundle == false)
                ? OnboardingStep.EncryptionSetup
                : OnboardingStep.ProfileInfo;
        }
    }


    public void Draw()
    {
        _fileDialog.Draw();
        _pendingPick.Poll();
        _cropPopup.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        DrawHeader();

        const float HeaderH   = 58f;
        const float HeaderGap = 6f;
        const float NavH      = 48f;
        var contentH = ImGui.GetWindowSize().Y - Px(HeaderH) - Px(HeaderGap) - Px(NavH);

        PushScrollbarStyle();

        DrainSaveQueue();

        using (var content = ImRaii.Child("##obContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case OnboardingStep.Welcome:
                        DrawStepWelcome();
                        break;
                    case OnboardingStep.HowItWorks:
                        DrawStepHowItWorks();
                        break;
                    case OnboardingStep.TermsOfService:
                        DrawStepTOS();
                        break;
                    case OnboardingStep.XIVAuth:
                        DrawStepAuth();
                        break;
                    case OnboardingStep.EncryptionSetup:
                        DrawStepPassphrase();
                        break;
                    case OnboardingStep.ProfileInfo:
                        DrawStepProfile();
                        break;
                    case OnboardingStep.AvatarUpload:
                        DrawStepAvatar();
                        break;
                    case OnboardingStep.Photos:
                        DrawStepPhotos();
                        break;
                    case OnboardingStep.OptionalInfo:
                        DrawStepOptional();
                        break;
                    case OnboardingStep.Filters:
                        DrawStepFilters();
                        break;
                    case OnboardingStep.Preferences:
                        DrawStepPreferences();
                        break;
                    case OnboardingStep.Finished:
                        DrawStepFinished();
                        break;
                }
            }
        }

        PopScrollbarStyle();

        DrawNavigation();
    }


    private void DrawHeader()
    {
        var t = ThemeService.Current;
        var wPos = ImGui.GetWindowPos();
        var wSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var H = Px(58f);

        dl.AddRectFilledMultiColor(
            wPos, wPos + new Vector2(wSize.X, H),
            t.AccentU32, t.AccentU32,
            t.AccentDarkWithAlpha(0.80f), t.AccentDarkWithAlpha(0.80f));

        dl.AddLine(wPos + new Vector2(0, H), wPos + new Vector2(wSize.X, H), t.AccentLightU32, 1f);

        var title = _step switch
        {
            OnboardingStep.Welcome => Loc.T("onboarding.header_welcome"),
            OnboardingStep.HowItWorks => Loc.T("onboarding.header_how_it_works"),
            OnboardingStep.TermsOfService => Loc.T("onboarding.header_terms_of_service"),
            OnboardingStep.XIVAuth => Loc.T("onboarding.header_sign_in"),
            OnboardingStep.EncryptionSetup => Loc.T("onboarding.header_secure_messages"),
            OnboardingStep.ProfileInfo => Loc.T("onboarding.header_your_profile"),
            OnboardingStep.AvatarUpload => Loc.T("onboarding.header_profile_picture"),
            OnboardingStep.Photos => Loc.T("onboarding.header_your_photos"),
            OnboardingStep.OptionalInfo => Loc.T("onboarding.header_optional_details"),
            OnboardingStep.Filters => Loc.T("onboarding.header_match_preferences"),
            OnboardingStep.Preferences => Loc.T("onboarding.header_make_it_yours"),
            OnboardingStep.Finished => Loc.T("onboarding.header_all_set"),
            _ => Loc.T("onboarding.header_default"),
        };
        var titleSz = ImGui.CalcTextSize(title);
        dl.AddText(wPos + new Vector2((wSize.X - titleSz.X) * 0.5f, Px(8f)), 0xFFFFFFFF, title);

        var DotR = Px(4f);
        var DotSpacing = Px(14f);
        var dotsW = (TotalDisplaySteps - 1) * DotSpacing + DotR * 2f;
        var dotsX = wPos.X + (wSize.X - dotsW) * 0.5f;
        var dotsY = wPos.Y + H - Px(14f);

        // Progress dots: one per step. Completed steps are filled accent, the current step is a filled
        // white dot, and steps still ahead are hollow.
        for (int i = 0; i < TotalDisplaySteps; i++)
        {
            var c = new Vector2(dotsX + i * DotSpacing + DotR, dotsY);
            if (i < (int)_step)
            {
                dl.AddCircleFilled(c, DotR, t.AccentLightU32);
            }
            else if (i == (int)_step)
            {
                dl.AddCircleFilled(c, DotR, 0xFFFFFFFF);
            }
            else
            {
                dl.AddCircle(c, DotR, 0x88FFFFFF, 0, 1.5f);
            }
        }

        ImGui.SetCursorPos(new Vector2(0f, H + Px(6f)));
    }


    private void DrawNavigation()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var winH = ImGui.GetWindowSize().Y;

        ImGui.SetCursorPos(new Vector2(0f, winH - Px(48f)));
        ImGui.Separator();

        var btnY = winH - Px(40f);

        if (_step > OnboardingStep.Welcome && !_saving)
        {
            ImGui.SetCursorPos(new Vector2(Px(4f), btnY));
            PushThemeButton(t);
            if (ImGui.Button(Loc.T("onboarding.back"), Px(80f, 30f)))
            {
                GoBack();
            }
            PopThemeButton();
        }

        bool canProceed = _step switch
        {
            OnboardingStep.Welcome => true,
            OnboardingStep.HowItWorks => true,
            OnboardingStep.TermsOfService => _tosAccepted,
            OnboardingStep.XIVAuth => _authService.State == AuthFlowState.Completed,
            // GoNext fires the upload if not yet uploaded.
            OnboardingStep.EncryptionSetup => _passphraseBundleUploaded || CanAdvancePassphrase(),
            OnboardingStep.ProfileInfo => _displayName.Trim().Length >= AetherLove.Shared.ProfileLimits.DisplayNameMinLength
                                          && _langSelected.Any(x => x)
                                          && _lookingFor.Any(x => x),
            OnboardingStep.AvatarUpload => _avatarConfirmed,
            OnboardingStep.Photos => _photos[0].Confirmed && AllConfirmedExtrasDeclared(),
            OnboardingStep.OptionalInfo => true,
            OnboardingStep.Filters => true,
            OnboardingStep.Preferences => true,
            OnboardingStep.Finished => true,
            _ => true,
        };

        var nextLabel = _saving
            ? Loc.T("onboarding.saving")
            : _step switch
            {
                OnboardingStep.Finished => Loc.T("onboarding.start_swiping"),
                OnboardingStep.Filters => _saveError is not null ? Loc.T("onboarding.retry") : Loc.T("onboarding.finish"),
                _ => _saveError is not null ? Loc.T("onboarding.retry") : Loc.T("onboarding.next"),
            };
        float baseW = Px(_step == OnboardingStep.Finished ? 130f : 92f);
        float nextW = Math.Max(baseW, ImGui.CalcTextSize(nextLabel).X + Px(16f));
        ImGui.SetCursorPos(new Vector2(winW - nextW - Px(4f), btnY));

        var buttonDisabled = !canProceed || _saving;
        if (buttonDisabled)
        {
            ImGui.BeginDisabled();
        }
        PushThemeButton(t);
        if (ImGui.Button(nextLabel, new Vector2(nextW, Px(30f))))
        {
            GoNext();
        }
        var nextBtnMin = ImGui.GetItemRectMin();
        var nextBtnMax = ImGui.GetItemRectMax();
        PopThemeButton();
        if (_saving)
        {
            var cy = (nextBtnMin.Y + nextBtnMax.Y) * 0.5f;
            AetherLove.Widgets.LoadingSpinner.Draw(new Vector2(nextBtnMin.X + Px(14f), cy), Px(7f), 2.2f, 0xFFFFFFFF);
        }
        if (buttonDisabled)
        {
            ImGui.EndDisabled();
        }
    }


    private void GoNext()
    {
        if (_saving)
        {
            return;
        }

        if (_step == OnboardingStep.Finished)
        {
            _router.Navigate(Screen.Deck);
            return;
        }

        if (_step == OnboardingStep.Welcome)
        {
            Plugin.Configuration.PluginLanguage = LanguageEntries[_pluginLangIdx].Name;
            Plugin.Configuration.Save();
        }

        switch (_step)
        {
            case OnboardingStep.EncryptionSetup:
                if (_passphraseBundleUploaded)
                {
                    break; // Already uploaded; fall through to Advance.
                }
                _passphraseSubmitAttempted = true;
                if (!CanAdvancePassphrase())
                {
                    return;
                }
                StartPassphraseUpload();
                return;

            case OnboardingStep.Photos:
                // Snapshot the slots on the UI thread; the heavy decode/resize/encode runs in the hub-call
                // task (off-thread). A PhotoProcessingException there localizes via the save catch.
                var photoInputs = SnapshotPhotoInputs();
                BeginSave("photos", () => photoInputs,
                    (inputs, ct) => _hubClient.SavePhotosAsync(BuildPhotoBatch(inputs), ct));
                return;

            case OnboardingStep.OptionalInfo:
                BeginSave("profile", BuildBasicProfile, (dto, ct) => _hubClient.SaveBasicProfileAsync(dto, ct));
                return;

            case OnboardingStep.Filters:
                BeginSave("filters", BuildFilters, (dto, ct) => _hubClient.SaveFiltersAsync(dto, ct));
                return;
        }

        Advance();
    }

    private void Advance()
    {
        _step = (OnboardingStep)((int)_step + 1);
        OnStepEntered();
    }

    private void BeginSave<TDto>(
        string label,
        Func<TDto> buildDto,
        Func<TDto, CancellationToken, Task> hubCall,
        Action? onSuccess = null)
    {
        _saving = true;
        _saveError = null;

        TDto dto;
        try
        {
            dto = buildDto();
        }
        catch (Exception ex)
        {
            _saving = false;
            _saveError = Loc.T("onboarding.could_not_assemble", label, ex.Message);
            _saveErrorModal.Show(_saveError);
            Plugin.Log.Warning(ex, $"[Onboarding] BuildDto failed for {label}.");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await hubCall(dto, CancellationToken.None).ConfigureAwait(false);
                onSuccess?.Invoke();
                _saveError = null;
                _advancePending = true;
            }
            catch (RateLimitException rl)
            {
                _rateLimitModal.Show(rl);
            }
            catch (Exception ex)
            {
                _saveError = HubErrorText.Localize(ex);
                _saveErrorModal.Show(_saveError);
                Plugin.Log.Warning(ex, $"[Onboarding] {label} save failed.");
            }
            finally
            {
                _saving = false;
            }
        });
    }

    private void DrainSaveQueue()
    {
        if (_advancePending)
        {
            _advancePending = false;
            Advance();
        }
    }

    private void GoBack()
    {
        if (_step == OnboardingStep.Welcome)
        {
            return;
        }
        _step = (OnboardingStep)((int)_step - 1);
        // Skip the passphrase-setup step backwards too when a key bundle already exists (mirrors OnShow).
        if (_step == OnboardingStep.EncryptionSetup && _bootstrap.LastConnection?.HasKeyBundle == true)
        {
            _step = (OnboardingStep)((int)_step - 1);
        }
        OnStepEntered();
    }

    private void OnStepEntered()
    {
        _saveError = null;

        if (_step == OnboardingStep.TermsOfService && !_tosAccepted
            && _tosTimerStart == DateTime.MinValue)
        {
            _tosTimerStart = DateTime.Now;
        }

        if (_step == OnboardingStep.Finished)
        {
            ResetConfetti();
        }

        _activePhotoSlot = -1;
    }


    private void OpenFilePicker()
    {
        // Avatar (-1) and the first profile photo (0) are SFW-mandatory; gate them behind the rules modal.
        if (_pickerTarget is -1 or 0)
        {
            _sfwGate.Open(OpenFilePickerCore);
            return;
        }
        OpenFilePickerCore();
    }

    private void OpenFilePickerCore()
    {
        _fileDialog.OpenFileDialog(
            title: Loc.T("onboarding.select_photo"),
            filters: $"{Loc.T("onboarding.image_files")}{{.png,.jpg,.jpeg,.bmp,.webp}}",
            callback: (ok, path) =>
            {
                if (!ok)
                {
                    return;
                }
                if (_pickerTarget == -1)
                {
                    var prevFromServer = _avatarFromServer;
                    var handle = LoadPickedPreview(path);
                    _avatarPath = path;
                    _avatarConfirmed = false;
                    _avatarFromServer = false;
                    _avatarHandle = handle;

                    void Unload()
                    {
                        _avatarPath = "";
                        _avatarHandle = null;
                        _avatarFromServer = prevFromServer;
                    }

                    _pendingPick.Begin(handle, PhotoSpec.AvatarSize, PhotoSpec.AvatarSize,
                        onValid: () => _cropPopup.Open(
                            Loc.T("onboarding.crop_avatar"),
                            handle,
                            1.0f,
                            cropRect => { _avatarCropRect = cropRect; _avatarConfirmed = true; },
                            onCancel: Unload),
                        onReject: Unload);
                }
                else
                {
                    var target = _pickerTarget;
                    var slot = _photos[target];
                    var prevPath = slot.Path;
                    var prevHandle = slot.Handle;
                    var prevCropRect = slot.CropRect;
                    var prevConfirmed = slot.Confirmed;
                    var prevFromServer = slot.FromServer;
                    var handle = LoadPickedPreview(path);
                    slot.Path = path;
                    slot.Confirmed = false;
                    slot.FromServer = false;
                    slot.Handle = handle;

                    void Restore()
                    {
                        _photos[target].Path = prevPath;
                        _photos[target].Handle = prevHandle;
                        _photos[target].CropRect = prevCropRect;
                        _photos[target].Confirmed = prevConfirmed;
                        _photos[target].FromServer = prevFromServer;
                    }

                    var label = target == 0 ? Loc.T("onboarding.crop_main_photo") : Loc.T("onboarding.crop_extra_photo", target);
                    _pendingPick.Begin(handle, PhotoSpec.PortraitWidth, PhotoSpec.PortraitHeight,
                        onValid: () => _cropPopup.Open(
                            label,
                            handle,
                            1.6f,
                            cropRect => { _photos[target].CropRect = cropRect; _photos[target].Confirmed = true; },
                            onCancel: Restore),
                        onReject: Restore);
                }
            });
    }


}
