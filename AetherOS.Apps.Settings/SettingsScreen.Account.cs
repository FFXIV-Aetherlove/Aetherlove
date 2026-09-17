using AetherLove;
using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Settings;

public sealed partial class SettingsScreen
{
    private readonly AetherFileDialogManager _recoveryPicker = new();
    private string _newPassphrase = "";
    private string _confirmPassphrase = "";
    private volatile bool _accountBusy;
    private volatile string? _accountResult;

    private void DrawMyAccount()
    {
        _recoveryPicker.Draw();
        DrawSubpageBack();
        DrawSubpageHeading(Loc.T("account.title"), PadX);
        using var scroll = ImRaii.Child("##accountRecovery", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
        if (!scroll) { return; }
        ImGui.Indent(Px(PadX));
        ImGui.TextWrapped(Loc.T(_host.EncryptionStateKey));
        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("account.change_help"));
        ImGui.TextUnformatted(Loc.T("account.new_passphrase"));
        ImGui.SetNextItemWidth(-Px(PadX));
        ImGui.InputText("##newPassphrase", ref _newPassphrase, 256, ImGuiInputTextFlags.Password);
        ImGui.TextUnformatted(Loc.T("account.repeat"));
        ImGui.SetNextItemWidth(-Px(PadX));
        ImGui.InputText("##confirmPassphrase", ref _confirmPassphrase, 256, ImGuiInputTextFlags.Password);
        using (ImRaii.Disabled(_accountBusy || !_host.EncryptionReady || _newPassphrase.Length < 12 || _newPassphrase != _confirmPassphrase))
        {
            if (SharedUiHelpers.Button(Loc.T("account.change"), new Vector2(-Px(PadX), Px(36))))
            {
                var pass = _newPassphrase;
                _newPassphrase = _confirmPassphrase = "";
                RunAccountAction(() => _host.ChangePassphraseAsync(pass));
            }
        }
        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("account.backup_help"));
        using (ImRaii.Disabled(_accountBusy || !_host.EncryptionReady))
        {
            if (SharedUiHelpers.Button(Loc.T("account.save"), new Vector2(-Px(PadX), Px(36))))
            {
                _recoveryPicker.SaveFileDialog(Loc.T("account.save"), ".aetherkey", "AetherLove-recovery", ".aetherkey",
                    (ok, path) => { if (ok) { RunAccountAction(() => _host.SaveRecoveryFileAsync(path)); } });
            }
        }
        using (ImRaii.Disabled(_accountBusy || _host.Account is null))
        {
            if (SharedUiHelpers.Button(Loc.T("account.restore"), new Vector2(-Px(PadX), Px(36))))
            {
                _recoveryPicker.OpenFileDialog(Loc.T("account.restore"), ".aetherkey", (ok, path) =>
                {
                    if (ok) { RunAccountAction(() => _host.RestoreRecoveryFileAsync(path)); }
                });
            }
            if (SharedUiHelpers.Button(Loc.T("account.retry"), new Vector2(-Px(PadX), Px(36)))) { RunAccountAction(_host.RefreshEncryptionAsync); }
        }
        if (_accountBusy) { ImGui.TextWrapped(Loc.T("account.working")); }
        if (_accountResult is { } result) { ImGui.TextWrapped(Loc.T(result)); }
        ImGui.Unindent(Px(PadX));
    }

    private void RunAccountAction(Func<Task> action)
    {
        if (_accountBusy) { return; }
        _accountBusy = true;
        _accountResult = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
                _accountResult = "account.success";
            }
            catch (Exception ex)
            {
                _accountResult = "account.failed";
                UiHost.Log.Warning("[MyAccount] Operation failed ({Reason}).", ex.GetType().Name);
            }
            finally { _accountBusy = false; }
        });
    }
}
