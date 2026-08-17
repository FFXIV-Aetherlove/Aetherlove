using AetherOS.Sdk;

namespace AetherOS.Apps.Calculator;

/// <summary>Which calculator the app puts in front of you. Both run on the same engine and share one tape,
/// so switching is a change of keypad rather than a change of calculator.</summary>
internal enum CalcMode
{
    Simple = 0,
    Graphing = 1,
}

/// <summary>The calculator's own preference. Simple is the default because the graphing keypad is a lot of
/// device to meet unannounced; the introduction asks once, and the menu changes it at any time.</summary>
internal sealed class CalcSettings
{
    public const string ModeKey = "mode";

    private readonly IAppStorage _storage;
    private bool _loaded;
    private CalcMode _mode = CalcMode.Simple;

    public CalcSettings(IAppStorage storage) => _storage = storage;

    public CalcMode Mode
    {
        get
        {
            Load();
            return _mode;
        }
        set
        {
            Load();
            _mode = value;
            _storage.Set(ModeKey, (int)value);
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        _mode = _storage.Get<int?>(ModeKey) == (int)CalcMode.Graphing ? CalcMode.Graphing : CalcMode.Simple;
    }
}
