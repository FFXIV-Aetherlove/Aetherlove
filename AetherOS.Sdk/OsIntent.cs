namespace AetherOS.Sdk;

/// <summary>A cross-app payload, delivered to the target app's <see cref="IAetherApp.OnIntent"/>.</summary>
public sealed class OsIntent
{
    public required string Type { get; init; }
    public string PayloadJson { get; init; } = "";
}
