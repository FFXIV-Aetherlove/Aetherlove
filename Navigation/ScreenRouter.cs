namespace AetherLove.Navigation;

/// <summary>Which screen the phone is showing, and a flag set when it changes. Access is locked because
/// navigation can be triggered off the UI thread.</summary>
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
