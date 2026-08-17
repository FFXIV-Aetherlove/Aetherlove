using System;
using System.Numerics;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>One scratch-off card: a foil of grid cells the cursor rubs away. Past the reveal
/// threshold the rest wipes itself and the caller is asked to reveal server-side; the prize face
/// underneath is whatever the caller draws. Under reduce motion the rubbing is a button.</summary>
internal sealed class ScratchCard(int slot)
{
    private const int Cols = 10;
    private const int Rows = 6;
    private const float RevealFraction = 0.6f;

    /// <summary>How long the prize takes to finish arriving: the flash, the ring and the sparks.</summary>
    private const float CelebrateSeconds = 1.6f;

    private readonly bool[] _cleared = new bool[Cols * Rows];
    private readonly Spark[] _sparks = new Spark[28];
    private int _clearedCount;
    private bool _wiping;
    private float _wipe;
    private float _celebrate = -1f;

    private struct Spark
    {
        public Vector2 Dir;
        public float Speed;
        public float Size;
        public float Spin;
        public float Life;
        public bool Gold;
    }

    public int Slot { get; } = slot;

    /// <summary>The card has been rubbed past the threshold and wants its server reveal.</summary>
    public bool WantsReveal { get; private set; }

    public void MarkRevealRequested() => WantsReveal = false;

    public void Reset()
    {
        Array.Clear(_cleared);
        _clearedCount = 0;
        _wiping = false;
        _wipe = 0f;
        _celebrate = -1f;
        WantsReveal = false;
    }

    /// <summary>The prize landed: run the shiny bit. Called when the server answers, not when the
    /// foil finishes, so the sparks and the thing they are celebrating arrive together.</summary>
    public void Celebrate()
    {
        _celebrate = 0f;
        var rng = new Random((Slot * 7919) + 17);
        for (var i = 0; i < _sparks.Length; i++)
        {
            var angle = (MathF.Tau * i / _sparks.Length) + ((float)rng.NextDouble() * 0.28f);
            _sparks[i] = new Spark
            {
                Dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle) * 0.62f),
                Speed = 0.55f + ((float)rng.NextDouble() * 0.75f),
                Size = 0.5f + ((float)rng.NextDouble() * 0.9f),
                Spin = ((float)rng.NextDouble() * 2f) - 1f,
                Life = 0.6f + ((float)rng.NextDouble() * 0.4f),
                Gold = rng.Next(3) != 0,
            };
        }
    }

    /// <summary>Draws the card. <paramref name="revealed"/> face-up cards carry no foil at all;
    /// <paramref name="busy"/> shows the spinner while the server answers; <paramref name="drawFace"/>
    /// paints the prize (or the face-down tease) inside the given rect.</summary>
    public void Draw(
        OsAppContext ctx,
        ImDrawListPtr dl,
        Vector2 tl,
        Vector2 size,
        bool revealed,
        bool busy,
        Action<Vector2, Vector2> drawFace)
    {
        var br = tl + size;
        var radius = Px(14f);
        dl.AddRectFilled(tl, br, Look.U32(new Vector4(0.10f, 0.09f, 0.16f, 0.96f)), radius);
        dl.AddRect(tl, br, Look.U32(Look.Crystal, 0.35f), radius, ImDrawFlags.RoundCornersAll, Px(1.2f));

        drawFace(tl, size);

        if (busy)
        {
            LoadingSpinner.Draw(tl + (size * 0.5f), Px(10f), Px(2.4f), Look.U32(Look.CrystalPale));
            return;
        }
        if (revealed)
        {
            DrawCelebration(ctx, dl, tl, size, radius);
            return;
        }

        // The foil. Cells fade individually; the auto-wipe sweeps the stragglers.
        if (_wiping)
        {
            _wipe = MathF.Min(1f, _wipe + (ImGui.GetIO().DeltaTime / 0.45f));
            if (_wipe >= 1f)
            {
                _wiping = false;
                WantsReveal = true;
            }
        }

        var cell = new Vector2(size.X / Cols, size.Y / Rows);
        dl.PushClipRect(tl, br, true);
        for (var i = 0; i < _cleared.Length; i++)
        {
            if (_cleared[i])
            {
                continue;
            }
            var col = i % Cols;
            var row = i / Cols;
            if (_wiping && (col + row) / (float)(Cols + Rows) < _wipe)
            {
                continue;
            }
            var cellTl = tl + new Vector2(col * cell.X, row * cell.Y);
            var shade = 0.32f + (0.10f * MathF.Sin((col * 1.7f) + (row * 2.3f)));
            dl.AddRectFilled(cellTl, cellTl + cell + new Vector2(0.5f, 0.5f),
                Look.U32(new Vector4(shade, shade, shade + 0.06f, 1f)));
        }
        dl.PopClipRect();

        if (!_wiping && _clearedCount == 0)
        {
            Look.Centred(dl, ctx.Localize("os.aetherling_scratch_hint"), tl.X + (size.X * 0.5f),
                tl.Y + ((size.Y - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.CrystalPale, 0.75f), 0.9f);
        }

        if (ctx.ReduceMotion)
        {
            var label = ctx.Localize("os.aetherling_scratch_reveal");
            var bw = ImGui.CalcTextSize(label).X + Px(28f);
            var bh = Px(28f);
            var btl = new Vector2(tl.X + ((size.X - bw) * 0.5f), br.Y - bh - Px(8f));
            ImGui.SetCursorScreenPos(btl);
            if (ImGui.InvisibleButton($"##scratchReveal{Slot}", new Vector2(bw, bh)))
            {
                WantsReveal = true;
            }
            if (ImGui.IsItemHovered())
            {
                HandOnHover();
            }
            dl.AddRectFilled(btl, btl + new Vector2(bw, bh), Look.U32(Look.Crystal with { W = 0.3f }), bh * 0.5f);
            Look.Centred(dl, label, btl.X + (bw * 0.5f),
                btl.Y + ((bh - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.CrystalPale));
            return;
        }

        // The rub: an invisible surface, cells clearing around a held-down cursor.
        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton($"##scratch{Slot}", size);
        if (ImGui.IsItemHovered())
        {
            HandOnHover();
        }
        if (!ImGui.IsItemActive() || !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            return;
        }

        var mouse = ImGui.GetMousePos();
        var radius2 = 1.2f;
        var mx = (mouse.X - tl.X) / cell.X;
        var my = (mouse.Y - tl.Y) / cell.Y;
        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Cols; col++)
            {
                var i = (row * Cols) + col;
                if (_cleared[i])
                {
                    continue;
                }
                var dx = col + 0.5f - mx;
                var dy = row + 0.5f - my;
                if ((dx * dx) + (dy * dy) > radius2 * radius2)
                {
                    continue;
                }
                _cleared[i] = true;
                _clearedCount += 1;
            }
        }

        if (_clearedCount >= _cleared.Length * RevealFraction && !_wiping)
        {
            _wiping = true;
            _wipe = 0f;
        }
    }

    /// <summary>The payoff: a wash of white, a rim that catches light, a ring pushing out, a glint
    /// crossing the foil and a shower of twinkles. It runs over the prize rather than under it, so
    /// the thing being celebrated is what the light is coming off.</summary>
    private void DrawCelebration(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size, float radius)
    {
        if (_celebrate < 0f)
        {
            return;
        }
        if (ctx.ReduceMotion)
        {
            _celebrate = -1f;
            return;
        }

        _celebrate += ImGui.GetIO().DeltaTime;
        if (_celebrate >= CelebrateSeconds)
        {
            _celebrate = -1f;
            return;
        }

        var br = tl + size;
        var centre = tl + (size * 0.5f);
        var t = _celebrate / CelebrateSeconds;

        var ring = Look.EaseOut(Math.Clamp(_celebrate / 0.7f, 0f, 1f));
        if (ring < 1f)
        {
            Look.Halo(dl, centre, size.Y * (0.3f + (0.9f * ring)), Look.Spark, 0.45f * (1f - ring));
        }

        dl.PushClipRect(tl, br, true);

        var flash = Math.Clamp(1f - (_celebrate / 0.22f), 0f, 1f);
        if (flash > 0f)
        {
            dl.AddRectFilled(tl, br, Look.U32(new Vector4(1f, 1f, 1f, 1f), 0.5f * flash), radius);
        }

        // The glint: a soft band of light crossing the card once.
        var sweep = Math.Clamp((_celebrate - 0.10f) / 0.55f, 0f, 1f);
        if (sweep > 0f && sweep < 1f)
        {
            var band = size.X * 0.24f;
            var x = tl.X - band + (sweep * (size.X + (band * 2f)));
            var clear = Look.U32(Look.CrystalPale, 0f);
            var lit = Look.U32(Look.CrystalPale, 0.26f * MathF.Sin(sweep * MathF.PI));
            dl.AddRectFilledMultiColor(new Vector2(x - band, tl.Y), new Vector2(x, br.Y), clear, lit, lit, clear);
            dl.AddRectFilledMultiColor(new Vector2(x, tl.Y), new Vector2(x + band, br.Y), lit, clear, clear, lit);
        }

        dl.PopClipRect();

        foreach (var spark in _sparks)
        {
            var p = _celebrate / (CelebrateSeconds * spark.Life);
            if (p >= 1f)
            {
                continue;
            }
            var travel = size.Y * 0.62f * spark.Speed * Look.EaseOut(p);
            var at = centre + (spark.Dir * travel) + new Vector2(0f, travel * 0.30f * p * p);
            var arm = Px(5f) * spark.Size * (1f - (0.45f * p));
            var angle = spark.Spin * p * 5f;
            var colour = Look.U32(spark.Gold ? Look.Spark : Look.CrystalPale, 1f - (p * p));
            var a = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * arm;
            var b = new Vector2(-a.Y, a.X) * 0.62f;
            dl.AddLine(at - a, at + a, colour, Px(1.4f));
            dl.AddLine(at - b, at + b, colour, Px(1.4f));
        }

        // The rim, catching first and cooling off last.
        dl.AddRect(tl, br, Look.U32(Look.Spark, 0.9f * (1f - Look.EaseOut(t))), radius,
            ImDrawFlags.RoundCornersAll, Px(1.8f));
    }
}
