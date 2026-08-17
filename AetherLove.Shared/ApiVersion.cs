namespace AetherLove.Shared;

/// <summary>Protocol version; server rejects clients whose version differs.</summary>
public static class ApiVersion
{
    public const int Current = 13;

    /// <summary>Error code prefix for version mismatches: <c>API_VERSION_MISMATCH|serverVersion</c>.</summary>
    public const string MismatchError = "API_VERSION_MISMATCH";

    /// <summary>Parses a client version string; defaults to 1 if missing or invalid.</summary>
    public static int Resolve(string? raw) => int.TryParse(raw, out var v) ? v : 1;

    /// <summary>True if a resolved client version is accepted by this server.</summary>
    public static bool IsSupported(int clientVersion) => clientVersion == Current;

    /// <summary>The <c>HubException</c> payload the server throws on a mismatch.</summary>
    public static string MismatchPayload() => $"{MismatchError}|{Current}";
}
