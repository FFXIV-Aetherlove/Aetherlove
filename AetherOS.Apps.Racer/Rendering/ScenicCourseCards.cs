namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Racing;
using Dalamud.Bindings.ImGui;

internal static class ScenicCourseCards
{
    private static readonly Dictionary<string, AetherRaceLive.Track> Tracks = new();

    public static bool Draw(ImDrawListPtr dl, string key, Vector2 tl, Vector2 br)
    {
        var course = AetherRaceLive.CourseByKey(key);
        if (course == null || course.Scenery == AetherRaceLive.SceneryKind.None) return false;
        if (!Tracks.TryGetValue(key, out var track))
        {
            track = AetherRaceLive.BuildTrack(course);
            Tracks[key] = track;
        }
        var size = br - tl;
        var focus = course.Key switch { "double-helix" => 240f, "spillway" => 154f, "looking-glass" => 150f, _ => track.Length * .45f };
        var sample = track.AtLerp(focus);
        var cam = StageCam.From(new(sample.X,sample.Y), -sample.Heading - MathF.PI * .5f,
            size.X / (track.Roads != null ? 34f : 24f), tl + size * .5f);
        dl.PushClipRect(tl,br,true);
        dl.AddRectFilled(tl,br,RaceDressing.NightInk);
        RaceSpectacle.Backdrop(dl,track,focus,in cam,tl,size,0f,true,0f);
        if (track.Roads != null || course.Scenery == AetherRaceLive.SceneryKind.Skybridge)
            RaceSpectacle.Roads(dl,track,focus,in cam,tl,size,0f,true);
        else
        {
            var from = MathF.Max(0f,focus - 60f);
            var end = MathF.Min(track.Length,focus + 60f);
            Vector2 Edge(float s, float sign)
            {
                var p = track.AtLerp(s);
                return cam.ToScreen(new(p.X - MathF.Sin(p.Heading) * p.Width * .5f * sign,p.Y + MathF.Cos(p.Heading) * p.Width * .5f * sign));
            }
            for (var s = from + 1f; s < end; s++)
            {
                var a=Edge(s-1f,1f); var b=Edge(s-1f,-1f); var c=Edge(s,-1f); var d=Edge(s,1f);
                dl.AddQuadFilled(a,b,c,d,RaceDressing.RoadInk(course.Terrain));
                dl.AddLine(a,d,RaceDressing.KerbInk(course.Terrain),1.5f);
                dl.AddLine(b,c,RaceDressing.KerbInk(course.Terrain),1.5f);
            }
        }
        RaceSpectacle.Surface(dl,track,focus,in cam,tl,size,0f,true);
        RaceSpectacle.Overhead(dl,track,focus,in cam,tl,size,0f,true);
        dl.PopClipRect();
        return true;
    }
}

