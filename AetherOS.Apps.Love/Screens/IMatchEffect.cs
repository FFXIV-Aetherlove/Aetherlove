namespace AetherLove.Screens;

/// <summary>One "You've matched!" celebration effect. The match host picks a random implementation from
/// the registered pool, resets it via <see cref="OnShow"/>, then drives it each frame with <see cref="Draw"/>.</summary>
public interface IMatchEffect
{
    void OnShow();
    void Draw();
}
