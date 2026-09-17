namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Numerics;
using AetherLove.Shared.Racing;
using Dalamud.Bindings.ImGui;

internal static class RaceSpectacle
{
    private static readonly AetherOS.PetKit.Rendering.LumiMiniatures Crowd = new();
    private static uint Ink(float r, float g, float b, float a = 1f) => ImGui.ColorConvertFloat4ToU32(new(r, g, b, a));
    private static Vector2 World(AetherRaceLive.TrackSample p, float lat) =>
        new(p.X - MathF.Sin(p.Heading) * lat, p.Y + MathF.Cos(p.Heading) * lat);

    public static float CameraBounds(AetherRaceLive.Track track, float focus)
    {
        var bounds = 10f;
        for (var s = MathF.Max(0f,focus-12f); s <= MathF.Min(track.Length,focus+24f); s += 3f)
        {
            var a = track.AtRoad(s,0,true);
            var b = track.AtRoad(s,1,true);
            bounds = MathF.Max(bounds,MathF.Abs(a.Y-b.Y)+a.Width+6f);
        }
        return bounds;
    }

    public static Vector2 Feet(AetherRaceLive.Track track, float s, int branch, float lat, in StageCam cam, float time, bool reduced)
    {
        var p = track.AtRoad(s, branch, true);
        var at = cam.ToScreen(World(p, lat));
        if (track.Scenery == AetherRaceLive.SceneryKind.DoubleHelix)
            at.Y -= AetherRaceLive.RoadDeck(track, s, branch) * cam.Zoom * 0.65f;
        if (track.Scenery == AetherRaceLive.SceneryKind.Skybridge && !reduced)
            at += cam.ToScreenDelta(new(-MathF.Sin(p.Heading), MathF.Cos(p.Heading))) * (0.12f * MathF.Sin(time * 1.2f + s * 0.08f));
        return at;
    }

    public static bool Below(AetherRaceLive.Track track, float s, int branch) =>
        AetherRaceLive.RoadsSeparated(track, s) && AetherRaceLive.RoadDeck(track, s, branch) < 1.7f;

    public static void Roads(ImDrawListPtr dl, AetherRaceLive.Track track, float focus, in StageCam cam, Vector2 origin, Vector2 size, float time, bool reduced, bool upperOnly = false)
    {
        var braid = track.Scenery == AetherRaceLive.SceneryKind.DoubleHelix;
        if (!braid && track.Scenery != AetherRaceLive.SceneryKind.Skybridge) return;
        var span = cam.VisibleRadius(origin, size) + 24f;
        var from = MathF.Max(0f, MathF.Floor((focus - span) / 1.5f) * 1.5f);
        var to = MathF.Min(track.Length, focus + span);
        for (var pass = 0; pass < (braid ? 2 : 1); pass++)
        for (var branch = 0; branch < (braid ? 2 : 1); branch++)
        for (var s = from + 1.5f; s <= to; s += 1.5f)
        {
            if (braid && branch == 1 && !AetherRaceLive.RoadsSeparated(track, s)) continue;
            var high = AetherRaceLive.RoadDeck(track, s - 0.75f, branch) >= 1.7f;
            if (braid && (high != (pass == 1) || (upperOnly && !high))) continue;
            if (!braid && upperOnly) continue;
            var half = track.At(s).Width * 0.5f;
            var a = Feet(track, s - 1.5f, branch, half, in cam, time, reduced);
            var b = Feet(track, s - 1.5f, branch, -half, in cam, time, reduced);
            var c = Feet(track, s, branch, -half, in cam, time, reduced);
            var d = Feet(track, s, branch, half, in cam, time, reduced);
            if (!StageCam.OnStage((a + c) * 0.5f, origin, size, 100f)) continue;
            var accent = braid ? (branch == 0 ? new Vector4(.38f,.78f,1f,1f) : new Vector4(.92f,.54f,1f,1f)) : new Vector4(.68f,.86f,.63f,1f);
            dl.AddQuadFilled(a, b, c, d, braid ? Ink(.20f,.23f,.34f) : Ink(.28f,.25f,.19f));
            var rail = ImGui.ColorConvertFloat4ToU32(accent);
            dl.AddLine(a, d, ImGui.ColorConvertFloat4ToU32(accent with { W = .12f }), cam.Zoom * .45f);
            dl.AddLine(b, c, ImGui.ColorConvertFloat4ToU32(accent with { W = .12f }), cam.Zoom * .45f);
            dl.AddLine(a, d, rail, MathF.Max(1f, cam.Zoom * .085f));
            dl.AddLine(b, c, rail, MathF.Max(1f, cam.Zoom * .085f));
            if (!braid || (int)(s / 1.5f) % 4 == 0)
                dl.AddLine(a, b, braid ? Ink(.65f,.8f,1f,.25f) : Ink(.07f,.09f,.08f,.55f), MathF.Max(1f, cam.Zoom * .075f));
            if ((int)(s / 1.5f) % 12 == 0)
            {
                for (var railSide = 0; railSide < 2; railSide++)
                {
                    var foot = railSide == 0 ? a : b;
                    var top = foot - new Vector2(0, cam.Zoom * (braid ? .7f : 2.6f));
                    dl.AddLine(foot + new Vector2(0, cam.Zoom * 2f), top, Ink(.12f,.16f,.23f), cam.Zoom * .22f);
                    dl.AddCircleFilled(top, cam.Zoom * .16f, rail, 10);
                }
            }
        }
    }

    public static void Backdrop(ImDrawListPtr dl, AetherRaceLive.Track track, float focus, in StageCam cam, Vector2 origin, Vector2 size, float time, bool reduced, float gust)
    {
        var kind = track.Scenery;
        if (kind == AetherRaceLive.SceneryKind.None) return;
        var sky = kind switch
        {
            AetherRaceLive.SceneryKind.Quarry => new Vector4(.24f,.16f,.10f,1f),
            AetherRaceLive.SceneryKind.Spillway => new Vector4(.06f,.24f,.28f,1f),
            AetherRaceLive.SceneryKind.Skybridge => new Vector4(.13f,.27f,.31f,1f),
            AetherRaceLive.SceneryKind.LookingGlass => new Vector4(.16f,.43f,.53f,1f),
            AetherRaceLive.SceneryKind.Grandstand => new Vector4(.22f,.10f,.25f,1f),
            _ => new Vector4(.09f,.08f,.22f,1f),
        };
        var topInk = ImGui.ColorConvertFloat4ToU32(sky);
        var bottomInk = ImGui.ColorConvertFloat4ToU32(sky * new Vector4(.32f,.38f,.48f,1f));
        dl.AddRectFilledMultiColor(origin, origin + size, topInk, topInk, bottomInk, bottomInk);
        if (kind == AetherRaceLive.SceneryKind.LookingGlass) IceShore(dl,track,focus,in cam,origin,size);
        for (var mote = 0; mote < 36; mote++)
        {
            var drift = reduced ? 0f : time * .005f;
            var at = origin + new Vector2(((mote * .618034f + drift) % 1f) * size.X, ((mote * .381966f - drift + 10f) % 1f) * size.Y);
            dl.AddCircleFilled(at, MathF.Max(.8f, cam.Zoom * .05f), Ink(.73f,.87f,1f,.14f), 6);
        }
        var span = cam.VisibleRadius(origin, size) + 25f;
        var spacing = kind == AetherRaceLive.SceneryKind.Skybridge ? 22f : 12f;
        var from = MathF.Max(spacing, MathF.Floor((focus - span) / spacing) * spacing);
        var to = MathF.Min(track.Length - 5f, focus + span);
        for (var s = from; s < to; s += spacing)
        {
            var p = track.AtLerp(s);
            var half = p.Width * .5f;
            for (var side = -1; side <= 1; side += 2)
            {
                if (kind == AetherRaceLive.SceneryKind.Skybridge && side != ((int)(s/22f) % 2 == 0 ? -1 : 1)) continue;
                var baseAt = cam.ToScreen(World(p, side * (half + 2.8f)));
                if (!StageCam.OnStage(baseAt, origin, size, cam.Zoom * 14f)) continue;
                var z = cam.Zoom;
                var phase = reduced ? 0f : time;
                if (kind == AetherRaceLive.SceneryKind.Quarry)
                {
                    var variant = ((int)(s/12f)+(side > 0 ? 3 : 0)) % 6;
                    if (variant != 0 && variant != 3)
                    {
                        QuarryProp(dl,baseAt,z,variant,s);
                        continue;
                    }
                }
                if (kind is AetherRaceLive.SceneryKind.Quarry or AetherRaceLive.SceneryKind.Skybridge)
                {
                    var height = kind == AetherRaceLive.SceneryKind.Quarry ? 2.6f : 7f;
                    for (var tier = kind == AetherRaceLive.SceneryKind.Quarry ? 3 : 0; tier >= 0; tier--)
                    {
                        var centre = baseAt + new Vector2(side * tier * z * 1.0f, z * tier * .85f);
                        var top = centre - new Vector2(0, z * height);
                        var w = z * (2.8f + tier * .65f);
                        dl.AddQuadFilled(top + new Vector2(-w,0), top + new Vector2(w,0), centre + new Vector2(w * .65f,z * 3), centre + new Vector2(-w * .65f,z * 3), Ink(.12f + tier * .025f,.15f + tier * .018f,.18f + tier * .008f));
                        dl.AddTriangleFilled(top + new Vector2(-w,0), top + new Vector2(w,0), top + new Vector2(-w * .2f,-z * .9f), Ink(.40f,.37f,.29f,.85f));
                        dl.AddLine(top + new Vector2(-w,0), top + new Vector2(w,0), Ink(.75f,.59f,.36f,.65f), z * .09f);
                    }
                }
                else if (kind == AetherRaceLive.SceneryKind.Spillway)
                {
                    var a = cam.ToScreen(World(p, side * (half + .9f)));
                    var b = cam.ToScreen(World(track.AtLerp(s + 12f), side * (half + .9f)));
                    var low = new Vector2(0,z * 4.8f);
                    dl.AddQuadFilled(a,b,b + low,a + low,Ink(.10f,.38f,.46f,.7f));
                    for (var i = 0; i < 7; i++)
                    {
                        var at = Vector2.Lerp(a,b,i / 6f);
                        var f = ((phase * .7f + i * .17f) % 1f);
                        dl.AddLine(at + low * f, at + low * MathF.Min(1f,f + .25f), Ink(.59f,.9f,1f,.55f), z * .075f);
                    }
                    dl.AddLine(a,b,Ink(.60f,.81f,.81f),z * .16f);
                    if ((int)(s / 12f) % 5 == 2 && side == 1)
                    {
                        var wheel = baseAt - new Vector2(0,z);
                        var r = z * 3.7f;
                        dl.AddCircle(wheel,r,Ink(.34f,.23f,.15f),36,z * .38f);
                        dl.AddCircle(wheel,r * .85f,Ink(.85f,.68f,.4f),36,z * .10f);
                        for (var spoke = 0; spoke < 12; spoke++)
                        {
                            var angle = phase * .35f + spoke * MathF.PI / 6f;
                            var tip = wheel + new Vector2(MathF.Cos(angle),MathF.Sin(angle)) * r;
                            dl.AddLine(wheel,tip,Ink(.62f,.43f,.25f),z * .12f);
                            dl.AddCircleFilled(tip,z * .24f,Ink(.75f,.55f,.31f),6);
                        }
                        dl.AddCircleFilled(wheel,z * .45f,Ink(.91f,.77f,.43f),12);
                    }
                }
                else if (kind == AetherRaceLive.SceneryKind.LookingGlass)
                {
                    var pine = cam.ToScreen(World(p,side*(half+8f+MathF.Sin(s*.13f))));
                    var height = z*(2.2f+.6f*MathF.Sin(s*.31f));
                    dl.AddLine(pine,pine-new Vector2(0,height),Ink(.27f,.43f,.48f),z*.10f);
                    for (var branch = 0; branch < 5; branch++)
                    {
                        var tip = pine-new Vector2(0,height*(.25f+branch*.15f));
                        var width = height*(.34f-branch*.06f);
                        for (var twig = -1; twig <= 1; twig += 2)
                        {
                            var end = tip+new Vector2(twig*width,height*.16f);
                            dl.AddLine(tip,end,Ink(.28f,.51f,.56f),z*.20f);
                            dl.AddLine(tip-new Vector2(0,z*.08f),end-new Vector2(0,z*.08f),Ink(.85f,.97f,.98f),z*.12f);
                        }
                    }
                }
                else if (kind == AetherRaceLive.SceneryKind.Grandstand)
                {
                    for (var row = 0; row < 3; row++)
                    {
                        var at = baseAt + new Vector2(side * row * z * .9f, -row * z * .75f);
                        dl.AddRectFilled(at - new Vector2(z * 2f,z * .3f),at + new Vector2(z * 2f,z * .45f),Ink(.28f,.19f,.30f));
                    }
                    for (var row = 2; row >= 0; row--)
                    {
                        var at = baseAt + new Vector2(side * row * z * .9f, -row * z * .75f);
                        for (var fan = 0; fan < 5; fan++)
                        {
                            var head = at + new Vector2((fan - 2) * z * .65f,-z * .65f);
                            if (!StageCam.OnStage(head, origin, size, z)) continue;
                            Crowd.DrawCrowdForm(dl, 1 + (fan + row * 5 + (int)(s / 12f) * 3 + (side > 0 ? 7 : 0)) % 14,
                                CelebrationColor(fan + row * 2 + (int)(s / 12f)), head + new Vector2(0,z*.45f),
                                z*.80f, fan + row * 5 + (int)s, time, reduced);
                        }
                    }
                    CelebrationProps(dl, baseAt + new Vector2(side*z*3.6f, 0), z, (int)(s/12f) + (side > 0 ? 1 : 0), phase);
                    var pole = baseAt - new Vector2(0,z * 4f);
                    dl.AddLine(baseAt,pole,Ink(.85f,.71f,.43f),z * .10f);
                    dl.AddTriangleFilled(pole,pole + new Vector2(side * z * 1.8f,z * .45f),pole + new Vector2(0,z),Ink(.93f,.41f,.65f));
                }
                else if (kind == AetherRaceLive.SceneryKind.DoubleHelix)
                {
                    var a = Feet(track,s,0,0,in cam,time,reduced);
                    var b = Feet(track,s,1,0,in cam,time,reduced);
                    dl.AddLine(a,b,Ink(.49f,.65f,1f,.18f),z * .1f);
                    dl.AddCircleFilled(baseAt,z * .3f,Ink(.54f,.74f,1f,.6f),12);
                    dl.AddCircle(baseAt,z * .65f,Ink(.63f,.52f,1f,.24f),16,z * .08f);
                }
            }
        }
        Flags(dl,track,focus,in cam,origin,size,time,reduced,gust);
    }

    private static Vector4 CelebrationColor(int index) => (index % 6) switch
    {
        0 => new(.95f,.45f,.62f,1), 1 => new(.48f,.82f,1,1),
        2 => new(.72f,.55f,1,1), 3 => new(1,.78f,.35f,1),
        4 => new(.45f,.88f,.68f,1), _ => new(1,.60f,.36f,1),
    };

    private static void CelebrationProps(ImDrawListPtr dl, Vector2 at, float z, int station, float phase)
    {
        var gold = Ink(1,.81f,.43f);
        if (station % 3 == 0)
        {
            for(var i=0;i<3;i++)
            {
                var tip=at+new Vector2((i-1)*z*.65f+MathF.Sin(phase*1.2f+i)*z*.10f,-z*(3.2f+i*.4f));
                dl.AddLine(at,tip,Ink(.9f,.8f,.9f,.65f),MathF.Max(1,z*.035f));
                dl.AddCircleFilled(tip,z*.48f,ImGui.ColorConvertFloat4ToU32(CelebrationColor(station+i)),14);
                dl.AddCircleFilled(tip-new Vector2(z*.15f,z*.15f),z*.10f,Ink(1,1,1,.6f),6);
            }
        }
        else if (station % 3 == 1)
        {
            for(var i=0;i<3;i++)
            {
                var p=at+new Vector2((i-1)*z*.8f,-(i==1 ? z*.8f : 0));
                dl.AddRectFilled(p-new Vector2(z*.45f,z*.8f),p+new Vector2(z*.45f,0),ImGui.ColorConvertFloat4ToU32(CelebrationColor(station+i)),z*.06f);
                dl.AddLine(p-new Vector2(0,z*.8f),p,gold,z*.12f);
                dl.AddLine(p-new Vector2(z*.45f,z*.45f),p+new Vector2(z*.45f,-z*.45f),gold,z*.10f);
                dl.AddCircle(p-new Vector2(z*.16f,z*.9f),z*.16f,gold,8,z*.07f);
                dl.AddCircle(p+new Vector2(z*.16f,-z*.9f),z*.16f,gold,8,z*.07f);
            }
        }
        else
        {
            for(var side=-1;side<=1;side+=2) dl.AddLine(at+new Vector2(side*z*1.5f,0),at+new Vector2(side*z*1.5f,-z*3),gold,z*.10f);
            for(var i=0;i<6;i++)
            {
                var a=at+new Vector2(z*(-1.5f+i*.5f),z*(-3f+.45f*MathF.Sin(i*MathF.PI/6)));
                var b=a+new Vector2(z*.5f,0);
                dl.AddLine(a,b,gold,z*.045f);
                dl.AddTriangleFilled(a,b,(a+b)*.5f+new Vector2(0,z*.55f),ImGui.ColorConvertFloat4ToU32(CelebrationColor(i+station)));
            }
        }
    }

    private static void QuarryProp(ImDrawListPtr dl, Vector2 at, float z, int variant, float station)
    {
        if (variant == 1)
        {
            var height = z*(2.8f+.4f*MathF.Sin(station));
            var top = at-new Vector2(0,height);
            var green = Ink(.27f,.48f,.29f);
            dl.AddLine(at,top,green,z*.55f);
            dl.AddCircleFilled(top,z*.275f,green,12);
            dl.AddLine(at-new Vector2(z*.12f,0),top-new Vector2(z*.12f,0),Ink(.52f,.68f,.35f),z*.07f);
            for (var arm = -1; arm <= 1; arm += 2)
            {
                var joint = at-new Vector2(0,height*(arm < 0 ? .4f : .57f));
                var elbow = joint+new Vector2(arm*z*.8f,0);
                var tip = elbow-new Vector2(0,z*(arm < 0 ? 1.1f : .8f));
                dl.AddLine(joint,elbow,green,z*.40f);
                dl.AddLine(elbow,tip,green,z*.40f);
                dl.AddCircleFilled(elbow,z*.20f,green,10);
                dl.AddCircleFilled(tip,z*.20f,green,10);
                dl.AddLine(elbow-new Vector2(z*.06f,0),tip-new Vector2(z*.06f,0),Ink(.58f,.70f,.40f),z*.055f);
            }
            for (var spine = 0; spine < 5; spine++)
            {
                var y = at.Y-height*(.15f+spine*.17f);
                dl.AddLine(new(at.X-z*.24f,y),new(at.X-z*.36f,y-z*.07f),Ink(.82f,.82f,.58f),MathF.Max(1f,z*.035f));
                dl.AddLine(new(at.X+z*.24f,y),new(at.X+z*.35f,y-z*.06f),Ink(.82f,.82f,.58f),MathF.Max(1f,z*.035f));
            }
            for (var petal = 0; petal < 5; petal++)
            {
                var angle = petal*MathF.Tau/5f;
                dl.AddCircleFilled(top-new Vector2(0,z*.18f)+new Vector2(MathF.Cos(angle),MathF.Sin(angle))*z*.18f,z*.13f,Ink(.94f,.48f,.48f),8);
            }
            dl.AddCircleFilled(top-new Vector2(0,z*.18f),z*.09f,Ink(1f,.84f,.38f),8);
        }
        else if (variant == 2)
        {
            for (var crystal = 0; crystal < 3; crystal++)
            {
                var foot = at+new Vector2((crystal-1)*z*.55f,z*.12f*crystal);
                var top = foot+new Vector2(z*(crystal-1)*.25f,-z*(crystal == 1 ? 2.2f : 1.3f));
                var left = foot-new Vector2(z*.35f,z*.3f);
                var right = foot+new Vector2(z*.35f,-z*.3f);
                dl.AddQuadFilled(foot,left,top,right,Ink(.63f,.39f,.15f));
                dl.AddTriangleFilled(foot,top,right,Ink(.95f,.68f,.27f));
                dl.AddLine(left,top,Ink(1f,.86f,.48f),z*.06f);
            }
            dl.AddCircleFilled(at+new Vector2(-z,z*.2f),z*.4f,Ink(.44f,.34f,.25f),9);
        }
        else if (variant == 4)
        {
            for (var tuft = 0; tuft < 9; tuft++)
            {
                var angle = MathF.PI*(.12f+tuft*.095f);
                var tip = at-new Vector2(MathF.Cos(angle),MathF.Sin(angle))*z*(1f+.35f*MathF.Sin(station+tuft));
                dl.AddLine(at,tip,tuft % 2 == 0 ? Ink(.76f,.61f,.35f) : Ink(.49f,.51f,.28f),z*.075f);
                if (tuft % 3 == 0) dl.AddCircleFilled(tip,z*.11f,Ink(.94f,.79f,.43f),7);
            }
        }
        else
        {
            for (var rock = 0; rock < 4; rock++)
            {
                var centre = at+new Vector2((rock % 2-.5f)*z,z*(.25f-rock*.4f));
                var radius = z*(.7f-rock*.08f);
                dl.AddCircleFilled(centre,radius,Ink(.45f+rock*.03f,.32f+rock*.025f,.23f),7);
                dl.AddLine(centre+new Vector2(-radius*.6f,-radius*.5f),centre+new Vector2(radius*.3f,-radius*.65f),Ink(.78f,.59f,.37f),z*.08f);
            }
        }
    }

    private static void IceShore(ImDrawListPtr dl, AetherRaceLive.Track track, float focus, in StageCam cam, Vector2 origin, Vector2 size)
    {
        var span = cam.VisibleRadius(origin,size)+20f;
        for (var s = MathF.Max(0f,MathF.Floor((focus-span)/2f)*2f); s < MathF.Min(track.Length,focus+span); s += 2f)
        for (var side = -1; side <= 1; side += 2)
        {
            var p = track.AtLerp(s);
            var next = track.AtLerp(MathF.Min(track.Length,s+2f));
            var width = p.Width*.5f+4.8f+MathF.Sin(s*.12f)*1.1f+MathF.Sin(s*.37f)*.3f;
            var nextWidth = next.Width*.5f+4.8f+MathF.Sin((s+2f)*.12f)*1.1f+MathF.Sin((s+2f)*.37f)*.3f;
            var a = cam.ToScreen(World(p,side*width));
            var b = cam.ToScreen(World(next,side*nextWidth));
            var outerA = cam.ToScreen(World(p,side*(width+18f)));
            var outerB = cam.ToScreen(World(next,side*(nextWidth+18f)));
            dl.AddQuadFilled(a,b,outerB,outerA,Ink(.66f,.84f,.88f));
            var snowA = cam.ToScreen(World(p,side*(width+.45f)));
            var snowB = cam.ToScreen(World(next,side*(nextWidth+.45f)));
            dl.AddQuadFilled(snowA,snowB,outerB,outerA,Ink(.87f,.95f,.96f));
            dl.AddLine(a,b,Ink(.61f,.89f,.94f,.6f),cam.Zoom*.16f);
            if ((int)s % 10 == 0)
            {
                var ice = cam.ToScreen(World(p,side*(width-1f)));
                dl.AddLine(ice,ice+cam.ToScreenDelta(new Vector2(MathF.Cos(p.Heading),MathF.Sin(p.Heading))*2.5f),Ink(.64f,.88f,.93f,.20f),cam.Zoom*.08f);
            }
        }
    }

    private static void Flags(ImDrawListPtr dl, AetherRaceLive.Track track, float focus, in StageCam cam, Vector2 origin, Vector2 size, float time, bool reduced, float gust)
    {
        if (track.Scenery != AetherRaceLive.SceneryKind.Skybridge) return;
        var span = cam.VisibleRadius(origin,size) + 25f;
        for (var s = MathF.Max(22f,MathF.Floor((focus-span)/22f)*22f); s < MathF.Min(track.Length-5f,focus+span); s += 22f)
        for (var side = -1; side <= 1; side += 2)
        {
            if (side != ((int)(s/22f) % 2 == 0 ? -1 : 1)) continue;
            var p = track.AtLerp(s);
            var top = cam.ToScreen(World(p,side*(p.Width*.5f+2.8f)))-new Vector2(0,cam.Zoom*7.7f);
            if (!StageCam.OnStage(top,origin,size,cam.Zoom*4f)) continue;
            var flap = MathF.Sin((reduced ? 0f : time)*2.4f+s)*.5f+gust*.6f;
            dl.AddLine(top,top-new Vector2(0,cam.Zoom*2.2f),Ink(.90f,.94f,.82f),cam.Zoom*.10f);
            dl.AddTriangleFilled(top-new Vector2(0,cam.Zoom*2.2f),top+new Vector2(cam.Zoom*(1.8f+flap),-cam.Zoom*1.5f),top-new Vector2(0,cam.Zoom*.9f),Ink(.80f,.96f,.63f));
        }
    }

    public static void Surface(ImDrawListPtr dl, AetherRaceLive.Track track, float focus, in StageCam cam, Vector2 origin, Vector2 size, float time, bool reduced)
    {
        if (track.Scenery != AetherRaceLive.SceneryKind.LookingGlass) return;
        var span = cam.VisibleRadius(origin,size) + 10f;
        for (var s = MathF.Max(2f,MathF.Floor((focus - span) / 5f) * 5f); s < MathF.Min(track.Length,focus + span); s += 5f)
        {
            var p = track.AtLerp(s);
            var next = track.AtLerp(MathF.Min(track.Length, s + 5f));
            var a = cam.ToScreen(World(p,p.Width*.49f));
            var b = cam.ToScreen(World(p,-p.Width*.49f));
            var c = cam.ToScreen(World(next,-next.Width*.49f));
            var d = cam.ToScreen(World(next,next.Width*.49f));
            dl.AddQuadFilled(a,b,c,d,Ink(.12f,.43f,.59f));
            dl.AddQuadFilled(Vector2.Lerp(a,b,.2f),Vector2.Lerp(a,b,.8f),Vector2.Lerp(d,c,.8f),Vector2.Lerp(d,c,.2f),Ink(.23f,.65f,.74f,.45f));
            dl.AddLine(a,d,Ink(.77f,.95f,1f,.85f),cam.Zoom*.13f);
            dl.AddLine(b,c,Ink(.77f,.95f,1f,.85f),cam.Zoom*.13f);
            for (var bubble = 0; bubble < 3; bubble++)
            {
                var sample = track.AtLerp(MathF.Min(track.Length,s+bubble*.85f));
                var at = cam.ToScreen(World(sample,MathF.Sin(s*1.7f+bubble*2.4f)*sample.Width*.34f));
                var radius = cam.Zoom*(.10f+bubble*.035f);
                dl.AddCircleFilled(at,radius,Ink(.44f,.83f,.91f,.20f),12);
                dl.AddCircle(at,radius,Ink(.72f,.96f,1f,.45f),12,MathF.Max(1f,cam.Zoom*.025f));
                dl.AddCircleFilled(at-new Vector2(radius*.25f,radius*.30f),radius*.22f,Ink(.87f,1f,1f,.70f),8);
            }
            var previous = cam.ToScreen(World(p,MathF.Sin(s*.7f)*p.Width*.30f));
            for (var crack = 1; (int)(s/5f) % 3 == 0 && crack <= 4; crack++)
            {
                var sample = track.AtLerp(MathF.Min(track.Length,s+crack*.9f));
                var point = cam.ToScreen(World(sample,MathF.Sin(s*.7f+crack*.8f)*sample.Width*.32f));
                dl.AddLine(previous,point,Ink(.04f,.22f,.34f,.7f),cam.Zoom*.10f);
                dl.AddLine(previous-new Vector2(0,1f),point-new Vector2(0,1f),Ink(.70f,.94f,1f,.72f),MathF.Max(1f,cam.Zoom*.035f));
                dl.AddLine(point,Vector2.Lerp(point,crack % 2 == 0 ? a : b,.36f),Ink(.66f,.90f,.97f,.42f),MathF.Max(1f,cam.Zoom*.03f));
                previous = point;
            }
            var shimmer = reduced ? .16f : .12f+.07f*MathF.Sin(time*.7f+s*.13f);
            dl.AddLine(Vector2.Lerp(a,b,.12f),Vector2.Lerp(d,c,.7f),Ink(.80f,.98f,1f,shimmer),cam.Zoom*.16f);
        }
    }

    public static void Overhead(ImDrawListPtr dl, AetherRaceLive.Track track, float focus, in StageCam cam, Vector2 origin, Vector2 size, float time, bool reduced)
    {
        var kind = track.Scenery;
        var span = cam.VisibleRadius(origin,size) + 15f;
        for (var s = MathF.Max(8f,MathF.Floor((focus - span) / 8f) * 8f); s < MathF.Min(track.Length - 5f,focus + span); s += 8f)
        {
            var p = track.AtLerp(s);
            var tunnel = p.Section is "the crystal tunnel" or "the underpass";
            var waterfall = p.Section == "the falling curtain";
            var arch = p.Section == "the stone arch";
            if (!tunnel && !waterfall && !arch) continue;
            var a = cam.ToScreen(World(p,p.Width * .65f));
            var b = cam.ToScreen(World(p,-p.Width * .65f));
            var z = cam.Zoom;
            var lift = new Vector2(0,z * (arch ? 4f : 2.3f));
            if (arch)
            {
                var previous = a;
                for (var stone = 1; stone <= 16; stone++)
                {
                    var t = stone / 16f;
                    var point = Vector2.Lerp(a,b,t) - lift * MathF.Sin(t * MathF.PI);
                    dl.AddLine(previous,point,Ink(.32f,.35f,.30f),z * .9f);
                    dl.AddLine(previous-new Vector2(0,z*.3f),point-new Vector2(0,z*.3f),Ink(.72f,.73f,.54f),z * .16f);
                    previous = point;
                }
                continue;
            }
            if (kind == AetherRaceLive.SceneryKind.LookingGlass)
            {
                var previous = a;
                for (var facet = 1; facet <= 8; facet++)
                {
                    var t = facet/8f;
                    var point = Vector2.Lerp(a,b,t)-new Vector2(0,z*4.2f)*MathF.Sin(t*MathF.PI);
                    var depth = new Vector2(z*.25f,z*.65f);
                    dl.AddQuadFilled(previous,point,point+depth,previous+depth,Ink(.36f,.72f,.86f,.43f));
                    dl.AddLine(previous,point,Ink(.80f,.98f,1f,.78f),z*.13f);
                    dl.AddLine(previous+depth,point+depth,Ink(.34f,.66f,.86f,.50f),z*.07f);
                    if (facet % 2 == 0)
                        dl.AddLine(point,point+new Vector2(0,z*(.45f+.2f*MathF.Sin(s+facet))),Ink(.70f,.95f,1f,.72f),z*.12f);
                    previous = point;
                }
                continue;
            }
            var tint = waterfall ? Ink(.49f,.85f,1f,.19f) : Ink(.43f,.35f,.47f,.24f);
            dl.AddQuadFilled(a,b,b-lift,a-lift,tint);
            dl.AddLine(a-lift,b-lift,kind == AetherRaceLive.SceneryKind.LookingGlass ? Ink(.76f,.94f,1f,.8f) : Ink(.75f,.71f,.56f,.7f),z * .14f);
            dl.AddLine(a,a-lift,Ink(.66f,.77f,.81f,.65f),z * .12f);
            dl.AddLine(b,b-lift,Ink(.66f,.77f,.81f,.65f),z * .12f);
            if (waterfall)
                for (var i = 0; i < 12; i++)
                {
                    var f = reduced ? .5f : (time * 1.1f + i * .13f) % 1f;
                    var at = Vector2.Lerp(a,b,i / 11f) - lift * f;
                    dl.AddLine(at,at + lift * .18f,Ink(.79f,.95f,1f,.45f),z * .04f);
                }
        }
        if (kind == AetherRaceLive.SceneryKind.Grandstand && !reduced)
        {
            for (var i = 0; i < 36; i++)
            {
                var p = track.AtLerp(Math.Clamp(focus + (i - 18) * 1.6f,0,track.Length));
                var at = cam.ToScreen(World(p,MathF.Sin(i * 4.1f + time * .3f) * (p.Width + 2f)));
                at.Y -= ((i * .27f + time * .8f) % 3f) * cam.Zoom;
                var colour = i % 2 == 0 ? Ink(.97f,.76f,.35f,.8f) : Ink(.89f,.48f,.72f,.8f);
                dl.AddLine(at,at + new Vector2(cam.Zoom * .16f,cam.Zoom * .09f),colour,MathF.Max(1f,cam.Zoom * .07f));
            }
        }
    }
}
