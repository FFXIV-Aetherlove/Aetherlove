using System;
using System.Numerics;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>Where the crystal sits, and therefore where the newborn is standing the instant the ceremony
/// ends. The pet page reads it too: it has to know where the last frame of the birth left him, or he
/// teleports to the floor of his card the moment the screen changes.</summary>
internal static class BirthStage
{
    public static float DisplaySize(Vector2 windowSize) =>
        MathF.Min(windowSize.X * 0.62f, windowSize.Y * 0.42f);

    public static Vector2 BottomCentre(Vector2 origin, Vector2 windowSize) =>
        origin + new Vector2(windowSize.X * 0.5f, windowSize.Y * 0.56f);
}
