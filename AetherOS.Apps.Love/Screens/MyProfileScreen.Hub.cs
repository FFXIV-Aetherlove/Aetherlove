using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class MyProfileScreen
{
    /// <summary>Which slice of the "My" area is showing.</summary>
    private enum Section { Hub, Detail, RpCharacters, Warnings, ModMessages, SupporterVanity, SupporterStats, Holiday, AvatarRing }

    private Section _section = Section.Hub;

    private readonly EntranceAnimation _entrance = new();

    private readonly SessionBootstrapper _bootstrap;

    private volatile MyStatsDto? _stats;

    private const float HubPadX = 16f;

    private void StartStatsFetch()
    {
        _stats = null;
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hubClient.GetMyStatsAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _stats = dto;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                UiHost.Log.Warning(ex, "[MyProfileScreen] GetMyStatsAsync failed.");
            }
        }, ct);
    }

    /// <summary>Opens the profile detail (view/edit/images) on a chosen tab; a different <see cref="_prevTab"/>
    /// forces that tab's on-enter load to run.</summary>
    private void OpenDetail(Tab tab)
    {
        _activeTab = tab;
        _prevTab = tab == Tab.View ? Tab.Edit : Tab.View;
        _section = Section.Detail;
    }

    private void DrawHub()
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetContentRegionAvail().X;
        _entrance.BeginFrame();

        ImGui.Spacing();
        if (_bootstrap.LastConnection is { HolidayMode: true })
        {
            DrawHolidayActiveBanner(winW);
        }
        DrawStatsRow(winW);
        ImGui.Spacing();
        ImGui.Spacing();

        DrawSectionHeader(Loc.T("profile.section_myprofile"), HubPadX);
        var profileRows = new List<MenuRow>
        {
            new(FontAwesomeIcon.User, t.Accent, Loc.T("profile.menu_view"), 0, false, () => OpenDetail(Tab.View)),
            new(FontAwesomeIcon.Edit, t.Accent, Loc.T("profile.menu_edit"), 0, false, () => OpenDetail(Tab.Edit)),
            new(FontAwesomeIcon.Images, t.Accent, Loc.T("profile.menu_images"), 0, false, () => OpenDetail(Tab.Images)),
            new(FontAwesomeIcon.TheaterMasks, t.Accent, Loc.T("profile.menu_rp"), 0, false, OpenRpCharacters),
            new(FontAwesomeIcon.Ring, t.Accent, Loc.T("rings.section_title"), 0, false, OpenAvatarRing),
            new(FontAwesomeIcon.UmbrellaBeach, t.Accent, Loc.T("profile.menu_holiday"), 0, false, OpenHoliday),
        };
        DrawMenuCard("myprof", winW, HubPadX, profileRows);

        if (_bootstrap.LastConnection is { IsSupporter: true })
        {
            ImGui.Spacing();
            ImGui.Spacing();
            DrawSectionHeader(Loc.T("profile.section_supporter"), HubPadX, UiColors.Patreon);
            var supOrigin = ImGui.GetCursorScreenPos();
            var supporterRows = new List<MenuRow>
            {
                new(FontAwesomeIcon.Palette, UiColors.Patreon, Loc.T("profile.menu_sup_vanity"), 0, false, OpenSupporterVanity),
                new(FontAwesomeIcon.ChartLine, UiColors.Patreon, Loc.T("profile.menu_sup_stats"), 0, false, OpenSupporterStats),
            };
            DrawMenuCard("supporter", winW, HubPadX, supporterRows);
            DrawSupporterShine(
                new Vector2(supOrigin.X + Px(HubPadX), supOrigin.Y),
                new Vector2(supOrigin.X + winW - Px(HubPadX), supOrigin.Y + Px(MenuCardRowHeight) * supporterRows.Count));
        }

        var conn = _bootstrap.LastConnection;
        var messages = conn?.ModeratorMessages ?? [];
        var warnings = conn?.Warnings ?? [];

        var rows = new List<MenuRow>();
        if (messages.Length > 0)
        {
            rows.Add(new(FontAwesomeIcon.Envelope, UiColors.MessageAccent, Loc.T("settings.modmsg_title"),
                messages.Count(m => !m.Seen), false, () =>
                {
                    _entrance.Arm();
                    _section = Section.ModMessages;
                }));
        }
        if (warnings.Length > 0)
        {
            rows.Add(new(FontAwesomeIcon.ExclamationTriangle, UiColors.WarningAccent, Loc.T("settings.warnings_title"),
                warnings.Count(w => !w.Seen), false, () =>
                {
                    _entrance.Arm();
                    _section = Section.Warnings;
                }));
        }
        if (rows.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            DrawSectionHeader(Loc.T("profile.section_service"), HubPadX);
            DrawMenuCard("service", winW, HubPadX, rows);
        }
        _entrance.EndFrame();
    }

    // One-shot shine over the Supporter card, armed each time the profile screen opens.
    private double _supShineStart = double.MinValue;

    private void ArmSupporterShine() => _supShineStart = ImGui.GetTime() + 0.35;

    /// <summary>Gold glint sweeping the Supporter card shortly after the hub opens; skipped under reduce-motion.</summary>
    private void DrawSupporterShine(Vector2 min, Vector2 max)
    {
        if (AccessibilityService.ReduceMotion)
        {
            return;
        }
        const double Sweep = 0.9;
        var t = (ImGui.GetTime() - _supShineStart) / Sweep;
        if (t is < 0 or > 1)
        {
            return;
        }
        var f = (float)t;
        var fade = 1f - MathF.Abs(f * 2f - 1f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(min, max, ImGui.GetColorU32(UiColors.Patreon with { W = 0.12f * fade }), Px(10f));

        var bandW = (max.Y - min.Y) * 1.3f;
        var centerX = min.X - bandW + f * (max.X - min.X + bandW * 2f);
        var gold = new Vector4(1f, 0.85f, 0.40f, 1f);
        var peak = ImGui.GetColorU32(gold with { W = 0.30f });
        var edge = ImGui.GetColorU32(gold with { W = 0f });
        dl.PushClipRect(min, max, true);
        dl.AddRectFilledMultiColor(new Vector2(centerX - bandW, min.Y), new Vector2(centerX, max.Y), edge, peak, peak, edge);
        dl.AddRectFilledMultiColor(new Vector2(centerX, min.Y), new Vector2(centerX + bandW, max.Y), peak, edge, edge, peak);
        dl.PopClipRect();
    }

    private void DrawStatsRow(float winW)
    {
        var t = ThemeService.Current;
        var pad = Px(HubPadX);
        var gap = Px(8f);
        var blockW = (winW - pad * 2f - gap * 2f) / 3f;
        var blockH = Px(92f);

        var lovesYou = _stats?.LovesYouCount.ToString() ?? "-";
        var matches = _stats?.MatchCount.ToString() ?? "-";
        string rate;
        if (_stats is null)
        {
            rate = "-";
        }
        else if (_stats.SwipeCount == 0)
        {
            rate = "0%";
        }
        else
        {
            rate = (_stats.MatchCount * 100.0 / _stats.SwipeCount).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        var origin = ImGui.GetCursorScreenPos() + new Vector2(pad, 0f);
        var dl = ImGui.GetWindowDrawList();

        DrawStatBlock(dl, origin, blockW, blockH, FontAwesomeIcon.Heart, t.SecondaryEnd, lovesYou, Loc.T("profile.stat_loves_you"), beat: true);
        DrawStatBlock(dl, origin + new Vector2(blockW + gap, 0f), blockW, blockH, FontAwesomeIcon.Fire, t.Accent, matches, Loc.T("profile.stat_matches"), beat: false);
        DrawStatBlock(dl, origin + new Vector2((blockW + gap) * 2f, 0f), blockW, blockH, FontAwesomeIcon.Percent, t.AccentLight, rate, Loc.T("profile.stat_match_rate"), beat: false);

        ImGui.Dummy(new Vector2(winW, blockH));
    }

    /// <summary>One stat tile: icon, large value, caption; the heart beats unless reduced motion is on.</summary>
    private static void DrawStatBlock(ImDrawListPtr dl, Vector2 tl, float w, float h, FontAwesomeIcon icon,
        Vector4 accent, string value, string label, bool beat)
    {
        var br = tl + new Vector2(w, h);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = 0.10f }), Px(10f));
        dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = 0.55f }), Px(10f), ImDrawFlags.None, Px(1.5f));

        var cx = tl.X + w * 0.5f;
        var scale = 1f;
        if (beat && !AccessibilityService.ReduceMotion)
        {
            var phase = (float)ImGui.GetTime() * 2.2f;
            var thump = MathF.Pow(MathF.Max(0f, MathF.Sin(phase)), 6f);
            scale = 1f + 0.24f * thump;
        }
        var iconPx = Px(21f) * scale;
        var iconCenter = new Vector2(cx, tl.Y + Px(24f));
        IconDraw.AddCentered(dl, icon, iconPx, iconCenter, ImGui.GetColorU32(accent));

        var bigSize = ImGui.GetFontSize() * 1.5f;
        var valueSz = ImGui.CalcTextSize(value) * 1.5f;
        dl.AddText(ImGui.GetFont(), bigSize, new Vector2(cx - valueSz.X * 0.5f, tl.Y + Px(42f)), 0xFFFFFFFFu, value);

        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(cx - labelSz.X * 0.5f, br.Y - Px(20f)), UiColors.TextMuted, label);
    }

    private void DrawDetail()
    {
        // Hiding the back button keeps its hit-target from lurking under the fullscreen overlay's own back pill.
        if (!(_activeTab == Tab.View && _profileScreen.IsCharacterFullscreenOpen))
        {
            DrawHubBackButton();
        }
        switch (_activeTab)
        {
            case Tab.View:
                DrawViewTab();
                break;
            case Tab.Edit:
                DrawEditTab();
                break;
            case Tab.Images:
                DrawImagesTab();
                break;
        }
    }

    private void DrawHubBackButton()
    {
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("profile.back_to_my"), FontAwesomeIcon.User))
        {
            _entrance.Arm();
            _section = Section.Hub;
        }
        ImGui.Spacing();
    }

    private void DrawWarningsView()
    {
        DrawHubBackButton();
        DrawSubpageHeading(Loc.T("settings.warnings_title"), HubPadX);

        var warnings = _bootstrap.LastConnection?.Warnings ?? [];
        if (warnings.Length == 0)
        {
            DrawNoticeEmpty(Loc.T("settings.no_warnings"));
            return;
        }

        using var scroll = ImRaii.Child("##myWarnList", ImGui.GetContentRegionAvail(), false);
        if (!scroll.Success)
        {
            return;
        }
        var listW = ImGui.GetContentRegionAvail().X;
        _entrance.BeginFrame();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(10f)));
        ImGui.Dummy(new Vector2(1f, Px(2f)));
        foreach (var w in warnings.OrderByDescending(w => w.CreatedAtUtc))
        {
            DrawNoticeCard(listW, FontAwesomeIcon.ExclamationTriangle, UiColors.WarningAccent, w.CreatedAtUtc, w.Reason, w.Seen, HubPadX,
                w.SourceProfileName is { } n ? Loc.T("common.moderation_warning_for", n) : null);
        }
        ImGui.PopStyleVar();
        _entrance.EndFrame();
    }

    private void DrawModMessagesView()
    {
        DrawHubBackButton();
        DrawSubpageHeading(Loc.T("settings.modmsg_title"), HubPadX);

        var messages = _bootstrap.LastConnection?.ModeratorMessages ?? [];
        if (messages.Length == 0)
        {
            DrawNoticeEmpty(Loc.T("settings.no_modmsg"));
            return;
        }

        using var scroll = ImRaii.Child("##myModMsgList", ImGui.GetContentRegionAvail(), false);
        if (!scroll.Success)
        {
            return;
        }
        var listW = ImGui.GetContentRegionAvail().X;
        _entrance.BeginFrame();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(10f)));
        ImGui.Dummy(new Vector2(1f, Px(2f)));
        foreach (var m in messages.OrderByDescending(m => m.CreatedAtUtc))
        {
            DrawNoticeCard(listW, FontAwesomeIcon.Envelope, UiColors.MessageAccent, m.CreatedAtUtc, m.Body, m.Seen, HubPadX,
                m.SourceProfileName is { } n ? Loc.T("common.moderation_message_for", n) : null);
        }
        ImGui.PopStyleVar();
        _entrance.EndFrame();
    }

    private static void DrawNoticeEmpty(string text)
    {
        ImGui.Spacing();
        var winW = ImGui.GetContentRegionAvail().X;
        var textW = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(MathF.Max(Px(HubPadX), (winW - textW) * 0.5f));
        ImGui.TextColored(UiColors.Muted, text);
    }
}
