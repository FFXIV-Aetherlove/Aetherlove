namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Numerics;

using AetherLove.Shared.Racing;
using Dalamud.Bindings.ImGui;

/// <summary>The stage's relief: owns the cached <see cref="RaceTerrainRelief.Profile"/> for the race
/// and draws its grade light on the road. The bank faces are drawn by <see cref="RaceDressing"/>, which
/// owns the ground palette they take their colour from. Viewer only: nothing here reads the sim or
/// moves the road, the runners' feet or the camera.</summary>
internal sealed class RaceRelief
{
    /// <summary>Screen pixels per bound under which the banks are not drawn at all.</summary>
    public const float MinZoom = 7f;

    private const float ShadeGradeSpan = 0.045f;
    private static readonly Vector4 DownhillInk = new(0.045f, 0.06f, 0.085f, 0.13f);
    private static readonly Vector4 UphillInk = new(0.95f, 0.80f, 0.60f, 0.045f);

    private AetherRaceLive.Track? _built;

    public RaceTerrainRelief.Profile Profile { get; private set; } = RaceTerrainRelief.Profile.Empty;

    /// <summary>Builds the profile once per race, after the road clearance cache exists.</summary>
    public void Build(AetherRaceLive.Track track, string courseKey, RaceRoadClearance road)
    {
        Profile = RaceTerrainRelief.Build(track, courseKey, road.Clear);
        _built = track;
    }

    /// <summary>A faint warm cue uphill and a darker cool cue downhill, on the road surface. Drawn
    /// right after the base ribbon and before the dressing and every runner layer: a slope changes
    /// the road's light and never a runner's depth pass.</summary>
    public void DrawRoadShade(ImDrawListPtr dl, AetherRaceLive.Track track, Vector2 origin, Vector2 size, in StageCam cam)
    {
        if (!ReferenceEquals(_built, track) || cam.Zoom <= 0f)
        {
            return;
        }

        foreach (ref readonly var strip in Profile.RoadShades.AsSpan())
        {
            var a = cam.ToScreen(strip.LeftA);
            var b = cam.ToScreen(strip.LeftB);
            var c = cam.ToScreen(strip.RightB);
            var d = cam.ToScreen(strip.RightA);
            if (!OnStage(a, b, c, d, origin, size))
            {
                continue;
            }

            var strength = Math.Clamp(MathF.Abs(strip.Grade) / ShadeGradeSpan, 0f, 1f);
            var ink = strip.Grade < 0f ? DownhillInk : UphillInk;
            dl.AddQuadFilled(a, b, c, d, ElementFx.U32(ink with { W = ink.W * strength }));
        }
    }

    /// <summary>Does a projected quad touch the stage rect?</summary>
    public static bool OnStage(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 origin, Vector2 size)
    {
        var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        return max.X >= origin.X && min.X <= origin.X + size.X && max.Y >= origin.Y && min.Y <= origin.Y + size.Y;
    }

    /// <summary>A face, cap or foot band with A and B on the rail side and C and D outward. A tapered
    /// end meets the ground at a point, and ImGui's antialiased quad must not be handed two equal
    /// corners, so a collapsed end is drawn as a triangle.</summary>
    public static void Face(ImDrawListPtr dl, Vector2 a, Vector2 b, Vector2 c, Vector2 d, uint colour)
    {
        var startTip = Vector2.DistanceSquared(a, d) < 0.0001f;
        var endTip = Vector2.DistanceSquared(b, c) < 0.0001f;
        if (startTip && endTip)
        {
            return;
        }

        if (startTip)
        {
            dl.AddTriangleFilled(a, b, c, colour);
        }
        else if (endTip)
        {
            dl.AddTriangleFilled(a, b, d, colour);
        }
        else
        {
            dl.AddQuadFilled(a, b, c, d, colour);
        }
    }
}
