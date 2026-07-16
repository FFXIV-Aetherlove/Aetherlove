using System;
using System.IO;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Emoji;
using AetherLove.Navigation;
using AetherLove.Screens;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using AetherLove.Services.Hub;
using AetherLove.Services.Signal;
using AetherLove.UI;
using AetherLove.Windows;
using Dalamud.Game.Config;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AetherLove;

/// <summary>Plugin entry point.</summary>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    internal static string ServerBaseUrl => AetherLove.Shared.AetherConstants.ServerBaseUrl;

    internal static Configuration Configuration { get; private set; } = null!;
    internal static EmojiService EmojiService { get; private set; } = null!;

    private readonly IHost _host;

    public Plugin()
    {
        Configuration = (PluginInterface.GetPluginConfig() as Configuration) ?? new Configuration();

        UiScale.Apply(Configuration.PhoneSize);
        MiniScale.Apply(Configuration.MiniPhoneSize);
        ThemeService.Initialise(Configuration);
        LanguageProvider.Initialise(Configuration);
        EmojiService = new EmojiService();

        var configDir = PluginInterface.ConfigDirectory.FullName;
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        // Clear image caches left by a prior session (a crash skips the shutdown purge) before anything repopulates them.
        ImageCacheCleaner.PurgeAll();

        _host = new HostBuilder()
            .UseContentRoot(configDir)
            .ConfigureServices(ConfigureServices)
            .Build();

        _ = _host.StartAsync().ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception is not null)
            {
                Log.Error(t.Exception, "[AetherLove] Host startup failed.");
            }
        }, TaskScheduler.Default);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(PluginInterface);
        services.AddSingleton(Log);
        services.AddSingleton(TextureProvider);
        services.AddSingleton(ObjectTable);
        services.AddSingleton(ClientState);
        services.AddSingleton(DataManager);
        services.AddSingleton(CommandManager);
        services.AddSingleton(GameConfig);
        services.AddSingleton(NotificationManager);
        services.AddSingleton(ChatGui);

        services.AddSingleton(Configuration);
        services.AddSingleton(new WindowSystem("AetherLove"));
        services.AddSingleton(new ScreenRouter(Screen.Splash));
        services.AddSingleton<NotificationCenter>();
        services.AddSingleton<NotificationDispatcher>();
        services.AddSingleton<PulseService>();
        services.AddSingleton<ChatCategoryStore>();
        services.AddSingleton<Widgets.RateLimitModal>();
        services.AddSingleton<Widgets.SaveErrorModal>();
        services.AddSingleton<Widgets.ImageRequirementsModal>();
        services.AddSingleton<Services.Crypto.CryptoService>();
        services.AddSingleton<Services.Crypto.KeyStorageService>();
        services.AddSingleton<ChatEventBus>();
        services.AddSingleton<Services.Chat.ChatCacheStore>();
        services.AddSingleton<Services.Chat.ChatSyncService>();
        services.AddSingleton<Services.Hangouts.HangoutStateService>();
        services.AddSingleton<Widgets.HangoutOverlay>();
        services.AddSingleton<WebpCapabilityProbe>();
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<Widgets.SelfieCaptureOverlay>();
        services.AddSingleton<MaintenanceNoticeService>();

        services.AddHttpClient<TokenService>(c =>
        {
            c.BaseAddress = new Uri(ServerBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient<AuthService>(c =>
        {
            c.BaseAddress = new Uri(ServerBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddSingleton<AetherSignalService>();
        services.AddSingleton<AetherLoveHubClient>();
        services.AddSingleton<Services.Patreon.PatreonLinkFlow>();
        services.AddSingleton<SessionBootstrapper>();
        services.AddSingleton<PendingMatchContext>();
        services.AddSingleton<VenueShareContext>();
        services.AddSingleton<HangoutShareContext>();
        services.AddSingleton<Widgets.HangoutSharePicker>();
        services.AddSingleton<Widgets.SupporterThanksScene>();
        services.AddSingleton<OwnAvatarCache>();
        services.AddSingleton<FlairCatalog>();

        services.AddSingleton<SplashScreen>();
        services.AddSingleton<OnboardingScreen>();
        services.AddSingleton<HangoutsScreen>();
        services.AddSingleton<BlockedScreen>();
        services.AddSingleton<ProfileScreen>();
        services.AddSingleton<DeckScreen>();
        services.AddSingleton<MatchScreen>();
        services.AddSingleton<IMatchEffect, MatchClassicScreen>();
        services.AddSingleton<IMatchEffect, MatchCosmicScreen>();
        services.AddSingleton<IMatchEffect, MatchSynthwaveScreen>();
        services.AddSingleton<IMatchEffect, MatchAuroraScreen>();
        services.AddSingleton<IMatchEffect, MatchKaleidoscopeScreen>();
        services.AddSingleton<IMatchEffect, MatchSupernovaScreen>();
        services.AddSingleton<IMatchEffect, MatchVortexScreen>();
        services.AddSingleton<IMatchEffect, MatchPortalRiftScreen>();
        services.AddSingleton<IMatchEffect, MatchElectricStormScreen>();
        services.AddSingleton<IMatchEffect, MatchBubbleMergeScreen>();
        services.AddSingleton<IMatchEffect, MatchDnaHelixScreen>();
        services.AddSingleton<IMatchEffect, MatchFireworkScreen>();
        services.AddSingleton<IMatchEffect, MatchVinylScreen>();
        services.AddSingleton<IMatchEffect, MatchArcadeScreen>();
        services.AddSingleton<IMatchEffect, MatchConstellationScreen>();
        services.AddSingleton<IMatchEffect, MatchSlotMachineScreen>();
        services.AddSingleton<IMatchEffect, MatchTarotScreen>();
        services.AddSingleton<IMatchEffect, MatchLavaLampScreen>();
        services.AddSingleton<IMatchEffect, MatchSkyLanternsScreen>();
        services.AddSingleton<IMatchEffect, MatchTreasureChestScreen>();
        services.AddSingleton<ChatListScreen>();
        services.AddSingleton<ChatCategoryScreen>();
        services.AddSingleton<ChatScreen>();
        services.AddSingleton<SettingsScreen>();
        services.AddSingleton<MyProfileScreen>();
        services.AddSingleton<PlacesScreen>();
        services.AddSingleton<MyVenuesScreen>();
        services.AddSingleton<BannedScreen>();
        services.AddSingleton<WarningAcknowledgeScreen>();
        services.AddSingleton<ModeratorMessageScreen>();
        services.AddSingleton<PassphraseUnlockScreen>();
        services.AddSingleton<EncryptionRecoveryScreen>();
        services.AddSingleton<EncryptionVerificationScreen>();
        services.AddSingleton<NewsScreen>();
        services.AddSingleton<OfflineScreen>();
        services.AddSingleton<OutdatedScreen>();

        services.AddSingleton<MainPluginWindow>();
        services.AddSingleton<MiniWindow>();
        services.AddSingleton<ChangelogWindow>();
        services.AddSingleton<DebugWindow>();

        services.AddSingleton<AetherLoveBootstrap>();
        services.AddHostedService(sp => sp.GetRequiredService<AetherLoveBootstrap>());
    }

    public void Dispose()
    {
        try
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AetherLove] Host shutdown failed.");
        }
        finally
        {
            _host.Dispose();
            ImageCacheCleaner.PurgeAll();
        }
    }
}
