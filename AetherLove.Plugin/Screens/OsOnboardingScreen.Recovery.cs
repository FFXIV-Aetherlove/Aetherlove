using System;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Screens;

public sealed partial class OsOnboardingScreen
{
    private volatile bool _backupSaving;
    private volatile bool _backupFailed;

    private void DrawRecoveryBackup()
    {
        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("account.backup_title"));
        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("account.backup_required"));
        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("account.backup_help"));
        if (OnboardingUi.DrawPrimaryButton(Loc.T("account.save"), !_backupSaving))
        {
            _fileDialog.SaveFileDialog(Loc.T("account.save"), ".aetherkey", "AetherLove-recovery", ".aetherkey", (ok, path) =>
            {
                if (!ok || _backupSaving) { return; }
                _backupSaving = true;
                _backupFailed = false;
                _ = Task.Run(async () =>
                {
                    try { await _encryption.SaveRecoveryFileAsync(path).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        _backupFailed = true;
                        Plugin.Log.Warning("[Onboarding] Recovery save failed ({Reason}).", ex.GetType().Name);
                    }
                    finally { _backupSaving = false; }
                });
            });
        }
        if (_backupSaving) { ImGui.TextWrapped(Loc.T("account.working")); }
        if (_backupFailed) { ImGui.TextWrapped(Loc.T("account.failed")); }
    }
}
