using System;
using System.Numerics;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Racer.Screens;

internal static class ResultCelebration
{
    private static readonly uint Ink = ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue with { W = 1 });
    private static readonly uint Paper = ImGui.ColorConvertFloat4ToU32(RacerChrome.Paper with { W = 1 });
    private static readonly uint Gold = ImGui.ColorConvertFloat4ToU32(RacerChrome.CupGold);

    public static void Backdrop(OsAppContext ctx, string assetRoot, Vector2 at, Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(at, at + size, 0xFFEBF8FF);
        var path = System.IO.Path.Combine(assetRoot, "racer", "celebration-bg.png");
        if (ctx.Capabilities.Textures.Get(path) is { } art)
        {
            dl.AddImage(art, at, at + size);
        }
    }

    public static void Header(OsAppContext ctx, Vector2 origin, float width, string titleKey)
    {
        var dl = ImGui.GetWindowDrawList();
        var center = origin + new Vector2(width / 2, Px(120));
        dl.AddCircleFilled(center + new Vector2(0, Px(2)), Px(29), 0x440F3656, 48);
        dl.AddCircleFilled(center, Px(29), Paper, 48);
        dl.AddCircle(center, Px(25), Gold, 48, Px(2));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Trophy, Px(26), center, Ink);
        var a = origin + new Vector2(Px(12), Px(140));
        var b = origin + new Vector2(width - Px(12), Px(185));
        dl.AddQuadFilled(a + new Vector2(-Px(11), Px(7)), a + new Vector2(Px(16), Px(11)), new Vector2(a.X + Px(16), b.Y + Px(12)), new Vector2(a.X - Px(11), b.Y + Px(7)), Ink);
        dl.AddQuadFilled(new Vector2(b.X - Px(16), a.Y + Px(11)), new Vector2(b.X + Px(11), a.Y + Px(7)), b + new Vector2(Px(11), Px(7)), b + new Vector2(-Px(16), Px(12)), Ink);
        dl.AddRectFilled(a + new Vector2(0, Px(4)), b + new Vector2(0, Px(4)), 0x400B335F, Px(6));
        dl.AddRectFilled(a, b, Paper, Px(6));
        dl.AddRect(a, b, Ink, Px(6), ImDrawFlags.None, Px(1.5f));
        dl.AddLine(a + new Vector2(Px(6), Px(4)), new Vector2(b.X - Px(6), a.Y + Px(4)), Gold);
        dl.AddLine(new Vector2(a.X + Px(6), b.Y - Px(4)), b - new Vector2(Px(6), Px(4)), Gold);
        GrandstandFrame.Label(ctx, ctx.Localize(titleKey), a + new Vector2(Px(32), 0), b - a - new Vector2(Px(64), 0), Ink, RacerTextSize.Button);
        Flag(dl, a + new Vector2(Px(7), Px(15)));
        Flag(dl, new Vector2(b.X - Px(23), a.Y + Px(15)));
    }

    public static void Laurels(ImDrawListPtr dl, Vector2 center, float halfWidth)
    {
        for (var side = -1; side <= 1; side += 2)
        for (var i = 0; i < 5; i++)
        {
            var t = i / 4f;
            var p = center + new Vector2(side * (halfWidth + Px(6) + MathF.Sin(t * 2f) * Px(9)), Px(13) - t * Px(31));
            dl.AddQuadFilled(p, p + new Vector2(side * Px(6), -Px(8)), p + new Vector2(side * Px(8), -Px(1)), p + new Vector2(side * Px(3), Px(3)), 0xFF398BA9);
        }
    }

    private static void Flag(ImDrawListPtr dl, Vector2 at)
    {
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 4; x++)
        {
            if ((x + y) % 2 == 0)
            {
                var p = at + new Vector2(Px(x * 4), Px(y * 4));
                dl.AddRectFilled(p, p + new Vector2(Px(4)), Ink);
            }
        }
    }

    public static void Stage(ImDrawListPtr dl, Vector2 at, float width)
    {
        var center = at + new Vector2(width / 2, Px(82));
        RacerChrome.Halo(dl, center, MathF.Min(width * .46f, Px(120)), RacerChrome.CupGold, .5f, 12);
        for (var i = 0; i < 11; i++)
        {
            var angle = MathF.PI + i * MathF.PI / 10;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            dl.AddLine(center + direction * Px(65), center + direction * Px(i % 2 == 0 ? 90 : 81), Gold, Px(1.5f));
        }
        RacerChrome.GroundGlow(dl, at + new Vector2(width / 2, Px(185)), width * .55f, Px(25), RacerChrome.CardBlue, .18f);
        for (var i = 0; i < 16; i++)
        {
            var x = i % 2 == 0 ? width * .04f + Px(i % 3 * 5) : width * .94f - Px(i % 3 * 5);
            var p = at + new Vector2(x, Px(18 + i * 9));
            var tint = i % 3 == 0 ? Gold : i % 3 == 1 ? 0x997F93D1u : 0x998DAB66u;
            dl.AddQuadFilled(p, p + new Vector2(Px(3), -Px(2)), p + new Vector2(Px(5), Px(3)), p + new Vector2(Px(2), Px(5)), tint);
        }
    }

    public static void Step(ImDrawListPtr dl, Vector2 centerTop, float width, float height, int rank)
    {
        var a = centerTop - new Vector2(width / 2, 0);
        var b = a + new Vector2(width, height);
        var face = rank == 0 ? 0xFF92DDF9u : rank == 1 ? 0xFFE3E3D9u : 0xFFB4CEE0u;
        var foot = rank == 0 ? 0xFF4DACC8u : rank == 1 ? 0xFFB9BDA6u : 0xFF7D9ABD;
        dl.AddRectFilled(a + new Vector2(Px(2), Px(4)), b + new Vector2(Px(2), Px(4)), 0x220D2342, Px(5));
        dl.AddRectFilled(a, b, face, Px(5));
        dl.AddRectFilledMultiColor(a + new Vector2(Px(2)), b - new Vector2(Px(2)), 0x66FFFFFF, 0x22FFFFFF, 0x22000000, 0x08000000);
        dl.AddRectFilled(new Vector2(a.X, b.Y - Px(7)), b, foot, Px(4));
        dl.AddRect(a, b, Ink, Px(5), ImDrawFlags.None, Px(1.3f));
        dl.AddRect(a + new Vector2(Px(4), Px(7)), b - new Vector2(Px(4), Px(10)), 0x77FFFFFF, Px(2));
        dl.AddLine(a + new Vector2(Px(5), Px(4)), new Vector2(b.X - Px(5), a.Y + Px(4)), 0xCCFFFFFF, Px(2));
        var label = (rank + 1).ToString();
        using var font = RacerFonts.Get(RacerTextSize.Button)?.Push();
        var textSize = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(MathF.Round(centerTop.X - textSize.X / 2), MathF.Round(a.Y + (height - textSize.Y) / 2 - Px(1))), Ink, label);
    }

    /// <summary>The height a <see cref="NamePlate"/> takes, padding included.</summary>
    public static float NamePlateHeight
    {
        get
        {
            using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
            return ImGui.GetTextLineHeight() + Px(NamePlatePadY * 2f);
        }
    }

    private const float NamePlatePadX = 8f;
    private const float NamePlatePadY = 4f;
    private const float NamePlateRounding = 8f;
    private const float NamePlateRim = 1.5f;

    /// <summary>A runner's name on a navy plate centred under a podium step, cut with an ellipsis to
    /// <paramref name="maxWidth"/>. The player's own Lumi gets a gold rim.</summary>
    public static void NamePlate(ImDrawListPtr dl, Vector2 centerTop, float maxWidth, string name, bool mine)
    {
        using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
        var padX = Px(NamePlatePadX);
        var shown = Cards.CardText.Ellipsize(name, MathF.Max(1f, maxWidth - (padX * 2f)), value => ImGui.CalcTextSize(value).X);
        var textSize = ImGui.CalcTextSize(shown);
        var tl = new Vector2(MathF.Round(centerTop.X - (textSize.X * 0.5f) - padX), MathF.Round(centerTop.Y));
        var br = tl + new Vector2(textSize.X + (padX * 2f), NamePlateHeight);
        dl.AddRectFilled(tl, br, Ink, Px(NamePlateRounding));
        if (mine)
        {
            dl.AddRect(tl, br, Gold, Px(NamePlateRounding), ImDrawFlags.None, Px(NamePlateRim));
        }

        dl.AddText(new Vector2(tl.X + padX, tl.Y + Px(NamePlatePadY)), GrandstandFrame.Cream, shown);
    }
}
