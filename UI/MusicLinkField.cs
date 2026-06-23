using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Profile.Enums;

namespace AetherLove.UI;

/// <summary>Per-provider "favourite song" input state for onboarding + the profile editor. Holds the raw
/// pasted link (sent to the server on save) and the server-resolved, curated name shown as a read-only
/// preview. Resolution is delegated to the hub; an in-flight resolve is superseded by newer input via a
/// sequence guard. The name is never user-typed.</summary>
public sealed class MusicLinkField
{
    private readonly Func<MusicProvider, string, CancellationToken, Task<MusicLinkDto?>> _resolve;
    private int _seq;

    /// <summary>The last input string a resolve was kicked off for. Guards against re-resolving the same
    /// text (an ImGui InputText re-reports its value each frame while focused), which would otherwise loop
    /// hub calls and exhaust the resolve rate limit.</summary>
    private string _lastResolvedInput = string.Empty;

    /// <summary>Input waiting out the debounce window before a resolve fires; null when nothing is pending.
    /// Collapses a paste or a burst of typing into a single hub call instead of one resolve per keystroke.</summary>
    private string? _pendingInput;
    private DateTime _pendingSince;

    /// <summary>How long the input must sit unchanged before a resolve is sent.</summary>
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

    public MusicProvider Provider { get; }

    /// <summary>Raw input the user pasted / the collapsed canonical ref after a successful resolve.</summary>
    public string Input = string.Empty;

    /// <summary>Last server-resolved canonical reference (empty when unresolved/invalid).</summary>
    public string ResolvedRef = string.Empty;

    /// <summary>Server-curated song name preview (empty when unresolved or the title couldn't be fetched).</summary>
    public string ResolvedName = string.Empty;

    public bool Fetching;

    /// <summary>True when the last non-empty input couldn't be recognised as a valid provider link.</summary>
    public bool Invalid;

    public MusicLinkField(
        MusicProvider provider,
        Func<MusicProvider, string, CancellationToken, Task<MusicLinkDto?>> resolve)
    {
        Provider = provider;
        _resolve = resolve;
    }

    public void Hydrate(string? storedRef, string? storedName)
    {
        Input = storedRef ?? string.Empty;
        ResolvedRef = Input;
        ResolvedName = storedName ?? string.Empty;
        _lastResolvedInput = Input;
        _pendingInput = null;
        Fetching = false;
        Invalid = false;
    }

    /// <summary>Called when the input box changes. Schedules a debounced resolve unless the input is blank or
    /// already equals the last resolved ref / scheduled text.</summary>
    public void OnInputChanged()
    {
        var input = Input.Trim();
        if (input.Length == 0)
        {
            _pendingInput = null;
            ResolvedRef = string.Empty;
            ResolvedName = string.Empty;
            _lastResolvedInput = string.Empty;
            Fetching = false;
            Invalid = false;
            return;
        }
        // Resolve each distinct input only once — the box already equals the canonical ref, or we already
        // fired a resolve for this exact text (an ImGui InputText re-reports its value each frame while focused).
        if (input == ResolvedRef || input == _lastResolvedInput)
        {
            _pendingInput = null;
            return;
        }
        // Defer the resolve until the input settles; every keystroke/paste resets the window, so a burst
        // collapses into a single hub call instead of one resolve per edit (which exhausts the rate limit).
        _pendingInput = input;
        _pendingSince = DateTime.UtcNow;
        ResolvedName = string.Empty;
        Invalid = false;
        Fetching = true;
    }

    /// <summary>Per-frame pump: fires the deferred resolve once the input has been unchanged for the debounce
    /// window. Call once per frame while the field is drawn.</summary>
    public void Tick()
    {
        if (_pendingInput is null || DateTime.UtcNow - _pendingSince < DebounceWindow)
        {
            return;
        }
        var input = _pendingInput;
        _pendingInput = null;
        _lastResolvedInput = input;
        var seq = ++_seq;
        _ = ResolveAsync(input, seq);
    }

    private async Task ResolveAsync(string input, int seq)
    {
        MusicLinkDto? dto = null;
        Exception? error = null;
        try
        {
            dto = await _resolve(Provider, input, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (seq != _seq)
        {
            return;
        }
        Fetching = false;
        if (dto is not null)
        {
            ResolvedRef = dto.Ref;
            ResolvedName = dto.Name;
            Invalid = false;
            return;
        }
        // A null result is the server rejecting the link; an exception is the hub call itself failing
        // (connection / rate-limit). Both leave the field invalid — log the exception so it isn't silent.
        if (error is not null)
        {
            Plugin.Log.Warning(error, "[MusicLink] {0} resolve failed for input of length {1}.", Provider, input.Length);
        }
        ResolvedRef = string.Empty;
        ResolvedName = string.Empty;
        Invalid = true;
    }
}
