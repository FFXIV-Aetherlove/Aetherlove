using System;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Racing;
using AetherLove.UI;
using AetherOS.PetKit.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>What race day is, in six pages written on the race card's paper: the field, the element the
/// player's own creature was born to, the three challenge grades, the stamp card, practice racing, and
/// racing together. The paper is the sheet the difficulty explainer writes on, so the words are never
/// asked to survive on top of the picture.</summary>
internal sealed class RaceOnboardingScreen(IRacerHost host, Action done)
{
    private const int Pages = 6;

    /// <summary>What the practice page says before the server has answered: the shipped defaults, so a
    /// read that arrives late shows the same numbers rather than blanks.</summary>
    private const short DefaultStampsPerDay = 3;

    private const short DefaultStampsPerWeek = 5;

    private const short DefaultGateMinutes = 15;

    /// <summary>The page's ink: the card's own blue, because the page is the card's paper.</summary>
    private static readonly Vector4 PageInk = RacerChrome.CardBlue with { W = 1f };

    /// <summary>The field on page one, in palettes far enough apart to read at a glance.</summary>
    private static readonly string[] FieldPalettes = ["ember", "lagoon", "meadow", "rose"];

    private static readonly string[] ElementKeys = ["fire", "water", "ice", "wind", "lightning", "earth"];

    private static readonly (FontAwesomeIcon Icon, string Key)[] Rules =
    [
        (FontAwesomeIcon.Stamp, "os.racer_intro_limit_races"),
        (FontAwesomeIcon.CalendarAlt, "os.racer_intro_limit_week"),
        (FontAwesomeIcon.Stopwatch, "os.racer_intro_limit_gate"),
        (FontAwesomeIcon.Running, "os.racer_intro_limit_practice"),
    ];

    private readonly PetRuntime[] _field = [new(), new(), new(), new()];

    /// <summary>The player's own creature, dressed from the state read, for the pages that speak about
    /// it rather than about racing in general.</summary>
    private readonly PetRuntime _own = new();

    private LumiRaceStateDto? _state;
    private LumiRaceStateDto? _pendingState;
    private bool _loaded;
    private string _ownLook = string.Empty;
    private int _page;
    private double _shown;

    public void Show()
    {
        _page = 0;
        _shown = ImGui.GetTime();
        Refresh();
    }

    public void Draw(OsAppContext ctx)
    {
        if (_pendingState is { } fresh)
        {
            _pendingState = null;
            _state = fresh;
        }

        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var now = ImGui.GetTime();

        EnsureField();
        EnsureOwn();
        RacerBackdrop.Draw(ctx, host, origin, size, dim: 0.30f);
        // Whole-window, because the step bar sits at the very top: a sheet inset from the frame left a
        // strip of the race-day picture above it, bunting and all.
        RacerChrome.PaperSheet(dl, origin, size, inset: 0f);

        var fade = ctx.ReduceMotion ? 1f : Math.Clamp((float)(now - _shown) / 0.4f, 0f, 1f);
        DrawSteps(dl, origin, size, now);

        // The words first, at the top where nothing competes with them; the picture takes what is left
        // between the last line and the button.
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + Px(58f)));
        using (Dalamud.Interface.Utility.Raii.ImRaii.PushColor(ImGuiCol.Text, PageInk with { W = fade }))
        {
            using (ctx.TitleFont?.Push())
            {
                RacerChrome.CenteredText(Title(ctx));
            }
            ImGui.Dummy(new Vector2(1f, Px(6f)));
            OnboardingUi.DrawCenteredParagraph(Body(ctx), size.X - Px(64f), PageInk with { W = 0.86f * fade });
        }

        var stageTop = ImGui.GetCursorScreenPos().Y + Px(10f);
        var stage = new Vector2(origin.X, stageTop);
        var stageSize = new Vector2(size.X, origin.Y + size.Y - Px(122f) - stageTop);
        switch (_page)
        {
            case 0:
                DrawField(ctx, dl, stage, stageSize, now, fade);
                break;
            case 1:
                DrawElements(ctx, dl, stage, stageSize, now, fade);
                break;
            case 2:
                DrawWheel(ctx, dl, stage, stageSize, fade);
                break;
            case 3:
                DrawCard(ctx, dl, stage, stageSize, now, fade);
                break;
            case 4:
                DrawRules(ctx, dl, stage, stageSize, fade);
                break;
            default:
                DrawParty(ctx, dl, stage, stageSize, now, fade);
                break;
        }

        // The button wears race day's own red, chequered on the page that actually starts the racing.
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + size.Y - Px(72f)));
        var last = _page == Pages - 1;
        if (RacerChrome.FlagButton(ctx, "##racerIntroNext",
            ctx.Localize(last ? "os.racer_intro_start" : "onboarding.next"),
            RacerChrome.DutchRed, RacerChrome.WhiteInk, null, true, chequered: last))
        {
            if (last)
            {
                done();
                return;
            }
            GoTo(_page + 1, now);
        }
    }

    /// <summary>The step bar, in race day's own colours and clickable: six segments, the ones reached in
    /// red, the rest faint, and a blue chevron back. The house bar is accent pink and unclickable, and
    /// its chevron is white, which vanishes on this paper.</summary>
    private void DrawSteps(ImDrawListPtr dl, Vector2 origin, Vector2 size, double now)
    {
        var top = origin.Y + Px(30f);
        var height = Px(5f);
        var left = origin.X + Px(46f);
        var right = origin.X + size.X - Px(26f);
        var gap = Px(5f);
        var segment = ((right - left) - (gap * (Pages - 1))) / Pages;

        for (var i = 0; i < Pages; i++)
        {
            var x = left + (i * (segment + gap));
            ImGui.SetCursorScreenPos(new Vector2(x, top - Px(8f)));
            var pressed = ImGui.InvisibleButton($"##racerStep{i}", new Vector2(segment, Px(20f)));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
            }
            var reached = i <= _page;
            var colour = reached
                ? RacerChrome.DutchRed with { W = hovered ? 1f : 0.95f }
                : RacerChrome.CardBlue with { W = hovered ? 0.45f : 0.20f };
            dl.AddRectFilled(new Vector2(x, top), new Vector2(x + segment, top + height),
                ImGui.ColorConvertFloat4ToU32(colour), height * 0.5f);
            if (pressed)
            {
                GoTo(i, now);
            }
        }

        if (_page <= 0)
        {
            return;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X + Px(14f), top - Px(12f)));
        var back = ImGui.InvisibleButton("##racerIntroBack", new Vector2(Px(28f), Px(28f)));
        var backHovered = ImGui.IsItemHovered();
        if (backHovered)
        {
            HandOnHover();
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronLeft, Px(15f),
            new Vector2(origin.X + Px(28f), top + Px(2f)),
            ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue with { W = backHovered ? 1f : 0.75f }));
        if (back)
        {
            GoTo(_page - 1, now);
        }
    }

    private void GoTo(int page, double now)
    {
        var next = Math.Clamp(page, 0, Pages - 1);
        if (next == _page)
        {
            return;
        }
        _page = next;
        _shown = now;
    }

    /// <summary>The creature's name for the copy that speaks to it, or the plain word for a Lumi that
    /// has none yet.</summary>
    private string PetName(OsAppContext ctx) =>
        _state?.PetName is { Length: > 0 } name ? name : ctx.Localize("os.racer_intro_your_lumi");

    private string Title(OsAppContext ctx) => _page switch
    {
        1 => string.Format(ctx.Localize("os.racer_intro_title_1"), PetName(ctx)),
        _ => ctx.Localize($"os.racer_intro_title_{_page}"),
    };

    private string Body(OsAppContext ctx)
    {
        var name = PetName(ctx);
        return _page switch
        {
            1 => string.Format(ctx.Localize("os.racer_intro_body_1"), name),
            2 => string.Format(ctx.Localize("os.racer_intro_body_2"), name, ElementName(ctx)),
            _ => ctx.Localize($"os.racer_intro_body_{_page}"),
        };
    }

    private string ElementName(OsAppContext ctx)
    {
        var element = OwnElement();
        var key = element is { } e ? RacingElements.NameOf(e) : string.Empty;
        return ctx.Localize(key.Length == 0 ? "os.racer_element_neutral" : $"os.racer_element_{key}");
    }

    private void EnsureField()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        for (var i = 0; i < _field.Length; i++)
        {
            _field[i].SetPhaseSeed($"onboarding#{i}");
            _field[i].EnsureLoaded(host.PetAssetRoot, PetState.FormFolderForStage(3));
            _field[i].ApplyDraftLook(FieldPalettes[i], [], string.Empty, []);
        }
    }

    /// <summary>Dresses the player's own creature once the state read lands, and again if the look ever
    /// changes under it.</summary>
    private void EnsureOwn()
    {
        if (_state is not { } state)
        {
            return;
        }
        var look = $"{state.PetPalette}|{state.PetAccessories}|{state.PetShell}";
        if (look == _ownLook)
        {
            return;
        }
        _ownLook = look;

        // Always the adult form. Racing is adults only, so that is the creature these pages are talking
        // about, whatever form the snapshot happens to have caught it in.
        _own.EnsureLoaded(host.PetAssetRoot, PetState.FormFolderForStage(3, state.PetShell));
        _own.ApplyDraftLook(
            state.PetPalette is { Length: > 0 } palette ? palette : FieldPalettes[0],
            state.PetAccessories is { Length: > 0 } worn
                ? worn.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                : [],
            string.Empty,
            []);
    }

    /// <summary>Four of them abreast, breathing out of step so the line never pulses as one animal.</summary>
    private void DrawField(OsAppContext ctx, ImDrawListPtr dl, Vector2 stage, Vector2 size, double now, float fade)
    {
        var pet = MathF.Min(MathF.Min(Px(86f), size.X * 0.22f), size.Y * 0.6f);
        var gap = size.X / (_field.Length + 1);
        var baseline = stage.Y + (size.Y * 0.55f);
        for (var i = 0; i < _field.Length; i++)
        {
            var bob = ctx.ReduceMotion ? 0f : MathF.Sin((float)(now * 2.1) + (i * 1.3f)) * Px(5f);
            var feet = new Vector2(stage.X + (gap * (i + 1)), baseline + bob);
            RacerChrome.GroundGlow(dl, new Vector2(feet.X, baseline + Px(3f)), pet * 0.5f, pet * 0.12f,
                RacerChrome.CardBlue, 0.18f * fade);
            _field[i].Tick(ctx.ReduceMotion);
            _field[i].Draw(dl, ctx.Capabilities.Textures, feet, pet, _field[i].Pose, props: false);
        }
    }

    /// <summary>The player's own creature with the six elements arced over it, its own one lit: the
    /// point is that the element is a stat, not a colour.</summary>
    private void DrawElements(OsAppContext ctx, ImDrawListPtr dl, Vector2 stage, Vector2 size, double now, float fade)
    {
        var centre = new Vector2(stage.X + (size.X * 0.5f), stage.Y + (size.Y * 0.42f));
        var radius = MathF.Min(size.X * 0.34f, Px(116f));
        var mine = OwnElement() is { } own ? RacingElements.NameOf(own) : string.Empty;
        for (var i = 0; i < ElementKeys.Length; i++)
        {
            var t = i / (float)(ElementKeys.Length - 1);
            var a = MathF.PI * (1.08f + (0.84f * t));
            var at = centre + new Vector2(MathF.Cos(a) * radius, MathF.Sin(a) * radius * 0.72f);
            var isMine = ElementKeys[i] == mine;
            var lit = isMine || mine.Length == 0;
            var pulse = ctx.ReduceMotion ? 0.5f : 0.5f + (0.5f * MathF.Sin((float)(now * 2.4) + (i * 0.9f)));
            var tint = Rendering.ElementFx.For(ElementKeys[i]).Tint;
            RacerChrome.Halo(dl, at, Px(19f) + (Px(4f) * pulse), tint,
                (lit ? 0.30f + (0.20f * pulse) : 0.10f) * fade, 3);

            var icon = ctx.Capabilities.Textures.Get(
                System.IO.Path.Combine(host.PetAssetRoot, "crystals", ElementKeys[i] + ".png"));
            var alpha = (lit ? 1f : 0.35f) * fade;
            if (icon is { } handle)
            {
                var half = Px(isMine ? 18f : 15f);
                dl.AddImage(handle, at - new Vector2(half), at + new Vector2(half), Vector2.Zero, Vector2.One,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha)));
            }
            else
            {
                dl.AddCircleFilled(at, Px(11f), ImGui.ColorConvertFloat4ToU32(tint with { W = alpha }), 20);
            }
        }

        var pet = MathF.Min(Px(96f), size.Y * 0.42f);
        var feet = new Vector2(centre.X, centre.Y + Px(58f));
        RacerChrome.GroundGlow(dl, feet, pet * 0.5f, pet * 0.12f, RacerChrome.CardBlue, 0.18f * fade);
        var runner = Own();
        runner.Tick(ctx.ReduceMotion);
        runner.Draw(dl, ctx.Capabilities.Textures, feet, pet, runner.Pose, props: false);
    }

    /// <summary>The exact wheel the difficulty page draws, on the same paper, turned to the racer's own
    /// element once the offers are standing.</summary>
    private void DrawWheel(OsAppContext ctx, ImDrawListPtr dl, Vector2 stage, Vector2 size, float fade)
    {
        var overhang = DifficultyWheel.Overhang(ImGui.GetTextLineHeight());
        var radius = MathF.Min(size.X * 0.30f, (size.Y * 0.5f) - overhang);
        var centre = new Vector2(stage.X + (size.X * 0.5f), stage.Y + (size.Y * 0.5f));
        DifficultyWheel.Draw(ctx, dl, centre, radius, OwnElement(),
            WheelSurface.Paper, PageInk with { W = fade });
    }

    /// <summary>The real printed card, stamped one slot at a time on a loop, so what racing buys is
    /// shown rather than described.</summary>
    private void DrawCard(OsAppContext ctx, ImDrawListPtr dl, Vector2 stage, Vector2 size, double now, float fade)
    {
        var cycle = (float)(now * 0.9 % (LumiRaceLimits.StampsPerCard + 1));
        var lit = ctx.ReduceMotion ? LumiRaceLimits.StampsPerCard : (int)cycle;
        var landing = ctx.ReduceMotion ? -1 : lit - 1;
        var landT = ctx.ReduceMotion ? 1f : MathF.Min(1f, (cycle - lit) * 4f);

        var width = MathF.Min(size.X * 0.62f, (size.Y - Px(20f)) * RacerCard.Aspect);
        var cardSize = new Vector2(width, width / RacerCard.Aspect);
        var topLeft = new Vector2(stage.X + ((size.X - cardSize.X) * 0.5f),
            stage.Y + ((size.Y - cardSize.Y) * 0.5f));

        dl.AddRectFilled(topLeft + new Vector2(Px(6f), cardSize.Y - Px(2f)),
            topLeft + new Vector2(cardSize.X - Px(6f), cardSize.Y + Px(8f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.22f * fade)), Px(8f));
        var last = RacerCard.Draw(dl, ctx, host, topLeft, cardSize, lit, fade, landing, landT);
        if (landing >= 0 && landT < 1f)
        {
            RacerChrome.Halo(dl, last, cardSize.X * 0.18f * (2f - landT),
                new Vector4(1f, 0.84f, 0.45f, 1f), 0.35f * fade * (1f - landT), 3);
        }
    }

    /// <summary>Three abreast with the player's own creature in the middle, under one shared glow: a
    /// party race is the same race run together.</summary>
    private void DrawParty(OsAppContext ctx, ImDrawListPtr dl, Vector2 stage, Vector2 size, double now, float fade)
    {
        var pet = MathF.Min(Px(88f), size.Y * 0.38f);
        var centre = new Vector2(stage.X + (size.X * 0.5f), stage.Y + (size.Y * 0.58f));
        RacerChrome.Halo(dl, centre - new Vector2(0f, pet * 0.45f), pet * 1.25f,
            new Vector4(1f, 0.84f, 0.45f, 1f), 0.26f * fade, 4);

        var spread = pet * 0.92f;
        for (var slot = -1; slot <= 1; slot++)
        {
            var runner = slot == 0 ? Own() : _field[slot < 0 ? 1 : 3];
            var scale = slot == 0 ? 1.12f : 0.94f;
            var bob = ctx.ReduceMotion ? 0f : MathF.Sin((float)(now * 2.2) + (slot * 1.7f)) * Px(4f);
            var feet = new Vector2(centre.X + (slot * spread), centre.Y + bob);
            RacerChrome.GroundGlow(dl, new Vector2(feet.X, centre.Y + Px(3f)), pet * 0.5f, pet * 0.12f,
                slot == 0 ? new Vector4(1f, 0.84f, 0.45f, 1f) : RacerChrome.CardBlue,
                (slot == 0 ? 0.34f : 0.18f) * fade);
            runner.Tick(ctx.ReduceMotion);
            runner.Draw(dl, ctx.Capabilities.Textures, feet, pet * scale, runner.Pose, props: false);
        }

        IconDraw.AddCentered(dl, FontAwesomeIcon.Bolt, Px(18f),
            centre - new Vector2(0f, pet * 1.28f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.72f, 0.20f, fade)));
    }

    /// <summary>The practice page's four rows: what a tournament race is capped at, how long the gate
    /// holds, and what happens once the stamps are spent.</summary>
    private void DrawRules(OsAppContext ctx, ImDrawListPtr dl, Vector2 stage, Vector2 size, float fade)
    {
        var day = _state?.StampsPerDay is > 0 and { } perDay ? perDay : DefaultStampsPerDay;
        var week = _state?.StampsPerWeek is > 0 and { } perWeek ? perWeek : DefaultStampsPerWeek;
        var minutes = _state?.GateMinutes is > 0 and { } gate ? gate : DefaultGateMinutes;
        object[] values = [day, week, minutes, 0];

        var line = ImGui.GetTextLineHeight();
        var step = line + Px(28f);
        var top = stage.Y + ((size.Y - (Rules.Length * step)) * 0.4f);

        var widest = 0f;
        for (var i = 0; i < Rules.Length; i++)
        {
            widest = MathF.Max(widest,
                ImGui.CalcTextSize(string.Format(ctx.Localize(Rules[i].Key), values[i])).X);
        }
        var left = stage.X + ((size.X - widest - Px(30f)) * 0.5f);

        for (var i = 0; i < Rules.Length; i++)
        {
            var at = new Vector2(left, top + (i * step));
            IconDraw.AddCentered(dl, Rules[i].Icon, Px(17f),
                new Vector2(at.X + Px(8f), at.Y + (line * 0.5f)),
                ImGui.ColorConvertFloat4ToU32(RacerChrome.DutchRed with { W = fade }));
            dl.AddText(new Vector2(at.X + Px(30f), at.Y),
                ImGui.ColorConvertFloat4ToU32(PageInk with { W = fade }),
                string.Format(ctx.Localize(Rules[i].Key), values[i]));
        }
    }

    /// <summary>The player's own creature if the state read has dressed it, a stand-in until then, so a
    /// page never draws an empty patch waiting for a round trip.</summary>
    private PetRuntime Own() => _ownLook.Length > 0 ? _own : _field[1];

    /// <summary>The racer's own element: the state read carries it once the creature has grown up, and
    /// the Easy offer confirms it (the easy pool IS the racer's own ground).</summary>
    private AetherlingElement? OwnElement()
    {
        if (_state?.PetElement is > 0 and { } element)
        {
            return (AetherlingElement)element;
        }
        foreach (var offer in _state?.Offers ?? [])
        {
            if (offer.Difficulty == (short)LumiRaceDifficulty.Easy && offer.Element > 0)
            {
                return (AetherlingElement)offer.Element;
            }
        }
        return null;
    }

    private void Refresh()
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                _pendingState = await host.GetStateAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The rules rows carry the shipped numbers when the read fails; an onboarding is no
                // place for an error.
            }
        });
    }
}
