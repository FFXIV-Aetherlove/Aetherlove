using System;
using System.Globalization;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using Microsoft.AspNetCore.SignalR;

namespace AetherLove.Services;

/// <summary>Renders hub-call exceptions as localized text. Server errors carry an <c>AL_ERR|code|args</c>
/// payload (<see cref="HubErrors"/>) whose code maps to the <c>huberror.&lt;code&gt;</c> string key.</summary>
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
        // Client-side failures (e.g. the pre-upload photo pipeline) carry the same AL_ERR payload.
        if (ex.Message is { } m && m.Contains(HubErrors.Sentinel, StringComparison.Ordinal))
        {
            return LocalizeMessage(m);
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
