namespace AetherOS.Apps.Aetherling;

/// <summary>Something the app wants drawn outside the phone. The app hands one to the host, which owns the
/// only window in the process that is allowed to sit over the game.</summary>
public interface IAetherlingOverlay
{
    /// <summary>Whether it should be on screen at all this frame.</summary>
    bool Visible { get; }

    /// <summary>Draws it, including its own window.</summary>
    void Draw();
}
