using System;
using System.Numerics;
using AetherOS.PetKit.Engine;
using Dalamud.Bindings.ImGui;

namespace AetherOS.PetKit.Rendering;

/// <summary>
/// The code-drawn parts (ears, tails) and the flown kite, transcribed from the prototype's
/// PetDraw (aetherlove-aetherling, merge 45af73b) onto this engine's draw arithmetic. Every
/// tuning number and every hard-won rule in here is the prototype's; the only adaptations are
/// the pose plumbing (this engine passes cellIndex/scale/offset/flipX loose rather than a
/// PetPose) and the absence of drawn limbs (a kite pin rides the anchor, never a hand's reach).
/// </summary>
public static class PartsDraw
{
    /// <summary>Fills go down with anti-aliasing off: the strips and fringes abut along shared
    /// edges, and two feathered edges laid together do not make an opaque join.</summary>
    private readonly ref struct NoFillAntiAlias
    {
        private readonly ImDrawListPtr _list;
        private readonly ImDrawListFlags _saved;

        public NoFillAntiAlias(ImDrawListPtr list)
        {
            _list = list;
            _saved = list.Flags;
            list.Flags = _saved & ~ImDrawListFlags.AntiAliasedFill;
        }

        public void Dispose() => _list.Flags = _saved;
    }

    private const int MaxTailKnots = TailFx.MaxKnots;

    /// <summary>Points in one ear's outline: two curves of thirteen, a six-point cap, and slack.</summary>
    private const int EarOutlinePoints = 40;

    /// <summary>The bluntest a tip is allowed to be sharp, as a fraction of the base half-width:
    /// two curves meeting at 43 degrees pinch ImGui's mitred stroke to nothing at the point.</summary>
    private const float MinTipHalfWidth = 0.06f;

    /// <summary>The shortest outline segment worth keeping, in cell units. A near-zero segment
    /// has a near-garbage normal, which is what the stroke's anti-aliased geometry is built
    /// from; no segment shorter than the ink is wide survives to be stroked.</summary>
    private const float MinEarSegment = 2.8f;

    /// <summary>Cell-local to screen with the flip applied inside, the prototype's contract:
    /// part geometry is authored unflipped in cell space and mirrors with the creature.</summary>
    private static Vector2 ToScreen(
        AtlasManifest manifest, Vector2 local, Vector2 scale, bool flipX, Vector2 anchorBase, float ds)
    {
        if (flipX)
        {
            local.X = manifest.Cell - local.X;
        }
        var relative = local - new Vector2(manifest.Cell / 2f, manifest.Cell);
        return anchorBase + (relative * ds * scale);
    }

    /// <summary>
    /// One tail: a bushy silhouette built from per-point normals along an integrated spine. The
    /// angle is SET per segment and the position integrated by stepping one segment length along
    /// it, so the tail bends and never stretches however hard the stack pushes. Filled as a quad
    /// STRIP rather than one polygon: a furred outline is not convex, and PathFillConvex on a
    /// concave loop folds it inside out; consecutive samples always bound a convex quad.
    /// </summary>
    public static void DrawTail(
        ImDrawListPtr drawList, AtlasManifest manifest, AccessoryDef accessory, PetPose pose,
        Vector2 anchorBase, float ds, Palette palette, PartsRig parts)
    {
        if (accessory.Tail is not { } def || !manifest.Anchors.ContainsKey("tail"))
        {
            // No anchor means this shell has never been fitted for a tail. Drawing one on the
            // centre fallback would stick it through the pet's middle, so a shell opts in by
            // carrying the anchor and nothing else has to know.
            return;
        }

        if (!PartInks(manifest, palette, out var body, out var accent, out var shade, out var rim, out var ink))
        {
            return;
        }

        var scale = pose.Scale;
        var flipX = pose.FlipX;
        var toCell = manifest.Cell / 256f;
        var knots = Math.Clamp(def.Segs + 1, 2, MaxTailKnots);
        var deltas = parts.Tail.Deltas(knots, def.Response);

        // The shell's own fit for this tail, read exactly as the ears read theirs: a tail authored
        // for the trueform is a body length on a small shell and a stub on a large one.
        var (fitScale, fitOffset, _) = manifest.FitFor(accessory.Slot, accessory.Name);

        // The SMOOTHED seat where the rig carries one, the raw anchor from Rest. The tail is the
        // one part whose seat glides: its root is buried deep enough inside the silhouette that
        // the glide never shows as a gap, and without it the largest thing on screen translates
        // in clip-rate steps. The ears stay on raw anchors, where a glide reads as detachment.
        var seat = (parts.TailSeat ?? manifest.AnchorFor("tail", pose))
                   + (def.NudgePoint * toCell * fitScale)
                   + (fitOffset * toCell);
        var segLen = def.Len * toCell * fitScale / (knots - 1);

        Span<Vector2> spine = stackalloc Vector2[MaxTailKnots];
        Span<float> radii = stackalloc float[MaxTailKnots];
        Span<Vector2> left = stackalloc Vector2[MaxTailKnots];
        Span<Vector2> right = stackalloc Vector2[MaxTailKnots];

        var p = seat;
        spine[0] = p;
        radii[0] = def.RadiusAt(0f) * toCell * fitScale;
        for (var i = 1; i < knots; i++)
        {
            var u = i / (float)(knots - 1);
            // The model's own line: rest direction plus its signature curl, biased so the bend
            // gathers toward the tip rather than spreading evenly.
            var baseAngle = (def.Dir * (MathF.PI / 180f))
                            + ((def.Curl * (MathF.PI / 180f)) * MathF.Pow(u, 1.35f));
            var ang = baseAngle + deltas[i];
            p += new Vector2(MathF.Cos(ang) * segLen, MathF.Sin(ang) * segLen);
            spine[i] = p;
            radii[i] = def.RadiusAt(u) * toCell * fitScale;
        }

        // The flanks, jagged into fur. Per-point normals, never a fixed offset: a fixed inset
        // walks outside the outline wherever the shape turns. Jags are hashed by INDEX, never by
        // the frame: jag by the clock and the whole edge boils; jag by the index and the fur
        // flows with the spine, as fur does.
        var step = Math.Max(1, def.FurStep);
        for (var i = 0; i < knots; i++)
        {
            var a = spine[Math.Max(i - 1, 0)];
            var b = spine[Math.Min(i + 1, knots - 1)];
            var d = b - a;
            var len = d.Length();
            var n = len < 0.001f ? new Vector2(0f, 1f) : new Vector2(-d.Y / len, d.X / len);
            var t = len < 0.001f ? Vector2.Zero : d / len;
            var deep = (i % step) == 1 ? 1f : 0.35f;
            var jl = radii[i] * def.Fur * deep * (0.5f + PartDrag.Hash(i));
            var jr = radii[i] * def.Fur * deep * (0.5f + PartDrag.Hash(i, 7f));

            // Guard hairs lie toward the tip: each jag swept back along the tangent, which is
            // the difference between fur and a saw blade.
            var sweep = t * (-0.55f * jl);
            left[i] = spine[i] + (n * (radii[i] + jl)) + sweep;
            right[i] = spine[i] - (n * (radii[i] + jr)) + sweep;
        }

        Vector2 S(Vector2 cell) => ToScreen(manifest, cell, scale, flipX, anchorBase, ds);

        // The end cap: a fan of arc points around the last knot at the tail's own final girth.
        // "Rounded end" and "pointed end" are both THIS, with no flag between them: a profile
        // that keeps its final radius ends in a soft curve, one that tapers ends in a point.
        var tipEnd = spine[knots - 1];
        var tang = tipEnd - spine[knots - 2];
        var tlen = tang.Length();
        tang = tlen < 0.001f ? new Vector2(1f, 0f) : tang / tlen;
        var tnrm = new Vector2(-tang.Y, tang.X);
        var capR = radii[knots - 1];
        var capSteps = Math.Clamp((int)capR, 2, 8);
        Span<Vector2> cap = stackalloc Vector2[9];
        for (var k = 0; k <= capSteps; k++)
        {
            var th = MathF.PI * k / capSteps;
            cap[k] = tipEnd + (((tnrm * MathF.Cos(th)) + (tang * MathF.Sin(th))) * capR);
        }

        var tipStart = def.TipFrac > 0f ? (int)MathF.Round(def.TipFrac * (knots - 1)) : int.MaxValue;
        var noAa = new NoFillAntiAlias(drawList);
        try
        {
            for (var i = 0; i < knots - 1; i++)
            {
                drawList.AddQuadFilled(S(left[i]), S(left[i + 1]), S(right[i + 1]), S(right[i]),
                    i >= tipStart ? accent : body);
            }

            // The cap fills as a fan from the last knot: wedges of a disc, convex each.
            var capColour = (knots - 1) >= tipStart ? accent : body;
            for (var k = 0; k < capSteps; k++)
            {
                drawList.AddTriangleFilled(S(tipEnd), S(cap[k]), S(cap[k + 1]), capColour);
            }

            // The underside shade, inside the silhouette on the lower flank.
            if (def.Shade > 0f)
            {
                for (var i = 2; i < knots - 1; i++)
                {
                    var a = Vector2.Lerp(spine[i], right[i], 0.45f);
                    var b = Vector2.Lerp(spine[i + 1], right[i + 1], 0.45f);
                    drawList.AddQuadFilled(S(a), S(b), S(right[i + 1]), S(right[i]), shade);
                }
            }
        }
        finally
        {
            noAa.Dispose();
        }

        // The rim, on the lit side and over the planted end only, fading out before the tip: a
        // rim that ran the whole length reads as a second, thinner tail alongside the first at
        // delivery size. The offset is applied in CELL space, before the transform, so the
        // highlight swaps sides when the creature turns instead of staying stubbornly left.
        var keep = Math.Max(2, (int)MathF.Round(knots * 0.62f));
        for (var i = 1; i < keep - 1; i++)
        {
            var fade = 1f - (i / (float)keep);
            var w = radii[i] * 0.30f * (0.40f + (0.60f * fade)) * ds;
            if (w < 0.6f)
            {
                continue;
            }

            var a = spine[i] + new Vector2(-0.80f, -0.16f) * (radii[i] * 0.52f);
            var b = spine[i + 1] + new Vector2(-0.80f, -0.16f) * (radii[i + 1] * 0.52f);
            drawList.AddLine(S(a), S(b), rim, w);
        }

        // One ink around the whole edge, last, so the fill can never leak past it. The cap's
        // interior points ride between the two flanks so the line wraps the end.
        drawList.PathClear();
        for (var i = 0; i < knots; i++)
        {
            drawList.PathLineTo(S(left[i]));
        }

        for (var k = 1; k < capSteps; k++)
        {
            drawList.PathLineTo(S(cap[k]));
        }

        for (var i = knots - 1; i >= 0; i--)
        {
            drawList.PathLineTo(S(right[i]));
        }

        drawList.PathStroke(ink, ImDrawFlags.Closed, MathF.Max(1f, 2.3f * ds));

        // The selfie compositor replays recorded geometry, and the compositor's polygon fill is
        // a general scanline, so the whole silhouette records as one loop plus the ink. The tip
        // tint, shade and rim are dropped from the recording: a keepsake's tail at selfie size
        // does not read them, and recording every strip quad would cost more than it shows.
        if (PetFrameRecorder.Recording)
        {
            var outline = new Vector2[(knots * 2) + Math.Max(0, capSteps - 1)];
            var w2 = 0;
            for (var i = 0; i < knots; i++)
            {
                outline[w2++] = S(left[i]);
            }
            for (var k = 1; k < capSteps; k++)
            {
                outline[w2++] = S(cap[k]);
            }
            for (var i = knots - 1; i >= 0; i--)
            {
                outline[w2++] = S(right[i]);
            }
            PetFrameRecorder.Add(outline, closed: true, thickness: 0f, body);
            PetFrameRecorder.Add(outline, closed: true, thickness: MathF.Max(1f, 2.3f * ds), ink);
        }
    }

    /// <summary>
    /// Both ears, each on its own anchor. Two anchors rather than one mirrored point, exactly as
    /// handL/handR already work, so a shell whose boop squashes one side harder places each ear
    /// honestly rather than averaging them. Filled as a convex core plus a fringe of triangles:
    /// nothing here is ever asked to fill a concave shape.
    /// </summary>
    public static void DrawEars(
        ImDrawListPtr drawList, AtlasManifest manifest, AccessoryDef accessory, PetPose pose,
        Vector2 anchorBase, float ds, Palette palette, PartsRig parts)
    {
        if (accessory.Ears is not { } def
            || !manifest.Anchors.ContainsKey("earL") || !manifest.Anchors.ContainsKey("earR"))
        {
            return;
        }

        if (!PartInks(manifest, palette, out var body, out var accent, out _, out var rim, out var ink))
        {
            return;
        }

        var scale = pose.Scale;
        var flipX = pose.FlipX;
        var toCell = manifest.Cell / 256f;

        // The shell's own fit for this pair, per ITEM. The SCALE matters as much as the offset:
        // a model is authored in 256-space and grows with the cell, but a shell's head does not,
        // so an ear tuned to the trueform's crown overshoots a small-headed shell entirely. The
        // model's own nudge scales with it, keeping the base where the author put it; the shell's
        // offset does not, because it is a correction in the shell's own units.
        var (fitScale, fitOffset, _) = manifest.FitFor(accessory.Slot, accessory.Name);
        var hw = def.BaseHalfWidth * toCell * fitScale;
        var h = def.Height * toCell * fitScale;

        Span<Vector2> shape = stackalloc Vector2[EarOutlinePoints];
        Span<Vector2> smooth = stackalloc Vector2[EarOutlinePoints];

        for (var ear = 0; ear < 2; ear++)
        {
            var side = ear == 0 ? -1f : 1f;
            parts.Ears.Sample(ear, out var degrees, out var earScale, out var bend);
            var seat = manifest.AnchorFor(ear == 0 ? "earL" : "earR", pose)
                       + (new Vector2(def.NudgePoint.X * side, def.NudgePoint.Y) * toCell * fitScale)
                       + new Vector2(fitOffset.X * side * toCell, fitOffset.Y * toCell);

            var count = BuildEar(def, hw, h, bend * def.Floppy * side, shape, smooth);
            var a = (def.Lean + def.Droop + degrees) * side * (MathF.PI / 180f);
            var sin = MathF.Sin(a);
            var cos = MathF.Cos(a);

            Vector2 Place(Vector2 local)
            {
                var x = local.X * side * earScale;
                var y = local.Y * earScale;
                return ToScreen(
                    manifest,
                    seat + new Vector2((x * cos) - (y * sin), (x * sin) + (y * cos)),
                    scale, flipX, anchorBase, ds);
            }

            // A CONVEX core, then the fur as a fringe of triangles on top of it. Filling the
            // furred outline directly cannot be made to work: the smooth outline is convex, the
            // furred one has reflex corners, and PathFillConvex is undefined on a concave
            // polygon. Every jag is two triangles bridging the smooth edge to the furred one,
            // and a triangle cannot be concave.
            var noAa = new NoFillAntiAlias(drawList);
            try
            {
                drawList.PathClear();
                for (var i = 0; i < count; i++)
                {
                    drawList.PathLineTo(Place(smooth[i]));
                }

                drawList.PathFillConvex(body);

                if (def.Fur > 0f || def.Tuft > 0f)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var j = (i + 1) % count;
                        var core0 = Place(smooth[i]);
                        var core1 = Place(smooth[j]);
                        var fur0 = Place(shape[i]);
                        var fur1 = Place(shape[j]);
                        drawList.AddTriangleFilled(core0, fur0, fur1, body);
                        drawList.AddTriangleFilled(core0, fur1, core1, body);
                    }
                }

                // The inner hollow, struck from the SMOOTH outline: convex, so it fills in one
                // pass. Insetting the furred one propagated every jag inward and gave a tufted
                // ear a ragged middle.
                if (def.Inner > 0f)
                {
                    var k = def.Inner;
                    drawList.PathClear();
                    for (var i = 0; i < count; i++)
                    {
                        var q = smooth[i];
                        drawList.PathLineTo(Place(new Vector2(
                            (q.X * k) + (hw * 0.077f), (q.Y * k * 0.86f) - (h * 0.08f))));
                    }

                    drawList.PathFillConvex(accent);
                }
            }
            finally
            {
                noAa.Dispose();
            }

            // The rim on the lit edge, over the lower half only: the same fade the tail takes,
            // so an ear and a tail on one creature are lit by one sun.
            var half = count / 2;
            var rimW = MathF.Max(1f, def.BaseHalfWidth * 0.16f * toCell * ds);
            for (var i = Math.Max(1, half - 5); i < half; i++)
            {
                drawList.AddLine(
                    Place(new Vector2(smooth[i].X * 0.90f, (smooth[i].Y * 0.92f) + (hw * 0.077f))),
                    Place(new Vector2(smooth[i + 1].X * 0.90f, (smooth[i + 1].Y * 0.92f) + (hw * 0.077f))),
                    rim,
                    rimW);
            }

            // The ink goes on as a CAPSULE CHAIN, not a stroked polyline: ImGui's polyline
            // stroker builds its corners from averaged segment normals, and at an ear's tip
            // that construction pinches and the ink thins or vanishes. A capsule per segment
            // puts a circle on every vertex, and a circle covers every angle a corner can turn.
            var inkR = MathF.Max(0.6f, 1.15f * ds);
            for (var i = 0; i < count; i++)
            {
                AddCapsule(drawList, Place(shape[i]), inkR, Place(shape[(i + 1) % count]), inkR, ink);
            }

            if (PetFrameRecorder.Recording)
            {
                var outline = new Vector2[count];
                for (var i = 0; i < count; i++)
                {
                    outline[i] = Place(shape[i]);
                }
                PetFrameRecorder.Add(outline, closed: true, thickness: 0f, body);
                PetFrameRecorder.Add(outline, closed: true, thickness: inkR * 2f, ink);
            }
        }
    }

    /// <summary>
    /// One ear, authored upright: base at the origin, tip up, outboard +x. Two quadratics, and
    /// the two bow numbers are what make them an animal rather than a shape. Built UNBENT,
    /// always: the bend is applied at the very end as a deformation of points whose topology is
    /// already settled, because per-frame inputs may bend the outline but never again decide
    /// what is in it (that was the vanishing tip). Fills <paramref name="shape"/> with the drawn
    /// outline (fur and tufts included) and <paramref name="smooth"/> with the clean one the
    /// hollow and the rim are struck from; returns how many points both carry.
    /// </summary>
    private static int BuildEar(EarPartDef def, float hw, float h, float bend, Span<Vector2> shape, Span<Vector2> smooth)
    {
        // No corner sharper than the ink can draw: the smallest cap takes the sharpest corner
        // on the whole outline from 43 degrees to 76, which strokes cleanly at every size. A
        // floor rather than a number in each record, because this is a fact about the renderer
        // and not an art decision.
        var tw = MathF.Max(def.TipWidth * hw * 0.5f, hw * MinTipHalfWidth);
        var cx = hw * 0.16f;
        var cy = -h;
        var tipA = new Vector2(cx + tw, cy);
        var tipB = new Vector2(cx - tw, cy);
        var apex = new Vector2(cx, cy - (tw * 1.35f));
        var baseY = hw * 0.23f;

        var n = 0;
        Quad(smooth, ref n, new Vector2(hw, baseY), new Vector2(hw * (1f + def.BowOut), -h * 0.62f), tipA, 12);
        Quad(smooth, ref n, tipA, apex, tipB, 6, skipFirst: true);

        // ALWAYS skip the inner curve's first point: the previous curve already ended exactly
        // where this one starts, and a duplicated vertex is a zero-length segment whose mitre
        // collapses the stroke at the tip.
        Quad(smooth, ref n, tipB, new Vector2(-hw * (0.68f + def.BowIn), -h * 0.42f), new Vector2(-hw, baseY), 12,
            skipFirst: true);

        for (var i = 0; i < n; i++)
        {
            shape[i] = smooth[i];
        }

        // Tufts ride the inner edge only, faded to nothing at the tip: tufting the whole edge
        // evenly put full-depth jags where the ear is narrowest and read as a torn ear.
        if (def.Tuft > 0f)
        {
            var start = n / 2;
            for (var i = start; i < n; i++)
            {
                var grow = MathF.Pow((i - start) / MathF.Max(1f, n - 1f - start), 1.5f);
                var k = def.Tuft * hw * grow * (0.4f + PartDrag.Hash(i, 5f)) * ((i % 2) != 0 ? 1f : 0.3f);
                shape[i] = new Vector2(shape[i].X - k, shape[i].Y + (k * 0.35f));
            }
        }

        // Fur on the ear's own edges, the tail's machinery at a fraction of the depth: jags ride
        // the local NORMAL, and the envelope closes to nothing at both the point and the base.
        if (def.Fur > 0f)
        {
            Span<Vector2> src = stackalloc Vector2[EarOutlinePoints];
            for (var i = 0; i < n; i++)
            {
                src[i] = shape[i];
            }

            for (var i = 0; i < n; i++)
            {
                var a = src[(i - 1 + n) % n];
                var b = src[(i + 1) % n];
                var d = b - a;
                var len = d.Length();
                if (len < 0.001f)
                {
                    continue;
                }

                var nx = -d.Y / len;
                var ny = d.X / len;
                var v = Math.Clamp(-src[i].Y / h, 0f, 1f);

                // The Max is doing real work: at the tip v is exactly 1, and float32 Sin(PI) is
                // a tiny NEGATIVE number. Pow of a negative base with a fractional exponent is
                // NaN, and a NaN vertex silently deletes every capsule that touches it.
                var fade = MathF.Pow(MathF.Max(0f, MathF.Sin(MathF.PI * v)), 1.2f);
                var k = def.Fur * hw * fade * (0.35f + PartDrag.Hash(i, 9f)) * ((i % 2) != 0 ? 1f : 0.45f);
                shape[i] = src[i] + new Vector2(nx * k, ny * k);
            }
        }

        // Compact away sub-pixel segments, LAST, after every shaping pass: the exactly-zero
        // case (a duplicated vertex) and the nearly-zero one are a single disease, so the cure
        // is a single rule about LENGTH. Both outlines are compacted in lockstep, keyed on the
        // smooth one, because the fur fringe pairs smooth[i] with shape[i].
        var kept = 1;
        for (var i = 1; i < n; i++)
        {
            if (Vector2.Distance(smooth[i], smooth[kept - 1]) < MinEarSegment)
            {
                continue;
            }

            smooth[kept] = smooth[i];
            shape[kept] = shape[i];
            kept++;
        }

        // The loop closes back to the first point, so the last survivor gets the same test.
        if (kept > 3 && Vector2.Distance(smooth[kept - 1], smooth[0]) < MinEarSegment)
        {
            kept--;
        }

        // NOW the bend, on the settled outline: a curl gathered toward the tip, so the root
        // stays planted in the head and a floppy ear arcs rather than hinging.
        if (bend != 0f)
        {
            var curl = bend * (MathF.PI / 180f);
            var pivot = new Vector2(0f, baseY);
            for (var i = 0; i < kept; i++)
            {
                var v = Math.Clamp(-smooth[i].Y / h, 0f, 1f);
                var a = curl * MathF.Pow(v, 1.5f);
                var sin = MathF.Sin(a);
                var cos = MathF.Cos(a);
                var q = smooth[i] - pivot;
                smooth[i] = pivot + new Vector2((q.X * cos) - (q.Y * sin), (q.X * sin) + (q.Y * cos));
                q = shape[i] - pivot;
                shape[i] = pivot + new Vector2((q.X * cos) - (q.Y * sin), (q.X * sin) + (q.Y * cos));
            }
        }

        return kept;
    }

    /// <summary>Points along a quadratic, appended into <paramref name="into"/>.</summary>
    private static void Quad(Span<Vector2> into, ref int n, Vector2 p0, Vector2 p1, Vector2 p2, int steps, bool skipFirst = false)
    {
        for (var i = skipFirst ? 1 : 0; i <= steps; i++)
        {
            if (n >= into.Length)
            {
                return;
            }

            var t = i / (float)steps;
            var a = (1 - t) * (1 - t);
            var b = 2 * (1 - t) * t;
            var c = t * t;
            into[n++] = new Vector2(
                (a * p0.X) + (b * p1.X) + (c * p2.X),
                (a * p0.Y) + (b * p1.Y) + (c * p2.Y));
        }
    }

    /// <summary>The parts' five colours, struck from the shell's palette so both follow every
    /// colour profile for free: an Ember fox is fox-orange because the palette is, and nobody
    /// drew one.</summary>
    private static bool PartInks(AtlasManifest manifest, Palette palette, out uint body, out uint accent, out uint shade, out uint rim, out uint ink)
    {
        var tint = palette.BodyColor;
        var accentTint = palette.AccentColor;
        var layerAlpha = 1f;
        foreach (var layer in manifest.Layers)
        {
            if (layer.Role == TintRole.Body)
            {
                layerAlpha = layer.Alpha;
                break;
            }
        }

        var alpha = tint.W * layerAlpha;
        body = accent = shade = rim = ink = 0;
        if (alpha <= 0f)
        {
            return false;
        }

        body = ImGui.ColorConvertFloat4ToU32(tint with { W = alpha });
        accent = ImGui.ColorConvertFloat4ToU32(accentTint with { W = alpha });
        shade = ImGui.ColorConvertFloat4ToU32(
            new Vector4(tint.X * 0.82f, tint.Y * 0.82f, tint.Z * 0.82f, alpha));
        rim = ImGui.ColorConvertFloat4ToU32(new Vector4(
            tint.X + ((1f - tint.X) * 0.42f),
            tint.Y + ((1f - tint.Y) * 0.42f),
            tint.Z + ((1f - tint.Z) * 0.42f),
            alpha));

        var line = manifest.InkFor(tint);
        if (line.W <= 0f)
        {
            line = MouthDraw.DefaultLine;
        }

        ink = ImGui.ColorConvertFloat4ToU32(line with { W = line.W * alpha });
        return true;
    }

    /// <summary>A capsule: circles at both ends bridged by a convex quad, so PathFillConvex is
    /// safe and every joint of a chain is complete by construction.</summary>
    private static void AddCapsule(ImDrawListPtr drawList, Vector2 a, float ra, Vector2 b, float rb, uint colour)
    {
        drawList.AddCircleFilled(a, ra, colour, 20);
        drawList.AddCircleFilled(b, rb, colour, 20);
        var d = b - a;
        var len = d.Length();
        if (len < 0.5f)
        {
            return;
        }

        var n = new Vector2(-d.Y, d.X) / len;
        drawList.PathLineTo(a + (n * ra));
        drawList.PathLineTo(b + (n * rb));
        drawList.PathLineTo(b - (n * rb));
        drawList.PathLineTo(a - (n * ra));
        drawList.PathFillConvex(colour);
    }

    /// <summary>How far below the <c>head</c> pin a worn strand pair is sown, 256-space. The root has to
    /// finish INSIDE the silhouette so the stalk grows out of the creature rather than starting in the air
    /// above it, and the head pin is a hat's base line rather than the last opaque pixel.</summary>
    private const float WornStrandSink = 5f;

    /// <summary>A strand fan worn on the ears slot (the Antennae), sown just under the head pin. Worn
    /// geometry is authored in 256-space, so it takes the cell ratio and the shell's fit exactly as the
    /// ear and tail models do; the shell's OWN fan below is authored in its own cell space and takes
    /// neither.</summary>
    public static void DrawEarStrands(
        ImDrawListPtr drawList, AtlasManifest manifest, AccessoryDef accessory, StrandDef def, PetPose pose,
        Vector2 anchorBase, float ds, Palette palette, TentacleFx strands)
    {
        if (!manifest.Anchors.ContainsKey("head"))
        {
            return;
        }
        var toCell = manifest.Cell / 256f;
        var (fitScale, fitOffset, _) = manifest.FitFor(accessory.Slot, accessory.Name);
        var seat = manifest.AnchorFor("head", pose)
                   + new Vector2(0f, WornStrandSink * toCell)
                   + (fitOffset * toCell);
        DrawStrandFan(drawList, manifest, def, pose, anchorBase, ds, palette, strands, seat, 0f,
            toCell * fitScale);
    }

    /// <summary>The shell's OWN strand fan (tendrils, legs, antennae), sown on the anchor the
    /// manifest names, seated as deep as the shell's waist so the roots finish inside the
    /// silhouette.</summary>
    public static void DrawShellStrands(
        ImDrawListPtr drawList, AtlasManifest manifest, StrandDef def, PetPose pose,
        Vector2 anchorBase, float ds, Palette palette, TentacleFx strands)
    {
        var seat = manifest.AnchorFor(def.Seat, pose);
        DrawStrandFan(drawList, manifest, def, pose, anchorBase, ds, palette, strands, seat,
            manifest.WrapSeat?.Ry ?? 0f);
    }

    /// <summary>The strand rig's fan. Ink over every segment of every strand FIRST, fill over all of it
    /// second, so the chain reads as one outlined appendage rather than N capsules with seams down the
    /// joins; a third pass lays the bright edge along the lit side, fading out towards the tip. Geometry is
    /// built in cell space and every knot goes through <see cref="ToScreen"/>, so flip and squash are
    /// inherited rather than reimplemented.</summary>
    private static void DrawStrandFan(
        ImDrawListPtr drawList, AtlasManifest manifest, StrandDef def, PetPose pose,
        Vector2 anchorBase, float ds, Palette palette, TentacleFx strands, Vector2 seat, float seatDepth,
        float sizeScale = 1f)
    {
        var scale = pose.Scale;
        var flipX = pose.FlipX;
        if (def.Count <= 0 || def.Len <= 0f || def.Root <= 0f)
        {
            return;
        }
        if (!StrandInks(manifest, palette, out var fill, out var rim, out var outline))
        {
            return;
        }

        strands.Build(def, seat, seatDepth, sizeScale);
        if (strands.Strands <= 0)
        {
            return;
        }

        // Radii take the mean of the two squash axes, as the hand ball does: a mid-boop pinch should thin
        // the strands, not leave them poking out of a flattened body at full width.
        var radiusScale = (scale.X + scale.Y) * 0.5f * ds;
        if (radiusScale <= 0f)
        {
            return;
        }

        var outlineT = MathF.Max(1.1f, def.Root * 0.26f) * radiusScale;
        var bulb = strands.Bulb * radiusScale;

        Vector2 Knot(int strand, int knot) =>
            ToScreen(manifest, strands.PointAt(strand, knot), scale, flipX, anchorBase, ds);
        float Radius(int strand, int knot) => strands.RadiusAt(strand, knot) * radiusScale;

        void Chain(float pad, uint colour)
        {
            for (var i = 0; i < strands.Strands; i++)
            {
                for (var s = 0; s < strands.Knots - 1; s++)
                {
                    var ra = Radius(i, s) + pad;
                    var rb = Radius(i, s + 1) + pad;
                    if (ra < 0.3f && rb < 0.3f)
                    {
                        continue;
                    }
                    AddCapsule(drawList, Knot(i, s), MathF.Max(0.1f, ra), Knot(i, s + 1), MathF.Max(0.1f, rb), colour);
                }
            }
        }

        void Tips(float pad, uint colour)
        {
            if (bulb <= 0.3f)
            {
                return;
            }
            for (var i = 0; i < strands.Strands; i++)
            {
                drawList.AddCircleFilled(Knot(i, strands.Knots - 1), bulb + pad, colour, 20);
            }
        }

        Chain(outlineT, outline);
        Tips(outlineT, outline);
        Chain(0f, fill);
        Tips(0f, fill);

        var keep = Math.Max(2, (int)MathF.Round(strands.Knots * 0.62f));
        for (var i = 0; i < strands.Strands; i++)
        {
            for (var s = 0; s < keep - 1; s++)
            {
                var fade = 1f - (s / (float)keep);
                var fadeNext = 1f - ((s + 1) / (float)keep);
                var ra = Radius(i, s) * 0.20f * (0.40f + (0.60f * fade));
                var rb = Radius(i, s + 1) * 0.20f * (0.40f + (0.60f * fadeNext));
                if (ra < 0.35f && rb < 0.35f)
                {
                    continue;
                }
                AddCapsule(drawList, RimOffset(i, s), MathF.Max(0.1f, ra), RimOffset(i, s + 1), MathF.Max(0.1f, rb), rim);
            }
        }

        Vector2 RimOffset(int strand, int knot)
        {
            // Nudged in CELL space, before the transform, so the highlight swaps sides with the creature.
            var off = strands.RadiusAt(strand, knot) * 0.52f;
            return ToScreen(manifest, strands.PointAt(strand, knot) - new Vector2(off * 0.80f, off * 0.16f),
                scale, flipX, anchorBase, ds);
        }
    }

    /// <summary>The strand rig's three inks, the sheet's own greys under the body tint: a mid fill, a
    /// bright rim and the manifest's line colour, all carrying the body layer's alpha.</summary>
    private static bool StrandInks(AtlasManifest manifest, Palette palette, out uint fill, out uint rim, out uint outline) =>
        SheetGreys(manifest, palette, out fill, out rim, out outline) > 0f;

    /// <summary>The three colours every code-drawn part is made of, derived from the sheet's own
    /// arithmetic: fill grey 190 with a 238 bright rim inside the edge, times the body tint, and
    /// the style's own ink for the dark line. <paramref name="fillValue"/> is the one thing a
    /// caller may argue with, and only one does: a limb drawn in FRONT of the body is the
    /// surface nearer the light, so it is filled a shade above the sheet's 190. Returns the body
    /// alpha the caller must respect; zero means draw nothing at all.</summary>
    private static float SheetGreys(
        AtlasManifest manifest, Palette palette, out uint fill, out uint rim, out uint outline,
        float fillValue = 190f / 255f)
    {
        var tint = palette.BodyColor;
        var layerAlpha = 1f;
        foreach (var layer in manifest.Layers)
        {
            if (layer.Role == TintRole.Body)
            {
                layerAlpha = layer.Alpha;
                break;
            }
        }
        var alpha = tint.W * layerAlpha;
        fill = rim = outline = 0;
        if (alpha <= 0f)
        {
            return 0f;
        }

        uint Grey(float value) => ImGui.ColorConvertFloat4ToU32(
            new Vector4(tint.X * value, tint.Y * value, tint.Z * value, alpha));
        fill = Grey(fillValue);
        rim = Grey(238f / 255f);

        var ink = manifest.InkFor(tint);
        if (ink.W <= 0f)
        {
            ink = MouthDraw.DefaultLine;
        }
        outline = ImGui.ColorConvertFloat4ToU32(ink with { W = ink.W * alpha });
        return alpha;
    }

    /// <summary>Knots a curved limb is built from. Fourteen spans is what the bench drew at, and
    /// the curve is a single cubic: below about ten the bow reads as a bent stick, above about
    /// twenty the smoothness does not survive the delivery size.</summary>
    private const int LimbKnots = 15;

    /// <summary>The front-of-body blend, as five numbers in one place. Every one answers the
    /// same objection, that a code shape over sheet art reads as an overlay: the shadow says the
    /// limb is above the surface; the lighter fill and the lit edge say it is nearer the light
    /// than the body it crosses; the thinner ink stops the limb out-inking the silhouette.</summary>
    private const float FrontShadowOffset256 = 2.6f;

    /// <summary>The cast shadow is a touch fatter than the limb, as a soft shadow is.</summary>
    private const float FrontShadowSpread = 1.14f;

    /// <summary>How dark the occlusion gets, as a fraction of the sheet's own ink. Low: contact
    /// shading on a flat-art creature, not a drop shadow on a button.</summary>
    private const float FrontShadowAlpha = 0.34f;

    /// <summary>The limb's ink relative to the behind-the-body weight. A limb drawn over the
    /// face at the silhouette's own line weight reads as a sticker however well it is shaded.</summary>
    private const float FrontOutlineScale = 0.6f;

    /// <summary>The fill a limb in front is drawn at, against the sheet's own 190. Lighter, not
    /// darker: the limb is the thing casting the shadow, so it is the nearer surface.</summary>
    private const float FrontFillGrey = 204f / 255f;

    /// <summary>The shoulder end of a limb drawn in front, replacing the row's own root, as a
    /// fraction of the hand ball. Growing the root out to the nub means the limb's own shoulder
    /// covers the baked nub exactly and the silhouette becomes one continuous thing. Behind the
    /// body the row's own 10 is right; this is the one number the two orders disagree about.</summary>
    private const float FrontJointScale = 0.91f;

    /// <summary>How many knots the limb's ink is faded in over at the shoulder, in front mode.
    /// At full weight the outline closes around the root, and a closed outline is the definition
    /// of a separate object; ramped, the line starts where the arm leaves the shoulder and
    /// merges into the body's own.</summary>
    private const int FrontRootFadeKnots = 4;

    /// <summary>How many directions the hand's silhouette is sampled in. A triangle fan, so
    /// forty-four is smooth at delivery size and cheap.</summary>
    private const int TipSamples = 44;

    /// <summary>The arc of the hand's edge that gets NO bright rim, screen-space radians.
    /// Measured off the wispv2 nub's own edge: the rim runs nearly all the way round and falls
    /// back to the fill only down and down-toward-the-body.</summary>
    private const float TipRimGapFrom = 55f * MathF.PI / 180f;

    private const float TipRimGapTo = 135f * MathF.PI / 180f;

    /// <summary>One limb (the Reaching's arm): the limited-length row between the shoulder pin
    /// and the hand, the manifest's own <see cref="HandStyleDef"/> row. <paramref name="front"/>
    /// draws it over the body instead of under it, with the blend the constants above describe;
    /// the two orders are otherwise the same code, so a shell can move between them without its
    /// arm changing shape.</summary>
    public static void DrawLimb(
        ImDrawListPtr drawList, AtlasManifest manifest, PetPose pose, Vector2 anchorBase, float ds,
        Palette palette, string anchor, HandFx hands, bool front)
    {
        if (!hands.TryGet(anchor, out var offset256, out var tilt))
        {
            return;
        }

        var style = manifest.HandStyle;
        var alpha = SheetGreys(manifest, palette, out var fill, out var rim, out var outline,
            front ? FrontFillGrey : 190f / 255f);
        if (alpha <= 0f)
        {
            return;
        }

        var toCell = manifest.Cell / 256f;
        var pin = manifest.AnchorFor(anchor, pose);

        // Squash scales positions through ToScreen; radii take the mean of the two axes, so a
        // mid-spin pinch shrinks the limb rather than leaving it poking out at full width.
        var scale = (pose.Scale.X + pose.Scale.Y) * 0.5f;
        var unit = toCell * ds * scale;
        if (style.Hand * unit < 0.75f)
        {
            return;
        }

        // Outboard space: +x away from the centre line, exactly as the tracks are authored. The
        // mirror happens on the way to the screen, once.
        var side = anchor == HandFx.LeftAnchor ? -1f : 1f;

        Span<Vector2> shape = stackalloc Vector2[LimbKnots];
        Span<float> girth = stackalloc float[LimbKnots];
        var knots = BuildLimb(style, front ? style.Hand * FrontJointScale : style.Root,
            new Vector2(offset256.X * side, offset256.Y), shape, girth);

        Span<Vector2> pts = stackalloc Vector2[LimbKnots];
        Span<float> radii = stackalloc float[LimbKnots];
        for (var i = 0; i < knots; i++)
        {
            var local = pin + (new Vector2(shape[i].X * side, shape[i].Y) * toCell);
            pts[i] = ToScreen(manifest, local, pose.Scale, pose.FlipX, anchorBase, ds);
            radii[i] = MathF.Max(0.1f, girth[i] * unit);
        }

        // The boundary normal at each knot, from the AVERAGED direction either side of it, so
        // consecutive quads share an edge exactly rather than overlapping; see Ribbon.
        Span<Vector2> normal = stackalloc Vector2[LimbKnots];
        for (var i = 0; i < knots; i++)
        {
            var seg = Normalise(
                pts[Math.Min(i + 1, knots - 1)] - pts[Math.Max(i - 1, 0)],
                Vector2.UnitY);
            normal[i] = new Vector2(-seg.Y, seg.X);
        }

        var hand = pts[knots - 1];
        var r = style.Hand * unit;
        var outlineT = 3.7f * unit * (front ? FrontOutlineScale : 1f);
        var rimT = MathF.Max(1f, 2.2f * unit);
        var fade = front ? Math.Min(FrontRootFadeKnots, knots - 1) : 0;

        // The tip's frame: the limb's own end tangent in SCREEN space, so the flip is already in
        // it. `mirror` is which way outboard points on screen once both the hand and the flip
        // have had their say; the digits have to fan away from the body on both sides.
        var mirror = (side < 0f ? -1f : 1f) * (pose.FlipX ? -1f : 1f);
        var tangent = Normalise(hand - pts[knots - 2], new Vector2(mirror, 0f));
        var lean = tilt * mirror;
        if (lean != 0f)
        {
            var c = MathF.Cos(lean);
            var sn = MathF.Sin(lean);
            tangent = new Vector2((tangent.X * c) - (tangent.Y * sn), (tangent.X * sn) + (tangent.Y * c));
        }

        var across = new Vector2(-tangent.Y, tangent.X) * mirror;

        if (front)
        {
            // The occlusion, first and under everything: the limb again, a little fatter, a
            // little down and to the right of itself, in the sheet's own ink at low alpha. Over
            // the body it is a cast shadow; over the background the same shape reads as the
            // lower-right side of the outline thickening. One shape, both reads.
            var drop = new Vector2(FrontShadowOffset256, FrontShadowOffset256) * unit;
            var inkColour = manifest.InkFor(palette.BodyColor);
            if (inkColour.W <= 0f)
            {
                inkColour = MouthDraw.DefaultLine;
            }
            var shadow = ImGui.ColorConvertFloat4ToU32(inkColour with { W = inkColour.W * alpha * FrontShadowAlpha });
            Ribbon(drawList, pts, normal, radii, knots, drop, (FrontShadowSpread - 1f) * r, fade, shadow);
            TipHull(drawList, style.Tip, hand + drop, tangent, across, r, (FrontShadowSpread - 1f) * r, shadow);
        }

        // Ink over the whole limb FIRST, fill over all of it second, so arm and hand compose as
        // ONE outlined limb rather than a chain of capsules with seams down the joins.
        Ribbon(drawList, pts, normal, radii, knots, Vector2.Zero, outlineT, fade, outline);
        Tip(outlineT, outline);
        Ribbon(drawList, pts, normal, radii, knots, Vector2.Zero, 0f, 0, fill);
        Tip(0f, fill);

        // The bright edge sits just inside the outline and follows the hand's OWN silhouette
        // rather than a circle inscribed in it: a perfect circle inside a shape that is not one
        // is a ring drawn ON the hand instead of light along its edge.
        TipRim(drawList, style.Tip, hand, tangent, across, r, rimT, rim);

        if (!front)
        {
            return;
        }

        // In front, the limb crosses lit body and has to be lit itself or it reads as a hole.
        // The house sun is upper-left; the side is chosen ONCE from the whole limb, because a
        // curve that changes its mind mid-way puts the highlight through the middle of the arm.
        var light = new Vector2(-0.7071f, -0.7071f);
        var lit = 0f;
        for (var i = 0; i < knots - 1; i++)
        {
            var seg = Normalise(pts[i + 1] - pts[i], Vector2.UnitY);
            lit += Vector2.Dot(new Vector2(-seg.Y, seg.X), light);
        }

        // Stops at the wrist, not at the hand's centre: the hand has its own edge light, and a
        // limb highlight run to the last knot ends inside the palm as a stray line across it.
        var litSide = lit >= 0f ? 1f : -1f;
        for (var i = 0; i < knots - 1; i++)
        {
            drawList.PathLineTo(pts[i] + (normal[i] * litSide * MathF.Max(0f, radii[i] - (rimT * 0.6f))));
        }

        drawList.PathStroke(rim, ImDrawFlags.None, rimT * 0.9f);

        void Tip(float pad, uint colour) => TipHull(drawList, style.Tip, hand, tangent, across, r, pad, colour);
    }

    /// <summary>The hand, drawn as ONE silhouette: the union of the palm and its digits, sampled
    /// radially about the palm centre and filled as a triangle fan. Star-shaped by construction
    /// (every digit overlaps the palm), so the radial sample is exact and the fan's triangles
    /// never overlap, which is what keeps a translucent hand the same weight as the creature.</summary>
    private static void TipHull(
        ImDrawListPtr drawList, string tip, Vector2 at, Vector2 tangent, Vector2 across, float r, float pad,
        uint colour)
    {
        Span<Vector2> centre = stackalloc Vector2[4];
        Span<float> radius = stackalloc float[4];
        var count = TipShapes(tip, tangent, across, r, pad, centre, radius);

        var previous = Vector2.Zero;
        for (var s = 0; s <= TipSamples; s++)
        {
            var p = at + TipEdge(centre, radius, count, MathF.Tau * s / TipSamples, 0f);
            if (s > 0)
            {
                drawList.AddTriangleFilled(at, previous, p, colour);
            }

            previous = p;
        }
    }

    /// <summary>The circles a hand is made of, placed and sized: (along the limb, across it,
    /// radius), in units of the hand ball. Every tip keeps the ball as its core, and a tipless
    /// point is deliberately not offered: an accessory pinned to a tendril reads as impaled.</summary>
    private static int TipShapes(
        string tip, Vector2 tangent, Vector2 across, float r, float pad, Span<Vector2> centre, Span<float> radius)
    {
        ReadOnlySpan<float> shapes = tip?.ToLowerInvariant() switch
        {
            "ball" => [0f, 0f, 1f],
            "mitten" => [0f, 0f, 0.94f, 0.55f, -0.72f, 0.42f],
            _ => [0f, 0f, 0.82f, 0.62f, -0.5f, 0.34f, 0.78f, 0.18f, 0.36f, 0.55f, 0.72f, 0.3f],
        };

        var count = shapes.Length / 3;
        for (var i = 0; i < count; i++)
        {
            centre[i] = (tangent * (shapes[i * 3] * r)) + (across * (shapes[(i * 3) + 1] * r));
            radius[i] = (shapes[(i * 3) + 2] * r) + pad;
        }

        return count;
    }

    /// <summary>The hand's bright edge: the same silhouette <see cref="TipHull"/> fills, inset
    /// by the rim's own width and stroked, so the highlight runs along the digits and into the
    /// webs between them. Broken over the shaded arc (<see cref="TipRimGapFrom"/>).</summary>
    private static void TipRim(
        ImDrawListPtr drawList, string tip, Vector2 at, Vector2 tangent, Vector2 across, float r, float rimT,
        uint colour)
    {
        Span<Vector2> centre = stackalloc Vector2[4];
        Span<float> radius = stackalloc float[4];
        var count = TipShapes(tip, tangent, across, r, 0f, centre, radius);

        const int steps = 30;
        var from = TipRimGapTo;
        var span = (MathF.Tau - TipRimGapTo) + TipRimGapFrom;
        for (var s = 0; s <= steps; s++)
        {
            drawList.PathLineTo(at + TipEdge(centre, radius, count, from + (span * s / steps), rimT * 0.6f));
        }

        drawList.PathStroke(colour, ImDrawFlags.None, rimT * 0.9f);
    }

    /// <summary>How far the union's boundary lies from the palm centre along
    /// <paramref name="angle"/>, pulled in by <paramref name="inset"/>. Star-shaped by
    /// construction, so the furthest exit over the circles the ray hits IS the boundary.</summary>
    private static Vector2 TipEdge(
        ReadOnlySpan<Vector2> centre, ReadOnlySpan<float> radius, int count, float angle, float inset)
    {
        var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var reach = 0f;
        for (var i = 0; i < count; i++)
        {
            var toC = centre[i];
            var b = Vector2.Dot(dir, toC);
            var disc = (radius[i] * radius[i]) - Vector2.Dot(toC, toC) + (b * b);
            if (disc >= 0f)
            {
                reach = MathF.Max(reach, b + MathF.Sqrt(disc));
            }
        }

        return dir * MathF.Max(0f, reach - inset);
    }

    /// <summary>One pass of a limb: a ribbon rather than a chain of capsules. Boundary points
    /// come from the averaged direction at each knot, so consecutive quads SHARE an edge instead
    /// of overlapping; on a translucent body a stack of fifteen overlapping shapes composites to
    /// a limb darker than the creature it grows from. Called twice with the same geometry (ink
    /// at <paramref name="pad"/>, fill at zero) so the arm and its outline can never disagree
    /// about where the arm is, and a third time, shifted, for the occlusion shadow.</summary>
    private static void Ribbon(
        ImDrawListPtr drawList, ReadOnlySpan<Vector2> pts, ReadOnlySpan<Vector2> normal,
        ReadOnlySpan<float> radii, int knots, Vector2 shift, float pad, int fade, uint colour)
    {
        // The pad at knot i, ramped in over the first `fade` knots so the ink does not close
        // around the shoulder. Zero fade is the flat pad the behind order wants, where a closed
        // outline is right because the body hides it.
        float PadAt(int i) => fade > 0 ? pad * MathF.Min(1f, i / (float)fade) : pad;

        drawList.AddCircleFilled(pts[0] + shift, radii[0] + PadAt(0), colour, 16);

        for (var i = 0; i < knots - 1; i++)
        {
            var ra = radii[i] + PadAt(i);
            var rb = radii[i + 1] + PadAt(i + 1);
            if (ra < 0.2f && rb < 0.2f)
            {
                continue;
            }

            drawList.PathLineTo(pts[i] + shift + (normal[i] * ra));
            drawList.PathLineTo(pts[i + 1] + shift + (normal[i + 1] * rb));
            drawList.PathLineTo(pts[i + 1] + shift - (normal[i + 1] * rb));
            drawList.PathLineTo(pts[i] + shift - (normal[i] * ra));
            drawList.PathFillConvex(colour);
        }
    }

    /// <summary>The limb's shape, in OUTBOARD 256-space with the pin at the origin: knot
    /// positions and the radius at each. Pure geometry, no screen and no palette. A capsule row
    /// is two knots and the shipped taper, byte for byte; everything else is the pseudopod, a
    /// cubic that leaves the pin along <c>sag</c> and gives in to the target, with any slack
    /// between the arc and the resting length spent as extra bow, then fattened or thinned by
    /// how far it is stretched.</summary>
    private static int BuildLimb(
        HandStyleDef style, float rootRadius, Vector2 target, Span<Vector2> pts, Span<float> radii)
    {
        if (!style.IsCurved || style.Len <= 0f)
        {
            pts[0] = Vector2.Zero;
            radii[0] = rootRadius;
            pts[1] = target;
            radii[1] = style.Wrist;
            return 2;
        }

        // HandFx has already limited the hand to this, so the clamp here is the belt to its
        // braces.
        var max = style.Len * 0.97f;
        var chord = target.Length();
        if (chord > max && chord > 0.0001f)
        {
            target *= max / chord;
            chord = max;
        }

        var rootDir = new Vector2(MathF.Sin(style.Sag), MathF.Cos(style.Sag));
        var rest = style.Len * style.Fill;

        var handle = MathF.Max(3f, chord * style.Bow);
        var arc = Spline(target, rootDir, handle, chord, pts);

        // Spend any slack as extra bow; converges in a pass or two, three is the cap because
        // this is per hand per frame.
        for (var pass = 0; pass < 3; pass++)
        {
            var slack = rest - arc;
            if (slack <= 0.5f)
            {
                break;
            }

            handle += slack * 0.85f;
            arc = Spline(target, rootDir, handle, chord, pts);
        }

        // Volume conservation, faked: contracted fattens, stretched thins. What makes the limb
        // read as flesh rather than rope.
        var stretch = arc / MathF.Max(1f, rest);
        var swell = stretch < 1f
            ? 1f + (style.Swell * (1f - stretch) * 1.6f)
            : 1f - (style.Swell * MathF.Min(0.5f, stretch - 1f) * 0.9f);

        for (var i = 0; i < LimbKnots; i++)
        {
            var u = i / (float)(LimbKnots - 1);
            var belly = 1f + (0.4f * style.Swell * MathF.Sin(MathF.PI * u));
            radii[i] = (rootRadius + ((style.Wrist - rootRadius) * u)) * swell * belly;
        }

        return LimbKnots;
    }

    /// <summary>One cubic from the pin to the hand, sampled into <paramref name="pts"/>, and its
    /// arc length back. The first control point holds the root tangent; the second aims back
    /// down the approach so the hand is entered along the limb rather than side-on.</summary>
    private static float Spline(Vector2 target, Vector2 rootDir, float handle, float chord, Span<Vector2> pts)
    {
        var p1 = rootDir * handle;
        var aim = Normalise(target - p1, rootDir);
        var p2 = target - (aim * MathF.Max(4f, chord * 0.35f));

        var arc = 0f;
        for (var i = 0; i < LimbKnots; i++)
        {
            var u = i / (float)(LimbKnots - 1);
            var v = 1f - u;
            pts[i] = (3f * v * v * u * p1)
                + (3f * v * u * u * p2)
                + (u * u * u * target);
            if (i > 0)
            {
                arc += (pts[i] - pts[i - 1]).Length();
            }
        }

        return arc;
    }

    private static Vector2 Normalise(Vector2 v, Vector2 fallback)
    {
        var len = v.Length();
        return len > 0.0001f ? v / len : fallback;
    }

    // The flown item's inks, matched to the sprite's own palette (draw_summer2.py) so the
    // code-drawn lines and the baked sail read as one item. Packed ABGR.
    private const uint KiteInk = 0xFF3C272E;
    private const uint KiteCoral = 0xFF566EEB;
    private const uint KiteCoralShade = 0xFF4840B0;

    /// <summary>
    /// A flown item (fx: "kite"): the sail quad riding the sim, with both of its strings inked
    /// in code, the flying line from the pin to the mooring point and the bowed tail below it.
    /// Everything is authored in the accessory's own 256-space relative to the pin, unflipped,
    /// and comes to the screen through one lambda that mirrors and scales about the pin. This
    /// engine has no drawn limbs, so the pin is the anchor itself; there is no hand ride and no
    /// fit tilt to add.
    /// </summary>
    public static void DrawKite(
        ImDrawListPtr drawList, ImTextureID tex, AccessoryDef accessory, AtlasManifest manifest,
        Vector2 screenAnchor, float quadScale, bool flipX, KiteFx rig)
    {
        if (quadScale <= 0f || accessory.Width <= 0 || accessory.Height <= 0)
        {
            return;
        }

        var fs = flipX ? -1f : 1f;
        Vector2 P(Vector2 p256) => screenAnchor + (new Vector2(p256.X * fs, p256.Y) * quadScale);

        var bridle = accessory.FxBridlePoint;
        var moor = bridle + rig.Offset;

        // The flying line, under everything as the baked one was: a quadratic from the pin to
        // the moved mooring point. The sag is the slack made visible: a sail blown towards the
        // hand shortens the chord and the line bellies; a sail dragged away pulls it straight.
        var sag = Math.Clamp(2.0f + ((bridle.Length() - moor.Length()) * 0.6f), 1.0f, 7.5f);
        var mid = (moor * 0.5f) + new Vector2(0f, sag);
        for (var i = 0; i <= 12; i++)
        {
            var u = i / 12f;
            drawList.PathLineTo(P((mid * 2f * (1f - u) * u) + (moor * u * u)));
        }

        drawList.PathStroke(KiteInk, ImDrawFlags.None, MathF.Max(1f, 1.0f * quadScale));

        // The tail, rooted at the moved moor so it rides the sail, a shade heavier than the
        // flying line: it is a ribbon, not a tether.
        for (var i = 0; i < KiteFx.TailKnots; i++)
        {
            drawList.PathLineTo(P(moor + rig.TailAt(i)));
        }

        drawList.PathStroke(KiteInk, ImDrawFlags.None, MathF.Max(1f, 1.3f * quadScale));

        // The bows, at the baked art's own stations, turned to the tail's local direction so
        // they ride the wave rather than hovering beside it. Fill, shade, then ink, exactly as
        // the generator layered them.
        foreach (var b in (ReadOnlySpan<int>)[0, 1, 2])
        {
            var seat = moor + rig.BowAt(b, out var bowAngle);
            var ca = MathF.Cos(bowAngle);
            var sa = MathF.Sin(bowAngle);
            Vector2 Bow(float along, float outw) =>
                P(seat + new Vector2((along * ca) - (outw * sa), (along * sa) + (outw * ca)));

            drawList.PathLineTo(Bow(4.4f, -1.2f));
            drawList.PathLineTo(Bow(1.2f, 3.8f));
            drawList.PathLineTo(Bow(-3.8f, -4.2f));
            drawList.PathLineTo(Bow(-1.2f, -4.6f));
            drawList.PathFillConvex(KiteCoral);
            drawList.PathLineTo(Bow(4.4f, -1.2f));
            drawList.PathLineTo(Bow(1.2f, 3.8f));
            drawList.PathLineTo(Bow(0.4f, -0.6f));
            drawList.PathFillConvex(KiteCoralShade);
            drawList.PathLineTo(Bow(4.4f, -1.2f));
            drawList.PathLineTo(Bow(1.2f, 3.8f));
            drawList.PathLineTo(Bow(-3.8f, -4.2f));
            drawList.PathLineTo(Bow(-1.2f, -4.6f));
            drawList.PathStroke(KiteInk, ImDrawFlags.Closed, MathF.Max(1f, 0.9f * quadScale));
        }

        // The sail last, over both line ends as the baked layering had it: the quad's corners
        // carried into pin space, displaced by the sim and yawed about the moor, the one point
        // the string holds still.
        var origin = accessory.OriginPoint;
        var sinF = MathF.Sin(rig.Tilt);
        var cosF = MathF.Cos(rig.Tilt);
        Vector2 Sail(float cx, float cy)
        {
            var d = new Vector2(cx - origin.X, cy - origin.Y) + rig.Offset - moor;
            return P(moor + new Vector2((d.X * cosF) - (d.Y * sinF), (d.X * sinF) + (d.Y * cosF)));
        }

        var inset = new Vector2(0.5f / accessory.Width, 0.5f / accessory.Height);
        var c0 = Sail(0f, 0f);
        var c1 = Sail(accessory.Width, 0f);
        var c2 = Sail(accessory.Width, accessory.Height);
        var c3 = Sail(0f, accessory.Height);
        drawList.AddImageQuad(
            tex, c0, c1, c2, c3,
            inset,
            new Vector2(1f - inset.X, inset.Y),
            new Vector2(1f - inset.X, 1f - inset.Y),
            new Vector2(inset.X, 1f - inset.Y),
            0xFFFFFFFF);

        // The recorder speaks axis-aligned quads, so a selfie keeps the sail at its bounding
        // box (the yaw is clamped to a tenth of a turn and does not read at keepsake size) and
        // the strings as plain strokes.
        if (PetFrameRecorder.Recording)
        {
            var min = Vector2.Min(Vector2.Min(c0, c1), Vector2.Min(c2, c3));
            var max = Vector2.Max(Vector2.Max(c0, c1), Vector2.Max(c2, c3));
            PetFrameRecorder.Add(_recorderKitePath, min, max,
                new Vector2(flipX ? 1f - inset.X : inset.X, inset.Y),
                new Vector2(flipX ? inset.X : 1f - inset.X, 1f - inset.Y),
                0xFFFFFFFF);
        }
    }

    /// <summary>The kite sprite's path for the selfie recorder, parked by the caller before the
    /// draw because this class never learns the asset root.</summary>
    private static string _recorderKitePath = string.Empty;

    public static void SetRecorderKitePath(string path) => _recorderKitePath = path;
}
