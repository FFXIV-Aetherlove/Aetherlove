using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Aetherling.Engine;

/// <summary>A mouth as parameters: five control-point heights across the width (256-space px,
/// positive is down, so a smile dips in the middle) and how open it is. Every field lerps, which
/// is the entire deformer story: any shape reaches any other through <see cref="MouthShapes.Lerp"/>,
/// so no transition is ever authored.</summary>
public readonly record struct MouthShape(
    string Name, float Width, float Y0, float Y1, float Y2, float Y3, float Y4, float Openness);

/// <summary>One key of a mouth track: at <see cref="At"/> seconds, deform to <see cref="Shape"/>
/// over <see cref="Tween"/> seconds.</summary>
public readonly record struct MouthKey(float At, string Shape, float Tween = 0.12f);

/// <summary>The shared shape library, replacing the mouths that used to be baked into each
/// sheet's overlay layer. Seventeen shapes; every other ask resolves through the alias map.</summary>
public static class MouthShapes
{
    public static readonly MouthShape[] All =
    [
        new("soft smile", 25f, -1.2f, 1.2f, 2.9f, 1.2f, -1.2f, 0f),
        new("cat smile", 24f, -2.0f, 1.4f, -0.5f, 1.4f, -2.0f, 0f),
        new("grin", 34f, -2.6f, 1.8f, 3.9f, 1.8f, -2.6f, 0.18f),
        new("open joy", 27f, -3.2f, 1.6f, 3.2f, 1.6f, -3.2f, 0.42f),
        new("ah", 18f, -1.0f, 0.4f, 0.8f, 0.4f, -1.0f, 0.90f),
        new("ee", 30f, -0.6f, 0.2f, 0.4f, 0.2f, -0.6f, 0.18f),
        new("oh", 16f, -0.5f, 0f, 0.2f, 0f, -0.5f, 0.55f),
        new("o", 13f, -1.0f, -1.5f, -1.8f, -1.5f, -1.0f, 1f),
        new("eh", 14f, -0.4f, 0.2f, 0.4f, 0.2f, -0.4f, 0.30f),
        new("kiss", 10f, 0.8f, -0.6f, -1.0f, -0.6f, 0.8f, 0.07f),
        new("flat", 22f, 0f, 0f, 0f, 0f, 0f, 0f),
        new("sleepy", 20f, 0.3f, -0.7f, 0.3f, -0.6f, 0.3f, 0f),
        new("smirk", 24f, 1.9f, 0.3f, -0.7f, -1.9f, -2.9f, 0f),
        new("pout", 17f, 1.5f, -1.0f, -1.7f, -1.0f, 1.5f, 0f),
        new("frown", 24f, 2.6f, -1.0f, -2.4f, -1.0f, 2.6f, 0f),
        new("wobble", 26f, 2.2f, -1.5f, 1.2f, -1.5f, 2.2f, 0f),
        new("open sad", 20f, 2.2f, -0.6f, -1.6f, -0.6f, 2.2f, 0.50f),
    ];

    /// <summary>Every common ask maps to a canonical shape, so tracks can say what they mean
    /// ("gasp", "laugh", "mm") and the library stays seventeen.</summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = "ah", ["i"] = "ah", ["ai"] = "ah",
            ["e"] = "ee",
            ["u"] = "o", ["oo"] = "o", ["w"] = "o", ["ooo"] = "o",
            ["m"] = "flat", ["b"] = "flat", ["p"] = "flat", ["mm"] = "flat",
            ["smile"] = "soft smile", ["rest"] = "soft smile", ["content"] = "soft smile",
            ["beam"] = "cat smile", ["happy"] = "cat smile",
            ["joy"] = "grin", ["excited"] = "grin",
            ["laugh"] = "open joy", ["cheer"] = "open joy", ["delight"] = "open joy",
            ["gasp"] = "ah", ["shocked"] = "ah", ["yell"] = "ah", ["sing"] = "ah",
            ["eek"] = "ee", ["grimace"] = "ee", ["wide"] = "ee",
            ["wow"] = "oh", ["round"] = "oh",
            ["boop"] = "o", ["whistle-small"] = "o", ["hum"] = "o",
            ["chew-open"] = "eh", ["uh"] = "eh", ["mid"] = "eh",
            ["pucker"] = "kiss", ["mwah"] = "kiss", ["whistle"] = "kiss",
            ["determined"] = "flat", ["chew-closed"] = "flat", ["press"] = "flat",
            ["doze"] = "sleepy", ["zzz"] = "sleepy",
            ["sly"] = "smirk", ["hmm"] = "smirk", ["side"] = "smirk",
            ["no"] = "pout", ["hmph"] = "pout",
            ["sad"] = "frown", ["upset"] = "frown",
            ["quiver"] = "wobble", ["teary"] = "wobble",
            ["wail"] = "open sad", ["sob"] = "open sad", ["dismay"] = "open sad",
        };

    /// <summary>Resolves a name or alias; unknown names fall back to the resting soft smile,
    /// because a mouth that shrugs is better than one that breaks.</summary>
    public static MouthShape Find(string name)
    {
        if (Aliases.TryGetValue(name, out var canonical))
        {
            name = canonical;
        }

        foreach (var shape in All)
        {
            if (string.Equals(shape.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return shape;
            }
        }

        return All[0];
    }

    /// <summary>Track builder: shapes spaced <paramref name="step"/> seconds apart, each
    /// tweening over <paramref name="tween"/>.</summary>
    public static MouthKey[] Sequence(float step, float tween, params string[] shapes)
    {
        var keys = new MouthKey[shapes.Length];
        for (var i = 0; i < shapes.Length; i++)
        {
            keys[i] = new MouthKey(i * step, shapes[i], tween);
        }

        return keys;
    }

    /// <summary>The deformer: point-wise lerp, any shape to any shape.</summary>
    public static MouthShape Lerp(in MouthShape a, in MouthShape b, float t) => new(
        b.Name,
        Mix(a.Width, b.Width, t),
        Mix(a.Y0, b.Y0, t),
        Mix(a.Y1, b.Y1, t),
        Mix(a.Y2, b.Y2, t),
        Mix(a.Y3, b.Y3, t),
        Mix(a.Y4, b.Y4, t),
        Mix(a.Openness, b.Openness, t));

    /// <summary>The resting shape for a clip and a mood. Mood leaks into the face the way the
    /// baked art never could: a Dozy pet wears its sleepiness.</summary>
    public static string For(string clip, MoodLevel mood) => clip switch
    {
        "nap" => "sleepy",
        "boop" => "o",
        "hop" or "chase" => "grin",
        _ => mood switch
        {
            MoodLevel.Napping or MoodLevel.Dozy => "sleepy",
            MoodLevel.Beaming => "cat smile",
            _ => "soft smile",
        },
    };

    private static float Mix(float a, float b, float t) => a + ((b - a) * t);
}

/// <summary>The one dynamic mouth: a base shape derived from the animation state, an override
/// track pushed by anything that wants the face for a moment, and the deformer easing every
/// change. Every surface reads the same mouth, the way every surface reads the same pose.</summary>
public sealed class MouthController
{
    private MouthShape _from = MouthShapes.All[0];
    private MouthShape _target = MouthShapes.All[0];
    private string _targetName = MouthShapes.All[0].Name;
    private float _tweenT = 1f;
    private float _tweenSeconds = 0.25f;

    private string _baseName = MouthShapes.All[0].Name;

    private MouthKey[] _track = [];
    private float _trackClock;
    private float _trackSeconds;
    private int _trackCursor;
    private bool _trackActive;

    /// <summary>This frame's mouth, fully deformed.</summary>
    public MouthShape Current => _tweenT >= 1f
        ? _target
        : MouthShapes.Lerp(_from, _target, Ease(_tweenT));

    /// <summary>The resting shape, applied when no track plays. No-ops on repeats, so calling
    /// every frame is the intended use.</summary>
    public void SetBase(string shapeName, float tween = 0.3f)
    {
        _baseName = shapeName;
        if (!_trackActive)
        {
            StartTween(shapeName, tween);
        }
    }

    /// <summary>Plays a mouth track over the base. When <paramref name="seconds"/> pass the
    /// mouth tweens home on its own; a new track replaces a playing one.</summary>
    public void Play(MouthKey[] keys, float seconds)
    {
        if (keys.Length == 0)
        {
            return;
        }

        _track = keys;
        _trackSeconds = seconds;
        _trackClock = 0f;
        _trackCursor = 0;
        _trackActive = true;
    }

    /// <summary>Ends any track now and eases home to the base shape.</summary>
    public void Stop(float tween = 0.3f)
    {
        _trackActive = false;
        StartTween(_baseName, tween);
    }

    public void Update(float dt)
    {
        if (_tweenT < 1f)
        {
            _tweenT = Math.Min(1f, _tweenT + (_tweenSeconds <= 0f ? 1f : dt / _tweenSeconds));
        }

        if (!_trackActive)
        {
            return;
        }

        _trackClock += dt;
        while (_trackCursor < _track.Length && _track[_trackCursor].At <= _trackClock)
        {
            var key = _track[_trackCursor++];
            StartTween(key.Shape, key.Tween);
        }

        if (_trackClock >= _trackSeconds)
        {
            Stop();
        }
    }

    private void StartTween(string shapeName, float seconds)
    {
        // Compared by canonical name so an alias pushed onto its own shape is the no-op it
        // should be.
        var resolved = MouthShapes.Find(shapeName);
        if (resolved.Name == _targetName && _tweenT >= 1f)
        {
            return;
        }

        _from = Current;
        _target = resolved;
        _targetName = resolved.Name;
        _tweenSeconds = MathF.Max(0.01f, seconds);
        _tweenT = 0f;
    }

    private static float Ease(float t)
    {
        var x = Math.Clamp(t, 0f, 1f);
        return x * x * (3f - (2f * x));
    }
}
