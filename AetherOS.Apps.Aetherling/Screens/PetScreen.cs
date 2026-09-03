using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.PetKit.Engine;
using AetherOS.PetKit.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>Where it lives once it is out. One fitted page, never a scroller: a header, the stage it sits on,
/// a line telling you how it seems, and the mode pill that decides what a touch means. Feeding and petting
/// live in the Modes partial; growing is the feed ladder the server runs.</summary>
internal sealed partial class PetScreen(IAetherlingHost host, PetRuntime pet)
{

    /// <summary>How long after the birth the furniture arrives, so the card does not land on the pop.</summary>
    private const float SettleSeconds = 0.7f;

    /// <summary>The ceremony ends with it standing where the crystal was, which is not where it lives. It
    /// hops down in two, and the tail after the second landing is the squash.</summary>
    private const float ArriveSeconds = 1.25f;
    private static readonly float[] HopEnds = [0.44f, 0.86f];
    private static readonly float[] HopTravel = [0.55f, 1f];

    /// <summary>Height and sideways lean of a hop, as a fraction of the creature's own size: a pixel count
    /// would only ever suit the one phone scale it was measured on.</summary>
    private const float HopArc = 0.34f;
    private const float HopSway = 0.13f;

    /// <summary>The top of the stage card the mood bar and its sentence occupy, in design pixels: the
    /// creature is centred in what is left below it.</summary>
    private const float StageHeadroom = 62f;

    private AetherlingDto? _core;
    private double _lastFrameTime;
    private float _settle = 1f;

    /// <summary>Where the mood marker is, eased toward the mood itself. Negative until the first frame,
    /// so a page opened on a beaming pet starts beaming rather than sliding there from asleep.</summary>
    private float _moodGlide = -1f;
    private float _arrive = 1f;
    private int _arriveHop = -1;

    private bool _namingOpen;
    private bool _namingConfirmLeave;
    private string _nameBuffer = string.Empty;
    private bool _nameFocusPending;
    private bool _busy;
    private string? _error;

    private AetherlingDto? _pendingNamed;
    private string? _pendingError;

    /// <summary>Whether the player has been told what any of this is. Until they have, the page carries one
    /// button and nothing else asks for attention.</summary>
    public bool IntroSeen { get; set; }

    /// <summary>Raised by the "what is this" button. The nav bar's help entry goes to the same
    /// place, straight from the app.</summary>
    public event Action? IntroRequested;

    /// <summary>A line over the creature from outside the screen (the emote eureka): rides the feed
    /// toast's own plate, because two toast systems on one page is one too many.</summary>
    public void ShowToast(string text)
    {
        _feedToast = text;
        _feedToastLeft = 4f;
    }

    public void OnShow(AetherlingDto? core, bool justBorn)
    {
        _lastFrameTime = ImGui.GetTime();
        AdoptCore(core);
        _settle = justBorn ? 0f : 1f;
        _arrive = justBorn ? 0f : 1f;
        _arriveHop = -1;
        _error = null;
        _namingConfirmLeave = false;
        RefreshInventory();
        if (justBorn)
        {
            pet.Celebrate();
        }
    }

    public void Apply(AetherlingDto? core)
    {
        AdoptCore(core);
        if (core is not null)
        {
            _wheel?.Adopt(core);
        }
    }

    /// <summary>Stores a snapshot and re-samples the clock offset against it. The offset has to be
    /// taken at the MOMENT the reply lands: computed fresh every frame it would always resolve back
    /// to the stamp inside the snapshot, which is frozen, and every countdown drawn from it would
    /// sit perfectly still.</summary>
    private void AdoptCore(AetherlingDto? core)
    {
        if (core is null)
        {
            return;
        }
        _core = core;
        _serverOffset = core.ServerNowUtc - DateTimeOffset.UtcNow;
    }

    public void Draw(OsAppContext ctx)
    {
        // The runtime ignores this while a ceremony is holding the old body, so every surface can
        // simply ask for the form the snapshot names.
        pet.EnsureLoaded(host.AssetRoot, PetState.FormFolder(_core));

        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var now = ImGui.GetTime();
        var dt = Math.Clamp((float)(now - _lastFrameTime), 0f, 0.25f);
        _lastFrameTime = now;

        DrainPending();
        DrainRename();
        DrainInventory();
        DrainFeeding(ctx, dt);
        pet.ApplyLook(_core);

        Look.Backdrop(dl, ctx.Theme, origin, size);
        if (_core is not { } core)
        {
            return;
        }

        pet.Tick(ctx.ReduceMotion);

        if (_arrive < 1f)
        {
            _arrive = ctx.ReduceMotion ? 1f : MathF.Min(1f, _arrive + (dt / ArriveSeconds));
            var hop = _arrive < HopEnds[0] ? 0 : 1;
            if (hop != _arriveHop && _arrive < HopEnds[^1])
            {
                _arriveHop = hop;
                pet.PlayHopClip();
            }
        }
        else if (_settle < 1f)
        {
            _settle = MathF.Min(1f, _settle + (dt / SettleSeconds));
            if (_settle >= 1f && !core.NameChosen)
            {
                OpenNaming(core);
            }
        }

        var pad = Px(18f);
        var headerY = origin.Y + Px(16f);
        var name = core.PetName ?? AetherlingLimits.DefaultName;
        float nameWidth;
        float nameLineH;
        using (ctx.TitleFont?.Push())
        {
            dl.AddText(new Vector2(origin.X + pad, headerY), Look.U32(Look.CrystalPale), name);
            nameWidth = ImGui.CalcTextSize(name).X;
            nameLineH = ImGui.GetTextLineHeight();
        }
        if (core is { NameChosen: true, HatchedAtUtc: not null }
            && !_namingOpen && !RenameOverlayOpen && _settle >= 1f && !Evolution.Playing)
        {
            DrawRenamePill(ctx, dl, new Vector2(origin.X + pad + nameWidth + Px(10f), headerY), nameLineH);
        }
        var born = (core.HatchedAtUtc ?? core.CreatedAtUtc).ToLocalTime().ToString("d MMM yyyy");
        dl.AddText(new Vector2(origin.X + pad, headerY + Px(40f)), Look.U32(Look.Whisper, 0.85f),
            string.Format(ctx.Localize("os.aetherling_kindled_on"), born));

        var stageTop = headerY + Px(70f);
        var wheelButton = WheelButtonVisible(core);
        var introButton = !IntroSeen && !_namingOpen && _arrive >= 1f;
        var stageBottom = origin.Y + size.Y - Px(58f) - (wheelButton || introButton ? Px(WheelRowExtra) : 0f) - FootReserved(core);
        var stage = new Vector2(size.X - (pad * 2f), MathF.Max(Px(150f), stageBottom - stageTop));
        var stageTl = new Vector2(origin.X + pad, stageTop);

        // Submitted before the card underneath it, or the card's own whole-area target takes the click.
        if (introButton)
        {
            DrawIntroButton(ctx, dl, stageTl, stage, now);
        }
        DrawStage(ctx, dl, stageTl, stage, origin, size, core);

        var hint = _feedToastLeft > 0f && _feedToast is { Length: > 0 }
            ? _feedToast
            : ctx.Localize("os.aetherling_tap_hint");
        Look.Centred(dl, hint, origin.X + (size.X * 0.5f), stageTl.Y + stage.Y + Px(12f),
            Look.U32(Look.Whisper, 0.7f * _settle), 0.9f);
        if (wheelButton)
        {
            DrawWheelButton(ctx, dl,
                new Vector2(origin.X + ((size.X - Px(WheelButtonSize)) * 0.5f), stageTl.Y + stage.Y + Px(36f)),
                core, now);
        }

        if (ModesAvailable(core))
        {
            DrawFoot(ctx, dl, origin, size, core);
            TickPetting(ctx, dt, stageTl, stage);
            TickCarriedAndFlying(ctx, dt, stageTl, stage);
        }

        DrawCheer(ctx, dl, origin, size, dt);

        if (NameChipVisible)
        {
            DrawNameChip(ctx, dl, origin, size, core);
        }
        else if (!_namingOpen && !Ticket.Visible && UnclaimedTicketSlot() is { } waiting && _settle >= 1f)
        {
            DrawTicketChip(ctx, dl, origin, size, core, waiting);
        }
        if (_namingOpen)
        {
            DrawNamingCard(ctx, dl, origin, size);
        }
        else if (_renameOpen)
        {
            DrawRenameCard(ctx, dl, origin, size);
        }
        else if (_renameOfferOpen)
        {
            DrawRenameOffer(ctx, dl, origin, size);
        }

        // Last, so it covers the page it took over. When it finishes and the pet has just grown up,
        // the app takes the hand-off into the adult welcome.
        if (Evolution.Playing
            && !Evolution.Draw(ctx, dl, origin, size, dt, core.PetName ?? AetherlingLimits.DefaultName)
            && _adultingHandOff)
        {
            _adultingHandOff = false;
            AdultingFinished?.Invoke();
        }

        // After the ceremony, in its own layer: a gift handed over while a growing-up is on screen would
        // be handed to nobody.
        if (Ticket.Visible && !Evolution.Playing)
        {
            Ticket.Draw(ctx, origin, size, dt);
        }
        if (WheelOpen && !Evolution.Playing)
        {
            Wheel.Draw(ctx, origin, size, dt);
        }
    }

    /// <summary>The one way in to the explanation, sitting over its head until it has been read. It breathes
    /// rather than shouts: the page is quiet by design and this is the only thing on it asking to be pressed.</summary>
    private void DrawIntroButton(OsAppContext ctx, ImDrawListPtr dl, Vector2 stageTl, Vector2 stage, double now)
    {
        const float LabelScale = 1.2f;
        var label = ctx.Localize("os.aetherling_what_is_this");
        // Under the stage, in the band the wheel button uses, rather than over the creature: a button
        // behind a hopping pet was a target nobody could hit.
        var height = Px(44f);
        var width = (ImGui.CalcTextSize(label).X * LabelScale) + Px(58f);
        var tl = new Vector2(
            stageTl.X + ((stage.X - width) * 0.5f),
            stageTl.Y + stage.Y + Px(36f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingWhatIsThis", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var pulse = ctx.ReduceMotion ? 0.5f : Look.Breathe(now, 2.6f);
        var radius = height * 0.5f;
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal with { W = (hovered ? 0.30f : 0.16f) + (0.08f * pulse) }), radius);
        dl.AddRect(tl, tl + new Vector2(width, height),
            Look.U32(Look.CrystalPale, 0.45f + (0.35f * pulse)), radius, ImDrawFlags.RoundCornersAll, Px(1.6f));
        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - (ImGui.GetTextLineHeight() * LabelScale)) * 0.5f), Look.U32(Look.CrystalPale),
            LabelScale);

        if (pressed)
        {
            IntroRequested?.Invoke();
        }
    }

    /// <summary>The card it sits on. The whole card is the target, because aiming at a small creature that
    /// hops is a chore rather than a kindness.</summary>
    private void DrawStage(
        OsAppContext ctx,
        ImDrawListPtr dl,
        Vector2 tl,
        Vector2 size,
        Vector2 window,
        Vector2 windowSize,
        AetherlingDto core)
    {
        var br = tl + size;
        var now = ImGui.GetTime();
        var centreX = tl.X + (size.X * 0.5f);
        dl.AddRectFilled(tl, br, 0x14FFFFFFu, Px(18f));
        dl.AddRect(tl, br, 0x1AFFFFFFu, Px(18f), ImDrawFlags.RoundCornersAll, Px(1f));

        if (!_namingOpen && !RenameOverlayOpen && !Ticket.Visible && !WheelOpen && _arrive >= 1f)
        {
            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton("##aetherlingStage", size))
            {
                Boop();
            }
            if (ImGui.IsItemHovered())
            {
                HandOnHover();
            }
        }

        // Everything ambient is clipped to the card, or the motes climb out over the header.
        dl.PushClipRect(tl, br, true);
        Look.Motes(dl, tl, size, 22, Look.Crystal, 0.30f, now, ctx.ReduceMotion);
        if (_arrive >= 1f)
        {
            DrawMoodBanner(ctx, dl, centreX, tl.Y + Px(18f), size.X - Px(36f), core, now);
        }

        // What it is wearing decides where it stands. A hat needs headroom and a nook needs floor, so the
        // creature is sized and lifted against its own worn extent rather than a constant that was measured
        // on a bare pet and clips the tall hats off the top of the card.
        // Centred in the room under the mood header rather than stood on the card's floor: the card's height
        // moves with the wheel button and the feeding timer, and a creature anchored to its floor drifted
        // with them. The worn extent is the block that gets centred, so a hat or a nook does not push it off.
        var footprint = pet.AccessoryFootprint();
        var headroom = Px(StageHeadroom);
        var room = size.Y - headroom - Px(12f);
        var petSize = MathF.Min(size.X * 0.74f, room / (1f + footprint.Y + footprint.W));
        var blockH = petSize * (1f + footprint.Y + footprint.W);
        var blockTop = tl.Y + headroom + ((room - blockH) * 0.5f);
        var bottom = new Vector2(centreX, blockTop + (petSize * (1f + footprint.Y)));
        if (pet.Ready)
        {
            var pose = pet.Pose;
            if (_arrive < 1f)
            {
                // Its own hop offset is dropped while it is travelling: two arcs over one sprite is a stumble.
                (bottom, petSize, pose.Scale, pose.FlipX) = Arrive(
                    BirthStage.BottomCentre(window, windowSize), BirthStage.DisplaySize(windowSize),
                    bottom, petSize);
                pose.Offset = Vector2.Zero;
            }
            // The pool of light it stands in, and the rings settling out of it. Drawn under the sprite, so
            // its feet sit in the light rather than on top of it.
            var glowW = petSize * 0.52f;
            var glowH = petSize * 0.13f;
            var pulse = ctx.ReduceMotion ? 0.5f : Look.Breathe(now, 4.2f);
            // Wider than it looks: the sides fade to nothing, so the visible core is about the creature's own
            // width and the rest is falloff.
            Look.LightShaft(dl, bottom + new Vector2(0f, glowH * 0.5f), petSize * 1.9f,
                (bottom.Y - tl.Y) * 0.86f, Look.Crystal, 0.11f + (0.04f * pulse));
            Look.GroundGlow(dl, bottom, glowW * (0.94f + (0.12f * pulse)), glowH, Look.Crystal,
                0.55f + (0.20f * pulse));
            if (!ctx.ReduceMotion)
            {
                Look.GroundRipples(dl, bottom, glowW * 1.7f, glowH * 1.7f, Look.Crystal, 0.20f, now);
            }
            pet.Draw(dl, ctx.Capabilities.Textures, bottom, petSize, pose);
            pet.DrawGlyph(dl, bottom, petSize, bubbleFrame: false);
        }

        dl.PopClipRect();
    }

    /// <summary>One colour per mood, asleep to beaming, read as a ramp rather than six swatches: the bar
    /// draws the whole scale at once, so the steps between them have to blend into each other.</summary>
    private static readonly Vector4[] MoodRamp =
    [
        new(0.42f, 0.36f, 0.74f, 1f),
        new(0.36f, 0.53f, 0.88f, 1f),
        new(0.31f, 0.79f, 0.82f, 1f),
        new(0.47f, 0.86f, 0.62f, 1f),
        new(0.98f, 0.83f, 0.44f, 1f),
        new(1.00f, 0.60f, 0.68f, 1f),
    ];

    /// <summary>The ramp sampled anywhere along the bar, 0 at the left end and 1 at the right.</summary>
    private static Vector4 MoodColour(float t)
    {
        var at = Math.Clamp(t, 0f, 1f) * (MoodRamp.Length - 1);
        var i = Math.Min((int)at, MoodRamp.Length - 2);
        return Vector4.Lerp(MoodRamp[i], MoodRamp[i + 1], at - i);
    }

    /// <summary>How it seems, as the page's own header: the whole mood scale as one rainbow capsule, asleep
    /// at the left and beaming at the right, with a marker gliding to where it is now. The sentence stays,
    /// under it and quieter, because the bar says where and only the words say what.
    ///
    /// <para>The marker moves and nothing else does. There is no fill, because a bar that fills is a bar
    /// that can be seen to empty, and the mood has a floor precisely so nobody is ever losing at owning a
    /// pet (<see cref="Engine.MoodTracker"/>).</para></summary>
    private void DrawMoodBanner(
        OsAppContext ctx, ImDrawListPtr dl, float centreX, float y, float maxWidth, AetherlingDto core, double now)
    {
        var target = pet.MoodProgress;
        var dt = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 1f / 30f);
        _moodGlide = _moodGlide < 0f || ctx.ReduceMotion
            ? target
            : _moodGlide + ((target - _moodGlide) * (1f - MathF.Exp(-dt * 5.5f)));

        var breath = ctx.ReduceMotion ? 0.5f : Look.Breathe(now, 5.0f);
        var width = maxWidth * 0.88f;
        var height = Px(15f);
        var left = centreX - (width * 0.5f);
        var right = centreX + (width * 0.5f);
        var radius = height * 0.5f;
        var midY = y + radius;
        var here = MoodColour(_moodGlide);

        // The bloom the bar sits in, in the colour of the mood it is reporting, so the whole header warms
        // and cools with the creature rather than only the marker.
        Look.Halo(dl, new Vector2(centreX, midY), width * 0.52f, here, 0.13f + (0.05f * breath));

        // The capsule: a strip per step between the round caps, each one a horizontal blend of its own two
        // ends. The caps take the colour at their own centre, so at this radius no seam is visible.
        const int Steps = 56;
        var innerL = left + radius;
        var innerR = right - radius;
        var span = innerR - innerL;
        var capT = radius / width;
        dl.AddCircleFilled(new Vector2(innerL, midY), radius, Look.U32(MoodColour(capT)), 26);
        dl.AddCircleFilled(new Vector2(innerR, midY), radius, Look.U32(MoodColour(1f - capT)), 26);
        for (var i = 0; i < Steps; i++)
        {
            var t0 = i / (float)Steps;
            var t1 = (i + 1) / (float)Steps;
            var c0 = Look.U32(MoodColour(capT + ((1f - (2f * capT)) * t0)));
            var c1 = Look.U32(MoodColour(capT + ((1f - (2f * capT)) * t1)));
            dl.AddRectFilledMultiColor(
                new Vector2(innerL + (span * t0), y),
                new Vector2(innerL + (span * t1) + 1f, y + height),
                c0, c1, c1, c0);
        }

        // Glass: a highlight along the top third and a hairline all the way round, which is what stops a
        // flat gradient reading as a painted rectangle.
        dl.AddRectFilled(
            new Vector2(innerL - (radius * 0.4f), y + (height * 0.16f)),
            new Vector2(innerR + (radius * 0.4f), y + (height * 0.44f)),
            Look.U32(new Vector4(1f, 1f, 1f, 0.16f)), height * 0.16f);
        dl.AddRect(new Vector2(left, y), new Vector2(right, y + height),
            Look.U32(new Vector4(1f, 1f, 1f, 0.24f)), radius, ImDrawFlags.RoundCornersAll, Px(1.1f));

        // A sheen crossing once every ten seconds. Two triangular-alpha halves rather than a loop of
        // slices, clipped to the bar; the clip is rectangular and the capsule is not, so the band fades out
        // over the last radius at each end instead of painting into the round caps.
        if (!ctx.ReduceMotion)
        {
            const float SweepEverySeconds = 10f;
            const float SweepSeconds = 1.15f;
            var phase = (float)(now % SweepEverySeconds);
            if (phase < SweepSeconds)
            {
                var t = phase / SweepSeconds;
                var travelled = t * t * (3f - (2f * t));
                var band = height * 2.4f;
                var sweepX = innerL + (span * travelled);
                var edge = Math.Clamp(
                    MathF.Min(sweepX - innerL, innerR - sweepX) / MathF.Max(1f, radius * 2f), 0f, 1f);
                var peak = Look.U32(new Vector4(1f, 1f, 1f, 0.5f * edge));
                var clear = Look.U32(new Vector4(1f, 1f, 1f, 0f));
                dl.PushClipRect(new Vector2(left, y), new Vector2(right, y + height), true);
                dl.AddRectFilledMultiColor(
                    new Vector2(sweepX - band, y), new Vector2(sweepX, y + height), clear, peak, peak, clear);
                dl.AddRectFilledMultiColor(
                    new Vector2(sweepX, y), new Vector2(sweepX + band, y + height), peak, clear, clear, peak);
                dl.PopClipRect();
            }
        }

        // The marker: a lit bead of the mood's own colour, ringed so it stays legible over every part of
        // the ramp it can stand on.
        var knobX = innerL + (span * _moodGlide);
        var knobR = height * 0.62f;
        Look.Halo(dl, new Vector2(knobX, midY), knobR * 3.2f, here, 0.42f + (0.18f * breath));
        dl.AddCircleFilled(new Vector2(knobX, midY), knobR, Look.U32(new Vector4(1f, 1f, 1f, 0.94f)), 30);
        dl.AddCircleFilled(new Vector2(knobX, midY), knobR - Px(2.6f), Look.U32(here), 30);

        var line = string.Format(
            ctx.Localize("os.aetherling_feeling"),
            core.PetName ?? AetherlingLimits.DefaultName,
            ctx.Localize($"os.aetherling_feel_{(int)pet.Mood}"));

        // Down a size at a time until it fits on one line: a subtitle that wraps is not a subtitle.
        var scale = 0.92f;
        while (scale > 0.7f && ImGui.CalcTextSize(line).X * scale > maxWidth)
        {
            scale -= 0.04f;
        }
        Look.Centred(dl, line, centreX, y + height + Px(9f),
            Look.U32(Look.Whisper, 1.15f + (0.15f * breath)), scale);
    }

    /// <summary>Where it is on its way down, and how big. The ceremony leaves it standing where the crystal
    /// was, so without this the change of screen would set it on the floor of its card in one frame.</summary>
    private (Vector2 Bottom, float Size, Vector2 Scale, bool FlipX) Arrive(
        Vector2 from, float fromSize, Vector2 to, float toSize)
    {
        var hop = _arrive < HopEnds[0] ? 0 : 1;
        var start = hop == 0 ? 0f : HopEnds[0];
        var t = Math.Clamp((_arrive - start) / (HopEnds[hop] - start), 0f, 1f);
        var travel = Lerp(hop == 0 ? 0f : HopTravel[0], HopTravel[hop], t);

        var bottom = Vector2.Lerp(from, to, travel);
        var petSize = Lerp(fromSize, toSize, travel);

        var arc = MathF.Sin(MathF.PI * t);
        var lean = hop == 0 ? -1f : 1f;
        bottom += new Vector2(arc * petSize * HopSway * lean, -arc * petSize * HopArc * (hop == 0 ? 1f : 0.6f));

        var squash = LandingSquash(_arrive);
        return (bottom, petSize, new Vector2(1f + (0.16f * squash), 1f - (0.20f * squash)), lean < 0f);
    }

    /// <summary>A pulse of squash just after each touchdown, so it lands with weight instead of stopping dead
    /// in the air.</summary>
    private static float LandingSquash(float progress)
    {
        const float Window = 0.10f;
        var strongest = 0f;
        foreach (var at in HopEnds)
        {
            var since = progress - at;
            if (since < 0f || since > Window)
            {
                continue;
            }
            strongest = MathF.Max(strongest, 1f - (since / Window));
        }
        return strongest;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private void Boop()
    {
        pet.Boop();
        host.PlayChirp();
    }

    /// <summary>The quiet way back to the card for anyone who put it off, and the reason skipping is safe.</summary>
    private void DrawNameChip(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherlingDto core)
    {
        var label = ctx.Localize("os.aetherling_name_chip");
        var height = Px(30f);
        var width = ImGui.CalcTextSize(label).X + Px(34f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - height - Px(16f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingNameChip", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal with { W = hovered ? 0.22f : 0.12f }), height * 0.5f);
        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.CrystalPale, 0.9f));
        if (pressed)
        {
            OpenNaming(core);
        }
    }

    /// <summary>A ticket that was dealt but never scratched, brought back within reach. It has to
    /// resurface: the prize lands at the reveal, so a ticket left alone is a flourish the owner earned
    /// and does not have.</summary>
    private void DrawTicketChip(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherlingDto core, short slot)
    {
        var label = ctx.Localize("os.aetherling_ticket_chip");
        var height = Px(30f);
        var width = ImGui.CalcTextSize(label).X + Px(42f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - height - Px(16f));
        var pulse = ctx.ReduceMotion ? 0.5f : Look.Breathe(ImGui.GetTime(), 2.4f);

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingTicketChip", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Spark with { W = (hovered ? 0.28f : 0.16f) + (0.08f * pulse) }), height * 0.5f);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Gift, Px(12f),
            new Vector2(tl.X + Px(16f), tl.Y + (height * 0.5f)), Look.U32(Look.Spark, 0.95f));
        Look.Centred(dl, label, tl.X + (width * 0.5f) + Px(8f),
            tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.CrystalPale, 0.92f));
        if (pressed)
        {
            Ticket.Open(core, slot);
        }
    }

    private void OpenNaming(AetherlingDto core)
    {
        _namingOpen = true;
        _namingConfirmLeave = false;
        _nameFocusPending = true;
        _nameBuffer = core.PetName ?? AetherlingLimits.DefaultName;
        _error = null;
    }

    /// <summary>The naming card. While it is up nothing else on the page is submitted, which is what makes it
    /// modal here: an ImGui item under it would otherwise still take the click.</summary>
    private void DrawNamingCard(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void with { W = 1f }, 0.72f));

        var pad = Px(18f);
        var cardW = size.X - (pad * 2f);
        var cardH = Px(_namingConfirmLeave ? 170f : 190f);
        var tl = new Vector2(origin.X + pad, origin.Y + ((size.Y - cardH) * 0.42f));
        var br = tl + new Vector2(cardW, cardH);
        dl.AddRectFilled(tl, br, Look.U32(new Vector4(0.10f, 0.09f, 0.16f, 0.97f)), Px(16f));
        dl.AddRect(tl, br, Look.U32(Look.Crystal, 0.35f), Px(16f), ImDrawFlags.RoundCornersAll, Px(1.2f));

        if (_namingConfirmLeave)
        {
            DrawLeaveConfirm(ctx, dl, tl, cardW, cardH);
            return;
        }

        var y = tl.Y + Px(16f);
        Look.Centred(dl, ctx.Localize("os.aetherling_naming_title"), tl.X + (cardW * 0.5f), y,
            Look.U32(Look.CrystalPale), 1.15f);
        y += Px(30f);
        Look.CentredWrapped(dl, ctx.Localize("os.aetherling_naming_body"), tl.X + (cardW * 0.5f), y,
            cardW - Px(28f), Look.U32(Look.Whisper, 0.9f), 0.9f);
        y += Px(34f);

        ImGui.SetCursorScreenPos(new Vector2(tl.X + Px(14f), y));
        ImGui.SetNextItemWidth(cardW - Px(28f));
        if (_nameFocusPending)
        {
            _nameFocusPending = false;
            ImGui.SetKeyboardFocusHere();
        }
        var submitted = ImGui.InputText("##aetherlingName", ref _nameBuffer,
            AetherlingLimits.NameMaxLength, ImGuiInputTextFlags.EnterReturnsTrue);
        y += Px(34f);

        if (_error is { Length: > 0 })
        {
            Look.CentredWrapped(dl, _error, tl.X + (cardW * 0.5f), y, cardW - Px(28f),
                Look.U32(new Vector4(0.95f, 0.5f, 0.5f, 1f)), 0.85f);
        }

        var buttonY = br.Y - Px(46f);
        var half = (cardW - Px(38f)) * 0.5f;
        var confirm = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(14f), buttonY), half,
            ctx.Localize("os.aetherling_naming_confirm"), primary: true);
        var later = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(24f) + half, buttonY), half,
            ctx.Localize("os.aetherling_naming_later"), primary: false);

        if ((confirm || submitted) && !_busy)
        {
            Submit();
        }
        if (later && !_busy)
        {
            _namingConfirmLeave = true;
        }
    }

    private void DrawLeaveConfirm(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float cardW, float cardH)
    {
        var y = tl.Y + Px(16f);
        Look.Centred(dl, ctx.Localize("os.aetherling_naming_later_title"), tl.X + (cardW * 0.5f), y,
            Look.U32(Look.CrystalPale), 1.1f);
        y += Px(30f);
        Look.CentredWrapped(dl, ctx.Localize("os.aetherling_naming_later_warning"), tl.X + (cardW * 0.5f), y,
            cardW - Px(28f), Look.U32(Look.Whisper, 0.9f), 0.9f);

        var buttonY = tl.Y + cardH - Px(46f);
        var half = (cardW - Px(38f)) * 0.5f;
        var back = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(14f), buttonY), half,
            ctx.Localize("os.aetherling_naming_back"), primary: true);
        var leave = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(24f) + half, buttonY), half,
            ctx.Localize("os.aetherling_naming_leave"), primary: false);

        if (back)
        {
            _namingConfirmLeave = false;
            _nameFocusPending = true;
        }
        if (leave)
        {
            // Nothing is sent: the name stays what the server called it, and the chip stays offering.
            _namingOpen = false;
            _namingConfirmLeave = false;
        }
    }

    private bool DrawCardButton(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float width, string label, bool primary)
    {
        var height = Px(34f);
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton($"##aetherlingCard{label}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered && !_busy)
        {
            HandOnHover();
        }
        var fill = primary
            ? Look.Crystal with { W = hovered ? 0.30f : 0.20f }
            : new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.07f);
        dl.AddRectFilled(tl, tl + new Vector2(width, height), Look.U32(fill), height * 0.5f);
        if (_busy && primary)
        {
            LoadingSpinner.Draw(tl + new Vector2(width * 0.5f, height * 0.5f), Px(8f), Px(2.2f),
                Look.U32(Look.CrystalPale));
            return false;
        }
        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f),
            Look.U32(primary ? Look.CrystalPale : Look.Whisper));
        _ = ctx;
        return pressed && !_busy;
    }

    private void Submit()
    {
        var name = _nameBuffer;
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.NameAsync(name).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingNamed, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }

    /// <summary>Takes what the naming round trip left. Called from Draw, because accepting a name spawns
    /// particles and closes an overlay the draw thread owns.</summary>
    private void DrainPending()
    {
        if (Interlocked.Exchange(ref _pendingNamed, null) is { } named)
        {
            _busy = false;
            AdoptCore(named);
            _namingOpen = false;
            _namingConfirmLeave = false;
            pet.Celebrate();
        }
        if (Interlocked.Exchange(ref _pendingError, null) is { } message)
        {
            _busy = false;
            _error = message;
        }
    }
}
