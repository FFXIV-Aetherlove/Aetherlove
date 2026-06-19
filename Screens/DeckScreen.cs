using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Shared.Matching;
using AetherLove.Shared.Profile.Enums;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>Swipe deck.</summary>
public class DeckScreen : IDisposable
{
    private readonly ScreenRouter _router;
    private readonly ProfileScreen _profileScreen;
    private readonly AetherLoveHubClient _hubClient;
    private readonly PendingMatchContext _pendingMatch;
    private readonly NotificationCenter _notifications;
    private readonly OwnAvatarCache _ownAvatar;
    private readonly PulseService _pulse;
    private readonly FlairCatalog _flairCatalog;

    private readonly List<DeckCardDto> _cards = new();
    private readonly HashSet<Guid> _processedThisPeriod = new();
    private readonly Dictionary<Guid, ISharedImmediateTexture?> _portraitTextures = new();

    private DateTimeOffset? _nextPullAtUtc;
    private bool _noPoolForPreferences;

    private volatile bool _refreshInFlight;
    private volatile string? _refreshError;
    /// <summary>Set by a server DeckRefresh push; consumed on the next Draw.</summary>
    private volatile bool _forceRefresh;
    private CancellationTokenSource _cts = new();

    private volatile bool _pendingMatchNav;

    private readonly CooldownScene _cooldownScene = new();

    // Loader sticks for at least MinLoaderDuration so a fast fetch doesn't flash the cooldown screen.
    private DateTimeOffset _loaderShownAt;
    private static readonly TimeSpan MinLoaderDuration = TimeSpan.FromSeconds(1);

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
        FlairCatalog flairCatalog)
    {
        _router = router;
        _profileScreen = profileScreen;
        _hubClient = hubClient;
        _pendingMatch = pendingMatch;
        _notifications = notifications;
        _ownAvatar = ownAvatar;
        _pulse = pulse;
        _flairCatalog = flairCatalog;
        _notifications.DeckRefreshRequested += OnDeckRefreshRequested;
    }

    private void OnDeckRefreshRequested() => _forceRefresh = true;

    public void OnShow()
    {
        _dragX = _dragY = 0;
        _isThrowingCard = _isSnappingBack = false;
        _throwProgress = _snapProgress = 0f;
        _cooldownScene.Reset();

        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _forceRefresh = false;

        // Warm the own-avatar cache so a first-ever match overlay never shows the grey fallback.
        _ownAvatar.Refresh(onlyIfCold: true);

        // Only hit the server when we have no deck in hand or the next-pull window has elapsed; an
        // emptied deck is re-pulled by Draw() once NextPullAtUtc passes.
        var pullDue = _nextPullAtUtc.HasValue && DateTimeOffset.UtcNow >= _nextPullAtUtc.Value;
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

        if (_pendingMatchNav)
        {
            _pendingMatchNav = false;
            _router.Navigate(Screen.Match);
            return;
        }

        if (_cards.Count == 0 && !_refreshInFlight &&
            _nextPullAtUtc.HasValue && DateTimeOffset.UtcNow >= _nextPullAtUtc.Value)
        {
            StartRefresh();
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

        var cardsToShow = Math.Min(3, _cards.Count);
        for (int i = cardsToShow - 1; i >= 0; i--)
        {
            var scale = i == 0 ? 1.0f : (i == 1 ? 0.98f : 0.96f);
            var alpha = i == 0 ? 1.0f : (i == 1 ? 0.7f : 0.5f);
            var offsetY = i == 0 ? 0f : (i == 1 ? Px(-5f) : Px(-10f));
            DrawCard(_cards[i], centerX, centerY + offsetY, scale, alpha, i == 0, windowSize);
        }

        DrawActionButtons(centerX, windowPos.Y + usableHeight - Px(BottomMargin) - Px(ButtonAreaHeight) + Px(10f));
    }

    private void StartRefresh()
    {
        if (_refreshInFlight)
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

                _processedThisPeriod.Clear();
                _cards.Clear();
                _cards.AddRange(deck.Cards);
                _nextPullAtUtc = deck.NextPullAtUtc;
                _noPoolForPreferences = deck.NoPoolForPreferences;

                CachePortraits(deck.Cards);
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

    private void CachePortraits(IEnumerable<DeckCardDto> cards)
    {
        var cacheDir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "DeckCache");
        try
        {
            Directory.CreateDirectory(cacheDir);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[DeckScreen] Failed to create deck cache dir.");
            return;
        }

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
        _cooldownScene.Draw(windowPos, windowSize, heading, body, timer, error);
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
            else
            {
                cardTopLeft.X += _dragX;
                cardTopLeft.Y += _dragY * 0.5f;
            }
        }

        var cardBottomRight = cardTopLeft + new Vector2(scaledWidth, scaledHeight);

        var rotation = 0f;
        if (isTopCard && !_isThrowingCard)
        {
            rotation = (_dragX / windowSize.X) * 25f;
        }
        else if (isTopCard && _isThrowingCard)
        {
            rotation = (_throwRight ? 1 : -1) * 25f * _throwProgress;
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

        // Neutral gray placeholder when a candidate has no portrait.
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

        if (isTopCard && !_isThrowingCard)
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
            isOverPill = mp.X >= viewPillTL.X && mp.X <= viewPillBR.X
                      && mp.Y >= viewPillTL.Y && mp.Y <= viewPillBR.Y;

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
            ImGui.PopFont();

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

            AddRotatedText(new Vector2(cardTopLeft.X + Px(14f), nameLineY),
                           alphaU32 | 0x00FFFFFF, profile.DisplayName, wrapWidth, nameFontPtr, nameFont);

            // Info line: race · gender(icon) · region at raceFont size. Any segment that's unset (fakes can
            // have no race/gender) is dropped along with its separator, so the dots never dangle.
            var infoCol = alphaU32 | 0x00DDDDDD;
            var infoX = cardTopLeft.X + Px(14f);
            const string Sep = "  ·  ";

            float MainW(string s) => ImGui.CalcTextSize(s).X * (raceFont / fontSize);
            var genderW = genderRawW * (raceFont / iconNatSize);

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
            // Only Male/Female have a glyph; None (fakes) and Other render without an icon.
            if (profile.Gender is Gender.Male or Gender.Female)
            {
                AddInfoSegment(genderIcon, iconFontPtr, genderW);
            }
            var regionLabel = RegionLabel(profile.Region);
            if (!string.IsNullOrEmpty(regionLabel))
            {
                AddInfoSegment(regionLabel, infoFontPtr, MainW(regionLabel));
            }

            // A single flair pill on the "view profile" pill's line, mirrored to the card's right edge. The
            // top card at rest shows the flair's description on hover.
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
                    }
                    break;
                }
            }

            drawList.PopClipRect();

            var t = ThemeService.Current;
            var pillBgCol = isOverPill
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

        if (isTopCard && !_isThrowingCard && !_isSnappingBack)
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
        }
    }

    private static readonly Vector4 NopeTop = new(1.00f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 NopeBottom = new(0.90f, 0.24f, 0.30f, 1f);
    private static readonly Vector4 LikeTop = new(0.42f, 0.88f, 0.56f, 1f);
    private static readonly Vector4 LikeBottom = new(0.18f, 0.72f, 0.42f, 1f);

    private void DrawActionButtons(float centerX, float topY)
    {
        var radius = Px(30f);
        var gap = Px(46f);
        var cy = topY + radius - Px(8f);

        if (DrawActionButton("##deckNope", new Vector2(centerX - gap * 0.5f - radius, cy), radius,
                FontAwesomeIcon.Times, NopeTop, NopeBottom, ref _nopeHover))
        {
            StartThrow(false);
        }
        if (DrawActionButton("##deckLike", new Vector2(centerX + gap * 0.5f + radius, cy), radius,
                FontAwesomeIcon.Heart, LikeTop, LikeBottom, ref _likeHover))
        {
            StartThrow(true);
        }
    }

    /// <summary>A vibrant, colour-filled round swipe action button drawn on the draw list — gloss, rim,
    /// drop shadow and an eased hover-pop that a plain ImGui button can't give.</summary>
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

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconFont = ImGui.GetFont();
        var iconStr = icon.ToIconString();
        var baseSz = ImGui.CalcTextSize(iconStr);
        var baseFont = ImGui.GetFontSize();
        ImGui.PopFont();
        var iconPx = r * 0.9f;
        var iconDim = baseSz * (iconPx / baseFont);
        dl.AddText(iconFont, iconPx, center - iconDim * 0.5f, 0xFFFFFFFFu, iconStr);

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
        _cards.RemoveAt(0);
        _processedThisPeriod.Add(swipedCard.ProfileId);
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
                    liked ? SwipeDirection.Like : SwipeDirection.Pass,
                    CancellationToken.None).ConfigureAwait(false);
                if (result.IsMatch)
                {
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
}
