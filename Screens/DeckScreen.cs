using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Flairs;
using AetherLove.Shared.Matching;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>Swipe deck.</summary>
public partial class DeckScreen : IDisposable
{
    private readonly ScreenRouter _router;
    private readonly ProfileScreen _profileScreen;
    private readonly AetherLoveHubClient _hubClient;
    private readonly PendingMatchContext _pendingMatch;
    private readonly NotificationCenter _notifications;
    private readonly OwnAvatarCache _ownAvatar;
    private readonly PulseService _pulse;
    private readonly FlairCatalog _flairCatalog;
    private readonly SettingsScreen _settingsScreen;

    private readonly List<DeckCardDto> _cards = new();
    private readonly HashSet<Guid> _processedThisPeriod = new();
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _portraitTextures = new();

    private DateTimeOffset? _nextPullAtUtc;
    private bool _noPoolForPreferences;

    private volatile bool _refreshInFlight;
    private volatile string? _refreshError;
    /// <summary>Set by a server DeckRefresh push; consumed on the next Draw.</summary>
    private volatile bool _forceRefresh;
    /// <summary>Set when the user leaves the deck while the slot is running; the next deck apply drops the
    /// stale card instead of pinning it.</summary>
    private bool _discardCurrentOnRefresh;
    /// <summary>Throttles the background pre-fetch so a failing server isn't hammered per frame.</summary>
    private DateTimeOffset _lastBackgroundRefreshUtc = DateTimeOffset.MinValue;
    private static readonly TimeSpan BackgroundRefreshRetry = TimeSpan.FromSeconds(5);
    /// <summary>Fetched deck waiting to be applied on the UI thread, never mid-gesture.</summary>
    private volatile MatchDeckDto? _pendingDeck;
    private CancellationTokenSource _cts = new();

    private volatile bool _pendingMatchNav;

    private readonly CooldownScene _cooldownScene = new();

    // Loader sticks for at least MinLoaderDuration so a fast fetch doesn't flash the cooldown screen.
    private DateTimeOffset _loaderShownAt;
    private static readonly TimeSpan MinLoaderDuration = TimeSpan.FromSeconds(1);

    // Deck-expiry nudge: warn when the next pull is within this window and cards still remain.
    private static readonly TimeSpan ExpiryWarnThreshold = TimeSpan.FromMinutes(5);

    private float _dragX;
    private float _dragY;
    private bool _isDragging;

    private bool _isThrowingCard;
    private float _throwProgress;
    private bool _throwRight;
    private const float ThrowSpeed = 2.85f;

    private bool _isSnappingBack;
    private float _snapDragX;
    private float _snapDragY;
    private float _snapProgress;
    private const float SnapSpeed = 4f;

    private float _nopeHover;
    private float _likeHover;

    // A matched entry blocks undoing past it, mirroring the server, which refuses to undo a swipe that became a match.
    private sealed class SwipedCard
    {
        public required DeckCardDto Card { get; init; }
        public required bool WasLike { get; init; }
        public bool WasMatch;
    }
    private readonly List<SwipedCard> _undoStack = [];
    private const int UndoStackCap = 10;
    private volatile int _reswipesRemaining;

    private bool _isUndoing;
    private float _undoProgress;
    private bool _undoFromRight;
    private const float UndoSpeed = 2.85f;

    // Reswipe intro overlay; panel height is re-measured each frame.
    private bool _showReswipeIntro;
    private float _reswipeIntroHeight;

    private bool _isDeferring;
    private float _deferProgress;
    private const float DeferSpeed = 2.85f;

    private const float CardHeight = 560f;
    private const float SwipeThreshold = 100f;

    public DeckScreen(
        ScreenRouter router,
        ProfileScreen profileScreen,
        AetherLoveHubClient hubClient,
        PendingMatchContext pendingMatch,
        NotificationCenter notifications,
        OwnAvatarCache ownAvatar,
        PulseService pulse,
        FlairCatalog flairCatalog,
        SettingsScreen settingsScreen)
    {
        _router = router;
        _profileScreen = profileScreen;
        _hubClient = hubClient;
        _pendingMatch = pendingMatch;
        _notifications = notifications;
        _ownAvatar = ownAvatar;
        _pulse = pulse;
        _flairCatalog = flairCatalog;
        _settingsScreen = settingsScreen;
        _notifications.DeckRefreshRequested += OnDeckRefreshRequested;
    }

    private void OnDeckRefreshRequested() => _forceRefresh = true;

    private void OpenSupporterSettings()
    {
        _settingsScreen.RequestSupporterView(returnTo: _router.Current);
        _router.Navigate(Screen.Settings);
    }

    /// <summary>Called when the user leaves the deck for anything other than its own view-profile page.</summary>
    public void MarkDeckLeft() => _discardCurrentOnRefresh = true;

    /// <summary>Runs each frame while the deck is not the active screen; pre-fetches the next deck once the
    /// slot elapses so it is already in hand on return.</summary>
    public void MaybeBackgroundRefresh()
    {
        var now = DateTimeOffset.UtcNow;
        if (_discardCurrentOnRefresh
            && !_refreshInFlight
            && _pendingDeck is null
            && _nextPullAtUtc.HasValue
            && now >= _nextPullAtUtc.Value
            && now - _lastBackgroundRefreshUtc >= BackgroundRefreshRetry)
        {
            _lastBackgroundRefreshUtc = now;
            StartRefresh();
        }
    }

    public void OnShow()
    {
        _dragX = _dragY = 0;
        _isThrowingCard = _isSnappingBack = _isUndoing = _isDeferring = false;
        _throwProgress = _snapProgress = _undoProgress = _deferProgress = 0f;
        _showReswipeIntro = false;
        _showSuperlikeIntro = false;
        _cooldownScene.Reset();

        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _forceRefresh = false;

        // Warm the own-avatar cache so a first-ever match overlay never shows the grey fallback.
        _ownAvatar.Refresh(onlyIfCold: true);

        // An emptied deck is re-pulled by Draw() once NextPullAtUtc passes.
        var pullDue = _nextPullAtUtc.HasValue && DateTimeOffset.UtcNow >= _nextPullAtUtc.Value;
        if (_discardCurrentOnRefresh && pullDue)
        {
            // Drop the stale deck now so the old card never flashes.
            _cards.Clear();
            _processedThisPeriod.Clear();
        }
        if (_cards.Count == 0 || pullDue)
        {
            StartRefresh();
        }
    }

    public void Draw()
    {
        if (_forceRefresh)
        {
            _forceRefresh = false;
            StartRefresh();
        }

        ApplyPendingDeckIfReady();

        if (_pendingMatchNav)
        {
            _pendingMatchNav = false;
            _router.Navigate(Screen.Match);
            return;
        }

        // On the deck the fresh deck loads behind the current card; if the slot elapsed off-screen, the stale card is dropped instead.
        var pullDue = _nextPullAtUtc.HasValue && DateTimeOffset.UtcNow >= _nextPullAtUtc.Value;
        if (!_refreshInFlight && pullDue)
        {
            if (_discardCurrentOnRefresh)
            {
                // Resumed onto the deck without an OnShow (e.g. reopened straight to it): drop the stale deck.
                _cards.Clear();
                _processedThisPeriod.Clear();
            }
            StartRefresh();
        }
        else if (!pullDue && _pendingDeck is null && _cards.Count > 0)
        {
            // Settled on the deck with the slot still running: a later on-deck refresh keeps the current card.
            _discardCurrentOnRefresh = false;
        }

        var dt = (float)ImGui.GetIO().DeltaTime;
        if (_isThrowingCard)
        {
            AnimationHelper.ClampedProgress(ref _throwProgress, dt, ThrowSpeed, forward: true);
            if (_throwProgress >= 1f)
            {
                CompleteSwipe();
            }
        }
        else if (_isSnappingBack)
        {
            AnimationHelper.ClampedProgress(ref _snapProgress, dt, SnapSpeed, forward: true);
            _dragX = AnimationHelper.Lerp(_snapDragX, 0f, _snapProgress);
            _dragY = AnimationHelper.Lerp(_snapDragY, 0f, _snapProgress);
            if (_snapProgress >= 1f)
            {
                _dragX = _dragY = 0f;
                _isSnappingBack = false;
            }
        }
        else if (_isUndoing)
        {
            AnimationHelper.ClampedProgress(ref _undoProgress, dt, UndoSpeed, forward: true);
            if (_undoProgress >= 1f)
            {
                _isUndoing = false;
                _undoProgress = 0f;
                _dragX = _dragY = 0f;
            }
        }
        else if (_isDeferring)
        {
            AnimationHelper.ClampedProgress(ref _deferProgress, dt, DeferSpeed, forward: true);
            if (_deferProgress >= 1f)
            {
                CompleteDeferral();
            }
        }

        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        const float ButtonAreaHeight = 74f;
        const float BottomMargin = 0f;
        var usableHeight = windowSize.Y;
        var centerX = windowPos.X + windowSize.X * 0.5f;
        var centerY = windowPos.Y + Px(CardHeight) * 0.5f;

        if (_cards.Count == 0)
        {
            DrawEmptyState(windowPos, windowSize);
            return;
        }

        // A superliked card gets a hero reveal instead of the deck stack.
        var topIsSuperlike = _cards[0].SuperlikedYou;
        if (topIsSuperlike)
        {
            DrawSuperlikeScene(windowPos, windowSize);
            DrawCard(_cards[0], centerX, RevealCardCenterY(windowPos), RevealCardScale, 1f, true, windowSize);
        }
        else
        {
            var cardsToShow = Math.Min(3, _cards.Count);
            for (int i = cardsToShow - 1; i >= 0; i--)
            {
                var scale = i == 0 ? 1.0f : (i == 1 ? 0.98f : 0.96f);
                var alpha = i == 0 ? 1.0f : (i == 1 ? 0.7f : 0.5f);
                var offsetY = i == 0 ? 0f : (i == 1 ? Px(-5f) : Px(-10f));
                DrawCard(_cards[i], centerX, centerY + offsetY, scale, alpha, i == 0, windowSize);
            }
        }

        DrawActionButtons(centerX, windowPos.Y + usableHeight - Px(BottomMargin) - Px(ButtonAreaHeight) + Px(10f));

        if (!topIsSuperlike)
        {
            DrawDeckExpiryWarning(windowPos, windowSize);
        }

        DrawReswipeIntroOverlay(windowPos, windowSize);
        DrawSuperlikeIntroOverlay(windowPos, windowSize);
        SupporterInfoPopup.Draw(windowPos, windowSize, OpenSupporterSettings);
    }

    /// <summary>Shown when cards remain and the next pull is within <see cref="ExpiryWarnThreshold"/>;
    /// drawn last so it sits over the card.</summary>
    private void DrawDeckExpiryWarning(Vector2 windowPos, Vector2 windowSize)
    {
        if (_cards.Count == 0 || !_nextPullAtUtc.HasValue)
        {
            return;
        }
        var remaining = _nextPullAtUtc.Value - DateTimeOffset.UtcNow;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }
        if (remaining > ExpiryWarnThreshold)
        {
            return;
        }

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var iconFont = Plugin.PluginInterface.UiBuilder.FontIcon;

        var label = Loc.T("deck.next_deck", $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}");

        ImGui.PushFont(iconFont);
        var iconStr = FontAwesomeIcon.Clock.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconStr);
        ImGui.PopFont();
        var textSz = ImGui.CalcTextSize(label);

        var padX = Px(13f);
        var padY = Px(6f);
        var gap = Px(7f);
        var pillW = iconSz.X + gap + textSz.X + padX * 2f;
        var pillH = MathF.Max(iconSz.Y, textSz.Y) + padY * 2f;
        var pillTL = new Vector2(windowPos.X + (windowSize.X - pillW) * 0.5f, windowPos.Y + Px(8f));
        var rounding = pillH * 0.5f;

        dl.AddRectFilled(pillTL, pillTL + new Vector2(pillW, pillH), UiColors.DeckExpiryWarnFill, rounding);
        dl.AddRect(pillTL, pillTL + new Vector2(pillW, pillH), t.AccentU32, rounding, ImDrawFlags.None, Px(1.5f));

        var iconX = pillTL.X + padX;
        ImGui.PushFont(iconFont);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(iconX, pillTL.Y + (pillH - iconSz.Y) * 0.5f), t.AccentLightU32, iconStr);
        ImGui.PopFont();
        dl.AddText(new Vector2(iconX + iconSz.X + gap, pillTL.Y + (pillH - textSz.Y) * 0.5f),
            0xFFFFFFFF, label);
    }

    private void StartRefresh()
    {
        if (_refreshInFlight || _pendingDeck is not null)
        {
            return;
        }
        _refreshInFlight = true;
        _refreshError = null;
        _loaderShownAt = DateTimeOffset.UtcNow;
        var ct = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var deck = await _hubClient.GetMatchDeckAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                CachePortraits(deck.Cards);

                // Draw applies it at a safe point, never mid-gesture.
                _pendingDeck = deck;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _refreshError = HubErrorText.Localize(ex);
                Plugin.Log.Warning(ex, "[DeckScreen] GetMatchDeckAsync failed.");
            }
            finally
            {
                _refreshInFlight = false;
            }
        }, ct);
    }

    /// <summary>Applies a fetched deck on the UI thread, never mid-gesture; a refresh replaces the queue
    /// behind the pinned card in hand, not the card itself.</summary>
    private void ApplyPendingDeckIfReady()
    {
        var pending = _pendingDeck;
        if (pending is null)
        {
            return;
        }
        if (_isDragging || _isThrowingCard || _isSnappingBack || _isUndoing || _isDeferring || _dragX != 0f || _dragY != 0f)
        {
            return;
        }
        _pendingDeck = null;

        // Keep the card in hand on top unless the user left the deck while the slot elapsed.
        var pinned = (!_discardCurrentOnRefresh && _cards.Count > 0) ? _cards[0] : null;
        _discardCurrentOnRefresh = false;
        _processedThisPeriod.Clear();
        _cards.Clear();
        if (pinned is not null)
        {
            // The fetch wiped the portrait cache and the pinned card isn't in the fresh deck; rebuild its image or it renders blank.
            EnsurePortraitCached(pinned);
            _cards.Add(pinned);
            _cards.AddRange(pending.Cards.Where(c => c.ProfileId != pinned.ProfileId));
        }
        else
        {
            _cards.AddRange(pending.Cards);
        }
        // A fresh deck re-injects un-actioned superlikers on top server-side, so parked copies would duplicate.
        _deferredSuperlikes.Clear();
        _nextPullAtUtc = pending.NextPullAtUtc;
        _noPoolForPreferences = pending.NoPoolForPreferences;
        _reswipesRemaining = pending.ReswipesRemaining;
        _superlikesRemaining = pending.SuperlikesRemaining;
        _superlikesPerDay = pending.SuperlikesPerDay;
    }

    private void CachePortraits(IEnumerable<DeckCardDto> cards)
    {
        var cacheDir = ImageCacheCleaner.DeckCacheDir;
        try
        {
            Directory.CreateDirectory(cacheDir);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[DeckScreen] Failed to create deck cache dir.");
            return;
        }

        // A new deck fully replaces the old one; drop the previous deck's portraits.
        ImageCacheCleaner.ClearDir(cacheDir);

        foreach (var c in cards)
        {
            try
            {
                var path = Path.Combine(cacheDir, $"{c.ProfileId}{ImageFormat.ExtensionFor(c.PortraitWebp)}");
                File.WriteAllBytes(path, c.PortraitWebp);
                _portraitTextures[c.ProfileId] = Plugin.TextureProvider.GetFromFile(path);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, $"[DeckScreen] Failed to cache portrait for {c.ProfileId}.");
            }
        }
    }

    /// <summary>Restores a card's portrait file from its in-memory bytes; a reswiped card can come from an
    /// earlier deck whose portraits were cleared on the last pull.</summary>
    private void EnsurePortraitCached(DeckCardDto card)
    {
        if (card.PortraitWebp is null || card.PortraitWebp.Length == 0)
        {
            return;
        }
        try
        {
            var cacheDir = ImageCacheCleaner.DeckCacheDir;
            Directory.CreateDirectory(cacheDir);
            var path = Path.Combine(cacheDir, $"{card.ProfileId}{ImageFormat.ExtensionFor(card.PortraitWebp)}");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, card.PortraitWebp);
                _portraitTextures[card.ProfileId] = Plugin.TextureProvider.GetFromFile(path);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[DeckScreen] Failed to re-cache reswiped portrait for {card.ProfileId}.");
        }
    }

    private void DrawEmptyState(Vector2 windowPos, Vector2 windowSize)
    {
        var loaderActive = _refreshInFlight
            || (DateTimeOffset.UtcNow - _loaderShownAt) < MinLoaderDuration;
        if (loaderActive)
        {
            Widgets.LoadingIndicator.Draw();
            return;
        }

        var heading = _noPoolForPreferences ? Loc.T("deck.no_pool_heading") : Loc.T("deck.cooldown_heading");
        var body = _noPoolForPreferences ? Loc.T("deck.no_pool_body") : Loc.T("deck.cooldown_body");

        string? timer = null;
        if (_nextPullAtUtc.HasValue)
        {
            var remaining = _nextPullAtUtc.Value - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }
            timer = remaining.TotalSeconds < 1
                ? Loc.T("deck.new_matches_ready")
                : $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m {remaining.Seconds:D2}s";
        }

        var error = _refreshError is not null ? Loc.T("deck.server_error", _refreshError) : null;

        // Reswipe offer: last swipe still undoable (not a match, quota left).
        var reswipeLabel = _undoStack.Count > 0 && !_undoStack[^1].WasMatch && _reswipesRemaining > 0
            ? Loc.T("deck.cooldown_reswipe_btn")
            : null;
        if (_cooldownScene.Draw(windowPos, windowSize, heading, body, timer, error, reswipeLabel))
        {
            OnReswipeClicked();
        }

        // Parked superlikes stay reachable from the cooldown screen.
        if (_deferredSuperlikes.Count > 0)
        {
            DrawViewSuperlikesButton(
                new Vector2(windowPos.X + windowSize.X * 0.5f, windowPos.Y + windowSize.Y - Px(38f)),
                Px(30f) * 0.68f);
        }

        // Hosted here too: the empty-state path returns before the card-view overlay would draw it.
        DrawReswipeIntroOverlay(windowPos, windowSize);
    }

    public void Dispose()
    {
        _notifications.DeckRefreshRequested -= OnDeckRefreshRequested;
    }

    private void DrawCard(DeckCardDto profile, float centerX, float centerY,
                          float scale, float alpha, bool isTopCard, Vector2 windowSize)
    {
        var scaledWidth = windowSize.X * scale;
        var scaledHeight = Px(CardHeight) * scale;
        var cardTopLeft = new Vector2(centerX - scaledWidth * 0.5f,
                                       centerY - scaledHeight * 0.5f);

        if (isTopCard)
        {
            if (_isThrowingCard)
            {
                var throwOffset = (_throwRight ? 1 : -1) * windowSize.X * _throwProgress;
                cardTopLeft.X += throwOffset;
                cardTopLeft.Y += Math.Abs(throwOffset) * 0.3f;
                alpha *= 1f - _throwProgress;
            }
            else if (_isUndoing)
            {
                // Reverse of the throw: the card flies back in from the side it left.
                var undoOffset = (_undoFromRight ? 1 : -1) * windowSize.X * (1f - _undoProgress);
                cardTopLeft.X += undoOffset;
                cardTopLeft.Y += Math.Abs(undoOffset) * 0.3f;
                alpha *= _undoProgress;
            }
            else if (_isDeferring)
            {
                // Decide later: slides straight down and fades, distinct from the horizontal swipe-throw.
                cardTopLeft.Y += windowSize.Y * _deferProgress;
                alpha *= 1f - _deferProgress;
            }
            else
            {
                cardTopLeft.X += _dragX;
                cardTopLeft.Y += _dragY * 0.5f;
            }
        }

        var cardBottomRight = cardTopLeft + new Vector2(scaledWidth, scaledHeight);

        var rotation = 0f;
        if (isTopCard && _isThrowingCard)
        {
            rotation = (_throwRight ? 1 : -1) * 25f * _throwProgress;
        }
        else if (isTopCard && _isUndoing)
        {
            rotation = (_undoFromRight ? 1 : -1) * 25f * (1f - _undoProgress);
        }
        else if (isTopCard)
        {
            rotation = (_dragX / windowSize.X) * 25f;
        }

        var drawList = ImGui.GetWindowDrawList();
        var alphaU32 = (uint)(alpha * 255) << 24;
        var tintColor = alphaU32 | 0x00FFFFFF;

        _portraitTextures.TryGetValue(profile.ProfileId, out var portraitTex);
        var wrap = portraitTex?.GetWrapOrDefault();

        var uv0 = Vector2.Zero;
        var uv1 = Vector2.One;
        if (wrap != null)
        {
            var imgAspect = (float)wrap.Width / wrap.Height;
            var tgtAspect = scaledWidth / scaledHeight;
            if (imgAspect >= tgtAspect)
            {
                var uvW = tgtAspect / imgAspect;
                var uvX = (1f - uvW) * 0.5f;
                uv0 = new Vector2(uvX, 0f);
                uv1 = new Vector2(uvX + uvW, 1f);
            }
            else
            {
                uv0 = new Vector2(0f, 0f);
                uv1 = new Vector2(1f, imgAspect / tgtAspect);
            }
        }

        const uint fallbackColor = 0xFF3A3A3Au;

        if (Math.Abs(rotation) < 0.01f)
        {
            if (wrap != null)
            {
                drawList.AddImage(wrap.Handle, cardTopLeft, cardBottomRight, uv0, uv1, tintColor);
            }
            else
            {
                drawList.AddRectFilled(cardTopLeft, cardBottomRight,
                                       alphaU32 | (fallbackColor & 0x00FFFFFF), 0f);
            }
        }
        else
        {
            var center = (cardTopLeft + cardBottomRight) * 0.5f;
            var halfSize = new Vector2(scaledWidth * 0.5f, scaledHeight * 0.5f);
            var radians = rotation * MathF.PI / 180f;
            var cos = MathF.Cos(radians);
            var sin = MathF.Sin(radians);

            var corners = new[]
            {
                new Vector2(-halfSize.X, -halfSize.Y),
                new Vector2(halfSize.X, -halfSize.Y),
                new Vector2(halfSize.X, halfSize.Y),
                new Vector2(-halfSize.X, halfSize.Y)
            };
            for (int i = 0; i < 4; i++)
            {
                var x = corners[i].X * cos - corners[i].Y * sin;
                var y = corners[i].X * sin + corners[i].Y * cos;
                corners[i] = center + new Vector2(x, y);
            }

            if (wrap != null)
            {
                drawList.AddImageQuad(wrap.Handle,
                                      corners[0], corners[1], corners[2], corners[3],
                                      uv0, new Vector2(uv1.X, uv0.Y),
                                      uv1, new Vector2(uv0.X, uv1.Y),
                                      tintColor);
            }
            else
            {
                drawList.AddQuadFilled(corners[0], corners[1], corners[2], corners[3],
                                       alphaU32 | (fallbackColor & 0x00FFFFFF));
            }
        }

        var viewPillTL = Vector2.Zero;
        var viewPillBR = Vector2.Zero;
        var isOverPill = false;

        var undoPillTL = Vector2.Zero;
        var undoPillBR = Vector2.Zero;
        var undoPillShown = false;

        var laterPillTL = Vector2.Zero;
        var laterPillBR = Vector2.Zero;
        var laterPillShown = false;

        if (isTopCard && !_isThrowingCard && !_isUndoing && !_isDeferring)
        {
            var cardCenter = (cardTopLeft + cardBottomRight) * 0.5f;
            var radians = rotation * MathF.PI / 180f;
            var cos = MathF.Cos(radians);
            var sin = MathF.Sin(radians);

            Vector2 Rot(Vector2 p)
            {
                var d = p - cardCenter;
                return cardCenter + new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
            }

            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();
            var wrapWidth = scaledWidth - Px(28f);

            var nameFontPtr = font;
            var nameFont = fontSize;
            using (UiFonts.H1?.Push())
            {
                nameFontPtr = ImGui.GetFont();
                nameFont = ImGui.GetFontSize();
            }
            var infoFontPtr = font;
            var raceFont = fontSize;
            using (UiFonts.H3?.Push())
            {
                infoFontPtr = ImGui.GetFont();
                raceFont = ImGui.GetFontSize();
            }

            string PillLabel = Loc.T("deck.view_profile");
            const float PillPadX = 10f;
            const float PillPadY = 5f;
            const float PillGap = 8f;
            var pillTextSz = ImGui.CalcTextSize(PillLabel);
            var pillW = pillTextSz.X + Px(PillPadX) * 2f;
            var pillH = pillTextSz.Y + Px(PillPadY) * 2f;

            const float TextBottomPad = 14f;
            var cardBottom = cardTopLeft.Y + scaledHeight;
            var pillTopY = cardBottom - Px(TextBottomPad) - pillH;
            var raceLineY = pillTopY - Px(PillGap) - raceFont;
            var nameLineY = raceLineY - Px(4f) - nameFont;
            var gradStart = nameLineY - Px(24f);

            viewPillTL = new Vector2(cardTopLeft.X + Px(14f), pillTopY);
            viewPillBR = viewPillTL + new Vector2(pillW, pillH);

            var mp = ImGui.GetMousePos();
            // isOverPill suppresses the card-drag button under any interactive element.
            var isOverViewPill = mp.X >= viewPillTL.X && mp.X <= viewPillBR.X
                              && mp.Y >= viewPillTL.Y && mp.Y <= viewPillBR.Y;
            isOverPill = isOverViewPill;

            {
                const float OverlayAlpha = 191f;
                if (MathF.Abs(rotation) < 0.01f)
                {
                    drawList.AddRectFilled(
                        new Vector2(cardTopLeft.X, gradStart),
                        cardBottomRight,
                        (uint)(alpha * OverlayAlpha) << 24);
                }
                else
                {
                    var qTL = Rot(new Vector2(cardTopLeft.X, gradStart));
                    var qTR = Rot(new Vector2(cardBottomRight.X, gradStart));
                    var qBR = Rot(cardBottomRight);
                    var qBL = Rot(new Vector2(cardTopLeft.X, cardBottomRight.Y));
                    drawList.AddQuadFilled(qTL, qTR, qBR, qBL, (uint)(alpha * OverlayAlpha) << 24);
                }
            }

            var bbMin = new Vector2(MathF.Min(MathF.Min(cardTopLeft.X, cardBottomRight.X),
                                              MathF.Min(Rot(cardTopLeft + new Vector2(scaledWidth, 0)).X, Rot(cardTopLeft + new Vector2(0, scaledHeight)).X)),
                                   MathF.Min(MathF.Min(cardTopLeft.Y, cardBottomRight.Y),
                                              MathF.Min(Rot(cardTopLeft + new Vector2(scaledWidth, 0)).Y, Rot(cardTopLeft + new Vector2(0, scaledHeight)).Y)));
            var bbMax = new Vector2(MathF.Max(MathF.Max(cardTopLeft.X, cardBottomRight.X),
                                              MathF.Max(Rot(cardTopLeft + new Vector2(scaledWidth, 0)).X, Rot(cardTopLeft + new Vector2(0, scaledHeight)).X)),
                                   MathF.Max(MathF.Max(cardTopLeft.Y, cardBottomRight.Y),
                                              MathF.Max(Rot(cardTopLeft + new Vector2(scaledWidth, 0)).Y, Rot(cardTopLeft + new Vector2(0, scaledHeight)).Y)));
            drawList.PushClipRect(bbMin, bbMax, true);

            // Icon font for the gender glyph; the ImFontPtr stays valid after PopFont (points into the atlas).
            ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
            var iconFontPtr = ImGui.GetFont();
            var iconNatSize = ImGui.GetFontSize();
            var genderIcon = profile.Gender == Gender.Female
                ? FontAwesomeIcon.Venus.ToIconString()
                : FontAwesomeIcon.Mars.ToIconString();
            var genderRawW = ImGui.CalcTextSize(genderIcon).X;
            var undoIconStr = FontAwesomeIcon.Undo.ToIconString();
            var undoIconRawW = ImGui.CalcTextSize(undoIconStr).X;
            var laterIconStr = FontAwesomeIcon.Clock.ToIconString();
            var laterIconRawW = ImGui.CalcTextSize(laterIconStr).X;
            ImGui.PopFont();

            // Measure and draw from the large icon atlas so the glyph stays crisp; falls back until that atlas is ready.
            var genderFontPtr = iconFontPtr;
            var genderNatSize = iconNatSize;
            if (UiFonts.Icon is { Available: true } genderHandle)
            {
                using (genderHandle.Push())
                {
                    genderFontPtr = ImGui.GetFont();
                    genderNatSize = ImGui.GetFontSize();
                    genderRawW = ImGui.CalcTextSize(genderIcon).X;
                }
            }

            void AddRotatedText(Vector2 pos, uint col, string text, float wrap, ImFontPtr useFont, float size)
            {
                var vtxStart = drawList.VtxBuffer.Size;
                drawList.AddText(useFont, size, pos, col, text, wrap);
                var vtxEnd = drawList.VtxBuffer.Size;
                for (int vi = vtxStart; vi < vtxEnd; vi++)
                {
                    var vtx = drawList.VtxBuffer[vi];
                    vtx.Pos = Rot(vtx.Pos);
                    drawList.VtxBuffer[vi] = vtx;
                }
            }

            void AddRotatedRectFilled(Vector2 tl, Vector2 size, uint col, float rounding)
            {
                var vtxStart = drawList.VtxBuffer.Size;
                drawList.AddRectFilled(tl, tl + size, col, rounding);
                var vtxEnd = drawList.VtxBuffer.Size;
                for (int vi = vtxStart; vi < vtxEnd; vi++)
                {
                    var vtx = drawList.VtxBuffer[vi];
                    vtx.Pos = Rot(vtx.Pos);
                    drawList.VtxBuffer[vi] = vtx;
                }
            }

            // Rounded capsule with a left-to-right two-tone fill (solid caps + gradient middle), then rotated.
            void AddRotatedPillGradient(Vector2 tl, Vector2 size, uint colLeft, uint colRight)
            {
                var r = size.Y * 0.5f;
                var vtxStart = drawList.VtxBuffer.Size;
                drawList.AddCircleFilled(new Vector2(tl.X + r, tl.Y + r), r, colLeft, 24);
                drawList.AddCircleFilled(new Vector2(tl.X + size.X - r, tl.Y + r), r, colRight, 24);
                drawList.AddRectFilledMultiColor(
                    new Vector2(tl.X + r, tl.Y), new Vector2(tl.X + size.X - r, tl.Y + size.Y),
                    colLeft, colRight, colRight, colLeft);
                var vtxEnd = drawList.VtxBuffer.Size;
                for (int vi = vtxStart; vi < vtxEnd; vi++)
                {
                    var vtx = drawList.VtxBuffer[vi];
                    vtx.Pos = Rot(vtx.Pos);
                    drawList.VtxBuffer[vi] = vtx;
                }
            }

            var nameCol = SupporterStyle.NameColor(profile.NameStyle, alphaU32 | 0x00FFFFFF);
            AddRotatedText(new Vector2(cardTopLeft.X + Px(14f), nameLineY),
                           nameCol, profile.DisplayName, wrapWidth, nameFontPtr, nameFont);
            if (profile.IsSupporter)
            {
                var nameW = MathF.Min(
                    ImGui.CalcTextSize(profile.DisplayName).X * (nameFont / fontSize), wrapWidth);
                ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
                var starFontPtr = ImGui.GetFont();
                ImGui.PopFont();
                var starTL = new Vector2(cardTopLeft.X + Px(14f) + nameW + Px(6f), nameLineY + nameFont * 0.22f);
                AddRotatedText(starTL,
                               alphaU32 | (UiColors.FavoriteStar & 0x00FFFFFF),
                               FontAwesomeIcon.Star.ToIconString(), 0f, starFontPtr, nameFont * 0.55f);
                if (isTopCard && MathF.Abs(rotation) < 0.01f
                    && ImGui.IsMouseHoveringRect(starTL - Px(2f, 2f), starTL + new Vector2(nameFont * 0.55f + Px(2f), nameFont * 0.55f + Px(2f))))
                {
                    isOverPill = true;
                    ImGui.SetTooltip(Loc.T("profile.section_supporter"));
                    // Open on release: open-on-press would arm the same-frame scrim, which dismisses the popup on release.
                    if (!_isDragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        SupporterInfoPopup.Open();
                    }
                }
            }

            // Unset segments (a profile can have no race/gender) drop with their separator so the dots never dangle.
            var infoCol = alphaU32 | 0x00DDDDDD;
            var infoX = cardTopLeft.X + Px(14f);
            const string Sep = "  ·  ";

            float MainW(string s) => ImGui.CalcTextSize(s).X * (raceFont / fontSize);
            var genderW = genderRawW * (raceFont / genderNatSize);

            var infoFirst = true;
            void AddInfoSegment(string text, ImFontPtr useFont, float width)
            {
                if (!infoFirst)
                {
                    AddRotatedText(new Vector2(infoX, raceLineY), infoCol, Sep, 0f, infoFontPtr, raceFont);
                    infoX += MainW(Sep);
                }
                AddRotatedText(new Vector2(infoX, raceLineY), infoCol, text, 0f, useFont, raceFont);
                infoX += width;
                infoFirst = false;
            }

            var raceLabel = RaceLabel(profile.Race);
            if (!string.IsNullOrEmpty(raceLabel))
            {
                AddInfoSegment(raceLabel, infoFontPtr, MainW(raceLabel));
            }
            // Only Male/Female have a glyph; None and Other render without an icon.
            if (profile.Gender is Gender.Male or Gender.Female)
            {
                AddInfoSegment(genderIcon, genderFontPtr, genderW);
            }
            var regionLabel = RegionLabel(profile.Region);
            if (!string.IsNullOrEmpty(regionLabel))
            {
                AddInfoSegment(regionLabel, infoFontPtr, MainW(regionLabel));
            }

            // A single flair pill, mirrored to the card's right edge.
            if (profile.FlairIds is { Length: > 0 })
            {
                var flairLang = FlairCatalog.ResolveLanguage(Plugin.Configuration.PluginLanguage);
                foreach (var fid in profile.FlairIds)
                {
                    var f = _flairCatalog.Get(fid);
                    if (f is null)
                    {
                        continue;
                    }
                    var label = FlairCatalog.Text(f, flairLang);
                    var flairTextSz = ImGui.CalcTextSize(label);
                    var flairW = flairTextSz.X + Px(PillPadX) * 2f;
                    var flairTL = new Vector2(cardTopLeft.X + scaledWidth - Px(14f) - flairW, pillTopY);
                    AddRotatedRectFilled(flairTL, new Vector2(flairW, pillH), HexToAbgr(f.BackgroundColor, alpha), pillH * 0.5f);
                    AddRotatedText(new Vector2(flairTL.X + Px(PillPadX), pillTopY + (pillH - flairTextSz.Y) * 0.5f),
                        ContrastText(f.BackgroundColor, alpha), label, 0f, font, fontSize);
                    if (isTopCard && MathF.Abs(rotation) < 0.01f
                        && ImGui.IsMouseHoveringRect(flairTL, flairTL + new Vector2(flairW, pillH)))
                    {
                        ImGui.SetTooltip(FlairCatalog.Description(f, flairLang));
                        if (string.Equals(f.Key, FlairKeys.Supporter, StringComparison.OrdinalIgnoreCase))
                        {
                            isOverPill = true;
                            if (!_isDragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                            {
                                SupporterInfoPopup.Open();
                            }
                        }
                    }
                    break;
                }
            }

            drawList.PopClipRect();

            var t = ThemeService.Current;
            var pillBgCol = isOverViewPill
                ? (alphaU32 | t.AccentLightRgb)
                : ((uint)(alpha * 220f) << 24 | t.AccentDarkRgb);
            var pillRound = pillH * 0.5f;

            if (MathF.Abs(rotation) < 0.01f)
            {
                drawList.AddRectFilled(viewPillTL, viewPillBR, pillBgCol, pillRound);
                drawList.AddText(font, fontSize,
                    viewPillTL + new Vector2(Px(PillPadX), Px(PillPadY)),
                    alphaU32 | 0x00FFFFFF, PillLabel);
            }
            else
            {
                var rpTL = Rot(viewPillTL);
                var rpTR = Rot(new Vector2(viewPillBR.X, viewPillTL.Y));
                var rpBR = Rot(viewPillBR);
                var rpBL = Rot(new Vector2(viewPillTL.X, viewPillBR.Y));
                drawList.AddQuadFilled(rpTL, rpTR, rpBR, rpBL, pillBgCol);
                AddRotatedText(viewPillTL + new Vector2(Px(PillPadX), Px(PillPadY)),
                    alphaU32 | 0x00FFFFFF, PillLabel, 0f, font, fontSize);
            }

            // Reswipe pill stays put after an undo (grayed once quota runs out) so undos can chain.
            if (_undoStack.Count > 0)
            {
                var undoW = undoIconRawW * (fontSize / iconNatSize) + Px(PillPadX) * 2f;
                undoPillTL = new Vector2(cardTopLeft.X + Px(14f), cardTopLeft.Y + Px(14f));
                undoPillBR = undoPillTL + new Vector2(undoW, pillH);
                undoPillShown = true;
                if (mp.X >= undoPillTL.X && mp.X <= undoPillBR.X && mp.Y >= undoPillTL.Y && mp.Y <= undoPillBR.Y)
                {
                    isOverPill = true;
                }

                var undoEnabled = _reswipesRemaining > 0;
                var undoBg = undoEnabled
                    ? ((uint)(alpha * 220f) << 24 | t.AccentDarkRgb)
                    : ((uint)(alpha * 170f) << 24 | 0x00262626u);
                var undoCol = undoEnabled
                    ? (alphaU32 | 0x00FFFFFFu)
                    : (alphaU32 | (UiColors.TextMuted & 0x00FFFFFFu));
                AddRotatedRectFilled(undoPillTL, undoPillBR - undoPillTL, undoBg, pillH * 0.5f);
                AddRotatedText(new Vector2(undoPillTL.X + Px(PillPadX), undoPillTL.Y + Px(PillPadY)),
                    undoCol, undoIconStr, 0f, iconFontPtr, fontSize);
            }

            // Decide-later pill; greyed on the last card unless it's a superlike, whose deferral parks it behind the star button.
            {
                var laterW = laterIconRawW * (fontSize / iconNatSize) + Px(PillPadX) * 2f;
                laterPillTL = new Vector2(cardTopLeft.X + scaledWidth - Px(14f) - laterW, cardTopLeft.Y + Px(14f));
                laterPillBR = laterPillTL + new Vector2(laterW, pillH);
                laterPillShown = true;
                if (mp.X >= laterPillTL.X && mp.X <= laterPillBR.X && mp.Y >= laterPillTL.Y && mp.Y <= laterPillBR.Y)
                {
                    isOverPill = true;
                }

                var laterEnabled = _cards.Count > 1 || profile.SuperlikedYou;
                if (laterEnabled)
                {
                    var pillVtx = drawList.VtxBuffer.Size;
                    AddRotatedPillGradient(laterPillTL, laterPillBR - laterPillTL,
                        (uint)(alpha * 220f) << 24 | t.SecondaryPillStartRgb,
                        (uint)(alpha * 220f) << 24 | t.SecondaryPillEndRgb);
                    // Matches the selected nav button's sweep; reduce-motion keeps the static fill.
                    if (!AccessibilityService.ReduceMotion)
                    {
                        var pillAlpha = alpha * (220f / 255f);
                        GradientSweepVertices(drawList, pillVtx,
                            t.SecondaryStart with { W = pillAlpha },
                            t.SecondaryEnd with { W = pillAlpha },
                            (float)ImGui.GetTime() * 1.6f);
                    }
                }
                else
                {
                    AddRotatedRectFilled(laterPillTL, laterPillBR - laterPillTL,
                        (uint)(alpha * 170f) << 24 | UiColors.DisabledPillFillRgb, pillH * 0.5f);
                }
                var laterCol = laterEnabled
                    ? (alphaU32 | 0x00FFFFFFu)
                    : (alphaU32 | (UiColors.TextMuted & 0x00FFFFFFu));
                AddRotatedText(new Vector2(laterPillTL.X + Px(PillPadX), laterPillTL.Y + Px(PillPadY)),
                    laterCol, laterIconStr, 0f, iconFontPtr, fontSize);
            }

            if (_isDragging && !_isSnappingBack)
            {
                if (_dragX > Px(30))
                {
                    var likeAlpha = Math.Clamp(_dragX / Px(SwipeThreshold), 0f, 1f);
                    var likeColor = ((uint)(likeAlpha * 200) << 24) | 0x0081C784;
                    var p = Px(5, 5);
                    drawList.AddQuad(
                        Rot(cardTopLeft + p),
                        Rot(cardTopLeft + new Vector2(scaledWidth - Px(5), Px(5))),
                        Rot(cardBottomRight - p),
                        Rot(cardTopLeft + new Vector2(Px(5), scaledHeight - Px(5))),
                        likeColor, Px(4f));
                    AddRotatedText(cardTopLeft + Px(20, 20), ((uint)(likeAlpha * 255) << 24) | 0x00FFFFFF, Loc.T("deck.like"), 0f, font, fontSize);
                }
                else if (_dragX < -Px(30))
                {
                    var nopeAlpha = Math.Clamp(-_dragX / Px(SwipeThreshold), 0f, 1f);
                    var nopeColor = ((uint)(nopeAlpha * 200) << 24) | 0x007373E5;
                    var p = Px(5, 5);
                    drawList.AddQuad(
                        Rot(cardTopLeft + p),
                        Rot(cardTopLeft + new Vector2(scaledWidth - Px(5), Px(5))),
                        Rot(cardBottomRight - p),
                        Rot(cardTopLeft + new Vector2(Px(5), scaledHeight - Px(5))),
                        nopeColor, Px(4f));
                    AddRotatedText(cardTopLeft + new Vector2(scaledWidth - Px(70), Px(20)), ((uint)(nopeAlpha * 255) << 24) | 0x00FFFFFF, Loc.T("deck.nope"), 0f, font, fontSize);
                }
            }
        }

        if (isTopCard && !_isThrowingCard && !_isSnappingBack && !_isUndoing && !_isDeferring)
        {
            ImGui.SetCursorScreenPos(cardTopLeft);
            if (!isOverPill)
            {
                ImGui.InvisibleButton("##cardDrag", new Vector2(scaledWidth, scaledHeight));

                if (ImGui.IsItemActive())
                {
                    _isDragging = true;
                    var delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
                    _dragX = delta.X;
                    _dragY = delta.Y;
                }
                else if (_isDragging)
                {
                    _isDragging = false;
                    if (Math.Abs(_dragX) > Px(SwipeThreshold))
                    {
                        StartThrow(_dragX > 0);
                    }
                    else
                    {
                        StartSnapBack();
                    }
                    ImGui.ResetMouseDragDelta();
                }
            }
            else
            {
                ImGui.Dummy(new Vector2(scaledWidth, scaledHeight));
                if (_isDragging)
                {
                    _isDragging = false;
                    StartSnapBack();
                    ImGui.ResetMouseDragDelta();
                }
            }

            if (viewPillBR != Vector2.Zero)
            {
                ImGui.SetCursorScreenPos(viewPillTL);
                if (ImGui.InvisibleButton("##viewProfile", viewPillBR - viewPillTL))
                {
                    _profileScreen.SetProfile(profile.ProfileId, ProfileSource.Deck);
                    _router.Navigate(Screen.Profile);
                }
            }

            if (undoPillShown)
            {
                ImGui.SetCursorScreenPos(undoPillTL);
                if (ImGui.InvisibleButton("##reswipe", undoPillBR - undoPillTL))
                {
                    OnReswipeClicked();
                }
                if (ImGui.IsItemHovered())
                {
                    string tip;
                    if (_reswipesRemaining > 0)
                    {
                        tip = Loc.T("deck.reswipe_tooltip");
                    }
                    else
                    {
                        var remaining = DailyResetRemaining();
                        tip = Loc.T("deck.reswipe_cooldown", (int)remaining.TotalHours, remaining.Minutes);
                    }
                    ImGui.SetTooltip(tip);
                }
            }

            if (laterPillShown)
            {
                ImGui.SetCursorScreenPos(laterPillTL);
                if (ImGui.InvisibleButton("##decideLater", laterPillBR - laterPillTL))
                {
                    OnDecideLaterClicked();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(_cards.Count > 1
                        ? Loc.T("deck.decide_later_tooltip")
                        : Loc.T("deck.decide_later_disabled"));
                }
            }
        }
    }

    private static readonly Vector4 NopeTop = new(1.00f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 NopeBottom = new(0.90f, 0.24f, 0.30f, 1f);
    private static readonly Vector4 LikeTop = new(0.42f, 0.88f, 0.56f, 1f);
    private static readonly Vector4 LikeBottom = new(0.18f, 0.72f, 0.42f, 1f);

    private void DrawActionButtons(float centerX, float topY)
    {
        var radius = Px(30f);
        var gap = Px(88f);
        var cy = topY + radius - Px(8f);
        var topIsReveal = _cards.Count > 0 && _cards[0].SuperlikedYou;

        if (DrawActionButton("##deckNope", new Vector2(centerX - gap * 0.5f - radius, cy), radius,
                FontAwesomeIcon.Times, NopeTop, NopeBottom, ref _nopeHover))
        {
            StartThrow(false);
        }
        // Middle slot: parked superlikes take priority, then send-superlike; the reveal shows neither.
        if (_deferredSuperlikes.Count > 0 && !topIsReveal)
        {
            DrawViewSuperlikesButton(new Vector2(centerX, cy + Px(4f)), radius * 0.68f);
        }
        else if (!topIsReveal)
        {
            DrawSuperlikeButton(new Vector2(centerX, cy + Px(4f)), radius * 0.68f);
        }
        if (DrawActionButton("##deckLike", new Vector2(centerX + gap * 0.5f + radius, cy), radius,
                FontAwesomeIcon.Heart, LikeTop, LikeBottom, ref _likeHover))
        {
            StartThrow(true);
        }
    }

    private bool DrawActionButton(string id, Vector2 center, float radius, FontAwesomeIcon icon,
        Vector4 colTop, Vector4 colBottom, ref float hover)
    {
        const uint shadowCol = 0x44000000u;
        const uint sheenCol = 0x26FFFFFFu;

        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
        var clicked = ImGui.InvisibleButton(id, new Vector2(radius * 2f, radius * 2f));
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        var dt = (float)ImGui.GetIO().DeltaTime;
        AnimationHelper.ClampedProgress(ref hover, dt, 7f, hovered);
        var r = radius * (1f + 0.10f * hover) * (held ? 0.93f : 1f);

        var top = Vector4.Lerp(colTop, Vector4.Min(colTop * 1.16f, Vector4.One), hover);
        var bot = Vector4.Lerp(colBottom, colTop, 0.12f + 0.22f * hover);

        dl.AddCircleFilled(center + new Vector2(0f, Px(3f)), r, shadowCol, 48);
        if (hover > 0.01f)
        {
            dl.AddCircleFilled(center, r + Px(6f) * hover, ToU32(colTop, 0.22f * hover), 56);
        }
        dl.AddCircleFilled(center, r, ToU32(bot), 56);
        dl.AddCircleFilled(center - new Vector2(0f, r * 0.16f), r * 0.88f, ToU32(top), 56);
        dl.AddCircleFilled(center - new Vector2(r * 0.30f, r * 0.40f), r * 0.26f, sheenCol, 24);
        dl.AddCircle(center, r, ToU32(top, 0.85f), 56, Px(1.6f));

        IconDraw.AddCentered(dl, icon, r * 0.9f, center, 0xFFFFFFFFu);

        return clicked;
    }

    private static uint ToU32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    private static uint ToU32(Vector4 c, float a) => ImGui.ColorConvertFloat4ToU32(new Vector4(c.X, c.Y, c.Z, a));

    private void StartThrow(bool right)
    {
        if (_isThrowingCard || _cards.Count == 0)
        {
            return;
        }
        _throwRight = right;
        _isThrowingCard = true;
        _throwProgress = 0f;
    }

    private void StartSnapBack()
    {
        _snapDragX = _dragX;
        _snapDragY = _dragY;
        _isSnappingBack = true;
        _snapProgress = 0f;
    }

    private void CompleteSwipe()
    {
        if (_cards.Count == 0)
        {
            return;
        }

        var swipedCard = _cards[0];
        var liked = _throwRight;
        var superliked = _throwSuperlike && liked;
        _throwSuperlike = false;
        if (superliked)
        {
            _superlikesRemaining = Math.Max(0, _superlikesRemaining - 1);
        }
        _cards.RemoveAt(0);
        _processedThisPeriod.Add(swipedCard.ProfileId);
        _undoStack.Add(new SwipedCard { Card = swipedCard, WasLike = liked });
        if (_undoStack.Count > UndoStackCap)
        {
            _undoStack.RemoveAt(0);
        }
        _pulse.MarkActivity();

        _isThrowingCard = false;
        _throwProgress = 0f;
        _dragX = _dragY = 0f;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _hubClient.SwipeAsync(
                    swipedCard.ProfileId,
                    superliked ? SwipeDirection.Superlike : liked ? SwipeDirection.Like : SwipeDirection.Pass,
                    CancellationToken.None).ConfigureAwait(false);
                if (result.IsMatch)
                {
                    var entry = _undoStack.FindLast(e => e.Card.ProfileId == swipedCard.ProfileId);
                    if (entry is not null)
                    {
                        entry.WasMatch = true;
                    }
                    _pendingMatch.Set(
                        swipedCard.ProfileId,
                        swipedCard.DisplayName,
                        swipedCard.AvatarWebp);
                    _pendingMatchNav = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, $"[DeckScreen] SwipeAsync failed for {swipedCard.ProfileId}.");
            }
        });
    }

    /// <summary>A matched card shows a popup; the first tap that would actually undo shows the one-time
    /// intro without spending the use; otherwise the last-swiped card is re-dealt.</summary>
    private void OnReswipeClicked()
    {
        if (_undoStack.Count == 0 || _isThrowingCard || _isSnappingBack || _isUndoing)
        {
            return;
        }
        var entry = _undoStack[^1];
        if (entry.WasMatch)
        {
            ShowReswipeMatchedPopup();
            return;
        }
        if (_reswipesRemaining <= 0)
        {
            return;
        }
        if (!Plugin.Configuration.SeenReswipeIntro)
        {
            Plugin.Configuration.SeenReswipeIntro = true;
            Plugin.Configuration.Save();
            _showReswipeIntro = true;
            return;
        }

        _undoStack.RemoveAt(_undoStack.Count - 1);
        var card = entry.Card;
        _cards.Insert(0, card);
        // The card may be from an earlier deck whose portraits were wiped on the last pull; rebuild its image.
        EnsurePortraitCached(card);
        _processedThisPeriod.Remove(card.ProfileId);
        _undoFromRight = entry.WasLike;
        _isUndoing = true;
        _undoProgress = 0f;
        _reswipesRemaining = Math.Max(0, _reswipesRemaining - 1);
        _pulse.MarkActivity();

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _hubClient.UndoLastSwipeAsync(CancellationToken.None).ConfigureAwait(false);
                _reswipesRemaining = result.ReswipesRemaining;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[DeckScreen] UndoLastSwipeAsync failed.");
                _forceRefresh = true;
            }
        });
    }

    /// <summary>Sends the current card to the back of the in-memory deck; pure client reorder, no server call.</summary>
    private void OnDecideLaterClicked()
    {
        if (_isThrowingCard || _isSnappingBack || _isUndoing || _isDeferring || _cards.Count == 0)
        {
            return;
        }
        if (_cards.Count == 1 && !_cards[0].SuperlikedYou)
        {
            return;
        }
        _dragX = _dragY = 0f;
        _isDeferring = true;
        _deferProgress = 0f;
        _pulse.MarkActivity();
    }

    private void CompleteDeferral()
    {
        _isDeferring = false;
        _deferProgress = 0f;
        if (_cards.Count > 0)
        {
            var card = _cards[0];
            _cards.RemoveAt(0);
            if (card.SuperlikedYou)
            {
                _deferredSuperlikes.Add(card);
            }
            else
            {
                _cards.Add(card);
            }
        }
    }

    private static void ShowReswipeMatchedPopup()
    {
        ModalHost.Instance?.Open(300f, availW =>
        {
            ModalUi.Header(availW, FontAwesomeIcon.Heart, Loc.T("deck.reswipe_matched_title"), ThemeService.Current.AccentLight);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Body, Loc.T("deck.reswipe_matched"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();
            if (ModalUi.Button($"{Loc.T("common.ok")}##reswipeMatchedOk", availW))
            {
                ModalHost.Instance?.Close();
            }
        });
    }

    /// <summary>One-time overlay explaining the reswipe button; dims only the phone content.</summary>
    private void DrawReswipeIntroOverlay(Vector2 windowPos, Vector2 windowSize)
    {
        if (!_showReswipeIntro)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(windowPos, windowPos + windowSize,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

        // Scrim swallows taps to the deck behind; a tap outside the panel dismisses.
        ImGui.SetCursorScreenPos(windowPos);
        if (ImGui.InvisibleButton("##reswipeIntroScrim", windowSize))
        {
            _showReswipeIntro = false;
        }

        var w = Px(272f);
        var pad = Px(16f, 16f);
        var h = _reswipeIntroHeight > 0f ? _reswipeIntroHeight : Px(180f);
        var panelPos = windowPos + (windowSize - new Vector2(w, h)) * 0.5f;

        ImGui.SetCursorScreenPos(panelPos);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
        using (var child = ImRaii.Child("##reswipeIntroPanel", new Vector2(w, h), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (child.Success)
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                ModalUi.Header(innerW, FontAwesomeIcon.Undo, Loc.T("deck.reswipe_intro_title"), ThemeService.Current.AccentLight);
                ImGui.TextColored(UiColors.Body, Loc.T("deck.reswipe_intro"));
                ImGui.Spacing();
                ImGui.Spacing();
                if (ModalUi.Button($"{Loc.T("common.ok")}##reswipeIntroOk", innerW))
                {
                    _showReswipeIntro = false;
                }
                ImGui.PopTextWrapPos();
                _reswipeIntroHeight = ImGui.GetCursorPosY() + pad.Y;
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    /// <summary>Time until the daily reswipe/superlike allowances reset (next UTC midnight).</summary>
    private static TimeSpan DailyResetRemaining()
    {
        var now = DateTime.UtcNow;
        return now.Date.AddDays(1) - now;
    }

    /// <summary>Time until the free weekly superlike resets (Monday 00:00 UTC).</summary>
    private static TimeSpan WeeklyResetRemaining()
    {
        var now = DateTime.UtcNow;
        var daysToMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (daysToMonday == 0)
        {
            daysToMonday = 7;
        }
        return now.Date.AddDays(daysToMonday) - now;
    }
}
