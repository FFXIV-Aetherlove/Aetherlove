namespace AetherLove.Shared;

/// <summary>Build-time constants shared between the plugin and the server.</summary>
public static class AetherConstants
{
#if DEBUG
    //public const string ServerBaseUrl = "https://localhost:7246/";
    public const string ServerBaseUrl = "https://apibeta.aetherlove.space/";
#else
    public const string ServerBaseUrl = "https://api.aetherlove.space/";
#endif
}
