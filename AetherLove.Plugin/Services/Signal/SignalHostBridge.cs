using System;
using AetherLove.Screens;
using AetherLove.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace AetherLove.Services.Signal;

/// <summary>Plugin-side <see cref="ISignalHost"/>: the phone window's open state and the gate screens the push
/// handlers touch. The window and gate screens are resolved lazily to avoid ctor cycles.</summary>
public sealed class SignalHostBridge : ISignalHost
{
    private readonly IServiceProvider _services;

    public SignalHostBridge(IServiceProvider services)
    {
        _services = services;
    }

    public bool IsPhoneOpen => _services.GetRequiredService<MainPluginWindow>().IsOpen;

    public bool IsAppInForeground(string appId)
    {
        if (!_services.GetRequiredService<MainPluginWindow>().IsOpen)
        {
            return false;
        }
        if (_services.GetRequiredService<Navigation.ScreenRouter>().Current != Navigation.Screen.App)
        {
            return false;
        }
        return _services.GetRequiredService<Os.OsShell>().ActiveSurfaceApp?.Id == appId;
    }

    public void RequestWarningLiveAcknowledge() =>
        _services.GetRequiredService<WarningAcknowledgeScreen>().RequestLiveAcknowledge();

    public void RequestModeratorLiveAcknowledge() =>
        _services.GetRequiredService<ModeratorMessageScreen>().RequestLiveAcknowledge();

    public void RequestStaffNoticeLiveAcknowledge() =>
        _services.GetRequiredService<StaffNoticeScreen>().RequestLiveAcknowledge();

    public void RefreshStaffNoticeGate() =>
        _services.GetRequiredService<StaffNoticeScreen>().RefreshBatch();
}
