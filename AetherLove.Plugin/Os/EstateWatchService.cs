using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherOS.Apps.Realtor;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.DependencyInjection;

namespace AetherLove.Os;

/// <summary>Warns a player before the game demolishes a private estate they have stopped visiting.
///
/// The game only ever shows the countdown inside its Timers window, which the player has to open, and nothing
/// on the client exposes it as data. So this tracks the thing the countdown is derived from instead: the
/// visit. HousingManager reports both the character's own private estate and the house it is currently
/// standing in, so a visit is a comparison of the two, which means another character's house can never reset
/// this character's clock. Read-only, no hooks or signatures; every game read degrades to no capture.
///
/// What it can and cannot know, which is why the wording everywhere is "days away from home" rather than the
/// game's countdown: absence is measured from what THIS install saw, so a second PC or a fresh install reads
/// as a longer absence than the truth (erring towards warning, which is the right direction), and the
/// server-side demolition pauses Square applies from time to time are invisible to it.</summary>
public sealed class EstateWatchService : IEstateWatch, IDisposable
{
    private const string BookKey = "estates";
    private const string NotificationTag = "realtor:estate";

    private const string LifestreamCommand = "/li";
    private const string LifestreamHomeArgument = "home";
    private const string LifestreamPlugin = "Lifestream";
    private static readonly TimeSpan LifestreamRecheck = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CountRecompute = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RestampVisitEvery = TimeSpan.FromMinutes(5);

    /// <summary>Consecutive "owns nothing" reads before a tracked estate is dropped. The manager reads empty
    /// for a moment around zoning, so one read is not enough to conclude the house is gone.</summary>
    private const int ForgetAfterEmptyReads = 6;

    private sealed class EstateBook
    {
        public List<EstateRecord> Estates { get; set; } = [];
    }

    private readonly IServiceProvider _services;
    private readonly AppStorageService _storage;
    private readonly NotificationDispatcher _notifier;
    private readonly object _gate = new();

    private EstateBook _book = new();
    private bool _bookLoaded;
    private int _version;
    private int _orderedForVersion = -1;
    private IReadOnlyList<EstateRecord> _ordered = [];
    private DateTime _nextCheckUtc = DateTime.MinValue;
    private DateTime _countComputedUtc = DateTime.MinValue;
    private int _atRiskCount;
    private ulong _emptyForContentId;
    private int _emptyReads;
    private DateTime _lifestreamCheckedUtc = DateTime.MinValue;
    private bool _lifestreamReady;

    /// <summary>Stamped by the poll on the framework thread so the reading side, which runs on the draw
    /// thread, never has to touch game memory to order the list.</summary>
    private ulong _currentContentId;

    public EstateWatchService(IServiceProvider services, AppStorageService storage,
        NotificationDispatcher notifier)
    {
        _services = services;
        _storage = storage;
        _notifier = notifier;
        Plugin.Framework.Update += OnUpdate;
        Plugin.ClientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        Plugin.ClientState.Logout -= OnLogout;
    }

    public IReadOnlyList<EstateRecord> Estates
    {
        get
        {
            lock (_gate)
            {
                EnsureBookLoadedLocked();
                var version = Volatile.Read(ref _version);
                if (_orderedForVersion != version)
                {
                    _orderedForVersion = version;
                    var currentId = Volatile.Read(ref _currentContentId);
                    _ordered = _book.Estates
                        .OrderByDescending(e => e.ContentId == currentId && currentId != 0)
                        .ThenBy(e => e.LastVisitUtc)
                        .ToList();
                }
                return _ordered;
            }
        }
    }

    /// <summary>Recomputed on a slow timer rather than per frame: this is a count of whole days, so it cannot
    /// change between two frames.</summary>
    public int AtRiskCount
    {
        get
        {
            var now = DateTime.UtcNow;
            lock (_gate)
            {
                if (now - _countComputedUtc < CountRecompute)
                {
                    return _atRiskCount;
                }
                _countComputedUtc = now;
                _atRiskCount = EstateRisk.AtRiskCount(Estates, now);
                return _atRiskCount;
            }
        }
    }

    public EstateRecord? Current
    {
        get
        {
            var currentId = Volatile.Read(ref _currentContentId);
            if (currentId == 0)
            {
                return null;
            }
            lock (_gate)
            {
                EnsureBookLoadedLocked();
                return _book.Estates.FirstOrDefault(e => e.ContentId == currentId);
            }
        }
    }

    /// <summary>Whether Lifestream is loaded AND its command is registered. Dalamud answers both, so this
    /// needs no IPC contract that could drift out from under us; the plugin half matters because a short
    /// alias like /li is not ours to assume. Re-asked on a slow timer, since plugins load and unload
    /// while we are running.</summary>
    public bool CanTeleportHome
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now - _lifestreamCheckedUtc < LifestreamRecheck)
            {
                return _lifestreamReady;
            }
            _lifestreamCheckedUtc = now;
            _lifestreamReady = false;
            try
            {
                if (!Plugin.CommandManager.Commands.ContainsKey(LifestreamCommand))
                {
                    return false;
                }
                foreach (var plugin in Plugin.PluginInterface.InstalledPlugins)
                {
                    if (plugin.IsLoaded
                        && string.Equals(plugin.InternalName, LifestreamPlugin, StringComparison.OrdinalIgnoreCase))
                    {
                        _lifestreamReady = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"[Realtor] Lifestream probe failed: {ex.Message}");
            }
            return _lifestreamReady;
        }
    }

    public void TeleportHome()
    {
        try
        {
            if (!Plugin.CommandManager.ProcessCommand($"{LifestreamCommand} {LifestreamHomeArgument}"))
            {
                Plugin.Log.Debug("[Realtor] Lifestream did not take the home command.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Realtor] Home teleport failed.");
        }
    }

    public int Version => Volatile.Read(ref _version);

    public void DismissWarnings()
    {
        try
        {
            _services.GetService<AetherOS.Sdk.IOsShell>()?.DismissByTag(NotificationTag);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[Realtor] Clearing the estate warning failed: {ex.Message}");
        }
    }

    private void OnLogout(int type, int code) => Volatile.Write(ref _currentContentId, 0);

    /// <summary>Capture runs whether or not the phone is switched on: a powered-off phone that stopped
    /// recording visits would come back claiming an absence that never happened. Only the announcing is gated.</summary>
    private void OnUpdate(IFramework framework)
    {
        if (!Plugin.ClientState.IsLoggedIn)
        {
            return;
        }
        var now = DateTime.UtcNow;
        if (now < _nextCheckUtc)
        {
            return;
        }
        _nextCheckUtc = now + CheckEvery;
        try
        {
            Poll(now);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[Realtor] Estate poll failed: {ex.Message}");
        }
    }

    private void Poll(DateTime now)
    {
        var contentId = CurrentContentId();
        if (contentId == 0)
        {
            return;
        }
        Volatile.Write(ref _currentContentId, contentId);

        var owned = OwnedPrivateEstate(out var territoryTypeId);
        if (owned == 0)
        {
            HandleNoEstate(contentId);
            return;
        }
        _emptyReads = 0;

        var inside = InsideOwnEstate(owned, out var ward, out var plot);
        var (name, world) = CurrentIdentity();

        EstateRecord tracked;
        var changed = false;
        lock (_gate)
        {
            EnsureBookLoadedLocked();
            tracked = RecordLocked(contentId, name, world, ref changed);
            if (territoryTypeId != 0 && tracked.TerritoryTypeId != territoryTypeId)
            {
                tracked.TerritoryTypeId = territoryTypeId;
                changed = true;
            }
            // Re-stamped on a coarse interval rather than every poll: the answer is measured in days, so
            // somebody idling in their house for an evening must not rewrite the book hundreds of times.
            if (inside && (!tracked.VisitObserved || now - tracked.LastVisitUtc >= RestampVisitEvery))
            {
                tracked.LastVisitUtc = now;
                tracked.VisitObserved = true;
                tracked.NotifiedStage = 0;
                if (ward > 0)
                {
                    tracked.Ward = ward;
                }
                if (plot > 0)
                {
                    tracked.Plot = plot;
                }
                changed = true;
            }
            if (changed)
            {
                PersistLocked();
            }
        }
        if (changed)
        {
            Interlocked.Increment(ref _version);
        }

        if (!inside)
        {
            MaybeAnnounce(tracked, now);
        }
    }

    private void HandleNoEstate(ulong contentId)
    {
        if (_emptyForContentId != contentId)
        {
            _emptyForContentId = contentId;
            _emptyReads = 0;
        }
        if (++_emptyReads < ForgetAfterEmptyReads)
        {
            return;
        }
        _emptyReads = 0;
        var had = false;
        lock (_gate)
        {
            EnsureBookLoadedLocked();
            had = _book.Estates.RemoveAll(e => e.ContentId == contentId) > 0;
            if (had)
            {
                PersistLocked();
            }
        }
        if (had)
        {
            Interlocked.Increment(ref _version);
            Plugin.Log.Debug("[Realtor] Dropped an estate record: the character no longer owns a private estate.");
        }
    }

    private void MaybeAnnounce(EstateRecord estate, DateTime now)
    {
        // Nothing to announce until a visit has been watched. Before that there is no absence to measure,
        // only a gap in what we saw, and warning about that is noise rather than help.
        if (!estate.VisitObserved)
        {
            return;
        }
        var days = EstateRisk.DaysAway(estate, now);
        var stage = EstateRisk.Stage(days);
        if (stage <= estate.NotifiedStage)
        {
            return;
        }
        // Checked before the stage is recorded, so switching the setting on later warns at once instead of
        // silently swallowing the crossing that already happened.
        if (!new RealtorSettings(_storage.For("realtor")).NotifyEstate)
        {
            return;
        }

        lock (_gate)
        {
            estate.NotifiedStage = stage;
            PersistLocked();
        }

        var text = Loc.T("notif.realtor_estate", estate.Character, EstateRisk.DaysLeft(days), days);
        _notifier.NotifyEstateRisk(text);
        try
        {
            var shell = _services.GetService<AetherOS.Sdk.IOsShell>();
            shell?.PostNotification("realtor", Loc.T("os.app_realtor"), text,
                onTap: () => shell.OpenApp("realtor"), tag: NotificationTag);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Realtor] Estate OS notification failed.");
        }
    }

    /// <summary>The logged-in character's own private estate, or 0. Free Company houses, chambers, shared
    /// estates and apartments all carry their own EstateType and are deliberately not asked for.</summary>
    private static ulong OwnedPrivateEstate(out uint territoryTypeId)
    {
        territoryTypeId = 0;
        try
        {
            var owned = HousingManager.GetOwnedHouseId(EstateType.PersonalEstate);
            territoryTypeId = owned.TerritoryTypeId;
            return owned.Id;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Whether the player is standing inside that exact house. HouseId carries the world and ward,
    /// so another character's estate cannot match even on the same plot number of another world.</summary>
    private static unsafe bool InsideOwnEstate(ulong ownedId, out int ward, out int plot)
    {
        ward = 0;
        plot = 0;
        try
        {
            var housing = HousingManager.Instance();
            if (housing == null || !housing->IsInside())
            {
                return false;
            }
            if (housing->GetCurrentIndoorHouseId().Id != ownedId)
            {
                return false;
            }
            ward = housing->GetCurrentWard() + 1;
            plot = housing->GetCurrentPlot() + 1;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static unsafe ulong CurrentContentId()
    {
        try
        {
            var playerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
            return playerState != null ? playerState->ContentId : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static (string Name, string World) CurrentIdentity()
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player is null)
            {
                return ("", "");
            }
            var world = string.Empty;
            try
            {
                world = player.HomeWorld.Value.Name.ExtractText();
            }
            catch
            {
                // A missing world row is cosmetic; the character still tracks under its name.
            }
            return (player.Name.TextValue, world);
        }
        catch
        {
            return ("", "");
        }
    }

    private void EnsureBookLoadedLocked()
    {
        if (_bookLoaded)
        {
            return;
        }
        _book = _storage.For("realtor").Get<EstateBook>(BookKey) ?? new EstateBook();
        _bookLoaded = true;
        // Books written before visits were flagged carry a stamped date with nothing to say it was real.
        // Left alone they read as an absence since the year zero.
        foreach (var estate in _book.Estates)
        {
            if (!estate.VisitObserved && estate.LastVisitUtc != default)
            {
                estate.LastVisitUtc = default;
            }
        }
    }

    private EstateRecord RecordLocked(ulong contentId, string name, string world, ref bool changed)
    {
        var estate = _book.Estates.FirstOrDefault(e => e.ContentId == contentId);
        if (estate is null)
        {
            estate = new EstateRecord { ContentId = contentId, FirstSeenUtc = DateTime.UtcNow };
            _book.Estates.Add(estate);
            changed = true;
        }
        if (name.Length > 0 && estate.Character != name)
        {
            estate.Character = name;
            changed = true;
        }
        if (world.Length > 0 && estate.World != world)
        {
            estate.World = world;
            changed = true;
        }
        return estate;
    }

    private void PersistLocked()
    {
        if (_bookLoaded)
        {
            _storage.For("realtor").Set(BookKey, _book);
        }
    }
}
