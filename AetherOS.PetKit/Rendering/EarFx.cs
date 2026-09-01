namespace AetherOS.PetKit.Rendering;

using System;

using AetherOS.PetKit.Engine;

/// <summary>
/// Both ears' animation stack: an ambient plan of small business, plus mood overrides.
/// Transcribed from the tuning bench (art-intake/ears-tails-rig, <c>rig_motion.py</c>).
///
/// <para><b>Ears are not strands, and this is not <see cref="TailFx"/> with different
/// numbers.</b> A tail is a spine carrying a travelling wave; an ear is a sprung rigid shape
/// that SNAPS to a new angle and settles. The two stacks were written apart on purpose, and
/// that seam is why either can be retuned without the other flinching.</para>
///
/// <para><b>It has never seen an ear's outline.</b> This class answers a pose per ear; angle,
/// scale, bend; and the model decides what to spend it on: a pointy fox ear spends the bend
/// on almost nothing, a long rabbit ear spends it curving its tip, and both are driven by the
/// same numbers.</para>
///
/// <para>The plan is scheduled on the same irrational clock everything else here uses, so the
/// ears never repeat a sequence and never act in a rhythm. Which ear acts, what it does, and
/// how long until the next one are all hashes of the action's index.</para>
/// </summary>
public sealed class EarFx
{
    /// <summary>How hard the tip lags the base. The bend a model spends on its own floppiness
    /// comes out of this one lag: a rigid fox ear turns as a piece, a lop's ear turns and its
    /// tip arrives late, and neither needs its own code path; only its own
    /// <see cref="EarPartDef.Floppy"/>.</summary>
    private const float LagRate = 11f;

    /// <summary>The ambient repertoire. Four is enough that a watcher never predicts the
    /// next one, and few enough that each is recognisable when it happens.</summary>
    private enum Action
    {
        Twitch,
        Flick,
        Perk,
        Swivel,
    }

    private readonly float[] angle = new float[2];
    private readonly float[] scale = [1f, 1f];
    private readonly float[] lag = new float[2];
    private readonly PartDrag drag = new();

    private float clock;
    private float nextAt = 1.2f;
    private int index;

    /// <summary>Where the clock folds back to zero, and the one number in this class that two
    /// fields have to agree about. See <see cref="Update"/>.</summary>
    private const float ClockWrap = 3600f;

    /// <summary>Advances the plan, the lag and the drag. Reduce-motion parks all three where
    /// they stand, the same contract every rig here honours.</summary>
    public void Update(float dt, bool reduceMotion, EarMood mood, float? seatX)
    {
        if (reduceMotion || dt <= 0f)
        {
            return;
        }

        // The clock wraps, and the schedule has to wrap with it. `nextAt` is an absolute time on
        // this clock rather than a countdown, so a modulo that moved one and not the other broke
        // the invariant the plan rests on: after an hour the clock returned to zero, nextAt
        // stayed near 3600, Plan stopped advancing, and the elapsed time went to minus an hour,
        // which the unbounded shaping curves turned into ears spinning by thousands of degrees.
        this.clock += dt;
        if (this.clock >= ClockWrap)
        {
            this.clock -= ClockWrap;
            this.nextAt -= ClockWrap;
        }

        this.drag.Update(dt, seatX);

        this.Plan(mood, out var a0, out var a1, out var s0, out var s1);

        // The head's own yank reaches both ears together, like the tail's trail: a body that
        // jumps left leaves both ears behind, it does not open them like scissors.
        var yank = this.drag.Lean * (180f / MathF.PI) * 0.35f;
        this.angle[0] = a0 + yank;
        this.angle[1] = a1 + yank;
        this.scale[0] = s0;
        this.scale[1] = s1;

        // One-pole lag per ear. Where the bend comes from, and free: the gap between where an
        // ear IS and where it is going is exactly what a floppy tip does.
        var k = MathF.Min(1f, dt * LagRate);
        for (var i = 0; i < 2; i++)
        {
            this.lag[i] += (this.angle[i] - this.lag[i]) * k;
        }
    }

    /// <summary>One ear's pose. <paramref name="ear"/> is 0 for the creature's left, 1 for its
    /// right. Bend is the base-to-tip lag in the same degrees; a model multiplies it by its own
    /// floppiness and may ignore it entirely.</summary>
    public void Sample(int ear, out float degrees, out float scale, out float bend)
    {
        var i = Math.Clamp(ear, 0, 1);
        degrees = this.angle[i];
        scale = this.scale[i];
        bend = this.angle[i] - this.lag[i];
    }

    private void Plan(EarMood mood, out float a0, out float a1, out float s0, out float s1)
    {
        if (mood == EarMood.Alert)
        {
            (a0, a1, s0, s1) = (2f, 2f, 1.06f, 1.06f);
            return;
        }

        if (mood == EarMood.Sleepy)
        {
            (a0, a1, s0, s1) = (34f, 34f, 0.92f, 0.92f);
            return;
        }

        var t = this.clock;
        while (t >= this.nextAt)
        {
            this.index++;
            this.nextAt += 1.6f + (3.4f * PartDrag.Hash(this.index, 3f));
        }

        var k = this.index;
        // Never negative: see the note in Update. An action that has not started yet reads as
        // one that has just started, which is a frame of stillness rather than a spin.
        var dt = MathF.Max(0f, t - (this.nextAt - (1.6f + (3.4f * PartDrag.Hash(k, 3f)))));
        var act = (Action)(int)(PartDrag.Hash(k, 11f) * 4f);
        var which = PartDrag.Hash(k, 17f) < 0.5f ? 0 : 1;
        a0 = a1 = 0f;
        s0 = s1 = 1f;

        switch (act)
        {
            case Action.Twitch when dt < 0.30f:
            {
                // A shiver in one ear, damped out. The smallest thing an ear does, and the
                // one that does most of the work of looking alive.
                var v = 9f * MathF.Sin(MathF.Tau * 10f * dt) * (1f - (dt / 0.30f));
                if (which == 0)
                {
                    a0 = v;
                }
                else
                {
                    a1 = v;
                }

                break;
            }

            case Action.Flick:
            {
                // Out fast, back slow, with a little overshoot on the way home.
                var v = 0f;
                if (dt < 0.12f)
                {
                    v = 32f * (dt / 0.12f);
                }
                else if (dt < 0.42f)
                {
                    var p = (dt - 0.12f) / 0.30f;
                    v = 32f * (1f - p) * (1f + (0.35f * MathF.Sin(MathF.PI * p)));
                }

                if (which == 0)
                {
                    a0 = v;
                }
                else
                {
                    a1 = v;
                }

                break;
            }

            case Action.Perk:
            {
                // Both up, taller, held a beat, then relaxed; the "what was that?" pose.
                float v;
                if (dt < 0.10f)
                {
                    v = dt / 0.10f;
                }
                else if (dt < 1.0f)
                {
                    v = 1f + (0.12f * MathF.Sin(MathF.Tau * 1.4f * (dt - 0.10f))
                                    * MathF.Exp(-3f * (dt - 0.10f)));
                }
                else if (dt < 1.35f)
                {
                    v = 1f - ((dt - 1.0f) / 0.35f);
                }
                else
                {
                    v = 0f;
                }

                a0 = a1 = -14f * v;
                s0 = s1 = 1f + (0.07f * v);
                break;
            }

            case Action.Swivel when dt < 0.9f:
            {
                // The ears part company: one forward, one back, listening two ways at once.
                var v = 12f * MathF.Min(1f, dt / 0.15f) * (dt < 0.7f ? 1f : (0.9f - dt) / 0.2f);
                a0 = v;
                a1 = -v;
                break;
            }
        }
    }
}
