namespace AetherOS.Sdk;

/// <summary>The keys an app can poll. Deliberately limited to movement and fire: every key here is
/// swallowed from the game while an app reads it, so the set stays small enough to be safe.</summary>
public enum AppKey
{
    Left,
    Right,
    Up,
    Down,
    W,
    A,
    S,
    D,
    Space,
}

/// <summary>Live key state for apps that are driven by the keyboard (the arcade games).
///
/// Polling TAKES KEYBOARD FOCUS: while an app reads keys, the game receives none of them, so steering with
/// WASD never also walks your character. That also means game hotkeys and the chat box are unreachable while
/// you poll, so only poll while your app genuinely wants the keys (a live run), never on a menu or a pause
/// screen. Polling stops on its own while a game text field is already open.</summary>
public interface IKeyboardInput
{
    /// <summary>True while the key is held.</summary>
    bool IsDown(AppKey key);

    /// <summary>True on the frame the key goes down. Poll every frame or the edge is missed.</summary>
    bool WasPressed(AppKey key);

    /// <summary>True while a GAME text field owns the keyboard (the chat box is open). No keys are reaching
    /// the app at all in that state, so anything mid-run should pause rather than play itself out.</summary>
    bool GameTextFocused { get; }
}
