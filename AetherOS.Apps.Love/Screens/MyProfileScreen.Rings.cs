using System;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Store;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class MyProfileScreen
{
    private readonly RingPickerUi _ringPicker = new();

    private void OpenAvatarRing()
    {
        _ringPicker.Open(_bootstrap.LastConnection?.EquippedFrameRef);
        _entrance.Arm();
        _section = Section.AvatarRing;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                _ringPicker.SetOwned(await _hubClient.GetMyAvatarRingsAsync(ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    _ringPicker.FailLoad(HubErrorText.Localize(ex));
                    UiHost.Log.Warning(ex, "[MyProfileScreen] GetMyAvatarRingsAsync failed.");
                }
            }
        }, ct);
    }

    private void DrawAvatarRingView()
    {
        DrawHubBackButton();
        DrawSubpageHeading(Loc.T("rings.picker_title"), HubPadX);

        using var scroll = ImRaii.Child("##loveRings", ImGui.GetContentRegionAvail(), false);
        if (!scroll.Success)
        {
            return;
        }
        var availW = ImGui.GetContentRegionAvail().X;
        _ringPicker.Draw(_ownAvatar.Texture, availW, Px(HubPadX), SaveAvatarRing,
            () => _shell.Shell?.SendIntent("store", OsIntents.CreatePath(OsIntents.StoreOpen, "avatar-packs")));
        ImGui.Spacing();
    }

    private void SaveAvatarRing(string? selected)
    {
        _ringPicker.BeginSave();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.SetAvatarRingAsync(AvatarRingSurface.Love, selected, ct).ConfigureAwait(false);
                if (_bootstrap.LastConnection is { } conn)
                {
                    _bootstrap.ReplaceConnectionSnapshot(conn with { EquippedFrameRef = selected });
                }
                _ringPicker.NotifySaved();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    _ringPicker.NotifyError(HubErrorText.Localize(ex));
                    UiHost.Log.Warning(ex, "[MyProfileScreen] SetAvatarRingAsync failed.");
                }
            }
        }, ct);
    }
}
