using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Profile;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class MyProfileScreen
{
    /// <summary>Which slice of the "My" area is showing: the hub (stats + menu), the profile detail (the
    /// view/edit/images tabs), or one of the moderation lists.</summary>
    private enum Section { Hub, Detail, Warnings, ModMessages }

    private Section _section = Section.Hub;

    private readonly SessionBootstrapper _bootstrap;
    private readonly ScreenRouter _router;
    private readonly NewsScreen _newsScreen;

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
                Plugin.Log.Warning(ex, "[MyProfileScreen] GetMyStatsAsync failed.");
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

        ImGui.Spacing();
        DrawStatsRow(winW);
        ImGui.Spacing();
        ImGui.Spacing();

        DrawSectionHeader(Loc.T("profile.section_myprofile"), HubPadX);
        DrawMenuCard("myprof", winW, HubPadX, new List<MenuRow>
        {
            new(FontAwesomeIcon.User, t.Accent, Loc.T("profile.menu_view"), 0, false, () => OpenDetail(Tab.View)),
            new(FontAwesomeIcon.Edit, t.Accent, Loc.T("profile.menu_edit"), 0, false, () => OpenDetail(Tab.Edit)),
            new(FontAwesomeIcon.Images, t.Accent, Loc.T("profile.menu_images"), 0, false, () => OpenDetail(Tab.Images)),
        });

        ImGui.Spacing();
        ImGui.Spacing();

        DrawSectionHeader(Loc.T("profile.section_service"), HubPadX);
        var conn = _bootstrap.LastConnection;
        var messages = conn?.ModeratorMessages ?? [];
        var warnings = conn?.Warnings ?? [];
        var newsBadge = _bootstrap.HasUnseenNews ? (conn?.UnseenNews.Length ?? 0) : 0;

        var rows = new List<MenuRow>
        {
            new(FontAwesomeIcon.Newspaper, t.Accent, Loc.T("news.settings_button"), newsBadge, false, () =>
            {
                _newsScreen.RequestListView();
                _router.Navigate(Screen.News);
            }),
        };
        if (messages.Length > 0)
        {
            rows.Add(new(FontAwesomeIcon.Envelope, UiColors.MessageAccent, Loc.T("settings.modmsg_title"),
                messages.Count(m => !m.Seen), false, () => _section = Section.ModMessages));
        }
        if (warnings.Length > 0)
        {
            rows.Add(new(FontAwesomeIcon.ExclamationTriangle, UiColors.WarningAccent, Loc.T("settings.warnings_title"),
                warnings.Count(w => !w.Seen), false, () => _section = Section.Warnings));
        }
        DrawMenuCard("service", winW, HubPadX, rows);
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

    /// <summary>One fancy stat tile: a theme-accent-tinted card with a coloured FontAwesome icon (the heart
    /// beats unless reduced motion is on), a large value, and a caption.</summary>
    private static void DrawStatBlock(ImDrawListPtr dl, Vector2 tl, float w, float h, FontAwesomeIcon icon,
        Vector4 accent, string value, string label, bool beat)
    {
        var br = tl + new Vector2(w, h);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = 0.10f }), Px(10f));
        dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = 0.55f }), Px(10f), ImDrawFlags.None, Px(1.5f));

        var cx = tl.X + w * 0.5f;
        var iconFont = Plugin.PluginInterface.UiBuilder.FontIcon;

        var scale = 1f;
        if (beat && !AccessibilityService.ReduceMotion)
        {
            var phase = (float)ImGui.GetTime() * 2.2f;
            var thump = MathF.Pow(MathF.Max(0f, MathF.Sin(phase)), 6f);
            scale = 1f + 0.24f * thump;
        }
        var iconPx = Px(21f) * scale;

        ImGui.PushFont(iconFont);
        var iconGlyph = icon.ToIconString();
        var iconDrawSz = ImGui.CalcTextSize(iconGlyph) * (iconPx / ImGui.GetFontSize());
        var iconCenter = new Vector2(cx, tl.Y + Px(24f));
        dl.AddText(ImGui.GetFont(), iconPx, iconCenter - iconDrawSz * 0.5f, ImGui.GetColorU32(accent), iconGlyph);
        ImGui.PopFont();

        var bigSize = ImGui.GetFontSize() * 1.5f;
        var valueSz = ImGui.CalcTextSize(value) * 1.5f;
        dl.AddText(ImGui.GetFont(), bigSize, new Vector2(cx - valueSz.X * 0.5f, tl.Y + Px(42f)), 0xFFFFFFFFu, value);

        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(cx - labelSz.X * 0.5f, br.Y - Px(20f)), UiColors.TextMuted, label);
    }

    private void DrawDetail()
    {
        DrawHubBackButton();
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
        if (DrawBackButton(Loc.T("profile.back_to_my")))
        {
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
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(10f)));
        ImGui.Dummy(new Vector2(1f, Px(2f)));
        foreach (var w in warnings.OrderByDescending(w => w.CreatedAtUtc))
        {
            DrawNoticeCard(listW, FontAwesomeIcon.ExclamationTriangle, UiColors.WarningAccent, w.CreatedAtUtc, w.Reason, w.Seen, HubPadX);
        }
        ImGui.PopStyleVar();
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
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(10f)));
        ImGui.Dummy(new Vector2(1f, Px(2f)));
        foreach (var m in messages.OrderByDescending(m => m.CreatedAtUtc))
        {
            DrawNoticeCard(listW, FontAwesomeIcon.Envelope, UiColors.MessageAccent, m.CreatedAtUtc, m.Body, m.Seen, HubPadX);
        }
        ImGui.PopStyleVar();
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
