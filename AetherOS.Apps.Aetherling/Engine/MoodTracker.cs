namespace AetherOS.Apps.Aetherling.Engine;

/// <summary>How it seems to be feeling. Deliberately a sentence and never a meter: a number the player can
/// watch drop is a chore, and nothing here decays into neglect.</summary>
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
/// is awake, so leaving for a week costs the player nothing; being around lifts it above that for a while.</summary>
public sealed class MoodTracker
{
    private const float BeamingSeconds = 12f;
    private const float BrightSeconds = 90f;
    private const float MellowAfterSeconds = 300f;
    private const float DozyAfterSeconds = 480f;

    private float _sinceLift = float.MaxValue;

    /// <summary>A boop, a name, anything that counts as attention.</summary>
    public void Lift() => _sinceLift = 0f;

    public void Update(float dt)
    {
        if (_sinceLift < float.MaxValue)
        {
            _sinceLift += dt;
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
        if (_sinceLift < BeamingSeconds)
        {
            return MoodLevel.Beaming;
        }
        if (_sinceLift < BrightSeconds)
        {
            return MoodLevel.Bright;
        }
        if (sinceInteraction >= DozyAfterSeconds)
        {
            return MoodLevel.Dozy;
        }
        return sinceInteraction >= MellowAfterSeconds ? MoodLevel.Mellow : MoodLevel.Content;
    }
}
