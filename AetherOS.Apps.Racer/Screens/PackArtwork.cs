using System;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens;

internal static class PackArtwork
{
    public static (Vector2 At, Vector2 Size) Stage(Vector2 origin, Vector2 room)
    {
        var height = MathF.Max(Px(60), room.Y - Px(112));
        var size = new Vector2(MathF.Min(room.X - Px(44), height * .74f), height);
        size.Y = size.X / .74f;
        return (origin + new Vector2((room.X - size.X) / 2, Px(14)), size);
    }

    public static string For(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        return (bytes[0] % 3) switch
        {
            0 => "pack-ruby",
            1 => "pack-sapphire",
            _ => "pack-gold",
        };
    }

    public static void Draw(OsAppContext ctx, IRacerHost host, Guid id, Vector2 at, Vector2 size)
    {
        GrandstandFrame.Art(ctx, host, For(id), at, size);
        var dl = ImGui.GetWindowDrawList();
        var t = ctx.ReduceMotion ? .4f : (float)(ImGui.GetTime() * .24 % 1.8);
        if (t > 1)
        {
            return;
        }
        dl.PushClipRect(at + size * .06f, at + size * .94f, true);
        var y = at.Y + size.Y * t;
        var a = new Vector2(at.X, y);
        var b = new Vector2(at.X + size.X, y - size.Y * .22f);
        dl.AddQuadFilled(a, b, b + new Vector2(0, Px(5)), a + new Vector2(0, Px(5)), 0x33FFFFFF);
        dl.AddLine(a + new Vector2(0, Px(7)), b + new Vector2(0, Px(7)), 0x55FFF6D1, Px(1));
        dl.PopClipRect();
    }
}
