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

    private ISharedImmediateTexture? _logoTex;
    private bool _logoLoaded;
    private const string LogoFileName = "logo_mini.png";

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

    private void EnsureLogo()
    {
        if (_logoLoaded)
        {
            return;
        }
        _logoLoaded = true;
        try
        {
            var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? "";
            var path = Path.Combine(dir, "Media", LogoFileName);
            if (File.Exists(path))
            {
                _logoTex = Plugin.TextureProvider.GetFromFile(path);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[DeckScreen] Failed to load logo_mini.png");
        }
    }

    private void DrawEmptyState(Vector2 windowPos, Vector2 windowSize)
    {
        var loaderActive = _refreshInFlight
            || (DateTimeOffset.UtcNow - _loaderShownAt) < MinLoaderDuration;

        if (loaderActive)
        {
            Widgets.LoadingIndicator.Draw();
        }
        else if (_noPoolForPreferences)
        {
            DrawNoPool(windowPos, windowSize);
        }
        else
        {
            DrawCooldown(windowPos, windowSize);
        }
    }

    private void DrawNoPool(Vector2 windowPos, Vector2 windowSize)
    {
        EnsureLogo();
        var dl = ImGui.GetWindowDrawList();
        var t = ThemeService.Current;
        var centerX = windowPos.X + windowSize.X * 0.5f;
        var textW = windowSize.X - Px(56f);
        var curY = windowPos.Y + windowSize.Y * 0.13f;

        var logoWrap = _logoTex?.GetWrapOrDefault();
        const float LogoSz = 74f;
        if (logoWrap != null)
        {
            dl.AddImage(logoWrap.Handle,
                new Vector2(centerX - Px(LogoSz) * 0.5f, curY),
                new Vector2(centerX + Px(LogoSz) * 0.5f, curY + Px(LogoSz)));
        }
        curY += Px(LogoSz) + Px(14f);

        var iconFontHandle = Plugin.PluginInterface.UiBuilder.FontIcon;
        var baseFontSize = ImGui.GetFontSize();
        ImGui.PushFont(iconFontHandle);
        var icon = FontAwesomeIcon.SearchMinus.ToIconString();
        var iconSz = baseFontSize * 1.7f;
        var iconW = ImGui.CalcTextSize(icon).X * (iconSz / baseFontSize);
        var iconH = ImGui.CalcTextSize(icon).Y * (iconSz / baseFontSize);
        var iconFont = ImGui.GetFont();
        ImGui.PopFont();
        dl.AddText(iconFont, iconSz, new Vector2(centerX - iconW * 0.5f, curY), t.AccentU32, icon);
        curY += iconH + Px(14f);

        using (UiFonts.H3?.Push())
        {
            var head = Loc.T("deck.no_pool_heading");
            var headSz = ImGui.CalcTextSize(head);
            ImGui.SetCursorScreenPos(new Vector2(centerX - headSz.X * 0.5f, curY));
            ImGui.TextColored(t.AccentLight, head);
        }
        curY = ImGui.GetCursorScreenPos().Y + Px(10f);

        ImGui.SetCursorScreenPos(new Vector2(centerX - textW * 0.5f, curY));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
        ImGui.TextColored(new Vector4(0.80f, 0.80f, 0.80f, 1f), Loc.T("deck.no_pool_body"));
        ImGui.PopTextWrapPos();
        curY = ImGui.GetCursorScreenPos().Y + Px(10f);

        ImGui.SetCursorScreenPos(new Vector2(centerX - textW * 0.5f, curY));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
        ImGui.TextColored(new Vector4(0.80f, 0.80f, 0.80f, 1f), Loc.T("deck.no_pool_footer"));
        ImGui.PopTextWrapPos();
    }

    /// <summary>Cooldown screen shown once the slot's deck is exhausted.</summary>
    private void DrawCooldown(Vector2 windowPos, Vector2 windowSize)
    {
        EnsureLogo();
        var dl = ImGui.GetWindowDrawList();
        var t = ThemeService.Current;
        var centerX = windowPos.X + windowSize.X * 0.5f;
        var textW = windowSize.X - Px(56f);
        var curY = windowPos.Y + windowSize.Y * 0.13f;

        var iconFontHandle = Plugin.PluginInterface.UiBuilder.FontIcon;
        var baseFontSize = ImGui.GetFontSize();

        var logoWrap = _logoTex?.GetWrapOrDefault();
        const float LogoSz = 74f;
        if (logoWrap != null)
        {
            dl.AddImage(logoWrap.Handle,
                new Vector2(centerX - Px(LogoSz) * 0.5f, curY),
                new Vector2(centerX + Px(LogoSz) * 0.5f, curY + Px(LogoSz)));
        }
        curY += Px(LogoSz) + Px(14f);

        ImGui.PushFont(iconFontHandle);
        var swatch = FontAwesomeIcon.LayerGroup.ToIconString();
        var swatchSize = baseFontSize * 1.7f;
        var swatchW = ImGui.CalcTextSize(swatch).X * (swatchSize / baseFontSize);
        var swatchH = ImGui.CalcTextSize(swatch).Y * (swatchSize / baseFontSize);
        var iconFont = ImGui.GetFont();
        ImGui.PopFont();
        dl.AddText(iconFont, swatchSize, new Vector2(centerX - swatchW * 0.5f, curY), t.AccentU32, swatch);
        curY += swatchH + Px(14f);

        using (UiFonts.H3?.Push())
        {
            string Head = Loc.T("deck.cooldown_heading");
            var headSz = ImGui.CalcTextSize(Head);
            ImGui.SetCursorScreenPos(new Vector2(centerX - headSz.X * 0.5f, curY));
            ImGui.TextColored(t.AccentLight, Head);
        }
        curY = ImGui.GetCursorScreenPos().Y + Px(10f);

        string Body = Loc.T("deck.cooldown_body");
        ImGui.SetCursorScreenPos(new Vector2(centerX - textW * 0.5f, curY));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
        ImGui.TextColored(new Vector4(0.80f, 0.80f, 0.80f, 1f), Body);
        ImGui.PopTextWrapPos();
        curY = ImGui.GetCursorScreenPos().Y + Px(20f);

        if (_nextPullAtUtc.HasValue)
        {
            var remaining = _nextPullAtUtc.Value - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }
            var cd = remaining.TotalSeconds < 1
                ? Loc.T("deck.new_matches_ready")
                : $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m {remaining.Seconds:D2}s";

            using (UiFonts.H3?.Push())
            {
                var lineFont = ImGui.GetFont();
                var LineSize = ImGui.GetFontSize();
                ImGui.PushFont(iconFontHandle);
                var clock = FontAwesomeIcon.Clock.ToIconString();
                var clockBase = ImGui.CalcTextSize(clock);
                ImGui.PopFont();
                var clockW = clockBase.X * (LineSize / baseFontSize);
                var cdW = ImGui.CalcTextSize(cd).X;
                var Gap = Px(8f);
                var lineX = centerX - (clockW + Gap + cdW) * 0.5f;
                var lineH = LineSize;
                var accentLight = ImGui.ColorConvertFloat4ToU32(t.AccentLight);

                dl.AddText(iconFont, LineSize, new Vector2(lineX, curY + (lineH - clockBase.Y * (LineSize / baseFontSize)) * 0.5f),
                    accentLight, clock);
                dl.AddText(lineFont, LineSize, new Vector2(lineX + clockW + Gap, curY), accentLight, cd);
                curY += lineH + Px(18f);
            }
        }

        if (_refreshError is not null)
        {
            ImGui.SetCursorScreenPos(new Vector2(centerX - textW * 0.5f, curY));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
            ImGui.TextColored(new Vector4(0.95f, 0.55f, 0.55f, 1f),
                Loc.T("deck.server_error", _refreshError));
            ImGui.PopTextWrapPos();
        }
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
            if (profile.Gender != Gender.None)
            {
                AddInfoSegment(genderIcon, iconFontPtr, genderW);
            }
            var regionLabel = RegionLabel(profile.Region);
            if (!string.IsNullOrEmpty(regionLabel))
            {
                AddInfoSegment(regionLabel, infoFontPtr, MainW(regionLabel));
            }

            // Flair pills, appended after the info line, rotation-aware (square corners; hover lives on the
            // profile-detail view since the card animates).
            if (profile.FlairIds is { Length: > 0 })
            {
                var flairLang = FlairCatalog.ResolveLanguage(Plugin.Configuration.PluginLanguage);
                var fPadX = Px(4f);
                var fPadY = Px(2f);
                foreach (var fid in profile.FlairIds)
                {
                    var f = _flairCatalog.Get(fid);
                    if (f is null)
                    {
                        continue;
                    }
                    var label = FlairCatalog.Text(f, flairLang);
                    var pw = MainW(label) + fPadX * 2f;
                    var ph = raceFont + fPadY * 2f;
                    var ftl = new Vector2(infoX, raceLineY - fPadY);
                    drawList.AddQuadFilled(
                        Rot(ftl), Rot(ftl + new Vector2(pw, 0f)), Rot(ftl + new Vector2(pw, ph)), Rot(ftl + new Vector2(0f, ph)),
                        HexToAbgr(f.BackgroundColor, alpha));
                    AddRotatedText(new Vector2(infoX + fPadX, raceLineY), ContrastText(f.BackgroundColor, alpha),
                        label, 0f, infoFontPtr, raceFont);
                    infoX += pw + Px(5f);
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
                    var nopeColor = ((uint)(nopeAlpha * 200) << 24) | 0x00E57373;
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

    private void DrawActionButtons(float centerX, float topY)
    {
        var buttonSize = Px(64, 64);
        const float Spacing = 50f;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(32f));
        ImGui.SetWindowFontScale(1.5f * UiScale.S);
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);

        ImGui.SetCursorScreenPos(new Vector2(centerX - buttonSize.X - Px(Spacing) * 0.5f, topY));
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.18f, 0.18f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.25f, 0.25f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.25f, 0.25f, 1f)));
        if (ImGui.Button(FontAwesomeIcon.Times.ToIconString(), buttonSize))
        {
            StartThrow(false);
        }
        if (ImGui.IsItemHovered())
        {
            var tl = ImGui.GetItemRectMin();
            var iconStr = FontAwesomeIcon.Times.ToIconString();
            var iconSz = ImGui.CalcTextSize(iconStr);
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                tl + (buttonSize - iconSz) * 0.5f, 0xFFFFFFFF, iconStr);
        }
        ImGui.PopStyleColor(3);

        ImGui.SetCursorScreenPos(new Vector2(centerX + Px(Spacing) * 0.5f, topY));
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.18f, 0.18f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.75f, 0.4f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.85f, 0.45f, 1f)));
        if (ImGui.Button(FontAwesomeIcon.Heart.ToIconString(), buttonSize))
        {
            StartThrow(true);
        }
        if (ImGui.IsItemHovered())
        {
            var tl = ImGui.GetItemRectMin();
            var iconStr = FontAwesomeIcon.Heart.ToIconString();
            var iconSz = ImGui.CalcTextSize(iconStr);
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                tl + (buttonSize - iconSz) * 0.5f, 0xFFFFFFFF, iconStr);
        }
        ImGui.PopStyleColor(3);

        ImGui.PopFont();
        ImGui.SetWindowFontScale(1f);
        ImGui.PopStyleVar();
    }

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
