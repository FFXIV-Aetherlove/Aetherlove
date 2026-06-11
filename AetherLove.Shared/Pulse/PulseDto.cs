using MessagePack;

namespace AetherLove.Shared.Pulse;

/// <summary>A single localized line the client surfaces in-game. Returned from the hub's pulse fetch;
/// the server picks one at random and localizes it to the caller's language.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record PulseDto(string Text);
