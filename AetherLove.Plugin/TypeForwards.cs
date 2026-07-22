using System.Runtime.CompilerServices;

// These config types moved into AetherLove.AppKit. Persisted Dalamud configs still name the old
// assembly in their Newtonsoft $type tokens; the forwarders let the CLR resolve those names to the new
// assembly, so an existing config (auth tokens, crypto keys, everything) deserializes without loss.
[assembly: TypeForwardedTo(typeof(AetherLove.Config.Configuration))]
[assembly: TypeForwardedTo(typeof(AetherLove.Config.AuthState))]
[assembly: TypeForwardedTo(typeof(AetherLove.Config.CryptoKeys))]
[assembly: TypeForwardedTo(typeof(AetherLove.Config.PulseState))]
[assembly: TypeForwardedTo(typeof(AetherLove.Config.PlacesState))]
[assembly: TypeForwardedTo(typeof(AetherLove.Config.HangoutClientState))]
