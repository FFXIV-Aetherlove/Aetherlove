using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Emoji;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Places;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class MyVenuesScreen
{
    private sealed class RuleEdit
    {
        public Guid Id = Guid.Empty;
        public bool OneTime;
        public readonly bool[] Days = new bool[7];
        public string DateText = "";
        public int StartHour = 21;
        public int EndHour = 23;
        public bool PickingEnd;
    }

    private Guid _editId = Guid.Empty;
    private string _editName = "";
    private string _editDescription = "";
    private readonly bool[] _editTags = new bool[VenueFields.VenueTagValues.Length];
    private int _editRegionIdx;
    private string _editDataCenter = "";
    private string _editWorld = "";
    private int _editDistrictIdx;
    private int _editWard = 1;
    private int _editPlot = 1;
    private int _editRoom;
    private int _editTimezoneIdx;
    private readonly List<RuleEdit> _editRules = [];
    private string? _editValidationError;
    private bool _confirmDeleteVenue;
    private float _confirmPanelHeight;
    private string _tzFilter = "";

    private readonly EmojiPickerPopup _descEmojiPicker = new();

    private sealed class ExtraBannerSlot
    {
        public ISharedImmediateTexture? ServerTex;
        public bool HasServer;
        public string StagedPath = "";
        public ISharedImmediateTexture? StagedTex;
        public Vector4 StagedCrop;
        public bool StagedConfirmed;
        public bool PendingRemove;

        public void Clear()
        {
            ServerTex = null;
            HasServer = false;
            StagedPath = "";
            StagedTex = null;
            StagedConfirmed = false;
            PendingRemove = false;
        }
    }

    /// <summary>Supporter carousel banner slots 2..N (index = slot - 2).</summary>
    private readonly ExtraBannerSlot[] _extraBanners =
        [.. Enumerable.Range(0, SupporterLimits.SupporterVenueBanners - 1).Select(_ => new ExtraBannerSlot())];

    private string _bannerPickPath = "";
    private ISharedImmediateTexture? _bannerPickTex;
    private Vector4 _bannerPickCrop;
    private bool _bannerConfirmed;
    private bool _bannerPendingRemove;
    private string _logoPickPath = "";
    private ISharedImmediateTexture? _logoPickTex;
    private Vector4 _logoPickCrop;
    private bool _logoConfirmed;
    private bool _logoPendingRemove;
    private bool _pickingBanner;

    private void OpenEditor(MyVenueDto? venue)
    {
        _editValidationError = null;
        _actionError = null;
        _confirmDeleteVenue = false;
        _editRules.Clear();
        ClearImagePicks();
        _tzFilter = "";

        if (venue is null)
        {
            _editId = Guid.Empty;
            _editName = "";
            _editDescription = "";
            Array.Clear(_editTags);
            _editRegionIdx = 0;
            _editDataCenter = "";
            _editWorld = "";
            _editDistrictIdx = 0;
            _editWard = 1;
            _editPlot = 1;
            _editRoom = 0;
            _editTimezoneIdx = Math.Max(0, Array.FindIndex(AllTimezones, tz => tz.Id == TimeZoneInfo.Local.Id));
        }
        else
        {
            _editId = venue.Id;
            _editName = venue.Name;
            _editDescription = venue.Description;
            MaskToBools(VenueFields.VenueTagValues, venue.Tags, (v, m) => (m & v) != 0, _editTags);
            _editRegionIdx = Math.Max(0, IndexOf(RegionValues, venue.Region, 0));
            _editDataCenter = venue.DataCenter;
            _editWorld = venue.World;
            _editDistrictIdx = Math.Max(0, Array.IndexOf(VenueFields.DistrictValues, venue.District));
            _editWard = venue.Ward;
            _editPlot = venue.Plot;
            _editRoom = venue.Room;
            _editTimezoneIdx = Math.Max(0, Array.FindIndex(AllTimezones, tz => tz.Id == venue.Timezone));
            foreach (var b in venue.Banners ?? [])
            {
                var idx = b.Slot - 2;
                if (idx < 0 || idx >= _extraBanners.Length || b.Webp is not { Length: > 0 })
                {
                    continue;
                }
                var slot = _extraBanners[idx];
                slot.HasServer = true;
                slot.ServerTex = AvatarDiskCache.Store(
                    Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "VenueBannerCache"),
                    $"venue_{venue.Id:N}_b{b.Slot}", b.Webp);
            }
            foreach (var time in venue.OpeningTimes)
            {
                var rule = new RuleEdit
                {
                    Id = time.Id,
                    OneTime = time.DaysMask == 0,
                    StartHour = time.StartMinute / 60,
                    EndHour = ((time.StartMinute + time.DurationMinutes - 1) / 60) % 24,
                };
                for (var d = 0; d < 7; d++)
                {
                    rule.Days[d] = (time.DaysMask & (1 << d)) != 0;
                }
                if (rule.OneTime && time.OneTimeDateDayNumber > 0)
                {
                    rule.DateText = DateOnly.FromDayNumber(time.OneTimeDateDayNumber).ToString("yyyy-MM-dd");
                }
                _editRules.Add(rule);
            }
        }
        _section = Section.Editor;
    }

    private void ClearImagePicks()
    {
        _bannerPickPath = "";
        _bannerPickTex = null;
        _bannerConfirmed = false;
        _bannerPendingRemove = false;
        _logoPickPath = "";
        _logoPickTex = null;
        _logoConfirmed = false;
        _logoPendingRemove = false;
        foreach (var slot in _extraBanners)
        {
            slot.Clear();
        }
    }

    private void ApplyDetectedLocation(DetectedVenueLocation detected, bool overwriteNames)
    {
        if (overwriteNames || detected.World.Length > 0)
        {
            if (detected.World.Length > 0)
            {
                _editWorld = detected.World;
                _editDataCenter = detected.DataCenter;
            }
        }
        if (detected.Region != 0)
        {
            _editRegionIdx = Math.Max(0, IndexOf(RegionValues, detected.Region, _editRegionIdx));
        }
        if (detected.District != HousingDistrict.Unknown)
        {
            _editDistrictIdx = Math.Max(0, Array.IndexOf(VenueFields.DistrictValues, detected.District));
            if (detected.Ward > 0)
            {
                _editWard = detected.Ward;
            }
            if (detected.Plot > 0)
            {
                _editPlot = detected.Plot;
            }
            _editRoom = detected.Room;
        }
    }

    private void DrawEditor()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetContentRegionAvail().X;
        var pad = Px(PadX);
        var fieldW = winW - pad * 2f;

        ImGui.Spacing();
        ImGui.SetCursorPosX(pad);
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("places.back_to_venues"), FontAwesomeIcon.Store))
        {
            _section = Section.List;
            return;
        }
        ImGui.Spacing();
        DrawSubpageHeading(
            _editId == Guid.Empty ? Loc.T("places.new_venue_title") : Loc.T("places.edit_venue_title"), PadX);

        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##venueEditorScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, pad);
                ImGui.Indent(pad);
                var innerW = ImGui.GetContentRegionAvail().X - pad;
                DrawEditorForm(t, innerW);
                ImGui.Unindent(pad);
                ImGui.PopStyleVar();
            }
        }
        PopScrollbarStyle();

        DrawDeleteVenueConfirm();
    }

    private void DrawEditorForm(ThemeDefinition t, float w)
    {
        DrawSectionHeading(Loc.T("places.section_details"), t);

        DrawFieldLabel(Loc.T("places.venue_name"), t);
        ImGui.SetNextItemWidth(w);
        ImGui.InputText("##venName", ref _editName, PlacesLimits.VenueNameMaxLength);

        ImGui.Spacing();
        DrawFieldLabel(Loc.T("places.venue_description"), t);
        ImGui.SameLine();
        {
            var iconH = ImGui.GetTextLineHeight();
            var grinTex = Plugin.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(2f, 2f));
            var clicked = grinTex != null
                ? ImGui.ImageButton(grinTex.Handle, new Vector2(iconH - Px(4f)))
                : ImGui.SmallButton(":)##venDescEmoji");
            ImGui.PopStyleVar();
            _descEmojiPicker.Draw();
            if (clicked)
            {
                _descEmojiPicker.Open(InsertDescriptionEmoji);
            }
        }
        var descBefore = _editDescription;
        InputTextMultilineWithPaste("##venDesc", ref _editDescription, PlacesLimits.VenueDescriptionRawMaxLength, new Vector2(w, Px(84f)));
        if (EmojiText.EffectiveLength(_editDescription) > PlacesLimits.VenueDescriptionMaxLength)
        {
            _editDescription = descBefore;
        }
        ImGui.TextColored(UiColors.Muted with { W = 0.75f },
            Loc.T("profile.char_count", EmojiText.EffectiveLength(_editDescription)));

        if (_editDescription.Length > 0)
        {
            ImGui.Spacing();
            var parsed = ParsedMessage.Parse(_editDescription);
            parsed.DrawWrapped("##venDescPreview", w);
        }

        DrawSectionHeading(Loc.T("places.section_tags"), t);
        VenueFields.DrawPillToggleRow("vtag", VenueFields.VenueTagLabels, _editTags, w,
            dangerAt: i => VenueFields.VenueTagValues[i] == VenueTag.Nsfw);

        DrawSectionHeading(Loc.T("places.section_location"), t);
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(Loc.T("places.use_current_location"), new Vector2(w, Px(30f))))
        {
            ApplyDetectedLocation(VenueLocationDetector.Detect(), overwriteNames: true);
        }
        ImGui.PopStyleVar();
        PopThemeButton();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);
        ImGui.TextColored(UiColors.Hint, Loc.T("places.use_current_location_hint"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        DrawFieldLabel(Loc.T("places.venue_region"), t);
        ImGui.SetNextItemWidth(w);
        var regions = Regions;
        ImGui.Combo("##venRegion", ref _editRegionIdx, regions, regions.Length);
        var region = ValueAt(RegionValues, _editRegionIdx, Region.NorthAmerica);

        DrawFieldLabel(Loc.T("places.venue_datacenter"), t);
        var dcs = GameWorldData.DataCenters(region);
        if (_editDataCenter.Length > 0 && !dcs.Contains(_editDataCenter))
        {
            _editDataCenter = "";
            _editWorld = "";
        }
        ImGui.SetNextItemWidth(w);
        if (ImGui.BeginCombo("##venDc", _editDataCenter.Length == 0 ? Loc.T("places.venue_dc_select") : _editDataCenter))
        {
            foreach (var dc in dcs)
            {
                if (ImGui.Selectable(dc, dc == _editDataCenter))
                {
                    _editDataCenter = dc;
                    _editWorld = "";
                }
            }
            ImGui.EndCombo();
        }

        DrawFieldLabel(Loc.T("places.venue_world"), t);
        var worlds = GameWorldData.Worlds(_editDataCenter);
        ImGui.SetNextItemWidth(w);
        using (ImRaii.Disabled(_editDataCenter.Length == 0))
        {
            if (ImGui.BeginCombo("##venWorld", _editWorld.Length == 0 ? Loc.T("places.venue_world_select") : _editWorld))
            {
                foreach (var world in worlds)
                {
                    if (ImGui.Selectable(world, world == _editWorld))
                    {
                        _editWorld = world;
                    }
                }
                ImGui.EndCombo();
            }
        }

        DrawFieldLabel(Loc.T("places.venue_district"), t);
        ImGui.SetNextItemWidth(w);
        ImGui.Combo("##venDistrict", ref _editDistrictIdx, VenueFields.DistrictLabels, VenueFields.DistrictLabels.Length);

        var third = (w - Px(16f)) / 3f;
        ImGui.Spacing();
        DrawNumberField(Loc.T("places.venue_ward"), "##venWard", ref _editWard, 1, PlacesLimits.MaxWard, third);
        ImGui.SameLine(0f, Px(8f));
        DrawNumberField(Loc.T("places.venue_plot"), "##venPlot", ref _editPlot, 0, PlacesLimits.MaxPlot, third);
        ImGui.SameLine(0f, Px(8f));
        DrawNumberField(Loc.T("places.venue_room"), "##venRoom", ref _editRoom, 0, PlacesLimits.MaxRoom, third);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);
        ImGui.TextColored(UiColors.Hint, Loc.T("places.venue_room_hint"));
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        DrawFieldLabel(Loc.T("places.venue_timezone"), t);
        ImGui.SameLine();
        HelpTooltip(Loc.T("places.venue_timezone_tip"));
        ImGui.SetNextItemWidth(w);
        var tzPreview = _editTimezoneIdx >= 0 && _editTimezoneIdx < TimezoneNames.Length
            ? TimezoneNames[_editTimezoneIdx]
            : Loc.T("places.venue_tz_select");
        if (ImGui.BeginCombo("##venTz", tzPreview))
        {
            if (ImGui.IsWindowAppearing())
            {
                _tzFilter = "";
                ImGui.SetKeyboardFocusHere();
            }
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##venTzFilter", Loc.T("places.venue_tz_search"), ref _tzFilter, 64);
            var filter = _tzFilter.Trim();
            for (var i = 0; i < TimezoneNames.Length; i++)
            {
                if (filter.Length > 0 && TimezoneNames[i].IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (ImGui.Selectable($"{TimezoneNames[i]}##tz{i}", i == _editTimezoneIdx))
                {
                    _editTimezoneIdx = i;
                }
            }
            ImGui.EndCombo();
        }

        DrawSectionHeading(Loc.T("places.section_hours"), t);
        ImGui.PushTextWrapPos(w);
        ImGui.TextColored(UiColors.Subtle, Loc.T("places.hours_explainer"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        for (var i = 0; i < _editRules.Count; i++)
        {
            DrawRuleCard(t, _editRules[i], i, w);
            ImGui.Spacing();
        }
        if (_editRules.Count < PlacesLimits.MaxOpeningTimesPerVenue)
        {
            PushThemeButton(t);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("places.add_opening_time"), new Vector2(w, Px(30f))))
            {
                var rule = new RuleEdit();
                rule.Days[4] = true;
                _editRules.Add(rule);
            }
            ImGui.PopStyleVar();
            PopThemeButton();
        }

        DrawSectionHeading(Loc.T("places.section_images"), t);
        DrawImageSlot(t, w, banner: true);
        ImGui.Spacing();
        DrawImageSlot(t, w, banner: false);
        DrawExtraBannerSlots(t, w);

        ImGui.Spacing();
        ImGui.Spacing();
        if (_editValidationError is not null || _actionError is not null)
        {
            ImGui.PushTextWrapPos(w);
            ImGui.TextColored(UiColors.Danger, _editValidationError ?? _actionError!);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (_saving)
        {
            ImGui.BeginDisabled();
        }
        if (ImGui.Button(Loc.T("places.save_venue"), new Vector2(w, Px(36f))))
        {
            SaveVenue();
        }
        if (_saving)
        {
            ImGui.EndDisabled();
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        if (_editId != Guid.Empty)
        {
            ImGui.Spacing();
            PushDangerButton();
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
            if (ImGui.Button(Loc.T("places.delete_venue"), new Vector2(w, Px(30f))))
            {
                _confirmDeleteVenue = true;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }
        ImGui.Dummy(new Vector2(1f, Px(14f)));
    }

    private void InsertDescriptionEmoji(string name)
    {
        var add = $":{name}: ";
        if (EmojiText.EffectiveLength(_editDescription + add) <= PlacesLimits.VenueDescriptionMaxLength)
        {
            _editDescription += add;
        }
    }

    private void DrawRuleCard(ThemeDefinition t, RuleEdit rule, int index, float w)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(6f));

        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(1f, Px(8f)));
        ImGui.Indent(Px(10f));
        var innerW = w - Px(20f);

        if (ImGui.RadioButton($"{Loc.T("places.rule_weekly")}##ruleW{index}", !rule.OneTime))
        {
            rule.OneTime = false;
        }
        ImGui.SameLine(0f, Px(12f));
        if (ImGui.RadioButton($"{Loc.T("places.rule_one_time")}##ruleO{index}", rule.OneTime))
        {
            rule.OneTime = true;
        }
        ImGui.SameLine(innerW - Px(18f));
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var removeClicked = ImGui.SmallButton($"{FontAwesomeIcon.Trash.ToIconString()}##ruleDel{index}");
        ImGui.PopFont();

        ImGui.Spacing();
        if (rule.OneTime)
        {
            ImGui.SetNextItemWidth(Px(120f));
            ImGui.InputTextWithHint($"##ruleDate{index}", "2026-12-31", ref rule.DateText, 10);
            ImGui.SameLine();
            ImGui.TextColored(UiColors.Hint, Loc.T("places.rule_date_hint"));
        }
        else
        {
            var abbr = VenueFields.DayAbbreviations;
            for (var d = 0; d < 7; d++)
            {
                if (d > 0)
                {
                    ImGui.SameLine(0f, Px(5f));
                }
                var selected = rule.Days[d];
                if (selected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, t.Accent with { W = 0.55f });
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.Accent with { W = 0.70f });
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.Accent with { W = 0.85f });
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.06f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.12f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.18f));
                }
                if (ImGui.Button($"{abbr[d]}##ruleDay{index}_{d}", new Vector2((innerW - Px(30f)) / 7f, Px(24f))))
                {
                    rule.Days[d] = !rule.Days[d];
                }
                ImGui.PopStyleColor(3);
            }
        }

        ImGui.Spacing();
        VenueFields.DrawHourRangeEditor(innerW, ref rule.StartHour, ref rule.EndHour,
            ref rule.PickingEnd, $"rule{index}");
        ImGui.TextColored(UiColors.Subtle, Loc.T("places.rule_span",
            VenueFields.SpanLabel(rule.StartHour * 60, VenueFields.SpanDurationMinutes(rule.StartHour, rule.EndHour))));

        ImGui.Dummy(new Vector2(1f, Px(8f)));
        ImGui.Unindent(Px(10f));
        ImGui.EndGroup();
        ImGui.PopStyleVar();

        var br = new Vector2(origin.X + w, ImGui.GetItemRectMax().Y);
        dl.AddRect(origin, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), Px(10f), ImDrawFlags.None, Px(1f));

        if (removeClicked)
        {
            _editRules.RemoveAt(index);
        }
    }

    /// <summary>A compact integer input with its label stacked above it, so the label never overflows the
    /// narrow column (ImGui draws an InputInt's own label to the right, which clipped here).</summary>
    private static void DrawNumberField(string label, string id, ref int value, int min, int max, float width)
    {
        ImGui.BeginGroup();
        ImGui.TextColored(UiColors.Subtle, label);
        ImGui.SetNextItemWidth(width);
        ImGui.InputInt(id, ref value, 0);
        value = Math.Clamp(value, min, max);
        ImGui.EndGroup();
    }

    private void DrawImageSlot(ThemeDefinition t, float w, bool banner)
    {
        var dl = ImGui.GetWindowDrawList();
        var label = banner ? Loc.T("places.image_banner") : Loc.T("places.image_logo");
        var slotW = banner ? Px(150f) : Px(56f);
        var slotH = banner ? Px(54f) : Px(56f);

        DrawFieldLabel(label, t);
        var origin = ImGui.GetCursorScreenPos();
        var pickTex = banner ? _bannerPickTex : _logoPickTex;
        var pendingRemove = banner ? _bannerPendingRemove : _logoPendingRemove;
        var serverTex = !pendingRemove && _editId != Guid.Empty
            ? (banner ? _bannerTex : _logoTex).GetValueOrDefault(_editId)
            : null;
        var wrap = (pickTex ?? serverTex)?.GetWrapOrDefault();
        if (wrap != null)
        {
            var (uv0, uv1) = SharedUiHelpers.CoverFitUvs(wrap.Width, wrap.Height, slotW, slotH);
            dl.AddImageRounded(wrap.Handle, origin, origin + new Vector2(slotW, slotH),
                uv0, uv1, 0xFFFFFFFFu, Px(10f), ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddRectFilled(origin, origin + new Vector2(slotW, slotH), UiColors.PhotoSlotFill, Px(10f));
            dl.AddRect(origin, origin + new Vector2(slotW, slotH),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.14f)), Px(10f), ImDrawFlags.None, Px(1f));
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X + slotW + Px(12f), origin.Y));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (w - slotW - Px(12f)));
        ImGui.TextColored(UiColors.Hint, banner
            ? Loc.T("places.image_banner_hint", PhotoSpec.VenueBannerWidth, PhotoSpec.VenueBannerHeight)
            : Loc.T("places.image_logo_hint", PhotoSpec.AvatarSize));
        ImGui.PopTextWrapPos();
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(6f));
        if (ImGui.Button($"{Loc.T("places.image_pick")}##pick{(banner ? "B" : "L")}", new Vector2(Px(120f), Px(26f))))
        {
            _pickingBanner = banner;
            OpenImagePicker();
        }
        ImGui.PopStyleVar();
        PopThemeButton();
        var confirmed = banner ? _bannerConfirmed : _logoConfirmed;
        if (confirmed)
        {
            ImGui.TextColored(UiColors.SuccessSoft, Loc.T("places.image_ready"));
        }
        var hasServer = _editId != Guid.Empty && (banner ? _bannerTex : _logoTex).GetValueOrDefault(_editId) is not null;
        if ((hasServer && !pendingRemove) || confirmed)
        {
            if (ImGui.SmallButton($"{Loc.T("places.image_remove")}##rm{(banner ? "B" : "L")}"))
            {
                if (banner)
                {
                    _bannerPickPath = "";
                    _bannerPickTex = null;
                    _bannerConfirmed = false;
                    _bannerPendingRemove = hasServer;
                }
                else
                {
                    _logoPickPath = "";
                    _logoPickTex = null;
                    _logoConfirmed = false;
                    _logoPendingRemove = hasServer;
                }
            }
        }
        ImGui.EndGroup();

        var groupBottom = ImGui.GetItemRectMax().Y;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(origin.Y + slotH, groupBottom) + Px(6f)));
    }

    /// <summary>Supporter carousel banner slots 2..N. Non-supporters only ever see slots that still hold a
    /// server image after a lapse, and those are remove-only.</summary>
    private void DrawExtraBannerSlots(ThemeDefinition t, float w)
    {
        var isSupporter = _bootstrap.LastConnection is { IsSupporter: true };
        var dl = ImGui.GetWindowDrawList();
        var slotW = Px(150f);
        var slotH = Px(54f);

        for (var i = 0; i < _extraBanners.Length; i++)
        {
            var slot = _extraBanners[i];
            if (!isSupporter && !slot.HasServer && !slot.StagedConfirmed)
            {
                continue;
            }

            ImGui.Spacing();
            DrawFieldLabel(Loc.T("places.image_extra_banner", i + 2), t);
            var origin = ImGui.GetCursorScreenPos();
            var tex = (slot.StagedTex ?? (slot.PendingRemove ? null : slot.ServerTex))?.GetWrapOrDefault();
            if (tex != null)
            {
                var (uv0, uv1) = SharedUiHelpers.CoverFitUvs(tex.Width, tex.Height, slotW, slotH);
                dl.AddImageRounded(tex.Handle, origin, origin + new Vector2(slotW, slotH),
                    uv0, uv1, 0xFFFFFFFFu, Px(10f), ImDrawFlags.RoundCornersAll);
            }
            else
            {
                dl.AddRectFilled(origin, origin + new Vector2(slotW, slotH), UiColors.PhotoSlotFill, Px(10f));
                dl.AddRect(origin, origin + new Vector2(slotW, slotH),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.14f)), Px(10f), ImDrawFlags.None, Px(1f));
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X + slotW + Px(12f), origin.Y));
            ImGui.BeginGroup();
            if (slot.PendingRemove)
            {
                ImGui.TextColored(new Vector4(0.90f, 0.60f, 0.20f, 1f), Loc.T("profile.photo_will_be_removed"));
                if (ImGui.SmallButton($"{Loc.T("profile.undo")}##xbUndo{i}"))
                {
                    slot.PendingRemove = false;
                }
            }
            else
            {
                if (isSupporter)
                {
                    PushThemeButton(t);
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(6f));
                    if (ImGui.Button($"{Loc.T("places.image_pick")}##xbPick{i}", new Vector2(Px(120f), Px(26f))))
                    {
                        OpenExtraBannerPicker(slot);
                    }
                    ImGui.PopStyleVar();
                    PopThemeButton();
                }
                else
                {
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (w - slotW - Px(12f)));
                    ImGui.TextColored(UiColors.Hint, Loc.T("profile.slot_locked"));
                    ImGui.PopTextWrapPos();
                }
                if (slot.StagedConfirmed)
                {
                    ImGui.TextColored(UiColors.SuccessSoft, Loc.T("places.image_ready"));
                }
                if (slot.HasServer || slot.StagedConfirmed)
                {
                    if (ImGui.SmallButton($"{Loc.T("places.image_remove")}##xbRm{i}"))
                    {
                        slot.StagedPath = "";
                        slot.StagedTex = null;
                        slot.StagedConfirmed = false;
                        slot.PendingRemove = slot.HasServer;
                    }
                }
            }
            ImGui.EndGroup();

            var groupBottom = ImGui.GetItemRectMax().Y;
            ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(origin.Y + slotH, groupBottom) + Px(6f)));
        }
    }

    private void OpenExtraBannerPicker(ExtraBannerSlot slot)
    {
        _fileDialog.OpenFileDialog(
            title: Loc.T("profile.select_image"),
            filters: Loc.T("profile.image_files_filter") + "{.png,.jpg,.jpeg,.bmp,.webp}",
            callback: (ok, path) =>
            {
                if (!ok || _pendingPick.RejectUnavailableCloudFile(path))
                {
                    return;
                }
                var handle = LoadPickedPreview(path);
                if (handle is null)
                {
                    return;
                }
                _pendingPick.Begin(handle, PhotoSpec.VenueBannerWidth, PhotoSpec.VenueBannerHeight,
                    onValid: () => _cropPopup.Open(
                        Loc.T("places.crop_banner"),
                        handle,
                        PhotoSpec.VenueBannerHeight / (float)PhotoSpec.VenueBannerWidth,
                        cropRect =>
                        {
                            slot.StagedPath = path;
                            slot.StagedTex = handle;
                            slot.StagedCrop = cropRect;
                            slot.StagedConfirmed = true;
                            slot.PendingRemove = false;
                        }),
                    onReject: () => { });
            });
    }

    private void OpenImagePicker()
    {
        _fileDialog.OpenFileDialog(
            title: Loc.T("profile.select_image"),
            filters: Loc.T("profile.image_files_filter") + "{.png,.jpg,.jpeg,.bmp,.webp}",
            callback: (ok, path) =>
            {
                if (!ok || _pendingPick.RejectUnavailableCloudFile(path))
                {
                    return;
                }
                HandleImagePicked(path);
            });
    }

    private void HandleImagePicked(string path)
    {
        var banner = _pickingBanner;
        var handle = LoadPickedPreview(path);
        if (handle is null)
        {
            return;
        }
        var minW = banner ? PhotoSpec.VenueBannerWidth : PhotoSpec.AvatarSize;
        var minH = banner ? PhotoSpec.VenueBannerHeight : PhotoSpec.AvatarSize;
        _pendingPick.Begin(handle, minW, minH,
            onValid: () => _cropPopup.Open(
                banner ? Loc.T("places.crop_banner") : Loc.T("places.crop_logo"),
                handle,
                banner ? PhotoSpec.VenueBannerHeight / (float)PhotoSpec.VenueBannerWidth : 1.0f,
                cropRect =>
                {
                    if (banner)
                    {
                        _bannerPickPath = path;
                        _bannerPickTex = handle;
                        _bannerPickCrop = cropRect;
                        _bannerConfirmed = true;
                        _bannerPendingRemove = false;
                    }
                    else
                    {
                        _logoPickPath = path;
                        _logoPickTex = handle;
                        _logoPickCrop = cropRect;
                        _logoConfirmed = true;
                        _logoPendingRemove = false;
                    }
                }),
            onReject: () => { });
    }

    private VenueEditDto? BuildEditDto()
    {
        var name = _editName.Trim();
        if (name.Length < PlacesLimits.VenueNameMinLength)
        {
            _editValidationError = Loc.T("places.err_name");
            return null;
        }
        if (_editDescription.Trim().Length == 0)
        {
            _editValidationError = Loc.T("places.err_description");
            return null;
        }
        if (!_editTags.Any(x => x))
        {
            _editValidationError = Loc.T("places.err_tags");
            return null;
        }
        var hasServerBanner = _editId != Guid.Empty && _bannerTex.GetValueOrDefault(_editId) is not null;
        var hasServerLogo = _editId != Guid.Empty && _logoTex.GetValueOrDefault(_editId) is not null;
        var bannerOk = _bannerConfirmed || (hasServerBanner && !_bannerPendingRemove);
        var logoOk = _logoConfirmed || (hasServerLogo && !_logoPendingRemove);
        if (!bannerOk || !logoOk)
        {
            _editValidationError = Loc.T("places.err_images");
            return null;
        }
        if (_editDataCenter.Trim().Length == 0 || _editWorld.Trim().Length == 0)
        {
            _editValidationError = Loc.T("places.err_location");
            return null;
        }
        if (_editPlot < 1 && _editRoom < 1)
        {
            _editValidationError = Loc.T("places.err_plot_or_room");
            return null;
        }
        if (_editTimezoneIdx < 0 || _editTimezoneIdx >= AllTimezones.Length)
        {
            _editValidationError = Loc.T("places.err_timezone");
            return null;
        }
        if (_editRules.Count == 0)
        {
            _editValidationError = Loc.T("places.err_hours");
            return null;
        }

        var times = new List<VenueOpeningTimeDto>(_editRules.Count);
        foreach (var rule in _editRules)
        {
            var daysMask = 0;
            var dayNumber = 0;
            if (rule.OneTime)
            {
                if (!DateOnly.TryParseExact(rule.DateText.Trim(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    _editValidationError = Loc.T("places.err_date");
                    return null;
                }
                dayNumber = date.DayNumber;
            }
            else
            {
                for (var d = 0; d < 7; d++)
                {
                    if (rule.Days[d])
                    {
                        daysMask |= 1 << d;
                    }
                }
                if (daysMask == 0)
                {
                    _editValidationError = Loc.T("places.err_days");
                    return null;
                }
            }
            times.Add(new VenueOpeningTimeDto(
                rule.Id,
                daysMask,
                dayNumber,
                (short)(rule.StartHour * 60),
                VenueFields.SpanDurationMinutes(rule.StartHour, rule.EndHour)));
        }

        _editValidationError = null;
        return new VenueEditDto(
            Id: _editId == Guid.Empty ? null : _editId,
            Name: name,
            Description: _editDescription.Trim(),
            Tags: MaskOr(VenueFields.VenueTagValues, _editTags, (a, b) => a | b),
            Region: ValueAt(RegionValues, _editRegionIdx, Region.NorthAmerica),
            DataCenter: _editDataCenter.Trim(),
            World: _editWorld.Trim(),
            District: ValueAt(VenueFields.DistrictValues, _editDistrictIdx, HousingDistrict.Mist),
            Ward: (short)_editWard,
            Plot: (short)_editPlot,
            Room: (short)_editRoom,
            Timezone: _editTimezoneIdx >= 0 && _editTimezoneIdx < AllTimezones.Length
                ? AllTimezones[_editTimezoneIdx].Id
                : "UTC",
            OpeningTimes: times.ToArray());
    }

    private void SaveVenue()
    {
        var dto = BuildEditDto();
        if (dto is null)
        {
            return;
        }
        _saving = true;
        _actionError = null;

        var bannerPath = _bannerConfirmed ? _bannerPickPath : null;
        var bannerCrop = _bannerPickCrop;
        var logoPath = _logoConfirmed ? _logoPickPath : null;
        var logoCrop = _logoPickCrop;
        var removeBanner = _bannerPendingRemove;
        var removeLogo = _logoPendingRemove;
        var ct = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var saved = await _hubClient.SaveVenueAsync(dto, ct).ConfigureAwait(false);
                if (bannerPath is not null)
                {
                    var upload = ReadPhotoUpload(bannerPath, bannerCrop, isNsfw: false, PhotoKind.VenueBanner);
                    await _hubClient.SetVenueImageAsync(saved.Id, banner: true, upload, slot: 1, ct).ConfigureAwait(false);
                }
                else if (removeBanner)
                {
                    await _hubClient.RemoveVenueImageAsync(saved.Id, banner: true, slot: 1, ct).ConfigureAwait(false);
                }
                if (logoPath is not null)
                {
                    var upload = ReadPhotoUpload(logoPath, logoCrop, isNsfw: false, PhotoKind.Avatar);
                    await _hubClient.SetVenueImageAsync(saved.Id, banner: false, upload, slot: 1, ct).ConfigureAwait(false);
                }
                else if (removeLogo)
                {
                    await _hubClient.RemoveVenueImageAsync(saved.Id, banner: false, slot: 1, ct).ConfigureAwait(false);
                }

                // Supporter carousel slots (2..N) staged in the editor.
                for (short s = 2; s <= _extraBanners.Length + 1; s++)
                {
                    var extra = _extraBanners[s - 2];
                    if (extra.StagedPath.Length > 0 && extra.StagedConfirmed)
                    {
                        var upload = ReadPhotoUpload(extra.StagedPath, extra.StagedCrop, isNsfw: false, PhotoKind.VenueBanner);
                        await _hubClient.SetVenueImageAsync(saved.Id, banner: true, upload, slot: s, ct).ConfigureAwait(false);
                    }
                    else if (extra.PendingRemove)
                    {
                        await _hubClient.RemoveVenueImageAsync(saved.Id, banner: true, slot: s, ct).ConfigureAwait(false);
                    }
                }

                _savedTimer = 2.5f;
                _section = Section.List;
                _venues = null;
                StartListFetch();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[MyVenuesScreen] Venue save failed.");
                _actionError = HubErrorText.Localize(ex);
            }
            finally
            {
                _saving = false;
            }
        }, ct);
    }

    private void DrawDeleteVenueConfirm()
    {
        if (!_confirmDeleteVenue)
        {
            return;
        }
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(windowPos, windowPos + windowSize,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

        ImGui.SetCursorScreenPos(windowPos);
        if (ImGui.InvisibleButton("##delVenueScrim", windowSize))
        {
            _confirmDeleteVenue = false;
        }

        var w = Px(280f);
        var pad = Px(16f, 16f);
        var h = _confirmPanelHeight > 0f ? _confirmPanelHeight : Px(190f);
        var panelPos = windowPos + (windowSize - new Vector2(w, h)) * 0.5f;

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
        using (var child = ImRaii.Child("##delVenuePanel", new Vector2(w, h), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child.Success)
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                ModalUi.Header(innerW, FontAwesomeIcon.Trash,
                    Loc.T("places.delete_venue_title"), UiColors.Danger);
                ImGui.TextColored(UiColors.Body, Loc.T("places.delete_venue_body", _editName));
                ImGui.Spacing();
                ImGui.Spacing();
                var btnW = (innerW - Px(8f)) * 0.5f;
                PushThemeButton(ThemeService.Current);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                if (ImGui.Button(Loc.T("common.cancel"), new Vector2(btnW, Px(30f))))
                {
                    _confirmDeleteVenue = false;
                }
                ImGui.PopStyleVar();
                PopThemeButton();
                ImGui.SameLine(0f, Px(8f));
                PushDangerButton();
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                if (ImGui.Button(Loc.T("places.delete_venue"), new Vector2(btnW, Px(30f))))
                {
                    _confirmDeleteVenue = false;
                    DeleteVenue();
                }
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(3);
                ImGui.PopTextWrapPos();
                _confirmPanelHeight = ImGui.GetCursorPosY() + pad.Y;
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private void DeleteVenue()
    {
        var venueId = _editId;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubClient.DeleteVenueAsync(venueId, ct).ConfigureAwait(false);
                _section = Section.List;
                _venues = null;
                StartListFetch();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[MyVenuesScreen] Venue delete failed.");
                _actionError = HubErrorText.Localize(ex);
            }
        }, ct);
    }
}
