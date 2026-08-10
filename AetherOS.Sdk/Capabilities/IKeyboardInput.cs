namespace AetherOS.Sdk;

/// <summary>The keys an app can poll. Deliberately limited to what a game surface needs: every key here is
/// swallowed from the game while an app reads it, so the set stays small enough to be safe. The order is
/// load-bearing, because the host maps it positionally onto its ImGui and game key tables.</summary>
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
    E,
    Ctrl,
    Shift,
    Enter,
    Escape,
    Tab,
    D1,
    D2,
    D3,
    D4,
    D5,
    D6,
    D7,
    Y,
    N,
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

    /// <summary>Demands the keyboard for this frame, even while ImGui reports something else as active. Call it
    /// every frame the app wants it, immediately before reading any key; it lapses on its own the moment the app
    /// stops asking.
    ///
    /// Normally the capture declines to re-take focus while any item is active, so it can never steal a held
    /// button out from under the user. That politeness costs the keyboard whenever the mouse is pressed on bare
    /// window space, because ImGui makes the window's own move handle the active item, and the keys then reach
    /// the game as well as the app. An app that hit-tests its controls by hand owns no items worth protecting,
    /// so it can ask for the keyboard unconditionally. An app built from ordinary ImGui widgets must NOT: taking
    /// focus back mid-press is exactly what stops those widgets firing.</summary>
    void RequestExclusive()
    {
    }
}
