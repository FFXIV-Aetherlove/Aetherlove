using AetherLove.Services;
using AetherLove.Services.Auth;
using Dalamud.Interface.Textures;

namespace AetherLove.Os;

/// <summary>Plugin-side <see cref="IOsAccountInfo"/>: display name from the session snapshot, avatar from the cache.</summary>
public sealed class OsAccountInfo : IOsAccountInfo
{
    private readonly SessionBootstrapper _bootstrap;
    private readonly OsAvatarCache _avatar;

    public OsAccountInfo(SessionBootstrapper bootstrap, OsAvatarCache avatar)
    {
        _bootstrap = bootstrap;
        _avatar = avatar;
    }

    public string? DisplayName => _bootstrap.LastAccount?.OsDisplayName;

    public ISharedImmediateTexture? Avatar => _avatar.Texture;

    public string? FrameRef => _bootstrap.LastAccount?.EquippedFrameRef;
}
