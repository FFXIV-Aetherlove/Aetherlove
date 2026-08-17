using AetherLove.Os;
using AetherLove.Screens;
using AetherOS.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace AetherOS.Shell;

public static class ShellServiceCollectionExtensions
{
    /// <summary>Registers the AetherOS shell runtime: the app registry (<see cref="OsShell"/>/<see cref="IOsShell"/>),
    /// wallpapers, notification shade, status bar, and the home launcher. The host plugin still supplies the
    /// <see cref="IOsAccountInfo"/> implementation and the shared <c>ScreenRouter</c> singleton.</summary>
    public static IServiceCollection AddAetherOsShell(this IServiceCollection services)
    {
        services.AddSingleton<OsShell>();
        services.AddSingleton<IOsShell>(sp => sp.GetRequiredService<OsShell>());
        services.AddSingleton<OsTour>();
        services.AddSingleton<NewAppOffer>();
        services.AddSingleton<WallpaperService>();
        services.AddSingleton<NotificationShade>();
        services.AddSingleton<StatusBar>();
        services.AddSingleton<HomeScreen>();
        services.AddSingleton<ShareSheet>();
        services.AddSingleton<ShareService>();
        services.AddSingleton<IShareService>(sp => sp.GetRequiredService<ShareService>());
        return services;
    }
}
