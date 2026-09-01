using System;

namespace AetherOS.PetKit.Engine;

/// <summary>Plays one named clip out of an atlas manifest: advance by delta time, loop or hold the last
/// frame, report the cell to draw. It never picks a clip of its own, so a caller that parks it on a frame
/// keeps it there.</summary>
public sealed class ClipPlayer
{
    private readonly AtlasManifest _manifest;

    private AnimationDef _clip;
    private float _timer;
    private int _cursor;

    public ClipPlayer(AtlasManifest manifest, string clipName = "idle")
    {
        _manifest = manifest;
        ClipName = clipName;
        _clip = ClipFor(clipName);
    }

    public string ClipName { get; private set; }

    public int StepCount => _clip.Frames.Length;

    /// <summary>The atlas cell to draw this frame.</summary>
    public int CurrentCell =>
        _clip.Frames.Length == 0 ? 0 : _clip.Frames[Math.Clamp(_cursor, 0, _clip.Frames.Length - 1)];

    /// <summary>A non-looping clip has reached and is holding its last frame.</summary>
    public bool Finished => !_clip.Loop && _cursor >= _clip.Frames.Length - 1;

    /// <summary>How far the clock has got through the current frame, 0..1. The one thing a drawn
    /// shell needs that a sheet never did: a sheet can only show the cell it is on, so the
    /// sub-frame position was never worth publishing; a drawn shell reads between two pose keys
    /// with it. Computed exactly as <see cref="Update"/> computes the frame duration, so the two
    /// cannot drift.</summary>
    public float FramePhase
    {
        get
        {
            var frameDuration = 1f / MathF.Max(1f, _clip.Fps);
            return Math.Clamp(_timer / frameDuration, 0f, 1f);
        }
    }

    /// <summary>The cells the current clip walks, in order. Read by the drawn shells to ask
    /// whether this clip actually ANIMATES a given channel or merely holds it.</summary>
    public ReadOnlySpan<int> CurrentFrames => _clip.Frames;

    /// <summary>The cell <paramref name="delta"/> steps along the clip from the one showing,
    /// wrapping on a looping clip and holding at the ends of a one-shot. For curve interpolation,
    /// which needs a key either side of the pair being blended.</summary>
    public int CellAtOffset(int delta)
    {
        var frames = _clip.Frames;
        if (frames.Length == 0)
        {
            return 0;
        }

        var i = Math.Clamp(_cursor, 0, frames.Length - 1) + delta;
        if (_clip.Loop)
        {
            i = ((i % frames.Length) + frames.Length) % frames.Length;
        }
        else
        {
            i = Math.Clamp(i, 0, frames.Length - 1);
        }

        return frames[i];
    }

    /// <summary>Starts this clip somewhere else in its own loop, from a stable seed, so two pets
    /// of the same shell never breathe in unison. It changes only WHERE in the loop the pet
    /// starts, never what the loop is.</summary>
    public void PrimePhase(int seed)
    {
        var frames = _clip.Frames;
        if (frames.Length <= 1)
        {
            return;
        }

        var r = new Random(seed);
        _cursor = r.Next(frames.Length);
        _timer = (float)r.NextDouble() / MathF.Max(1f, _clip.Fps);
    }

    /// <summary>Starts <paramref name="name"/> from its first frame, or the manifest's idle clip
    /// when the name is unknown.</summary>
    public void Play(string name)
    {
        ClipName = name;
        _clip = ClipFor(name);
        _cursor = 0;
        _timer = 0f;
    }

    /// <summary>How fast the clip runs, as a multiplier: the emote's Rate. Applied to the frame
    /// DURATION rather than to dt, so the accumulator keeps counting real seconds and a rate that
    /// changes mid-clip cannot lose or repeat the frame it changes on.</summary>
    public float RateScale { get; set; } = 1f;

    public void Update(float dt)
    {
        if (_clip.Frames.Length == 0)
        {
            return;
        }

        _timer += dt;
        var frameDuration = 1f / MathF.Max(1f, _clip.Fps * MathF.Max(0.1f, RateScale));
        while (_timer >= frameDuration)
        {
            _timer -= frameDuration;
            _cursor++;
            if (_cursor >= _clip.Frames.Length)
            {
                _cursor = _clip.Loop ? 0 : _clip.Frames.Length - 1;
            }
        }
    }

    private AnimationDef ClipFor(string name) =>
        _manifest.Animations.TryGetValue(name, out var def) ? def : _manifest.Animations["idle"];
}
