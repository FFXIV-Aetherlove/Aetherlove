using System;
using AetherLove.Config;
using AetherLove.Os;
using AetherLove.Windows;
using AetherOS.Apps.Groove;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AetherLove.Services;

/// <summary>Publishes the current track to the DTR bar. A sibling of <see cref="DtrBarService"/> rather
/// than a category on it: that model is count-based ("label N"), this entry is text.</summary>
public sealed class GrooveDtrService
{
    private const double PollSeconds = 0.5;
    private const int TitleMaxChars = 30;

    private readonly IDtrBar _dtrBar;
    private readonly GrooveHostService _host;
    private readonly GrooveSettings _settings;
    private readonly Configuration _config;
    private readonly MainPluginWindow _mainWindow;

    private IDtrBarEntry? _entry;
    private double _accum;
    private string _lastText = string.Empty;
    private bool _lastShown;

    public GrooveDtrService(IDtrBar dtrBar, GrooveHostService host, GrooveSettings settings,
        Configuration config, MainPluginWindow mainWindow)
    {
        _dtrBar = dtrBar;
        _host = host;
        _settings = settings;
        _config = config;
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        if (_entry is not null)
        {
            return;
        }
        _entry = _dtrBar.Get("AetherOS Groove");
        _entry.OnClick = _ => _mainWindow.OpenToGroove();
        Plugin.Framework.Update += OnUpdate;
        Refresh();
    }

    public void Shutdown()
    {
        Plugin.Framework.Update -= OnUpdate;
        _entry?.Remove();
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
        // Removing the Groove app is an opt-out of the whole feature, so the server-bar line goes with it.
        var session = _config.EnableDtrEntry && _settings.ShowDtr && _mainWindow.IsPoweredOn
                      && !_config.Os.RemovedApps.Contains("groove")
            ? host.Current
            : null;
        var shown = session is { Title.Length: > 0 };
        var text = string.Empty;
        if (shown)
        {
            var title = session!.Title.Length > TitleMaxChars
                ? session.Title[..TitleMaxChars] + "…"
                : session.Title;
            var glyph = session.IsPlaying ? "♪" : "‖";
            text = session.Artist.Length > 0 ? $"{glyph} {title} · {session.Artist}" : $"{glyph} {title}";
        }
        if (shown == _lastShown && text == _lastText)
        {
            return;
        }
        _lastShown = shown;
        _lastText = text;
        _entry.Shown = shown;
        if (shown)
        {
            _entry.Text = new SeStringBuilder().AddText(text).Build();
        }
    }
}
