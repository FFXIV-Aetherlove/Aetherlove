using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Places;

public partial class PlacesScreen
{
    private bool _detailFromChat;
    private string? _detailReturnApp;

    public void OpenDetailFromChat(Guid venueId, string? returnApp = null)
    {
        _detailReturnApp = returnApp;
        _detailVenueId = venueId;
        _detailName = "";
        _detail = null;
        _detailError = null;
        _extraReviews = [];
        _reviewsExhausted = false;
        _confirmDeleteReview = false;
        _reviewError = null;
        _tagsExpanded = false;
        _detailFromChat = true;
        _section = Section.Detail;
        StartDetailFetch();
    }

    private void DrawSharePill(float winW, Vector2 rowTop)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var label = Loc.T("places.share");
        var labelSz = ImGui.CalcTextSize(label);
        var iconPx = ImGui.GetFontSize() * 0.85f;
        var iconSz = IconDraw.Measure(FontAwesomeIcon.Share, iconPx);
        var padX = Px(11f);
        var gap = Px(6f);
        var pillH = labelSz.Y + Px(9f);
        var pillW = padX * 2f + iconSz.X + gap + labelSz.X;
        var tl = new Vector2(rowTop.X + winW - Px(PadX) - pillW, rowTop.Y - Px(2f));

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton("##venueShareBtn", new Vector2(pillW, pillH));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }

        dl.AddRectFilled(tl, tl + new Vector2(pillW, pillH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.45f : 0.22f }), pillH * 0.5f);
        dl.AddRect(tl, tl + new Vector2(pillW, pillH),
            ImGui.GetColorU32(t.Accent with { W = hovered ? 0.95f : 0.60f }), pillH * 0.5f, ImDrawFlags.None, Px(1f));
        IconDraw.Add(dl, FontAwesomeIcon.Share, iconPx,
            new Vector2(tl.X + padX, tl.Y + (pillH - iconSz.Y) * 0.5f), ImGui.GetColorU32(t.AccentLight));
        dl.AddText(new Vector2(tl.X + padX + iconSz.X + gap, tl.Y + (pillH - labelSz.Y) * 0.5f),
            0xFFFFFFFFu, label);

        if (clicked && _detail is { } detail)
        {
            var venue = detail.Summary;
            var address = VenueFields.LocationLine(venue.World, venue.District, venue.Ward, venue.Plot, venue.Room);
            var name = _detailName.Length > 0 ? _detailName : venue.Name;
            _share?.Offer(new ShareItem
            {
                Type = ShareTypes.Venue,
                RefId = _detailVenueId.ToString("D"),
                Title = name,
                Subtitle = $"{venue.DataCenter}, {address}",
                SourceAppId = "places",
            }, title: name);
        }
    }

}
