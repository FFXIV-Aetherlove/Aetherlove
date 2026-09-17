namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Racing;
using Dalamud.Bindings.ImGui;

internal sealed class ClassicCourseScenery
{
    private enum Prop { Pine, Willow, Orchard, Basalt, Crystal, Flowers, Reeds, Pond, Fence, Mill, Hut, Tower, Dome, Ruin, Fan, Fountain, Fissure, Mushroom, Cairn, Snow, Cliff, Lantern, Bird, Windsock }
    private readonly record struct Site(Vector2 At, Prop Kind, float Scale, int Variant);
    private readonly List<Site> sites = [];
    private readonly List<float> portals = [];
    public bool Active => sites.Count > 0;
    private string key = "";
    private RaceRoadClearance? roadClearance;
    private Vector4 accent;
    private Vector4 stone;

    public void Build(AetherRaceLive.Track track, string course, RaceRoadClearance clearance)
    {
        sites.Clear();
        portals.Clear();
        key = course;
        roadClearance = clearance;
        Prop[] palette;
        float tunnel;
        (Prop[] Palette, Vector4 Accent, Vector4 Stone, float Tunnel) profile = course switch
        {
            "the-knot" => ([Prop.Basalt, Prop.Lantern, Prop.Fan, Prop.Fissure, Prop.Ruin], new(1f,.50f,.20f,1), new(.35f,.23f,.18f,1), 0),
            "ember-dash" => ([Prop.Basalt, Prop.Fissure, Prop.Basalt, Prop.Pine], new(1f,.38f,.10f,1), new(.22f,.18f,.20f,1), .48f),
            "gale-route" => ([Prop.Flowers, Prop.Orchard, Prop.Fence, Prop.Mill], new(.63f,.85f,.38f,1), new(.43f,.48f,.33f,1), .35f),
            "duskwind-journey" => ([Prop.Willow, Prop.Pond, Prop.Reeds, Prop.Lantern], new(.38f,.76f,.83f,1), new(.22f,.36f,.40f,1), 0),
            "quiet-mile" => ([Prop.Orchard, Prop.Flowers, Prop.Fence, Prop.Pond], new(.93f,.68f,.57f,1), new(.43f,.43f,.31f,1), .78f),
            "levin-run" => ([Prop.Tower, Prop.Crystal, Prop.Ruin, Prop.Tower], new(.71f,.55f,1f,1), new(.30f,.30f,.43f,1), 0),
            "stone-ladder" => ([Prop.Cairn, Prop.Pine, Prop.Cliff, Prop.Ruin], new(.77f,.68f,.43f,1), new(.42f,.38f,.30f,1), .43f),
            "frostline" => ([Prop.Pine, Prop.Snow, Prop.Pine, Prop.Pond], new(.72f,.90f,1f,1), new(.42f,.61f,.72f,1), .45f),
            "long-burn" => ([Prop.Fissure, Prop.Basalt, Prop.Pine, Prop.Cliff], new(.95f,.45f,.23f,1), new(.26f,.23f,.23f,1), .55f),
            "riven-straight" => ([Prop.Fissure, Prop.Crystal, Prop.Cairn, Prop.Ruin], new(.50f,.70f,1f,1), new(.30f,.28f,.41f,1), 0),
            "hollow-way" => ([Prop.Willow, Prop.Mushroom, Prop.Cliff, Prop.Flowers], new(.52f,.78f,.40f,1), new(.32f,.36f,.22f,1), .53f),
            "squall-line" => ([Prop.Cliff, Prop.Reeds, Prop.Windsock, Prop.Pond, Prop.Bird], new(.60f,.84f,.82f,1), new(.37f,.46f,.45f,1), 0),
            "millrace" => ([Prop.Pond, Prop.Reeds, Prop.Hut, Prop.Flowers], new(.42f,.79f,.82f,1), new(.45f,.33f,.22f,1), .52f),
            "glassway" => ([Prop.Crystal, Prop.Cliff, Prop.Snow, Prop.Pond], new(.49f,.82f,1f,1), new(.28f,.52f,.68f,1), .50f),
            "the-bellows" => ([Prop.Hut, Prop.Fan, Prop.Fence, Prop.Lantern], new(.88f,.69f,.40f,1), new(.46f,.36f,.28f,1), .57f),
            "the-ring" => ([Prop.Flowers, Prop.Orchard, Prop.Fence, Prop.Fountain], new(.77f,.82f,.57f,1), new(.48f,.51f,.39f,1), .62f),
            _ => ([], Vector4.One, Vector4.One, 0),
        };
        (palette, accent, stone, tunnel) = profile;
        if (palette.Length == 0) return;
        var length = track.LapRows > 0 ? track.LapLength : track.Length;
        var index = 0;
        for (var s = 12f; s < length - 10f; s += 9f)
        {
            var p = track.AtLerp(s);
            for (var side = -1; side <= 1; side += 2)
            {
                var variant = index * 7 + (side > 0 ? 3 : 0);
                var kind = palette[(index + (side > 0 ? 1 : 0)) % palette.Length];
                var scale = .78f + (variant % 5) * .08f;
                var lateral = side * (p.Width * .5f + 2.8f + (variant % 3) * .3f);
                var at = World(p, lateral);
                if (clearance.Clear(at, 2.6f * scale)) sites.Add(new(at, kind, scale, variant));
            }
            index++;
        }
        var landmark = course switch
        {
            "levin-run" => Prop.Dome, "squall-line" => Prop.Tower, "millrace" => Prop.Mill,
            "the-ring" => Prop.Tower, "duskwind-journey" => Prop.Hut, "stone-ladder" => Prop.Ruin,
            "riven-straight" => Prop.Ruin, _ => palette[^1],
        };
        var landmarkS = course == "millrace" ? .54f : .76f;
        var anchor = track.AtLerp(length * landmarkS);
        for (var side = -1; side <= 1; side += 2)
        {
            var at = World(anchor, side * (anchor.Width * .5f + 4.5f));
            if (!clearance.Clear(at, 3.5f)) continue;
            sites.Add(new(at, landmark, 1.15f, 0));
            break;
        }
        if (tunnel > 0)
            for (var rib = 0; rib < (course == "gale-route" ? 1 : 4); rib++) portals.Add(length * tunnel + rib * 3.2f);
    }

    public void Draw(ImDrawListPtr dl, AetherRaceLive.Track track, in StageCam cam, Vector2 origin, Vector2 size, float time, bool reduced, float gust)
    {
        var phase = reduced ? 0f : time;
        var effectBudget = key == "squall-line" ? 5 : 2;
        foreach (var site in sites)
        {
            var at = cam.ToScreen(site.At);
            var z = cam.Zoom * site.Scale;
            if (!StageCam.OnStage(at, origin, size, z * 7f)) continue;
            DrawProp(dl, at, z, site.Kind, site.Variant, phase, reduced ? 0f : gust);
            if (!reduced && site.Variant % 3 == 0) Motes(dl, at, z, phase, site.Variant);
            if (effectBudget > 0 && DrawElementAccent(dl, site, at, z, in cam, time, reduced, gust)) effectBudget--;
        }
        foreach (var s in portals)
        {
            var p = track.AtLerp(s);
            var a = cam.ToScreen(World(p, p.Width * .5f + .8f));
            var b = cam.ToScreen(World(p, -p.Width * .5f - .8f));
            if (!StageCam.OnStage((a+b)*.5f, origin, size, cam.Zoom * 8f)) continue;
            var rise = new Vector2(0, -cam.Zoom * 2.5f);
            var roof = (a + b) * .5f + rise * 1.5f;
            var material = key is "quiet-mile" or "hollow-way" or "the-ring" ? new Vector4(.28f,.43f,.23f,1) : stone;
            dl.AddLine(a, a + rise, Ink(material), cam.Zoom * .35f);
            dl.AddLine(b, b + rise, Ink(material), cam.Zoom * .35f);
            dl.AddLine(a + rise, roof, Ink(material with { W = .48f }), cam.Zoom * .5f);
            dl.AddLine(roof, b + rise, Ink(material with { W = .48f }), cam.Zoom * .5f);
            dl.AddCircleFilled(a + rise, cam.Zoom * .12f, Ink(accent), 8);
            dl.AddCircleFilled(b + rise, cam.Zoom * .12f, Ink(accent), 8);
            if (key is "quiet-mile" or "hollow-way" or "the-ring")
                for (var leaf = 0; leaf < 5; leaf++)
                    dl.AddCircleFilled(Vector2.Lerp(a+rise, roof, leaf/5f), cam.Zoom*.28f, Ink(material with {W=.65f}), 7);
        }
    }

    private void DrawProp(ImDrawListPtr dl, Vector2 p, float z, Prop kind, int n, float t, float gust)
    {
        var dark = Ink(stone * new Vector4(.55f,.55f,.55f,1));
        var mid = Ink(stone);
        var light = Ink(accent);
        var edge = Ink(Vector4.Lerp(stone, Vector4.One, .22f));
        var sway = MathF.Sin(t * .7f + n) * .12f + gust * .10f;
        var winter = key is "frostline" or "glassway";
        var burnt = key is "ember-dash" or "long-burn";
        Ellipse(dl, p + new Vector2(0,z*.15f), new(z*1.3f,z*.35f), 0x30000000, 16);
        switch (kind)
        {
            case Prop.Pine:
            case Prop.Willow:
            case Prop.Orchard:
                dl.AddLine(p, p + new Vector2(sway*z,-z*3.2f), dark, z*.23f);
                if (burnt)
                {
                    for (var i=0;i<3;i++) dl.AddLine(p-new Vector2(0,z*(i*.7f+.5f)),p+new Vector2((i%2==0?-1:1)*z,-z*(i*.7f+1.2f)),mid,z*.13f);
                    break;
                }
                var green = winter ? new Vector4(.35f,.57f,.58f,1) : key == "duskwind-journey" ? new(.18f,.43f,.43f,1) : new Vector4(.29f,.49f,.25f,1);
                for (var i=0;i<3;i++)
                {
                    var c=p+new Vector2(sway*z,-z*(1.2f+i*.7f));
                    if (kind==Prop.Pine)
                    {
                        dl.AddTriangleFilled(c+new Vector2(-z*(1.3f-i*.22f),z*.4f),c+new Vector2(z*(1.3f-i*.22f),z*.4f),c-new Vector2(0,z*1.5f),Ink(green));
                        if(winter) dl.AddTriangleFilled(c+new Vector2(-z*.6f,-z*.25f),c+new Vector2(z*.6f,-z*.25f),c-new Vector2(0,z*1.5f),Ink(accent));
                    }
                    else dl.AddCircleFilled(c+new Vector2((i-1)*z*.6f,0),z*1.05f,Ink(green+new Vector4(i*.025f,i*.035f,i*.02f,0)),14);
                }
                if(kind==Prop.Willow)
                    for(var i=-2;i<=2;i++) dl.AddBezierCubic(p+new Vector2(i*z*.45f,-z*3),p+new Vector2(i*z*.75f,-z*2.5f),p+new Vector2(i*z*.6f+sway*z,-z),p+new Vector2(i*z*.65f,-z*.6f),Ink(green),z*.10f,8);
                if(kind==Prop.Orchard)
                    for(var i=0;i<5;i++) dl.AddCircleFilled(p+new Vector2(MathF.Sin(i*2.4f)*z,-z*(1.9f+.7f*MathF.Cos(i*2.4f))),z*.10f,light,7);
                break;
            case Prop.Basalt:
            case Prop.Crystal:
            case Prop.Cliff:
            case Prop.Cairn:
                for(var i=0;i<3;i++)
                {
                    var c=p+new Vector2((i-1)*z*.9f,0);
                    var h=z*(1.2f+((n+i)%3)*.65f);
                    dl.AddQuadFilled(c+new Vector2(-z*.55f,0),c+new Vector2(-z*.65f,-h),c+new Vector2(z*.3f,-h-z*.5f),c+new Vector2(z*.6f,0),mid);
                    dl.AddTriangleFilled(c+new Vector2(z*.3f,-h-z*.5f),c+new Vector2(z*.6f,0),c,kind==Prop.Crystal?Ink(accent with{W=.6f}):dark);
                    dl.AddLine(c+new Vector2(-z*.6f,-h),c+new Vector2(z*.3f,-h-z*.5f),light,z*.07f);
                    if(kind==Prop.Crystal) dl.AddLine(c+new Vector2(z*.3f,-h-z*.35f),c+new Vector2(z*.15f,-z*.25f),Ink(accent with{W=.4f}),z*.055f);
                    if(kind==Prop.Cliff) for(var r=1;r<4;r++) dl.AddLine(c+new Vector2(-z*.5f,-h*r/4),c+new Vector2(z*.4f,-h*r/4),dark,z*.07f);
                }
                break;
            case Prop.Flowers:
            case Prop.Reeds:
            case Prop.Mushroom:
                for(var i=0;i<7;i++)
                {
                    var c=p+new Vector2(MathF.Sin(i*2.4f)*z*1.3f,MathF.Cos(i*2.4f)*z*.35f);
                    var tip=c+new Vector2(sway*z,-z*(.35f+(i%3)*.2f));
                    dl.AddLine(c,tip,Ink(new(.35f,.48f,.25f,1)),z*.06f);
                    if(kind==Prop.Reeds) dl.AddLine(tip,tip-new Vector2(0,z*.3f),mid,z*.12f);
                    else if(kind==Prop.Mushroom) Ellipse(dl, tip,new(z*.25f,z*.12f),light,10);
                    else { dl.AddCircleFilled(tip,z*.13f,i%2==0?light:0xFFDBBAEE,8); dl.AddCircleFilled(tip,z*.045f,0xFFEAF3FF,6); }
                }
                break;
            case Prop.Pond:
            case Prop.Snow:
                Ellipse(dl, p,new(z*1.8f,z*.8f),kind==Prop.Snow?Ink(accent with{W=.28f}):Ink(new(.16f,.38f,.47f,.8f)),24);
                for(var i=0;i<3;i++) dl.AddLine(p+new Vector2(-z+z*.2f*i,z*(i-1)*.25f),p+new Vector2(z*.8f+MathF.Sin(t+i)*z*.12f,z*(i-1)*.25f),Ink(accent with{W=.4f}),z*.05f);
                if(key=="duskwind-journey") for(var i=0;i<3;i++) dl.AddCircleFilled(p+new Vector2((i-1)*z*.8f,z*.2f),z*.24f,0xFF547A50,12);
                break;
            case Prop.Fence:
                for(var i=-1;i<=1;i++) dl.AddLine(p+new Vector2(i*z,0),p+new Vector2(i*z,-z),mid,z*.13f);
                for(var i=1;i<=2;i++) dl.AddLine(p+new Vector2(-z*1.2f,-z*i*.35f),p+new Vector2(z*1.2f,-z*i*.35f),light,z*.08f);
                break;
            case Prop.Hut:
            case Prop.Dome:
            case Prop.Ruin:
                dl.AddRectFilled(p+new Vector2(-z*1.3f,-z*2),p+new Vector2(z*1.3f,0),mid,z*.08f);
                if(kind==Prop.Dome) dl.AddCircleFilled(p-new Vector2(0,z*2),z*1.3f,dark,24);
                else if(kind==Prop.Hut) dl.AddTriangleFilled(p+new Vector2(-z*1.6f,-z*2),p+new Vector2(z*1.6f,-z*2),p-new Vector2(0,z*3.2f),dark);
                else for(var i=-1;i<=1;i++) dl.AddRectFilled(p+new Vector2(i*z-z*.2f,-z*2.8f),p+new Vector2(i*z+z*.2f,-z*1.9f),mid);
                                if(kind==Prop.Hut)
                {
                    for(var row=0;row<4;row++) dl.AddLine(p+new Vector2(-z*1.2f,-z*(.25f+row*.4f)),p+new Vector2(z*1.2f,-z*(.25f+row*.4f)),dark,z*.035f);
                    for(var beam=-1;beam<=1;beam++) dl.AddLine(p+new Vector2(beam*z*1.1f,0),p+new Vector2(beam*z*1.1f,-z*1.9f),edge,z*.07f);
                    dl.AddLine(p+new Vector2(-z*1.55f,-z*2),p-new Vector2(0,z*3.2f),edge,z*.08f);
                    dl.AddRectFilled(p+new Vector2(z*.7f,-z*3.2f),p+new Vector2(z,-z*2.3f),mid);
                    for(var flower=0;flower<5;flower++) dl.AddCircleFilled(p+new Vector2(-z*.9f+flower*z*.12f,-z*.75f),z*.07f,0xFFD29CDD,7);
                }
                if(kind==Prop.Dome)
                {
                    dl.AddLine(p-new Vector2(0,z*3.3f),p-new Vector2(0,z*2),edge,z*.08f);
                    dl.AddLine(p+new Vector2(z*.4f,-z*2.6f),p+new Vector2(z*1.6f,-z*3.3f),mid,z*.3f);
                    dl.AddCircleFilled(p+new Vector2(z*1.6f,-z*3.3f),z*.16f,light,12);
                }
                for(var i=-1;i<=1;i+=2) dl.AddRectFilled(p+new Vector2(i*z*.7f-z*.15f,-z*1.5f),p+new Vector2(i*z*.7f+z*.15f,-z*.9f),light,z*.04f);
                dl.AddRectFilled(p+new Vector2(-z*.25f,-z*.8f),p+new Vector2(z*.25f,0),dark);
                break;
            case Prop.Mill:
            case Prop.Fan:
                var water=key=="millrace";
                var hub=p-new Vector2(0,z*(water?1.4f:2.2f));
                dl.AddQuadFilled(p+new Vector2(-z*.7f,0),hub-new Vector2(z*.35f,0),hub+new Vector2(z*.35f,0),p+new Vector2(z*.7f,0),mid);
                                var blade=water?Ink(new Vector4(.67f,.49f,.30f,1)):light;
                if(water || kind==Prop.Fan)
                {
                    dl.AddCircle(hub,z*1.25f,blade,24,z*.14f);
                    dl.AddCircle(hub,z*1.10f,dark,24,z*.06f);
                }
                for(var i=0;i<(water?8:4);i++)
                {
                    var angle=t*(water?.3f:.45f)+i*MathF.Tau/(water?8:4);
                    var tip=hub+new Vector2(MathF.Cos(angle),MathF.Sin(angle))*z*1.5f;
                    dl.AddLine(hub,tip,blade,z*(water?.10f:.20f));
                    var v=new Vector2(-MathF.Sin(angle),MathF.Cos(angle))*z*.22f;
                    dl.AddLine(tip-v,tip+v,blade,z*.12f);
                }
                dl.AddCircleFilled(hub,z*.18f,dark,10);
                break;
            case Prop.Tower:
                var coastal=key=="squall-line";
                dl.AddQuadFilled(p+new Vector2(-z*.55f,0),p+new Vector2(-z*.35f,-z*3.8f),p+new Vector2(z*.35f,-z*3.8f),p+new Vector2(z*.55f,0),coastal?0xFFD1DBDB:mid);
                for(var i=1;i<4;i++) dl.AddLine(p+new Vector2(-z*.5f,-z*i),p+new Vector2(z*.5f,-z*i),light,z*.15f);
                dl.AddCircleFilled(p-new Vector2(0,z*3.9f),z*.4f,Ink(accent with{W=.18f}),16);
                dl.AddCircleFilled(p-new Vector2(0,z*3.9f),z*.16f,light,12);
                                if(coastal)
                {
                    dl.AddRectFilled(p+new Vector2(-z*.5f,-z*4.1f),p+new Vector2(z*.5f,-z*3.7f),dark);
                    dl.AddTriangleFilled(p+new Vector2(-z*.65f,-z*4.1f),p+new Vector2(z*.65f,-z*4.1f),p-new Vector2(0,z*4.6f),mid);
                    dl.AddRectFilled(p+new Vector2(-z*.2f,-z*4.05f),p+new Vector2(z*.2f,-z*3.75f),light);
                }
                if(key=="the-ring") {dl.AddCircleFilled(p-new Vector2(0,z*3.2f),z*.35f,0xFFE5E9E7,16); dl.AddLine(p-new Vector2(0,z*3.2f),p+new Vector2(z*.2f,-z*3.35f),dark,z*.05f);}
                break;
            case Prop.Fountain:
                Ellipse(dl, p,new(z*1.3f,z*.6f),mid,20);
                Ellipse(dl, p,new(z*1.05f,z*.4f),0xFFAD9071,20);
                for(var i=-1;i<=1;i++) dl.AddBezierCubic(p,p+new Vector2(0,-z*2.4f),p+new Vector2(i*z,-z*2.1f),p+new Vector2(i*z,z*.1f),Ink(new(.65f,.83f,.90f,.65f)),z*.06f,12);
                break;
            case Prop.Fissure:
                for(var i=0;i<4;i++)
                {
                    var a=p+new Vector2((i-2)*z*.7f,MathF.Sin(i*2+n)*z*.35f);
                    var b=p+new Vector2((i-1)*z*.7f,MathF.Sin((i+1)*2+n)*z*.35f);
                    dl.AddLine(a,b,Ink(accent with{W=.1f}),z*.5f);
                    dl.AddLine(a,b,Ink(accent with{W=.75f}),z*.07f);
                }
                break;
            case Prop.Bird:
                var wing=MathF.Sin(t*1.8f+n)*z*.18f;
                dl.AddBezierCubic(p+new Vector2(-z*.7f,-z*.2f+wing),p+new Vector2(-z*.3f,-z*.5f),p-new Vector2(z*.1f,0),p,0xFFC7D0D0,z*.07f,8);
                dl.AddBezierCubic(p,p+new Vector2(z*.1f,0),p+new Vector2(z*.3f,-z*.5f),p+new Vector2(z*.7f,-z*.2f+wing),0xFFC7D0D0,z*.07f,8);
                break;
            case Prop.Windsock:
                var mast = p - new Vector2(0, z * 2f);
                dl.AddLine(p, mast, edge, z * .10f);
                dl.AddCircleFilled(mast, z * .09f, light, 8);
                for (var band = 0; band < 4; band++)
                {
                    var x = band * .30f;
                    var flap = MathF.Sin(t * 2f + n + band * .5f) * .10f + gust * .10f;
                    var a = mast + new Vector2(z * x, z * (flap + x * .15f));
                    var b = mast + new Vector2(z * (x + .30f), z * (flap + (x + .30f) * .15f));
                    var half = z * (.19f - band * .03f);
                    dl.AddQuadFilled(a - new Vector2(0, half), b - new Vector2(0, half * .8f), b + new Vector2(0, half * .8f), a + new Vector2(0, half), band % 2 == 0 ? light : 0xFFE2E7E7);
                }
                break;
            case Prop.Lantern:
                dl.AddLine(p,p-new Vector2(0,z*1.7f),mid,z*.10f);
                var lamp=p-new Vector2(0,z*1.7f);
                dl.AddCircleFilled(lamp,z*.5f,Ink(accent with{W=.08f}),16);
                dl.AddRectFilled(lamp-new Vector2(z*.15f,z*.2f),lamp+new Vector2(z*.15f,z*.2f),light,z*.07f);
                break;
        }
    }

    private bool DrawElementAccent(ImDrawListPtr dl, Site site, Vector2 p, float z, in StageCam cam, float time, bool reduced, float gust)
    {
        if (key == "squall-line")
        {
            if (reduced || site.Kind is not (Prop.Reeds or Prop.Windsock or Prop.Pond)) return false;
            var cycle = (time * .33f + site.Variant * .13f) % 1f;
            var strength = MathF.Sin(cycle * MathF.PI) * (.22f + Math.Clamp(gust, 0f, 1f) * .12f);
            for (var trail = 0; trail < 3; trail++)
            {
                var start = p + new Vector2(-z * 1.7f, -z * (.4f + trail * .35f));
                for (var segment = 0; segment < 7; segment++)
                {
                    var u = segment / 7f;
                    var v = (segment + 1) / 7f;
                    var a = start + new Vector2(u * z * 3.1f, MathF.Sin(u * 4f + time + trail) * z * .12f);
                    var b = start + new Vector2(v * z * 3.1f, MathF.Sin(v * 4f + time + trail) * z * .12f);
                    var alpha = strength * MathF.Max(0f, 1f - MathF.Abs(u - cycle) * 2f);
                    AccentLine(dl, a, b, in cam, accent with { W = alpha }, z * .035f);
                }
            }
            return true;
        }
        if (key is not ("levin-run" or "riven-straight")) return false;
        var coil = key == "levin-run";
        if (coil ? site.Kind is not (Prop.Tower or Prop.Dome) : site.Kind is not (Prop.Fissure or Prop.Crystal)) return false;
        var tip = p - new Vector2(0, z * (site.Kind == Prop.Tower ? 3.9f : site.Kind == Prop.Dome ? 2.8f : .2f));
        if (reduced)
        {
            AccentLine(dl, tip - new Vector2(z * .22f, 0), tip + new Vector2(z * .22f, 0), in cam, accent with { W = .32f }, z * .09f);
            return true;
        }
        var age = (time + site.Variant * .71f) % (coil ? 4.8f : 6.6f);
        var envelope = Math.Clamp(1f - age / (coil ? 1.15f : .85f), 0f, 1f);
        if (envelope <= 0f) return false;
        var startBolt = tip + new Vector2(coil ? z * .95f : -z * .35f, -z * (coil ? 1.2f : 3.4f));
        for (var step = 0; step < 6; step++)
        {
            var u = step / 6f;
            var v = (step + 1) / 6f;
            var a = Vector2.Lerp(startBolt, tip, u) + new Vector2(step == 0 ? 0f : MathF.Sin(step * 7f + site.Variant) * z * .22f, 0);
            var b = Vector2.Lerp(startBolt, tip, v) + new Vector2(step == 5 ? 0f : MathF.Sin((step + 1) * 7f + site.Variant) * z * .22f, 0);
            AccentLine(dl, a, b, in cam, accent with { W = envelope * .10f }, z * .23f);
            AccentLine(dl, a, b, in cam, Vector4.Lerp(accent, Vector4.One, .5f) with { W = envelope * .75f }, z * .045f);
        }
        for (var spark = 0; spark < 3; spark++)
        {
            var direction = new Vector2(MathF.Cos(spark * 2.4f + site.Variant), MathF.Sin(spark * 2.4f + site.Variant));
            var at = tip + direction * z * (.15f + age * .55f);
            AccentLine(dl, at, at + direction * z * .10f, in cam, accent with { W = envelope * .55f }, z * .04f);
        }
        return true;
    }

    private void AccentLine(ImDrawListPtr dl, Vector2 a, Vector2 b, in StageCam cam, Vector4 color, float width)
    {
        if (color.W < .015f || roadClearance == null || cam.Zoom <= 0f) return;
        var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / (cam.Zoom * .25f)));
        for (var i = 0; i <= steps; i++)
            if (!roadClearance.Clear(cam.ToWorld(Vector2.Lerp(a, b, i / (float)steps)), width / cam.Zoom + .18f)) return;
        dl.AddLine(a, b, Ink(color), MathF.Max(.7f, width));
    }

    private void Motes(ImDrawListPtr dl, Vector2 p, float z, float time, int n)
    {
        for(var i=0;i<2;i++)
        {
            var phase=(time*.14f+i*.5f+n*.17f)%1f;
            var at=p+new Vector2(MathF.Sin(n+i+time*.5f)*z,-z*(phase*2.2f+.3f));
            var alpha=MathF.Sin(phase*MathF.PI)*.38f;
            var radius=z*(key is "squall-line" or "gale-route" ? .065f : .045f);
            dl.AddCircleFilled(at,radius*3,Ink(accent with{W=alpha*.13f}),8);
            dl.AddCircleFilled(at,MathF.Max(.6f,radius),Ink(accent with{W=alpha}),6);
        }
    }

    private static void Ellipse(ImDrawListPtr dl, Vector2 centre, Vector2 radius, uint color, int segments)
    {
        for (var i = 0; i < segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            dl.PathLineTo(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
        dl.PathFillConvex(color);
    }

    private static Vector2 World(AetherRaceLive.TrackSample p,float lateral) => new(p.X-MathF.Sin(p.Heading)*lateral,p.Y+MathF.Cos(p.Heading)*lateral);
    private static uint Ink(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);
}
