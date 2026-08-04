using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Hub;
using AetherLove.Services.Signal;
using AetherLove.Shared.Sparks;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Sparks;

/// <summary>Client-side tracker for the spark milestones only the phone can observe: distinct apps opened
/// today and Market/Realtor use. Reports fire once per day per milestone over the hub; the SERVER owns all
/// amounts and caps, so this class is purely a duplicate-call saver and its state can be lost harmlessly.</summary>
public sealed class SparkActivityReporter(
    AetherHubContext hub, AetherSignalService signal, Configuration config, IPluginLog log)
{
    private const int DistinctAppsMilestone = 3;
    private const int ArcadeGamesPerDay = 3;

    private readonly ConcurrentDictionary<SparkAction, bool> _inFlight = new();

    /// <summary>Called from the draw thread whenever an app becomes the foreground surface.</summary>
    public void NoteAppForeground(string appId)
    {
        RollDay();
        var state = config.Sparks;
        if (!state.AppsOpenedToday.Contains(appId))
        {
            state.AppsOpenedToday.Add(appId);
            config.Save();
        }

        if (!state.AppsMilestoneReported && state.AppsOpenedToday.Count >= DistinctAppsMilestone)
        {
            Report(SparkAction.OpenedThreeApps, () => config.Sparks.AppsMilestoneReported = true);
        }

        if (appId is "market" or "realtor")
        {
            NoteMarketActivity();
        }
        if (appId == "yapper" && !state.YapperCheckReported)
        {
            Report(SparkAction.YapperCheckFeed, () => config.Sparks.YapperCheckReported = true);
        }
    }

    /// <summary>Any qualifying Market/Realtor use; safe to call repeatedly.</summary>
    public void NoteMarketActivity()
    {
        RollDay();
        if (!config.Sparks.MarketActivityReported)
        {
            Report(SparkAction.MarketActivity, () => config.Sparks.MarketActivityReported = true);
        }
    }

    /// <summary>One finished arcade round. Stops reporting past the daily allowance so a long session does
    /// not hammer the hub; the server holds the real cap either way.</summary>
    public void NoteArcadeGameFinished()
    {
        RollDay();
        if (config.Sparks.ArcadeGamesReported >= ArcadeGamesPerDay)
        {
            return;
        }
        Report(SparkAction.ArcadeGame, () => config.Sparks.ArcadeGamesReported++);
    }

    private void RollDay()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var state = config.Sparks;
        if (state.ActivityDayUtc == today)
        {
            return;
        }
        state.ActivityDayUtc = today;
        state.AppsOpenedToday.Clear();
        state.AppsMilestoneReported = false;
        state.MarketActivityReported = false;
        state.ArcadeGamesReported = 0;
        state.YapperCheckReported = false;
        config.Save();
    }

    /// <summary>Fire-and-forget report, at most one in flight per action so a burst of signals collapses
    /// to a single call. The stamp is only written on a successful round trip, so an offline moment
    /// retries on the next signal instead of losing the day.</summary>
    private void Report(SparkAction action, Action markReported)
    {
        if (!signal.IsConnected || !_inFlight.TryAdd(action, true))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await hub.ReportSparkActivityAsync((short)action).ConfigureAwait(false);
                markReported();
            }
            catch (Exception ex)
            {
                log.Debug(ex, "[Sparks] Activity report for {Action} failed; will retry on the next signal.", action);
            }
            finally
            {
                _inFlight.TryRemove(action, out _);
            }
        });
    }
}
