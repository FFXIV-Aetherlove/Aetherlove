using System;
using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Places;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

public partial class ChatScreen
{
    private const float VenueCardH = 150f;

    private sealed record VenueCardVisual(VenueCardDto? Card, ISharedImmediateTexture? Tex, bool LogoBackdrop);

    // One fetched card per venue id (session cache); null Card marks an unavailable entry.
    private readonly ConcurrentDictionary<Guid, VenueCardVisual> _venueCards = new();
    private readonly ConcurrentDictionary<Guid, byte> _venueCardFetches = new();

    /// <summary>Failed card fetches retry on the next chat open (the venue may just have been unreachable).</summary>
    private void ResetFailedVenueCards()
    {
        foreach (var kv in _venueCards)
        {
            if (kv.Value.Card is null)
            {
                _venueCards.TryRemove(kv.Key, out _);
            }
        }
    }

    private void StartVenueCardFetch(Guid venueId)
    {
        if (_venueCards.ContainsKey(venueId) || !_venueCardFetches.TryAdd(venueId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetVenueCardAsync(venueId, CancellationToken.None).ConfigureAwait(false);
                ISharedImmediateTexture? tex = null;
                var logoBackdrop = false;
                var cacheDir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "PlacesCache");
                if (dto.BannerWebp is { Length: > 0 })
                {
                    tex = AvatarDiskCache.Store(cacheDir, $"chatvenue_{venueId:N}", dto.BannerWebp);
                }
                else if (dto.Summary.LogoWebp is { Length: > 0 })
                {
                    tex = AvatarDiskCache.Store(cacheDir, $"chatvenuelogo_{venueId:N}", dto.Summary.LogoWebp);
                    logoBackdrop = true;
                }
                _venueCards[venueId] = new VenueCardVisual(dto, tex, logoBackdrop);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, $"[ChatScreen] Venue card fetch failed for {venueId}.");
                _venueCards[venueId] = new VenueCardVisual(null, null, false);
            }
            finally
            {
                _venueCardFetches.TryRemove(venueId, out _);
            }
        });
    }

    /// <summary>A shared venue rendered as a card in place of a bubble; clicking deep-links into the venue
    /// detail, with back returning to this chat.</summary>
    private void DrawVenueCardMessage(DisplayedMessage msg, Guid venueId, float windowWidth, bool isGroupEnd)
    {
        StartVenueCardFetch(venueId);
        _venueCards.TryGetValue(venueId, out var visual);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var cardW = windowWidth * 0.72f;
        var cardH = Px(VenueCardH);
        var lineH = ImGui.GetTextLineHeight();

        var (entryDy, entryAlpha) = MessageEntrance(msg.Id);
        var fading = entryAlpha < 0.999f;
        if (fading)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, entryAlpha * ImGui.GetStyle().Alpha);
        }

        var left = msg.IsOwn ? cursorPos.X + windowWidth - cardW - Px(10) : cursorPos.X + Px(10);
        var tl = new Vector2(left, cursorPos.Y + entryDy);
        var br = tl + new Vector2(cardW, cardH);

        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##venueCard{msg.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();

        if (visual?.Card is { } card)
        {
            var venue = card.Summary;
            var wrap = visual.Tex?.GetWrapOrDefault();
            if (wrap != null)
            {
                var (uv0, uv1) = SharedUiHelpers.CoverFitUvs(wrap.Width, wrap.Height, cardW, cardH);
                dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(14f), ImDrawFlags.RoundCornersAll);
                if (visual.LogoBackdrop)
                {
                    dl.AddRectFilled(tl, br, 0x59000000u, Px(14f));
                }
            }
            else
            {
                dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.14f }), Px(14f));
            }

            dl.AddRectFilledMultiColor(tl, new Vector2(br.X, tl.Y + Px(46f)),
                0x88000000u, 0x88000000u, 0x00000000u, 0x00000000u);
            dl.AddRectFilledMultiColor(new Vector2(tl.X, br.Y - Px(84f)), br,
                0x00000000u, 0x00000000u, 0xD8000000u, 0xD8000000u);
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
                ImDrawFlags.None, Px(1.5f));

            VenueFields.DrawStarSummary(dl, tl + new Vector2(Px(12f), Px(11f)),
                venue.AverageRating, venue.ReviewCount, Px(12f));

            var textX = tl.X + Px(14f);
            var textMaxW = cardW - Px(28f);
            float nameH;
            using (UiFonts.H3?.Push())
            {
                nameH = ImGui.GetFontSize();
                dl.AddText(ImGui.GetFont(), nameH, new Vector2(textX, br.Y - Px(58f)),
                    0xFFFFFFFFu, TruncateToWidth(venue.Name, textMaxW));
            }
            dl.AddText(new Vector2(textX, br.Y - Px(58f) + nameH + Px(3f)), ImGui.GetColorU32(UiColors.Body),
                TruncateToWidth(VenueFields.LocationLine(venue), textMaxW - Px(12f)));

            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("places.share_view"));
            }
            if (clicked)
            {
                _shareCtx.PendingOpenVenueId = venueId;
                _shareCtx.PendingOpenReturnApp = null;
                _shell.Shell?.OpenApp("places");
            }
        }
        else
        {
            dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), Px(14f));
            dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.35f }), Px(14f), ImDrawFlags.None, Px(1.5f));
            var text = visual is null ? Loc.T("places.share_loading") : Loc.T("places.share_unavailable");
            var textSz = ImGui.CalcTextSize(text);
            dl.AddText(tl + (new Vector2(cardW, cardH) - textSz) * 0.5f, ImGui.GetColorU32(UiColors.Muted), text);
        }

        if (isGroupEnd)
        {
            var local = msg.SentAt.LocalDateTime;
            var seenSuffix = msg.IsOwn && msg.ReadByOtherAtUtc is not null ? Loc.T("chat.seen_suffix") : string.Empty;
            var timeStr = local.ToString("HH:mm") + seenSuffix;
            var timeSize = ImGui.CalcTextSize(timeStr);
            var timeX = msg.IsOwn ? tl.X + cardW - timeSize.X : tl.X;
            ImGui.SetCursorScreenPos(new Vector2(timeX, br.Y + Px(2f)));
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.75f, 0.40f), timeStr);
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, cardH + timeSize.Y + Px(8f)));
        }
        else
        {
            ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, cardH + Px(2f)));
        }

        if (fading)
        {
            ImGui.PopStyleVar();
        }
    }
}
