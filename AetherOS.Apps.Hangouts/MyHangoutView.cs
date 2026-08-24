using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Hangouts;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Hangouts;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Hangouts;

/// <summary>The user's create / manage-my-hangout view inside the Hangouts app.</summary>
public sealed class MyHangoutView
{
    private const float PadX = 16f;

    private static readonly int[] StartOffsetsMinutes = [0, 30, 60, 120, 240, 480];
    private static readonly int[] DurationsMinutes = [30, 60, 120, 180, 240, 360, 480];

    private static readonly Region[] HangoutRegions =
    [
        Region.NorthAmerica,
        Region.Europe,
        Region.Japan,
        Region.Oceania,
    ];

    private readonly IHangoutsHost _host;
    private readonly HangoutStateService _state;
    private readonly Action _back;

    private int _hgCategoryIdx = 3;
    private int _hgCategoryZero;
    private string _hgDescription = "";
    private string _hgDataCenter = "";
    private string _hgWorld = "";
    private string _hgLocation = "";
    private int _hgStartIdx;
    private int _hgDurationIdx = 2;
    private bool _hgLimitEnabled;
    private int _hgLimit = 5;
    private bool _hgUnlistWhenFull;
    private bool _hgPublic = true;
    private volatile bool _hgSubmitting;
    private volatile string? _hgError;

    /// <summary>Set when the form was opened from a party share: the next submit publishes THAT party rather
    /// than creating a plain hangout. Cleared on submit, so a later ordinary create is never mistaken for
    /// another publish.</summary>
    public Guid? PendingPartyId { get; set; }
    private bool _hgEndArmed;
    private double _hgCopiedUntil;

    /// <summary>Set by the host to the share-sheet offer; lets the owner share their own live hangout.</summary>
    public Action<HangoutSummaryDto>? ShareHandler { get; set; }

    public MyHangoutView(IHangoutsHost host, HangoutStateService state, Action back)
    {
        _host = host;
        _state = state;
        _back = back;
    }

    public void OnShow()
    {
        _hgError = null;
        _hgEndArmed = false;
        // A party publish arrives pre-answered: the category is the party's own and the party is wherever
        // its host is standing, so the current world overwrites whatever the form remembered. The player
        // writes a description and presses publish.
        if (PendingPartyId is not null || (_state.MyHangout is null && _hgDataCenter.Length == 0))
        {
            var detected = VenueLocationDetector.Detect();
            if (detected.World.Length > 0)
            {
                _hgDataCenter = detected.DataCenter;
                _hgWorld = detected.World;
            }
        }
    }

    public void Draw()
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("places.back"), FontAwesomeIcon.List))
        {
            _back();
        }
        ImGui.Spacing();
        DrawSubpageHeading(Loc.T("hangout.my_title"), PadX);

        var pad = Px(PadX);
        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##myHangoutScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, pad);
                ImGui.Indent(pad);
                var innerW = ImGui.GetContentRegionAvail().X - pad;
                if (_state.MyHangout is { } mine)
                {
                    DrawHangoutDashboard(mine, innerW);
                }
                else
                {
                    DrawHangoutForm(innerW);
                }
                ImGui.Unindent(pad);
                ImGui.PopStyleVar();
            }
        }
        PopScrollbarStyle();
    }

    private void DrawHangoutForm(float w)
    {
        var t = ThemeService.Current;

        DrawWarningBox(w, Loc.T("hangout.tos_warning"));
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("hangout.section_what"), t);
        DrawFieldLabel(Loc.T("hangout.field_category"), t);
        ImGui.SetNextItemWidth(w);
        if (PendingPartyId is not null)
        {
            // The category is not a choice on a party publish; the server forces it anyway.
            using (ImRaii.Disabled())
            {
                var partyLabel = HangoutCategories.Label(HangoutCategory.AetherParty);
                ImGui.Combo("##hgCat", ref _hgCategoryZero, [partyLabel], 1);
            }
        }
        else
        {
            var labels = HangoutCategories.CreatableLabels();
            ImGui.Combo("##hgCat", ref _hgCategoryIdx, labels, labels.Length);
        }

        ImGui.Spacing();
        DrawWarningBox(w, Loc.T("hangout.nsfw_warning"), UiColors.DangerBoxFill, UiColors.DangerBoxBorder);
        ImGui.Spacing();
        DrawFieldLabel(Loc.T("hangout.field_description"), t);
        InputTextMultilineWithPaste("##hgDesc", ref _hgDescription, HangoutLimits.DescriptionRawMaxLength, new Vector2(w, Px(70f)));
        var descLen = EmojiText.EffectiveLength(_hgDescription);
        var descOver = descLen > HangoutLimits.DescriptionMaxLength;
        ImGui.TextColored(descOver ? UiColors.Danger : UiColors.Muted with { W = 0.75f },
            Loc.T("hangout.char_count", descLen, HangoutLimits.DescriptionMaxLength));

        DrawSectionHeading(Loc.T("hangout.section_where"), t);
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (SharedUiHelpers.Button(Loc.T("places.use_current_location"), new Vector2(w, Px(30f))))
        {
            var detected = VenueLocationDetector.Detect();
            if (detected.World.Length > 0)
            {
                _hgDataCenter = detected.DataCenter;
                _hgWorld = detected.World;
            }
        }
        ImGui.PopStyleVar();
        PopThemeButton();
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("places.venue_datacenter"), t);
        ImGui.SetNextItemWidth(w);
        if (ImGui.BeginCombo("##hgDc", _hgDataCenter.Length == 0 ? Loc.T("places.venue_dc_select") : _hgDataCenter))
        {
            foreach (var region in HangoutRegions)
            {
                foreach (var dc in GameWorldData.DataCenters(region))
                {
                    if (ImGui.Selectable(dc, dc == _hgDataCenter))
                    {
                        _hgDataCenter = dc;
                        _hgWorld = "";
                    }
                }
            }
            ImGui.EndCombo();
        }

        DrawFieldLabel(Loc.T("places.venue_world"), t);
        ImGui.SetNextItemWidth(w);
        using (ImRaii.Disabled(_hgDataCenter.Length == 0))
        {
            if (ImGui.BeginCombo("##hgWorld", _hgWorld.Length == 0 ? Loc.T("places.venue_world_select") : _hgWorld))
            {
                foreach (var world in GameWorldData.Worlds(_hgDataCenter))
                {
                    if (ImGui.Selectable(world, world == _hgWorld))
                    {
                        _hgWorld = world;
                    }
                }
                ImGui.EndCombo();
            }
        }

        DrawFieldLabel(Loc.T("hangout.field_location"), t);
        ImGui.SetNextItemWidth(w);
        ImGui.InputTextWithHint("##hgLoc", Loc.T("hangout.field_location_hint"), ref _hgLocation, HangoutLimits.LocationMaxLength);

        DrawSectionHeading(Loc.T("hangout.section_when"), t);
        DrawFieldLabel(Loc.T("hangout.field_start"), t);
        ImGui.SetNextItemWidth(w);
        var startLabels = StartOffsetsMinutes.Select(StartLabel).ToArray();
        ImGui.Combo("##hgStart", ref _hgStartIdx, startLabels, startLabels.Length);

        DrawFieldLabel(Loc.T("hangout.field_duration"), t);
        ImGui.SetNextItemWidth(w);
        var durLabels = DurationsMinutes.Select(DurationLabel).ToArray();
        ImGui.Combo("##hgDur", ref _hgDurationIdx, durLabels, durLabels.Length);

        DrawSectionHeading(Loc.T("hangout.section_who"), t);
        ImGui.Checkbox(Loc.T("hangout.field_limit"), ref _hgLimitEnabled);
        if (_hgLimitEnabled)
        {
            ImGui.SetNextItemWidth(w * 0.5f);
            if (ImGui.InputInt("##hgLimit", ref _hgLimit))
            {
                _hgLimit = Math.Clamp(_hgLimit, HangoutLimits.MinAttendees, HangoutLimits.MaxAttendees);
            }
            ImGui.Checkbox(Loc.T("hangout.field_unlist_full"), ref _hgUnlistWhenFull);
            ImGui.SameLine();
            HelpTooltip(Loc.T("hangout.field_unlist_full_tooltip"));
        }
        ImGui.Checkbox(Loc.T("hangout.field_public"), ref _hgPublic);
        ImGui.SameLine();
        HelpTooltip(Loc.T("hangout.field_public_tooltip"));

        ImGui.Spacing();
        if (_hgError is { } err)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);
            ImGui.TextColored(UiColors.Danger, err);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        using (ImRaii.Disabled(_hgSubmitting))
        {
            PushThemeButton(t);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (SharedUiHelpers.Button(_hgSubmitting ? Loc.T("hangout.creating") : Loc.T("hangout.create_button"),
                    new Vector2(w, Px(36f))))
            {
                SubmitHangout();
            }
            ImGui.PopStyleVar();
            PopThemeButton();
        }
        ImGui.Spacing();
        ImGui.Spacing();
    }

    private static string StartLabel(int minutes) => minutes switch
    {
        0 => Loc.T("hangout.start_now"),
        < 60 => Loc.T("hangout.start_in_minutes", minutes),
        _ => Loc.T("hangout.start_in_hours", minutes / 60),
    };

    private static string DurationLabel(int minutes) => minutes < 60
        ? Loc.T("hangout.duration_minutes", minutes)
        : Loc.T("hangout.duration_hours", minutes / 60.0 == Math.Floor(minutes / 60.0) ? (minutes / 60).ToString() : (minutes / 60.0).ToString("0.#"));

    private void SubmitHangout()
    {
        var missing = new List<string>();
        if (_hgDescription.Trim().Length == 0)
        {
            missing.Add(Loc.T("hangout.field_description"));
        }
        if (_hgDataCenter.Length == 0)
        {
            missing.Add(Loc.T("places.venue_datacenter"));
        }
        if (_hgWorld.Length == 0)
        {
            missing.Add(Loc.T("places.venue_world"));
        }
        if (_hgLocation.Trim().Length == 0)
        {
            missing.Add(Loc.T("hangout.field_location"));
        }
        if (missing.Count > 0)
        {
            _hgError = Loc.T("hangout.err_missing_fields", string.Join(", ", missing));
            return;
        }
        if (EmojiText.EffectiveLength(_hgDescription) > HangoutLimits.DescriptionMaxLength)
        {
            _hgError = Loc.T("hangout.err_desc_too_long", HangoutLimits.DescriptionMaxLength);
            return;
        }

        _hgSubmitting = true;
        _hgError = null;
        // A party publish keeps the whole ordinary form (world, duration, cap, visibility) and only swaps
        // the call: the category is the server's to force, and every other rule is unchanged.
        var party = PendingPartyId;
        PendingPartyId = null;
        var req = new CreateHangoutRequest(
            Category: HangoutCategories.CreatableValues[Math.Clamp(_hgCategoryIdx, 0, HangoutCategories.CreatableValues.Length - 1)],
            Description: _hgDescription.Trim(),
            DataCenter: _hgDataCenter,
            World: _hgWorld,
            Location: _hgLocation.Trim(),
            StartUtc: DateTimeOffset.UtcNow.AddMinutes(StartOffsetsMinutes[Math.Clamp(_hgStartIdx, 0, StartOffsetsMinutes.Length - 1)]),
            DurationMinutes: DurationsMinutes[Math.Clamp(_hgDurationIdx, 0, DurationsMinutes.Length - 1)],
            MaxAttendees: _hgLimitEnabled ? (short)_hgLimit : null,
            UnlistWhenFull: _hgLimitEnabled && _hgUnlistWhenFull,
            IsPublic: _hgPublic);
        _ = Task.Run(async () =>
        {
            try
            {
                var created = party is { } partyId
                    ? await _host.PublishTogetherPartyHangoutAsync(partyId, req).ConfigureAwait(false)
                    : await _host.CreateHangoutAsync(req).ConfigureAwait(false);
                _state.SetMyHangout(created);
                _hgDescription = "";
                _hgLocation = "";
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[MyHangoutView] CreateHangoutAsync failed.");
                _hgError = HubErrorText.Localize(ex);
            }
            finally
            {
                _hgSubmitting = false;
            }
        });
    }

    private void DrawHangoutDashboard(HangoutSummaryDto mine, float w)
    {
        var t = ThemeService.Current;
        var live = HangoutFields.IsLiveNow(mine);
        var dl = ImGui.GetWindowDrawList();

        var cardH = Px(74f);
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(w, cardH);
        var accent = live ? UiColors.LiveGreen : t.Accent;
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = 0.10f }), Px(10f));
        dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = 0.55f }), Px(10f), ImDrawFlags.None, Px(1.5f));
        IconDraw.AddCentered(dl, HangoutCategories.Icon(mine.Category), Px(22f),
            new Vector2(tl.X + Px(28f), tl.Y + cardH * 0.5f), ImGui.GetColorU32(accent));
        var textX = tl.X + Px(52f);
        dl.AddText(new Vector2(textX, tl.Y + Px(12f)), ImGui.GetColorU32(accent),
            live ? Loc.T("hangout.status_live") : Loc.T("hangout.status_upcoming"));
        dl.AddText(new Vector2(textX, tl.Y + Px(32f)), 0xFFFFFFFFu, HangoutCategories.Label(mine.Category));
        dl.AddText(new Vector2(textX, tl.Y + Px(50f)), UiColors.TextMuted, HangoutFields.TimeLabel(mine));
        ImGui.Dummy(new Vector2(w, cardH));
        ImGui.Spacing();

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);
        ImGui.TextUnformatted(mine.Description);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        DrawSectionHeading(Loc.T("hangout.section_address"), t);
        ImGui.TextColored(UiColors.Body, HangoutFields.FormatAddress(mine));
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        var copied = ImGui.GetTime() < _hgCopiedUntil;
        if (SharedUiHelpers.Button(copied ? Loc.T("hangout.copied") : Loc.T("hangout.copy_address"), new Vector2(w, Px(30f))))
        {
            ImGui.SetClipboardText(HangoutFields.FormatAddress(mine));
            _hgCopiedUntil = ImGui.GetTime() + 1.5;
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        if (ShareHandler is { } share)
        {
            PushThemeButton(t);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (SharedUiHelpers.Button(Loc.T("hangout.share"), new Vector2(w, Px(30f))))
            {
                share(mine);
            }
            ImGui.PopStyleVar();
            PopThemeButton();
        }

        DrawSectionHeading(Loc.T("hangout.section_coming", HangoutFields.CountLabel(mine)), t);
        if (HangoutFields.IsAtCapacity(mine))
        {
            ImGui.TextColored(UiColors.WarningAccent, Loc.T("hangout.at_capacity"));
        }
        var rsvpers = _state.MyRsvpers;
        if (rsvpers.Count == 0)
        {
            ImGui.TextColored(UiColors.Muted, Loc.T("hangout.nobody_yet"));
        }
        foreach (var (_, name) in rsvpers)
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextUnformatted(name);
        }
        ImGui.Spacing();

        ImGui.TextColored(UiColors.Hint, mine.IsPublic
            ? Loc.T("hangout.listed_public")
            : Loc.T("hangout.listed_private"));
        if (mine.UnlistWhenFull && mine.MaxAttendees is not null)
        {
            ImGui.TextColored(UiColors.Hint, Loc.T("hangout.unlists_when_full"));
        }
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Button, UiColors.Danger with { W = _hgEndArmed ? 0.85f : 0.20f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.Danger with { W = 0.60f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.Danger with { W = 0.85f });
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (SharedUiHelpers.Button(_hgEndArmed ? Loc.T("hangout.end_confirm") : Loc.T("hangout.end_button"), new Vector2(w, Px(34f))))
        {
            if (!_hgEndArmed)
            {
                _hgEndArmed = true;
            }
            else
            {
                _hgEndArmed = false;
                EndHangout();
            }
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void EndHangout()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.EndMyHangoutAsync().ConfigureAwait(false);
                _state.SetMyHangout(null);
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[MyHangoutView] EndMyHangoutAsync failed.");
                _hgError = HubErrorText.Localize(ex);
            }
        });
    }

    private static void DrawWarningBox(float w, string text, uint fill = UiColors.WarningBoxFill, uint border = UiColors.WarningBoxBorder)
    {
        var pad = Px(10f);
        var wrapW = w - pad * 2f;
        var size = ImGui.CalcTextSize(text, false, wrapW);
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(w, size.Y + pad * 2f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tl, br, fill, Px(8f));
        dl.AddRect(tl, br, border, Px(8f));
        ImGui.SetCursorScreenPos(tl + new Vector2(pad, pad));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y));
    }
}
