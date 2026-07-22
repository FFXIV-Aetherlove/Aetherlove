using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Shown when the server denies a request with a <see cref="RateLimitException"/>.</summary>
public sealed class RateLimitModal
{
    private string _bucket = "";
    private int _limit;
    private DateTimeOffset _retryAtUtc;

    /// <summary>Safe to call from a background task; no ImGui calls happen until the next UI frame.</summary>
    public void Show(string bucket, int limit, DateTimeOffset retryAtUtc)
    {
        _bucket = bucket;
        _limit = limit;
        _retryAtUtc = retryAtUtc;
        ModalHost.Instance?.Open(310f, DrawBody);
    }

    public void Show(RateLimitException ex) => Show(ex.Bucket, ex.Limit, ex.RetryAtUtc);

    private void DrawBody(float availW)
    {
        ModalUi.Header(availW, FontAwesomeIcon.Stopwatch, Loc.T("common.rate_limit_title"), UiColors.Amber);

        ImGui.TextColored(UiColors.Body, BuildBodyText());
        ImGui.Spacing();
        ImGui.Spacing();

        if (ModalUi.Button($"{Loc.T("common.ok")}##rlOk", availW))
        {
            ModalHost.Instance?.Close();
        }
    }

    private string BuildBodyText()
    {
        var noun = _bucket switch
        {
            "profile" => Loc.T("common.rate_limit_noun_profile"),
            "image" => Loc.T("common.rate_limit_noun_images"),
            _ => _bucket,
        };
        var retryIn = _retryAtUtc - DateTimeOffset.UtcNow;
        var retryText = FormatRetry(retryIn);
        return Loc.T("common.rate_limit_body", noun, _limit, retryText);
    }

    private static string FormatRetry(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
        {
            return Loc.T("common.rate_limit_retry_moment");
        }
        if (span.TotalMinutes < 1)
        {
            var s = Math.Max(1, (int)Math.Ceiling(span.TotalSeconds));
            return s == 1 ? Loc.T("common.rate_limit_retry_one_second") : Loc.T("common.rate_limit_retry_seconds", s);
        }
        var m = (int)Math.Ceiling(span.TotalMinutes);
        return m == 1 ? Loc.T("common.rate_limit_retry_one_minute") : Loc.T("common.rate_limit_retry_minutes", m);
    }
}
