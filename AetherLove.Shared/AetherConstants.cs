namespace AetherLove.Shared;

/// <summary>Build-time constants shared between the plugin and the server.</summary>
public static class AetherConstants
{
#if DEBUG
    //public const string ServerBaseUrl = "https://localhost:7246/";
    public const string ServerBaseUrl = "https://apibeta.aetherlove.space/";
    //public const string ServerBaseUrl = "https://api.aetherlove.space/";
#else
    public const string ServerBaseUrl = "https://api.aetherlove.space/";
#endif

    /// <summary>Public Patreon campaign page opened by the in-app "Become a Supporter" button. A stable public
    /// link, kept client-side so it stays correct regardless of server config.</summary>
    public const string PatreonCampaignUrl = "https://www.patreon.com/cw/FFXIV_Aether";
}
