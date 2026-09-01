namespace AetherOS.PetKit.Engine;

using System.Numerics;
using System.Text.Json.Serialization;

/// <summary>
/// What a mood asks a tail to do. Named rather than numeric so a Reaction or an emote can say
/// what the pet feels without knowing that a tail wags while ears perk; see
/// <see cref="PartMoods"/>, which is the whole vocabulary the rest of the app has to learn.
/// </summary>
public enum TailMood
{
    Idle,
    Swish,
    Wag,
    Swoosh,
    Alert,
    Sleepy,
}

/// <summary>The same, for ears. Deliberately a shorter list: ears have an ambient plan of
/// their own, and a mood's job is only to override it.</summary>
public enum EarMood
{
    Ambient,
    Alert,
    Sleepy,
}

/// <summary>One name for both stacks, so callers pair them without knowing either.</summary>
public static class PartMoods
{
    public static (TailMood Tail, EarMood Ears) For(string mood) => mood switch
    {
        "happy" => (TailMood.Wag, EarMood.Ambient),
        "content" => (TailMood.Swish, EarMood.Ambient),
        "curious" => (TailMood.Swoosh, EarMood.Ambient),
        "alert" => (TailMood.Alert, EarMood.Alert),
        "sleepy" => (TailMood.Sleepy, EarMood.Sleepy),
        _ => (TailMood.Idle, EarMood.Ambient),
    };
}

/// <summary>
/// A tail model: what it IS, with no notion of time anywhere in the record.
///
/// <para><b>Authored in 256-space</b>, like every other piece of accessory art, and scaled to
/// the shell's own cell by the renderer. A record tied to one shell's cell resolution would
/// shrink or swell the first time it was worn by a species with a different sheet.</para>
///
/// <para><b>And it does not know which shell it is on.</b> Placement comes from the shell's own
/// <c>tail</c> anchor, per cell; the record carries only <see cref="Nudge"/> from it. That is
/// the accessory fit table's bargain; the shell says where things attach, the item says only
/// how this one differs; so a new shell that wants tails costs an anchor array and no edit to
/// any model ever written.</para>
/// </summary>
public sealed class TailPartDef
{
    /// <summary>Small correction from the shell's <c>tail</c> anchor, in <see cref="Dir"/>'s own
    /// screen space (y down). Mirrors with the pet, like the rest of the tail.</summary>
    [JsonPropertyName("nudge")]
    public float[] Nudge { get; set; } = [0f, 0f];

    /// <summary>Rest direction, degrees, screen space (y down).</summary>
    [JsonPropertyName("dir")]
    public float Dir { get; set; } = 213f;

    /// <summary>Total bend accumulated along the arc, degrees; the tail's signature line.
    /// Gathered toward the tip rather than spread evenly, because a fox drops, sweeps and then
    /// hooks up, where a linear curl reads as a banana.</summary>
    [JsonPropertyName("curl")]
    public float Curl { get; set; } = -88f;

    /// <summary>Arc length in 256-space, and how finely it is sampled.</summary>
    [JsonPropertyName("len")]
    public float Len { get; set; } = 130f;

    [JsonPropertyName("segs")]
    public int Segs { get; set; } = 26;

    /// <summary>Girth as a flat list of <c>u, radius</c> pairs, lerped between; the
    /// silhouette's whole story. A list rather than root/belly/tip because three numbers fitted
    /// the fox and would have fought a rabbit's puff, whose widest point is its end.</summary>
    [JsonPropertyName("profile")]
    public float[] Profile { get; set; } = [0f, 6f, 0.5f, 22f, 1f, 7f];

    /// <summary>Jag depth as a fraction of the local radius; 0 is a smooth tail.</summary>
    [JsonPropertyName("fur")]
    public float Fur { get; set; }

    /// <summary>Every Nth sample takes a deep jag; low is coarse and shaggy.</summary>
    [JsonPropertyName("furStep")]
    public int FurStep { get; set; } = 3;

    /// <summary>Where the accent-coloured tip begins, or 0 for a tail of one colour.</summary>
    [JsonPropertyName("tipFrac")]
    public float TipFrac { get; set; }

    /// <summary>Strength of the underside shadow, 0 to skip it.</summary>
    [JsonPropertyName("shade")]
    public float Shade { get; set; } = 0.84f;

    /// <summary>How freely this tail answers the animation stack: a heavy brush under 1, a
    /// skinny whip over it.</summary>
    [JsonPropertyName("response")]
    public float Response { get; set; } = 1f;

    [JsonIgnore]
    public Vector2 NudgePoint => new(
        this.Nudge.Length >= 2 ? this.Nudge[0] : 0f,
        this.Nudge.Length >= 2 ? this.Nudge[1] : 0f);

    /// <summary>Radius at <paramref name="u"/> from the profile pairs. <c>dohl.lerp_profile</c>
    /// ported; the project already had an answer for "how thick is this thing here", and a
    /// second one would only be a second thing to get wrong.</summary>
    public float RadiusAt(float u)
    {
        var p = this.Profile;
        if (p.Length < 4)
        {
            return 8f;
        }

        if (u <= p[0])
        {
            return p[1];
        }

        for (var i = 0; i + 3 < p.Length; i += 2)
        {
            float u0 = p[i], r0 = p[i + 1], u1 = p[i + 2], r1 = p[i + 3];
            if (u0 <= u && u <= u1)
            {
                var k = u1 == u0 ? 0f : (u - u0) / (u1 - u0);
                return r0 + ((r1 - r0) * k);
            }
        }

        return p[^1];
    }
}

/// <summary>
/// An ear model, on the same terms as <see cref="TailPartDef"/>: 256-space, shell-agnostic, no
/// notion of time. Placement comes from the shell's <c>earL</c> and <c>earR</c> anchors; two
/// anchors rather than one mirrored point, exactly as <c>handL</c>/<c>handR</c> already work,
/// so a shell whose boop squashes one side harder places each ear honestly.
/// </summary>
public sealed class EarPartDef
{
    /// <summary>Small correction from the shell's ear anchors, outboard +x.</summary>
    [JsonPropertyName("nudge")]
    public float[] Nudge { get; set; } = [0f, 0f];

    /// <summary>Half-width where the ear leaves the head, 256-space.</summary>
    [JsonPropertyName("baseHalfWidth")]
    public float BaseHalfWidth { get; set; } = 17f;

    /// <summary>Tip height above the seat, 256-space.</summary>
    [JsonPropertyName("height")]
    public float Height { get; set; } = 75f;

    /// <summary>How far the outer edge bellies; the difference between a spike and a leaf.
    /// </summary>
    [JsonPropertyName("bowOut")]
    public float BowOut { get; set; } = 0.12f;

    /// <summary>The same for the inner edge; negative pinches the ear's waist.</summary>
    [JsonPropertyName("bowIn")]
    public float BowIn { get; set; } = -0.18f;

    /// <summary>Rest tilt, degrees outward.</summary>
    [JsonPropertyName("lean")]
    public float Lean { get; set; } = 9f;

    /// <summary>Inner-ear inset as a fraction; 0 skips the accent hollow entirely.</summary>
    [JsonPropertyName("inner")]
    public float Inner { get; set; } = 0.5f;

    /// <summary>How much of the stack's bend this ear spends: 0 is rigid, 1 flops.</summary>
    [JsonPropertyName("floppy")]
    public float Floppy { get; set; } = 0.15f;

    /// <summary>Rest droop added to the lean, degrees; a lop's ears hang before they move.
    /// </summary>
    [JsonPropertyName("droop")]
    public float Droop { get; set; }

    /// <summary>Fur tufts along the inner edge, as a fraction of the base half-width.</summary>
    [JsonPropertyName("tuft")]
    public float Tuft { get; set; }

    /// <summary>How blunt the tip is, as a fraction of the base half-width: 0 is a spike, high
    /// a dome. It opens the two curves' meeting point into a capped span, so a fox's point and
    /// a bear's dome are one construction rather than two special cases.</summary>
    [JsonPropertyName("tipWidth")]
    public float TipWidth { get; set; }

    /// <summary>Edge jag depth as a fraction of the base half-width, closing to nothing at both
    /// the point and the base. A fox ear's tip is its signature; blunt it with noise and the
    /// animal is gone.</summary>
    [JsonPropertyName("fur")]
    public float Fur { get; set; }

    [JsonIgnore]
    public Vector2 NudgePoint => new(
        this.Nudge.Length >= 2 ? this.Nudge[0] : 0f,
        this.Nudge.Length >= 2 ? this.Nudge[1] : 0f);
}
