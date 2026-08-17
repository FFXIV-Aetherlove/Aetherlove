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
}

internal static class GameSounds
{
    private const string CrystalFile = "crystal_ding.ogg";
    private const string ThudFile = "crystal_thud.ogg";
    private const string JumpFile = "cloud_jump.ogg";

    /// <summary>A bonus crystal is bigger, so it rings a fourth higher rather than merely louder.</summary>
    private const float BigCrystalPitch = 1.33f;

    /// <summary>A bounce happens every second or so, so it sits under the rest: a landing is not an
    /// event, it is the game running normally.</summary>
    private const float JumpLevel = 0.22f;

    private const float CrystalLevel = 0.35f;
    private const float ThudLevel = 0.3f;

    public static void Play(IAudioPlayer audio, string folder, GameSound sound)
    {
        var (file, level, pitch) = sound switch
        {
            GameSound.Crystal => (CrystalFile, CrystalLevel, 1f),
            GameSound.BigCrystal => (CrystalFile, CrystalLevel, BigCrystalPitch),
            GameSound.Thud => (ThudFile, ThudLevel, 1f),
            _ => (JumpFile, JumpLevel, 1f),
        };
        audio.Play(Path.Combine(folder, file), level, pitch);
    }
}
