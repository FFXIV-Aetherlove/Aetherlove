using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherOS.Apps.Together.Screens;

/// <summary>The party explainer, five beats: what a party is, where it lives, what playing together pays,
/// the Aetherlings gathering (skipped for somebody with no pet, who has nothing to gather), and the pet
/// switches. The settings-only entry jumps straight to the switches. Finishing sets the shell's own seen
/// flag through the host, so the widget card stops intercepting.</summary>
internal sealed class TourScreen(ITogetherHost host, Action done)
{
    private enum Step
    {
        WhatItIs,
        WhereItLives,
        Sparks,
        Pets,
        Settings,
    }

    /// <summary>Stable keys for the two sample companions, so the renderer keeps one runtime each.</summary>
    private static readonly Guid LeftSample = new("7d0a2c2e-3a5b-4a6e-9f1b-0a1b2c3d4e51");
    private static readonly Guid RightSample = new("7d0a2c2e-3a5b-4a6e-9f1b-0a1b2c3d4e52");
    private static readonly string[] NoAccessories = [];

    private readonly List<Step> _steps = [];
    private int _index;
    private bool _settingsOnly;

    public void OnShow(bool settingsOnly)
    {
        _settingsOnly = settingsOnly;
        _steps.Clear();
        if (settingsOnly)
        {
            _steps.Add(Step.Settings);
        }
        else
        {
            _steps.Add(Step.WhatItIs);
            _steps.Add(Step.WhereItLives);
            _steps.Add(Step.Sparks);
            if (host.HasPet)
            {
                _steps.Add(Step.Pets);
            }
            _steps.Add(Step.Settings);
        }
        _index = 0;
    }

    public void Draw(OsAppContext ctx)
    {
        if (_steps.Count == 0)
        {
            OnShow(false);
        }
        if (DrawProgress(_index, _steps.Count, true))
        {
            if (_index == 0)
            {
                done();
            }
            else
            {
                _index--;
            }
        }

        const float topH = 34f;
        const float navH = 62f;
        var contentH = ImGui.GetWindowSize().Y - Px(topH) - Px(navH);

        ImGui.SetCursorPos(new Vector2(0f, Px(topH)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##togetherTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_steps[_index])
                {
                    case Step.WhatItIs:
                        DrawWhatItIs();
                        break;
                    case Step.WhereItLives:
                        DrawWhereItLives();
                        break;
                    case Step.Sparks:
                        DrawSparks();
                        break;
                    case Step.Pets:
                        DrawPets(ctx);
                        break;
                    default:
                        DrawSettings();
                        break;
                }
            }
        }
        PopScrollbarStyle();

        var last = _index >= _steps.Count - 1;
        var label = _settingsOnly
            ? Loc.T("os.party_dismiss")
            : last ? Loc.T("os.party_intro_start") : Loc.T("os.party_intro_next");
        ImGui.SetCursorPos(new Vector2(0f, ImGui.GetWindowSize().Y - Px(54f)));
        if (DrawPrimaryButton(label, true))
        {
            if (last)
            {
                host.Pets?.Forget([]);
                done();
            }
            else
            {
                _index++;
            }
        }
    }

    private static void DrawWhatItIs()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("party_intro_together", FontAwesomeIcon.UserFriends, Loc.T("os.party_intro_title_0"),
            Loc.T("os.party_intro_body_0"), 40f);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Key, Loc.T("os.party_intro_f0_code"));
        DrawFeatureRow(FontAwesomeIcon.Bolt, Loc.T("os.party_intro_f0_activity"));
        DrawFeatureRow(FontAwesomeIcon.Users, Loc.T("os.party_intro_f0_size"));
    }

    private static void DrawWhereItLives()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("party_intro_dock", FontAwesomeIcon.Comments, Loc.T("os.party_intro_title_1"),
            Loc.T("os.party_intro_body_1"), 40f);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Th, Loc.T("os.together_tour_f1_app"));
        DrawFeatureRow(FontAwesomeIcon.Columns, Loc.T("os.party_intro_f1_widget"));
        DrawFeatureRow(FontAwesomeIcon.CommentDots, Loc.T("os.party_intro_f1_chat"));
    }

    private static void DrawSparks()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("party_intro_sparks", FontAwesomeIcon.Bolt, Loc.T("os.together_tour_sparks_title"),
            Loc.T("os.together_tour_sparks_body"), 40f);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Compass, Loc.T("os.together_tour_sparks_wayfinder"));
        DrawFeatureRow(FontAwesomeIcon.Film, Loc.T("os.together_tour_sparks_echo"));
        DrawFeatureRow(FontAwesomeIcon.Wallet, Loc.T("os.together_tour_sparks_wallet"));
    }

    /// <summary>Three creatures on a ledge, the player's own in the middle, drawn by the pet app through
    /// its renderer. The two companions wear fixed sample looks; nothing here is another player.</summary>
    private void DrawPets(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        var winW = ImGui.GetWindowSize().X;
        var stageH = Px(150f);
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var floorY = tl.Y + stageH - Px(14f);
        var centreX = tl.X + (winW * 0.5f);

        var accent = ThemeService.Current.Accent;
        for (var i = 0; i < 4; i++)
        {
            dl.AddCircleFilled(new Vector2(centreX, floorY), Px(120f) - (i * Px(22f)),
                ImGui.GetColorU32(accent with { W = 0.05f }), 48);
        }
        // The ledge: a squashed disc, because ImGui has no ellipse and a circle on a floor reads as a ball.
        dl.PathClear();
        for (var i = 0; i <= 48; i++)
        {
            var a = i / 48f * MathF.Tau;
            dl.PathLineTo(new Vector2(centreX + (MathF.Cos(a) * Px(150f)), floorY + (MathF.Sin(a) * Px(14f))));
        }
        dl.PathFillConvex(ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)));

        if (host.Pets is { } pets)
        {
            var side = Px(84f);
            var own = Px(108f);
            var gap = Px(96f);
            pets.Draw(dl, LeftSample, new Vector2(centreX - gap, floorY - Px(2f)), side, 3, "ember", NoAccessories, ctx.ReduceMotion);
            pets.Draw(dl, RightSample, new Vector2(centreX + gap, floorY - Px(2f)), side, 3, "frost", NoAccessories, ctx.ReduceMotion);
            pets.DrawOwn(dl, new Vector2(centreX, floorY), own, ctx.ReduceMotion);
        }
        ImGui.Dummy(new Vector2(0f, stageH));

        using (UiFonts.H2?.Push())
        {
            var title = Loc.T("os.party_intro_title_2");
            var sz = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX((winW - sz.X) * 0.5f);
            ImGui.TextColored(UiColors.Body, title);
        }
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawCenteredParagraph(Loc.T("os.party_intro_body_2"), winW - Px(40f), UiColors.Body);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Eye, Loc.T("os.party_intro_pets_show_hint"));
        DrawFeatureRow(FontAwesomeIcon.Share, Loc.T("os.party_intro_pets_share_hint"));
    }

    private void DrawSettings()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("party_intro_pets", FontAwesomeIcon.Paw, Loc.T("os.party_settings"),
            Loc.T("os.together_tour_settings_body"), 40f);
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        var margin = Px(20f);
        var width = ImGui.GetWindowSize().X - (margin * 2f);

        var show = host.ShowPartyPets;
        if (SettingCard("togetherPetsShow", Loc.T("os.party_intro_pets_show"), Loc.T("os.party_intro_pets_show_hint"),
                margin, width, ref show))
        {
            host.ShowPartyPets = show;
        }
        ImGui.Dummy(new Vector2(0f, Px(8f)));
        if (host.HasPet)
        {
            var share = host.ShareMyPet;
            if (SettingCard("togetherPetsShare", Loc.T("os.party_intro_pets_share"), Loc.T("os.party_intro_pets_share_hint"),
                    margin, width, ref share))
            {
                host.ShareMyPet = share;
            }
            ImGui.Dummy(new Vector2(0f, Px(8f)));
        }
        if (!host.ShowPartyPets)
        {
            return;
        }
        ImGui.SetCursorPosX(margin);
        ImGui.TextColored(UiColors.Hint, Loc.T("os.party_intro_pets_size"));
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        DrawSizeRow(margin, width);
    }

    private static bool SettingCard(string id, string label, string hint, float x, float width, ref bool value)
    {
        var dl = ImGui.GetWindowDrawList();
        var padIn = Px(12f);
        var box = ImGui.GetFrameHeight();
        var textW = width - (padIn * 2f) - box - Px(12f);
        var labelH = ImGui.CalcTextSize(label, false, textW).Y;
        var hintH = ImGui.CalcTextSize(hint, false, textW).Y;
        var tl = new Vector2(ImGui.GetWindowPos().X + x, ImGui.GetCursorScreenPos().Y);
        var size = new Vector2(width, padIn + labelH + Px(4f) + hintH + padIn);
        var br = tl + size;

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), Px(12f));
        dl.AddRect(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(12f), ImDrawFlags.RoundCornersAll, 1f);

        ImGui.SetCursorScreenPos(new Vector2(br.X - padIn - box, tl.Y + ((size.Y - box) * 0.5f)));
        var changed = ImGui.Checkbox($"##{id}", ref value);
        HandOnHover();

        // Submitted after the box, so the box keeps its own clicks and this catches the rest of the card.
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##{id}Row", size))
        {
            value = !value;
            changed = true;
        }
        HandOnHover();

        ImGui.SetCursorScreenPos(tl + new Vector2(padIn, padIn));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
        ImGui.TextUnformatted(label);
        ImGui.SetCursorScreenPos(new Vector2(tl.X + padIn, tl.Y + padIn + labelH + Px(4f)));
        ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.62f), hint);
        ImGui.PopTextWrapPos();

        ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y));
        return changed;
    }

    private void DrawSizeRow(float x, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var count = host.PartyPetSizeCount;
        var gap = Px(6f);
        var height = Px(34f);
        var chip = (width - (gap * (count - 1))) / count;
        var tl = new Vector2(ImGui.GetWindowPos().X + x, ImGui.GetCursorScreenPos().Y);
        var t = ThemeService.Current;

        for (var i = 0; i < count; i++)
        {
            var chipTl = tl + new Vector2(i * (chip + gap), 0f);
            ImGui.SetCursorScreenPos(chipTl);
            var pressed = ImGui.InvisibleButton($"##togetherPetSize{i}", new Vector2(chip, height));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
            }
            if (pressed)
            {
                host.PartyPetSize = i;
            }
            var picked = host.PartyPetSize == i;
            dl.AddRectFilled(chipTl, chipTl + new Vector2(chip, height),
                picked ? ImGui.GetColorU32(t.Accent) : ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.16f : 0.08f)),
                Px(10f));
            var label = Loc.T($"os.party_pet_size_{i}");
            var labelSz = ImGui.CalcTextSize(label);
            dl.AddText(chipTl + ((new Vector2(chip, height) - labelSz) * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, picked ? 1f : 0.78f)), label);
        }
        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + height));
    }
}
