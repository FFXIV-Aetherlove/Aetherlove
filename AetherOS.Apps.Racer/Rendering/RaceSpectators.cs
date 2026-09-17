namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Racing;
using AetherOS.PetKit.Rendering;
using Dalamud.Bindings.ImGui;

internal sealed class RaceSpectators
{
    private readonly List<(Vector2 At, int Variant)> sites = [];
    private readonly LumiMiniatures miniatures = new();
    private string element = "";
    private Vector4 tint;

    public void Build(AetherRaceLive.Track track, string ground, RaceRoadClearance clearance)
    {
        sites.Clear();
        element = ground;
        var look = ElementFx.For(ground);
        tint = look.Key.Length == 0 ? new Vector4(.64f,.62f,.70f,1f) : look.Tint;
        if (track.Scenery == AetherRaceLive.SceneryKind.Grandstand) return;
        var length = track.LapRows > 0 ? track.LapLength : track.Length;
        var variant = 0;
        for (var s = 14f; s < length - 12f; s += 18f)
        {
            for (var branch = 0; branch < (track.Roads == null ? 1 : 2); branch++)
            {
                var p = track.AtRoad(s, branch, true);
                var side = variant % 2 == 0 ? -1f : 1f;
                var lat = side * (p.Width * .5f + 1.6f);
                var at = new Vector2(p.X - MathF.Sin(p.Heading) * lat, p.Y + MathF.Cos(p.Heading) * lat);
                var clear = clearance.Clear(at, .9f);
                if (clear && track.Roads != null)
                {
                    for (var t = 0f; t < length && clear; t += 1f)
                    for (var road = 0; road < 2; road++)
                    {
                        var sample = track.AtRoad(t, road, true);
                        var radius = sample.Width * .5f + 1.2f;
                        if (Vector2.DistanceSquared(at, new(sample.X, sample.Y)) < radius * radius) { clear = false; break; }
                    }
                }
                if (clear) sites.Add((at, variant));
                variant++;
            }
        }
    }

    public void Draw(ImDrawListPtr dl, in StageCam cam, Vector2 origin, Vector2 size, float time, bool reduced)
    {
        var drawn = 0;
        foreach (var site in sites)
        {
            var p = cam.ToScreen(site.At);
            if (!StageCam.OnStage(p, origin, size, cam.Zoom * 1.8f)) continue;
            miniatures.Draw(dl, element, tint, p, cam.Zoom * 1.35f, site.Variant, time, reduced);
            if (++drawn == 12) break;
        }
    }
}
