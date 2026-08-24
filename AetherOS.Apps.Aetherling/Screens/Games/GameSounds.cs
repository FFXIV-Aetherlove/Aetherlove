using System;
using System.IO;
using AetherOS.Sdk;

namespace AetherOS.Apps.Aetherling.Screens.Games;

/// <summary>What the minigames are allowed to make a noise about, and how each one sounds. The games name
/// the event; this is the only place that knows which file that is, how loud it plays, and at what pitch.
/// New entries are cheap: a value here and one line at the call site.</summary>
internal enum GameSound
{
    /// <summary>A crystal caught or collected.</summary>
    Crystal,

    /// <summary>A bonus crystal. The same ding rung higher, for the bigger stone.</summary>
    BigCrystal,

    /// <summary>Something that was not a crystal: the dull answer to the ding.</summary>
    Thud,

    /// <summary>Bouncing off a cloud.</summary>
    Jump,

    /// <summary>Lumi-Link: two tiles trading places.</summary>
    Swap,

    /// <summary>Lumi-Link: a swap that made nothing, sliding back.</summary>
    Bad,

    /// <summary>Lumi-Link: a match, rung on a ladder that rises a step per cascade. <see cref="Ladder0"/>
    /// through <see cref="Ladder7"/> are the eight rungs; the game picks by cascade depth.</summary>
    Ladder0,
    Ladder1,
    Ladder2,
    Ladder3,
    Ladder4,
    Ladder5,
    Ladder6,
    Ladder7,

    /// <summary>Lumi-Link: a Bolt firing down its line.</summary>
    Bolt,

    /// <summary>Lumi-Link: a Burst going off.</summary>
    Burst,

    /// <summary>Lumi-Link: a Prism taking a whole colour, or two of them taking the board.</summary>
    Chord0,
    Chord1,
    Chord2,

    /// <summary>Lumi-Link: the level bar filled.</summary>
    LevelUp,

    /// <summary>Lumi-Link: an element power spent.</summary>

    /// <summary>Lumi-Link: the last seconds, one tick a second.</summary>
    Tick,

    /// <summary>Lumi-Link: the creature pleased with a cascade.</summary>
    Chirp,
}

internal static class GameSounds
{
    private const string CrystalFile = "crystal_ding.ogg";
    private const string ThudFile = "crystal_thud.ogg";
    private const string JumpFile = "cloud_jump.ogg";
    private const string ChirpFile = "aetherling_chirp_03.ogg";
    private const string ResponseFile = "aetherling_response_05.ogg";

    /// <summary>A bonus crystal is bigger, so it rings a fourth higher rather than merely louder.</summary>
    private const float BigCrystalPitch = 1.33f;

    /// <summary>A bounce happens every second or so, so it sits under the rest: a landing is not an
    /// event, it is the game running normally.</summary>
    private const float JumpLevel = 0.22f;

    private const float CrystalLevel = 0.35f;
    private const float ThudLevel = 0.3f;

    /// <summary>The match ladder in semitones above the ding's own pitch: a major scale, so eight
    /// cascades in a row sing rather than squeal.</summary>
    private static readonly int[] LadderSemitones = [0, 2, 4, 5, 7, 9, 11, 12];

    private static float Semitones(int n) => MathF.Pow(2f, n / 12f);

    public static void Play(IAudioPlayer audio, string folder, GameSound sound)
    {
        var (file, level, pitch) = sound switch
        {
            GameSound.Crystal => (CrystalFile, CrystalLevel, 1f),
            GameSound.BigCrystal => (CrystalFile, CrystalLevel, BigCrystalPitch),
            GameSound.Thud => (ThudFile, ThudLevel, 1f),
            GameSound.Jump => (JumpFile, JumpLevel, 1f),
            GameSound.Swap => (ThudFile, 0.2f, 2.3f),
            GameSound.Bad => (ThudFile, 0.3f, 0.7f),
            >= GameSound.Ladder0 and <= GameSound.Ladder7 =>
                (CrystalFile, 0.5f, Semitones(LadderSemitones[sound - GameSound.Ladder0])),
            GameSound.Bolt => (JumpFile, 0.4f, 1.3f),
            GameSound.Burst => (ThudFile, 0.45f, 0.55f),
            GameSound.Chord0 => (CrystalFile, 0.5f, Semitones(0)),
            GameSound.Chord1 => (CrystalFile, 0.5f, Semitones(4)),
            GameSound.Chord2 => (CrystalFile, 0.5f, Semitones(7)),
            GameSound.LevelUp => (ResponseFile, 0.45f, 1f),
            GameSound.Tick => (CrystalFile, 0.18f, 2f),
            GameSound.Chirp => (ChirpFile, 0.4f, 1f),
            _ => (JumpFile, JumpLevel, 1f),
        };
        audio.Play(Path.Combine(folder, file), level, pitch);
    }
}
