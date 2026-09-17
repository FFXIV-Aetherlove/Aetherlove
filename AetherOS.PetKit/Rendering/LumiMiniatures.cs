namespace AetherOS.PetKit.Rendering;

using System;
using System.Numerics;
using AetherOS.PetKit.Rendering.LineArt;
using Dalamud.Bindings.ImGui;

public sealed class LumiMiniatures
{
    private readonly LineCanvas canvas = new();
    private static readonly LineShell.Channels[] Rest = BuildRest();

    public static ReadOnlySpan<int> FormsFor(string? element) => element switch
    {
        "water" => [1, 3, 2],
        "earth" => [5, 4],
        "wind" => [6, 8],
        "fire" => [7, 14],
        "lightning" => [9, 13],
        "ice" => [12, 11],
        _ => [10],
    };

    public void Draw(ImDrawListPtr dl, string? element, Vector4 mapColor, Vector2 feet,
        float size, int variant, float time, bool reducedMotion)
    {
        if (size <= 0f) return;
        if (dl.VtxBuffer.Size > 48000) return;
        var forms = FormsFor(element);
        var shell = forms[(int)((uint)variant % (uint)forms.Length)];
        var body = Vector4.Lerp(mapColor, Vector4.One, .12f) with { W = 1f };
        var accent = Vector4.Lerp(body, Vector4.One, .32f);
        var beat = reducedMotion ? 0f : MathF.Max(0f, MathF.Sin(time * 2.6f + variant * 1.7f));
        feet.Y -= beat * size * .12f;
        if (shell == 10)
        {
            DrawRegular(dl, feet, size, body);
            return;
        }
        LineArtDispatch.Draw(shell, canvas, dl, feet, size, Rest[shell], Rest[shell], LineShell.Open, 0f,
            body, accent, new(.08f,.12f,.16f,1f), Vector4.Lerp(body, new(.08f,.10f,.14f,1f), .65f),
            new(1f + beat * .025f, 1f - beat * .025f), false);
    }

    public void DrawCrowdForm(ImDrawListPtr dl, int shell, Vector4 color, Vector2 feet,
        float size, int variant, float time, bool reducedMotion)
    {
        if (size <= 0 || dl.VtxBuffer.Size > 46000) return;
        var hop = reducedMotion ? 0 : MathF.Max(0, MathF.Sin(time * 3f + variant * 1.7f));
        feet.Y -= hop * size * .18f;
        var r = size * .28f;
        var c = feet - new Vector2(0, r * 1.25f);
        var ink = ImGui.ColorConvertFloat4ToU32(color);
        var light = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(color, Vector4.One, .42f));
        Vector2 P(float x, float y) => c + new Vector2(x, y) * r;
        void Disc(float x, float y, float radius, uint col) => dl.AddCircleFilled(P(x,y), r * radius, col, 10);
        void Line(float x, float y, float x2, float y2, float width, uint col) => dl.AddLine(P(x,y),P(x2,y2),col,r*width);
        switch (shell)
        {
            case 1:
                Disc(0,-.25f,1,ink);
                for (var i=-1;i<=1;i++) Line(i*.6f,.2f,i*.7f+hop*.15f,1.1f,.22f,light);
                break;
            case 2:
                Disc(0,0,.8f,ink);
                for(var side=-1;side<=1;side+=2) { Line(side*.5f,0,side*1.2f,-.7f,.22f,ink); Disc(side*1.25f,-.8f,.38f,light); }
                break;
            case 3:
                for(var i=0;i<8;i++) { var a=i*MathF.Tau/8; var v=new Vector2(MathF.Cos(a),MathF.Sin(a)); dl.AddTriangleFilled(c+v*r*1.35f,c+(v+new Vector2(-v.Y,v.X)*.2f)*r*.8f,c+(v-new Vector2(-v.Y,v.X)*.2f)*r*.8f,light); }
                Disc(0,0,1,ink); break;
            case 4:
                Disc(-.2f,0,1,ink); dl.AddCircle(P(-.2f,0),r*.6f,light,12,r*.13f); Disc(-.2f,0,.23f,light); Disc(.65f,.35f,.5f,ink); break;
            case 5:
                Line(-.8f,.7f,.5f,.7f,.5f,ink); Line(.5f,.7f,-.25f,.15f,.5f,ink); Line(-.25f,.15f,.25f,-.6f,.5f,ink); Disc(.25f,-.65f,.65f,ink); break;
            case 6:
                for(var side=-1;side<=1;side+=2) { Disc(side*.85f,-.4f,.72f,light); Disc(side*.65f,.45f,.52f,ink); Line(side*.2f,-.65f,side*.5f,-1.35f,.1f,light); }
                Disc(0,0,.5f,ink); break;
            case 7:
                dl.AddRectFilled(P(-.7f,-.85f),P(.7f,.65f),ink,r*.3f); Line(-.6f,-1,.6f,-1,.23f,light); Line(0,-1,0,-1.5f,.12f,light); Line(-.6f,.8f,.6f,.8f,.2f,light); break;
            case 8:
                dl.AddTriangleFilled(P(-1,-.4f),P(1,-.4f),P(0,1.1f),ink); Disc(0,-.4f,.8f,light); Line(0,-.5f,0,-1.3f,.2f,ink); break;
            case 9:
                Line(-.65f,1,-.65f,-1.25f,.15f,light); dl.AddTriangleFilled(P(-.6f,-1.2f),P(1.2f,-.55f),P(-.6f,.3f),ink); break;
            case 10: DrawRegular(dl,feet,size,color); return;
            case 11:
                Disc(0,.4f,.85f,ink); Disc(0,-.65f,.65f,light); Line(-.65f,0,.65f,0,.2f,light); break;
            case 12:
                dl.AddTriangleFilled(P(-.7f,-1.15f),P(.7f,-1.15f),P(0,1.2f),ink); Line(-.3f,-.8f,0,.6f,.12f,light); break;
            case 13:
                Disc(-.6f,0,.65f,ink); Disc(0,-.4f,.85f,ink); Disc(.65f,0,.65f,ink); Line(.2f,.4f,-.2f,.85f,.2f,light); Line(-.2f,.85f,.25f,1.1f,.2f,light); break;
            default:
                dl.AddTriangleFilled(P(-.9f,.6f),P(-.65f,-.9f),P(.65f,-.9f),ink); dl.AddTriangleFilled(P(-.9f,.6f),P(.65f,-.9f),P(.9f,.6f),ink); Line(-.5f,.45f,0,.15f,.12f,light); Line(0,.15f,.4f,.5f,.12f,light); break;
        }
        for(var side=-1;side<=1;side+=2)
        {
            Disc(side*.28f,-.2f,.14f,0xFF38291F);
            Line(side*.65f,.3f,side*1.15f,-.15f-hop*.55f,.14f,light);
        }
        dl.AddLine(P(-.18f,.2f),P(0,.3f),0xFF38291F,r*.07f);
        dl.AddLine(P(0,.3f),P(.18f,.2f),0xFF38291F,r*.07f);
    }

    private static void DrawRegular(ImDrawListPtr dl, Vector2 feet, float size, Vector4 color)
    {
        var r = size * .32f;
        var body = feet - new Vector2(0, r * 1.05f);
        var ink = ImGui.ColorConvertFloat4ToU32(color);
        dl.AddCircleFilled(body, r, ink, 12);
        dl.AddTriangleFilled(body + new Vector2(-r * .85f, 0), body - new Vector2(0, r * 1.45f), body + new Vector2(r * .85f, 0), ink);
        dl.AddTriangleFilled(body + new Vector2(-r * .15f, -r * 1.1f), body + new Vector2(r * .12f, -r * 2f), body + new Vector2(r * .27f, -r * 1.05f), ink);
        for (var side = -1; side <= 1; side += 2)
        {
            dl.AddCircleFilled(body + new Vector2(side * r * 1.06f, r * .3f), r * .18f, ink, 6);
            dl.AddCircleFilled(body + new Vector2(side * r * .42f, r * .85f), r * .22f, 0xFFB9CEE3, 6);
            var eye = body + new Vector2(side * r * .38f, -r * .05f);
            dl.AddCircleFilled(eye, r * .24f, 0xFF38291F, 8);
            dl.AddCircleFilled(eye - new Vector2(r * .06f, r * .08f), r * .08f, 0xFFF8FBFF, 6);
        }
    }

    private static LineShell.Channels[] BuildRest()
    {
        var rest = new LineShell.Channels[15];
        for (var shell = 1; shell < rest.Length; shell++) rest[shell] = LineArtDispatch.PoseAt(shell, 0, 0, 0, 0, 0f);
        return rest;
    }
}
