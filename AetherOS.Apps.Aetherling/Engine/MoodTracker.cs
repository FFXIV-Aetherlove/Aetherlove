namespace AetherOS.Apps.Aetherling.Engine;

/// <summary>How it seems to be feeling. Shown as a sentence and as a place on a scale, never as a fill: a
/// bar that fills is a bar that can be watched emptying, which is a chore. The pet page's marker slides
/// along the whole ramp and the mood keeps its floor, so there is nothing here to lose and nothing that
/// decays into neglect.</summary>
public enum MoodLevel
{
    Napping = 0,
    Dozy = 1,
    Mellow = 2,
    Content = 3,
    Bright = 4,
    Beaming = 5,
}

/// <summary>Derives the mood from attention alone, and keeps nothing on disk. Content is the floor while it
/// is awake, so leaving for a week costs the player nothing; attention adds warmth, which bleeds away on its
/// own, so the top of the scale is somewhere you visit rather than somewhere you park.</summary>
public sealed class MoodTracker
{
    /// <summary>Warmth per lift, and what the two steps above Content cost. Beaming is deliberately five
    /// lifts rather than two: a mood that maxes on the second touch is a mood nobody notices moving, and
    /// the bar on the pet page reads the warmth directly, so the climb is the thing being watched.</summary>
    private const float LiftGain = 1f;
    private const float BrightAt = 2f;
    private const float BeamingAt = 5f;

    /// <summary>The shortest gap between two lifts that both count. A stroke of petting fires once every
    /// few pixels of travel, so without this one continuous drag is the whole scale in half a second.</summary>
    private const float LiftGapSeconds = 0.55f;

    /// <summary>Warmth lost per second, once the hold has passed. A full five bleeds off in about four
    /// minutes, so a beaming pet left alone settles back to content at roughly the pace it used to.</summary>
    private const float DecayPerSecond = 1f / 48f;

    /// <summary>How long warmth sits still before it starts bleeding. Without it the top of the scale is
    /// unreachable in practice rather than merely hard: warmth caps at the beaming threshold, so the first
    /// instant of decay would put it under again and the pet would beam for a single frame.</summary>
    private const float HoldSeconds = 30f;

    private const float MellowAfterSeconds = 300f;
    private const float DozyAfterSeconds = 480f;

    private float _warmth;
    private float _sinceLift = LiftGapSeconds;

    /// <summary>A boop, a treat, a stroke, anything that counts as attention. Nothing here can drop the
    /// mood below the Content floor, and a lift inside the gap is ignored rather than queued.</summary>
    public void Lift()
    {
        if (_sinceLift < LiftGapSeconds)
        {
            return;
        }
        _sinceLift = 0f;
        _warmth = System.MathF.Min(BeamingAt, _warmth + LiftGain);
    }

    /// <summary>Coming back after time away: the pet is found at rest, not still glowing from a
    /// lift nobody watched.</summary>
    public void PrimeQuiet()
    {
        _warmth = 0f;
        _sinceLift = LiftGapSeconds;
    }

    public void Update(float dt)
    {
        _sinceLift += dt;
        if (_warmth > 0f && _sinceLift >= HoldSeconds)
        {
            _warmth = System.MathF.Max(0f, _warmth - (dt * DecayPerSecond));
        }
    }

    /// <summary>The current mood. <paramref name="sinceInteraction"/> comes from the animator, so the two
    /// never disagree about how long it has been alone.</summary>
    public MoodLevel Current(float sinceInteraction, bool napping)
    {
        if (napping)
        {
            return MoodLevel.Napping;
        }
        if (_warmth >= BeamingAt)
        {
            return MoodLevel.Beaming;
        }
        if (_warmth >= BrightAt)
        {
            return MoodLevel.Bright;
        }
        if (sinceInteraction >= DozyAfterSeconds)
        {
            return MoodLevel.Dozy;
        }
        return sinceInteraction >= MellowAfterSeconds ? MoodLevel.Mellow : MoodLevel.Content;
    }

    /// <summary>Where the mood sits on its own scale, 0 asleep to 1 beaming, for the bar. Above the Content
    /// floor this follows the warmth itself rather than the named level, so the marker creeps with every
    /// stroke instead of standing still between two thresholds and then jumping.</summary>
    public float Progress01(float sinceInteraction, bool napping)
    {
        var steps = (float)(int)MoodLevel.Beaming;
        if (napping)
        {
            return 0f;
        }
        if (_warmth > 0f)
        {
            var content = (float)(int)MoodLevel.Content / steps;
            return content + ((1f - content) * System.Math.Clamp(_warmth / BeamingAt, 0f, 1f));
        }
        return (float)(int)Current(sinceInteraction, napping) / steps;
    }
}
