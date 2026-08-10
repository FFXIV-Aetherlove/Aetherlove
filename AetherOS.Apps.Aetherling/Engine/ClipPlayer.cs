using System;

namespace AetherOS.Apps.Aetherling.Engine;

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

    /// <summary>Starts <paramref name="name"/> from its first frame, or the manifest's idle clip
    /// when the name is unknown.</summary>
    public void Play(string name)
    {
        ClipName = name;
        _clip = ClipFor(name);
        _cursor = 0;
        _timer = 0f;
    }

    public void Update(float dt)
    {
        if (_clip.Frames.Length == 0)
        {
            return;
        }

        _timer += dt;
        var frameDuration = 1f / MathF.Max(1f, _clip.Fps);
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
