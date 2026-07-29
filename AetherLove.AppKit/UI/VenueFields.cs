using System;
using System.Numerics;
using AetherLove.Emoji;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared.Places;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherLove.UI;

internal static class VenueFields
{
    /// <summary>"Europe · Chaos · Cerberus", skipping any empty part.</summary>
    internal static string LocationLine(VenueSummaryDto venue)
    {
        var idx = IndexOf(RegionValues, venue.Region, -1);
        var region = idx >= 0 ? Regions[idx] : "";
        var parts = new System.Collections.Generic.List<string>(3);
        if (region.Length > 0)
        {
            parts.Add(region);
        }
        if (venue.DataCenter.Length > 0)
        {
            parts.Add(venue.DataCenter);
        }
        if (venue.World.Length > 0)
        {
            parts.Add(venue.World);
        }
        return string.Join(" · ", parts);
    }

    internal static readonly VenueTag[] VenueTagValues =
    [
        VenueTag.RoleplayVenue,
        VenueTag.Barding,
        VenueTag.Nsfw,
        VenueTag.LiveDj,
        VenueTag.SyncEnabled,
        VenueTag.Nightclub,
        VenueTag.BarTavern,
        VenueTag.Cafe,
        VenueTag.Restaurant,
        VenueTag.Casino,
        VenueTag.MaidCafe,
        VenueTag.Bathhouse,
        VenueTag.GposeStudio,
        VenueTag.LiveMusic,
        VenueTag.MarketShop,
        VenueTag.AlwaysOpen,
        VenueTag.Lgbtq,
        VenueTag.MiniGames,
    ];

    internal static string[] VenueTagLabels =>
    [
        Loc.T("places.tag_rp_venue"),
        Loc.T("places.tag_barding"),
        Loc.T("places.tag_nsfw"),
        Loc.T("places.tag_live_dj"),
        Loc.T("places.tag_sync"),
        Loc.T("places.tag_nightclub"),
        Loc.T("places.tag_bar_tavern"),
        Loc.T("places.tag_cafe"),
        Loc.T("places.tag_restaurant"),
        Loc.T("places.tag_casino"),
        Loc.T("places.tag_maid_cafe"),
        Loc.T("places.tag_bathhouse"),
        Loc.T("places.tag_gpose_studio"),
        Loc.T("places.tag_live_music"),
        Loc.T("places.tag_market"),
        Loc.T("places.tag_247"),
        Loc.T("places.tag_lgbtq"),
        Loc.T("places.tag_minigames"),
    ];

    internal static readonly HousingDistrict[] DistrictValues =
    [
        HousingDistrict.Mist,
        HousingDistrict.LavenderBeds,
        HousingDistrict.Goblet,
        HousingDistrict.Shirogane,
        HousingDistrict.Empyreum,
    ];

    internal static readonly string[] DistrictLabels =
    [
        "Mist",
        "The Lavender Beds",
        "The Goblet",
        "Shirogane",
        "Empyreum",
    ];

    /// <summary>Monday-first day-of-week abbreviations, matching the wire DaysMask bit order.</summary>
    internal static string[] DayAbbreviations =>
    [
        Loc.T("places.day_mon"),
        Loc.T("places.day_tue"),
        Loc.T("places.day_wed"),
        Loc.T("places.day_thu"),
        Loc.T("places.day_fri"),
        Loc.T("places.day_sat"),
        Loc.T("places.day_sun"),
    ];

    internal static string DistrictLabel(HousingDistrict district)
    {
        var idx = Array.IndexOf(DistrictValues, district);
        return idx >= 0 ? DistrictLabels[idx] : "?";
    }

    /// <summary>"Shiva · Mist W10 P30" (or "Room 12" for an apartment/FC room).</summary>
    internal static string LocationLine(string world, HousingDistrict district, short ward, short plot, short room)
    {
        var spot = room > 0
            ? Loc.T("places.location_room", room)
            : Loc.T("places.location_plot", plot);
        return $"{world} · {DistrictLabel(district)} · {Loc.T("places.location_ward", ward)} {spot}";
    }

    /// <summary>Returns true the frame the "…" overflow pill is clicked.</summary>
    internal static bool DrawTagPills(VenueTag tags, float maxWidth, bool expanded)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var labels = VenueTagLabels;

        var ordered = new System.Collections.Generic.List<(string Label, bool Nsfw)>();
        for (var i = 0; i < VenueTagValues.Length; i++)
        {
            if ((tags & VenueTagValues[i]) == VenueTag.None)
            {
                continue;
            }
            if (VenueTagValues[i] == VenueTag.Nsfw)
            {
                ordered.Insert(0, (labels[i], true));
            }
            else
            {
                ordered.Add((labels[i], false));
            }
        }

        const int maxCollapsed = 3;
        var overflow = !expanded && ordered.Count > maxCollapsed;
        var shownCount = overflow ? maxCollapsed : ordered.Count;

        var origin = ImGui.GetCursorScreenPos();
        var x = 0f;
        var y = 0f;
        var padX = Px(9f);
        var gap = Px(6f);
        var pillH = ImGui.GetTextLineHeight() + Px(6f);
        var fillStart = t.Accent with { W = 0.18f };
        var fillEnd = t.SecondaryEnd with { W = 0.24f };
        var borderStart = t.Accent with { W = 0.55f };
        var borderEnd = t.SecondaryEnd with { W = 0.65f };
        var denom = MathF.Max(1f, ordered.Count - 1);
        var expandClicked = false;

        void Pill(Vector2 tl, float w, Vector4 fill, Vector4 border, string label)
        {
            dl.AddRectFilled(tl, tl + new Vector2(w, pillH), ImGui.GetColorU32(fill), pillH * 0.5f);
            dl.AddRect(tl, tl + new Vector2(w, pillH), ImGui.GetColorU32(border), pillH * 0.5f,
                ImDrawFlags.None, Px(1.5f));
            dl.AddText(tl + new Vector2(padX, (pillH - ImGui.GetTextLineHeight()) * 0.5f), 0xFFFFFFFFu, label);
        }

        for (var i = 0; i < shownCount; i++)
        {
            var (label, isNsfw) = ordered[i];
            var lerp = i / denom;
            var w = ImGui.CalcTextSize(label).X + padX * 2f;
            if (x > 0f && x + w > maxWidth)
            {
                x = 0f;
                y += pillH + gap;
            }
            var fill = isNsfw ? UiColors.NsfwFrameBg with { W = 0.55f } : Vector4.Lerp(fillStart, fillEnd, lerp);
            var border = isNsfw ? UiColors.Danger with { W = 0.65f } : Vector4.Lerp(borderStart, borderEnd, lerp);
            Pill(origin + new Vector2(x, y), w, fill, border, label);
            x += w + gap;
        }

        if (overflow)
        {
            const string more = "…";
            var w = ImGui.CalcTextSize(more).X + padX * 2f;
            if (x > 0f && x + w > maxWidth)
            {
                x = 0f;
                y += pillH + gap;
            }
            var tl = origin + new Vector2(x, y);
            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton("##tagsMore", new Vector2(w, pillH)))
            {
                expandClicked = true;
            }
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                ImGui.SetTooltip(Loc.T("places.show_all_tags"));
            }
            Pill(tl, w, new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.08f),
                new Vector4(1f, 1f, 1f, hovered ? 0.40f : 0.22f), more);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(maxWidth, y + pillH));
        return expandClicked;
    }

    /// <summary>Returns the drawn width; <paramref name="alignRight"/> treats <paramref name="pos"/> as the
    /// top-right corner.</summary>
    internal static float DrawStarSummary(ImDrawListPtr dl, Vector2 pos, double average, int count,
        float starPx, bool alignRight = false)
    {
        var starSz = IconDraw.Measure(FontAwesomeIcon.Star, starPx);
        var gap = Px(2f);
        var starsW = 5f * starSz.X + 4f * gap;
        var hasReviews = count > 0;
        var value = hasReviews ? average.ToString("0.0") : "";
        var valueSz = hasReviews ? ImGui.CalcTextSize(value) : Vector2.Zero;
        var width = hasReviews ? starsW + Px(6f) + valueSz.X : starsW;
        var tl = alignRight ? pos - new Vector2(width, 0f) : pos;
        var starY = hasReviews ? (valueSz.Y - starSz.Y) * 0.5f : 0f;
        var rounded = hasReviews ? (int)Math.Round(average) : 0;
        for (var i = 0; i < 5; i++)
        {
            IconDraw.Add(dl, FontAwesomeIcon.Star, starPx,
                tl + new Vector2(i * (starSz.X + gap), starY),
                i < rounded ? GoldStar : DimStar);
        }
        if (hasReviews)
        {
            dl.AddText(tl + new Vector2(starsW + Px(6f), 0f), ImGui.GetColorU32(UiColors.Body), value);
        }
        return width;
    }

    internal static bool DrawStarRow(string id, ref int rating, bool interactive, float starPx)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var changed = false;
        var gap = Px(3f);
        var starSize = IconDraw.Measure(FontAwesomeIcon.Star, starPx);
        var starW = starSize.X;
        var starH = starSize.Y;
        for (var i = 0; i < 5; i++)
        {
            var tl = origin + new Vector2(i * (starW + gap), 0f);
            var filled = rating >= i + 1;
            var hovered = false;
            if (interactive)
            {
                ImGui.SetCursorScreenPos(tl);
                if (ImGui.InvisibleButton($"##{id}star{i}", new Vector2(starW, starH)))
                {
                    rating = i + 1;
                    changed = true;
                }
                hovered = ImGui.IsItemHovered();
                if (hovered)
                {
                    filled = true;
                }
            }
            var px = starPx;
            var drawTl = tl;
            if (hovered && !AccessibilityService.ReduceMotion)
            {
                px = starPx * 1.22f;
                drawTl = tl - new Vector2(starW, starH) * 0.11f;
            }
            IconDraw.Add(dl, FontAwesomeIcon.Star, px, drawTl, filled ? GoldStar : DimStar);
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + starH + Px(2f)));
        return changed;
    }

    internal static bool DrawPillToggle(string id, string label, bool selected, bool danger = false)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var padX = Px(10f);
        var h = ImGui.GetTextLineHeight() + Px(10f);
        var labelSz = ImGui.CalcTextSize(label);
        var w = labelSz.X + padX * 2f;
        var tl = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();

        var accent = danger ? UiColors.Danger : t.Accent;
        var fill = selected
            ? accent with { W = hovered ? 0.40f : 0.30f }
            : new Vector4(1f, 1f, 1f, hovered ? 0.10f : 0.05f);
        var border = selected ? accent : new Vector4(1f, 1f, 1f, hovered ? 0.35f : 0.16f);
        dl.AddRectFilled(tl, tl + new Vector2(w, h), ImGui.GetColorU32(fill), h * 0.5f);
        dl.AddRect(tl, tl + new Vector2(w, h), ImGui.GetColorU32(border), h * 0.5f,
            ImDrawFlags.None, selected ? Px(1.5f) : Px(1f));
        dl.AddText(tl + new Vector2(padX, (h - labelSz.Y) * 0.5f),
            selected ? 0xFFFFFFFFu : ImGui.GetColorU32(UiColors.Body), label);
        return clicked;
    }

    internal static bool DrawPillToggleRow(string idPrefix, string[] labels, bool[] selected,
        float maxWidth, Func<int, bool>? dangerAt = null, Func<int, bool>? skipAt = null)
    {
        var changed = false;
        var startPos = ImGui.GetCursorPos();
        var gap = Px(6f);
        var pillH = ImGui.GetTextLineHeight() + Px(10f);
        var x = 0f;
        var y = 0f;

        var count = Math.Min(labels.Length, selected.Length);
        for (var i = 0; i < count; i++)
        {
            if (skipAt?.Invoke(i) == true)
            {
                continue;
            }
            var w = ImGui.CalcTextSize(labels[i]).X + Px(20f);
            if (x > 0f && x + w > maxWidth)
            {
                x = 0f;
                y += pillH + gap;
            }
            ImGui.SetCursorPos(new Vector2(startPos.X + x, startPos.Y + y));
            if (DrawPillToggle($"##{idPrefix}{i}", labels[i], selected[i], dangerAt?.Invoke(i) == true))
            {
                selected[i] = !selected[i];
                changed = true;
            }
            x += w + gap;
        }

        ImGui.SetCursorPos(new Vector2(startPos.X, startPos.Y + y + pillH + Px(4f)));
        return changed;
    }

    /// <summary>Gold star fill and its dimmed empty variant (0xAABBGGRR).</summary>
    internal const uint GoldStar = 0xFF3CC8FFu;
    internal const uint DimStar = 0x40FFFFFFu;

    internal static void DrawReviewCard(VenueReviewDto review, ISharedImmediateTexture? avatarTex,
        float cardW, float padX) =>
        DrawReviewCard(review.Id, review.Rating, review.Text, review.CreatedAtUtc, review.Mine,
            avatarTex, cardW, padX);

    internal static void DrawReviewCard(Guid id, short rating, string text, DateTimeOffset createdAtUtc,
        bool mine, ISharedImmediateTexture? avatarTex, float cardW, float padX)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(padX);
        var avatarR = Px(16f);
        var avatarLeft = Px(12f);
        var textX = avatarLeft + avatarR * 2f + Px(12f);
        var contentW = cardW - textX - Px(12f);

        var parsed = ParsedMessage.Parse(text);
        var textH = text.Length > 0 ? parsed.MeasureHeight(contentW) : 0f;

        var starPx = ImGui.GetFontSize() * 0.82f;
        var starSz = IconDraw.Measure(FontAwesomeIcon.Star, starPx);
        var avatarCenterY = Px(10f) + avatarR;
        var starY = avatarCenterY - starSz.Y * 0.5f;
        var textY = starY + starSz.Y + Px(5f);
        var contentBottom = textH > 0f ? textY + textH : starY + starSz.Y;
        var cardH = MathF.Max(Px(10f) + avatarR * 2f, contentBottom) + Px(10f);

        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, cardH);

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), Px(10f));
        if (mine)
        {
            dl.AddRect(tl, br, ImGui.GetColorU32(ThemeService.Current.Accent with { W = 0.40f }), Px(10f));
        }

        var avatarCenter = new Vector2(tl.X + avatarLeft + avatarR, tl.Y + avatarCenterY);
        var wrap = avatarTex?.GetWrapOrDefault();
        if (wrap != null)
        {
            dl.AddImageRounded(wrap.Handle, avatarCenter - new Vector2(avatarR, avatarR),
                avatarCenter + new Vector2(avatarR, avatarR),
                Vector2.Zero, Vector2.One, 0xFFFFFFFFu, avatarR, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(avatarCenter, avatarR, UiColors.AvatarFallback);
        }
        dl.AddCircle(avatarCenter, avatarR, UiColors.AvatarRing, 32, Px(1f));

        for (var i = 0; i < 5; i++)
        {
            IconDraw.Add(dl, FontAwesomeIcon.Star, starPx,
                new Vector2(tl.X + textX + i * (starSz.X + Px(2f)), tl.Y + starY),
                i < rating ? GoldStar : DimStar);
        }

        var dateStr = createdAtUtc.ToLocalTime().ToString("yyyy-MM-dd");
        var dateSz = ImGui.CalcTextSize(dateStr);
        dl.AddText(new Vector2(br.X - dateSz.X - Px(12f), tl.Y + avatarCenterY - dateSz.Y * 0.5f),
            ImGui.GetColorU32(UiColors.Muted), dateStr);

        if (textH > 0f)
        {
            ImGui.SetCursorScreenPos(new Vector2(tl.X + textX, tl.Y + textY));
            parsed.DrawWrapped($"##rev_{id:N}", contentW);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y));
    }

    /// <summary>First click sets the start block, second the end. The span may wrap past midnight and the
    /// end block is inclusive: start 22 + end 1 spans 22,23,0,1.</summary>
    internal static bool DrawHourRangeEditor(float availW, ref int startHour, ref int endHour,
        ref bool pickingEnd, string idSuffix)
    {
        var barH = Px(38f);
        var labelH = Px(16f);
        var t = ThemeService.Current;
        var barW = availW / 24f;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var changed = false;

        for (var h = 0; h < 24; h++)
        {
            var x0 = origin.X + h * barW + Px(1.5f);
            var x1 = origin.X + (h + 1) * barW - Px(1.5f);

            ImGui.SetCursorScreenPos(new Vector2(x0, origin.Y));
            if (ImGui.InvisibleButton($"##vhr{idSuffix}{h}", new Vector2(x1 - x0, barH)))
            {
                if (!pickingEnd)
                {
                    startHour = h;
                    endHour = h;
                    pickingEnd = true;
                }
                else
                {
                    endHour = h;
                    pickingEnd = false;
                }
                changed = true;
            }

            var hovered = ImGui.IsItemHovered();
            var inSpan = InHourSpan(h, startHour, endHour);
            var isEdge = h == startHour || h == endHour;

            uint barCol;
            if (isEdge)
            {
                barCol = t.AccentLightU32;
            }
            else if (inSpan)
            {
                barCol = hovered ? t.AccentLightU32 : t.AccentU32;
            }
            else
            {
                barCol = hovered ? 0xFF4A4A4Au : 0xFF2D2D2Du;
            }

            dl.AddRectFilled(new Vector2(x0, origin.Y), new Vector2(x1, origin.Y + barH), barCol, Px(3f));
            if (isEdge)
            {
                dl.AddRect(new Vector2(x0, origin.Y), new Vector2(x1, origin.Y + barH), 0xFFFFFFFFu, Px(3f));
            }

            if (hovered)
            {
                var h12 = h == 0 ? 12 : (h > 12 ? h - 12 : h);
                var amPm = h < 12 ? Loc.T("onboarding.time_am") : Loc.T("onboarding.time_pm");
                var action = pickingEnd ? Loc.T("places.hours_pick_end") : Loc.T("places.hours_pick_start");
                ImGui.SetTooltip($"{h12}:00 {amPm}  /  {h:D2}:00\n{action}");
            }
        }

        var labelFsz = ImGui.GetFontSize() * 0.82f;
        foreach (var h in new[] { 0, 6, 12, 18 })
        {
            var lx = origin.X + h * barW;
            dl.AddText(ImGui.GetFont(), labelFsz,
                new Vector2(lx + Px(1f), origin.Y + barH + Px(4f)),
                UiColors.TextMuted, $"{h:D2}:00");
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + barH + labelH + Px(4f)));
        return changed;
    }

    /// <summary>True when hour <paramref name="h"/> lies inside the inclusive start→end span, which may
    /// wrap past midnight.</summary>
    internal static bool InHourSpan(int h, int startHour, int endHour) =>
        startHour <= endHour
            ? h >= startHour && h <= endHour
            : h >= startHour || h <= endHour;

    /// <summary>Inclusive span duration in minutes (start 22, end 1 → 240).</summary>
    internal static short SpanDurationMinutes(int startHour, int endHour) =>
        (short)((((endHour - startHour + 24) % 24) + 1) * 60);

    /// <summary>"22:00 – 02:00" for a start minute + duration.</summary>
    internal static string SpanLabel(int startMinute, int durationMinutes)
    {
        var endMinute = (startMinute + durationMinutes) % 1440;
        return $"{startMinute / 60:D2}:{startMinute % 60:D2} – {endMinute / 60:D2}:{endMinute % 60:D2}";
    }

    /// <summary>Localized day-pill summary for a rule's DaysMask ("Mon, Fri, Sat"), or the one-time date.</summary>
    internal static string DaysSummary(int daysMask, int oneTimeDateDayNumber)
    {
        if (daysMask == 0)
        {
            return oneTimeDateDayNumber > 0
                ? DateOnly.FromDayNumber(oneTimeDateDayNumber).ToString("yyyy-MM-dd")
                : "?";
        }
        var abbr = DayAbbreviations;
        var parts = new System.Collections.Generic.List<string>(7);
        for (var d = 0; d < 7; d++)
        {
            if ((daysMask & (1 << d)) != 0)
            {
                parts.Add(abbr[d]);
            }
        }
        return string.Join(", ", parts);
    }
}
