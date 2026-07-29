namespace AetherOS.Sdk;

/// <summary>Host/system side effects an app surface cannot perform on its own.</summary>
public interface ISystemBridge
{
    /// <summary>Opens a URL in the user's default browser.</summary>
    void OpenUrl(string url);

    /// <summary>Copies text to the system clipboard.</summary>
    void CopyToClipboard(string text);

    /// <summary>Opens a local folder in the system file explorer.</summary>
    void OpenFolder(string path);

    /// <summary>Drops a flag on the in-game map at the given territory/map and 2-decimal map coordinates,
    /// like clicking a chat map link. No-op when the game map can't be opened.</summary>
    void OpenMapMarker(uint territoryId, uint mapId, float mapX, float mapY, string? label = null);

    /// <summary>Executes the emote bound to a chat text command such as "/wave" on the player's character.
    /// Arguments after the command are ignored. Returns false when the command matches no emote.</summary>
    bool TryExecuteEmote(string chatCommand);
}
