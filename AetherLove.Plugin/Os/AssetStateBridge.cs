using System;
using AetherLove.Services.Assets;
using AetherOS.Sdk;

namespace AetherLove.Os;

/// <summary>The app-facing view of the asset sync: pack readiness and progress, with the landed event
/// re-raised from the sync's draw-thread drain so an app never hears it from a worker.</summary>
public sealed class AssetStateBridge : IAssetState
{
    private readonly AssetSyncService _assets;

    public AssetStateBridge(AssetSyncService assets)
    {
        _assets = assets;
        _assets.PackReady += pack => PackReady?.Invoke(pack);
    }

    public bool IsReady(string pack) => _assets.IsReady(pack);

    public (long Done, long Total) Progress(string pack) => _assets.Progress(pack);

    public event Action<string>? PackReady;
}
