using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Racing;
using AetherLove.Shared.Racing.Cards;
using AetherOS.Apps.Racer.Screens.Cards;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>Racing cards on the stage. A race stored with a support version is replayed through the same
/// resolver the server ran, over both engine copies the screen steps; a card-free race never builds a session
/// and steps the engine exactly as before. A race stored under a version this build cannot replay skips the
/// running phase and shows its placements with a notice. Only the player's Lumi carries cards. On the stage,
/// the player's Gold gets a notice card and a halo on its runner when it fires, both timed on the race's own
/// clock so a replay shows them at the same moment; the parade shows the player's hand under its runner and
/// the result page says what the player's Gold did.</summary>
internal sealed partial class RaceScreen
{
    /// <summary>How long a runner's halo stays up after its Gold fires, how long it takes to fade at the end,
    /// and the period of its shrinking ground ring.</summary>
    private const float CardHaloSeconds = 4.4f;
    private const float CardHaloFadeSeconds = 0.9f;
    private const float CardHaloPulseSeconds = 1.3f;

    /// <summary>The halo in body sizes: its centre above the feet, its reach and strength, and the ground ring's
    /// radius, how far it shrinks per pulse, its squash onto the ground plane and its strength. The stroke
    /// widths are design px, and every line and arc sits on a dark backing that much wider.</summary>
    private const float CardHaloLift = 0.28f;
    private const float CardHaloReach = 1.05f;
    private const float CardHaloAlpha = 0.7f;
    private const float CardRingRadius = 0.66f;
    private const float CardRingShrink = 0.12f;
    private const float CardRingSquash = 0.36f;
    private const float CardRingAlpha = 0.9f;
    private const float CardRingStroke = 2.2f;
    private const float CardBackingWiden = 2f;
    private const float CardBackingAlpha = 0.5f;
    private const float CardBrightShare = 0.45f;

    /// <summary>A recovery Gold's marks on its ring: how many, their size in body sizes, and how fast they
    /// orbit.</summary>
    private const int CardOrbitMarks = 3;
    private const float CardOrbitMarkRadius = 0.045f;
    private const float CardOrbitTurnsPerSecond = 0.45f;
    private const int CardOrbitSegments = 10;

    /// <summary>A speed Gold's wake, in body sizes: the height it streams from, how long each stroke is, how far
    /// to each side of the body it starts and how much further out its tail opens; then its stroke, how many
    /// pieces it tapers over and how faint and thin its tail gets, the dash that runs down each stroke (its share
    /// of the stroke and runs per second) and the smallest heading that still reads as a direction.</summary>
    private const int CardWakeStrokes = 2;
    private const float CardWakeLift = 0.36f;
    private const float CardWakeLength = 0.75f;
    private const float CardWakeSpread = 0.4f;
    private const float CardWakeSplay = 0.14f;
    private const float CardWakeStroke = 2.6f;
    private const int CardWakePieces = 4;
    private const float CardWakeTailAlpha = 0.2f;
    private const float CardWakeTailWidth = 0.45f;
    private const float CardWakeDashShare = 0.3f;
    private const float CardWakeDashRate = 1.6f;
    private const float CardWakeDashWiden = 1f;
    private const float CardWakeMinHeading = 0.0001f;

    /// <summary>A stumble-rescue Gold's supporting arc under the body, in body sizes: its centre above the feet,
    /// radius and squash, the part of the lower half it spans (as fractions of a half turn), how much it
    /// breathes and how slowly, and the size of the marks at its ends.</summary>
    private const float CardSupportLift = 0.12f;
    private const float CardSupportRadius = 0.58f;
    private const float CardSupportSquash = 0.5f;
    private const float CardSupportFrom = 0.08f;
    private const float CardSupportTo = 0.92f;
    private const float CardSupportSway = 0.04f;
    private const float CardSupportSwaySeconds = 1.6f;
    private const float CardSupportStroke = 2.6f;
    private const float CardSupportEndRadius = 0.05f;
    private const int CardSupportSegments = 16;

    private static readonly Vector4 CardBackingInk = new(0.02f, 0.02f, 0.04f, 1f);

    /// <summary>The Gold operations a halo tells apart: recovery (stamina or speed back), speed (a sprint or a
    /// raised top speed) and stumble rescue.</summary>
    private enum CardHaloFamily
    {
        Recovery,
        Speed,
        StumbleRescue,
    }

    /// <summary>The notice plate, in design px: its widest, its margin from the stage edge, the gap above the
    /// narration lines and the rail, the gap between two stacked notices, and its padding and corners.</summary>
    private const float NoticeWidth = 252f;
    private const float NoticeMargin = 10f;
    private const float NoticeBottom = 84f;
    private const float NoticeStackGap = 8f;
    private const float NoticePad = 8f;
    private const float NoticeMinHeight = 80f;
    private const float NoticeRounding = 8f;
    private const float NoticeBorder = 2f;
    private const float NoticeLineGap = 2f;
    private const float NoticeTextGap = 9f;

    /// <summary>The notice's picture tile: its width, corners, frame stroke, the thumbnail's inset and the
    /// element dot under it.</summary>
    private const float NoticeArtWidth = 46f;
    private const float NoticeArtRounding = 3f;
    private const float NoticeArtBorder = 2f;
    private const float NoticeArtInset = 3f;
    private const float NoticeDotRadius = 5f;
    private const int NoticeDotSegments = 6;

    /// <summary>The notice's fade at the end of its reading window, the picture's flight from the runner, how
    /// close to the stage edge that flight may start, and the smallest a name is squeezed before it clips.</summary>
    private const float NoticeFadeSeconds = 0.6f;
    private const float NoticeFlySeconds = 0.45f;
    private const float NoticeFlyEdge = 12f;
    private const float NoticeMinTextScale = 0.72f;

    /// <summary>A notice that started later than its card fired waited for a slot; it does not fly in.</summary>
    private const float NoticeQueuedEpsilon = 0.01f;

    private static readonly Vector4 NoticeFill = new(0.035f, 0.045f, 0.065f, 0.96f);
    private static readonly Vector4 NoticeArtFill = new(0.28f, 0.19f, 0.07f, 1f);
    private static readonly Vector4 NoticeArtEdge = new(0.96f, 0.79f, 0.40f, 1f);
    private static readonly Vector4 NoticeGoldInk = new(0.95f, 0.80f, 0.45f, 1f);
    private static readonly Vector4 NoticeCardInk = new(1f, 1f, 1f, 1f);

    /// <summary>The parade hand, in design px: the widest and narrowest face, the gap between faces, the gap
    /// under the name plate and the clearance above the gantry, which leaves room for a lit lamp's glow. The
    /// plate pad matches <see cref="RaceLabel"/>; the gantry top matches <see cref="DrawStartLights"/>, which
    /// the faces must stay above.</summary>
    private const float LineupFaceMaxWidth = 40f;
    private const float LineupFaceMinWidth = 26f;
    private const float LineupFaceGap = 6f;
    private const float LineupFaceBelowPlate = 5f;
    private const float LineupAboveGantry = 14f;
    private const float LineupPlatePad = 5f;
    private const float LineupGantryTop = 120f;

    /// <summary>How far above its feet a paraded body reaches, in body sizes, with room for a tall form.</summary>
    private const float LineupBodyReach = 0.95f;

    /// <summary>The result page's card lines: the gap above each line, the side inset the headline uses, the
    /// smallest a line is squeezed before it wraps instead, and the step a wrapped line is squeezed by to keep
    /// within its rows.</summary>
    private const float ResultCardLineGap = 3f;
    private const float ResultCardInset = 18f;
    private const float ResultCardMinScale = 0.8f;
    private const float ResultCardScaleStep = 0.1f;

    /// <summary>The rows every card line together may take when a stamp panel follows them: the panel and the
    /// Continue button under it leave room for no more.</summary>
    private const int ResultCardStampRows = 2;
    private const int ResultCardFreeRows = int.MaxValue;
    private const int ResultCardWrapCache = 16;

    private RaceSupportSession? _cardSession;
    private RaceCardNotices? _notices;
    private int _cardPlayerSlot = -1;
    private bool _replayRefused;
    private int _noticeCursor;
    private readonly CardFaceRenderer _lineupFaces = new();
    private readonly Dictionary<(string Text, float Room, int Rows, float FontPx), (string[] Lines, float Scale)> _resultCardWraps = new();

    /// <summary>Resolves the race's cards before anything steps. The resolved copy is run to its end here, with
    /// its own session when the race carries cards, so the playback rate comes from the same run the server
    /// stored.</summary>
    private void BeginCards(LumiRaceDto dto, AetherRaceLive.Race live, AetherRaceLive.Race resolved)
    {
        EndCards();
        if (string.IsNullOrEmpty(dto.SupportVersion))
        {
            resolved.RunToEnd();
            return;
        }

        if (!RaceCardVersions.CanReplay(dto.SupportVersion))
        {
            _replayRefused = true;
            return;
        }

        var hands = HandsOf(dto);
        _cardSession = RaceSupportSession.Create(live, hands, dto.WeatherKey, dto.Seed);
        var outcome = RaceSupportSession.Create(resolved, hands, dto.WeatherKey, dto.Seed);
        while (!outcome.IsComplete)
        {
            outcome.Step();
        }

        _notices = new RaceCardNotices(dto.Field.Length);
        _cardPlayerSlot = dto.PlayerSlot;
    }

    private void EndCards()
    {
        _cardSession = null;
        _notices = null;
        _cardPlayerSlot = -1;
        _replayRefused = false;
        _noticeCursor = 0;
        _resultCardWraps.Clear();
    }

    /// <summary>One engine tick, through the resolver when the race carries cards.</summary>
    private void StepRace(AetherRaceLive.Race race)
    {
        if (_cardSession is { } session)
        {
            session.Step();
            return;
        }

        race.Step();
    }

    /// <summary>Queues a notice for the player's Gold when it fired since the last frame, at the race time it fired
    /// rather than the frame it was seen, so catching up many ticks at once still places the notice where it
    /// belongs. Only the player's own Lumi uses cards; a stored race whose rivals carried hands still replays
    /// through them but shows no notice or halo for a rival.</summary>
    private void ObserveCardNotices()
    {
        if (_cardSession is not { } session || _notices is not { } notices)
        {
            return;
        }

        var events = session.Events;
        for (; _noticeCursor < events.Count; _noticeCursor++)
        {
            var line = events[_noticeCursor];
            if (line.Outcome == RaceSupportOutcome.Fired && line.Slot == _cardPlayerSlot)
            {
                notices.Observe(line.Slot, PresentationSeconds(line.Time));
            }
        }
    }

    /// <summary>The field's stored hands in slot order, the only input the resolver reads.</summary>
    private static RaceCardHand[] HandsOf(LumiRaceDto dto)
    {
        var hands = new RaceCardHand[dto.Field.Length];
        Array.Fill(hands, RaceCardHand.Empty);
        foreach (var entry in dto.Field)
        {
            if (entry.Slot < 0 || entry.Slot >= hands.Length)
            {
                continue;
            }

            hands[entry.Slot] = RaceCardHand.From(entry.GoldCardId, entry.GoldLevel, entry.SilverCardIds, Levels(entry.SilverLevels));
        }

        return hands;
    }

    private static int[]? Levels(short[]? levels)
    {
        if (levels is null)
        {
            return null;
        }

        var result = new int[levels.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            result[i] = levels[i];
        }

        return result;
    }

    /// <summary>A race time as the seconds it takes to show at the race's playback pace.</summary>
    private float PresentationSeconds(float raceSeconds) => raceSeconds / MathF.Max(1f, _playbackRate);

    private float CardClock(AetherRaceLive.Race race) => PresentationSeconds(race.Time);

    /// <summary>A soft element halo on a runner whose Gold just fired, drawn under its body, with a shape for
    /// the Gold's operation family: a shrinking ground ring with orbiting marks for recovery, two wake strokes
    /// behind the runner for speed, and a supporting arc under the body for a stumble rescue. Reduced motion
    /// holds every shape still.</summary>
    private void DrawCardHalo(OsAppContext ctx, ImDrawListPtr dl, AetherRaceLive.Race race, int slot, Vector2 feet, float petSize)
    {
        if (_cardSession is not { } session || _notices is not { } notices || slot >= notices.Observed.Length)
        {
            return;
        }

        var observed = notices.Observed[slot];
        if (observed < 0f)
        {
            return;
        }

        var age = CardClock(race) - observed;
        if (age < 0f || age >= CardHaloSeconds || session.HandOf(slot).Gold.Card is not { } card)
        {
            return;
        }

        var tint = Rendering.ElementFx.For(card.Element).Tint;
        var alpha = Math.Clamp((CardHaloSeconds - age) / CardHaloFadeSeconds, 0f, 1f);
        RacerChrome.Halo(dl, feet - new Vector2(0f, petSize * CardHaloLift), petSize * CardHaloReach, tint, alpha * CardHaloAlpha);

        var ink = Rendering.ElementFx.U32(tint with { W = alpha * CardRingAlpha });
        var bright = Rendering.ElementFx.U32(Vector4.Lerp(tint, Vector4.One, CardBrightShare) with { W = alpha });
        var backing = Rendering.ElementFx.U32(CardBackingInk with { W = alpha * CardBackingAlpha });
        switch (HaloFamily(card.Operation.Kind))
        {
            case CardHaloFamily.Speed:
                DrawCardWake(ctx, dl, race, slot, feet, petSize, age, tint, alpha, bright);
                break;
            case CardHaloFamily.StumbleRescue:
                DrawCardSupport(ctx, dl, feet, petSize, age, ink, bright, backing);
                break;
            default:
                DrawCardRecovery(ctx, dl, feet, petSize, age, ink, bright, backing);
                break;
        }
    }

    private static CardHaloFamily HaloFamily(string operation) => operation switch
    {
        RaceCardOperationKinds.Rush or RaceCardOperationKinds.NativeBurst or RaceCardOperationKinds.PaceSurge or RaceCardOperationKinds.Ghost => CardHaloFamily.Speed,
        RaceCardOperationKinds.SoftenStumble => CardHaloFamily.StumbleRescue,
        _ => CardHaloFamily.Recovery,
    };

    /// <summary>Recovery: the ground ring shrinks on a loop while three marks orbit on it.</summary>
    private static void DrawCardRecovery(OsAppContext ctx, ImDrawListPtr dl, Vector2 feet, float petSize, float age, uint ink, uint bright, uint backing)
    {
        var pulse = ctx.ReduceMotion ? 0f : (age % CardHaloPulseSeconds) / CardHaloPulseSeconds;
        var reach = petSize * (CardRingRadius - (CardRingShrink * pulse));
        var radius = new Vector2(reach, reach * CardRingSquash);
        StrokeEllipse(dl, feet, radius, backing, Px(CardRingStroke + CardBackingWiden));
        StrokeEllipse(dl, feet, radius, ink, Px(CardRingStroke));

        var turn = ctx.ReduceMotion ? 0f : age * CardOrbitTurnsPerSecond * MathF.Tau;
        var mark = petSize * CardOrbitMarkRadius;
        for (var i = 0; i < CardOrbitMarks; i++)
        {
            var angle = turn + (i * MathF.Tau / CardOrbitMarks);
            var at = feet + new Vector2(MathF.Cos(angle) * radius.X, MathF.Sin(angle) * radius.Y);
            dl.AddCircleFilled(at, mark + Px(CardBackingWiden * 0.5f), backing, CardOrbitSegments);
            dl.AddCircleFilled(at, mark, bright, CardOrbitSegments);
        }
    }

    /// <summary>Speed: two strokes stream back from either side of the body along the runner's drawn heading,
    /// fading and thinning toward their tails, each with a bright dash running down it.</summary>
    private void DrawCardWake(OsAppContext ctx, ImDrawListPtr dl, AetherRaceLive.Race race, int slot, Vector2 feet, float petSize, float age,
        Vector4 tint, float alpha, uint bright)
    {
        var runner = race.Runners[slot];
        var sample = race.Track.AtRoad(_drawnS[slot], runner.Post & 1, true);
        var forward = _cam.ToScreenDelta(new Vector2(MathF.Cos(sample.Heading), MathF.Sin(sample.Heading)));
        forward = forward.LengthSquared() > CardWakeMinHeading ? Vector2.Normalize(forward) : -Vector2.UnitY;
        var side = new Vector2(-forward.Y, forward.X);
        var body = feet - new Vector2(0f, petSize * CardWakeLift);
        for (var i = 0; i < CardWakeStrokes; i++)
        {
            var outward = (i - ((CardWakeStrokes - 1) * 0.5f)) * 2f;
            var start = body + (side * outward * petSize * CardWakeSpread);
            var end = start - (forward * petSize * CardWakeLength) + (side * outward * petSize * CardWakeSplay);
            for (var piece = 0; piece < CardWakePieces; piece++)
            {
                var near = (float)piece / CardWakePieces;
                var far = (float)(piece + 1) / CardWakePieces;
                var fade = 1f - ((1f - CardWakeTailAlpha) * near);
                var thin = 1f - ((1f - CardWakeTailWidth) * near);
                var from = Vector2.Lerp(start, end, near);
                var to = Vector2.Lerp(start, end, far);
                dl.AddLine(from, to, Rendering.ElementFx.U32(CardBackingInk with { W = alpha * CardBackingAlpha * fade }), Px((CardWakeStroke * thin) + CardBackingWiden));
                dl.AddLine(from, to, Rendering.ElementFx.U32(tint with { W = alpha * CardRingAlpha * fade }), Px(CardWakeStroke * thin));
            }

            if (ctx.ReduceMotion)
            {
                continue;
            }

            var run = ((age * CardWakeDashRate) + ((float)i / CardWakeStrokes)) % 1f;
            var dashFrom = Vector2.Lerp(start, end, run * (1f - CardWakeDashShare));
            var dashTo = Vector2.Lerp(start, end, (run * (1f - CardWakeDashShare)) + CardWakeDashShare);
            dl.AddLine(dashFrom, dashTo, bright, Px(CardWakeStroke + CardWakeDashWiden));
        }
    }

    /// <summary>Stumble rescue: an arc cups the body from below, gently breathing, with a mark at each end.</summary>
    private static void DrawCardSupport(OsAppContext ctx, ImDrawListPtr dl, Vector2 feet, float petSize, float age, uint ink, uint bright, uint backing)
    {
        var sway = ctx.ReduceMotion ? 0f : MathF.Sin(age * MathF.Tau / CardSupportSwaySeconds) * CardSupportSway;
        var reach = petSize * (CardSupportRadius + sway);
        var centre = feet - new Vector2(0f, petSize * CardSupportLift);
        Span<Vector2> points = stackalloc Vector2[CardSupportSegments + 1];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = MathF.PI * (CardSupportFrom + ((CardSupportTo - CardSupportFrom) * i / CardSupportSegments));
            points[i] = centre + new Vector2(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach * CardSupportSquash);
        }

        dl.AddPolyline(ref points[0], points.Length, backing, ImDrawFlags.None, Px(CardSupportStroke + CardBackingWiden));
        dl.AddPolyline(ref points[0], points.Length, ink, ImDrawFlags.None, Px(CardSupportStroke));
        var mark = petSize * CardSupportEndRadius;
        dl.AddCircleFilled(points[0], mark, bright, CardOrbitSegments);
        dl.AddCircleFilled(points[^1], mark, bright, CardOrbitSegments);
    }

    /// <summary>Every visible Gold notice: a plate at the stage's right edge, stacked upward in two slots, with the
    /// card's picture, the runner's name, the used-a-Gold-card line and the card's name. A fresh notice's
    /// picture flies in from its runner unless motion is reduced.</summary>
    private void DrawCardNotices(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherRaceLive.Race race, float petSize)
    {
        if (_cardSession is not { } session || _notices is not { } notices || _result is not { } result)
        {
            return;
        }

        var clock = CardClock(race);
        var packReady = false;
        var measured = false;
        var bodyHeight = 0f;
        var captionHeight = 0f;
        for (var slot = 0; slot < notices.Starts.Length; slot++)
        {
            var start = notices.Starts[slot];
            if (start < 0f)
            {
                continue;
            }

            var age = clock - start;
            if (age < 0f || age >= RaceCardNotices.Duration || session.HandOf(slot).Gold.Card is not { } card)
            {
                continue;
            }

            if (!measured)
            {
                measured = true;
                packReady = CardArt.PackReady(ctx);
                using (RacerFonts.Get(RacerTextSize.Body)?.Push())
                {
                    bodyHeight = ImGui.GetTextLineHeight();
                }

                using (RacerFonts.Get(RacerTextSize.Caption)?.Push())
                {
                    captionHeight = ImGui.GetTextLineHeight();
                }
            }

            var pad = Px(NoticePad);
            var width = MathF.Min(Px(NoticeWidth), size.X - Px(NoticeMargin * 2f));
            var height = MathF.Max(Px(NoticeMinHeight), (pad * 2f) + (bodyHeight * 2f) + captionHeight + (Px(NoticeLineGap) * 2f));
            var bottom = origin.Y + size.Y - Px(NoticeBottom);
            var at = new Vector2(
                origin.X + size.X - width - Px(NoticeMargin),
                MathF.Max(origin.Y + Px(NoticeMargin), bottom - height - (notices.Slots[slot] * (height + Px(NoticeStackGap)))));
            var alpha = Math.Clamp((RaceCardNotices.Duration - age) / NoticeFadeSeconds, 0f, 1f);
            var tint = Rendering.ElementFx.For(card.Element).Tint;
            dl.AddRectFilled(at, at + new Vector2(width, height), Rendering.ElementFx.U32(NoticeFill with { W = NoticeFill.W * alpha }), Px(NoticeRounding));
            dl.AddRect(at, at + new Vector2(width, height), Rendering.ElementFx.U32(tint with { W = alpha }), Px(NoticeRounding),
                ImDrawFlags.RoundCornersAll, Px(NoticeBorder));

            var artSize = new Vector2(Px(NoticeArtWidth), height - (pad * 2f));
            var artAt = at + new Vector2(pad);
            var fresh = start - notices.Observed[slot] < NoticeQueuedEpsilon;
            if (!ctx.ReduceMotion && fresh && age < NoticeFlySeconds && slot < _screens.Length)
            {
                var edge = new Vector2(Px(NoticeFlyEdge));
                var from = Vector2.Clamp(_screens[slot] - new Vector2(0f, petSize * CardHaloLift), origin + edge, origin + size - edge);
                var flight = 1f - (age / NoticeFlySeconds);
                artAt = Vector2.Lerp(from - (artSize * 0.5f), artAt, 1f - (flight * flight * flight));
            }

            DrawNoticeArt(ctx, dl, card, artAt, artSize, tint, alpha, packReady);

            var block = (bodyHeight * 2f) + captionHeight + (Px(NoticeLineGap) * 2f);
            var textAt = at + new Vector2(pad + artSize.X + Px(NoticeTextGap), MathF.Max(pad, (height - block) * 0.5f));
            var textEnd = at + new Vector2(width - pad, height - pad);
            var textWidth = textEnd.X - textAt.X;
            var name = slot < result.Race.Field.Length ? result.Race.Field[slot].Name : string.Empty;
            dl.PushClipRect(textAt, textEnd, true);
            NoticeLine(dl, RacerTextSize.Body, name, textAt, textWidth, RunnerNameInk(slot) with { W = alpha });
            NoticeLine(dl, RacerTextSize.Caption, ctx.Localize("os.racer_notice_gold_used"),
                textAt + new Vector2(0f, bodyHeight + Px(NoticeLineGap)), textWidth, NoticeGoldInk with { W = alpha });
            NoticeLine(dl, RacerTextSize.Body, CardStrings.NameOf(ctx, card),
                textAt + new Vector2(0f, bodyHeight + captionHeight + (Px(NoticeLineGap) * 2f)), textWidth, NoticeCardInk with { W = alpha });
            dl.PopClipRect();
        }
    }

    /// <summary>The notice's picture tile: a gold frame, the card's cell from the Gold atlas (or the element
    /// placeholder while the pack is not on hand) and the element dot under it.</summary>
    private static void DrawNoticeArt(OsAppContext ctx, ImDrawListPtr dl, RaceCard card, Vector2 at, Vector2 size, Vector4 tint, float alpha, bool packReady)
    {
        var rounding = Px(NoticeArtRounding);
        dl.AddRectFilled(at, at + size, Rendering.ElementFx.U32(NoticeArtFill with { W = alpha }), rounding);
        dl.AddRect(at, at + size, Rendering.ElementFx.U32(NoticeArtEdge with { W = alpha }), rounding, ImDrawFlags.RoundCornersAll, Px(NoticeArtBorder));

        var inset = Px(NoticeArtInset);
        var side = size.X - (inset * 2f);
        var thumbTl = at + new Vector2(inset);
        var thumbBr = thumbTl + new Vector2(side);
        if (!CardArt.DrawGoldThumbnail(ctx, dl, card.Id, thumbTl, thumbBr, Rendering.ElementFx.U32(Vector4.One with { W = alpha }), packReady))
        {
            CardArt.DrawPlaceholder(dl, card.Element, thumbTl, thumbBr, side / CardFaceLayout.Width, alpha);
        }

        var dot = new Vector2(at.X + (size.X * 0.5f), (thumbBr.Y + at.Y + size.Y) * 0.5f);
        dl.AddCircleFilled(dot, Px(NoticeDotRadius), Rendering.ElementFx.U32(tint with { W = alpha }), NoticeDotSegments);
    }

    /// <summary>One line of a notice in the given size, squeezed to fit its width down to a floor, then clipped
    /// by the caller.</summary>
    private static void NoticeLine(ImDrawListPtr dl, RacerTextSize textSize, string text, Vector2 at, float maxWidth, Vector4 ink)
    {
        using var font = RacerFonts.Get(textSize)?.Push();
        var measured = ImGui.CalcTextSize(text).X;
        var scale = measured > maxWidth && measured > 0f ? MathF.Max(NoticeMinTextScale, maxWidth / measured) : 1f;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, at, Rendering.ElementFx.U32(ink), text);
    }

    /// <summary>Where the line-up stands so the player's hand fits between its name plate and the start gantry.
    /// A player without cards keeps the usual stage. Otherwise the field first rises toward the subtitle, then
    /// the faces shrink to their floor, and only then does the body shrink, so the faces never reach the gantry
    /// on any theme or phone size. The subtitle is measured in the current font, the one it is drawn in.</summary>
    private LineupFit FitLineup(OsAppContext ctx, Vector2 origin, Vector2 size, LumiRaceDto dto, float petSize, float feetY)
    {
        var fit = new LineupFit(petSize, feetY, Px(LineupFaceMaxWidth));
        if (_cardSession is not { } session || dto.PlayerSlot < 0 || dto.PlayerSlot >= dto.Field.Length || session.HandOf(dto.PlayerSlot).IsEmpty)
        {
            return fit;
        }

        var gap = Px(LineupFaceBelowPlate);
        var ceiling = origin.Y + Px(ParadeSubtitleTop) + ImGui.GetTextLineHeight() + gap;
        var floor = origin.Y + size.Y - Px(LineupGantryTop) - Px(LineupAboveGantry);
        float belowName;
        using (ctx.HeadingFont?.Push())
        {
            belowName = (ImGui.GetTextLineHeight() * 0.5f) + Px(LineupPlatePad) + gap;
        }

        var over = feetY + (petSize * LineupNameBelowFeet) + belowName + (fit.FaceWidth * CardFaceLayout.Ratio) - floor;
        if (over <= 0f)
        {
            return fit;
        }

        var feet = MathF.Max(feetY - over, MathF.Min(feetY, ceiling + (petSize * LineupBodyReach)));
        var room = floor - (feet + (petSize * LineupNameBelowFeet) + belowName);
        var width = MathF.Min(fit.FaceWidth, room / CardFaceLayout.Ratio);
        if (width >= Px(LineupFaceMinWidth))
        {
            return new LineupFit(petSize, feet, width);
        }

        var minWidth = Px(LineupFaceMinWidth);
        var body = (floor - ceiling - belowName - (minWidth * CardFaceLayout.Ratio)) / (LineupBodyReach + LineupNameBelowFeet);
        body = Math.Clamp(body, 0f, petSize);
        return new LineupFit(body, floor - belowName - (minWidth * CardFaceLayout.Ratio) - (body * LineupNameBelowFeet), minWidth);
    }

    /// <summary>The line-up's body size and feet line, and the width of the player's hand faces.</summary>
    private readonly record struct LineupFit(float PetSize, float FeetY, float FaceWidth);

    /// <summary>The player's hand as three small faces under its name in the line-up, at the width
    /// <see cref="FitLineup"/> chose. Rivals' hands are never shown.</summary>
    private void DrawLineupHand(OsAppContext ctx, ImDrawListPtr dl, LumiRaceDto dto, int slot, Vector2 nameCentre, float faceWidth)
    {
        if (slot != dto.PlayerSlot || _cardSession is not { } session || slot >= dto.Field.Length)
        {
            return;
        }

        var hand = session.HandOf(slot);
        if (hand.IsEmpty)
        {
            return;
        }

        float plateHalf;
        using (ctx.HeadingFont?.Push())
        {
            plateHalf = (ImGui.GetTextLineHeight() * 0.5f) + Px(LineupPlatePad);
        }

        var top = nameCentre.Y + plateHalf + Px(LineupFaceBelowPlate);
        var face = new Vector2(faceWidth, faceWidth * CardFaceLayout.Ratio);
        var gap = Px(LineupFaceGap);
        var left = nameCentre.X - (((face.X * RaceCardHandRules.SlotCount) + (gap * (RaceCardHandRules.SlotCount - 1))) * 0.5f);
        var packReady = CardArt.PackReady(ctx);
        Span<RaceCardSlot> slots = [hand.Gold, hand.Silver1, hand.Silver2];
        for (var i = 0; i < slots.Length; i++)
        {
            var at = new Vector2(left + (i * (face.X + gap)), top);
            if (slots[i].Card is { } card)
            {
                _lineupFaces.Draw(ctx, dl, $"lineup{i}", card, slots[i].Level, at, face, false, false, packReady);
            }
            else
            {
                CardChrome.EmptyPlate(ctx, dl, at, face, string.Empty, false);
            }
        }
    }

    /// <summary>The notice under the result headline that this build cannot replay the race. What the cards did
    /// is not said here: the race itself showed it.</summary>
    private void DrawCardResult(OsAppContext ctx, Vector2 origin, Vector2 size, uint ink, bool stampFollows, ref float y)
    {
        if (_replayRefused)
        {
            var rows = stampFollows ? ResultCardStampRows : ResultCardFreeRows;
            ResultCardLine(ctx.Localize("os.racer_replay_unavailable"), origin, size, ink, rows, ref y);
        }
    }

    /// <summary>One centred caption line under the headline, in at most <paramref name="maxRows"/> rows. A line a
    /// little too wide is squeezed onto one row, because the stamp block below has little room to give. A longer
    /// one wraps at spaces, squeezed down to the floor if that keeps it within its rows, and whatever still does
    /// not fit ends in an ellipsis. Returns the rows it took.</summary>
    private int ResultCardLine(string text, Vector2 origin, Vector2 size, uint ink, int maxRows, ref float y)
    {
        var room = size.X - Px(ResultCardInset * 2f);
        y += Px(ResultCardLineGap);
        using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
        var width = ImGui.CalcTextSize(text).X;
        var lineHeight = ImGui.GetTextLineHeight();
        var fontPx = ImGui.GetFontSize();
        var dl = ImGui.GetWindowDrawList();
        if (width <= 0f || width * ResultCardMinScale <= room)
        {
            var scale = MathF.Min(1f, room / MathF.Max(1f, width));
            var at = new Vector2(origin.X + ((size.X - (width * scale)) * 0.5f), origin.Y + y + ((lineHeight * (1f - scale)) * 0.5f));
            dl.AddText(ImGui.GetFont(), fontPx * scale, new Vector2(MathF.Round(at.X), MathF.Round(at.Y)), ink, text);
            y += lineHeight;
            return 1;
        }

        var (lines, fitted) = ResultCardWrap(text, room, maxRows, fontPx);
        var pitch = lineHeight * fitted;
        for (var i = 0; i < lines.Length; i++)
        {
            var lineWidth = ImGui.CalcTextSize(lines[i]).X * fitted;
            var at = new Vector2(origin.X + ((size.X - lineWidth) * 0.5f), origin.Y + y + (i * pitch));
            dl.AddText(ImGui.GetFont(), fontPx * fitted, new Vector2(MathF.Round(at.X), MathF.Round(at.Y)), ink, lines[i]);
        }

        y += lines.Length * pitch;
        return lines.Length;
    }

    /// <summary>The rows a result line wraps to and the scale they draw at: the largest scale down to the floor
    /// at which the whole line fits its rows, else the floor with the last row cut. Cached per text, width and
    /// rows, since the result page draws every frame.</summary>
    private (string[] Lines, float Scale) ResultCardWrap(string text, float room, int maxRows, float fontPx)
    {
        var key = (text, MathF.Round(room), maxRows, fontPx);
        if (_resultCardWraps.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_resultCardWraps.Count >= ResultCardWrapCache)
        {
            _resultCardWraps.Clear();
        }

        var result = (Lines: Array.Empty<string>(), Scale: ResultCardMinScale);
        var found = false;
        for (var step = 0; !found; step++)
        {
            var scale = MathF.Max(ResultCardMinScale, 1f - (step * ResultCardScaleStep));
            found = CardText.TryWrapWhole(text, room, value => ImGui.CalcTextSize(value).X * scale, maxRows, out var lines);
            if (found)
            {
                result = (lines, scale);
            }
            else if (scale <= ResultCardMinScale)
            {
                result = (CardText.Wrap(text, room, value => ImGui.CalcTextSize(value).X * scale, maxRows), scale);
                break;
            }
        }

        _resultCardWraps[key] = result;
        return result;
    }

    /// <summary>When each Gold notice shows and where, in presentation seconds. Two slots stacked upward; a
    /// notice that fires while both are taken waits for the first to free. Presentation only: it never steps
    /// the race.</summary>
    private sealed class RaceCardNotices
    {
        public const float Duration = 4.8f;
        private const int SlotCount = 2;
        private const float NotSeen = -1f;
        private readonly float[] _available = new float[SlotCount];

        public RaceCardNotices(int runners)
        {
            Observed = new float[runners];
            Starts = new float[runners];
            Slots = new int[runners];
            Array.Fill(Observed, NotSeen);
            Array.Fill(Starts, NotSeen);
        }

        /// <summary>When each runner's Gold fired, or -1.</summary>
        public float[] Observed { get; }

        /// <summary>When each runner's notice starts showing, or -1.</summary>
        public float[] Starts { get; }

        public int[] Slots { get; }

        /// <summary>Places a runner's notice in the lowest slot that is free when it fires, or in the slot that
        /// frees first when none is. Only a runner's first call counts.</summary>
        public void Observe(int runner, float at)
        {
            if (runner < 0 || runner >= Observed.Length || Observed[runner] >= 0f)
            {
                return;
            }

            var slot = SlotFor(at);
            Observed[runner] = at;
            Starts[runner] = MathF.Max(at, _available[slot]);
            Slots[runner] = slot;
            _available[slot] = Starts[runner] + Duration;
        }

        private int SlotFor(float at)
        {
            var earliest = 0;
            for (var i = 0; i < SlotCount; i++)
            {
                if (_available[i] <= at)
                {
                    return i;
                }

                if (_available[i] < _available[earliest])
                {
                    earliest = i;
                }
            }

            return earliest;
        }
    }
}
