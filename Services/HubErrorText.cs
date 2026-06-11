using System;
using System.Globalization;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using Microsoft.AspNetCore.SignalR;

namespace AetherLove.Services;

/// <summary>
/// Renders exceptions from hub calls as localized user-facing text. Server errors carry an
/// <c>AL_ERR|code|args</c> payload (<see cref="HubErrors"/>) inside the SignalR-wrapped message;
/// the code maps to the <c>huberror.&lt;code&gt;</c> string key. Unknown codes and legacy payloads
/// fall back to a generic localized line, and non-hub exceptions pass their message through.
/// </summary>
public static class HubErrorText
{
    public static string Localize(Exception ex)
    {
        if (ex is RateLimitException)
        {
            return Loc.T("huberror.rate_limited");
        }
        if (ex is HubException hub)
        {
            return LocalizeMessage(hub.Message);
        }
        return ex.Message;
    }

    private static string LocalizeMessage(string message)
    {
        var idx = message.IndexOf(HubErrors.Sentinel, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var parts = message[(idx + HubErrors.Sentinel.Length)..].Split('|');
            var key = "huberror." + parts[0];
            var template = Loc.T(key);
            if (template != key)
            {
                try
                {
                    return string.Format(CultureInfo.CurrentCulture, template, parts[1..]);
                }
                catch (FormatException)
                {
                    return template;
                }
            }
            return Loc.T("huberror.generic");
        }

        // Legacy / non-protocol hub error: strip SignalR's invocation wrapper, keep the detail.
        const string marker = "HubException: ";
        var m = message.IndexOf(marker, StringComparison.Ordinal);
        var detail = m >= 0 ? message[(m + marker.Length)..] : message;
        return Loc.T("huberror.generic_detail", detail);
    }
}
