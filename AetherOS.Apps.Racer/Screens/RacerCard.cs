using System;
using System.IO;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The printed stamp card and the five places a stamp lands on it. The slot map was measured off
/// the art, so moving a slot means re-measuring rather than nudging a number.</summary>
internal static class RacerCard
{
    /// <summary>Where each slot's centre sits, as a fraction of the card art. Three across, then two.</summary>
    private static readonly Vector2[] Slots =
    [
        new(0.2135f, 0.5303f),
        new(0.5019f, 0.5303f),
        new(0.7904f, 0.5303f),
        new(0.3538f, 0.7496f),
        new(0.6423f, 0.7496f),
    ];

    /// <summary>A slot's radius, as a fraction of the card's width.</summary>
    private const float SlotRadius = 0.1335f;

    /// <summary>The stamp fills a little less than its ring, so the printed ring still reads underneath.</summary>
    private const float StampFill = 0.92f;

    /// <summary>The card art's width over its height.</summary>
    public const float Aspect = 0.7403f;

    /// <summary>The ink the stamp is printed in. It is a rubber stamp on paper, so it is red on every
    /// card whatever element the shard was rolled from.</summary>
    public static readonly Vector4 Ink = new(0.706f, 0.165f, 0.173f, 1f);

    public static string Path(IRacerHost host) =>
        System.IO.Path.Combine(host.PetAssetRoot, "racer", "stamp-card.png");

    /// <summary>The card at <paramref name="topLeft"/>, with <paramref name="stamped"/> slots filled.
    /// Returns the centre of the last stamp drawn, which is what a caller animates or hangs a hit-test
    /// on. <paramref name="animating"/> names the slot that is still landing.</summary>
    public static Vector2 Draw(ImDrawListPtr dl, OsAppContext ctx, IRacerHost host, Vector2 topLeft,
        Vector2 size, int stamped, float alpha = 1f, int animating = -1, float animT = 1f)
    {
        var tint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha));
        if (ctx.Capabilities.Textures.Get(Path(host)) is { } tex)
        {
            dl.AddImage(tex, topLeft, topLeft + size, Vector2.Zero, Vector2.One, tint);
        }
        else
        {
            dl.AddRectFilled(topLeft, topLeft + size,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.976f, 0.921f, 0.847f, alpha)), size.X * 0.05f);
        }

        return DrawStamps(dl, ctx, host, topLeft, topLeft + new Vector2(size.X, 0f),
            topLeft + size, topLeft + new Vector2(0f, size.Y), stamped, alpha, animating, animT);
    }

    /// <summary>The stamps alone, laid on any quad the card is drawn as. Each slot's place is read off the
    /// quad by its own fractions, so the stamps lean and squeeze with the card through a turn instead of
    /// sitting flat on top of it.</summary>
    public static Vector2 DrawStamps(ImDrawListPtr dl, OsAppContext ctx, IRacerHost host, Vector2 tl,
        Vector2 tr, Vector2 br, Vector2 bl, int stamped, float alpha = 1f, int animating = -1,
        float animT = 1f)
    {
        var last = (tl + br) * 0.5f;
        for (var i = 0; i < Slots.Length && i < stamped; i++)
        {
            var u = Slots[i].X;
            var v = Slots[i].Y;
            var centre = Vector2.Lerp(Vector2.Lerp(tl, tr, u), Vector2.Lerp(bl, br, u), v);
            var span = Vector2.Distance(Vector2.Lerp(tl, bl, v), Vector2.Lerp(tr, br, v));
            var scale = 1f;
            var ink = alpha;
            if (i == animating)
            {
                scale = 1f + (0.9f * (1f - animT) * (1f - animT));
                ink *= MathF.Min(1f, animT * 2.2f);
            }
            RacerChrome.Stamp(dl, ctx, host.PetAssetRoot, centre, span * SlotRadius * StampFill * scale,
                ImGui.ColorConvertFloat4ToU32(Ink with { W = ink }));
            last = centre;
        }
        return last;
    }

    /// <summary>Where a full-size card sits on a phone screen. The sleeve is the same shape, so the turn
    /// and the rip both land on this one rect and the handover between them is not a cut.</summary>
    public static (Vector2 TopLeft, Vector2 Size) Stage(Vector2 origin, Vector2 size)
    {
        var width = MathF.Min(size.X - Px(48f), (size.Y * 0.62f) * Aspect);
        var drawn = new Vector2(width, width / Aspect);
        var centre = origin + new Vector2(size.X * 0.5f, size.Y * 0.44f);
        return (centre - (drawn * 0.5f), drawn);
    }
}
