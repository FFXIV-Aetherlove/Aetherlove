namespace AetherLove.Os;

/// <summary>The single sparks signal an arcade app raises: a round finished. Apps never name an amount or a
/// cap; the server prices the action and decides whether it pays. Lives here rather than in the SDK because
/// sparks are an AetherLove concept, not a platform one.</summary>
public interface IArcadeRewards
{
    void NoteGameFinished();
}
