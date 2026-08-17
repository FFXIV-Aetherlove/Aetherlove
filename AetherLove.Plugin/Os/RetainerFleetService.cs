using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using AetherOS.Apps.Timers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace AetherLove.Os;

/// <summary>Captures every character's retainer ventures and FC workshop fleet into one persisted book for
/// the Timers app. Retainers are read from RetainerManager whenever a summoning bell surface opens, plus a
/// once-per-login venture-timer request so the book refreshes without visiting a bell; the fleet is read
/// from the workshop territory while the player stands in the FC workshop. Strictly read-only and hook-free;
/// every game read is try/caught so a patch degrades to a stale book instead of breaking the app.</summary>
public sealed class RetainerFleetService : ITimersRetainers, IDisposable
{
    private const string BookKey = "characters";
    private static readonly TimeSpan LoginRequestTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PostRequestSnapshotDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FleetCaptureEvery = TimeSpan.FromSeconds(30);

    private sealed class FleetBook
    {
        public List<TimersCharacter> Characters { get; set; } = [];
    }

    private readonly AetherOS.Sdk.IAppStorage _storage;
    private readonly object _gate = new();
    private FleetBook _book = new();
    private bool _bookLoaded;
    private int _version;

    private DateTime _loginSeenUtc = DateTime.MinValue;
    private DateTime _snapshotDueUtc = DateTime.MinValue;
    private DateTime _nextFleetCaptureUtc = DateTime.MinValue;
    private bool _started;

    public RetainerFleetService(AppStorageService storage)
    {
        _storage = storage.For("timers");
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }
        try
        {
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerList", OnRetainerAddon);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", OnRetainerAddon);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSellList", OnRetainerAddon);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[TimersRetainers] Addon listener registration failed.");
        }
        Plugin.ClientState.Login += OnLogin;
        Plugin.Framework.Update += OnFrameworkUpdate;
        if (Plugin.ClientState.IsLoggedIn)
        {
            OnLogin();
        }
        _started = true;
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }
        Plugin.Framework.Update -= OnFrameworkUpdate;
        Plugin.ClientState.Login -= OnLogin;
        try
        {
            Plugin.AddonLifecycle.UnregisterListener(OnRetainerAddon);
        }
        catch
        {
            // Listener may already be gone on shutdown.
        }
        _started = false;
    }

    public void Dispose()
    {
        Stop();
    }

    public IReadOnlyList<TimersCharacter> Characters
    {
        get
        {
            var currentId = CurrentContentId();
            lock (_gate)
            {
                EnsureBookLoadedLocked();
                return _book.Characters
                    .OrderByDescending(c => c.ContentId == currentId && currentId != 0)
                    .ThenByDescending(c => c.CapturedUtc)
                    .ToList();
            }
        }
    }

    public int Version => Volatile.Read(ref _version);

    private void OnLogin()
    {
        _loginSeenUtc = DateTime.UtcNow;
        _snapshotDueUtc = DateTime.MinValue;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        HandleLoginRequest(now);
        HandleFleetCapture(now);
    }

    private void HandleLoginRequest(DateTime now)
    {
        if (_snapshotDueUtc != DateTime.MinValue && now >= _snapshotDueUtc)
        {
            _snapshotDueUtc = DateTime.MinValue;
            try
            {
                CaptureRetainers();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[TimersRetainers] Post-login capture failed.");
            }
            return;
        }
        if (_loginSeenUtc == DateTime.MinValue)
        {
            return;
        }
        if (now - _loginSeenUtc > LoginRequestTimeout)
        {
            _loginSeenUtc = DateTime.MinValue;
            return;
        }
        if (!RetainerManagerReady())
        {
            return;
        }
        _loginSeenUtc = DateTime.MinValue;
        RequestVentureTimers();
        _snapshotDueUtc = now + PostRequestSnapshotDelay;
    }

    private static unsafe bool RetainerManagerReady()
    {
        try
        {
            var manager = RetainerManager.Instance();
            return manager != null && manager->IsReady;
        }
        catch
        {
            return false;
        }
    }

    private static unsafe void RequestVentureTimers()
    {
        try
        {
            var manager = RetainerManager.Instance();
            if (manager != null)
            {
                manager->RequestVenturesTimers();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[TimersRetainers] Venture timer request failed: {ex.Message}");
        }
    }

    private void OnRetainerAddon(AddonEvent type, AddonArgs args)
    {
        try
        {
            CaptureRetainers();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[TimersRetainers] Retainer capture failed.");
        }
    }

    private unsafe void CaptureRetainers()
    {
        var contentId = CurrentContentId();
        if (contentId == 0)
        {
            return;
        }
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
        {
            return;
        }
        var rows = new List<RetainerRow>();
        for (var i = 0u; i < manager->GetRetainerCount(); i++)
        {
            var retainer = manager->GetRetainerBySortedIndex(i);
            if (retainer == null || retainer->RetainerId == 0 || !retainer->Available)
            {
                continue;
            }
            var retainerName = retainer->NameString;
            if (retainerName.Length == 0)
            {
                continue;
            }
            var ventureId = (uint)retainer->VentureId;
            rows.Add(new RetainerRow
            {
                RetainerId = retainer->RetainerId,
                Name = retainerName,
                VentureId = ventureId,
                VentureName = ventureId == 0 ? string.Empty : ResolveVentureName(ventureId),
                CompleteUtc = ventureId == 0 || retainer->VentureComplete == 0
                    ? default
                    : DateTimeOffset.FromUnixTimeSeconds(retainer->VentureComplete).UtcDateTime,
            });
        }
        if (rows.Count == 0)
        {
            return;
        }
        var (name, world) = CurrentIdentity();
        lock (_gate)
        {
            EnsureBookLoadedLocked();
            var character = CharacterLocked(contentId, name, world);
            character.Retainers = rows;
            character.CapturedUtc = DateTime.UtcNow;
            PersistLocked();
        }
        BumpVersion();
    }

    private static string ResolveVentureName(uint ventureId)
    {
        try
        {
            var task = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.RetainerTask>().GetRowOrDefault(ventureId);
            if (task is null)
            {
                return string.Empty;
            }
            var targetId = task.Value.Task.RowId;
            if (task.Value.IsRandom)
            {
                return Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.RetainerTaskRandom>()
                    .GetRowOrDefault(targetId)?.Name.ExtractText() ?? string.Empty;
            }
            return Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.RetainerTaskNormal>()
                .GetRowOrDefault(targetId)?.Item.ValueNullable?.Name.ExtractText() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[TimersRetainers] Venture name resolve failed: {ex.Message}");
            return string.Empty;
        }
    }

    private unsafe void HandleFleetCapture(DateTime now)
    {
        try
        {
            var housing = HousingManager.Instance();
            if (housing == null || housing->WorkshopTerritory == null)
            {
                return;
            }
            if (now < _nextFleetCaptureUtc)
            {
                return;
            }
            _nextFleetCaptureUtc = now + FleetCaptureEvery;
            CaptureFleet(housing->WorkshopTerritory);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[TimersRetainers] Fleet capture failed: {ex.Message}");
        }
    }

    private unsafe void CaptureFleet(WorkshopTerritory* territory)
    {
        var contentId = CurrentContentId();
        if (contentId == 0)
        {
            return;
        }
        var fleet = new List<FleetVessel>();
        var subs = territory->Submersible.Data;
        for (var i = 0; i < subs.Length; i++)
        {
            ref var sub = ref subs[i];
            var vesselName = DecodeVesselName(sub.Name);
            if (sub.RankId == 0 || vesselName.Length == 0)
            {
                continue;
            }
            fleet.Add(new FleetVessel
            {
                Kind = VesselKind.Submersible,
                Name = vesselName,
                ReturnUtc = sub.ReturnTime == 0
                    ? DateTime.MinValue
                    : DateTimeOffset.FromUnixTimeSeconds(sub.ReturnTime).UtcDateTime,
            });
        }
        var airships = territory->Airship.Data;
        for (var i = 0; i < airships.Length; i++)
        {
            ref var ship = ref airships[i];
            var vesselName = ship.NameString;
            if (ship.RankId == 0 || vesselName.Length == 0)
            {
                continue;
            }
            fleet.Add(new FleetVessel
            {
                Kind = VesselKind.Airship,
                Name = vesselName,
                ReturnUtc = ship.ReturnTime == 0
                    ? DateTime.MinValue
                    : DateTimeOffset.FromUnixTimeSeconds(ship.ReturnTime).UtcDateTime,
            });
        }
        var freeCompany = FreeCompanyName();
        var (name, world) = CurrentIdentity();
        lock (_gate)
        {
            EnsureBookLoadedLocked();
            var character = CharacterLocked(contentId, name, world);
            character.Fleet = fleet;
            if (freeCompany.Length > 0)
            {
                character.FreeCompany = freeCompany;
            }
            character.FleetCapturedUtc = DateTime.UtcNow;
            PersistLocked();
        }
        BumpVersion();
    }

    private static string DecodeVesselName(Span<byte> raw)
    {
        var terminator = raw.IndexOf((byte)0);
        if (terminator == 0)
        {
            return string.Empty;
        }
        return Encoding.UTF8.GetString(terminator < 0 ? raw : raw[..terminator]);
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

    private static unsafe string FreeCompanyName()
    {
        try
        {
            var proxy = InfoProxyFreeCompany.Instance();
            return proxy != null ? proxy->NameString : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>The logged-in character's name and home world; every capture path runs on the framework
    /// thread, which is the only place these reads are valid.</summary>
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
                // A missing world row is cosmetic; the character still logs under its name.
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
        if (!_bookLoaded)
        {
            _book = _storage.Get<FleetBook>(BookKey) ?? new FleetBook();
            _bookLoaded = true;
        }
    }

    private TimersCharacter CharacterLocked(ulong contentId, string name, string world)
    {
        var character = _book.Characters.FirstOrDefault(c => c.ContentId == contentId);
        if (character is null)
        {
            character = new TimersCharacter { ContentId = contentId };
            _book.Characters.Add(character);
        }
        if (name.Length > 0)
        {
            character.Name = name;
        }
        if (world.Length > 0)
        {
            character.World = world;
        }
        return character;
    }

    private void PersistLocked()
    {
        if (_bookLoaded)
        {
            _storage.Set(BookKey, _book);
        }
    }

    private void BumpVersion() => Interlocked.Increment(ref _version);
}
