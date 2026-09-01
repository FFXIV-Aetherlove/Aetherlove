using System;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Interface;

namespace AetherOS.Apps.Levemetes;

/// <summary>The classifieds app: the public browse/detail surface plus the owner's My ads editor.</summary>
public sealed class LevemetesApp : IAetherApp
{
    private enum View { Browse, MyAds }

    private readonly Func<string> _name;
    private readonly Func<bool> _available;
    private readonly ILevemetesHost _host;
    private readonly LevemetesScreen _browse;
    private readonly MyLevemetesScreen _myAds;
    private View _view = View.Browse;

    public LevemetesApp(Func<string> name, Func<bool> available, ILevemetesHost host, IAppCapabilities caps)
    {
        _name = name;
        _available = available;
        _host = host;
        _browse = new LevemetesScreen(host, OpenMyAds);
        _myAds = new MyLevemetesScreen(host, caps, BackToBrowse);
    }

    public string Id => "levemetes";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.Scroll;
    public Vector4 TileTop => new(0.93f, 0.72f, 0.25f, 1f);
    public Vector4 TileBottom => new(0.60f, 0.36f, 0.08f, 1f);
    public int Badge => 0;
    public bool Available => _available();
    public bool HasSurface => true;

    public bool RequiresConnection => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        if (_view == View.Browse)
        {
            _browse.OnShow();
        }
        else
        {
            _myAds.OnShow();
        }
    }

    public void Draw(OsAppContext ctx)
    {
        if (_host.TakePendingOpen() is { } pending)
        {
            _view = View.Browse;
            _browse.OpenDetailFromChat(pending.AdId, pending.ReturnApp);
        }

        if (_view == View.Browse)
        {
            _browse.Draw(ctx.Shell, ctx.Capabilities.Share);
        }
        else
        {
            _myAds.Draw(ctx.Shell);
        }
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.OpenLevemete && OsIntents.TryGetId(intent, out var adId))
        {
            _view = View.Browse;
            _browse.OpenDetailFromChat(adId, OsIntents.TryGetReturnApp(intent, out var returnApp) ? returnApp : null);
        }
    }

    private void OpenMyAds()
    {
        _view = View.MyAds;
        _myAds.OnShow();
    }

    private void BackToBrowse()
    {
        _view = View.Browse;
        _browse.OnShow();
    }
}
