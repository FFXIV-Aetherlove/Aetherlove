namespace AetherLove.Config;

/// <summary>How AetherLove was presented at the last unload, persisted so a mid-session reload can restore it.</summary>
public enum WindowOpenState
{
    /// <summary>Neither window showing.</summary>
    Closed = 0,
    /// <summary>The full phone.</summary>
    Full = 1,
    /// <summary>The minimised bubble.</summary>
    Minimized = 2,
}
