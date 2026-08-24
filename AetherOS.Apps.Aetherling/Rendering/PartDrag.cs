namespace AetherOS.Apps.Aetherling.Rendering;

using System;
using System.Numerics;

/// <summary>
/// The body-drag spring shared by <see cref="TailFx"/> and <see cref="EarFx"/>, and the
/// deterministic scatter both use for their irregularity.
///
/// <para><b>Transcribed from <see cref="TentacleFx"/>, constant for constant, and not
/// re-tuned.</b> A hanging appendage dragged about by a hopping body is a problem the strand
/// rig already solved; solving it a second time with different numbers would read as
/// different physics per body part on the same creature. The argument in full lives in that
/// class's own doc comments, and the short of it is that the seat is a per-CELL anchor — it
/// holds still for seven render frames on an 8 fps clip and then jumps. Differentiate that
/// and you get an impulse train whose height is the render framerate; a follow point chasing
/// a step just moves smoothly to it, and the gap between them IS the drag.</para>
///
/// <para>Both parts hang off a body that hops, so both take the same spring with a different
/// seat: the tail trails the whole body, the ears trail the head.</para>
/// </summary>
public sealed class PartDrag
{
    private const float Stiffness = 64f;

    private const float Damping = 5.6f;

    private const float PerPixel = 0.022f;

    private const float Max = 0.78f;

    /// <summary>Fixed substep, so the swing is a property of the pet rather than of the
    /// graphics card — an explicit integrator's error goes with the step.</summary>
    private const float Step = 1f / 120f;

    /// <summary>A dropped frame, or a phone that spent a minute minimised, hands us a delta
    /// measured in seconds. There is nothing worth simulating in it.</summary>
    private const float MaxCatchUp = 0.1f;

    private float follow;

    private float velocity;

    private bool following;

    /// <summary>The lag, in radians of lean. Read by the stacks, written only here.</summary>
    public float Lean { get; private set; }

    public void Update(float dt, float? seatX)
    {
        float now;
        if (seatX is not { } seat)
        {
            // No body this frame. Let the point catch up and the part come back to rest
            // rather than freezing it mid-swing, and forget where it was — the next seat
            // handed in may belong to an entirely different shell.
            this.following = false;
            now = this.follow;
        }
        else
        {
            if (!this.following)
            {
                // First sight of a body: start ON it. Chasing in from wherever the last
                // shell's seat happened to be would open the scene with a swing nothing
                // in it caused.
                this.follow = seat;
                this.velocity = 0f;
                this.following = true;
            }

            now = seat;
        }

        for (var remaining = MathF.Min(dt, MaxCatchUp); remaining > 0f; remaining -= Step)
        {
            var step = MathF.Min(remaining, Step);
            this.velocity += (((now - this.follow) * Stiffness) - (this.velocity * Damping)) * step;
            this.follow += this.velocity * step;
        }

        this.Lean = Math.Clamp((now - this.follow) * PerPixel, -Max, Max);
    }

    /// <summary>
    /// Deterministic per-index scatter: the golden ratio's fractional part and the plastic
    /// number's, the strand rig's own two irrationals, so neighbouring indices never line up.
    ///
    /// <para>A hash rather than an RNG, and deliberately: the same part is built several
    /// times a frame (three surfaces, plus a footprint measurement) and must come out
    /// identical every time, and a tuning judgement made today must be the same performance
    /// tomorrow. A seeded generator would need state and an ordering guarantee to manage
    /// that; a hash of the index needs neither.</para>
    /// </summary>
    public static float Hash(int index, float salt = 0f)
    {
        var v = (index * 0.61803399f) + (salt * 0.75487767f);
        return v - MathF.Floor(v);
    }

    /// <summary>Where a part is sown this frame, as the one number the spring chases: the
    /// anchor's x plus the code-side hop, in the shell's own cell pixels and PRE-FLIP. A
    /// part that trailed the other way when the pet turned round would be the mirror, not
    /// the motion.</summary>
    public static float SeatX(Vector2 anchorCell, Vector2 poseOffset256, float cell) =>
        anchorCell.X + (poseOffset256.X * (cell / 256f));
}
