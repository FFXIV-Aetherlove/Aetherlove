using AetherLove.Services.Sparks;

namespace AetherLove.Os;

/// <summary>Hands an arcade app's finished-round signal to the spark reporter, which is the only thing that
/// knows how to talk to the hub.</summary>
public sealed class ArcadeRewardsService(SparkActivityReporter reporter) : IArcadeRewards
{
    public void NoteGameFinished() => reporter.NoteArcadeGameFinished();
}
