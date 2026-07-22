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

    public void RequestWarningLiveAcknowledge() =>
        _services.GetRequiredService<WarningAcknowledgeScreen>().RequestLiveAcknowledge();

    public void RequestModeratorLiveAcknowledge() =>
        _services.GetRequiredService<ModeratorMessageScreen>().RequestLiveAcknowledge();
}
