using System;
using System.Numerics;
using AetherOS.Apps.Aetherling.Engine;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Rendering;

/// <summary>
/// The code-drawn parts (ears, tails) and the flown kite, transcribed from the prototype's
/// PetDraw (aetherlove-aetherling, merge 45af73b) onto this engine's draw arithmetic. Every
/// tuning number and every hard-won rule in here is the prototype's; the only adaptations are
/// the pose plumbing (this engine passes cellIndex/scale/offset/flipX loose rather than a
/// PetPose) and the absence of drawn limbs (a kite pin rides the anchor, never a hand's reach).
/// </summary>
internal static class PartsDraw
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
        ImDrawListPtr drawList, AtlasManifest manifest, AccessoryDef accessory, int cellIndex,
        Vector2 scale, bool flipX, Vector2 anchorBase, float ds, Palette palette, PartsRig parts)
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

        var toCell = manifest.Cell / 256f;
        var knots = Math.Clamp(def.Segs + 1, 2, MaxTailKnots);
        var deltas = parts.Tail.Deltas(knots, def.Response);

        // The SMOOTHED seat where the rig carries one, the raw anchor from Rest. The tail is the
        // one part whose seat glides: its root is buried deep enough inside the silhouette that
        // the glide never shows as a gap, and without it the largest thing on screen translates
        // in clip-rate steps. The ears stay on raw anchors, where a glide reads as detachment.
        var seat = (parts.TailSeat ?? manifest.AnchorForCell("tail", cellIndex))
                   + (def.NudgePoint * toCell);
        var segLen = def.Len * toCell / (knots - 1);

        Span<Vector2> spine = stackalloc Vector2[MaxTailKnots];
        Span<float> radii = stackalloc float[MaxTailKnots];
        Span<Vector2> left = stackalloc Vector2[MaxTailKnots];
        Span<Vector2> right = stackalloc Vector2[MaxTailKnots];

        var p = seat;
        spine[0] = p;
        radii[0] = def.RadiusAt(0f) * toCell;
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
            radii[i] = def.RadiusAt(u) * toCell;
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
        ImDrawListPtr drawList, AtlasManifest manifest, AccessoryDef accessory, int cellIndex,
        Vector2 scale, bool flipX, Vector2 anchorBase, float ds, Palette palette, PartsRig parts)
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

        var toCell = manifest.Cell / 256f;
        var hw = def.BaseHalfWidth * toCell;
        var h = def.Height * toCell;

        Span<Vector2> shape = stackalloc Vector2[EarOutlinePoints];
        Span<Vector2> smooth = stackalloc Vector2[EarOutlinePoints];

        for (var ear = 0; ear < 2; ear++)
        {
            var side = ear == 0 ? -1f : 1f;
            parts.Ears.Sample(ear, out var degrees, out var earScale, out var bend);
            var seat = manifest.AnchorForCell(ear == 0 ? "earL" : "earR", cellIndex)
                       + new Vector2(def.NudgePoint.X * side * toCell, def.NudgePoint.Y * toCell);

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

        var line = manifest.LineColor.Length > 0 ? Palette.ParseHex(manifest.LineColor) : default;
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

    /// <summary>A strand fan worn on the ears slot (the Antennae), sown just under the head pin.</summary>
    public static void DrawEarStrands(
        ImDrawListPtr drawList, AtlasManifest manifest, StrandDef def, int cellIndex, Vector2 scale, bool flipX,
        Vector2 anchorBase, float ds, Palette palette, TentacleFx strands)
    {
        if (!manifest.Anchors.ContainsKey("head"))
        {
            return;
        }
        var seat = manifest.AnchorForCell("head", cellIndex) + new Vector2(0f, WornStrandSink * (manifest.Cell / 256f));
        DrawStrandFan(drawList, manifest, def, cellIndex, scale, flipX, anchorBase, ds, palette, strands, seat, 0f);
    }

    /// <summary>The strand rig's fan. Ink over every segment of every strand FIRST, fill over all of it
    /// second, so the chain reads as one outlined appendage rather than N capsules with seams down the
    /// joins; a third pass lays the bright edge along the lit side, fading out towards the tip. Geometry is
    /// built in cell space and every knot goes through <see cref="ToScreen"/>, so flip and squash are
    /// inherited rather than reimplemented.</summary>
    private static void DrawStrandFan(
        ImDrawListPtr drawList, AtlasManifest manifest, StrandDef def, int cellIndex, Vector2 scale, bool flipX,
        Vector2 anchorBase, float ds, Palette palette, TentacleFx strands, Vector2 seat, float seatDepth)
    {
        if (def.Count <= 0 || def.Len <= 0f || def.Root <= 0f)
        {
            return;
        }
        if (!StrandInks(manifest, palette, out var fill, out var rim, out var outline))
        {
            return;
        }

        strands.Build(def, seat, seatDepth);
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
    private static bool StrandInks(AtlasManifest manifest, Palette palette, out uint fill, out uint rim, out uint outline)
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
            return false;
        }

        uint Grey(float value) => ImGui.ColorConvertFloat4ToU32(
            new Vector4(tint.X * value, tint.Y * value, tint.Z * value, alpha));
        fill = Grey(190f / 255f);
        rim = Grey(238f / 255f);

        var ink = manifest.LineColor.Length > 0 ? Palette.ParseHex(manifest.LineColor) : default;
        if (ink.W <= 0f)
        {
            ink = MouthDraw.DefaultLine;
        }
        outline = ImGui.ColorConvertFloat4ToU32(ink with { W = ink.W * alpha });
        return true;
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
