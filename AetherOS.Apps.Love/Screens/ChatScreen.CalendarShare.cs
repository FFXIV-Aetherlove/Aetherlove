using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Screens;

public partial class ChatScreen
{
    private const float CalendarCardH = 92f;

    private CalendarEventShare.Payload? _calPrompt;
    private float _calPromptH;

    /// <summary>A shared calendar event rendered as a card; clicking opens the RSVP / add-to-calendar
    /// chooser. Venue events resolve their title against the live-fetched venue card.</summary>
    private void DrawCalendarEventCard(DisplayedMessage msg, CalendarEventShare.Payload ev, float windowWidth, bool isGroupEnd)
    {
        if (ev.IsVenue)
        {
            StartVenueCardFetch(ev.VenueId);
        }
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var cardW = windowWidth * 0.72f;
        var cardH = Px(CalendarCardH);

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
        var clicked = ImGui.InvisibleButton($"##calCard{msg.Id:N}", new Vector2(cardW, cardH));
        var hovered = ImGui.IsItemHovered();

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(t.Accent with { W = 0.14f }), Px(14f));
        dl.AddRect(tl, br, ImGui.GetColorU32(t.Accent with { W = hovered ? 0.90f : 0.55f }), Px(14f),
            ImDrawFlags.None, Px(1.5f));

        var iconR = Px(18f);
        var iconC = new Vector2(tl.X + Px(14f) + iconR, (tl.Y + br.Y) * 0.5f);
        dl.AddCircleFilled(iconC, iconR, ImGui.GetColorU32(t.Accent));
        var glyph = FontAwesomeIcon.CalendarAlt.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var glyphSz = ImGui.CalcTextSize(glyph) * (Px(16f) / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), Px(16f), iconC - glyphSz * 0.5f, 0xFFFFFFFFu, glyph);
        ImGui.PopFont();

        var (title, sub, ready) = ResolveCalendarEventText(ev);
        var textX = iconC.X + iconR + Px(12f);
        var lineH = ImGui.GetTextLineHeight();
        var textMaxW = br.X - textX - Px(10f);
        dl.AddText(new Vector2(textX, tl.Y + Px(14f)), 0xFFFFFFFFu, TruncateToWidth(title, textMaxW));
        dl.AddText(new Vector2(textX, tl.Y + Px(14f) + lineH + Px(2f)), ImGui.GetColorU32(t.Accent),
            FormatCalendarEventTime(ev.StartUtc));
        if (sub.Length > 0)
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.82f,
                new Vector2(textX, tl.Y + Px(14f) + (lineH + Px(2f)) * 2f), ImGui.GetColorU32(UiColors.Muted),
                TruncateToWidth(sub.Replace('\n', ' '), textMaxW));
        }

        if (clicked && ready)
        {
            _calPrompt = ev;
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

    /// <summary>Title/subtitle for a calendar-event card: venue kind resolves against the live-fetched venue
    /// card; ready is false while that fetch is still in flight.</summary>
    private (string Title, string Sub, bool Ready) ResolveCalendarEventText(CalendarEventShare.Payload ev)
    {
        if (!ev.IsVenue)
        {
            return (ev.Title, ev.Note, true);
        }
        if (_venueCards.TryGetValue(ev.VenueId, out var visual) && visual.Card is { Summary: { } venue })
        {
            return (venue.Name, VenueFields.LocationLine(venue), true);
        }
        return (Loc.T(_venueCards.ContainsKey(ev.VenueId) ? "places.share_unavailable" : "places.share_loading"), "", false);
    }

    private static string FormatCalendarEventTime(DateTimeOffset startUtc) =>
        startUtc.ToLocalTime().ToString("ddd d MMM' · 'HH:mm", LanguageProvider.CurrentCulture);

    /// <summary>The tapped shared-event chooser: RSVP (future venue events) or add a local calendar copy.
    /// Uses the shared page overlay so it layers above the messages child window.</summary>
    private void DrawCalendarEventPrompt()
    {
        if (_calPrompt is not { } ev)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _calPrompt = null;
            return;
        }

        var t = ThemeService.Current;
        var canRsvp = ev.IsVenue && ev.StartUtc > DateTimeOffset.UtcNow;
        var dismissed = DrawPageOverlayPanel("loveCalPrompt", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _calPromptH, Px(180f), innerW =>
        {
            var (title, _, _) = ResolveCalendarEventText(ev);
            ImGui.TextUnformatted(TruncateToWidth(title, innerW));
            ImGui.TextColored(t.Accent, FormatCalendarEventTime(ev.StartUtc));
            ImGui.Dummy(new Vector2(0f, Px(8f)));

            var btnSize = new Vector2(innerW, Px(32f));
            if (canRsvp)
            {
                if (CalendarPromptButton("##loveCalRsvp", Loc.T("chat.calevent_rsvp"),
                        ImGui.GetCursorScreenPos(), btnSize, t.Accent))
                {
                    var venueId = ev.VenueId;
                    var start = ev.StartUtc;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _hub.SetVenueRsvpAsync(venueId, start, true).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            UiHost.Log.Warning(ex, "[ChatScreen] Shared-event RSVP failed.");
                        }
                    });
                    _calPrompt = null;
                }
                ImGui.Dummy(new Vector2(0f, Px(4f)));
            }
            if (CalendarPromptButton("##loveCalAdd", Loc.T("chat.calevent_add"),
                    ImGui.GetCursorScreenPos(), btnSize, t.Accent with { W = 0.55f }))
            {
                _shell.Shell?.SendIntent("calendar",
                    AetherOS.Sdk.OsIntents.CreateCalendarAdd(title, ev.IsVenue ? "" : ev.Note, ev.StartUtc.ToUnixTimeSeconds()));
                _calPrompt = null;
            }
            ImGui.Dummy(new Vector2(0f, Px(4f)));
            if (CalendarPromptButton("##loveCalCancel", Loc.T("common.cancel"),
                    ImGui.GetCursorScreenPos(), btnSize, new Vector4(1f, 1f, 1f, 0.08f)))
            {
                _calPrompt = null;
            }
        });
        if (dismissed)
        {
            _calPrompt = null;
        }
    }

    private static bool CalendarPromptButton(string id, string label, Vector2 tl, Vector2 size, Vector4 fill)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var col = hovered ? fill with { W = MathF.Min(1f, fill.W + 0.15f) } : fill;
        dl.AddRectFilled(tl, tl + size, ImGui.GetColorU32(col), Px(10f));
        var sz = ImGui.CalcTextSize(label);
        dl.PushClipRect(tl, tl + size, true);
        dl.AddText(tl + (size - sz) * 0.5f, 0xF2FFFFFFu, label);
        dl.PopClipRect();
        return clicked;
    }
}
