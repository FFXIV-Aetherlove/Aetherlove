using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Hub;
using AetherLove.Shared.Profile.Enums;
using Dalamud.Game.ClientState.Conditions;

namespace AetherLove.Services;

/// <summary>Surfaces a server-provided presence line in the game chat after a long stretch of plugin
/// inactivity, only while the player is in a safe state. The schedule is client-owned and persisted; the
/// server only supplies the line when asked.</summary>
public sealed class PulseService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan MinWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxWindow = TimeSpan.FromHours(36);
    private static readonly TimeSpan SessionWarmup = TimeSpan.FromMinutes(30);

    private readonly Configuration _config;
    private readonly AetherLoveHubClient _hub;
    private readonly NotificationDispatcher _notifier;

    private CancellationTokenSource? _cts;
    private DateTimeOffset _sessionStartUtc;
    private bool _activityDirty;

    public PulseService(Configuration config, AetherLoveHubClient hub, NotificationDispatcher notifier)
    {
        _config = config;
        _hub = hub;
        _notifier = notifier;
    }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }
        _cts = new CancellationTokenSource();
        _sessionStartUtc = DateTimeOffset.UtcNow;
        Plugin.ClientState.Login += OnLogin;
        _ = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        Plugin.ClientState.Login -= OnLogin;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        Flush();
    }

    public void Dispose() => Stop();

    private void OnLogin() => _sessionStartUtc = DateTimeOffset.UtcNow;

    /// <summary>Records a plugin interaction and pushes the next eligible time out by a random 24–36 h.
    /// Held in memory until the next flush so frequent calls (e.g. swipes) don't hit disk.</summary>
    public void MarkActivity()
    {
        var now = DateTimeOffset.UtcNow;
        _config.Pulse.LastActivityUtc = now;
        _config.Pulse.NextEligibleUtc = now + RandomWindow();
        _activityDirty = true;
    }

    private static TimeSpan RandomWindow()
    {
        var spreadTicks = (MaxWindow - MinWindow).Ticks;
        return MinWindow + TimeSpan.FromTicks((long)(Random.Shared.NextDouble() * spreadTicks));
    }

    private void Flush()
    {
        if (_activityDirty)
        {
            _activityDirty = false;
            _config.Save();
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await TickAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[PulseService] tick failed.");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        Flush();

        var pulse = _config.Pulse;
        if (pulse.MutePulse || pulse.NextEligibleUtc is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < pulse.NextEligibleUtc.Value || now - _sessionStartUtc < SessionWarmup)
        {
            return;
        }

        var safe = await Plugin.Framework.RunOnFrameworkThread(IsSafeNow).ConfigureAwait(false);
        if (!safe)
        {
            return;
        }

        await FireAsync(ct).ConfigureAwait(false);
    }

    private static bool IsSafeNow()
    {
        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer is null)
        {
            return false;
        }

        var c = Plugin.Condition;
        return !c[ConditionFlag.InCombat]
            && !c[ConditionFlag.BetweenAreas]
            && !c[ConditionFlag.BetweenAreas51]
            && !c[ConditionFlag.WatchingCutscene]
            && !c[ConditionFlag.WatchingCutscene78]
            && !c[ConditionFlag.OccupiedInCutSceneEvent]
            && !c[ConditionFlag.OccupiedInQuestEvent]
            && !c[ConditionFlag.BoundByDuty];
    }

    private async Task FireAsync(CancellationToken ct)
    {
        var lang = ResolveLanguage(_config.PluginLanguage);
        var pulse = await _hub.GetPulseAsync(lang, ct).ConfigureAwait(false);

        // Reschedule either way so an empty pool doesn't re-hit the hub every tick.
        _config.Pulse.NextEligibleUtc = DateTimeOffset.UtcNow + RandomWindow();

        if (pulse is null || string.IsNullOrWhiteSpace(pulse.Text))
        {
            _config.Save();
            return;
        }

        await Plugin.Framework.RunOnFrameworkThread(() => _notifier.PrintPulse(pulse.Text)).ConfigureAwait(false);
        _config.Pulse.SeenPulse = true;
        _config.Save();
    }

    private static Language ResolveLanguage(string pluginLanguage) =>
        Enum.TryParse<Language>(pluginLanguage, ignoreCase: true, out var lang) ? lang : Language.English;
}
