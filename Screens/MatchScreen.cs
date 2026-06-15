using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>The match host. On a real match it captures the matched pair's avatars and names into
/// <see cref="MatchContent"/>, then picks a random effect from the registered pool and delegates drawing
/// to it — so every match is "treated" to a random celebration.</summary>
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

        // The match is consumed once shown — clearing here means dismissing via any effect button can't re-trigger it.
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
        var cacheDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "MatchOverlayCache");
        _peerAvatarTex = AvatarDiskCache.Store(cacheDir, _pending.PeerProfileId.ToString(), _pending.PeerAvatarWebp);
        _cachedPeerId = _pending.PeerProfileId;
    }
}
