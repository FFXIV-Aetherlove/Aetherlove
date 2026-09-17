using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Racing;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Rendering;

/// <summary>The bridge as structure: the deck's road quads as an overlay laid over the base ribbon,
/// a dark fascia strip hanging below the deck's edge, a lit parapet along its top, and a bounded set
/// of piers standing on the verge beside it. Geometry is baked once per race on the ribbon's own
/// sample grid, so an overlay quad lands exactly on the base quad beneath it; the draw path projects,
/// culls to the stage and allocates nothing.</summary>
internal sealed class RaceBridgeDeck
{
    /// <summary>Samples per ribbon quad, in track rows. The base ribbon walks the same stride, or the
    /// overlay's quads would straddle the base's.</summary>
    public const float RibbonStride = 2f;

    private const int MaxSupports = 8;
    private const float SupportDeck = 1.2f;
    private const float SupportSpacing = 18f;
    private const float SupportOut = 1.35f;
    private const float SupportClearance = 0.65f;
    private const float FasciaDepthX = 0.16f;
    private const float FasciaDepthY = 0.28f;
    private const float FasciaMaxHeight = 1.6f;

    private static readonly Vector4 ParapetInk = new(1f, 1f, 1f, 0.52f);
    private static readonly Vector4 FasciaInk = new(0.07f, 0.065f, 0.085f, 1f);
    private static readonly Vector4 FasciaShadeInk = new(0.045f, 0.04f, 0.06f, 1f);
    private static readonly Vector4 FasciaFootInk = new(0f, 0f, 0f, 0.24f);
    private static readonly Vector4 PierInk = new(0.10f, 0.085f, 0.12f, 1f);
    private static readonly Vector4 PierEdgeInk = new(0.38f, 0.32f, 0.39f, 0.70f);
    private static readonly Vector4 PierCapInk = new(0.27f, 0.23f, 0.29f, 1f);

    private readonly record struct DeckQuad(Vector2 L0, Vector2 R0, Vector2 L1, Vector2 R1, float Height);
    private record struct ScreenQuad(Vector2 L0, Vector2 R0, Vector2 L1, Vector2 R1, bool Visible);

    private DeckQuad[] _quads = [];
    private ScreenQuad[] _screen = [];
    private readonly Vector2[] _supports = new Vector2[MaxSupports];
    private int _supportCount;

    public void Generate(AetherRaceLive.Track track)
    {
        _supportCount = 0;
        if (track.Under is null)
        {
            _quads = [];
            _screen = [];
            return;
        }

        var to = track.LapRows > 0 ? track.LapLength : track.Length;
        var step = track.Step * RibbonStride;
        var samples = (int)MathF.Ceiling(to / step) + 1;
        var quads = new List<DeckQuad>();
        var prevL = Vector2.Zero;
        var prevR = Vector2.Zero;
        var prevDeck = 0f;
        for (var i = 0; i < samples; i++)
        {
            var s = MathF.Min(i * step, to);
            var (deck, _) = track.DeckAt(s);
            var p = track.AtLerp(s);
            var normal = new Vector2(-MathF.Sin(p.Heading), MathF.Cos(p.Heading));
            var half = normal * (p.Width * 0.5f);
            var pos = new Vector2(p.X, p.Y);
            var l = pos - half;
            var r = pos + half;
            var height = MathF.Max(deck, prevDeck);
            if (i > 0 && height > AetherRaceLive.DeckDrawn)
            {
                quads.Add(new DeckQuad(prevL, prevR, l, r, height));
            }

            if (deck >= SupportDeck && s % SupportSpacing < step)
            {
                for (var side = -1; side <= 1; side += 2)
                {
                    var at = (side > 0 ? r : l) + (normal * (side * SupportOut));
                    if (_supportCount < MaxSupports && !track.RoadClaims(at.X, at.Y, SupportClearance))
                    {
                        _supports[_supportCount++] = at;
                    }
                }
            }

            prevL = l;
            prevR = r;
            prevDeck = deck;
        }

        _quads = quads.ToArray();
        _screen = new ScreenQuad[_quads.Length];
    }

    /// <summary>The piers, drawn with the ground furniture: under every runner and under the deck.</summary>
    public void DrawSupports(ImDrawListPtr dl, in StageCam cam, Vector2 origin, Vector2 size)
    {
        var unit = cam.Zoom;
        var pier = ImGui.ColorConvertFloat4ToU32(PierInk);
        var edge = ImGui.ColorConvertFloat4ToU32(PierEdgeInk);
        var cap = ImGui.ColorConvertFloat4ToU32(PierCapInk);
        for (var i = 0; i < _supportCount; i++)
        {
            var at = cam.ToScreen(_supports[i]);
            if (!StageCam.OnStage(at, origin, size, unit))
            {
                continue;
            }

            var top = at + (new Vector2(-0.20f, -0.42f) * unit);
            var bottom = at + (new Vector2(0.20f, 0.10f) * unit);
            dl.AddRectFilled(top, bottom, pier, unit * 0.045f);
            dl.AddLine(top, new Vector2(top.X, bottom.Y), edge, Px(1.5f));
            dl.AddRectFilled(top - new Vector2(unit * 0.06f, 0f), new Vector2(bottom.X + (unit * 0.06f), top.Y + (unit * 0.11f)),
                cap, unit * 0.025f);
        }
    }

    /// <summary>The deck overlay: fascia first, then every deck quad, then the parapets, so one quad's
    /// fascia never lands on its neighbour's road. Drawn after the underpass runners and before
    /// everyone else.</summary>
    public void Draw(ImDrawListPtr dl, in StageCam cam, Vector2 origin, Vector2 size, uint road, uint kerb)
    {
        if (_quads.Length == 0)
        {
            return;
        }

        var margin = MathF.Max(Px(8f), cam.Zoom * 0.7f);
        var winTL = origin - new Vector2(margin, margin);
        var winBR = origin + size + new Vector2(margin, margin);
        var any = false;
        for (var i = 0; i < _quads.Length; i++)
        {
            var q = _quads[i];
            var l0 = cam.ToScreen(q.L0);
            var r0 = cam.ToScreen(q.R0);
            var l1 = cam.ToScreen(q.L1);
            var r1 = cam.ToScreen(q.R1);
            var min = Vector2.Min(Vector2.Min(l0, r0), Vector2.Min(l1, r1));
            var max = Vector2.Max(Vector2.Max(l0, r0), Vector2.Max(l1, r1));
            var visible = max.X >= winTL.X && min.X <= winBR.X && max.Y >= winTL.Y && min.Y <= winBR.Y;
            _screen[i] = new ScreenQuad(l0, r0, l1, r1, visible);
            any |= visible;
        }

        if (!any)
        {
            return;
        }

        var fascia = ImGui.ColorConvertFloat4ToU32(FasciaInk);
        var shade = ImGui.ColorConvertFloat4ToU32(FasciaShadeInk);
        var foot = ImGui.ColorConvertFloat4ToU32(FasciaFootInk);
        for (var i = 0; i < _screen.Length; i++)
        {
            var q = _screen[i];
            if (!q.Visible)
            {
                continue;
            }

            var depth = new Vector2(FasciaDepthX, FasciaDepthY) * cam.Zoom * Math.Clamp(_quads[i].Height, 0f, FasciaMaxHeight);
            dl.AddQuadFilled(q.L0, q.L1, q.L1 + depth, q.L0 + depth, fascia);
            dl.AddQuadFilled(q.R0, q.R1, q.R1 + depth, q.R0 + depth, shade);
            dl.AddLine(q.L0 + depth, q.L1 + depth, foot, Px(3f));
            dl.AddLine(q.R0 + depth, q.R1 + depth, foot, Px(3f));
        }

        for (var i = 0; i < _screen.Length; i++)
        {
            var q = _screen[i];
            if (q.Visible)
            {
                dl.AddQuadFilled(q.L0, q.R0, q.R1, q.L1, road);
            }
        }

        var parapet = ImGui.ColorConvertFloat4ToU32(ParapetInk);
        for (var i = 0; i < _screen.Length; i++)
        {
            var q = _screen[i];
            if (!q.Visible)
            {
                continue;
            }

            dl.AddLine(q.L0, q.L1, kerb, Px(1.5f));
            dl.AddLine(q.R0, q.R1, kerb, Px(1.5f));
            dl.AddLine(q.L0, q.L1, parapet, Px(2.6f));
            dl.AddLine(q.R0, q.R1, parapet, Px(2.6f));
        }
    }
}
