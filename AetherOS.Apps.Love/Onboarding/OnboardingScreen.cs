using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherLove.Screens;

/// <summary>Multi-step onboarding wizard rendered inside the phone shell.</summary>
public partial class OnboardingScreen
{
    private readonly LoveRouter _router;
    private readonly AetherHubContext _hubClient;
    private readonly SessionBootstrapper _bootstrap;
    private readonly RateLimitModal _rateLimitModal;
    private readonly SaveErrorModal _saveErrorModal;
    private readonly PendingImagePick _pendingPick;
    private readonly IAppCapabilities _caps;
    private readonly LoveShell _shell;

    private volatile bool _saving;
    private volatile string? _saveError;
    private volatile bool _advancePending;

    private const int TotalSteps = 13;
    private OnboardingStep _step = OnboardingStep.Welcome;
    private bool _scrollToTop;

    private readonly AetherLove.Widgets.AetherFileDialogManager _fileDialog = new();
    private readonly ImageCropPopup _cropPopup = new();
    private readonly SfwImageGateModal _sfwGate = new();
    private int _pickerTarget; // -1 = avatar, 0-3 = photo slot


    public OnboardingScreen(LoveRouter router,
                            AetherHubContext hubClient, SessionBootstrapper bootstrap,
                            RateLimitModal rateLimitModal,
                            SaveErrorModal saveErrorModal,
                            ImageRequirementsModal imageReqModal,
                            IAppCapabilities caps,
                            LoveShell shell)
    {
        _router = router;
        _hubClient = hubClient;
        _bootstrap = bootstrap;
        _rateLimitModal = rateLimitModal;
        _saveErrorModal = saveErrorModal;
        _pendingPick = new PendingImagePick(imageReqModal);
        _caps = caps;
        _shell = shell;
    }


    public void OnShow()
    {
        _step = OnboardingStep.Welcome;
        _imageRulesScrolledToBottom = false;

        AutoDetectDefaults();
        ResetFilters();

        if (_displayName.Length == 0)
        {
            var osName = _bootstrap.LastAccount?.OsDisplayName;
            if (!string.IsNullOrWhiteSpace(osName))
            {
                _displayName = osName.Trim().Split(' ')[0];
            }
            else
            {
                var fullName = UiHost.ObjectTable.LocalPlayer?.Name.TextValue ?? "";
                _displayName = fullName.Contains(' ')
                    ? fullName[..fullName.IndexOf(' ')]
                    : fullName;
            }
        }

        if (_bootstrap.LastResult == SessionBootstrapResult.SignedInOnboarding)
        {
            var state = _bootstrap.ConsumeOnboardingState();
            if (state is not null)
            {
                HydrateFromOnboardingState(state);
            }
        }
    }


    public void Draw()
    {
        _fileDialog.Draw();
        _pendingPick.Poll();
        _cropPopup.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        DrainSaveQueue();
        DrawProgress();

        const float TopH = 34f;
        const float NavH = 62f;
        var contentH = ImGui.GetWindowSize().Y - Px(TopH) - Px(NavH);

        ImGui.SetCursorPos(new Vector2(0f, Px(TopH)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##obContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                // The step body child keeps its scroll offset by id; reset it on a fresh step so a tall page
                // (Extras) never opens the next step scrolled partway down.
                if (_scrollToTop)
                {
                    _scrollToTop = false;
                    ImGui.SetScrollY(0f);
                }
                switch (_step)
                {
                    case OnboardingStep.Welcome:
                        DrawStepWelcome();
                        break;
                    case OnboardingStep.Name:
                        DrawStepName();
                        break;
                    case OnboardingStep.Bio:
                        DrawStepBio();
                        break;
                    case OnboardingStep.Character:
                        DrawStepCharacter();
                        break;
                    case OnboardingStep.Languages:
                        DrawStepLanguages();
                        break;
                    case OnboardingStep.Interests:
                        DrawStepInterests();
                        break;
                    case OnboardingStep.LookingFor:
                        DrawStepLookingFor();
                        break;
                    case OnboardingStep.ImageRules:
                        DrawStepImageRules();
                        break;
                    case OnboardingStep.Avatar:
                        DrawStepAvatar();
                        break;
                    case OnboardingStep.Photos:
                        DrawStepPhotos();
                        break;
                    case OnboardingStep.Extras:
                        DrawStepExtras();
                        break;
                    case OnboardingStep.Filters:
                        DrawStepFilters();
                        break;
                    case OnboardingStep.Finished:
                        DrawStepFinished();
                        break;
                }
            }
        }
        PopScrollbarStyle();

        DrawBottomNav();
    }

    private void DrawProgress()
    {
        var canGoBack = _step > OnboardingStep.Welcome && !_saving;
        if (OnboardingUi.DrawProgress((int)_step, TotalSteps, canGoBack))
        {
            GoBack();
        }
    }

    private void DrawBottomNav()
    {
        var winH = ImGui.GetWindowSize().Y;
        ImGui.SetCursorPos(new Vector2(0f, winH - Px(54f)));

        var canProceed = _step switch
        {
            OnboardingStep.Name => _displayName.Trim().Length >= AetherLove.Shared.ProfileLimits.DisplayNameMinLength,
            OnboardingStep.Character => _ownRegions.Any(x => x),
            OnboardingStep.Languages => _langSelected.Any(x => x),
            OnboardingStep.LookingFor => _lookingFor.Any(x => x),
            OnboardingStep.ImageRules => _imageRulesScrolledToBottom,
            OnboardingStep.Avatar => _avatarConfirmed,
            OnboardingStep.Photos => _photos[0].Confirmed && AllConfirmedExtrasDeclared(),
            _ => true,
        };

        var label = _saving
            ? Loc.T("onboarding.saving")
            : _step switch
            {
                OnboardingStep.Finished => Loc.T("onboarding.start_swiping"),
                OnboardingStep.Filters => _saveError is not null ? Loc.T("onboarding.retry") : Loc.T("onboarding.finish"),
                OnboardingStep.ImageRules when !_imageRulesScrolledToBottom => Loc.T("onboarding.img_scroll_hint"),
                _ => _saveError is not null ? Loc.T("onboarding.retry") : Loc.T("onboarding.next"),
            };

        if (DrawPrimaryButton(label, canProceed && !_saving))
        {
            GoNext();
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
            _router.Navigate(LoveView.Deck);
            return;
        }

        switch (_step)
        {
            case OnboardingStep.Photos:
                // Snapshot the slots on the UI thread; the heavy image work runs in the hub-call task.
                var photoInputs = SnapshotPhotoInputs();
                BeginSave("photos", () => photoInputs,
                    (inputs, ct) => _hubClient.SavePhotosAsync(BuildPhotoBatch(inputs), ct));
                return;

            case OnboardingStep.Extras:
                BeginSave("profile", BuildBasicProfile, (dto, ct) => _hubClient.SaveBasicProfileAsync(dto, ct));
                return;

            case OnboardingStep.Filters:
                BeginSave("filters", BuildFilters, (dto, ct) => _hubClient.SaveFiltersAsync(dto, ct),
                    onSuccess: () => _ = _bootstrap.RefreshConnectionInfoAsync());
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
            UiHost.Log.Warning(ex, $"[Onboarding] BuildDto failed for {label}.");
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
                UiHost.Log.Warning(ex, $"[Onboarding] {label} save failed.");
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
        OnStepEntered();
    }

    private void OnStepEntered()
    {
        _saveError = null;

        _scrollToTop = true;
        if (_step == OnboardingStep.ImageRules)
        {
            _imageRulesScrolledToBottom = false;
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

    private void OpenSelfie()
    {
        if (_pickerTarget is -1 or 0)
        {
            _sfwGate.Open(OpenSelfieCore);
            return;
        }
        OpenSelfieCore();
    }

    private void OpenSelfieCore()
    {
        var aspect = _pickerTarget == -1 ? 1.0f : 1.6f;
        var minW = _pickerTarget == -1 ? PhotoSpec.AvatarSize : PhotoSpec.PortraitWidth;
        _shell.RequestCamera(aspect, minW, (path, crop) => HandlePicked(path, crop));
    }

    private void TryPickPhoto(Action open)
    {
        if (AnyConfirmedExtraIsUndeclared())
        {
            _undeclaredModalPending = true;
            return;
        }
        _pickerTarget = _activePhotoSlot;
        open();
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
                if (_pendingPick.RejectUnavailableCloudFile(path))
                {
                    return;
                }
                HandlePicked(path);
            });
    }

    private void HandlePicked(string path, Vector4? presetCrop = null)
    {
        if (_pickerTarget == -1)
        {
            var prevFromServer = _avatarFromServer;
            var handle = LoadPickedPreview(path);
            if (handle is null)
            {
                return;
            }
            _avatarPath = path;
            _avatarConfirmed = false;
            _avatarFromServer = false;
            _avatarHandle = handle;

            if (presetCrop is { } avatarCrop)
            {
                _avatarCropRect = avatarCrop;
                _avatarConfirmed = true;
                return;
            }

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
            if (handle is null)
            {
                return;
            }
            slot.Path = path;
            slot.Confirmed = false;
            slot.FromServer = false;
            slot.Handle = handle;

            if (presetCrop is { } photoCrop)
            {
                slot.CropRect = photoCrop;
                slot.Confirmed = true;
                return;
            }

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
    }


}
