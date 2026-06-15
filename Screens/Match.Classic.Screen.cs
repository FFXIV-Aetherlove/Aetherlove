using System.Numerics;
using AetherLove.Navigation;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using static AetherLove.Screens.MatchFx;

namespace AetherLove.Screens;

/// <summary>Match effect "Classic" — the original celebration: a dimming overlay, confetti, and two
/// avatars sliding in to meet beneath the title. One of the random match-effect pool.</summary>
public sealed class MatchClassicScreen : IMatchEffect
{
    private readonly ScreenRouter _router;

    private float _bg;
    private float _slide;
    private float _text;
    private readonly ConfettiBurst _confetti = new();

    public MatchClassicScreen(ScreenRouter router)
    {
        _router = router;
    }

    public void OnShow()
    {
        _bg = _slide = _text = 0f;
        _confetti.Reset();
    }

    public void Draw()
    {
        var reduce = AccessibilityService.ReduceMotion;
        var dt = (float)ImGui.GetIO().DeltaTime;

        if (reduce)
        {
            _bg = _slide = _text = 1f;
        }
        else
        {
            AnimationHelper.ClampedProgress(ref _bg, dt, 2.9f, forward: true);
            AnimationHelper.ClampedProgress(ref _slide, dt, 1.8f, forward: true);
            AnimationHelper.ClampedProgress(ref _text, dt, 2.5f, forward: true);
        }

        var t = ThemeService.Current;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var cx = pos.X + size.X * 0.5f;
        var avatarY = pos.Y + size.Y * 0.46f;

        dl.AddRectFilled(pos, pos + size, (uint)(_bg * 210f) << 24);

        if (!reduce)
        {
            _confetti.Draw(pos, pos + size);
        }

        if (_text > 0.01f)
        {
            using (UiFonts.H3?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.20f, Loc.T("deck.match_congratulations"),
                    U32(new Vector4(t.AccentLight.X, t.AccentLight.Y, t.AccentLight.Z, _text)));
            }
            using (UiFonts.H1?.Push())
            {
                CenterText(dl, cx, pos.Y + size.Y * 0.26f, Loc.T("deck.match_its_a_match"),
                    U32(new Vector4(1f, 1f, 1f, _text)));
            }
        }

        var radius = Px(52f);
        var spacing = Px(78f);
        var leftPos = new Vector2(cx - spacing - radius * 2f * (1f - _slide), avatarY);
        var rightPos = new Vector2(cx + spacing + radius * 2f * (1f - _slide), avatarY);

        Avatar(dl, leftPos, radius, MatchContent.OwnAvatar, 0xFFFFFFFFu, Px(3f));
        Avatar(dl, rightPos, radius, MatchContent.PeerAvatar, 0xFFFFFFFFu, Px(3f));

        if (_text > 0.01f)
        {
            var nameCol = U32(new Vector4(0.92f, 0.92f, 0.92f, _text));
            CenterText(dl, leftPos.X, avatarY + radius + Px(10f), MatchContent.OwnName, nameCol);
            CenterText(dl, rightPos.X, avatarY + radius + Px(10f), MatchContent.PeerName, nameCol);
        }

        DrawActionButtons(_router, pos, size, reduce ? 1f : _text);
    }
}
