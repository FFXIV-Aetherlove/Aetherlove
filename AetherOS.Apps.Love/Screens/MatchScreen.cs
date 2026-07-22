using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>The match host: captures the matched pair into <see cref="MatchContent"/>, then delegates
/// drawing to a random effect from the registered pool.</summary>
public sealed class MatchScreen
{
    private readonly IMatchEffect[] _effects;
    private readonly OwnAvatarCache _ownAvatar;
    private readonly PendingMatchContext _pending;
    private readonly SessionBootstrapper _bootstrap;
    private readonly Random _rng = new();

    private IMatchEffect? _current;
    private ISharedImmediateTexture? _peerAvatarTex;
    private Guid _cachedPeerId;

    public MatchScreen(
        IEnumerable<IMatchEffect> effects,
        OwnAvatarCache ownAvatar,
        PendingMatchContext pending,
        SessionBootstrapper bootstrap)
    {
        _effects = effects.ToArray();
        _ownAvatar = ownAvatar;
        _pending = pending;
        _bootstrap = bootstrap;
    }

    public void OnShow()
    {
        // Cached avatar shows instantly; the refresh swaps in a just-changed one when it lands.
        _ownAvatar.Refresh();
        EnsurePeerAvatar();

        MatchContent.OwnAvatar = _ownAvatar.Texture;
        MatchContent.PeerAvatar = _peerAvatarTex;
        MatchContent.OwnName = string.IsNullOrWhiteSpace(_bootstrap.LastDisplayName)
            ? Loc.T("deck.match_you")
            : _bootstrap.LastDisplayName!;
        MatchContent.PeerName = _pending.HasPending && !string.IsNullOrWhiteSpace(_pending.PeerDisplayName)
            ? _pending.PeerDisplayName
            : Loc.T("deck.match_your_match");

        // Consume match once shown.
        _pending.Clear();

        _current = _effects.Length > 0 ? _effects[_rng.Next(_effects.Length)] : null;
        _current?.OnShow();
    }

    public void Draw()
    {
        _current?.Draw();
    }

    private void EnsurePeerAvatar()
    {
        if (!_pending.HasPending)
        {
            _peerAvatarTex = null;
            _cachedPeerId = Guid.Empty;
            return;
        }
        if (_cachedPeerId == _pending.PeerProfileId && _peerAvatarTex is not null)
        {
            return;
        }
        var cacheDir = ImageCacheCleaner.MatchOverlayCacheDir;
        _peerAvatarTex = AvatarDiskCache.Store(cacheDir, _pending.PeerProfileId.ToString(), _pending.PeerAvatarWebp);
        _cachedPeerId = _pending.PeerProfileId;
        ImageCacheCleaner.ClearExcept(cacheDir, "self_", _pending.PeerProfileId.ToString());
    }
}
