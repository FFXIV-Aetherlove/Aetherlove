using System;
using System.Numerics;
using System.Text.Json;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Places;

/// <summary>The venue directory app: the Places browse/detail surface plus the owner's My venues editor.</summary>
public sealed class PlacesApp : IAetherApp
{
    private enum View { Places, MyVenues }

    private readonly Func<string> _name;
    private readonly Func<bool> _available;
    private readonly IPlacesHost _host;
    private readonly PlacesScreen _places;
    private readonly MyVenuesScreen _myVenues;
    private View _view = View.Places;

    public PlacesApp(Func<string> name, Func<bool> available, IPlacesHost host, AetherLove.Os.ISocialBridge social, IAppCapabilities caps)
    {
        _name = name;
        _available = available;
        _host = host;
        _places = new PlacesScreen(host, social, OpenMyVenues);
        _myVenues = new MyVenuesScreen(host, caps, BackToPlaces);
    }

    public string Id => "places";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.MapMarkedAlt;
    public Vector4 TileTop => new(0.25f, 0.62f, 0.96f, 1f);
    public Vector4 TileBottom => new(0.08f, 0.28f, 0.62f, 1f);
    public int Badge => 0;
    public bool Available => _available();
    public bool HasSurface => true;

    public bool RequiresConnection => true;

    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyDictionary<string, string>> Strings => AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground() => ShowCurrentView();

    public void Draw(OsAppContext ctx)
    {
        if (_host.TakePendingOpenVenue() is { } pending)
        {
            OpenVenueDetail(pending.VenueId, pending.ReturnApp);
        }

        if (_view == View.Places)
        {
            _places.Draw(ctx.Shell, ctx.Capabilities.Share);
        }
        else
        {
            _myVenues.Draw();
        }
    }

    public void OnIntent(OsIntent intent)
    {
        if (intent.Type == OsIntents.OpenMyVenues)
        {
            OpenMyVenues();
            return;
        }
        if (intent.Type != OsIntents.OpenVenue)
        {
            return;
        }
        // Senders normalize the guid under "id"; the legacy "venueId" key stays accepted.
        if (OsIntents.TryGetId(intent, out var intentVenueId))
        {
            OpenVenueDetail(intentVenueId, null);
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(intent.PayloadJson);
            if (doc.RootElement.TryGetProperty("venueId", out var el)
                && Guid.TryParse(el.GetString(), out var venueId))
            {
                OpenVenueDetail(venueId, null);
            }
        }
        catch (JsonException)
        {
        }
    }

    private void OpenVenueDetail(Guid venueId, string? returnApp)
    {
        _view = View.Places;
        _places.OpenDetailFromChat(venueId, returnApp);
    }

    private void OpenMyVenues()
    {
        _view = View.MyVenues;
        _myVenues.OnShow();
    }

    private void BackToPlaces()
    {
        _view = View.Places;
        _places.OnShow();
    }

    private void ShowCurrentView()
    {
        if (_view == View.Places)
        {
            _places.OnShow();
        }
        else
        {
            _myVenues.OnShow();
        }
    }
}
