namespace AetherOS.Apps.Aetherling;

/// <summary>Dev-only: what the plugin's interact lab may poke. The app parks an implementation on the
/// host at startup (the Overlay handover's pattern), so the lab window drives the real creature through
/// the real channels without knowing the app's insides.</summary>
public interface IAetherlingInteractLab
{
    /// <summary>Plays a choreography by key, gates forced open: the lab exists to see things.</summary>
    void PlayEmote(string key, float amplitude);

    /// <summary>Shows a glyph (or a two-glyph saying) through the audition door.</summary>
    void ShowGlyph(string name, string? then, string element);

    /// <summary>One status line for the lab's header.</summary>
    string Status { get; }
}

/// <summary>Something the app wants drawn outside the phone. The app hands one to the host, which owns the
/// only window in the process that is allowed to sit over the game.</summary>
public interface IAetherlingOverlay
{
    /// <summary>Whether it should be on screen at all this frame.</summary>
    bool Visible { get; }

    /// <summary>Draws it, including its own window.</summary>
    void Draw();
}
