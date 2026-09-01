using System;
using System.Text.Json.Serialization;

namespace AetherOS.PetKit.Engine;

/// <summary>A fan of code-drawn strands: the record behind the Antennae, and behind every tendril,
/// whisker and leg the prototype's lab shells grow. Transcribed from the prototype's StrandDef; the
/// numbers are tuned on its bench and must not drift here. Strands are built with +x pointing AWAY
/// from the centre line and mirrored on the way out, so a pair leans apart rather than in step.</summary>
public sealed class StrandDef
{
    [JsonPropertyName("part")]
    public string Part { get; set; } = string.Empty;

    /// <summary>The anchor the fan is sown on.</summary>
    [JsonPropertyName("seat")]
    public string Seat { get; set; } = "body";

    /// <summary>Base direction in radians, screen-down positive: PI/2 hangs, negative rises.</summary>
    [JsonPropertyName("dir")]
    public float Dir { get; set; } = MathF.PI / 2f;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>Arc length of the outermost strand, 256-space.</summary>
    [JsonPropertyName("len")]
    public float Len { get; set; }

    /// <summary>Root radius, 256-space.</summary>
    [JsonPropertyName("root")]
    public float Root { get; set; }

    /// <summary>Half the fan's base width, 256-space.</summary>
    [JsonPropertyName("spread")]
    public float Spread { get; set; }

    /// <summary>Tip radius as a fraction of the root.</summary>
    [JsonPropertyName("taper")]
    public float Taper { get; set; } = 1f;

    /// <summary>How much shorter and thinner the inner strands run, 0 for an even fan.</summary>
    [JsonPropertyName("stagger")]
    public float Stagger { get; set; }

    /// <summary>Outboard lean of the outer strands, radians at the edge of the fan.</summary>
    [JsonPropertyName("splay")]
    public float Splay { get; set; }

    /// <summary>Outboard bend accumulated along the strand, radians over its length.</summary>
    [JsonPropertyName("curl")]
    public float Curl { get; set; }

    /// <summary>Tip ball as a multiple of the root radius, 0 for none.</summary>
    [JsonPropertyName("bulb")]
    public float Bulb { get; set; }

    /// <summary>Wave amplitude in radians.</summary>
    [JsonPropertyName("amp")]
    public float Amp { get; set; }

    /// <summary>Crests along the strand.</summary>
    [JsonPropertyName("waves")]
    public float Waves { get; set; }

    /// <summary>Crests passing per second.</summary>
    [JsonPropertyName("speed")]
    public float Speed { get; set; }

    [JsonPropertyName("segs")]
    public int Segs { get; set; } = 8;

    /// <summary>A slow current each strand leans in, radians; scattered per strand so a pair does
    /// not pulse in formation.</summary>
    [JsonPropertyName("drift")]
    public float Drift { get; set; }

    [JsonPropertyName("driftSpeed")]
    public float DriftSpeed { get; set; }

    /// <summary>How loosely the fan hangs: a multiplier on the swing the body's own motion earns.</summary>
    [JsonPropertyName("swing")]
    public float Swing { get; set; }

    /// <summary>"flowing" bends per segment; "jointed" is the three-bend leg profile.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "flowing";

    [JsonIgnore]
    public bool Jointed => string.Equals(Mode, "jointed", StringComparison.OrdinalIgnoreCase);
}
