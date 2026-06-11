using System;
using System.IO;
using System.Numerics;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherLove.Screens;

/// <summary>Match notification overlay.</summary>
public class MatchScreen
{
    private readonly ScreenRouter _router;
    private readonly PendingMatchContext _pending;
    private readonly OwnAvatarCache _ownAvatar;
    private readonly SessionBootstrapper _bootstrap;

    private float _bgAlpha;
    private float _avatarSlide;
    private float _textAlpha;
    private float _buttonsAlpha;

    private const float BgSpeed = 2.9f;
    private const float AvatarSpeed = 1.8f;
    private const float TextSpeed = 2.5f;
    private const float ButtonSpeed = 2.9f;

    private readonly ConfettiBurst _confetti = new();

    private ISharedImmediateTexture? _peerAvatarTex;
    private Guid _cachedPeerId;

    public MatchScreen(
        ScreenRouter router,
        PendingMatchContext pending,
        OwnAvatarCache ownAvatar,
        SessionBootstrapper bootstrap)
    {
        _router = router;
        _pending = pending;
        _ownAvatar = ownAvatar;
        _bootstrap = bootstrap;
    }

    public void OnShow()
    {
        _bgAlpha = _avatarSlide = _textAlpha = _buttonsAlpha = 0f;
        _confetti.Reset();
        EnsurePeerAvatar();
        // Cached copy shows instantly; the refresh swaps in a just-changed avatar when it lands.
        _ownAvatar.Refresh();
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
        try { Directory.CreateDirectory(cacheDir); } catch { return; }
        try
        {
            var path = Path.Combine(cacheDir, $"{_pending.PeerProfileId}.webp");
            File.WriteAllBytes(path, _pending.PeerAvatarWebp);
            _peerAvatarTex = Plugin.TextureProvider.GetFromFile(path);
            _cachedPeerId = _pending.PeerProfileId;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[MatchScreen] Failed to cache peer avatar.");
        }
    }

    public void Draw()
    {
        var dt = (float)ImGui.GetIO().DeltaTime;
        AnimationHelper.ClampedProgress(ref _bgAlpha, dt, BgSpeed, forward: true);
        AnimationHelper.ClampedProgress(ref _avatarSlide, dt, AvatarSpeed, forward: true);
        AnimationHelper.ClampedProgress(ref _textAlpha, dt, TextSpeed, forward: true);
        AnimationHelper.ClampedProgress(ref _buttonsAlpha, dt, ButtonSpeed, forward: true);

        var t = ThemeService.Current;
        var windowSize = ImGui.GetWindowSize();
        var windowPos = ImGui.GetWindowPos();
        var drawList = ImGui.GetWindowDrawList();

        var centerX = windowPos.X + windowSize.X * 0.5f;
        var avatarY = windowPos.Y + windowSize.Y * 0.46f;

        var bgColor = ((uint)(_bgAlpha * 210) << 24) | 0x00000000;
        drawList.AddRectFilled(windowPos, windowPos + windowSize, bgColor);

        // Confetti behind the foreground content, clipped to the window.
        _confetti.Draw(windowPos, windowPos + windowSize);

        var ownName = string.IsNullOrWhiteSpace(_bootstrap.LastDisplayName) ? Loc.T("deck.match_you") : _bootstrap.LastDisplayName!;
        var peerName = _pending.HasPending && !string.IsNullOrWhiteSpace(_pending.PeerDisplayName)
            ? _pending.PeerDisplayName
            : Loc.T("deck.match_your_match");

        if (_textAlpha > 0.01f)
        {
            var titleCol = ImGui.ColorConvertFloat4ToU32(
                new Vector4(t.AccentLight.X, t.AccentLight.Y, t.AccentLight.Z, _textAlpha));
            var mainCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, _textAlpha));

            string TopTitle = Loc.T("deck.match_congratulations");
            using (UiFonts.H3?.Push())
            {
                var f = ImGui.GetFont();
                var sz = ImGui.GetFontSize();
                var w = ImGui.CalcTextSize(TopTitle).X;
                drawList.AddText(f, sz,
                    new Vector2(centerX - w * 0.5f, windowPos.Y + windowSize.Y * 0.20f),
                    titleCol, TopTitle);
            }

            string MainTitle = Loc.T("deck.match_its_a_match");
            using (UiFonts.H1?.Push())
            {
                var f = ImGui.GetFont();
                var sz = ImGui.GetFontSize();
                var w = ImGui.CalcTextSize(MainTitle).X;
                drawList.AddText(f, sz,
                    new Vector2(centerX - w * 0.5f, windowPos.Y + windowSize.Y * 0.26f),
                    mainCol, MainTitle);
            }
        }

        var AvatarRadius = Px(52f);
        var AvatarSpacing = Px(78f);
        var leftPos = new Vector2(centerX - AvatarSpacing - AvatarRadius * 2f * (1f - _avatarSlide), avatarY);
        var rightPos = new Vector2(centerX + AvatarSpacing + AvatarRadius * 2f * (1f - _avatarSlide), avatarY);

        DrawAvatar(drawList, leftPos, AvatarRadius, _ownAvatar.Texture, UiColors.AvatarFallback);
        DrawAvatar(drawList, rightPos, AvatarRadius, _peerAvatarTex, UiColors.AvatarFallback);

        if (_textAlpha > 0.01f)
        {
            var nameCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.92f, 0.92f, _textAlpha));
            var font = ImGui.GetFont();
            var sz = ImGui.GetFontSize();
            DrawCenteredText(drawList, font, sz, leftPos.X, avatarY + AvatarRadius + Px(10f), ownName, nameCol);
            DrawCenteredText(drawList, font, sz, rightPos.X, avatarY + AvatarRadius + Px(10f), peerName, nameCol);
        }

        if (_buttonsAlpha > 0.01f)
        {
            var BtnW = Px(150f);
            var BtnH = Px(38f);
            var Gap = Px(12f);
            var btnY = avatarY + AvatarRadius + Px(48f);

            var accent = WithAlpha(t.ButtonNormal, _buttonsAlpha);
            var accentHov = WithAlpha(t.ButtonHovered, _buttonsAlpha);
            var neutral = WithAlpha(new Vector4(0.22f, 0.22f, 0.22f, 1f), _buttonsAlpha);
            var neutralHov = WithAlpha(new Vector4(0.34f, 0.34f, 0.34f, 1f), _buttonsAlpha);

            if (DrawIconButton("##startChat", Loc.T("deck.match_start_chatting"), FontAwesomeIcon.Comments,
                    accent, accentHov, new Vector2(centerX - BtnW - Gap * 0.5f, btnY), new Vector2(BtnW, BtnH)))
            {
                _pending.Clear();
                _router.Navigate(Screen.ChatList);
            }

            if (DrawIconButton("##keepSwiping", Loc.T("deck.match_keep_swiping"), FontAwesomeIcon.ArrowLeft,
                    neutral, neutralHov, new Vector2(centerX + Gap * 0.5f, btnY), new Vector2(BtnW, BtnH)))
            {
                _pending.Clear();
                _router.Navigate(Screen.Deck);
            }
        }
    }

    private void DrawAvatar(ImDrawListPtr dl, Vector2 center, float radius,
                            ISharedImmediateTexture? tex, uint fallback)
    {
        var wrap = tex?.GetWrapOrDefault();
        if (wrap != null)
        {
            var tl = center - new Vector2(radius, radius);
            var br = center + new Vector2(radius, radius);
            dl.AddImageRounded(wrap.Handle, tl, br,
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, radius, ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddCircleFilled(center, radius, fallback);
        }
        dl.AddCircle(center, radius, 0xFFFFFFFF, 0, Px(3f));
    }

    private static void DrawCenteredText(ImDrawListPtr dl, ImFontPtr font, float size,
                                         float centerX, float y, string text, uint col)
    {
        var w = ImGui.CalcTextSize(text).X;
        dl.AddText(font, size, new Vector2(centerX - w * 0.5f, y), col, text);
    }

    private static bool DrawIconButton(string id, string label, FontAwesomeIcon icon,
                                       Vector4 bg, Vector4 bgHover, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, bgHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, bg);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        var clicked = ImGui.Button(id, size);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var fontSize = ImGui.GetFontSize();
        var textFont = ImGui.GetFont();

        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconFont = ImGui.GetFont();
        var iconStr = icon.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconStr);
        ImGui.PopFont();

        var textSz = ImGui.CalcTextSize(label);
        var Gap = Px(6f);
        var totalW = iconSz.X + Gap + textSz.X;
        var startX = min.X + (size.X - totalW) * 0.5f;
        var cy = min.Y + size.Y * 0.5f;

        dl.AddText(iconFont, fontSize, new Vector2(startX, cy - iconSz.Y * 0.5f), 0xFFFFFFFFu, iconStr);
        dl.AddText(textFont, fontSize, new Vector2(startX + iconSz.X + Gap, cy - textSz.Y * 0.5f), 0xFFFFFFFFu, label);
        return clicked;
    }

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, c.W * a);
}
