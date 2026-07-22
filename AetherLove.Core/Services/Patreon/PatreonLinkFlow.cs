using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Patreon;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Patreon;

public enum PatreonFlowState
{
    Idle = 0,
    Starting = 1,
    AwaitingBrowser = 2,
    Completed = 3,
    Failed = 4,
}

/// <summary>Drives the Patreon account-link flow over the hub and caches the latest <see cref="PatreonStatusDto"/>.</summary>
public sealed class PatreonLinkFlow
{
    private readonly IPluginLog _log;
    private readonly AetherHubContext _hub;
    private readonly Auth.SessionBootstrapper _bootstrap;

    private volatile int _stateRaw;
    private volatile string? _authorizeUrl;
    private volatile string? _errorMessage;
    private volatile PatreonStatusDto? _status;
    private volatile bool _statusLoading;

    private CancellationTokenSource? _cts;
    private readonly object _flowLock = new();
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public PatreonLinkFlow(IPluginLog log, AetherHubContext hub, Auth.SessionBootstrapper bootstrap)
    {
        _log = log;
        _hub = hub;
        _bootstrap = bootstrap;
    }

    public PatreonFlowState State => (PatreonFlowState)_stateRaw;
    public string? ErrorMessage => _errorMessage;
    public PatreonStatusDto? Status => _status;
    public bool StatusLoading => _statusLoading;

    /// <summary>Raised only when a link completes with an entitled membership; fires off the UI thread.</summary>
    public event Action? LinkCompleted;

    public void Reset()
    {
        lock (_flowLock)
        {
            _cts?.Cancel();
            _cts = null;
        }
        _authorizeUrl = null;
        _errorMessage = null;
        SetState(PatreonFlowState.Idle);
        _ = RefreshStatusAsync();
    }

    public async Task RefreshStatusAsync()
    {
        _statusLoading = true;
        try
        {
            _status = await _hub.GetPatreonStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Patreon] Failed to load link status.");
        }
        finally
        {
            _statusLoading = false;
        }
    }

    public void StartLink()
    {
        lock (_flowLock)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _authorizeUrl = null;
            _errorMessage = null;
            SetState(PatreonFlowState.Starting);

            var ct = _cts.Token;
            _ = Task.Run(() => RunFlowAsync(ct), ct);
        }
    }

    public void Cancel()
    {
        lock (_flowLock)
        {
            _cts?.Cancel();
            _cts = null;
        }
        _authorizeUrl = null;
        _errorMessage = null;
        SetState(PatreonFlowState.Idle);
    }

    public void ReopenBrowser()
    {
        var url = _authorizeUrl;
        if (!string.IsNullOrEmpty(url))
        {
            TryOpenBrowser(url);
        }
    }

    public async Task<bool> UnlinkAsync()
    {
        try
        {
            _status = await _hub.UnlinkPatreonAsync().ConfigureAwait(false);
            SetState(PatreonFlowState.Idle);
            _errorMessage = null;
            _ = _bootstrap.RefreshConnectionInfoAsync();
            _ = _bootstrap.RefreshAccountInfoAsync();
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Patreon] Unlink failed.");
            _errorMessage = HubErrorText.Localize(ex);
            return false;
        }
    }

    private async Task RunFlowAsync(CancellationToken ct)
    {
        try
        {
            PatreonLinkStartDto start;
            try
            {
                start = await _hub.StartPatreonLinkAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warning(ex, "[Patreon] Failed to start link.");
                Fail(HubErrorText.Localize(ex));
                return;
            }

            _authorizeUrl = start.AuthorizeUrl;
            SetState(PatreonFlowState.AwaitingBrowser);
            TryOpenBrowser(start.AuthorizeUrl);

            while (!ct.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= start.ExpiresAtUtc)
                {
                    Fail(Loc.T("settings.supporter_link_expired"));
                    return;
                }

                try
                {
                    var status = await _hub.GetPatreonStatusAsync(ct).ConfigureAwait(false);
                    _status = status;
                    switch (status.Flow)
                    {
                        case PatreonLinkFlowStatus.Completed:
                            SetState(PatreonFlowState.Completed);
                            _ = _bootstrap.RefreshConnectionInfoAsync();
                            _ = _bootstrap.RefreshAccountInfoAsync();
                            if (status.IsEntitled)
                            {
                                LinkCompleted?.Invoke();
                            }
                            return;
                        case PatreonLinkFlowStatus.Failed:
                            Fail(LocalizeCode(status.FlowErrorCode));
                            return;
                        case PatreonLinkFlowStatus.Expired:
                            Fail(Loc.T("settings.supporter_link_expired"));
                            return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Warning(ex, "[Patreon] Status poll failed.");
                    Fail(HubErrorText.Localize(ex));
                    return;
                }

                try
                {
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[Patreon] Unexpected link flow error.");
            Fail(HubErrorText.Localize(ex));
        }
    }

    private static string LocalizeCode(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return Loc.T("settings.supporter_failed");
        }
        var key = "huberror." + code;
        var text = Loc.T(key);
        return text == key ? Loc.T("settings.supporter_failed") : text;
    }

    private void Fail(string message)
    {
        _errorMessage = message;
        SetState(PatreonFlowState.Failed);
    }

    private void SetState(PatreonFlowState state) => Interlocked.Exchange(ref _stateRaw, (int)state);

    private void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Patreon] Failed to open browser. Url={Url}", url);
        }
    }
}
