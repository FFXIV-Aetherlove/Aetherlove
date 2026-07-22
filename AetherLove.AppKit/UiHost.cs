using AetherLove.Config;
using AetherLove.Emoji;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AetherLove;

/// <summary>Dalamud services and shared state for client-side code that lives outside the plugin assembly;
/// the plugin initialises it at startup before any of this library's types are used.</summary>
public static class UiHost
{
    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    public static IPluginLog Log { get; private set; } = null!;
    public static ITextureProvider TextureProvider { get; private set; } = null!;
    public static IGameConfig GameConfig { get; private set; } = null!;
    public static IDataManager DataManager { get; private set; } = null!;
    public static IObjectTable ObjectTable { get; private set; } = null!;
    public static IClientState ClientState { get; private set; } = null!;
    public static Configuration Configuration { get; private set; } = null!;
    public static EmojiService EmojiService { get; private set; } = null!;

    public static void Initialise(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        ITextureProvider textureProvider,
        IGameConfig gameConfig,
        IDataManager dataManager,
        IObjectTable objectTable,
        IClientState clientState,
        Configuration configuration)
    {
        PluginInterface = pluginInterface;
        Log = log;
        TextureProvider = textureProvider;
        GameConfig = gameConfig;
        DataManager = dataManager;
        ObjectTable = objectTable;
        ClientState = clientState;
        Configuration = configuration;
    }

    public static void SetEmojiService(EmojiService emojiService)
    {
        EmojiService = emojiService;
    }
}
