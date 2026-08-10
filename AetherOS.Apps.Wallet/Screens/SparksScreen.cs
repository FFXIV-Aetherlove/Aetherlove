using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherLove.Shared.Sparks;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Wallet;

/// <summary>The sparks home: balance hero, the weekly-cap ring, a card each for the earning guide and the
/// history page, and the starred currencies carried over from the Currencies tab.</summary>
internal sealed class SparksScreen
{
    private const float PadX = 16f;
    private const float RevealSeconds = 0.7f;
    private const float CountUpSeconds = 0.5f;
    private const float NavCardHeight = 76f;
    private const float NavGap = 10f;
    private const float CurrencyPollSeconds = 2f;

    private static readonly Vector4 EarnCardTop = new(0.36f, 0.58f, 0.92f, 1f);
    private static readonly Vector4 EarnCardBottom = new(0.16f, 0.29f, 0.60f, 1f);
    private static readonly Vector4 HistoryCardTop = new(0.44f, 0.38f, 0.72f, 1f);
    private static readonly Vector4 HistoryCardBottom = new(0.22f, 0.18f, 0.42f, 1f);

    private readonly IWalletHost _host;
    private readonly WalletFavorites _favorites;
    private readonly Action<SparkWalletDto> _openEarn;
    private readonly Action _openHistory;
    private readonly EntranceAnimation _entrance = new();

    private volatile SparkWalletDto? _wallet;
    private volatile IReadOnlyList<WalletCurrencyRow>? _allRows;
    private volatile IReadOnlyList<WalletCurrencyRow>? _favoriteRows;
    private volatile bool _loading;
    private volatile bool _currenciesLoading;
    private int _generation;
    private int _currencyGeneration;
    private int _snapshotVersion;
    private float _sincePoll;
    private double _revealStamp = -1.0;

    public SparksScreen(IWalletHost host, WalletFavorites favorites, Action<SparkWalletDto> openEarn,
        Action openHistory)
    {
        _host = host;
        _favorites = favorites;
        _openEarn = openEarn;
        _openHistory = openHistory;
    }

    /// <summary>The last loaded snapshot, so a page opened by intent can borrow it instead of refetching.</summary>
    public SparkWalletDto? Wallet => _wallet;

    public void OnShow()
    {
        _entrance.Arm();
        Refresh();
    }

    public void Refresh()
    {
        var generation = Interlocked.Increment(ref _generation);
        _loading = true;
        _ = Task.Run(async () =>
        {
            var wallet = await _host.GetSparkWalletAsync().ConfigureAwait(false);
            if (generation != _generation)
            {
                return;
            }
            if (wallet is not null)
            {
                _wallet = wallet;
                _revealStamp = -1.0;
            }
            _loading = false;
        });
        RefreshCurrencies();
    }

    /// <summary>The starred rows are read from the game rather than the server, so they refresh on their own
    /// schedule and a hub outage leaves them intact.</summary>
    private void RefreshCurrencies()
    {
        var generation = Interlocked.Increment(ref _currencyGeneration);
        _currenciesLoading = true;
        _sincePoll = 0f;
        _ = Task.Run(async () =>
        {
            IReadOnlyList<WalletCurrencyRow> rows;
            try
            {
                rows = await _host.ReadCurrenciesAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                rows = [];
            }
            if (generation != _currencyGeneration)
            {
                return;
            }
            _allRows = rows;
            _favoriteRows = _favorites.Pick(rows);
            _currenciesLoading = false;
        });
    }

    /// <summary>Unstars from here, where every row is a favorite by definition. The list this frame is
    /// already captured, so the row simply stops being drawn from the next one.</summary>
    private void Unstar(uint itemId)
    {
        _favorites.Toggle(itemId);
        if (_allRows is { } rows)
        {
            _favoriteRows = _favorites.Pick(rows);
        }
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;

        if (_host.SnapshotVersion != _snapshotVersion)
        {
            _snapshotVersion = _host.SnapshotVersion;
            _favoriteRows = null;
            RefreshCurrencies();
        }
        else if (!_currenciesLoading)
        {
            _sincePoll += ImGui.GetIO().DeltaTime;
            if (_sincePoll >= CurrencyPollSeconds)
            {
                RefreshCurrencies();
            }
        }

        var wallet = _wallet;
        if (wallet is null)
        {
            if (_loading)
            {
                ImGui.Dummy(new Vector2(0f, Px(60f)));
                var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(20f));
                LoadingSpinner.Draw(center, Px(14f), Px(3f), ImGui.GetColorU32(t.Accent));
                ImGui.Dummy(new Vector2(0f, Px(50f)));
                DrawCenteredHint(Loc.T("os.wallet_loading"), winW);
            }
            else
            {
                ImGui.Dummy(new Vector2(0f, Px(60f)));
                DrawCenteredHint(Loc.T("os.wallet_offline"), winW);
                ImGui.Dummy(new Vector2(0f, Px(10f)));
                var btnW = Px(150f);
                ImGui.SetCursorPosX((winW - btnW) * 0.5f);
                if (ModalUi.Button(Loc.T("os.wallet_retry"), btnW))
                {
                    Refresh();
                }
            }
            _entrance.EndFrame();
            return;
        }

        if (_revealStamp < 0.0)
        {
            _revealStamp = ImGui.GetTime();
        }
        var elapsed = (float)(ImGui.GetTime() - _revealStamp);
        var reveal = ctx.ReduceMotion ? 1f : EaseOut(Math.Clamp(elapsed / RevealSeconds, 0f, 1f));
        var countUp = ctx.ReduceMotion ? 1f : EaseOut(Math.Clamp(elapsed / CountUpSeconds, 0f, 1f));

        DrawBalanceHero(ctx, wallet, winW, countUp);
        DrawRingCard(ctx, wallet, winW, reveal);
        DrawNavCards(wallet, winW);
        DrawFavorites(ctx, winW);

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        _entrance.EndFrame();
    }

    private void DrawBalanceHero(OsAppContext ctx, SparkWalletDto wallet, float winW, float countUp)
    {
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(118f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(cardW, cardH);
        var dl = ImGui.GetWindowDrawList();
        OsDrawShared.RoundedGradient(dl, tl, br, Px(18f), WalletApp.TileTopColor, WalletApp.TileBottomColor);

        var boltPx = Px(52f);
        var boltSz = IconDraw.Measure(FontAwesomeIcon.Bolt, boltPx);
        IconDraw.Add(dl, FontAwesomeIcon.Bolt, boltPx,
            new Vector2(br.X - boltSz.X - Px(18f), tl.Y + (cardH - boltSz.Y) * 0.5f), OsDrawShared.White(0.22f));

        var shown = (long)(wallet.Balance * countUp);
        using (UiFonts.H1?.Push())
        {
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(tl.X + Px(18f), tl.Y + Px(16f)),
                0xFFFFFFFFu, shown.ToString("N0", ctx.Culture));
        }
        float lineH;
        using (UiFonts.H1?.Push())
        {
            lineH = ImGui.GetTextLineHeight();
        }
        dl.AddText(new Vector2(tl.X + Px(18f), tl.Y + Px(20f) + lineH), OsDrawShared.White(0.82f),
            Loc.T("os.wallet_balance_caption"));
        dl.AddText(new Vector2(tl.X + Px(18f), br.Y - Px(14f) - ImGui.GetTextLineHeight()),
            OsDrawShared.White(0.62f),
            Loc.T("os.wallet_lifetime_earned", wallet.LifetimeEarned.ToString("N0", ctx.Culture)));

        ImGui.Dummy(new Vector2(0f, cardH + Px(10f)));
    }

    private void DrawRingCard(OsAppContext ctx, SparkWalletDto wallet, float winW, float reveal)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var lineH = ImGui.GetTextLineHeight();
        var ringR = Px(64f);
        var showBonus = wallet.BonusEarnedThisWeek > 0 || wallet.Catalog.Any(e => e.Pool == SparkPool.Bonus);
        var legendLines = showBonus ? 4f : 3f;
        var cardH = Px(16f) + lineH + Px(10f) + ringR * 2f + Px(16f) + legendLines * (lineH + Px(5f)) + Px(12f);

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), OsDrawShared.White(0.05f), Px(18f));

        dl.AddText(new Vector2(tl.X + Px(14f), tl.Y + Px(12f)), ImGui.GetColorU32(UiColors.Hint),
            Loc.T("os.wallet_week_title"));

        var ringCenter = new Vector2(tl.X + cardW * 0.5f, tl.Y + Px(16f) + lineH + Px(6f) + ringR);
        CapRing.Draw(ringCenter, ringR, Px(11f), wallet.RoutineEarnedThisWeek, wallet.ExemptEarnedThisWeek,
            wallet.RoutineWeeklyCap, wallet.TotalWeeklyCap, reveal);

        var legendY = ringCenter.Y + ringR + Px(16f);
        var legendX = tl.X + Px(18f);
        DrawLegendLine(dl, legendX, ref legendY, lineH, ImGui.GetColorU32(t.Accent),
            Loc.T("os.wallet_routine_legend",
                wallet.RoutineEarnedThisWeek.ToString("N0", ctx.Culture),
                wallet.RoutineWeeklyCap.ToString("N0", ctx.Culture)));
        DrawLegendLine(dl, legendX, ref legendY, lineH, UiColors.FavoriteStar,
            Loc.T("os.wallet_explorer_legend", wallet.ExemptEarnedThisWeek.ToString("N0", ctx.Culture),
                (wallet.TotalWeeklyCap - wallet.RoutineWeeklyCap).ToString("N0", ctx.Culture)));
        if (showBonus)
        {
            DrawLegendLine(dl, legendX, ref legendY, lineH, ImGui.GetColorU32(UiColors.Success),
                Loc.T("os.wallet_bonus_legend", wallet.BonusEarnedThisWeek.ToString("N0", ctx.Culture),
                    wallet.BonusWeeklyCap.ToString("N0", ctx.Culture)));
        }

        var untilReset = wallet.WeekResetsAtUtc - DateTimeOffset.UtcNow;
        var clockSz = IconDraw.Measure(FontAwesomeIcon.HourglassHalf, Px(12f));
        IconDraw.Add(dl, FontAwesomeIcon.HourglassHalf, Px(12f),
            new Vector2(legendX, legendY + (lineH - clockSz.Y) * 0.5f), ImGui.GetColorU32(t.AccentLight));
        dl.AddText(new Vector2(legendX + clockSz.X + Px(7f), legendY), ImGui.GetColorU32(t.AccentLight),
            Loc.T("os.wallet_resets_in", FormatCountdown(untilReset < TimeSpan.Zero ? TimeSpan.Zero : untilReset)));

        ImGui.Dummy(new Vector2(0f, cardH + Px(10f)));
    }

    private static void DrawLegendLine(ImDrawListPtr dl, float x, ref float y, float lineH, uint dotColor, string text)
    {
        var dotR = Px(5f);
        dl.AddCircleFilled(new Vector2(x + dotR, y + lineH * 0.5f), dotR, dotColor);
        dl.AddText(new Vector2(x + dotR * 2f + Px(8f), y), ImGui.GetColorU32(UiColors.Body), text);
        y += lineH + Px(5f);
    }

    private void DrawNavCards(SparkWalletDto wallet, float winW)
    {
        var totalW = winW - Px(PadX) * 2f;
        var cardW = (totalW - Px(NavGap)) * 0.5f;
        var size = new Vector2(cardW, Px(NavCardHeight));
        var earnCount = wallet.Catalog.Length;

        ImGui.SetCursorPosX(Px(PadX));
        var rowY = ImGui.GetCursorPosY();
        if (WalletNavCard.Draw("##walletEarnCard", size, EarnCardTop, EarnCardBottom, FontAwesomeIcon.Bolt,
                Loc.T("os.wallet_earn_title"), Loc.T("os.wallet_earn_card_sub", earnCount)))
        {
            _openEarn(wallet);
        }

        ImGui.SetCursorPos(new Vector2(Px(PadX) + cardW + Px(NavGap), rowY));
        if (WalletNavCard.Draw("##walletHistoryCard", size, HistoryCardTop, HistoryCardBottom, FontAwesomeIcon.History,
                Loc.T("os.wallet_history_title"), Loc.T("os.wallet_history_card_sub")))
        {
            _openHistory();
        }

        ImGui.Dummy(new Vector2(0f, Px(6f)));
    }

    /// <summary>The currencies starred on the other tab, so the numbers people actually track sit on the
    /// screen they open first. The star stays live here so one tap drops a currency off the list.</summary>
    private void DrawFavorites(OsAppContext ctx, float winW)
    {
        var rows = _favoriteRows;
        if (rows is null || rows.Count == 0)
        {
            return;
        }

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(UiColors.Body, Loc.T("os.wallet_cur_section_favorites"));
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var totalH = 0f;
        foreach (var row in rows)
        {
            totalH += CurrencyRowDraw.RowHeight(row);
        }

        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var rounding = Px(CurrencyRowDraw.CardRounding);
        dl.AddRectFilled(tl, tl + new Vector2(cardW, totalH), OsDrawShared.White(0.05f), rounding);

        var y = tl.Y;
        for (var i = 0; i < rows.Count; i++)
        {
            var rowH = CurrencyRowDraw.RowHeight(rows[i]);
            if (i > 0)
            {
                dl.AddLine(new Vector2(tl.X + Px(12f), y), new Vector2(tl.X + cardW - Px(12f), y),
                    OsDrawShared.White(0.06f), Px(1f));
            }
            var itemId = rows[i].ItemId;
            CurrencyRowDraw.Draw(ctx, dl, rows[i], new Vector2(tl.X, y), cardW, rowH, rounding,
                i == 0, i == rows.Count - 1, flying: false, _host.GetCurrencyIcon,
                new CurrencyStar(true, () => Unstar(itemId)));
            y += rowH;
        }

        ImGui.Dummy(new Vector2(cardW, totalH + Px(6f)));
    }

    internal static FontAwesomeIcon ActionIcon(SparkAction action) => action switch
    {
        SparkAction.GameLogin => FontAwesomeIcon.SignInAlt,
        SparkAction.OpenedThreeApps => FontAwesomeIcon.ThLarge,
        SparkAction.MarketActivity => FontAwesomeIcon.Store,
        SparkAction.PlacesBrowsing => FontAwesomeIcon.MapMarkedAlt,
        SparkAction.ArcadeGame => FontAwesomeIcon.Gamepad,
        SparkAction.YapperEngage => FontAwesomeIcon.Heart,
        SparkAction.YapperPost => FontAwesomeIcon.Feather,
        SparkAction.YapperReply => FontAwesomeIcon.Reply,
        SparkAction.YapperCheckFeed => FontAwesomeIcon.Rss,
        SparkAction.WayfinderFindFirst or SparkAction.WayfinderFindSecond or SparkAction.WayfinderFind =>
            FontAwesomeIcon.Compass,
        SparkAction.AdminAdjust => FontAwesomeIcon.Wrench,
        _ => FontAwesomeIcon.Question,
    };

    internal static string ActionLabel(SparkAction action) => action switch
    {
        SparkAction.GameLogin => Loc.T("os.wallet_action_game_login"),
        SparkAction.OpenedThreeApps => Loc.T("os.wallet_action_three_apps"),
        SparkAction.MarketActivity => Loc.T("os.wallet_action_market"),
        SparkAction.PlacesBrowsing => Loc.T("os.wallet_action_places"),
        SparkAction.ArcadeGame => Loc.T("os.wallet_action_arcade"),
        SparkAction.YapperEngage => Loc.T("os.wallet_action_yapper_engage"),
        SparkAction.YapperPost => Loc.T("os.wallet_action_yapper_post"),
        SparkAction.YapperReply => Loc.T("os.wallet_action_yapper_reply"),
        SparkAction.YapperCheckFeed => Loc.T("os.wallet_action_yapper_feed"),
        SparkAction.WayfinderFindFirst => Loc.T("os.wallet_action_wayfinder_first"),
        SparkAction.WayfinderFindSecond => Loc.T("os.wallet_action_wayfinder_second"),
        SparkAction.WayfinderFind => Loc.T("os.wallet_action_wayfinder_find"),
        SparkAction.GrooveActivity => Loc.T("os.wallet_action_groove"),
        SparkAction.EchoHosted => Loc.T("os.wallet_action_echo_host"),
        SparkAction.EchoJoined => Loc.T("os.wallet_action_echo_join"),
        SparkAction.StoreVisit => Loc.T("os.wallet_action_store_visit"),
        SparkAction.WalletVisit => Loc.T("os.wallet_action_wallet_visit"),
        SparkAction.AetherlingAdopt or SparkAction.AetherlingAttune => Loc.T("os.wallet_action_unknown_thing"),
        SparkAction.AdminAdjust => Loc.T("os.wallet_action_admin_adjust"),
        _ => Loc.T("os.wallet_action_unknown"),
    };

    internal static string FormatCountdown(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }
        return $"{span.Minutes}m";
    }

    private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

    private static void DrawCenteredHint(string text, float winW)
    {
        var wrapW = winW - Px(PadX) * 2.5f;
        var sz = ImGui.CalcTextSize(text, false, wrapW);
        ImGui.SetCursorPosX((winW - Math.Min(sz.X, wrapW)) * 0.5f);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
        ImGui.TextColored(UiColors.Hint, text);
        ImGui.PopTextWrapPos();
    }
}
