using System;
using System.Numerics;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>Three screens that explain nothing, each less readable than the last. The joke only works if it
/// never breaks: no icon, no hint, no way to tell what the app is.</summary>
internal sealed class OnboardingScreen(Action done)
{
    private const int TotalSteps = 3;

    /// <summary>How much of each step survives. The first still scans as sentences, the last is static.</summary>
    private static readonly float[] Legibility = [0.62f, 0.28f, 0.04f];

    private static readonly int[] Words = [14, 18, 26];

    private int _step;
    private double _shown;

    public void OnShow()
    {
        _step = 0;
        _shown = ImGui.GetTime();
    }

    public void Draw(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var t = (float)(ImGui.GetTime() - _shown);
        var fade = ctx.ReduceMotion ? 1f : Look.EaseOut(t / 0.5f);

        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void));
        Look.Halo(dl, origin + new Vector2(size.X * 0.5f, size.Y * 0.42f), size.X * 0.75f, Look.Crystal, 0.10f * fade);

        var pipY = origin.Y + Px(26f);
        for (var i = 0; i < TotalSteps; i++)
        {
            var centre = new Vector2(origin.X + (size.X * 0.5f) + ((i - 1) * Px(16f)), pipY);
            dl.AddCircleFilled(centre, Px(3.5f), Look.U32(Look.Crystal, (i <= _step ? 0.85f : 0.22f) * fade), 16);
        }

        var wobble = ctx.ReduceMotion ? 0f : MathF.Sin((float)ImGui.GetTime() * 1.7f) * Px(2f);
        var markY = origin.Y + (size.Y * 0.20f) + wobble;
        using (ctx.TitleFont?.Push())
        {
            Look.Centred(dl, "???", origin.X + (size.X * 0.5f), markY,
                Look.U32(Look.CrystalPale, 0.9f * fade), 1.6f);
        }

        var noise = Garble.Wrap(Garble.Block(_step * 977, Words[_step], Legibility[_step]), 26);
        var lineStep = ImGui.GetTextLineHeight() * 1.35f;
        Look.CentredBlock(dl, noise, origin.X + (size.X * 0.5f), origin.Y + (size.Y * 0.40f),
            Look.U32(Look.Whisper, fade), 1f, lineStep);

        var drift = ctx.ReduceMotion ? 0f : MathF.Sin((float)ImGui.GetTime() * 0.6f) * Px(6f);
        var tail = Garble.Wrap(Garble.Block((_step * 977) + 41, Words[_step] / 2, 0.02f), 30);
        Look.CentredBlock(dl, tail, origin.X + (size.X * 0.5f) + drift, origin.Y + (size.Y * 0.70f),
            Look.U32(Look.Whisper, 0.45f * fade), 0.85f, lineStep * 0.85f);

        DrawButton(ctx, dl, origin, size, fade);
    }

    private void DrawButton(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float fade)
    {
        var label = ctx.Localize($"os.aetherling_step_{_step + 1}");
        var height = Px(38f);
        var width = size.X - (Px(48f) * 2f);
        var tl = new Vector2(origin.X + (size.X - width) * 0.5f, origin.Y + size.Y - height - Px(30f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingStep", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var radius = height * 0.5f;
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal with { W = hovered ? 0.20f : 0.11f }, fade), radius);
        dl.AddRect(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal, (hovered ? 0.75f : 0.40f) * fade), radius, ImDrawFlags.RoundCornersAll, Px(1.2f));
        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.CrystalPale, fade));

        if (!pressed)
        {
            return;
        }
        _step++;
        _shown = ImGui.GetTime();
        if (_step >= TotalSteps)
        {
            done();
        }
    }
}
