using AetherLove.Os;
using AetherLove.Windows;
using AetherOS.Apps.Groove;
using AetherOS.Sdk;
using Dalamud.Plugin.Services;

namespace AetherLove.Services;

/// <summary>Publishes the current track to the server info bar through <see cref="ServerBarService"/>
/// (ADR 21). This service only knows WHAT to say; every gate, the toggles included, belongs to the
/// bar service.</summary>
public sealed class GrooveDtrService
{
    private const double PollSeconds = 0.5;
    private const int TitleMaxChars = 30;

    private readonly ServerBarService _serverBar;
    private readonly GrooveHostService _host;
    private readonly GrooveSettings _settings;
    private readonly MainPluginWindow _mainWindow;

    private IServerBarEntry? _entry;
    private double _accum;

    public GrooveDtrService(ServerBarService serverBar, GrooveHostService host, GrooveSettings settings,
        MainPluginWindow mainWindow)
    {
        _serverBar = serverBar;
        _host = host;
        _settings = settings;
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        if (_entry is not null)
        {
            return;
        }
        _serverBar.SeedLegacyToggle("groove", _settings.ShowDtr);
        _entry = _serverBar.For("groove").Entry(
            "track", "AetherOS Groove", "os.groove_set_dtr", _mainWindow.OpenToGroove);
        Plugin.Framework.Update += OnUpdate;
        Refresh();
    }

    public void Shutdown()
    {
        Plugin.Framework.Update -= OnUpdate;
        _entry?.Set(null);
        _entry = null;
    }

    private void OnUpdate(IFramework framework)
    {
        _accum += framework.UpdateDelta.TotalSeconds;
        if (_accum < PollSeconds)
        {
            return;
        }
        _accum = 0;
        Refresh();
    }

    private void Refresh()
    {
        if (_entry is null)
        {
            return;
        }
        IGrooveHost host = _host;
        if (host.Current is not { Title.Length: > 0 } session)
        {
            _entry.Set(null);
            return;
        }
        var title = session.Title.Length > TitleMaxChars
            ? session.Title[..TitleMaxChars] + "…"
            : session.Title;
        var glyph = session.IsPlaying ? "♪" : "‖";
        _entry.Set(session.Artist.Length > 0 ? $"{glyph} {title} · {session.Artist}" : $"{glyph} {title}");
    }
}
