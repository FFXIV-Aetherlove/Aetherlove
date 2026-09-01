namespace AetherLove.Shared.Racing;

using AetherLove.Shared.Aetherling;

/// <summary>How hard a course runs for one racer's element. Append-only, stored as short.</summary>
public enum LumiRaceDifficulty : short
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
}

/// <summary>Grades every course against a racer's element. The engine grades affinity with no negative
/// branch, so Hard means "no edge on this ground", never a penalty.</summary>
public static class LumiRaceDifficultyRules
{
    private const float EdgeEpsilon = 0.001f;
    private const float FullEdge = 1f;
    private const float NeighbourEdge = 0.25f;

    /// <summary>The grade a racer of this element gets on this course. A course with no terrain is
    /// Normal for everyone, because nobody has an edge there.</summary>
    public static LumiRaceDifficulty For(short racerElement, AetherRaceLive.CourseDef course)
    {
        if (course is null || string.IsNullOrEmpty(course.Terrain))
        {
            return LumiRaceDifficulty.Normal;
        }

        var mine = RacingElements.NameOf((AetherlingElement)racerElement);
        var edge = RacingElements.WheelEdge(mine, course.Terrain);
        if (MathF.Abs(edge - FullEdge) < EdgeEpsilon)
        {
            return LumiRaceDifficulty.Easy;
        }

        if (MathF.Abs(edge - NeighbourEdge) < EdgeEpsilon)
        {
            return LumiRaceDifficulty.Normal;
        }

        return LumiRaceDifficulty.Hard;
    }

    /// <summary>Every course that grades to this difficulty for this element.</summary>
    public static IReadOnlyList<AetherRaceLive.CourseDef> PoolFor(short racerElement, LumiRaceDifficulty difficulty)
    {
        var pool = new List<AetherRaceLive.CourseDef>();
        foreach (var course in AetherRaceLive.Courses)
        {
            if (For(racerElement, course) == difficulty)
            {
                pool.Add(course);
            }
        }

        return pool;
    }
}
