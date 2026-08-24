using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Os;

/// <summary>The first-run explainer for together mode: three beats, the last of which is the party-pet
/// settings. Raised by the party card the first time somebody creates or joins, and it carries that action
/// with it, so the last button both saves the answers and does the thing they asked for.
/// <para>Built out of <see cref="OnboardingUi"/> rather than a look of its own, because every other
/// onboarding on the phone is: the segmented progress bar, the hero badge, feature rows and the full-width
/// primary button are the house style, and an explainer that looked like nothing else on the phone was the
/// first thing anybody noticed about it.</para></summary>
public sealed class TogetherOnboarding(IOsTogether together)
{
    private const int Pages = 3;

    private int _page;
    private Action? _then;
    private bool _settingsOnly;

    public bool Active { get; private set; }

    /// <summary>Puts the explainer up, to be followed by <paramref name="then"/> when it is done.</summary>
    public void Show(Action? then)
    {
        Active = true;
        _settingsOnly = false;
        _page = 0;
        _then = then;
    }

    /// <summary>The last page on its own, for the party widget's settings row: the explainer beats are not
    /// worth re-reading to move a switch.</summary>
    public void ShowSettings()
    {
        Active = true;
        _settingsOnly = true;
        _page = Pages - 1;
        _then = null;
    }

    /// <summary>Closes the explainer without running whatever it was carrying, for the home button. The
    /// seen flag is only set by finishing it, so a dismissed first run still teaches the feature next time.</summary>
    public void Dismiss()
    {
        if (!Active)
        {
            return;
        }
        Active = false;
        _settingsOnly = false;
        _then = null;
    }

    public void Draw(Vector2 contentTL, Vector2 contentBR)
    {
        if (!Active)
        {
            return;
        }

        var size = contentBR - contentTL;
        ImGui.SetCursorScreenPos(contentTL);
        using var layer = ImRaii.Child("##togetherOnboarding", size, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }

        // Opaque, because this is a page rather than something floating over the home screen; the phone's
        // own backdrop is what every other onboarding sits on.
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(contentTL, contentBR,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.055f, 0.05f, 0.08f, 1f)));

        if (OnboardingUi.DrawProgress(_page, _settingsOnly ? 1 : Pages, !_settingsOnly && _page > 0)
            && _page > 0)
        {
            _page--;
        }

        var navH = Px(62f);
        ImGui.SetCursorPos(new Vector2(0f, Px(34f)));
        SharedUiHelpers.PushScrollbarStyle();
        using (var content = ImRaii.Child("##togetherOnboardingBody", new Vector2(0f, size.Y - Px(34f) - navH), false))
        {
            if (content.Success)
            {
                switch (_page)
                {
                    case 0:
                        DrawWhatItIs();
                        break;
                    case 1:
                        DrawWhereItLives();
                        break;
                    default:
                        DrawPets();
                        break;
                }
            }
        }
        SharedUiHelpers.PopScrollbarStyle();

        var last = _page == Pages - 1;
        ImGui.SetCursorPos(new Vector2(0f, size.Y - Px(54f)));
        var label = Loc.T(_settingsOnly ? "os.party_dismiss" : last ? "os.party_intro_start" : "os.party_intro_next");
        if (OnboardingUi.DrawPrimaryButton(label, true))
        {
            if (!last)
            {
                _page++;
                return;
            }
            Finish();
        }
    }

    private void Finish()
    {
        Active = false;
        _settingsOnly = false;
        together.OnboardingSeen = true;
        var then = _then;
        _then = null;
        then?.Invoke();
    }

    private static void DrawWhatItIs()
    {
        OnboardingUi.DrawHero("party_intro_together", FontAwesomeIcon.UserFriends,
            Loc.T("os.party_intro_title_0"), Loc.T("os.party_intro_body_0"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Key, Loc.T("os.party_intro_f0_code"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Film, Loc.T("os.party_intro_f0_activity"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.UserPlus, Loc.T("os.party_intro_f0_size"));
    }

    private static void DrawWhereItLives()
    {
        OnboardingUi.DrawHero("party_intro_dock", FontAwesomeIcon.Comments,
            Loc.T("os.party_intro_title_1"), Loc.T("os.party_intro_body_1"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.ThLarge, Loc.T("os.party_intro_f1_widget"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Users, Loc.T("os.party_intro_f1_dock"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.CommentDots, Loc.T("os.party_intro_f1_chat"));
    }

    /// <summary>The pets page: the two halves of the switch and how big they stand. The sending half is only
    /// offered to somebody who has a pet at all, since a switch for a creature you have never had explains
    /// nothing.</summary>
    private void DrawPets()
    {
        OnboardingUi.DrawHero("party_intro_pets", FontAwesomeIcon.Paw,
            Loc.T("os.party_intro_title_2"), Loc.T("os.party_intro_body_2"));

        var winW = ImGui.GetWindowSize().X;
        var margin = Px(20f);
        var width = winW - (margin * 2f);

        var show = together.ShowPartyPets;
        if (SettingCard("partyPetsShow", Loc.T("os.party_intro_pets_show"), Loc.T("os.party_intro_pets_show_hint"),
                margin, width, ref show))
        {
            together.ShowPartyPets = show;
        }

        if (together.HasPet)
        {
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            var share = together.ShareMyPet;
            if (SettingCard("partyPetsShare", Loc.T("os.party_intro_pets_share"),
                    Loc.T("os.party_intro_pets_share_hint"), margin, width, ref share))
            {
                together.ShareMyPet = share;
            }
        }

        if (!together.ShowPartyPets)
        {
            return;
        }

        ImGui.Dummy(new Vector2(0f, Px(14f)));
        ImGui.SetCursorPosX(margin);
        ImGui.TextColored(ThemeService.Current.AccentLight, Loc.T("os.party_intro_pets_size"));
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawSizeRow(margin, width);
    }

    /// <summary>The house settings card: a ghost-filled rounded box carrying a label, a hint and a checkbox,
    /// the whole thing clickable. Hand-drawn rather than shared from <c>AppSettingsUi</c> because that one
    /// takes an app's frame context and the shell has none.</summary>
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

        dl.AddRectFilled(tl, br, OsDraw.White(0.08f), Px(12f));
        dl.AddRect(tl, br, OsDraw.White(0.10f), Px(12f), ImDrawFlags.RoundCornersAll, 1f);

        ImGui.SetCursorScreenPos(new Vector2(br.X - padIn - box, tl.Y + ((size.Y - box) * 0.5f)));
        var changed = ImGui.Checkbox($"##{id}", ref value);
        SharedUiHelpers.HandOnHover();

        // Submitted after the box, so the box keeps its own clicks and this catches the rest of the card.
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##{id}Row", size))
        {
            value = !value;
            changed = true;
        }
        SharedUiHelpers.HandOnHover();

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
        var count = together.PartyPetSizeCount;
        var gap = Px(6f);
        var height = Px(34f);
        var chip = (width - (gap * (count - 1))) / count;
        var tl = new Vector2(ImGui.GetWindowPos().X + x, ImGui.GetCursorScreenPos().Y);

        for (var i = 0; i < count; i++)
        {
            var chipTL = tl + new Vector2(i * (chip + gap), 0f);
            ImGui.SetCursorScreenPos(chipTL);
            var pressed = ImGui.InvisibleButton($"##partyPetSize{i}", new Vector2(chip, height));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                SharedUiHelpers.HandOnHover();
            }
            if (pressed)
            {
                together.PartyPetSize = i;
            }

            var picked = together.PartyPetSize == i;
            var t = ThemeService.Current;
            dl.AddRectFilled(chipTL, chipTL + new Vector2(chip, height),
                picked ? t.AccentU32 : OsDraw.White(hovered ? 0.16f : 0.08f), Px(10f));
            var label = Loc.T($"os.party_pet_size_{i}");
            var labelSz = ImGui.CalcTextSize(label);
            dl.AddText(chipTL + ((new Vector2(chip, height) - labelSz) * 0.5f),
                OsDraw.White(picked ? 1f : 0.78f), label);
        }

        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + height));
    }
}
