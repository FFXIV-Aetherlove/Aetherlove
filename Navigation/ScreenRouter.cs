namespace AetherLove.Navigation;

/// <summary>
/// Holds which screen the phone is currently showing, and flags when it changes. Screens never switch to
/// each other directly — they call <see cref="Navigate"/>, and the main window reads <see cref="Current"/>
/// each frame to draw the matching screen. Access is locked because navigation can be triggered off the
/// UI thread (e.g. from a server push).
/// </summary>
public class ScreenRouter
{
    private readonly object _lock = new();
    private Screen _current;
    private bool _navigationOccurred;

    public ScreenRouter(Screen initialScreen = Screen.Onboarding)
    {
        _current = initialScreen;
    }

    public Screen Current
    {
        get { lock (_lock) { return _current; } }
    }

    public bool NavigationOccurred
    {
        get { lock (_lock) { return _navigationOccurred; } }
        set { lock (_lock) { _navigationOccurred = value; } }
    }

    public void Navigate(Screen newScreen)
    {
        lock (_lock)
        {
            _current = newScreen;
            _navigationOccurred = true;
        }
    }
}
