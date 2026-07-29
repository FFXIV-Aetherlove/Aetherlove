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
using AetherOS.Shell;
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
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IMarketBoard MarketBoard { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;

    internal static string ServerBaseUrl => AetherLove.Shared.AetherConstants.ServerBaseUrl;

    internal static Configuration Configuration { get; private set; } = null!;
    internal static EmojiService EmojiService { get; private set; } = null!;

    private readonly IHost _host;

    public Plugin()
    {
        Configuration = LoadConfiguration();

        UiHost.Initialise(PluginInterface, Log, TextureProvider, GameConfig,
            DataManager, ObjectTable, ClientState, Configuration);

        UiScale.Apply(Configuration.PhoneSize);
        MiniScale.Apply(Configuration.MiniPhoneSize);
        ThemeService.Initialise(Configuration);
        LanguageProvider.Initialise(Configuration);
        EmojiService = new EmojiService();
        UiHost.SetEmojiService(EmojiService);

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
        services.AddSingleton(DtrBar);

        services.AddSingleton(Configuration);
        services.AddSingleton(new WindowSystem("AetherLove"));
        services.AddSingleton(new ScreenRouter(Screen.Splash));
        services.AddSingleton<NotificationCenter>();
        services.AddSingleton<SiblingBadgeStore>();
        services.AddSingleton<NotificationDispatcher>();
        services.AddSingleton<Services.Signal.INotifier>(sp => sp.GetRequiredService<NotificationDispatcher>());
        services.AddSingleton<Services.Signal.ISignalHost, Services.Signal.SignalHostBridge>();
        services.AddSingleton<PulseService>();
        services.AddSingleton<Services.TomestoneEmoteService>();
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
        services.AddSingleton<Services.Messenger.MessengerStore>();
        services.AddSingleton<Services.Messenger.MessengerCryptoService>();
        services.AddSingleton<Services.Messenger.MessengerSyncService>();
        services.AddSingleton<Services.Auth.PassphraseResetFlow>();
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
        services.AddSingleton<AetherHubContext>();
        services.AddSingleton<Services.Patreon.PatreonLinkFlow>();
        services.AddSingleton<SessionBootstrapper>();
        services.AddSingleton<Services.Auth.AccountUnlockService>();
        services.AddSingleton<PendingMatchContext>();
        services.AddSingleton<VenueShareContext>();
        services.AddSingleton<HangoutShareContext>();
        services.AddSingleton<NewsShareContext>();
        services.AddSingleton<CalendarShareContext>();
        services.AddSingleton<LevemeteShareContext>();
        services.AddSingleton<MarketShareContext>();
        services.AddSingleton<OwnAvatarCache>();
        services.AddSingleton<OsAvatarCache>();
        services.AddSingleton<FlairCatalog>();

        services.AddSingleton<SplashScreen>();
        services.AddSingleton<OsOnboardingScreen>();
        services.AddSingleton<BannedScreen>();
        services.AddSingleton<WarningAcknowledgeScreen>();
        services.AddSingleton<ModeratorMessageScreen>();
        services.AddSingleton<PassphraseUnlockScreen>();
        services.AddSingleton<EncryptionRecoveryScreen>();
        services.AddSingleton<OfflineScreen>();
        services.AddSingleton<OutdatedScreen>();

        services.AddSingleton<Os.AppStorageService>();
        services.AddSingleton<Os.AppCapabilities>();
        services.AddSingleton<AetherOS.Sdk.IAppCapabilities>(sp => sp.GetRequiredService<Os.AppCapabilities>());
        services.AddSingleton<Os.ISocialBridge, Os.SocialBridgeService>();

        services.AddSingleton<Os.LoveHostService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Love.AetherLoveApp(
            sp.GetRequiredService<Os.LoveHostService>(),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>(),
            sp.GetRequiredService<AetherHubContext>(),
            sp.GetRequiredService<PendingMatchContext>(),
            sp.GetRequiredService<NotificationCenter>(),
            sp.GetRequiredService<OwnAvatarCache>(),
            sp.GetRequiredService<FlairCatalog>(),
            sp.GetRequiredService<ChatEventBus>(),
            sp.GetRequiredService<Services.Crypto.CryptoService>(),
            sp.GetRequiredService<Services.Crypto.KeyStorageService>(),
            sp.GetRequiredService<ChatCategoryStore>(),
            sp.GetRequiredService<Services.Chat.ChatSyncService>(),
            sp.GetRequiredService<Services.Chat.ChatCacheStore>(),
            sp.GetRequiredService<AetherSignalService>(),
            sp.GetRequiredService<TokenService>(),
            sp.GetRequiredService<SessionBootstrapper>(),
            sp.GetRequiredService<VenueShareContext>(),
            sp.GetRequiredService<HangoutShareContext>(),
            sp.GetRequiredService<NewsShareContext>(),
            sp.GetRequiredService<CalendarShareContext>(),
            sp.GetRequiredService<LevemeteShareContext>(),
            sp.GetRequiredService<MarketShareContext>(),
            sp.GetRequiredService<Services.Patreon.PatreonLinkFlow>(),
            sp.GetRequiredService<SiblingBadgeStore>(),
            sp.GetRequiredService<Services.Messenger.MessengerStore>(),
            sp.GetRequiredService<Services.Market.MarketDataService>(),
            sp.GetRequiredService<Services.Market.MarketItemIndex>()));
        services.AddSingleton<Os.SettingsHostService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Settings.SettingsApp(
            () => Services.Localization.Loc.T("common.nav_settings"),
            sp.GetRequiredService<Os.SettingsHostService>(),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>()));
        services.AddSingleton<Os.ClockAlarmService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Clock.ClockApp(
            () => Services.Localization.Loc.T("os.app_clock"),
            sp.GetRequiredService<Os.ClockAlarmService>()));
        services.AddSingleton<Os.WeatherStationService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Weather.WeatherApp(
            () => Services.Localization.Loc.T("os.app_weather"),
            sp.GetRequiredService<Os.WeatherStationService>()));
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Messenger.MessengerApp(
            () => Services.Localization.Loc.T("os.app_messenger"),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>(),
            sp.GetRequiredService<Services.Messenger.MessengerStore>(),
            sp.GetRequiredService<Services.Messenger.MessengerSyncService>(),
            sp.GetRequiredService<Services.Messenger.MessengerCryptoService>(),
            sp.GetRequiredService<AetherHubContext>(),
            sp.GetRequiredService<Services.Hangouts.HangoutStateService>(),
            sp.GetRequiredService<VenueShareContext>(),
            sp.GetRequiredService<HangoutShareContext>(),
            sp.GetRequiredService<LevemeteShareContext>(),
            sp.GetRequiredService<Services.Market.MarketDataService>(),
            sp.GetRequiredService<Services.Market.MarketItemIndex>()));
        services.AddSingleton<Os.PlacesHostService>();
        services.AddSingleton<Os.HangoutsHostService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Places.PlacesApp(
            () => Services.Localization.Loc.T("common.nav_places"),
            () => sp.GetRequiredService<SessionBootstrapper>().LastConnection?.PlacesEnabled != false,
            sp.GetRequiredService<Os.PlacesHostService>(),
            sp.GetRequiredService<Os.ISocialBridge>(),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>()));
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Hangouts.HangoutsApp(
            () => Services.Localization.Loc.T("common.nav_hangouts"),
            () => sp.GetRequiredService<SessionBootstrapper>().LastConnection?.HangoutsEnabled != false,
            sp.GetRequiredService<Os.HangoutsHostService>(),
            sp.GetRequiredService<Os.ISocialBridge>(),
            sp.GetRequiredService<Services.Hangouts.HangoutStateService>()));
        services.AddSingleton<Os.LevemetesHostService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Levemetes.LevemetesApp(
            () => Services.Localization.Loc.T("os.app_levemetes"),
            () => sp.GetRequiredService<SessionBootstrapper>().LastConnection?.LevemetesEnabled != false,
            sp.GetRequiredService<Os.LevemetesHostService>(),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>()));
        services.AddSingleton<Os.NewsHostService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.News.NewsApp(
            () => Services.Localization.Loc.T("os.app_news_tile"),
            sp.GetRequiredService<Os.NewsHostService>()));
        services.AddSingleton<Services.Market.UniversalisClient>();
        services.AddSingleton<Services.Market.MarketDataService>();
        services.AddSingleton(sp => new Services.Market.MarketItemIndex(
            sp.GetRequiredService<Services.Market.UniversalisClient>(),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>().Storage("market")));
        services.AddSingleton(sp => new Services.Market.MarketUserStore(
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>().Storage("market")));
        services.AddSingleton(sp => new Services.Market.MarketAlertStore(
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>().Storage("market")));
        services.AddSingleton<Os.MarketDeskService>();
        services.AddSingleton<AetherOS.Apps.Market.IMarketDesk>(sp => sp.GetRequiredService<Os.MarketDeskService>());
        services.AddSingleton<Os.MarketContextMenuService>();
        services.AddSingleton<Services.MarketAlertService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Market.MarketApp(
            () => Services.Localization.Loc.T("os.app_market"),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>(),
            sp.GetRequiredService<Services.Market.MarketDataService>(),
            sp.GetRequiredService<Services.Market.MarketItemIndex>(),
            sp.GetRequiredService<Services.Market.MarketUserStore>(),
            sp.GetRequiredService<Services.Market.MarketAlertStore>(),
            sp.GetRequiredService<AetherOS.Apps.Market.IMarketDesk>()));
        services.AddSingleton<Services.Realtor.PaissaClient>();
        services.AddSingleton<Services.Realtor.RealtorDataService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Realtor.RealtorApp(
            () => Services.Localization.Loc.T("os.app_realtor"),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>(),
            sp.GetRequiredService<Services.Realtor.RealtorDataService>()));
        services.AddSingleton<Os.FeedbackHostService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Feedback.FeedbackApp(
            () => Services.Localization.Loc.T("os.app_feedback"),
            sp.GetRequiredService<Os.FeedbackHostService>()));
        services.AddSingleton<Os.CameraLibraryService>();
        services.AddSingleton<AetherOS.Apps.Camera.ICameraLibrary>(sp => sp.GetRequiredService<Os.CameraLibraryService>());
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Camera.CameraApp(
            () => Services.Localization.Loc.T("os.app_camera"),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>(),
            sp.GetRequiredService<AetherOS.Apps.Camera.ICameraLibrary>()));
        services.AddSingleton<Os.PhotoLibraryService>();
        services.AddSingleton<Os.ScreenshotImportService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Photos.PhotosApp(
            () => Services.Localization.Loc.T("os.app_photos"),
            sp.GetRequiredService<Os.PhotoLibraryService>(),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>()));
        services.AddSingleton<Os.CalendarHostService>();
        services.AddSingleton<AetherOS.Sdk.IAetherApp>(sp => new AetherOS.Apps.Calendar.CalendarApp(
            () => Services.Localization.Loc.T("os.app_calendar"),
            sp.GetRequiredService<Os.CalendarHostService>(),
            sp.GetRequiredService<AetherOS.Sdk.IAppCapabilities>()));
        services.AddSingleton<Os.IOsAccountInfo, Os.OsAccountInfo>();
        services.AddAetherOsShell();

        services.AddSingleton<MainPluginWindow>();
        services.AddSingleton<MiniWindow>();
        services.AddSingleton<Services.DtrBarService>();
        services.AddSingleton<ChangelogWindow>();
        services.AddSingleton<DebugWindow>();

        services.AddSingleton<AetherLoveBootstrap>();
        services.AddHostedService(sp => sp.GetRequiredService<AetherLoveBootstrap>());
    }

    /// <summary>Loads the persisted config, rewriting legacy assembly tokens on disk.</summary>
    private static Configuration LoadConfiguration()
    {
        try
        {
            var file = PluginInterface.ConfigFile;
            if (file is { Exists: true })
            {
                var text = File.ReadAllText(file.FullName);
                var cfg = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(
                    text,
                    new Newtonsoft.Json.JsonSerializerSettings
                    {
                        TypeNameHandling = Newtonsoft.Json.TypeNameHandling.All,
                        SerializationBinder = new PluginContextBinder(),
                    });
                if (cfg is not null)
                {
                    // Migrate legacy assembly tokens to AetherLove.AppKit.
                    if (text.Contains(", AetherLovePlugin\"", StringComparison.Ordinal)
                        || text.Contains(", AetherLove.ClientCore\"", StringComparison.Ordinal))
                    {
                        var fixedText = System.Text.RegularExpressions.Regex.Replace(
                            text,
                            "(\"AetherLove\\.Config\\.[^\"]+?), (?:AetherLovePlugin|AetherLove\\.ClientCore)\"",
                            "$1, AetherLove.AppKit\"");
                        File.WriteAllText(file.FullName, fixedText);
                        Log.Information("[AetherLove] Migrated config type names to AetherLove.AppKit.");
                    }
                    return cfg;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AetherLove] Self-load of config failed; falling back to Dalamud.");
        }

        // Dalamud's loader can serve a ReliableFileStorage BACKUP even when the json itself was deleted, and it
        // deserializes with Newtonsoft's default binder, which cannot resolve types living in the plugin's
        // collectible ALC ("Resolving to a collectible assembly is not supported"). That must never kill the
        // plugin load: a wipe-and-reinstall lands here on purpose and gets a fresh configuration.
        try
        {
            if (PluginInterface.GetPluginConfig() is Configuration cfg)
            {
                return cfg;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AetherLove] Dalamud config load failed; starting with a fresh configuration.");
        }
        return new Configuration();
    }

    /// <summary>Resolves persisted <c>$type</c> tokens against the known assemblies by type name, bypassing assembly mismatches.</summary>
    private sealed class PluginContextBinder : Newtonsoft.Json.Serialization.ISerializationBinder
    {
        private static readonly System.Reflection.Assembly[] Known =
        {
            typeof(Plugin).Assembly,
            typeof(Config.Configuration).Assembly,
            typeof(AetherOS.Sdk.OsConfig).Assembly,
        };

        public Type BindToType(string? assemblyName, string typeName)
        {
            foreach (var asm in Known)
            {
                var t = asm.GetType(typeName, throwOnError: false);
                if (t is not null)
                {
                    return t;
                }
            }
            var full = string.IsNullOrEmpty(assemblyName) ? typeName : typeName + ", " + assemblyName;
            return Type.GetType(full) ?? throw new Newtonsoft.Json.JsonSerializationException("Cannot resolve type " + full);
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            assemblyName = serializedType.Assembly.GetName().Name;
            typeName = serializedType.FullName;
        }
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
            Services.NotificationSoundPlayer.Stop();
            ImageCacheCleaner.PurgeAll();
        }
    }
}
