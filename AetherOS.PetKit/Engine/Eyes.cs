using System;
using System.Collections.Generic;

using AetherOS.PetKit.Rendering.LineArt;

namespace AetherOS.PetKit.Engine;

/// <summary>One key of an emote's eye track: at <see cref="At"/> seconds the eyes take a named
/// state, arriving over <see cref="Tween"/> seconds, exactly as <see cref="MouthKey"/> works.
/// Default 0.09s because a lid is quicker than a mouth: a blink is about a tenth of a second
/// all in.</summary>
public readonly record struct EyeKey(float At, string State, float Tween = 0.09f);

/// <summary>The named eye states an emote may ask for, and the only vocabulary it has. The names
/// are the sheet's own, now the ends of tweens rather than the only places the eye may rest.</summary>
public static class EyeStates
{
    private static readonly Dictionary<string, LineShell.EyeState> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["open"] = LineShell.Open,
        ["threeq"] = LineShell.ThreeQ,
        ["half"] = LineShell.HalfShut,
        ["quarter"] = LineShell.Quarter,
        ["shut"] = LineShell.Shut,
        ["drowsy"] = LineShell.Drowsy,
        ["heavy"] = LineShell.Heavy,

        // Not lid heights, so the sheet could never draw them.
        ["wide"] = LineShell.Wide,
        ["squint"] = LineShell.Squint,
        ["happy"] = LineShell.Happy,

        // Gaze: four names rather than a free vector.
        ["down"] = LineShell.Down,
        ["up"] = LineShell.Up,
        ["away"] = LineShell.Away,
        ["downcast"] = LineShell.Downcast,
    };

    /// <summary>The state a name means, or null for one the vocabulary does not have. Null is
    /// treated as "leave the eye alone" rather than as an error, on the house rule that a track the
    /// pet cannot perform is dropped and not refused.</summary>
    public static LineShell.EyeState? Find(string name) =>
        Named.TryGetValue(name, out var s) ? s : null;
}

/// <summary>The eye track player: <see cref="MouthController"/>'s twin. It blends OVER the clip's
/// own eye rather than replacing it: the shell's answer is the base, a track owns the eye only as
/// far as <c>blend</c>, and at blend zero the clip's eye is untouched. Never a cell substitution:
/// swapping in a rest-registered eye cell pins the pose spline and freezes the body at rest, which
/// is what <see cref="AnimationController.SuppressEyeCellSwap"/> turns off.</summary>
public sealed class EyeController
{
    private const float ReleaseSeconds = 0.16f;

    private LineShell.EyeState _from = LineShell.Open;
    private LineShell.EyeState _target = LineShell.Open;
    private float _tweenT = 1f;
    private float _tweenSeconds = 0.09f;

    private EyeKey[] _track = [];
    private float _clock;
    private float _seconds;
    private int _cursor;
    private bool _active;

    private float _blend;

    /// <summary>True while a track owns any part of the eye, so a caller can tell "the emote is
    /// doing the eyes" from "the clip is".</summary>
    public bool Playing => _active || _blend > 0f;

    /// <summary>Plays an eye track. A new track replaces a playing one, and an empty track is a
    /// no-op rather than a stop: an emote with nothing to say about the eyes must leave the ones a
    /// previous emote is still easing home alone.</summary>
    public void Play(EyeKey[] keys, float seconds)
    {
        if (keys.Length == 0)
        {
            return;
        }

        _track = keys;
        _seconds = seconds;
        _clock = 0f;
        _cursor = 0;
        _active = true;
    }

    /// <summary>Ends the track now and hands the eye back to the clip over the release.</summary>
    public void Stop() => _active = false;

    public void Update(float dt)
    {
        if (_tweenT < 1f)
        {
            _tweenT = Math.Min(1f, _tweenT + (_tweenSeconds <= 0f ? 1f : dt / _tweenSeconds));
        }

        if (_active)
        {
            _clock += dt;
            while (_cursor < _track.Length && _track[_cursor].At <= _clock)
            {
                var key = _track[_cursor];
                if (EyeStates.Find(key.State) is { } state)
                {
                    _from = Current;
                    _target = state;
                    _tweenSeconds = key.Tween;
                    _tweenT = 0f;
                }

                _cursor++;
            }

            if (_clock >= _seconds)
            {
                _active = false;
            }

            // Up fast: a track that eased in would spend its first key arriving rather than
            // performing it, and the first key is usually the point of the whole track.
            _blend = Math.Min(1f, _blend + (dt / 0.05f));
        }
        else if (_blend > 0f)
        {
            _blend = Math.Max(0f, _blend - (dt / ReleaseSeconds));
        }
    }

    /// <summary>This frame's eye, given whatever the shell's own clip is doing. Returns the base
    /// untouched when no track is playing or fading.</summary>
    public LineShell.EyeState Over(LineShell.EyeState clipEye) =>
        _blend <= 0f ? clipEye : LineShell.EyeState.Lerp(clipEye, Current, _blend);

    private LineShell.EyeState Current => _tweenT >= 1f
        ? _target
        : LineShell.EyeState.Lerp(_from, _target, Ease(_tweenT));

    // The mouth's easing, for the same reason it has one: a lid that arrives at constant speed and
    // stops dead reads as a mechanism rather than as a face.
    private static float Ease(float t) => t * t * (3f - (2f * t));
}
