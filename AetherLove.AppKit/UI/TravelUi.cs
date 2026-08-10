using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Places;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.UI;

/// <summary>Turning the client's own addresses into a <see cref="TravelAddress"/>, plus the pill every surface
/// offers travel through, so the affordance reads and behaves the same wherever it appears.</summary>
internal static class TravelUi
{
    public static TravelAddress ForVenue(VenueSummaryDto venue) =>
        new(venue.World, DistrictOf(venue.District), venue.Ward, venue.Plot, venue.Room);

    public static TravelAddress ForLocation(LocationShare.LocationCardData loc) =>
        new(loc.World, DistrictOf(LocationShare.DistrictOf(loc.District)), loc.Ward, loc.Plot, 0);

    /// <summary>Whether travel is worth offering at all: a provider is present and the address names a place it
    /// could reach. Surfaces hide their pill entirely when this is false.</summary>
    public static bool CanOffer(ITravelBridge? travel, TravelAddress address) =>
        travel is { IsAvailable: true } && address.IsComplete;

    /// <summary>"Teleport (Lifestream)", crediting whoever is doing the travelling.</summary>
    public static string Label(ITravelBridge travel) =>
        Loc.T("common.travel_teleport_with", travel.ProviderName ?? string.Empty);

    /// <summary>A compact pill in the theme's accent, sized to its own label, that hands the address over when
    /// pressed. Draws at the cursor and advances past itself. Dimmed and inert while the provider is already
    /// travelling, because a second request would abandon the first trip.</summary>
    public static bool DrawTeleportPill(ITravelBridge travel, TravelAddress address, string id)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var label = Label(travel);
        var busy = travel.IsBusy;

        var padX = Px(11f);
        var padY = Px(5f);
        var gap = Px(6f);
        var iconPx = ImGui.GetFontSize();
        var iconW = IconDraw.Measure(FontAwesomeIcon.LocationArrow, iconPx).X;
        var labelSz = ImGui.CalcTextSize(label);
        var size = new Vector2(padX + iconW + gap + labelSz.X + padX, labelSz.Y + padY * 2f);

        var tl = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##travelPill{id}", size);
        var hovered = !busy && ImGui.IsItemHovered();
        var clicked = !busy && ImGui.IsItemClicked();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
            ImGui.SetTooltip(Loc.T("common.travel_tooltip", travel.ProviderName ?? string.Empty));
        }

        var alpha = busy ? 0.35f : 1f;
        var fill = t.Accent with { W = (hovered ? 0.34f : 0.20f) * alpha };
        var border = t.Accent with { W = (hovered ? 0.95f : 0.60f) * alpha };
        var text = t.AccentLight with { W = alpha };
        var rounding = size.Y * 0.5f;
        dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(fill), rounding);
        dl.AddRect(tl, tl + size, ImGui.GetColorU32(border), rounding, ImDrawFlags.None, Px(1.2f));

        IconDraw.AddCentered(dl, FontAwesomeIcon.LocationArrow, iconPx,
            new Vector2(tl.X + padX + iconW * 0.5f, tl.Y + size.Y * 0.5f), ImGui.GetColorU32(text));
        dl.AddText(new Vector2(tl.X + padX + iconW + gap, tl.Y + padY), ImGui.GetColorU32(text), label);

        return clicked && travel.GoTo(address);
    }

    private static TravelDistrict DistrictOf(HousingDistrict district) => district switch
    {
        HousingDistrict.Mist => TravelDistrict.Mist,
        HousingDistrict.LavenderBeds => TravelDistrict.LavenderBeds,
        HousingDistrict.Goblet => TravelDistrict.Goblet,
        HousingDistrict.Shirogane => TravelDistrict.Shirogane,
        HousingDistrict.Empyreum => TravelDistrict.Empyreum,
        _ => TravelDistrict.Unknown,
    };
}
