namespace AetherLove.Shared;

/// <summary>Protocol version the plugin and server agree on. The client sends <see cref="Current"/> on the
/// hub connection; the server is the authority and rejects any client whose version differs, so an outdated
/// plugin is told to update instead of talking to an incompatible server. Bump this whenever a wire-level
/// change (DTO shape, hub method contract) would break older clients.</summary>
public static class ApiVersion
{
    public const int Current = 2;

    /// <summary>Sentinel the server puts in a <c>HubException</c> on a version mismatch so the client can
    /// recognise it (vs. an ordinary error) and show the "update the plugin" screen. Format:
    /// <c>API_VERSION_MISMATCH|serverVersion</c>.</summary>
    public const string MismatchError = "API_VERSION_MISMATCH";

    /// <summary>The effective client version from the raw query-string value. Clients from before versioning
    /// send nothing, so a missing or unparseable value defaults to <see cref="Current"/>'s original value of 1.</summary>
    public static int Resolve(string? raw) => int.TryParse(raw, out var v) ? v : 1;

    /// <summary>True if a resolved client version is accepted by this server.</summary>
    public static bool IsSupported(int clientVersion) => clientVersion == Current;

    /// <summary>The <c>HubException</c> payload the server throws on a mismatch.</summary>
    public static string MismatchPayload() => $"{MismatchError}|{Current}";
}
