using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Apps.Timers.Schedule;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Timers;

/// <summary>The Coming up card: venue RSVPs from the host plus the weekly spark reset.</summary>
public sealed partial class TimersApp
{
    private const double CommitmentsMemoMinutes = 5.0;

    private static readonly Vector4 SparkGold = new(0.95f, 0.71f, 0.24f, 1f);

    private volatile IReadOnlyList<TimersCommitment> _commitments = [];
    private DateTime _commitmentsFetchedUtc = DateTime.MinValue;
    private TimerRow[] _comingRows = [];
    private bool _comingHasCommitments;

    private void MaybeRefreshCommitments(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _commitmentsFetchedUtc).TotalMinutes < CommitmentsMemoMinutes)
        {
            return;
        }
        _commitmentsFetchedUtc = now;
        _ = Task.Run(async () =>
        {
            try
            {
                _commitments = await _host.GetCommitmentsAsync().ConfigureAwait(false);
                BumpData();
            }
            catch (Exception)
            {
            }
        });
    }

    private void BuildComingRows(DateTime utcNow)
    {
        var rows = new List<TimerRow>();
        var commitments = _commitments;
        _comingHasCommitments = false;
        foreach (var commitment in commitments)
        {
            if (commitment.WhenUtc < utcNow.AddHours(-1))
            {
                continue;
            }
            _comingHasCommitments = true;
            rows.Add(new TimerRow(FontAwesomeIcon.MapMarkerAlt, new Vector4(1f, 1f, 1f, 0.9f),
                commitment.Name,
                ToLocal(commitment.WhenUtc).ToString("ddd d MMM HH:mm", _culture),
                FormatCountdown(commitment.WhenUtc - utcNow), UiColors.Body,
                $"##calcmt{commitment.VenueId:N}", commitment.Name, ToUnix(commitment.WhenUtc)));
        }

        var sparkReset = EorzeaSchedule.NextWeeklyReset(utcNow);
        rows.Add(new TimerRow(FontAwesomeIcon.Bolt, SparkGold,
            Loc.T("os.timers_spark_reset"),
            ToLocal(sparkReset).ToString("ddd d MMM HH:mm", _culture),
            FormatCountdown(sparkReset - utcNow), UiColors.Body,
            "##calspark", Loc.T("os.timers_spark_reset"), ToUnix(sparkReset)));

        rows.Sort((a, b) => a.CalUnix.CompareTo(b.CalUnix));
        _comingRows = rows.ToArray();
    }

    private void DrawComingUpCard(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var winW = ImGui.GetWindowSize().X;
        var rowH = Px(RowHeight);
        // With nothing but the spark reset in it, the card is named after the one thing it holds. "Coming up"
        // promises imminence it cannot keep for something a work week away, and the empty-plans line under it
        // was answering a question the card had only raised by claiming to be a plans list.
        var title = Loc.T(_comingHasCommitments ? "os.timers_coming_title" : "os.timers_spark_title");
        var cardTL = BeginCard(dl, winW, _comingRows.Length * rowH, title,
            out var cardW, out var cardH, out var y);

        for (var i = 0; i < _comingRows.Length; i++)
        {
            if (i > 0)
            {
                DrawHairline(dl, cardTL.X, y, cardW);
            }
            DrawTimerRow(dl, new Vector2(cardTL.X, y), cardW, rowH, in _comingRows[i]);
            y += rowH;
        }

        EndCard(cardTL, cardW, cardH);
    }
}
