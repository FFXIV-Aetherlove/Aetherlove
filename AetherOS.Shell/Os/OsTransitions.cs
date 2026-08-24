using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Os;

/// <summary>The iOS-style zoom card played when opening an app from its tile and when returning home.</summary>
public static class OsTransitions
{
    private enum Phase
    {
        Idle,
        Expand,
        FadeIn,
        Shrink,
    }

    private static Phase _phase = Phase.Idle;
    private static IAetherApp? _app;
    private static Vector2 _tileTL;
    private static Vector2 _tileBR;
    private static float _t;
    private static Action? _navigate;

    private const float ExpandSeconds = 0.26f;
    private const float FadeSeconds = 0.14f;
    private const float ShrinkSeconds = 0.24f;

    public static bool Active => _phase != Phase.Idle;

    public static void PlayOpen(IAetherApp app, Vector2 tileTL, Vector2 tileBR, Action navigate)
    {
        if (AccessibilityService.ReduceMotion)
        {
            navigate();
            return;
        }
        _app = app;
        _tileTL = tileTL;
        _tileBR = tileBR;
        _navigate = navigate;
        _t = 0f;
        _phase = Phase.Expand;
    }

    /// <summary>Navigates home immediately and shrinks the card down to the app's tile.</summary>
    public static void PlayClose(IAetherApp app, Vector2 tileTL, Vector2 tileBR, Action navigateHome)
    {
        navigateHome();
        if (AccessibilityService.ReduceMotion)
        {
            return;
        }
        _app = app;
        _tileTL = tileTL;
        _tileBR = tileBR;
        _navigate = null;
        _t = 0f;
        _phase = Phase.Shrink;
    }

    /// <summary>How far the card may open before an app's icon art has faded out completely.</summary>
    private const float IconFadeAt = 0.55f;

    public static void Draw(ImDrawListPtr dl, Vector2 contentTL, Vector2 contentBR)
    {
        if (_phase == Phase.Idle || _app == null)
        {
            return;
        }

        var dt = ImGui.GetIO().DeltaTime;
        switch (_phase)
        {
            case Phase.Expand:
            {
                _t = Math.Min(1f, _t + dt / ExpandSeconds);
                var e = EaseInOutCubic(_t);
                DrawCard(dl, contentTL, contentBR, e, 1f);
                if (_t >= 1f)
                {
                    _navigate?.Invoke();
                    _navigate = null;
                    _t = 0f;
                    _phase = Phase.FadeIn;
                }
                break;
            }
            case Phase.FadeIn:
            {
                _t = Math.Min(1f, _t + dt / FadeSeconds);
                DrawCard(dl, contentTL, contentBR, 1f, 1f - _t);
                if (_t >= 1f)
                {
                    _phase = Phase.Idle;
                    _app = null;
                }
                break;
            }
            case Phase.Shrink:
            {
                _t = Math.Min(1f, _t + dt / ShrinkSeconds);
                var e = 1f - EaseInOutCubic(_t);
                var alpha = _t > 0.75f ? (1f - _t) / 0.25f : 1f;
                DrawCard(dl, contentTL, contentBR, e, alpha);
                if (_t >= 1f)
                {
                    _phase = Phase.Idle;
                    _app = null;
                }
                break;
            }
        }
    }

    private static void DrawCard(ImDrawListPtr dl, Vector2 contentTL, Vector2 contentBR, float expand, float alpha)
    {
        if (_app == null)
        {
            return;
        }

        dl.AddRectFilled(contentTL, contentBR, OsDraw.Black(0.35f * expand * alpha));

        var tl = Vector2.Lerp(_tileTL, contentTL, expand);
        var br = Vector2.Lerp(_tileBR, contentBR, expand);
        var rounding = float.Lerp((_tileBR.X - _tileTL.X) * 0.28f, Px(6f), expand);

        OsDraw.RoundedGradient(dl, tl, br, rounding, _app.TileTop, _app.TileBottom, alpha);

        // An app's own art rides the growing card at very close to its tile size and fades out early. It is
        // a 256px icon: stretched to the full content rect it turns to mush, and the card's own gradient is
        // what the eye should be following by then anyway.
        var centre = (tl + br) * 0.5f;
        if ((_app.TileImage ?? AppIcons.Tile(_app.Id)) is { } art)
        {
            var artAlpha = alpha * Math.Clamp(1f - (expand / IconFadeAt), 0f, 1f);
            if (artAlpha <= 0.004f)
            {
                return;
            }
            var side = (_tileBR.X - _tileTL.X) * float.Lerp(1f, 1.12f, expand);
            var half = new Vector2(side * 0.5f);
            dl.AddImageRounded(art, centre - half, centre + half, Vector2.Zero, Vector2.One,
                OsDraw.White(artAlpha), side * 0.28f, ImDrawFlags.RoundCornersAll);
            return;
        }
        var iconPx = float.Lerp((_tileBR.X - _tileTL.X) * 0.46f, Px(64f), expand);
        UI.IconDraw.AddCentered(dl, _app.Icon, iconPx, centre, OsDraw.White(alpha));
    }

    private static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
}
