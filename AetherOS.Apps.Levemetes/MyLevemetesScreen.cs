using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Emoji;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Levemetes;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Widgets = AetherLove.Widgets;

namespace AetherOS.Apps.Levemetes;

public sealed partial class MyLevemetesScreen
{
    private enum Section { List, Editor }

    private Section _section = Section.List;

    private readonly ILevemetesHost _host;
    private readonly IAppCapabilities _caps;
    private readonly Action _backToBrowse;
    private readonly CancellationTokenSource _cts = new();
    private IOsShell? _shell;

    private volatile MyLevemeteDto[]? _mine;
    private volatile bool _listLoading;
    private volatile string? _listError;
    private volatile string? _actionError;
    private volatile bool _saving;
    private volatile bool _renewBusy;
    private float _savedTimer;

    private readonly Dictionary<string, ISharedImmediateTexture?> _photoTex = new();

    /// <summary>Client-side mirror of the server's default ad cap, for the slots-used hint only; the server
    /// stays authoritative via LeveLimitReached.</summary>
    private const int DefaultMaxAds = 3;

    private sealed class PhotoSlot
    {
        public MyLevemetePhotoDto? Server;
        public string StagedPath = "";
        public ISharedImmediateTexture? StagedTex;
        public Vector4 StagedCrop;
        public bool StagedConfirmed;
        public bool PendingRemove;
        public bool Nsfw;

        public void Clear()
        {
            Server = null;
            StagedPath = "";
            StagedTex = null;
            StagedConfirmed = false;
            PendingRemove = false;
            Nsfw = false;
        }
    }

    private Guid _editId = Guid.Empty;
    private int _editKindIdx;
    private int _editCategoryIdx = -1;
    private string _editTitle = "";
    private string _editDescription = "";
    private readonly bool[] _editRegions = new bool[RegionValues.Length];
    private readonly bool[] _editWeekdayHours = new bool[24];
    private readonly bool[] _editWeekendHours = new bool[24];
    private int _editTimezoneIdx;
    private string _tzFilter = "";
    private bool _editReviewsEnabled = true;
    private string _editPrice = "";
    private string _editDiscord = "";
    private readonly PhotoSlot[] _slots = [new(), new(), new()];
    private string? _editValidationError;
    private bool _confirmDelete;
    private float _confirmPanelHeight;
    private readonly EmojiPickerPopup _descEmojiPicker = new();

    private const float PadX = 16f;

    private static string LevemetesCacheDir =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "LevemetesCache");

    public MyLevemetesScreen(ILevemetesHost host, IAppCapabilities caps, Action backToBrowse)
    {
        _host = host;
        _caps = caps;
        _backToBrowse = backToBrowse;
    }

    public void OnShow()
    {
        if (_section == Section.List)
        {
            StartListFetch();
        }
        StartBoostCountFetch();
    }

    private void StartListFetch()
    {
        if (_listLoading)
        {
            return;
        }
        _listLoading = true;
        _listError = null;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var mine = await _host.GetMineAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                CacheTextures(mine);
                _mine = mine;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[MyLevemetesScreen] List fetch failed.");
                _listError = HubErrorText.Localize(ex);
            }
            finally
            {
                _listLoading = false;
            }
        }, ct);
    }

    private void CacheTextures(MyLevemeteDto[] mine)
    {
        foreach (var ad in mine)
        {
            foreach (var photo in ad.Photos)
            {
                var key = $"my_{ad.Id:N}_{photo.Order}";
                if (photo.WebpBytes is { Length: > 0 })
                {
                    _photoTex[key] = AvatarDiskCache.Store(LevemetesCacheDir, key, photo.WebpBytes);
                }
            }
        }
    }

    public void Draw(IOsShell shell)
    {
        _shell = shell;
        if (_section == Section.List)
        {
            DrawList();
        }
        else
        {
            DrawEditor();
        }
    }

    private void DrawList()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetContentRegionAvail().X;
        var pad = Px(PadX);
        var cardW = winW - pad * 2f;
        var scrollViewportTL = ImGui.GetCursorScreenPos();

        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##myLeveScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                ImGui.Dummy(new Vector2(1f, Px(44f)));
                ImGui.SetCursorPosX(pad);
                using (UiFonts.H3?.Push())
                {
                    ImGui.TextColored(t.AccentLight, Loc.T("os.leve_my_title"));
                }
                ImGui.Spacing();

                var mine = _mine;
                if (_listLoading && mine is null)
                {
                    Widgets.LoadingIndicator.Draw();
                }
                else if (_listError is not null && mine is null)
                {
                    DrawCenteredMuted(Loc.T("os.leve_load_failed", _listError));
                }
                else if (mine is not null)
                {
                    DrawListContent(mine, cardW);
                }
            }
        }
        PopScrollbarStyle();

        if (DrawFloatingBackPill(scrollViewportTL + Px(10f, 10f), Loc.T("os.leve_back"), FontAwesomeIcon.Scroll))
        {
            _backToBrowse();
        }
    }

    private void DrawListContent(MyLevemeteDto[] mine, float cardW)
    {
        var pad = Px(PadX);
        var liveCount = mine.Count(a =>
            a.Status is (short)LevemeteAdStatus.Active or (short)LevemeteAdStatus.PendingModeration);

        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(UiColors.Subtle, Loc.T("os.leve_cap_note", liveCount, DefaultMaxAds));
        ImGui.Spacing();

        if (!_host.MessengerAddsEnabled && mine.Length > 0)
        {
            ImGui.SetCursorPosX(pad);
            DrawWarningCard(Loc.T("os.leve_adds_off_warning"), cardW);
            ImGui.Spacing();
        }

        if (_savedTimer > 0f)
        {
            _savedTimer -= ImGui.GetIO().DeltaTime;
            ImGui.SetCursorPosX(pad);
            ImGui.TextColored(UiColors.Success, Loc.T("os.leve_saved"));
            ImGui.Spacing();
        }
        if (_actionError is not null)
        {
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + cardW);
            ImGui.TextColored(UiColors.Danger, _actionError);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        if (mine.Length == 0)
        {
            ImGui.Dummy(new Vector2(1f, Px(20f)));
            DrawCenteredMuted(Loc.T("os.leve_none_yet"));
            ImGui.Dummy(new Vector2(1f, Px(10f)));
        }
        else
        {
            foreach (var ad in mine)
            {
                DrawMyAdRow(ad, cardW);
            }
        }

        ImGui.Spacing();
        if (liveCount < DefaultMaxAds)
        {
            ImGui.SetCursorPosX(pad);
            PushThemeButton(ThemeService.Current);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
            if (SharedUiHelpers.Button($"{Loc.T("os.leve_new_ad")}##leveNewAd", new Vector2(cardW, Px(34f))))
            {
                OpenEditor(null);
            }
            ImGui.PopStyleVar();
            PopThemeButton();
        }
        ImGui.Dummy(new Vector2(1f, Px(12f)));
    }

    private static string StatusLabel(short status) => status switch
    {
        (short)LevemeteAdStatus.Active => Loc.T("os.leve_status_active"),
        (short)LevemeteAdStatus.PendingModeration => Loc.T("os.leve_status_pending"),
        (short)LevemeteAdStatus.Unlisted => Loc.T("os.leve_status_unlisted"),
        (short)LevemeteAdStatus.Expired => Loc.T("os.leve_status_expired"),
        _ => "",
    };

    private static Vector4 StatusColor(short status) => status switch
    {
        (short)LevemeteAdStatus.Active => UiColors.Success,
        (short)LevemeteAdStatus.PendingModeration => UiColors.ReviewOrange,
        (short)LevemeteAdStatus.Unlisted => UiColors.Danger,
        _ => UiColors.Muted,
    };

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }
        return remaining.TotalDays >= 1.0
            ? $"{(int)Math.Ceiling(remaining.TotalDays)}d"
            : $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalHours))}h";
    }

    private void DrawMyAdRow(MyLevemeteDto ad, float cardW)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(PadX);
        var rowH = Px(84f);
        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = tl + new Vector2(cardW, rowH);

        var hovered = ImGui.IsMouseHoveringRect(tl, br);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.07f : 0.045f)), Px(10f));

        var thumbW = Px(80f);
        var thumbH = Px(60f);
        var thumbTL = new Vector2(tl.X + Px(8f), tl.Y + (rowH - thumbH) * 0.5f);
        var cover = ad.Photos.FirstOrDefault(p => p.Order == 1);
        var wrap = cover is not null && _photoTex.TryGetValue($"my_{ad.Id:N}_1", out var tex)
            ? tex?.GetWrapOrDefault()
            : null;
        if (wrap != null)
        {
            var (uv0, uv1) = SharedUiHelpers.CoverFitUvs(wrap.Width, wrap.Height, thumbW, thumbH);
            dl.AddImageRounded(wrap.Handle, thumbTL, thumbTL + new Vector2(thumbW, thumbH),
                uv0, uv1, 0xFFFFFFFFu, Px(8f), ImDrawFlags.RoundCornersAll);
        }
        else
        {
            LevemetesScreen.DrawCategoryTile(dl, thumbTL, new Vector2(thumbW, thumbH), ad.Category, Px(8f));
        }

        var lineH = ImGui.GetTextLineHeight();
        var textX = thumbTL.X + thumbW + Px(10f);
        var textMaxW = br.X - textX - Px(10f);
        var y = tl.Y + Px(9f);
        dl.AddText(new Vector2(textX, y), 0xFFFFFFFFu, TruncateToWidth(ad.Title, textMaxW));

        y += lineH + Px(3f);
        dl.AddText(new Vector2(textX, y), ImGui.GetColorU32(StatusColor(ad.Status)), StatusLabel(ad.Status));

        y += lineH + Px(3f);
        if (ad.Status == (short)LevemeteAdStatus.Active && ad.ExpiresAtUtc is { } expires)
        {
            dl.AddText(new Vector2(textX, y), ImGui.GetColorU32(UiColors.Subtle),
                Loc.T("os.leve_expires_in", FormatRemaining(expires - DateTimeOffset.UtcNow)));
        }
        else
        {
            dl.AddText(new Vector2(textX, y), ImGui.GetColorU32(UiColors.Subtle),
                LevemetesScreen.CategoryLabel(ad.Category));
        }

        // The renew button is submitted before the row button: the first-submitted overlapping item wins
        // clicks, so submitting it after let the row eat every press.
        if (ad.Status is (short)LevemeteAdStatus.Active or (short)LevemeteAdStatus.Expired)
        {
            var renewLabel = Loc.T("os.leve_renew");
            var renewW = ImGui.CalcTextSize(renewLabel).X + Px(18f);
            var btnH = Px(26f);
            ImGui.SetCursorScreenPos(new Vector2(br.X - renewW - Px(10f), tl.Y + (rowH - btnH) * 0.5f));
            PushThemeButton(ThemeService.Current);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, btnH * 0.5f);
            var renewBusy = _renewBusy;
            if (renewBusy)
            {
                ImGui.BeginDisabled();
            }
            if (SharedUiHelpers.Button($"{renewLabel}##renew_{ad.Id:N}", new Vector2(renewW, btnH)))
            {
                StartRenew(ad.Id);
            }
            if (renewBusy)
            {
                ImGui.EndDisabled();
            }
            ImGui.PopStyleVar();
            PopThemeButton();
        }

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##myleve_{ad.Id:N}", new Vector2(cardW, rowH)))
        {
            OpenEditor(ad);
        }
        if (ImGui.IsItemHovered())
        {
            SharedUiHelpers.HandOnHover();
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(6f)));
    }

    private static void DrawCenteredMuted(string text)
    {
        var winW = ImGui.GetContentRegionAvail().X;
        var wrapped = ImGui.CalcTextSize(text, false, winW - Px(PadX) * 2f);
        ImGui.SetCursorPosX(MathF.Max(Px(PadX), (winW - wrapped.X) * 0.5f));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(UiColors.Muted, text);
        ImGui.PopTextWrapPos();
    }

    private void StartRenew(Guid adId)
    {
        _renewBusy = true;
        _actionError = null;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.RenewAdAsync(adId, ct).ConfigureAwait(false);
                StartListFetch();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[MyLevemetesScreen] Renew failed.");
                _actionError = HubErrorText.Localize(ex);
            }
            finally
            {
                _renewBusy = false;
            }
        }, ct);
    }

    private void OpenEditor(MyLevemeteDto? ad)
    {
        _editValidationError = null;
        _actionError = null;
        _confirmDelete = false;
        _tzFilter = "";
        foreach (var slot in _slots)
        {
            slot.Clear();
        }

        if (ad is null)
        {
            _editId = Guid.Empty;
            _editKindIdx = 1;
            _editCategoryIdx = -1;
            _editTitle = "";
            _editDescription = "";
            Array.Clear(_editRegions);
            Array.Clear(_editWeekdayHours);
            Array.Clear(_editWeekendHours);
            _editTimezoneIdx = Math.Max(0, Array.FindIndex(AllTimezones, tz => tz.Id == TimeZoneInfo.Local.Id));
            _editReviewsEnabled = true;
            _editPrice = "";
            _editDiscord = "";
        }
        else
        {
            _editId = ad.Id;
            _editKindIdx = ad.Kind == (short)LevemeteKind.LookingFor ? 0 : 1;
            _editCategoryIdx = Array.IndexOf(LevemetesScreen.KnownCategories, ad.Category);
            _editTitle = ad.Title;
            _editDescription = ad.Description;
            MaskToBools(RegionValues, (Region)ad.RegionMask, (v, m) => (m & v) != 0, _editRegions);
            MaskToHours(ad.WeekdayHoursMask, _editWeekdayHours);
            MaskToHours(ad.WeekendHoursMask, _editWeekendHours);
            _editTimezoneIdx = Math.Max(0, Array.FindIndex(AllTimezones, tz => tz.Id == (ad.Timezone ?? "")));
            _editReviewsEnabled = ad.ReviewsEnabled;
            _editPrice = ad.Price ?? "";
            _editDiscord = ad.Discord ?? "";
            foreach (var photo in ad.Photos)
            {
                if (photo.Order is >= 1 and <= LevemetesLimits.MaxPhotos)
                {
                    var slot = _slots[photo.Order - 1];
                    slot.Server = photo;
                    slot.Nsfw = photo.IsNsfw;
                }
            }
        }
        _section = Section.Editor;
    }

    private void DrawEditor()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetContentRegionAvail().X;
        var pad = Px(PadX);
        var w = winW - pad * 2f;
        var scrollViewportTL = ImGui.GetCursorScreenPos();

        PushScrollbarStyle();
        using (var scroll = ImRaii.Child("##leveEditorScroll", ImGui.GetContentRegionAvail(), false))
        {
            if (scroll.Success)
            {
                ImGui.Dummy(new Vector2(1f, Px(44f)));
                ImGui.SetCursorPosX(pad);
                using (UiFonts.H3?.Push())
                {
                    ImGui.TextColored(t.AccentLight,
                        Loc.T(_editId == Guid.Empty ? "os.leve_new_ad" : "os.leve_edit_ad"));
                }
                ImGui.Spacing();
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(Px(8f), Px(6f)));
                DrawEditorBody(w, pad);
                ImGui.PopStyleVar();
            }
        }
        PopScrollbarStyle();

        if (DrawFloatingBackPill(scrollViewportTL + Px(10f, 10f), Loc.T("os.leve_my_title"), FontAwesomeIcon.List))
        {
            _section = Section.List;
            StartListFetch();
        }
        DrawDeleteConfirm();
        DrawBoostOverlay();
    }

    private void DrawEditorBody(float w, float pad)
    {
        var t = ThemeService.Current;

        ImGui.SetCursorPosX(pad);
        DrawFieldLabel(Loc.T("os.leve_editor_kind"), t);
        ImGui.SetCursorPosX(pad);
        string[] kindLabels = [Loc.T("chat.leve_kind_looking"), Loc.T("chat.leve_kind_offering")];
        DrawRadioPills("levekindedit", kindLabels, ref _editKindIdx, w);

        ImGui.Dummy(new Vector2(1f, Px(14f)));
        ImGui.SetCursorPosX(pad);
        DrawFieldLabel(Loc.T("os.leve_editor_category"), t);
        ImGui.SetCursorPosX(pad);
        var catLabels = LevemetesScreen.KnownCategories.Select(LevemetesScreen.CategoryLabel).ToArray();
        DrawRadioPills("levecatedit", catLabels, ref _editCategoryIdx, w);

        ImGui.Dummy(new Vector2(1f, Px(14f)));
        ImGui.SetCursorPosX(pad);
        DrawFieldLabel(Loc.T("os.leve_editor_title"), t);
        ImGui.SetCursorPosX(pad);
        ImGui.SetNextItemWidth(w);
        ImGui.InputText("##leveEditTitle", ref _editTitle, LevemetesLimits.TitleMaxLength);

        ImGui.Dummy(new Vector2(1f, Px(14f)));
        ImGui.SetCursorPosX(pad);
        DrawFieldLabel(Loc.T("os.leve_editor_description"), t);
        ImGui.SameLine();
        {
            var iconH = ImGui.GetTextLineHeight();
            var grinTex = UiHost.EmojiService.GetEmoji("grinning")?.GetWrapOrDefault();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Px(2f, 2f));
            var clicked = grinTex != null
                ? ImGui.ImageButton(grinTex.Handle, new Vector2(iconH - Px(4f)))
                : ImGui.SmallButton(":)##leveDescEmoji");
            ImGui.PopStyleVar();
            _descEmojiPicker.Draw();
            if (clicked)
            {
                _descEmojiPicker.Open(InsertDescriptionEmoji);
            }
        }
        ImGui.SetCursorPosX(pad);
        var descBefore = _editDescription;
        InputTextMultilineWithPaste("##leveEditDesc", ref _editDescription,
            LevemetesLimits.DescriptionRawMaxLength, new Vector2(w, Px(110f)));
        if (EmojiText.EffectiveLength(_editDescription) > LevemetesLimits.DescriptionMaxLength)
        {
            _editDescription = descBefore;
        }
        ImGui.SetCursorPosX(pad);
        ImGui.TextColored(UiColors.Hint,
            $"{EmojiText.EffectiveLength(_editDescription)}/{LevemetesLimits.DescriptionMaxLength}");

        ImGui.Dummy(new Vector2(1f, Px(14f)));
        ImGui.SetCursorPosX(pad);
        DrawFieldLabel(Loc.T("os.leve_editor_regions"), t);
        ImGui.SetCursorPosX(pad);
        VenueFields.DrawPillToggleRow("leveregedit", Regions, _editRegions, w);

        ImGui.Dummy(new Vector2(1f, Px(14f)));
        ImGui.SetCursorPosX(pad);
        DrawSectionHeading(Loc.T("os.leve_avail_weekday"), t);
        ImGui.SetCursorPosX(pad);
        DrawOnlineHoursEditor(w, _editWeekdayHours, "leveWeekday");
        ImGui.Dummy(new Vector2(1f, Px(14f)));
        ImGui.SetCursorPosX(pad);
        DrawSectionHeading(Loc.T("os.leve_avail_weekend"), t);
        ImGui.SetCursorPosX(pad);
        DrawOnlineHoursEditor(w, _editWeekendHours, "leveWeekend");

        ImGui.Dummy(new Vector2(1f, Px(14f)));
        ImGui.SetCursorPosX(pad);
        DrawFieldLabel(Loc.T("os.leve_editor_timezone"), t);
        ImGui.SetCursorPosX(pad);
        ImGui.SetNextItemWidth(w);
        var tzPreview = _editTimezoneIdx >= 0 && _editTimezoneIdx < TimezoneNames.Length
            ? TimezoneNames[_editTimezoneIdx]
            : "UTC";
        if (ImGui.BeginCombo("##leveTz", tzPreview))
        {
            if (ImGui.IsWindowAppearing())
            {
                _tzFilter = "";
                ImGui.SetKeyboardFocusHere();
            }
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##leveTzFilter", Loc.T("os.leve_editor_tz_search"), ref _tzFilter, 64);
            var filter = _tzFilter.Trim();
            for (var i = 0; i < TimezoneNames.Length; i++)
            {
                if (filter.Length > 0 && TimezoneNames[i].IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (ImGui.Selectable($"{TimezoneNames[i]}##levetz{i}", i == _editTimezoneIdx))
                {
                    _editTimezoneIdx = i;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Dummy(new Vector2(1f, Px(14f)));
        ImGui.SetCursorPosX(pad);
        DrawFieldLabel(Loc.T("os.leve_editor_price"), t);
        ImGui.SetCursorPosX(pad);
        ImGui.SetNextItemWidth(w);
        ImGui.InputTextWithHint("##levePrice", Loc.T("os.leve_editor_price_hint"),
            ref _editPrice, LevemetesLimits.PriceMaxLength);

        ImGui.Dummy(new Vector2(1f, Px(14f)));
        ImGui.SetCursorPosX(pad);
        DrawFieldLabel(Loc.T("os.leve_editor_discord"), t);
        ImGui.SetCursorPosX(pad);
        ImGui.SetNextItemWidth(w);
        ImGui.InputTextWithHint("##leveDiscord", "https://discord.gg/...",
            ref _editDiscord, LevemetesLimits.DiscordMaxLength);

        ImGui.Spacing();
        ImGui.SetCursorPosX(pad);
        if (DrawToggleSwitch("##leveReviewsOn", Loc.T("os.leve_editor_reviews"), _editReviewsEnabled))
        {
            _editReviewsEnabled = !_editReviewsEnabled;
        }

        ImGui.Dummy(new Vector2(1f, Px(14f)));
        ImGui.SetCursorPosX(pad);
        DrawSectionHeading(Loc.T("os.leve_editor_images"), t);
        ImGui.SetCursorPosX(pad);
        ImGui.PushTextWrapPos(pad + w);
        ImGui.TextColored(UiColors.Subtle, Loc.T("os.leve_editor_slot1_sfw"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        DrawPhotoSlots(w, pad);

        var currentAd = _mine?.FirstOrDefault(a => a.Id == _editId);
        if (currentAd is { Status: (short)LevemeteAdStatus.PendingModeration, TextFlagReason.Length: > 0 })
        {
            ImGui.Spacing();
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + w);
            ImGui.TextColored(UiColors.ReviewOrange, Loc.T("os.leve_flagged_note"));
            ImGui.PopTextWrapPos();
        }

        if (_editValidationError is not null)
        {
            ImGui.Spacing();
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + w);
            ImGui.TextColored(UiColors.Danger, _editValidationError);
            ImGui.PopTextWrapPos();
        }
        if (_actionError is not null)
        {
            ImGui.Spacing();
            ImGui.SetCursorPosX(pad);
            ImGui.PushTextWrapPos(pad + w);
            ImGui.TextColored(UiColors.Danger, _actionError);
            ImGui.PopTextWrapPos();
        }

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SetCursorPosX(pad);
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
        var saving = _saving;
        if (saving)
        {
            ImGui.BeginDisabled();
        }
        if (SharedUiHelpers.Button($"{Loc.T("os.leve_save")}##leveSave", new Vector2(w, Px(36f))))
        {
            SaveAd();
        }
        if (saving)
        {
            ImGui.EndDisabled();
        }
        ImGui.PopStyleVar();
        PopThemeButton();

        if (_editId != Guid.Empty)
        {
            ImGui.Spacing();
            DrawBoostRow(w, pad);

            ImGui.Spacing();
            ImGui.SetCursorPosX(pad);
            PushDangerButton();
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(10f));
            if (SharedUiHelpers.Button($"{Loc.T("os.leve_delete")}##leveDeleteAd", new Vector2(w, Px(30f))))
            {
                _confirmDelete = true;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }
        ImGui.Dummy(new Vector2(1f, Px(14f)));
    }

    private void InsertDescriptionEmoji(string name)
    {
        var add = $":{name}: ";
        if (EmojiText.EffectiveLength(_editDescription + add) <= LevemetesLimits.DescriptionMaxLength)
        {
            _editDescription += add;
        }
    }

    private static void DrawRadioPills(string idPrefix, string[] labels, ref int selectedIdx, float maxWidth)
    {
        var t = ThemeService.Current;
        var x = 0f;
        var startX = ImGui.GetCursorPosX();
        for (var i = 0; i < labels.Length; i++)
        {
            var pillW = ImGui.CalcTextSize(labels[i]).X + Px(20f);
            if (x > 0f && x + pillW > maxWidth)
            {
                x = 0f;
                ImGui.SetCursorPosX(startX);
            }
            else if (i > 0)
            {
                ImGui.SameLine(0f, Px(6f));
            }
            var selected = selectedIdx == i;
            if (selected)
            {
                PushThemeButton(t);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.07f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.13f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.18f));
            }
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(14f));
            if (SharedUiHelpers.Button($"{labels[i]}##{idPrefix}{i}", new Vector2(pillW, Px(26f))))
            {
                selectedIdx = i;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
            x += pillW + Px(6f);
        }
    }

    private void DrawPhotoSlots(float w, float pad)
    {
        var gap = Px(8f);
        var slotW = (w - gap * 2f) / 3f;
        var slotH = slotW * (PhotoSpec.LevemeteHeight / (float)PhotoSpec.LevemeteWidth);
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetCursorPosX(pad);
        var origin = ImGui.GetCursorScreenPos();

        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            var tl = new Vector2(origin.X + i * (slotW + gap), origin.Y);
            var br = tl + new Vector2(slotW, slotH);

            ImGui.SetCursorScreenPos(tl);
            var clicked = ImGui.InvisibleButton($"##leveSlot{i}", new Vector2(slotW, slotH));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                SharedUiHelpers.HandOnHover();
            }

            var stagedWrap = slot.StagedConfirmed ? slot.StagedTex?.GetWrapOrDefault() : null;
            var serverWrap = !slot.StagedConfirmed && !slot.PendingRemove && slot.Server is not null
                && _photoTex.TryGetValue($"my_{_editId:N}_{slot.Server.Order}", out var st)
                    ? st?.GetWrapOrDefault()
                    : null;
            var wrap = stagedWrap ?? serverWrap;
            if (wrap != null)
            {
                Vector2 uv0;
                Vector2 uv1;
                if (stagedWrap != null)
                {
                    // The crop rect is (x, y, WIDTH, HEIGHT) in source pixels, matching Shared.CropRect.
                    uv0 = new Vector2(slot.StagedCrop.X / stagedWrap.Width, slot.StagedCrop.Y / stagedWrap.Height);
                    uv1 = new Vector2((slot.StagedCrop.X + slot.StagedCrop.Z) / stagedWrap.Width,
                        (slot.StagedCrop.Y + slot.StagedCrop.W) / stagedWrap.Height);
                }
                else
                {
                    (uv0, uv1) = SharedUiHelpers.CoverFitUvs(wrap.Width, wrap.Height, slotW, slotH);
                }
                dl.AddImageRounded(wrap.Handle, tl, br, uv0, uv1, 0xFFFFFFFFu, Px(8f), ImDrawFlags.RoundCornersAll);
            }
            else
            {
                dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.10f : 0.06f)), Px(8f));
                IconDraw.AddCentered(dl, FontAwesomeIcon.Plus, Px(18f), (tl + br) * 0.5f,
                    ImGui.GetColorU32(UiColors.Muted));
            }
            dl.AddRect(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.14f)), Px(8f), ImDrawFlags.None, Px(1f));

            if (slot.Server is { InReview: true } && !slot.StagedConfirmed && !slot.PendingRemove)
            {
                var label = Loc.T("os.leve_photo_review");
                var labelSz = ImGui.CalcTextSize(label);
                var chipTL = new Vector2(tl.X + Px(4f), br.Y - labelSz.Y - Px(8f));
                dl.AddRectFilled(chipTL, chipTL + new Vector2(labelSz.X + Px(8f), labelSz.Y + Px(4f)),
                    ImGui.GetColorU32(UiColors.ReviewOrange with { W = 0.85f }), Px(4f));
                dl.AddText(chipTL + new Vector2(Px(4f), Px(2f)), 0xFF000000u, label);
            }

            var hasImage = wrap != null;
            if (hasImage)
            {
                var xSize = Px(18f);
                var xTL = new Vector2(br.X - xSize - Px(4f), tl.Y + Px(4f));
                ImGui.SetCursorScreenPos(xTL);
                if (ImGui.InvisibleButton($"##leveSlotX{i}", new Vector2(xSize, xSize)))
                {
                    if (slot.StagedConfirmed)
                    {
                        slot.StagedPath = "";
                        slot.StagedTex = null;
                        slot.StagedConfirmed = false;
                    }
                    else
                    {
                        slot.PendingRemove = true;
                    }
                }
                var xHovered = ImGui.IsItemHovered();
                if (xHovered)
                {
                    SharedUiHelpers.HandOnHover();
                }
                dl.AddCircleFilled(xTL + new Vector2(xSize, xSize) * 0.5f, xSize * 0.5f,
                    ImGui.GetColorU32(new Vector4(0f, 0f, 0f, xHovered ? 0.85f : 0.6f)));
                IconDraw.AddCentered(dl, FontAwesomeIcon.Times, xSize * 0.55f,
                    xTL + new Vector2(xSize, xSize) * 0.5f, 0xFFFFFFFFu);
            }

            if (clicked)
            {
                PickSlotImage(slot);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + slotH + Px(6f)));

        for (var i = 1; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            if (i > 1)
            {
                ImGui.SameLine();
            }
            ImGui.SetCursorPosX(pad + i * (slotW + gap));
            var nsfw = slot.Nsfw;
            if (ImGui.Checkbox($"{Loc.T("os.leve_editor_nsfw")}##leveNsfw{i}", ref nsfw))
            {
                slot.Nsfw = nsfw;
            }
        }
    }

    private void PickSlotImage(PhotoSlot slot)
    {
        _caps.Images.PickAndCrop(new ImageCropRequest(
            Loc.T("profile.select_image"),
            Loc.T("profile.image_files_filter") + "{.png,.jpg,.jpeg,.bmp,.webp}",
            Loc.T("os.leve_crop_title"),
            PhotoSpec.LevemeteHeight / (float)PhotoSpec.LevemeteWidth,
            PhotoSpec.LevemeteWidth,
            PhotoSpec.LevemeteHeight), pick =>
        {
            slot.StagedPath = pick.Path;
            slot.StagedTex = pick.Preview;
            slot.StagedCrop = pick.Crop;
            slot.StagedConfirmed = true;
            slot.PendingRemove = false;
        });
    }

    private LevemeteEditDto? BuildEditDto()
    {
        var title = _editTitle.Trim();
        if (title.Length < LevemetesLimits.TitleMinLength || title.Length > LevemetesLimits.TitleMaxLength)
        {
            _editValidationError = Loc.T("os.leve_val_title",
                LevemetesLimits.TitleMinLength, LevemetesLimits.TitleMaxLength);
            return null;
        }
        if (_editCategoryIdx < 0 || _editCategoryIdx >= LevemetesScreen.KnownCategories.Length)
        {
            _editValidationError = Loc.T("os.leve_val_category");
            return null;
        }
        var regionMask = (int)MaskOr(RegionValues, _editRegions, (a, b) => a | b);
        if (regionMask == 0)
        {
            _editValidationError = Loc.T("os.leve_val_region");
            return null;
        }
        var slot1 = _slots[0];
        var hasSlot1 = slot1.StagedConfirmed || (slot1.Server is not null && !slot1.PendingRemove);
        var hasExtras = _slots.Skip(1).Any(s => s.StagedConfirmed || (s.Server is not null && !s.PendingRemove));
        if (hasExtras && !hasSlot1)
        {
            _editValidationError = Loc.T("os.leve_val_photo");
            return null;
        }
        _editValidationError = null;
        return new LevemeteEditDto(
            Id: _editId == Guid.Empty ? null : _editId,
            Kind: _editKindIdx == 0 ? (short)LevemeteKind.LookingFor : (short)LevemeteKind.Offering,
            Category: LevemetesScreen.KnownCategories[_editCategoryIdx],
            Title: title,
            Description: _editDescription.Trim(),
            RegionMask: regionMask,
            WeekdayHoursMask: HoursToMask(_editWeekdayHours),
            WeekendHoursMask: HoursToMask(_editWeekendHours),
            Timezone: _editTimezoneIdx >= 0 && _editTimezoneIdx < AllTimezones.Length
                ? AllTimezones[_editTimezoneIdx].Id
                : "UTC",
            ReviewsEnabled: _editReviewsEnabled,
            Price: _editPrice.Trim(),
            Discord: _editDiscord.Trim());
    }

    private void SaveAd()
    {
        var dto = BuildEditDto();
        if (dto is null)
        {
            return;
        }
        _saving = true;
        _actionError = null;

        var uploads = new List<(short Slot, string Path, Vector4 Crop, bool Nsfw)>();
        var removals = new List<short>();
        for (short s = 1; s <= _slots.Length; s++)
        {
            var slot = _slots[s - 1];
            if (slot.StagedConfirmed && slot.StagedPath.Length > 0)
            {
                uploads.Add((s, slot.StagedPath, slot.StagedCrop, s > 1 && slot.Nsfw));
            }
            else if (slot.PendingRemove && slot.Server is not null)
            {
                removals.Add(s);
            }
        }
        var ct = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var saved = await _host.SaveAdAsync(dto, ct).ConfigureAwait(false);
                foreach (var (slotNo, path, crop, nsfw) in uploads)
                {
                    var upload = ReadPhotoUpload(path, crop, nsfw, PhotoKind.LevemetePhoto);
                    await _host.SetImageAsync(saved.Id, slotNo, upload, ct).ConfigureAwait(false);
                }
                foreach (var slotNo in removals)
                {
                    await _host.RemoveImageAsync(saved.Id, slotNo, ct).ConfigureAwait(false);
                }
                _savedTimer = 3f;
                _section = Section.List;
                _mine = null;
                StartListFetch();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[MyLevemetesScreen] Save failed.");
                _actionError = HubErrorText.Localize(ex);
            }
            finally
            {
                _saving = false;
            }
        }, ct);
    }

    private void DrawDeleteConfirm()
    {
        if (!_confirmDelete)
        {
            return;
        }
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(windowPos, windowPos + windowSize,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

        ImGui.SetCursorScreenPos(windowPos);
        if (ImGui.InvisibleButton("##leveDelAdScrim", windowSize))
        {
            _confirmDelete = false;
        }

        var w = Px(280f);
        var pad = Px(16f, 16f);
        var h = _confirmPanelHeight > 0f ? _confirmPanelHeight : Px(180f);
        var panelPos = windowPos + (windowSize - new Vector2(w, h)) * 0.5f;

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
        using (var child = ImRaii.Child("##leveDelAdPanel", new Vector2(w, h), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child.Success)
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                Widgets.ModalUi.Header(innerW, FontAwesomeIcon.Trash,
                    Loc.T("os.leve_delete_title"), UiColors.Danger);
                ImGui.TextColored(UiColors.Body, Loc.T("os.leve_delete_confirm"));
                ImGui.Spacing();
                ImGui.Spacing();
                var btnW = (innerW - Px(8f)) * 0.5f;
                PushThemeButton(ThemeService.Current);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                if (SharedUiHelpers.Button(Loc.T("common.cancel"), new Vector2(btnW, Px(30f))))
                {
                    _confirmDelete = false;
                }
                ImGui.PopStyleVar();
                PopThemeButton();
                ImGui.SameLine(0f, Px(8f));
                PushDangerButton();
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
                if (SharedUiHelpers.Button(Loc.T("os.leve_delete"), new Vector2(btnW, Px(30f))))
                {
                    _confirmDelete = false;
                    DeleteAd();
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

    private void DeleteAd()
    {
        var adId = _editId;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _host.DeleteAdAsync(adId, ct).ConfigureAwait(false);
                _section = Section.List;
                _mine = null;
                StartListFetch();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[MyLevemetesScreen] Delete failed.");
                _actionError = HubErrorText.Localize(ex);
            }
        }, ct);
    }
}
