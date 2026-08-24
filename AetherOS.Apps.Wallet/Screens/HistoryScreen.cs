using System;
using System.Collections.Generic;
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

/// <summary>The earnings history page: the keyset ledger, grouped by day, newest first, paging in as the
/// list is scrolled.</summary>
internal sealed class HistoryScreen
{
    private const float PadX = 16f;
    private const int PageSize = 30;

    private readonly IWalletHost _host;
    private readonly Action _back;
    private readonly EntranceAnimation _entrance = new();

    private volatile IReadOnlyList<SparkLedgerEntryDto>? _lines;
    private long? _cursor;
    private volatile bool _endReached;
    private volatile bool _loading;
    private volatile bool _loadingMore;
    private volatile bool _loadedOnce;
    private int _generation;

    public HistoryScreen(IWalletHost host, Action back)
    {
        _host = host;
        _back = back;
    }

    public void Show()
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
            var page = await _host.GetSparkLedgerAsync(null, PageSize).ConfigureAwait(false);
            if (generation != _generation)
            {
                return;
            }
            if (page is not null)
            {
                _lines = page.Lines;
                _cursor = page.NextBeforeSequence;
                _endReached = page.NextBeforeSequence is null;
                _loadedOnce = true;
            }
            _loading = false;
        });
    }

    private void LoadMore()
    {
        if (_loadingMore || _endReached || _cursor is not { } cursor)
        {
            return;
        }
        _loadingMore = true;
        var generation = _generation;
        _ = Task.Run(async () =>
        {
            var page = await _host.GetSparkLedgerAsync(cursor, PageSize).ConfigureAwait(false);
            if (generation != _generation)
            {
                return;
            }
            if (page is not null)
            {
                var merged = new List<SparkLedgerEntryDto>(_lines ?? []);
                merged.AddRange(page.Lines);
                _lines = merged;
                _cursor = page.NextBeforeSequence;
                _endReached = page.NextBeforeSequence is null;
            }
            _loadingMore = false;
        });
    }

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;

        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("os.wallet_back_sparks"), FontAwesomeIcon.Bolt))
        {
            _back();
        }
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        var lines = _lines;
        if (lines is null || lines.Count == 0)
        {
            if (_loading && !_loadedOnce)
            {
                ImGui.Dummy(new Vector2(0f, Px(50f)));
                var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(16f));
                LoadingSpinner.Draw(center, Px(14f), Px(3f), ImGui.GetColorU32(ThemeService.Current.Accent));
                ImGui.Dummy(new Vector2(0f, Px(40f)));
            }
            else
            {
                ImGui.Dummy(new Vector2(0f, Px(40f)));
                DrawCenteredHint(Loc.T("os.wallet_history_empty"), winW);
            }
            _entrance.EndFrame();
            return;
        }

        DateTime? currentDay = null;
        foreach (var line in lines)
        {
            var day = line.AtUtc.ToLocalTime().Date;
            if (day != currentDay)
            {
                currentDay = day;
                DrawDayHeader(ctx, day);
            }
            DrawRow(ctx, line, winW);
        }

        if (_loadingMore)
        {
            ImGui.Dummy(new Vector2(0f, Px(8f)));
            var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f, ImGui.GetCursorScreenPos().Y + Px(10f));
            LoadingSpinner.Draw(center, Px(10f), Px(2.5f), ImGui.GetColorU32(ThemeService.Current.Accent));
            ImGui.Dummy(new Vector2(0f, Px(24f)));
        }

        if (!_endReached && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Px(300f))
        {
            LoadMore();
        }

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        _entrance.EndFrame();
    }

    private static void DrawDayHeader(OsAppContext ctx, DateTime day)
    {
        var today = DateTime.Now.Date;
        var label = day == today
            ? Loc.T("os.wallet_history_today")
            : day == today.AddDays(-1)
                ? Loc.T("os.wallet_history_yesterday")
                : day.ToString("d MMMM yyyy", ctx.Culture);
        ImGui.Dummy(new Vector2(0f, Px(6f)));
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(UiColors.Hint, label);
        ImGui.Dummy(new Vector2(0f, Px(2f)));
    }

    private static void DrawRow(OsAppContext ctx, SparkLedgerEntryDto line, float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var hasContext = line.Context is { Length: > 0 }
            && line.Kind is SparkTransactionKind.Adjustment or SparkTransactionKind.Clawback or SparkTransactionKind.Prize;
        var lineH = ImGui.GetTextLineHeight();
        var cardH = hasContext ? Px(20f) + lineH * 3f : Px(16f) + lineH * 2f;
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), OsDrawShared.White(0.04f), Px(12f));

        var positive = line.Amount >= 0;
        var glyph = line.Kind switch
        {
            SparkTransactionKind.Adjustment => FontAwesomeIcon.Wrench,
            SparkTransactionKind.Clawback => FontAwesomeIcon.ArrowDown,
            SparkTransactionKind.Spend => FontAwesomeIcon.ArrowDown,
            _ => FontAwesomeIcon.ArrowUp,
        };
        var glyphColor = positive ? ImGui.GetColorU32(t.AccentLight) : ImGui.GetColorU32(UiColors.Danger);
        var chipR = Px(13f);
        var chipC = new Vector2(tl.X + Px(10f) + chipR, tl.Y + cardH * 0.5f);
        dl.AddCircleFilled(chipC, chipR, (glyphColor & 0x00FFFFFFu) | 0x28000000u);
        IconDraw.AddCentered(dl, glyph, Px(11f), chipC, glyphColor);

        var amount = (positive ? "+" : "") + line.Amount.ToString("N0", ctx.Culture);
        var amountSz = ImGui.CalcTextSize(amount);
        dl.AddText(new Vector2(tl.X + cardW - amountSz.X - Px(12f), tl.Y + Px(8f)),
            positive ? ImGui.GetColorU32(t.AccentLight) : ImGui.GetColorU32(UiColors.Danger), amount);

        var balance = Loc.T("os.wallet_history_balance", line.BalanceAfter.ToString("N0", ctx.Culture));
        var balanceSz = ImGui.CalcTextSize(balance);
        dl.AddText(new Vector2(tl.X + cardW - balanceSz.X - Px(12f), tl.Y + Px(10f) + lineH),
            ImGui.GetColorU32(UiColors.Hint), balance);

        var textX = chipC.X + chipR + Px(10f);
        var label = line.Kind switch
        {
            SparkTransactionKind.Spend => Loc.T("os.wallet_kind_spend"),
            SparkTransactionKind.Clawback => Loc.T("os.wallet_kind_clawback"),
            _ => SparksScreen.ActionLabel(line.Action),
        };
        dl.AddText(new Vector2(textX, tl.Y + Px(8f)), ImGui.GetColorU32(UiColors.Body), label);
        dl.AddText(new Vector2(textX, tl.Y + Px(10f) + lineH), ImGui.GetColorU32(UiColors.Hint),
            line.AtUtc.ToLocalTime().ToString("t", ctx.Culture));
        if (hasContext)
        {
            dl.AddText(new Vector2(textX, tl.Y + Px(12f) + lineH * 2f), ImGui.GetColorU32(UiColors.Hint),
                line.Context);
        }

        ImGui.Dummy(new Vector2(0f, cardH + Px(5f)));
    }

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
