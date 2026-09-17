using System;

namespace AetherOS.Sdk;

/// <summary>Whether the phone's downloaded media has arrived. Art, sounds and music are fetched from the
/// server after sign-in and after an update, pack by pack; an app that draws from a pack asks here before
/// reading files, and draws a waiting card while the pack is still on its way.</summary>
public interface IAssetState
{
    /// <summary>True once the named pack is on disk. Pack names are the constants in
    /// <c>AetherLove.Shared.Assets.AssetPacks</c>.</summary>
    bool IsReady(string pack);

    /// <summary>Bytes fetched so far and the total for a pack in flight; both zero when nothing is moving.</summary>
    (long Done, long Total) Progress(string pack);

    /// <summary>Raised on the draw thread with the pack name when a pack has landed.</summary>
    event Action<string>? PackReady;
}
