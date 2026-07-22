using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Places;

public partial class PlacesScreen
{
    private void DrawAddVenue()
    {
        var winW = ImGui.GetWindowSize().X;
        var t = ThemeService.Current;

        PushScrollbarStyle();
        var scrollViewportTL = ImGui.GetCursorScreenPos();
        using (var scroll = ImRaii.Child("##placesAddVenue", ImGui.GetContentRegionAvail(), false))
        {
            PopScrollbarStyle();
            if (!scroll.Success)
            {
                return;
            }

            var pad = Px(PadX);
            var btnW = winW - pad * 2f;

            ImGui.Dummy(new Vector2(1f, Px(44f)));

            ImGui.SetCursorPosX(pad);
            using (UiFonts.H1?.Push())
            {
                ImGui.TextColored(t.AccentLight, Loc.T("places.addvenue_title"));
            }
            ImGui.Spacing();

            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(winW - pad);
            ImGui.TextColored(UiColors.Body, Loc.T("places.addvenue_intro"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();

            DrawSectionHeader(Loc.T("places.addvenue_perks_header"), PadX);
            var gold = new Vector4(0.96f, 0.78f, 0.30f, 1f);
            var green = new Vector4(0.45f, 0.85f, 0.48f, 1f);
            var violet = new Vector4(0.72f, 0.52f, 0.96f, 1f);
            DrawPerkCard(winW, PadX, FontAwesomeIcon.Star, gold,
                Loc.T("places.addvenue_perk_reviews_title"), Loc.T("places.addvenue_perk_reviews_body"));
            DrawPerkCard(winW, PadX, FontAwesomeIcon.Users, green,
                Loc.T("places.addvenue_perk_reach_title"), Loc.T("places.addvenue_perk_reach_body"));
            DrawPerkCard(winW, PadX, FontAwesomeIcon.Comments, violet,
                Loc.T("places.addvenue_perk_share_title"), Loc.T("places.addvenue_perk_share_body"));
            ImGui.Spacing();

            DrawSectionHeader(Loc.T("places.addvenue_how_heading"), PadX);
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(winW - pad);
            ImGui.TextColored(UiColors.Body, Loc.T("places.addvenue_how_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            ImGui.SetCursorPosX(pad);
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.Discord with { W = 0.92f });
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.Discord);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.Discord with { W = 0.82f });
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
            if (ImGui.Button(Loc.T("places.addvenue_discord_btn"), new Vector2(btnW, Px(40f))))
            {
                OpenDiscord();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
            ImGui.Spacing();

            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(winW - pad);
            ImGui.TextColored(UiColors.Hint, Loc.T("places.addvenue_ticket_note"));
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(1f, Px(12f)));
        }

        if (DrawFloatingBackPill(scrollViewportTL + Px(10f, 10f), Loc.T("places.back"), FontAwesomeIcon.List))
        {
            _section = Section.Browse;
            _entrance.Arm();
        }
    }
}
