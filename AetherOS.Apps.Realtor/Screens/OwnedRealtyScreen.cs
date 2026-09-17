using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Realtor;

/// <summary>Every house the app is watching, one row per character and house, with how long that character
/// has been away from it. Rows warm from amber to red well before the phone starts announcing
/// anything, because this is the screen you come to look at rather than one that comes to you.</summary>
internal sealed class OwnedRealtyScreen
{
    private const float PadX = 16f;

    private readonly EstateView _estates;
    private readonly Action _back;
    private readonly EntranceAnimation _entrance = new();
    private EstateRecord? _pendingRemove;
    private float _removePanelH;

    public OwnedRealtyScreen(EstateView estates, Action back)
    {
        _estates = estates;
        _back = back;
    }

    public void OnShow()
    {
        _entrance.Arm();
        _pendingRemove = null;
    }

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
            DrawFootnote(winW, HasFreeCompany(estates));
        });
        DrawRemoveConfirm();
        _entrance.EndFrame();
    }

    /// <summary>One house, four lines: what kind of house it is, where it stands, whose it is, and when it
    /// was last entered. Every line wraps instead of being cut, and the card grows to fit, so a long name
    /// or a long language never hides the address. The day count sits beside the first line only.</summary>
    private void DrawRow(EstateRecord estate, DateTime now, float winW)
    {
        var days = EstateRisk.DaysAway(estate, now);
        var color = estate.VisitObserved ? RowColor(days) : null;
        var isFc = estate.Kind == EstateKind.FreeCompany;
        var accent = color ?? ThemeService.Current.AccentLight;
        var cardW = winW - (Px(PadX) * 2f);
        var padY = Px(11f);
        var lineGap = Px(3f);
        var circle = Px(36f);

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var circleTl = new Vector2(tl.X + Px(11f), tl.Y + padY);
        var textX = circleTl.X + circle + Px(11f);
        var right = tl.X + cardW - Px(14f);
        var fullW = right - textX;

        // No count until a visit has been watched: there is nothing honest to put in the pill before that.
        var pillLabel = estate.VisitObserved ? Loc.T("os.realtor_realty_days", days) : string.Empty;
        var pillSz = pillLabel.Length > 0 ? ImGui.CalcTextSize(pillLabel) + new Vector2(Px(16f), Px(6f)) : Vector2.Zero;
        var kindW = pillLabel.Length > 0 ? fullW - pillSz.X - Px(8f) : fullW;

        // Stamped as UTC but round-tripped through JSON, which loses the kind; without saying so again the
        // conversion is skipped and the date reads wrong by the local offset.
        var when = estate.VisitObserved
            ? Loc.T(isFc ? "os.realtor_realty_fc_entered" : "os.realtor_realty_entered",
                DateTime.SpecifyKind(estate.LastVisitUtc, DateTimeKind.Utc).ToLocalTime().ToString("d"))
            : Loc.T(isFc ? "os.realtor_realty_fc_never" : "os.realtor_realty_never");
        var who = estate.World.Length > 0 ? $"{estate.Character} ({estate.World})" : estate.Character;
        var removeSide = Px(26f);
        var lines = new (string Text, Vector4 Color, float Width)[]
        {
            (Loc.T(isFc ? "os.realtor_realty_kind_fc" : "os.realtor_realty_kind_personal"), accent, kindW),
            (Address(estate), UiColors.Body, fullW),
            (who, UiColors.Body, fullW),
            (when, UiColors.Hint, fullW - removeSide - Px(8f)),
        };

        var textH = lineGap * (lines.Length - 1);
        foreach (var line in lines)
        {
            textH += ImGui.CalcTextSize(line.Text, false, line.Width).Y;
        }
        var cardH = (padY * 2f) + MathF.Max(textH, circle);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), Px(14f));
        // Only a row that has something to say gets an outline, so the list reads at a glance.
        if (color is { } tint)
        {
            dl.AddRect(tl, tl + new Vector2(cardW, cardH), ImGui.GetColorU32(tint with { W = 0.55f }),
                Px(14f), ImDrawFlags.None, Px(1.2f));
        }

        dl.AddCircleFilled(circleTl + new Vector2(circle * 0.5f, circle * 0.5f), circle * 0.5f,
            ImGui.GetColorU32(accent with { W = 0.22f }));
        var iconPx = circle * 0.52f;
        var icon = isFc ? FontAwesomeIcon.Users : FontAwesomeIcon.Home;
        var iconSz = IconDraw.Measure(icon, iconPx);
        IconDraw.Add(dl, icon, iconPx,
            circleTl + new Vector2((circle - iconSz.X) * 0.5f, (circle - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(accent));

        if (pillLabel.Length > 0)
        {
            var pillTl = new Vector2(right - pillSz.X, tl.Y + padY - Px(3f));
            dl.AddRectFilled(pillTl, pillTl + pillSz, ImGui.GetColorU32(accent with { W = 0.16f }), pillSz.Y * 0.5f);
            dl.AddText(pillTl + new Vector2(Px(8f), Px(3f)), ImGui.GetColorU32(accent), pillLabel);
        }

        var y = tl.Y + padY;
        foreach (var line in lines)
        {
            ImGui.SetCursorScreenPos(new Vector2(textX, y));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + line.Width);
            ImGui.PushStyleColor(ImGuiCol.Text, line.Color);
            ImGui.TextUnformatted(line.Text);
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
            y += ImGui.CalcTextSize(line.Text, false, line.Width).Y + lineGap;
        }

        var removeTl = new Vector2(right - removeSide, tl.Y + cardH - padY - removeSide + Px(4f));
        ImGui.SetCursorScreenPos(removeTl);
        if (ImGui.InvisibleButton($"##realtyRemove{estate.ContentId}_{(int)estate.Kind}",
            new Vector2(removeSide, removeSide)))
        {
            _pendingRemove = estate;
            _removePanelH = 0f;
        }
        HandOnHover();
        var removeHovered = ImGui.IsItemHovered();
        var removeCenter = removeTl + new Vector2(removeSide * 0.5f, removeSide * 0.5f);
        dl.AddCircleFilled(removeCenter, removeSide * 0.5f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, removeHovered ? 0.16f : 0.07f)));
        IconDraw.AddCentered(dl, FontAwesomeIcon.TrashAlt, Px(12f), removeCenter,
            ImGui.GetColorU32(removeHovered ? UiColors.Danger : UiColors.Hint));
        if (removeHovered)
        {
            ImGui.SetTooltip(Loc.T("os.realtor_realty_remove_confirm"));
        }

        ImGui.SetCursorScreenPos(tl);
        ImGui.Dummy(new Vector2(cardW, cardH));
        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    /// <summary>The in-page "are you sure" for a row's remove button. Removal is not permanent for a house the
    /// character still has: the watcher lists it again on that character's next login, and the copy says so.</summary>
    private void DrawRemoveConfirm()
    {
        if (_pendingRemove is not { } pending)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _pendingRemove = null;
            return;
        }

        var confirmed = false;
        var cancelled = false;
        var dismissed = DrawPageOverlayPanel("realtyRemove", ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ref _removePanelH, Px(220f), innerW =>
        {
            ModalUi.Header(innerW, FontAwesomeIcon.TrashAlt, Loc.T("os.realtor_realty_remove_title"),
                UiColors.Danger);
            var isFc = pending.Kind == EstateKind.FreeCompany;
            var who = pending.World.Length > 0 ? $"{pending.Character} ({pending.World})" : pending.Character;
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body,
                Loc.T(isFc ? "os.realtor_realty_kind_fc" : "os.realtor_realty_kind_personal"));
            ImGui.TextColored(UiColors.Body, Address(pending));
            ImGui.TextColored(UiColors.Body, who);
            ImGui.Spacing();
            ImGui.TextColored(UiColors.Hint, Loc.T("os.realtor_realty_remove_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            var btnW = (innerW - Px(10f)) * 0.5f;
            PushDangerButton();
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (Button($"{Loc.T("os.realtor_realty_remove_confirm")}##realtyRemoveOk", new Vector2(btnW, Px(32f))))
            {
                confirmed = true;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
            ImGui.SameLine(0f, Px(10f));
            if (ModalUi.Button($"{Loc.T("common.cancel")}##realtyRemoveCancel", btnW))
            {
                cancelled = true;
            }
        });

        if (confirmed)
        {
            _estates.Remove(pending);
            _pendingRemove = null;
        }
        else if (cancelled || dismissed)
        {
            _pendingRemove = null;
        }
    }

    /// <summary>District, ward and plot, with whichever half is known when the other is not. A book written
    /// before the address was read off the house id has no ward until the next login on that character.</summary>
    private static string Address(EstateRecord estate)
    {
        var zone = RealtorUi.ZoneName(estate.TerritoryTypeId);
        if (estate.Ward <= 0 || estate.Plot <= 0)
        {
            return zone.Length > 0 ? zone : Loc.T("os.realtor_realty_address_unknown");
        }
        var wardPlot = Loc.T("os.realtor_ward_plot", estate.Ward, estate.Plot);
        return zone.Length > 0 ? $"{zone} · {wardPlot}" : wardPlot;
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

    private static bool HasFreeCompany(System.Collections.Generic.IReadOnlyList<EstateRecord> estates)
    {
        foreach (var estate in estates)
        {
            if (estate.Kind == EstateKind.FreeCompany)
            {
                return true;
            }
        }
        return false;
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
    private static void DrawFootnote(float winW, bool hasFreeCompany)
    {
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Hint);
        ImGui.TextUnformatted(Loc.T("os.realtor_realty_note", EstateRisk.LimitDays));
        if (hasFreeCompany)
        {
            ImGui.Dummy(new Vector2(0f, Px(6f)));
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextUnformatted(Loc.T("os.realtor_realty_fc_note"));
        }
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(12f)));
    }
}
