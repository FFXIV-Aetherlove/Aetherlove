using System;
using System.Linq;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Shared.Sparks;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Wallet;

/// <summary>The ways-to-earn page: what sparks are, the catalog grouped by pool with each pool explained,
/// and how the weekly ceilings interact. Static content, so it renders straight off the wallet snapshot the
/// Sparks tab already holds.</summary>
internal sealed class EarnScreen
{
    private const float PadX = 16f;

    private readonly Action _back;
    private readonly EntranceAnimation _entrance = new();

    private SparkWalletDto? _wallet;

    public EarnScreen(Action back)
    {
        _back = back;
    }

    /// <summary>Set while the page was reached from another app, so the back pill names that app instead
    /// of the sparks tab it would normally return to.</summary>
    public string? BackTooltipOverride { get; set; }

    public FontAwesomeIcon BackIconOverride { get; set; } = FontAwesomeIcon.Bolt;

    /// <summary>The snapshot on show, so the host can tell a landed refetch from the one already drawn.</summary>
    public SparkWalletDto? Wallet => _wallet;

    public void Show(SparkWalletDto? wallet)
    {
        _wallet = wallet;
        _entrance.Arm();
    }

    /// <summary>Fills in the snapshot for a page opened by intent before the wallet had loaded.</summary>
    public void SetWallet(SparkWalletDto wallet) => _wallet = wallet;

    public void Draw(OsAppContext ctx)
    {
        _entrance.BeginFrame();
        var winW = ImGui.GetWindowSize().X;

        ImGui.Dummy(new Vector2(0f, Px(4f)));
        ImGui.SetCursorPosX(Px(PadX));
        if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(),
                BackTooltipOverride ?? Loc.T("os.wallet_back_sparks"), BackIconOverride))
        {
            _back();
        }
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        var wallet = _wallet;
        if (wallet is null || wallet.Catalog.Length == 0)
        {
            DrawParagraph(Loc.T("os.wallet_earning_paused"), winW, UiColors.Hint);
            _entrance.EndFrame();
            return;
        }

        DrawParagraph(Loc.T("os.wallet_earn_intro"), winW, UiColors.Body);
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        DrawPool(ctx, wallet, winW, SparkPool.Routine, "os.wallet_earn_pool_routine", "os.wallet_earn_help_routine");
        DrawPool(ctx, wallet, winW, SparkPool.Exempt, "os.wallet_earn_pool_exempt", "os.wallet_earn_help_exempt");
        DrawPool(ctx, wallet, winW, SparkPool.Bonus, "os.wallet_earn_pool_bonus", "os.wallet_earn_help_bonus");

        DrawCapsNote(ctx, wallet, winW);

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        _entrance.EndFrame();
    }

    private void DrawPool(OsAppContext ctx, SparkWalletDto wallet, float winW, SparkPool pool, string headingKey,
        string helpKey)
    {
        var entries = wallet.Catalog
            .Where(e => e.Pool == pool)
            .OrderByDescending(e => e.Amount)
            .ThenBy(e => (short)e.Action)
            .ToArray();
        if (entries.Length == 0)
        {
            return;
        }

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(UiColors.Body, Loc.T(headingKey));
        }
        DrawParagraph(Loc.T(helpKey), winW, UiColors.Hint);

        // The daily pool is the one that comes back tomorrow, so it is the only one where a countdown to
        // midnight means anything. The weekly reset already has its own line down in the ceilings block.
        if (pool == SparkPool.Routine)
        {
            var untilMidnight = NextUtcMidnight() - DateTimeOffset.UtcNow;
            ImGui.SetCursorPosX(Px(PadX));
            ImGui.TextColored(ThemeService.Current.AccentLight,
                Loc.T("os.wallet_earn_daily_reset", SparksScreen.FormatCountdown(untilMidnight)));
        }
        ImGui.Dummy(new Vector2(0f, Px(4f)));

        foreach (var entry in entries)
        {
            DrawEarnRow(ctx, entry, winW);
        }
    }

    /// <summary>When the per-day counts start again. UTC midnight, because that is the boundary the server
    /// counts a day's grants against; anything local would promise a reset that does not happen.</summary>
    private static DateTimeOffset NextUtcMidnight() =>
        new(DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);

    private static void DrawEarnRow(OsAppContext ctx, SparkCatalogEntryDto entry, float winW)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var cardW = winW - Px(PadX) * 2f;
        var cardH = Px(52f);
        ImGui.SetCursorPosX(Px(PadX));
        var tl = ImGui.GetCursorScreenPos();
        var done = entry.Exhausted;

        // The row is a hover target and nothing else: the tick needs somewhere to explain itself, and a
        // page of read-only facts should not start opening things when it is clicked.
        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton($"##earn{(short)entry.Action}", new Vector2(cardW, cardH));
        if (done && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T(entry.WeekSpent ? "os.wallet_earn_done_week" : "os.wallet_earn_done_today"));
        }

        dl.AddRectFilled(tl, tl + new Vector2(cardW, cardH), OsDrawShared.White(done ? 0.03f : 0.05f), Px(14f));

        var chipR = Px(16f);
        var chipC = new Vector2(tl.X + Px(12f) + chipR, tl.Y + cardH * 0.5f);
        var tick = UiColors.Success;
        dl.AddCircleFilled(chipC, chipR,
            ImGui.GetColorU32(done ? tick with { W = 0.18f } : t.Accent with { W = 0.16f }));
        IconDraw.AddCentered(dl, done ? FontAwesomeIcon.Check : SparksScreen.ActionIcon(entry.Action),
            Px(14f), chipC, ImGui.GetColorU32(done ? tick : t.AccentLight));

        var amount = Loc.T("os.wallet_earn_amount", entry.Amount);
        using (UiFonts.H3?.Push())
        {
            var amountSz = ImGui.CalcTextSize(amount);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                new Vector2(tl.X + cardW - amountSz.X - Px(14f), tl.Y + (cardH - amountSz.Y) * 0.5f),
                ImGui.GetColorU32(done ? UiColors.Hint : t.AccentLight), amount);
        }

        var textX = chipC.X + chipR + Px(12f);
        dl.AddText(new Vector2(textX, tl.Y + Px(8f)),
            ImGui.GetColorU32(done ? UiColors.Hint : UiColors.Body), EarnLabel(ctx, entry.Action));
        dl.AddText(new Vector2(textX, tl.Y + Px(10f) + ImGui.GetTextLineHeight()),
            ImGui.GetColorU32(UiColors.Hint), FrequencyLine(entry));

        ImGui.SetCursorScreenPos(tl);
        ImGui.Dummy(new Vector2(0f, cardH + Px(6f)));
    }

    /// <summary>The row's title. Everything reads from the shared ledger table except the companion's own
    /// row, which the ledger has to keep as question marks (it renders for accounts that never hatched
    /// anything) and this page does not: the wallet only ever receives that entry once the pet is grown, so
    /// here it can say what it is and use the creature's name, taken from the app registry's own tile
    /// name rather than by reaching into another app.</summary>
    private static string EarnLabel(OsAppContext ctx, SparkAction action)
    {
        if (action != SparkAction.AetherlingGame)
        {
            return SparksScreen.ActionLabel(action);
        }
        var name = ctx.Shell.Apps.FirstOrDefault(a => a.Id == "aetherling")?.Name;
        return string.IsNullOrWhiteSpace(name) || name == "???"
            ? Loc.T("os.wallet_action_aetherling_game_plain")
            : Loc.T("os.wallet_action_aetherling_game", name);
    }

    /// <summary>The ceiling block: the two weekly numbers, spelled out, plus the reset time. This is the
    /// question the ring on the Sparks tab raises and cannot answer in a legend.</summary>
    private static void DrawCapsNote(OsAppContext ctx, SparkWalletDto wallet, float winW)
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(Px(PadX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(UiColors.Body, Loc.T("os.wallet_earn_caps_title"));
        }
        DrawParagraph(
            Loc.T("os.wallet_earn_caps_body",
                wallet.RoutineWeeklyCap.ToString("N0", ctx.Culture),
                wallet.TotalWeeklyCap.ToString("N0", ctx.Culture)),
            winW, UiColors.Hint);

        var untilReset = wallet.WeekResetsAtUtc - DateTimeOffset.UtcNow;
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.TextColored(ThemeService.Current.AccentLight,
            Loc.T("os.wallet_resets_in",
                SparksScreen.FormatCountdown(untilReset < TimeSpan.Zero ? TimeSpan.Zero : untilReset)));
    }

    private static string FrequencyLine(SparkCatalogEntryDto entry)
    {
        if (entry.MaxPerDay == 1)
        {
            return Loc.T("os.wallet_freq_once_daily");
        }
        if (entry.MaxPerDay is { } perDay)
        {
            return Loc.T("os.wallet_freq_per_day", perDay);
        }
        if (entry.MaxPointsPerWeek is { } perWeek)
        {
            return Loc.T("os.wallet_freq_week_points", perWeek);
        }
        return Loc.T("os.wallet_freq_unlimited");
    }

    private static void DrawParagraph(string text, float winW, Vector4 color)
    {
        ImGui.SetCursorPosX(Px(PadX));
        ImGui.PushTextWrapPos(winW - Px(PadX));
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }
}
