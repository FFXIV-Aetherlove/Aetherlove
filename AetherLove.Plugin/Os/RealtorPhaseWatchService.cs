using System;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Realtor;
using AetherOS.Apps.Realtor;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AetherLove.Os;

/// <summary>Announces the housing lottery flipping between taking entries and publishing results.
///
/// The cycle is global and re-derivable offline (see <see cref="LotteryClock"/>), so this needs no network
/// and no open app: it re-checks on a slow timer and on login, and compares against the phase it last told
/// the player about. The very first run only records the phase, so installing the plugin mid-cycle never
/// opens with a stale announcement.</summary>
public sealed class RealtorPhaseWatchService : IRealtorAlerts, IDisposable
{
    private const string LastPhaseKey = "notifiedPhase";
    private const string NotificationTag = "realtor:phase";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _services;
    private readonly AppStorageService _storage;
    private readonly NotificationDispatcher _notifier;
    private DateTime _nextCheckUtc = DateTime.MinValue;

    public RealtorPhaseWatchService(IServiceProvider services, AppStorageService storage,
        NotificationDispatcher notifier)
    {
        _services = services;
        _storage = storage;
        _notifier = notifier;
        Plugin.Framework.Update += OnUpdate;
        Plugin.ClientState.Login += OnLogin;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        Plugin.ClientState.Login -= OnLogin;
    }

    /// <summary>Clears the announcement once the player has actually looked at the app.</summary>
    public void ClearNotifications()
    {
        try
        {
            _services.GetService<AetherOS.Sdk.IOsShell>()?.DismissByTag(NotificationTag);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[Realtor] Clearing the phase notification failed: {ex.Message}");
        }
    }

    private void OnLogin() => _nextCheckUtc = DateTime.MinValue;

    private void OnUpdate(IFramework framework)
    {
        if (!PhonePower.IsOn)
        {
            return;
        }
        if (DateTime.UtcNow < _nextCheckUtc)
        {
            return;
        }
        _nextCheckUtc = DateTime.UtcNow + CheckEvery;
        try
        {
            Check();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[Realtor] Phase check failed: {ex.Message}");
        }
    }

    private void Check()
    {
        var store = _storage.For("realtor");
        if (new LotteryClock(store).Current is not { } now)
        {
            return;
        }

        // Tracked before the settings gate: the entry announcement below is not the generic phase
        // chatter and must still fire for someone who muted that, so the cursor has to keep moving.
        var last = store.Get<int?>(LastPhaseKey);
        store.Set<int?>(LastPhaseKey, now.Phase);
        if (last is null || last == now.Phase)
        {
            return;
        }

        if (now.Phase == PaissaLottoPhase.Results && AnnounceEntryResolved())
        {
            return;
        }
        if (!new RealtorSettings(store).NotifyPhase)
        {
            return;
        }

        Announce(Loc.T(now.Phase == PaissaLottoPhase.Accepting
            ? "notif.realtor_accepting"
            : "notif.realtor_results"));
    }

    /// <summary>The results are out on a plot the player actually entered for, so it names the plot and
    /// tells them to go and look. The entry is dropped in the same breath: it has resolved either way, and
    /// a bid left behind would be shown as live again when the next cycle opens.</summary>
    private bool AnnounceEntryResolved()
    {
        var watch = _services.GetService<IHousingLotteryWatch>();
        if (watch?.Current is not { } entry)
        {
            return false;
        }
        Announce(Loc.T("notif.realtor_entry_results", entry.Plot, entry.Ward, entry.District));
        watch.Clear();
        return true;
    }

    private void Announce(string text)
    {
        _notifier.NotifyRealtorPhase(text);
        try
        {
            _services.GetService<AetherOS.Sdk.IOsShell>()?.PostNotification(
                "realtor", Loc.T("os.app_realtor"), text,
                onTap: () => _services.GetService<AetherOS.Sdk.IOsShell>()?.OpenApp("realtor"),
                tag: NotificationTag);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Realtor] Phase OS notification failed.");
        }
    }
}
