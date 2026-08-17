using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>Every private estate this install knows about, one row per character, with how long that
/// character has been away from it. Rows warm from amber to red well before the phone starts announcing
/// anything, because this is the screen you come to look at rather than one that comes to you.</summary>
internal sealed class OwnedRealtyScreen
{
    private const float PadX = 16f;
    private const float RowHeight = 58f;

    private readonly IEstateWatch _estates;
    private readonly Action _back;
    private readonly EntranceAnimation _entrance = new();

    public OwnedRealtyScreen(IEstateWatch estates, Action back)
    {
        _estates = estates;
        _back = back;
    }

    public void OnShow() => _entrance.Arm();

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        if (RealtorHeader.Draw(Loc.T("os.realtor_realty_title")))
        {
            _entrance.EndFrame();
            _back();
            return;
        }

        var estates = _estates.Estates;
        if (estates.Count == 0)
        {
            DrawEmpty();
            _entrance.EndFrame();
            return;
        }

        var now = DateTime.UtcNow;
        RealtorUi.ScrollBody("##realtorRealtyBody", () =>
        {
            var winW = ImGui.GetWindowSize().X;
            ImGui.Dummy(new Vector2(0f, Px(2f)));
            foreach (var estate in estates)
            {
                DrawRow(estate, now, winW);
            }
            DrawFootnote(winW);
        });
        _entrance.EndFrame();
    }

    private static void DrawRow(EstateRecord estate, DateTime now, float winW)
    {
        var days = EstateRisk.DaysAway(estate, now);
        var color = estate.VisitObserved ? RowColor(days) : null;
        var cardW = winW - (Px(PadX) * 2f);
        var cardH = Px(RowHeight);

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(cardW, cardH));

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), Px(14f));
        // Only a row that has something to say gets an outline, so the list reads at a glance.
        if (color is { } tint)
        {
            dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(tint with { W = 0.55f }),
                Px(14f), ImDrawFlags.None, Px(1.2f));
        }

        var accent = color ?? ThemeService.Current.AccentLight;
        var circle = Px(36f);
        var circleTl = new Vector2(tl.X + Px(11f), tl.Y + (cardH - circle) * 0.5f);
        dl.AddCircleFilled(circleTl + new Vector2(circle * 0.5f, circle * 0.5f), circle * 0.5f,
            ImGui.GetColorU32(accent with { W = 0.22f }));
        var iconPx = circle * 0.52f;
        var iconSz = IconDraw.Measure(FontAwesomeIcon.Home, iconPx);
        IconDraw.Add(dl, FontAwesomeIcon.Home, iconPx,
            circleTl + new Vector2((circle - iconSz.X) * 0.5f, (circle - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(accent));

        // No count until a visit has been watched: there is nothing honest to put in the pill before that.
        var pillLeft = tl.X + cardW - Px(14f);
        if (estate.VisitObserved)
        {
            var pillLabel = Loc.T("os.realtor_realty_days", days);
            var pillSz = ImGui.CalcTextSize(pillLabel);
            var pillH = pillSz.Y + Px(8f);
            var pillTl = new Vector2(tl.X + cardW - pillSz.X - Px(16f) - Px(14f),
                tl.Y + (cardH - pillH) * 0.5f);
            dl.AddRectFilled(pillTl, pillTl + new Vector2(pillSz.X + Px(16f), pillH),
                ImGui.GetColorU32(accent with { W = 0.16f }), pillH * 0.5f);
            dl.AddText(pillTl + new Vector2(Px(8f), Px(4f)), ImGui.GetColorU32(accent), pillLabel);
            pillLeft = pillTl.X;
        }

        var textX = circleTl.X + circle + Px(11f);
        var limit = pillLeft - textX - Px(8f);
        var lineH = ImGui.GetTextLineHeight();
        var who = estate.World.Length > 0 ? $"{estate.Character} ({estate.World})" : estate.Character;
        dl.AddText(new Vector2(textX, tl.Y + (cardH * 0.5f) - lineH - Px(1f)),
            ImGui.GetColorU32(UiColors.Body), TruncateToWidth(who, limit));

        // Stamped as UTC but round-tripped through JSON, which loses the kind; without saying so again the
        // conversion is skipped and the date reads wrong by the local offset.
        var when = estate.VisitObserved
            ? Loc.T("os.realtor_realty_entered",
                DateTime.SpecifyKind(estate.LastVisitUtc, DateTimeKind.Utc).ToLocalTime().ToString("d"))
            : Loc.T("os.realtor_realty_never");
        dl.AddText(new Vector2(textX, tl.Y + (cardH * 0.5f) + Px(2f)),
            ImGui.GetColorU32(UiColors.Hint), TruncateToWidth(when, limit));

        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    /// <summary>Null while there is nothing to say; the list deliberately warms up earlier than the banner.</summary>
    private static Vector4? RowColor(int days)
    {
        if (days >= EstateRisk.ListRedDays)
        {
            return RealtorUi.RiskRed;
        }
        return days >= EstateRisk.ListAmberDays ? RealtorUi.RiskAmber : null;
    }

    private static void DrawEmpty()
    {
        ImGui.Dummy(new Vector2(0f, Px(30f)));
        var winW = ImGui.GetWindowSize().X;
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Hint);
        ImGui.TextUnformatted(Loc.T("os.realtor_realty_empty"));
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
    }

    /// <summary>The honest footnote. Everything above is measured from what this install saw, not from the
    /// game's own countdown, and saying so is what keeps the numbers trustworthy.</summary>
    private static void DrawFootnote(float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Hint);
        ImGui.TextUnformatted(Loc.T("os.realtor_realty_note", EstateRisk.LimitDays));
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(12f)));
    }
}
