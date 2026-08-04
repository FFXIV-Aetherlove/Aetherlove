using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Os;
using AetherLove.Services.Localization;
using AetherLove.Shared.Arcade;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Widgets;

/// <summary>The shared arcade leaderboard screen, drawn in the RetroLcd style every game already uses:
/// weekly / all-time tabs, the top 100 (display name, score, date), the caller's own row highlighted, and
/// a pinned "you" row when the caller places outside the top 100.</summary>
public sealed class ArcadeLeaderboardView(IArcadeScores scores, ArcadeGame game)
{
    private const double RefreshSeconds = 60;

    private sealed class BoardState
    {
        public ArcadeLeaderboardDto? Data;
        public bool Loading;
        public bool Failed;
        public double FetchedAt;
    }

    private readonly BoardState[] boards = [new(), new()];
    private ArcadeBoard tab = ArcadeBoard.Weekly;

    public void Draw(OsAppContext ctx, Vector2 winPos, Vector2 winSize, Action onBack)
    {
        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize("os.arcade_leaderboard");
        using (ctx.TitleFont?.Push())
        {
            var titleSize = ImGui.CalcTextSize(title);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                winPos + new Vector2((winSize.X - titleSize.X) * 0.5f, winSize.Y * 0.055f),
                ImGui.GetColorU32(RetroLcd.Pixel), title);
        }

        var tabW = winSize.X * 0.38f;
        var tabH = ctx.Px(30f);
        var tabsY = winSize.Y * 0.135f;
        var gap = ctx.Px(8f);
        var tabsX = (winSize.X - (tabW * 2f) - gap) * 0.5f;
        if (RetroLcd.Button("##lbWeekly", ctx.Localize("os.arcade_tab_weekly"),
            winPos + new Vector2(tabsX, tabsY), new Vector2(tabW, tabH), ctx.Px(4f), filled: this.tab == ArcadeBoard.Weekly))
        {
            this.tab = ArcadeBoard.Weekly;
        }
        if (RetroLcd.Button("##lbAllTime", ctx.Localize("os.arcade_tab_alltime"),
            winPos + new Vector2(tabsX + tabW + gap, tabsY), new Vector2(tabW, tabH), ctx.Px(4f), filled: this.tab == ArcadeBoard.AllTime))
        {
            this.tab = ArcadeBoard.AllTime;
        }

        var state = this.boards[(int)this.tab];
        EnsureFetched(state, this.tab);

        var backH = ctx.Px(38f);
        var backY = winSize.Y - backH - ctx.Px(20f);
        var listTop = tabsY + tabH + ctx.Px(12f);
        var padX = ctx.Px(16f);

        var rowH = ImGui.GetTextLineHeight() * 2.55f;
        var myOutside = state.Data is { MyRank: > 100, MyScore: not null };
        var pinnedH = myOutside ? rowH + ctx.Px(4f) : 0f;
        var listH = backY - listTop - ctx.Px(8f) - pinnedH;

        DrawList(ctx, state, winPos, winSize, listTop, listH, padX, rowH);

        if (myOutside)
        {
            DrawRow(ctx, dl,
                winPos + new Vector2(padX, listTop + listH + ctx.Px(4f)),
                winSize.X - (padX * 2f), rowH,
                state.Data!.MyRank!.Value, ctx.Localize("os.arcade_you"), state.Data.MyScore!.Value,
                state.Data.MyScoreAtUtc, highlight: true, zebra: false);
        }

        var backW = winSize.X * 0.62f;
        if (RetroLcd.Button("##lbBack", ctx.Localize("os.arcade_back"),
            winPos + new Vector2((winSize.X - backW) * 0.5f, backY), new Vector2(backW, backH),
            ctx.Px(4f), filled: false))
        {
            onBack();
        }
    }

    private void EnsureFetched(BoardState state, ArcadeBoard board)
    {
        var now = ImGui.GetTime();
        if (state.Loading || (state.Data is not null && now - state.FetchedAt < RefreshSeconds) || (state.Failed && now - state.FetchedAt < RefreshSeconds))
        {
            return;
        }
        state.Loading = true;
        _ = Task.Run(async () =>
        {
            var data = await scores.GetLeaderboardAsync(game, board).ConfigureAwait(false);
            state.Data = data ?? state.Data;
            state.Failed = data is null;
            state.FetchedAt = ImGui.GetTime();
            state.Loading = false;
        });
    }

    private void DrawList(OsAppContext ctx, BoardState state, Vector2 winPos, Vector2 winSize, float listTop, float listH, float padX, float rowH)
    {
        var dl = ImGui.GetWindowDrawList();
        if (state.Data is null || state.Data.Entries.Length == 0)
        {
            var message = state.Loading && state.Data is null
                ? ctx.Localize("os.arcade_loading")
                : state.Failed && state.Data is null
                    ? ctx.Localize("os.arcade_error")
                    : ctx.Localize("os.arcade_empty");
            var size = ImGui.CalcTextSize(message);
            dl.AddText(winPos + new Vector2((winSize.X - size.X) * 0.5f, listTop + listH * 0.35f),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.7f }), message);
            return;
        }

        ImGui.SetCursorScreenPos(winPos + new Vector2(padX, listTop));
        using var child = ImRaii.Child("##lbList", new Vector2(winSize.X - (padX * 2f), listH), false,
            ImGuiWindowFlags.NoBackground);
        if (!child)
        {
            return;
        }

        var rowW = ImGui.GetContentRegionAvail().X;
        var childDl = ImGui.GetWindowDrawList();
        for (var i = 0; i < state.Data.Entries.Length; i++)
        {
            var entry = state.Data.Entries[i];
            var pos = ImGui.GetCursorScreenPos();
            DrawRow(ctx, childDl, pos, rowW, rowH, entry.Rank, entry.DisplayName, entry.Score,
                entry.AchievedAtUtc, entry.IsMe, zebra: i % 2 == 1);
            ImGui.Dummy(new Vector2(rowW, rowH));
        }
    }

    private static void DrawRow(OsAppContext ctx, ImDrawListPtr dl, Vector2 pos, float width, float rowH,
        int rank, string name, int score, DateTimeOffset? at, bool highlight, bool zebra)
    {
        var lineH = ImGui.GetTextLineHeight();
        var padX = ctx.Px(8f);
        var gap = ctx.Px(3f);
        if (highlight || zebra)
        {
            dl.AddRectFilled(pos, pos + new Vector2(width, rowH - gap),
                ImGui.GetColorU32(RetroLcd.Pixel with { W = highlight ? 0.18f : 0.06f }), ctx.Px(4f));
        }
        var color = ImGui.GetColorU32(RetroLcd.Pixel with { W = highlight ? 1f : 0.85f });
        var faded = ImGui.GetColorU32(RetroLcd.Pixel with { W = 0.55f });

        var textY = pos.Y + ((rowH - gap - (lineH * 2.05f)) * 0.5f);
        var rankW = ImGui.CalcTextSize("000.").X;
        dl.AddText(new Vector2(pos.X + padX, textY), color, $"{rank}.");

        var scoreLabel = score.ToString("N0");
        var scoreW = ImGui.CalcTextSize(scoreLabel).X;
        dl.AddText(new Vector2(pos.X + width - padX - scoreW, textY), color, scoreLabel);

        var maxNameW = width - (padX * 2f) - rankW - scoreW - ctx.Px(10f);
        var shown = name;
        while (shown.Length > 1 && ImGui.CalcTextSize(shown).X > maxNameW)
        {
            shown = shown[..^1];
        }
        dl.AddText(new Vector2(pos.X + padX + rankW, textY), color, shown);

        if (at is not null)
        {
            // The phone's language decides the date format, never the system culture.
            dl.AddText(new Vector2(pos.X + padX + rankW, textY + (lineH * 1.05f)), faded,
                at.Value.ToLocalTime().ToString("d MMM yyyy", LanguageProvider.CurrentCulture));
        }
    }
}
