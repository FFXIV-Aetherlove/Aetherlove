using System;
using System.Globalization;
using System.Linq;
using AetherOS.Sdk;
using Dalamud.Plugin.Ipc;

namespace AetherLove.Os;

/// <summary>Travel driven by Lifestream, over its published IPC. Everything here is best-effort: the plugin may
/// be absent, disabled mid-session, or a future version may change the surface, and none of that may do more
/// than hide a button.</summary>
public sealed class LifestreamBridge : ITravelBridge
{
    private const string PluginInternalName = "Lifestream";
    private const string DisplayName = "Lifestream";

    /// <summary>Takes the text that would follow <c>/li</c>; Lifestream runs it through its own command
    /// parser, which is a far smaller contract than its address-book tuple and survives its updates.</summary>
    private const string ExecuteCommandIpc = "Lifestream.ExecuteCommand";
    private const string IsBusyIpc = "Lifestream.IsBusy";

    /// <summary>Installed-plugin scanning is not free, so the answer is held between polls.</summary>
    private static readonly TimeSpan PresencePollInterval = TimeSpan.FromSeconds(5);

    private readonly ICallGateSubscriber<string, object> _execute;
    private readonly ICallGateSubscriber<bool> _isBusy;

    private DateTime _presenceCheckedUtc = DateTime.MinValue;
    private bool _present;

    public LifestreamBridge()
    {
        _execute = Plugin.PluginInterface.GetIpcSubscriber<string, object>(ExecuteCommandIpc);
        _isBusy = Plugin.PluginInterface.GetIpcSubscriber<bool>(IsBusyIpc);
    }

    public string? ProviderName => IsAvailable ? DisplayName : null;

    public bool IsAvailable
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now - _presenceCheckedUtc >= PresencePollInterval)
            {
                _presenceCheckedUtc = now;
                _present = Scan();
            }
            return _present;
        }
    }

    public bool IsBusy
    {
        get
        {
            if (!IsAvailable)
            {
                return false;
            }
            try
            {
                return _isBusy.InvokeFunc();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public bool GoTo(TravelAddress address)
    {
        if (!IsAvailable || !address.IsComplete || DistrictKeyword(address.District) is not { } district)
        {
            return false;
        }

        var spot = address.Room > 0
            ? "A" + address.Room.ToString(CultureInfo.InvariantCulture)
            : "P" + address.Plot.ToString(CultureInfo.InvariantCulture);
        var command = $"{address.World} {district} W{address.Ward.ToString(CultureInfo.InvariantCulture)} {spot}";

        try
        {
            _execute.InvokeAction(command);
            Plugin.Log.Debug("[LifestreamBridge] Sent '{Command}'.", command);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[LifestreamBridge] Travel request failed.");
            _present = false;
            return false;
        }
    }

    /// <summary>Lifestream matches districts against its own English alias list, whatever the game's language,
    /// so these are its short forms rather than anything the player sees.</summary>
    private static string? DistrictKeyword(TravelDistrict district) => district switch
    {
        TravelDistrict.Mist => "mist",
        TravelDistrict.LavenderBeds => "lavender",
        TravelDistrict.Goblet => "goblet",
        TravelDistrict.Shirogane => "shirogane",
        TravelDistrict.Empyreum => "empyreum",
        _ => null,
    };

    private static bool Scan()
    {
        try
        {
            return Plugin.PluginInterface.InstalledPlugins
                .Any(p => p.IsLoaded
                    && string.Equals(p.InternalName, PluginInternalName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[LifestreamBridge] Presence scan failed.");
            return false;
        }
    }
}
