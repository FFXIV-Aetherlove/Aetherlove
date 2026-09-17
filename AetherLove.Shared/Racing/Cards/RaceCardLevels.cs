using System;

namespace AetherLove.Shared.Racing.Cards;

/// <summary>Card levels and what they do to an operation's amount. A pack that deals a card the player already owns
/// raises that card one level, up to <see cref="MaxLevel"/>. The scale multiplies the authored amount only; durations, windows,
/// triggers and costs never change with level. Balance work will move these numbers, so nothing else may
/// hard-code them.</summary>
public static class RaceCardLevels
{
    public const int MinLevel = 1;

    public const int MaxLevel = 3;

    private const float Level1Scale = 1.00f;
    private const float Level2Scale = 1.15f;
    private const float Level3Scale = 1.30f;

    public static int Clamp(int level) => Math.Clamp(level, MinLevel, MaxLevel);

    public static float Scale(int level) => Clamp(level) switch
    {
        MinLevel => Level1Scale,
        MaxLevel => Level3Scale,
        _ => Level2Scale,
    };

    /// <summary>The amount the resolver applies for a card at <paramref name="level"/>.</summary>
    public static float Effective(float amount, int level) => amount * Scale(level);
}
