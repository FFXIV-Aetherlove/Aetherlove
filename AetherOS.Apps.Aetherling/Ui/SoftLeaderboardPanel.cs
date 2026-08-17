using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Os;
using AetherLove.Shared.Arcade;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>The minigames' leaderboard, in the app's own soft night style rather than the arcade's LCD:
/// a frosted card, two pill tabs, glowing rows. Same data and manners as the arcade widget (60 second
/// memo for success and failure alike, stale data kept over an error, own row pinned past the top 100),
/// reusing the central os.arcade_* strings so nothing new needs translating.</summary>
internal sealed class SoftLeaderboardPanel(IArcadeScores scores)
{
    private const double RefreshSeconds = 60;

    private sealed class BoardState
    {
        public ArcadeLeaderboardDto? Data;
        public volatile bool Loading;
        public bool Failed;
        public double FetchedAt;
    }

    private readonly Dictionary<(ArcadeGame, ArcadeBoard), BoardState> _boards = [];
    private ArcadeBoard _tab = ArcadeBoard.Weekly;

    /// <summary>Drops the memo for one game so the next draw fetches fresh, called right after a run
    /// submits so the player's new score shows without the minute-long wait.</summary>
    public void Invalidate(ArcadeGame game)
    {
        _boards.Remove((game, ArcadeBoard.Weekly));
        _boards.Remove((game, ArcadeBoard.AllTime));
    }

    public void Draw(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, ArcadeGame game)
    {
        var pad = ctx.Px(14f);
        var centreX = origin.X + (size.X * 0.5f);

        var tabY = origin.Y;
        var tabH = DrawTabs(ctx, dl, centreX, tabY);

        var state = Board(game, _tab);
        EnsureFetched(game, _tab, state);

        var listTop = tabY + tabH + ctx.Px(10f);
        var listH = origin.Y + size.Y - listTop;
        var rowH = ctx.Px(30f);

        if (state.Data is null || state.Data.Entries.Length == 0)
        {
            var key = state.Loading && state.Data is null
                ? "os.arcade_loading"
                : state.Failed && state.Data is null ? "os.arcade_error" : "os.arcade_empty";
            Look.CentredWrapped(dl, ctx.Localize(key), centreX, listTop + (listH * 0.35f),
                size.X - (pad * 2f), Look.U32(Look.Whisper), 1f);
            return;
        }

        var data = state.Data;
        var pinMine = data.MyRank is > 100 && data.MyScore is not null;
        var pinH = pinMine ? rowH + ctx.Px(6f) : 0f;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, listTop));
        using (var list = ImRaii.Child("##aetherlingLb", new Vector2(size.X, listH - pinH), false,
            ImGuiWindowFlags.NoBackground))
        {
            if (list)
            {
                var y = ImGui.GetCursorScreenPos().Y;
                var childDl = ImGui.GetWindowDrawList();
                foreach (var entry in data.Entries)
                {
                    DrawRow(ctx, childDl, entry, origin.X + pad, y, size.X - (pad * 2f), rowH);
                    y += rowH;
                }
                ImGui.Dummy(new Vector2(0f, data.Entries.Length * rowH));
            }
        }

        if (pinMine)
        {
            var mine = new ArcadeLeaderboardEntryDto(
                data.MyRank!.Value, ctx.Localize("os.arcade_you"), data.MyScore!.Value,
                data.MyScoreAtUtc ?? DateTimeOffset.UtcNow, IsMe: true);
            DrawRow(ctx, dl, mine, origin.X + pad, listTop + listH - rowH, size.X - (pad * 2f), rowH);
        }
    }

    private float DrawTabs(OsAppContext ctx, ImDrawListPtr dl, float centreX, float y)
    {
        var weekly = ctx.Localize("os.arcade_tab_weekly");
        var allTime = ctx.Localize("os.arcade_tab_alltime");
        var h = ImGui.GetTextLineHeight() + ctx.Px(12f);
        var wWeekly = ImGui.CalcTextSize(weekly).X + ctx.Px(28f);
        var wAll = ImGui.CalcTextSize(allTime).X + ctx.Px(28f);
        var gap = ctx.Px(8f);
        var left = centreX - ((wWeekly + gap + wAll) * 0.5f);

        DrawTab(ctx, dl, weekly, new Vector2(left, y), new Vector2(wWeekly, h), _tab == ArcadeBoard.Weekly,
            () => _tab = ArcadeBoard.Weekly);
        DrawTab(ctx, dl, allTime, new Vector2(left + wWeekly + gap, y), new Vector2(wAll, h),
            _tab == ArcadeBoard.AllTime, () => _tab = ArcadeBoard.AllTime);
        return h;
    }

    private static void DrawTab(OsAppContext ctx, ImDrawListPtr dl, string label, Vector2 tl, Vector2 size,
        bool active, Action select)
    {
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##lbTab{label}", size))
        {
            select();
        }
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var radius = size.Y * 0.5f;
        dl.AddRectFilled(tl, tl + size, Look.U32(active
            ? Look.Crystal with { W = 0.24f }
            : new Vector4(1f, 1f, 1f, hovered ? 0.10f : 0.055f)), radius);
        if (active)
        {
            dl.AddRect(tl, tl + size, Look.U32(Look.Crystal, 0.6f), radius, ImDrawFlags.None, 1.2f);
        }
        Look.Centred(dl, label, tl.X + (size.X * 0.5f), tl.Y + ctx.Px(6f),
            Look.U32(active ? Look.CrystalPale : Look.Whisper));
    }

    private static void DrawRow(OsAppContext ctx, ImDrawListPtr dl, ArcadeLeaderboardEntryDto entry,
        float x, float y, float width, float rowH)
    {
        if (entry.IsMe)
        {
            dl.AddRectFilled(new Vector2(x - ctx.Px(6f), y + ctx.Px(2f)),
                new Vector2(x + width + ctx.Px(6f), y + rowH - ctx.Px(2f)),
                Look.U32(Look.Crystal, 0.16f), ctx.Px(8f));
        }
        var textY = y + ((rowH - ImGui.GetTextLineHeight()) * 0.5f);
        var rankColour = entry.Rank switch
        {
            1 => Look.Spark,
            2 => Look.CrystalPale,
            3 => new Vector4(0.86f, 0.62f, 0.44f, 1f),
            _ => Look.Whisper,
        };
        dl.AddText(new Vector2(x, textY), Look.U32(rankColour), entry.Rank.ToString());
        var scoreText = entry.Score.ToString("N0");
        var scoreW = ImGui.CalcTextSize(scoreText).X;
        dl.AddText(new Vector2(x + width - scoreW, textY),
            Look.U32(entry.IsMe ? Look.CrystalPale : Look.Body), scoreText);

        var nameX = x + ctx.Px(34f);
        var nameLimit = width - ctx.Px(34f) - scoreW - ctx.Px(10f);
        dl.AddText(new Vector2(nameX, textY), Look.U32(entry.IsMe ? Look.CrystalPale : Look.Body),
            TruncateToWidth(entry.DisplayName, nameLimit));
    }

    private BoardState Board(ArcadeGame game, ArcadeBoard board)
    {
        if (!_boards.TryGetValue((game, board), out var state))
        {
            state = new BoardState();
            _boards[(game, board)] = state;
        }
        return state;
    }

    private void EnsureFetched(ArcadeGame game, ArcadeBoard board, BoardState state)
    {
        var now = ImGui.GetTime();
        if (state.Loading || (state.Data is not null && now - state.FetchedAt < RefreshSeconds)
            || (state.Failed && now - state.FetchedAt < RefreshSeconds))
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
}
