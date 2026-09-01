using System;
using System.IO;
using System.Numerics;
using AetherLove.Shared.Racing;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>A full card, claimed. The card stands on the phone and turning it over is the gesture: press
/// the card and it spins about its middle, coming back round as the foil pack at the same size in the
/// same place, which is where the rip screen draws it too, so the handover is not a cut.</summary>
internal sealed class CardFlipOverlay(
    IRacerHost host,
    LumiRacePackDto pack,
    int stamps,
    Action<OsAppContext> playThud,
    Action backToMain)
{
    /// <summary>How long the turn takes, and how long the pack is held before the rip screen takes it.</summary>
    private const float TurnSeconds = 1.15f;
    private const float SettleSeconds = 0.45f;

    private float _age;
    private float _settle;
    private float _turn = -1f;
    private bool _thudded;

    /// <summary>The pack is face up and the rip screen should take over.</summary>
    public bool Done { get; private set; }

    /// <summary>The player left without turning it. The pack stays pending.</summary>
    public bool Dismissed { get; private set; }

    public LumiRacePackDto Pack => pack;

    public void Draw(OsAppContext ctx)
    {
        var origin = ImGui.GetWindowPos();
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##cardFlip", ImGui.GetWindowSize(), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }

        var dt = ImGui.GetIO().DeltaTime;
        _age += dt;
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();

        var turning = _turn >= 0f;
        if (turning)
        {
            _turn = MathF.Min(1f, _turn + (dt / (ctx.ReduceMotion ? 0.25f : TurnSeconds)));
        }

        dl.AddRectFilled(origin, origin + size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.72f * MathF.Min(1f, _age * 4f))));

        var (stageTopLeft, stageSize) = RacerCard.Stage(origin, size);
        var centre = stageTopLeft + (stageSize * 0.5f);

        if (!turning)
        {
            DrawStanding(ctx, dl, stageTopLeft, stageSize);
            DrawLeave(ctx, dl, origin, size);
            return;
        }

        var eased = Ease(_turn);
        var angle = eased * MathF.PI;
        var face = MathF.Abs(MathF.Cos(angle));
        var half = new Vector2(stageSize.X * 0.5f * face, stageSize.Y * 0.5f);

        // A little perspective: the edge swinging toward the viewer stands taller than the one going away.
        var lean = MathF.Sin(angle) * stageSize.Y * 0.055f;
        var side = MathF.Cos(angle) >= 0f ? 1f : -1f;
        var tl = new Vector2(centre.X - half.X, centre.Y - half.Y + (lean * side));
        var tr = new Vector2(centre.X + half.X, centre.Y - half.Y - (lean * side));
        var br = new Vector2(centre.X + half.X, centre.Y + half.Y + (lean * side));
        var bl = new Vector2(centre.X - half.X, centre.Y + half.Y - (lean * side));

        // Edge-on the card catches the light; away from it the turned face is shaded.
        var shade = 0.55f + (0.45f * face);
        var ink = ImGui.ColorConvertFloat4ToU32(new Vector4(shade, shade, shade, 1f));

        if (MathF.Cos(angle) >= 0f)
        {
            DrawTurningCard(ctx, dl, tl, tr, br, bl, ink, face);
        }
        else
        {
            DrawTurningPack(ctx, dl, tl, tr, br, bl, ink);
        }

        // Edge-on there is nothing left to draw, so the card's own edge takes the light instead.
        if (face < 0.09f)
        {
            var glare = 1f - (face / 0.09f);
            RacerChrome.Halo(dl, centre, stageSize.Y * 0.5f * glare,
                new Vector4(1f, 0.96f, 0.78f, 1f), 0.45f * glare, 4);
            dl.AddLine(new Vector2(centre.X, centre.Y - (stageSize.Y * 0.5f)),
                new Vector2(centre.X, centre.Y + (stageSize.Y * 0.5f)),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.98f, 0.88f, glare)), Px(3f));
        }

        if (!ctx.ReduceMotion)
        {
            DrawSpin(dl, centre, MathF.Max(stageSize.X, stageSize.Y) * 0.62f, _turn);
        }

        if (_turn >= 1f)
        {
            if (!_thudded)
            {
                _thudded = true;
                playThud(ctx);
            }
            _settle += dt;
            if (_settle >= (ctx.ReduceMotion ? 0.1f : SettleSeconds))
            {
                Done = true;
            }
        }
    }

    /// <summary>The card before the turn. It breathes, catches a slow sweep of light, and is itself the
    /// button: pressing the card is what turns it.</summary>
    private void DrawStanding(OsAppContext ctx, ImDrawListPtr dl, Vector2 topLeft, Vector2 size)
    {
        var bob = ctx.ReduceMotion ? 0f : MathF.Sin(_age * 1.7f) * Px(4);
        var at = topLeft + new Vector2(0f, bob);

        ImGui.SetCursorScreenPos(at);
        var pressed = ImGui.InvisibleButton("##turnCard", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        RacerChrome.Halo(dl, at + (size * 0.5f), size.X * 0.78f,
            new Vector4(1f, 0.84f, 0.45f, 1f), hovered ? 0.44f : 0.30f, 5);
        dl.AddRectFilled(at + new Vector2(Px(6), size.Y - Px(2)),
            at + new Vector2(size.X - Px(6), size.Y + Px(10)), 0x40000000u, Px(10));
        RacerCard.Draw(dl, ctx, host, at, size, stamps);

        if (!ctx.ReduceMotion)
        {
            // A slow sweep of light across the paper, so a still card does not read as a frozen screen.
            var sweep = ((_age * 0.42f) % 1.6f) - 0.3f;
            if (sweep is > 0f and < 1f)
            {
                var x = at.X + (size.X * sweep);
                var band = size.X * 0.14f;
                dl.PushClipRect(at, at + size, true);
                dl.AddRectFilledMultiColor(new Vector2(x - band, at.Y), new Vector2(x + band, at.Y + size.Y),
                    0x00FFFFFFu, 0x28FFFFFFu, 0x28FFFFFFu, 0x00FFFFFFu);
                dl.PopClipRect();
            }
        }

        var hint = ctx.Localize("os.racer_card_turn");
        var pulse = ctx.ReduceMotion ? 1f : 0.72f + (0.28f * MathF.Sin(_age * 3.1f));
        var hintSize = ImGui.CalcTextSize(hint);
        dl.AddText(new Vector2(at.X + ((size.X - hintSize.X) * 0.5f), at.Y + size.Y + Px(16)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.92f, 0.62f, pulse)), hint);

        if (pressed)
        {
            _turn = 0f;
        }
    }

    private void DrawLeave(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        var width = MathF.Min(Px(240), size.X - Px(56));
        var height = Px(42);
        var at = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - Px(58));
        ImGui.SetCursorScreenPos(at);
        var pressed = ImGui.InvisibleButton("##backMain", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var br = at + new Vector2(width, height);
        var fill = RacerChrome.DutchBlue with { W = hovered ? 1f : 0.92f };
        dl.AddRectFilled(at + new Vector2(0f, Px(3)), br + new Vector2(0f, Px(3)), 0x66000000u, height * 0.3f);
        dl.AddRectFilled(at, br, ImGui.ColorConvertFloat4ToU32(fill), height * 0.3f);
        dl.AddRect(at, br, hovered ? 0xFFFFFFFFu : 0x8CFFFFFFu, height * 0.3f,
            ImDrawFlags.RoundCornersAll, Px(1.6f));

        var label = ctx.Localize("os.racer_back_main");
        var text = ImGui.CalcTextSize(label);
        dl.AddText(at + ((new Vector2(width, height) - text) * 0.5f), 0xFFFFFFFF, label);

        if (pressed)
        {
            Dismissed = true;
            backToMain();
        }
    }

    private void DrawTurningCard(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 tr, Vector2 br,
        Vector2 bl, uint ink, float face)
    {
        if (ctx.Capabilities.Textures.Get(RacerCard.Path(host)) is { } tex)
        {
            dl.AddImageQuad(tex, tl, tr, br, bl,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), ink);
        }
        else
        {
            Span<Vector2> pts = [tl, tr, br, bl];
            dl.AddConvexPolyFilled(ref pts[0], 4, ink);
        }

        if (face > 0.08f)
        {
            RacerCard.DrawStamps(dl, ctx, host, tl, tr, br, bl, stamps, face);
        }
    }

    private void DrawTurningPack(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 tr, Vector2 br,
        Vector2 bl, uint ink)
    {
        var path = Path.Combine(host.PetAssetRoot, "racer", "foil-pack.png");
        if (ctx.Capabilities.Textures.Get(path) is { } tex)
        {
            // Drawn the right way round even though this is the back coming over: the rip screen
            // draws the pack unmirrored in the same place, and a mirror that snaps straight at the
            // handover reads as a glitch, not as physics.
            dl.AddImageQuad(tex, tl, tr, br, bl,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), ink);
        }
        else
        {
            Span<Vector2> pts = [tl, tr, br, bl];
            dl.AddConvexPolyFilled(ref pts[0], 4, 0xFF6B4C9Au);
        }
    }

    /// <summary>Sparks thrown off the turn, densest as the card goes edge-on. They are drawn from the
    /// clock rather than a pool, so nothing survives the overlay closing.</summary>
    private static void DrawSpin(ImDrawListPtr dl, Vector2 centre, float radius, float turn)
    {
        const int count = 22;
        var burst = MathF.Exp(-40f * (turn - 0.5f) * (turn - 0.5f));
        for (var i = 0; i < count; i++)
        {
            var seed = (i * 2.399963f) + (turn * 3.1f);
            var a = seed % MathF.Tau;
            var far = radius * (0.55f + (0.85f * ((i * 0.137f) % 1f))) * (0.6f + (0.7f * turn));
            var p = centre + (new Vector2(MathF.Cos(a), MathF.Sin(a) * 0.8f) * far);
            var glow = burst * (0.35f + (0.65f * ((i * 0.311f) % 1f)));
            if (glow < 0.04f)
            {
                continue;
            }
            var ink = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.92f, 0.62f, glow));
            var r = Px(2.6f) * (0.6f + glow);
            dl.AddCircleFilled(p, r, ink, 8);
            dl.AddLine(p - new Vector2(r * 2.4f, 0f), p + new Vector2(r * 2.4f, 0f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0.85f, glow * 0.5f)), Px(1f));
        }
    }

    /// <summary>Slow in, fast through the turn, slow out, so the moment the face changes is the moment the
    /// eye is following fastest.</summary>
    private static float Ease(float t) => t * t * (3f - (2f * t));
}
