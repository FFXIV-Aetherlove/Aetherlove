using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Shared.Aetherling;
using AetherOS.PetKit.Engine;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>
/// The living layer: everything the creature does because of the world rather than because of a tap.
/// Two channels share this tick. Watching (Taught by Watching, from the prototype's pilot): the player's
/// own emotes fill per-emote meters the SERVER owns, one unlock a day at most, with a puzzled first
/// sighting, clumsy practice attempts and an earned eureka along the way. Chatter: ambient glyphs on
/// genuine transitions only, weather turning, a new zone, a new job, the mood moving, plus a rare idle
/// musing, so the creature speaks every few minutes when something is actually happening.
///
/// <para>Runs from <see cref="PetRuntime"/>'s own frame-guarded tick, so whichever surface draws the
/// creature keeps it alive. Hub replies are parked in pending fields and drained here, never acted on
/// from their continuations, because everything downstream reaches ImGui state.</para>
/// </summary>
internal sealed class PetLiveliness(IAetherlingHost host, PetRuntime pet)
{
    /// <summary>Row id per learnable key, resolved from the game's own Emote sheet through the host at
    /// first use. Built rather than typed: a hand-copied table is how the prototype shipped three wrong
    /// ids, and it cannot survive a batch of forty-odd new emotes.</summary>
    private Dictionary<uint, string>? _emoteRows;

    private Dictionary<uint, string> EmoteRows(IAetherlingHost host)
    {
        if (_emoteRows is not null)
        {
            return _emoteRows;
        }
        var map = new Dictionary<uint, string>();
        foreach (var def in EmoteChoreographies.All)
        {
            if (def.GameEmote.Length == 0)
            {
                continue;
            }
            var row = host.EmoteRowForCommand(def.GameEmote);
            if (row != 0)
            {
                map[row] = def.Key;
            }
        }
        _emoteRows = map;
        return map;
    }

    private const float MirrorChance = 0.34f;
    private const float AttemptChance = 0.55f;
    private const float MirrorGapSeconds = 8f;

    /// <summary>How rarely a sighting may be reported for the same emote. The charm above is per-frame and
    /// costs nothing; this is the only thing that ever leaves the machine, so it is paced for privacy
    /// rather than for the ledger: five minutes between reports of one emote, whatever you do in between.</summary>
    private const double ReportGapSeconds = 300.0;

    /// <summary>Ambient musing cadence bounds: the "every few minutes" voice when nothing else spoke.</summary>
    private const float MuseMinSeconds = 150f;
    private const float MuseMaxSeconds = 300f;

    private static readonly string[] Musings = ["note", "ring", "waves", "heart"];

    /// <summary>Consent for the watching channel; the app owns persistence.</summary>
    public bool LearnsEmotes = true;

    private long _seqSeen = -1;
    private float _mirrorCooldown;
    private readonly Dictionary<string, DateTime> _lastReportAt = [];
    private bool _reportInFlight;

    /// <summary>A learn confirmed by the server, parked by the continuation and drained on the tick.</summary>
    private string? _pendingEureka;

    private MoodLevel? _moodSeen;
    private byte _weatherSeen;
    private uint _territorySeen;
    private string _jobSeen = "";
    private float _museIn = MuseMaxSeconds;

    /// <summary>Raised on the tick after the server confirms a learn, with the emote key: the app shows
    /// the toast, because strings live above the engine.</summary>
    public event Action<string>? EmoteLearned;

    public void Tick(float dt)
    {
        _mirrorCooldown = MathF.Max(0f, _mirrorCooldown - dt);

        if (host.Snapshot is not { HatchedAtUtc: not null } snapshot)
        {
            return;
        }

        if (_pendingEureka is { } learned)
        {
            _pendingEureka = null;
            OnEureka(learned);
        }

        TickWatching(snapshot);
        TickChatter(dt);
    }

    // ------------------------------------------------------------------ watching

    private void TickWatching(AetherlingDto snapshot)
    {
        if (!LearnsEmotes)
        {
            return;
        }

        var seq = host.LastEmoteSequence;
        if (seq == _seqSeen)
        {
            return;
        }

        // An emote performed before anyone was watching is old news: the first read only records.
        var first = _seqSeen < 0;
        _seqSeen = seq;
        if (first || host.LastEmoteRowId == 0 || !EmoteRows(host).TryGetValue(host.LastEmoteRowId, out var key))
        {
            return;
        }

        var def = EmoteChoreographies.Find(key);
        if (def is null)
        {
            return;
        }

        var progress = snapshot.Emotes?.Emotes.FirstOrDefault(e => e.Key == key);
        if (progress?.LearnedAtUtc is not null)
        {
            // Learned means done: from here the emote is mirrored entirely on this machine and NOTHING
            // about it is ever sent again. The watching channel exists to learn, not to log a person.
            // Its own now: joins in with you, sometimes. Every time would be a mirror; a third of the
            // time is a companion.
            if (_mirrorCooldown <= 0f && Random.Shared.NextSingle() < MirrorChance && pet.PlayEmote(def))
            {
                _mirrorCooldown = MirrorGapSeconds;
            }
            return;
        }

        // The ledger half rides the server; the client only paces the traffic. One report per emote per
        // ReportGapSeconds, so a session of heavy emoting sends a handful of rows rather than a transcript.
        var now = DateTime.UtcNow;
        if (!_reportInFlight
            && (!_lastReportAt.TryGetValue(key, out var last) || (now - last).TotalSeconds >= ReportGapSeconds))
        {
            _lastReportAt[key] = now;
            _reportInFlight = true;
            var wasLearned = progress?.LearnedAtUtc is not null;
            _ = host.ReportEmoteSightingAsync(key).ContinueWith(t =>
            {
                _reportInFlight = false;
                // Park, never act: the eureka reaches ImGui and game state and belongs on the tick.
                if (!wasLearned && t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                    && t.Result?.Emotes?.Emotes.FirstOrDefault(e => e.Key == key)?.LearnedAtUtc is not null)
                {
                    _pendingEureka = key;
                }
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        // The charm half, never bottlenecked by the ledger. First sighting: what was THAT? After:
        // sometimes a clumsy attempt, the real curves at 60% with an unsure mouth.
        if ((progress?.Sightings ?? 0) == 0)
        {
            pet.AuditionGlyph("query");
            if (_mirrorCooldown <= 0f && EmoteChoreographies.Find("think") is { } think && pet.PlayEmote(think))
            {
                _mirrorCooldown = MirrorGapSeconds;
            }
            return;
        }

        if (_mirrorCooldown <= 0f && Random.Shared.NextSingle() < AttemptChance && pet.PlayEmote(def, 0.6f))
        {
            _mirrorCooldown = MirrorGapSeconds;
        }
    }

    /// <summary>The eureka: the once-per-emote-ever moment, so it takes the audition door and never a
    /// rate limit. The server already wrote the ledger; this is purely the celebration.</summary>
    private void OnEureka(string key)
    {
        pet.AuditionGlyph("burst");
        if (EmoteChoreographies.Find(key) is { } def)
        {
            pet.PlayEmote(def);
        }
        EmoteLearned?.Invoke(key);
    }

    // ------------------------------------------------------------------ chatter

    private void TickChatter(float dt)
    {
        // Transitions only, each read every tick and compared to what was seen: a glyph marks a CHANGE,
        // never a condition, or the chatter quietly becomes a status display.
        var mood = pet.Mood;
        if (_moodSeen is { } seenMood && seenMood != mood)
        {
            var word = GlyphShapes.ForMood(mood);
            if (word.Length > 0)
            {
                pet.ShowGlyph(word, ambient: true);
            }
        }
        _moodSeen = mood;

        var territory = host.TerritoryId;
        if (_territorySeen != 0 && territory != 0 && territory != _territorySeen)
        {
            // A new zone resets the weather memory too, or the first reading over there would
            // double-announce the arrival.
            _weatherSeen = 0;
            pet.ShowGlyph("notice", ambient: true);
        }
        _territorySeen = territory;

        var weather = host.CurrentWeatherId;
        if (_weatherSeen != 0 && weather != 0 && weather != _weatherSeen)
        {
            var word = WeatherGlyph(weather);
            if (word.Length > 0)
            {
                pet.ShowGlyph(word, ambient: true);
            }
        }
        if (weather != 0)
        {
            _weatherSeen = weather;
        }

        var job = host.CurrentJobAbbreviation;
        if (_jobSeen.Length > 0 && job.Length > 0 && job != _jobSeen)
        {
            pet.ShowGlyph("jobmark", ambient: true);
        }
        if (job.Length > 0)
        {
            _jobSeen = job;
        }

        // The idle musing: a rare small sound of being alive when nothing has happened for minutes.
        // Never while napping (rest says nothing) and never below Content.
        _museIn -= dt;
        if (_museIn <= 0f)
        {
            _museIn = MuseMinSeconds + (Random.Shared.NextSingle() * (MuseMaxSeconds - MuseMinSeconds));
            if (!pet.Napping && pet.Mood >= MoodLevel.Content)
            {
                pet.ShowGlyph(Musings[Random.Shared.Next(Musings.Length)], ambient: true);
            }
        }
    }

    /// <summary>The game's weather id in the creature's vocabulary. Only the weathers it has a word for;
    /// everything else is silence, which is the honest reading of fog at dawn.</summary>
    private static string WeatherGlyph(byte weatherId) => weatherId switch
    {
        1 or 2 => "clear",
        3 or 4 => "overcast",
        5 or 6 => "breeze",
        7 or 8 => "rainy",
        9 or 10 => "lightning",
        15 or 16 => "snow",
        _ => string.Empty,
    };
}
