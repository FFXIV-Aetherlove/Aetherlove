namespace AetherLove.Shared.Racing;

using AetherLove.Shared.Aetherling;

/// <summary>The element wheel as the race engine reads it: six names, clockwise, and the
/// distance grading that ground affinity is built on.</summary>
public static class RacingElements
{
    /// <summary>The six, clockwise: fire, lightning, wind, ice, water, earth. Index arithmetic
    /// against this array IS the chart, so the order may not be rearranged. It is deliberately
    /// NOT <see cref="AetherlingElement"/>'s declaration order, which differs; the wheel is the
    /// arrangement every race was resolved under.</summary>
    public static readonly string[] WheelOrder = ["fire", "lightning", "wind", "ice", "water", "earth"];

    /// <summary>Ground affinity by distance around the wheel: the ground's own element a full
    /// boost, either neighbour a quarter, anything further nothing. There is no negative branch;
    /// the far half of the wheel is neutral, never punished.</summary>
    public static float WheelEdge(string mine, string scenario)
    {
        if (string.IsNullOrEmpty(mine) || string.IsNullOrEmpty(scenario))
        {
            return 0f;
        }

        var a = Array.IndexOf(WheelOrder, mine);
        var b = Array.IndexOf(WheelOrder, scenario);
        if (a < 0 || b < 0)
        {
            return 0f;
        }

        var steps = Math.Abs(a - b);
        var around = Math.Min(steps, WheelOrder.Length - steps);
        return around switch
        {
            0 => 1f,
            1 => 0.25f,
            _ => 0f,
        };
    }

    /// <summary>The wheel name for an <see cref="AetherlingElement"/>; empty for
    /// <see cref="AetherlingElement.None"/> or an unknown value.</summary>
    public static string NameOf(AetherlingElement element) => element switch
    {
        AetherlingElement.Fire => "fire",
        AetherlingElement.Lightning => "lightning",
        AetherlingElement.Wind => "wind",
        AetherlingElement.Ice => "ice",
        AetherlingElement.Water => "water",
        AetherlingElement.Earth => "earth",
        _ => string.Empty,
    };

    /// <summary>The <see cref="AetherlingElement"/> for a wheel name;
    /// <see cref="AetherlingElement.None"/> for empty or unknown.</summary>
    public static AetherlingElement ElementOf(string name) => name switch
    {
        "fire" => AetherlingElement.Fire,
        "lightning" => AetherlingElement.Lightning,
        "wind" => AetherlingElement.Wind,
        "ice" => AetherlingElement.Ice,
        "water" => AetherlingElement.Water,
        "earth" => AetherlingElement.Earth,
        _ => AetherlingElement.None,
    };
}
