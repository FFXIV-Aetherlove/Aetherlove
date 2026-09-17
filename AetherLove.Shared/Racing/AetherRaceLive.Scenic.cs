namespace AetherLove.Shared.Racing;

using System;

public static partial class AetherRaceLive
{
    public enum SceneryKind { None, DoubleHelix, Quarry, Spillway, Skybridge, LookingGlass, Grandstand }

    private static CourseDef[] ScenicCourses() =>
    [
        new() { Key = "double-helix", Name = "the Double Helix", Category = RaceCategory.Journey, Terrain = "lightning", Width = 5.4f, Scenery = SceneryKind.DoubleHelix,
            Segments = [Straight(50f, 0f, "the fork"), Straight(100f, 0f, "the first crossing", element: "lightning"), Straight(100f, 0f, "the upper road"), Straight(100f, 0f, "the second crossing", element: "lightning"), Straight(100f, 0f, "the lower road"), Straight(40f, 0f, "the reunion")] },
        new() { Key = "quarry-drop", Name = "the Quarry Drop", Category = RaceCategory.Sprint, Terrain = "earth", Width = 6.2f, Scenery = SceneryKind.Quarry,
            Segments = [Straight(45f, 0f, "the quarry floor"), Corner(-65f, 28f, -0.025f, "the first shelf"), Straight(38f, -0.035f, "the stone slide", element: "earth"), Corner(60f, 30f, -0.02f, "the lower shelf"), Straight(28f, 0.025f, "the daylight ramp")], RunInGrade = 0.01f },
        new() { Key = "spillway", Name = "the Spillway", Category = RaceCategory.Route, Terrain = "water", Width = 6.2f, Scenery = SceneryKind.Spillway,
            Segments = [Straight(58f, 0f, "the aqueduct"), Corner(-50f, 36f, 0f, "the first meander"), Straight(48f, -0.01f, "the falling curtain", element: "water"), Corner(55f, 34f, 0f, "the great wheel"), Straight(36f, 0f, "the causeway", width: 4.8f), Corner(-45f, 38f, 0f, "the far bank"), Straight(40f, 0f, "the shallows", element: "water"), Corner(40f, 36f, 0f, "the home bank")] },
        new() { Key = "skybridge", Name = "the Skybridge", Category = RaceCategory.Journey, Terrain = "wind", Width = 6.6f, Scenery = SceneryKind.Skybridge,
            Segments = [Straight(72f, -0.015f, "the high road"), Corner(-55f, 38f, 0f, "the ridge sweep"), Straight(66f, -0.025f, "the hanging span", element: "wind"), Corner(50f, 36f, 0f, "the open shoulder"), Straight(62f, 0f, "the sky shelf"), Corner(-45f, 40f, 0f, "the stone arch"), Straight(52f, -0.015f, "the long gust", element: "wind"), Corner(45f, 38f, 0f, "the lee")], RunInGrade = -0.015f },
        new() { Key = "looking-glass", Name = "the Looking Glass", Category = RaceCategory.Journey, Terrain = "ice", Width = 6.8f, Scenery = SceneryKind.LookingGlass,
            Segments = [Straight(84f, 0f, "the frozen lake"), Corner(-40f, 42f, 0f, "the frozen shore"), Straight(74f, -0.015f, "the crystal tunnel", element: "ice"), Corner(45f, 40f, 0f, "the pale curve"), Straight(66f, 0f, "the still expanse"), Corner(-40f, 42f, 0f, "the far shore"), Straight(48f, -0.015f, "the ice road", element: "ice"), Corner(40f, 40f, 0f, "the thaw")], RunInGrade = -0.01f },
        new() { Key = "grandstand-dash", Name = "the Grandstand Dash", Category = RaceCategory.Sprint, Terrain = "", Width = 6.5f, Scenery = SceneryKind.Grandstand,
            Segments = [Straight(66f, 0f, "the festival green"), Corner(-45f, 38f, 0f, "the cheering stands"), Straight(42f, 0f, "the underpass"), Corner(45f, 38f, 0f, "the arena entrance")] },
    ];

    public static bool RoadsSeparated(Track track, float s) => track.Scenery == SceneryKind.DoubleHelix && s > 50f && s < 450f;

    public static float RoadDeck(Track track, float s, int branch)
    {
        if (!RoadsSeparated(track, s)) return 0f;
        var p = (s - 50f) / 400f;
        var envelope = Math.Clamp(MathF.Min(p, 1f - p) * 12f, 0f, 1f);
        envelope = envelope * envelope * (3f - 2f * envelope);
        return envelope * (1.7f + (1.5f * (branch == 0 ? 1f : -1f) * (float)PortableMath.Cos(p * Math.PI * 4)));
    }

    private static void BuildScenicRoads(Track track)
    {
        if (track.Scenery != SceneryKind.DoubleHelix) return;
        var left = new TrackSample[track.Count];
        var right = new TrackSample[track.Count];
        float Offset(float s)
        {
            var p = Math.Clamp((s - 50f) / 400f, 0f, 1f);
            var envelope = Math.Clamp(MathF.Min(p, 1f - p) * 12f, 0f, 1f);
            envelope = envelope * envelope * (3f - 2f * envelope);
            return 9f * envelope * (float)PortableMath.Sin(p * Math.PI * 4);
        }
        var x = 0f;
        var previousY = 0f;
        var previousH = 0f;
        for (var j = 0; j < track.Count; j++)
        {
            var s = j * track.Step;
            var y = Offset(s);
            var dy = y - previousY;
            if (j > 0) x += MathF.Sqrt(MathF.Max(0f, track.Step * track.Step - dy * dy));
            var derivative = Offset(s + 0.25f) - Offset(s - 0.25f);
            var h = RoadsSeparated(track, s) ? MathF.Atan2(derivative, MathF.Sqrt(MathF.Max(0f, 0.25f - derivative * derivative))) : 0f;
            var k = j == 0 || !RoadsSeparated(track, s) ? 0f : (h - previousH) / track.Step;
            left[j] = new TrackSample(x, y, h, k, track.Gs[j], track.Ws[j], track.Elems[j], track.Names[j]);
            right[j] = left[j] with { Y = -y, Heading = -h, Kappa = -k };
            track.Xs[j] = x;
            track.Ys[j] = 0f;
            track.Hs[j] = 0f;
            track.Ks[j] = 0f;
            previousY = y;
            previousH = h;
        }
        track.Roads = [left, right];
    }

    public static float PlaybackRate(Race resolved) =>
        MathF.Max(1f, MathF.Max(resolved.Time + Dials.Dt, resolved.WinnerTime + 3.5f) / 59.5f);
}



