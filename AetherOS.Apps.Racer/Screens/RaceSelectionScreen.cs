using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Racing;
using AetherOS.Apps.Racer.Rendering;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The three courses on offer, one card each. Its own page rather than a strip on the home
/// screen, because a card carrying its own track picture needs the room to be looked at. Pressing an offer
/// does not start the race: it hands the offer on to the race hand screen, which starts it.</summary>
internal sealed class RaceSelectionScreen(
    IRacerHost host,
    Action<LumiRaceOfferDto> chooseOffer,
    Action back,
    Action openDifficultyHelp)
{
    private const int OfferCount = 3;

    private const float CardInset = 24f;
    private const float CardPad = 12f;
    private const float CardLineGap = 3f;
    private const float CardGap = 10f;
    private const float CardRound = 14f;

    /// <summary>How much of a card's width the text scrim covers. The art behind it is whatever the
    /// artist made it, so legibility is bought here rather than asked of every picture.</summary>
    private const float ScrimShare = 0.62f;

    /// <summary>The share of the width the scrim holds at full strength before it fades: the heading
    /// block sits inside it, so a bright sky behind "Normal" cannot wash the grade out.</summary>
    private const float ScrimHoldShare = 0.34f;

    private const float ScrimAlpha = 0.74f;

    private const float HeadingLuminance = 0.62f;

    private LumiRaceStateDto? _state;
    private TimeSpan _serverOffset;
    private LumiRaceStateDto? _pendingState;
    private string? _error;
    private string? _pendingError;

    public void OnShow()
    {
        _state = null;
        _error = null;
        Refresh();
    }

    public void Draw(OsAppContext ctx)
    {
        Drain();

        var avail = ImGui.GetContentRegionAvail();
        using var body = ImRaii.Child("##racerSelect", avail, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        if (!body)
        {
            return;
        }

        using (RacerFonts.Get(RacerTextSize.Caption)?.Push())
        {
            RacerChrome.CenteredWrapped(ctx.Localize("os.racer_pick_sub"));
        }
        ImGui.Dummy(new Vector2(1f, Px(10)));
        if (_state is not { } state)
        {
            RacerChrome.CenteredText(ctx.Localize("os.racer_loading"));
            DrawBack(ctx);
            return;
        }

        var offers = state.Offers;
        if (offers is not { Length: OfferCount })
        {
            RacerChrome.CenteredMuted(ctx.Localize("os.racer_pick_none"));
            DrawBack(ctx);
            return;
        }

        var reason = RaceReason(ctx, state);
        if (reason is null && HomeScreen.IsPractice(state))
        {
            RacerChrome.CenteredNotice(string.Format(ctx.Localize("os.racer_practice_note"), HomeScreen.Countdown(ctx, HomeScreen.PracticeLeft(state, ServerNow))));
            ImGui.Dummy(new Vector2(1f, Px(4)));
        }
        for (var i = 0; i < offers.Length; i++)
        {
            if (DrawOfferCard(ctx, i, offers[i], reason is not null))
            {
                chooseOffer(offers[i]);
            }
            if (i == 0 && reason is { Length: > 0 })
            {
                ImGui.Dummy(new Vector2(1f, Px(2)));
                RacerChrome.CenteredMuted(reason);
            }
            if (i < offers.Length - 1)
            {
                ImGui.Dummy(new Vector2(1f, Px(CardGap)));
            }
        }

        ImGui.Dummy(new Vector2(1f, Px(8)));
        DrawHowDifficulty(ctx);
        if (_error is { } error)
        {
            ImGui.Dummy(new Vector2(1f, Px(6)));
            RacerChrome.CenteredMuted(error);
        }
    }

    /// <summary>One offer as a card: its own track picture, then its grade, its track, the ground it
    /// runs on and the sky it runs under. The whole panel is the button.</summary>
    private bool DrawOfferCard(OsAppContext ctx, int index, LumiRaceOfferDto offer, bool blocked)
    {
        var width = ImGui.GetContentRegionAvail().X - Px(CardInset * 2f);
        var height = width / RacerChrome.CourseArtAspect;
        ImGui.SetCursorPosX(Px(CardInset));

        var tl = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton($"##racerOffer{index}", new Vector2(width, height)) && !blocked;
        var hovered = ImGui.IsItemHovered() && !blocked;
        if (hovered)
        {
            HandOnHover();
        }

        var dl = ImGui.GetWindowDrawList();
        var br = tl + new Vector2(width, height);
        var dim = blocked ? 0.45f : 1f;
        var round = Px(CardRound);
        dl.AddRectFilled(tl + new Vector2(0f, Px(3)), br + new Vector2(0f, Px(3)), 0x66000000u, round);

        dl.PushClipRect(tl, br, true);
        DrawCourseArt(ctx, dl, tl, br, offer.CourseKey, round, dim, hovered);

        // The scrim, not the art, is what makes the words readable: seven pictures cannot all be
        // asked to hold their contrast in the same corner, and a new one must not be able to break
        // the page by being bright.
        var scrimHold = tl.X + (width * ScrimHoldShare);
        var scrimTo = tl.X + (width * ScrimShare);
        var scrimInk = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, ScrimAlpha * dim));
        var scrimClear = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0f));
        dl.AddRectFilled(tl, new Vector2(scrimHold, br.Y), scrimInk, round, ImDrawFlags.RoundCornersLeft);
        dl.AddRectFilledMultiColor(new Vector2(scrimHold, tl.Y), new Vector2(scrimTo, br.Y),
            scrimInk, scrimClear, scrimClear, scrimInk);

        var pad = Px(CardPad);
        var element = RacingElements.NameOf((AetherlingElement)offer.Element);
        var grade = HeadingInk(RacerChrome.GradeFlag(offer.Difficulty));
        float headingStep;
        using (RacerFonts.Get(RacerTextSize.Button)?.Push())
        {
            headingStep = ImGui.GetTextLineHeight() + Px(CardLineGap);
            ShadowedText(dl, tl + new Vector2(pad, pad), grade with { W = dim },
                RacerChrome.DifficultyLabel(ctx, offer.Difficulty));
        }
        float courseHeight;
        using (RacerFonts.Get(RacerTextSize.Body)?.Push())
        {
            var name = ctx.Localize($"os.racer_course_{offer.CourseKey}");
            courseHeight = ImGui.CalcTextSize(name, false, width - pad * 2).Y;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), tl + new Vector2(pad, pad + headingStep),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, dim)), name, width - pad * 2);
        }
        using (RacerFonts.Get(RacerTextSize.Body)?.Push())
        {
            DrawElementChip(dl, tl + new Vector2(pad, pad + headingStep + courseHeight + Px(6)), ElementFx.For(element),
                ctx.Localize(element.Length == 0 ? "os.racer_element_neutral" : $"os.racer_element_{element}"), dim);
        }

        DrawWeatherBadge(ctx, dl, tl, br, offer.WeatherKey, dim);

        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, .81f, .43f, .8f * dim)), round,
            ImDrawFlags.RoundCornersAll, Px(1.4f));
        dl.PopClipRect();
        return pressed;
    }

    /// <summary>The sky this race runs under, in the card's top-right corner, with its name and what it
    /// does on hover. Hit-tested by hand rather than submitted: the whole card is one button underneath,
    /// and a later item over it would lose every click to the card anyway (first-submitted-wins).</summary>
    private static void DrawWeatherBadge(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 br,
        string weatherKey, float dim)
    {
        var key = string.IsNullOrEmpty(weatherKey) ? AetherRaceLive.ClearWeather : weatherKey;
        var side = Px(34);
        var centre = new Vector2(br.X - Px(CardPad) - (side * 0.5f), tl.Y + Px(CardPad) + (side * 0.5f));
        WeatherBadge.Draw(dl, key, centre, side, dim);

        var mouse = ImGui.GetMousePos();
        if ((mouse - centre).Length() > side * 0.5f)
        {
            return;
        }
        Cards.CardChrome.Tooltip(ctx.Localize(WeatherBadge.NameKey(key)), ctx.Localize(WeatherBadge.TipKey(key)));
    }

    /// <summary>The track's own picture, or the house panel while there is none. Art is optional on
    /// purpose: a course whose picture has not been drawn yet still gets a usable card.</summary>
    private void DrawCourseArt(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 br,
        string courseKey, float round, float dim, bool hovered)
    {
        var path = System.IO.Path.Combine(host.PetAssetRoot, "racer", "courses", $"{courseKey}.png");
        var tint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, (hovered ? 1f : 0.94f) * dim));
        if (ctx.Capabilities.Textures.Get(path) is { } art)
        {
            dl.AddImageRounded(art, tl, br, Vector2.Zero, Vector2.One, tint, round,
                ImDrawFlags.RoundCornersAll);
            return;
        }

        if (Rendering.ScenicCourseCards.Draw(dl, courseKey, tl, br))
        {
            return;
        }

        var face = RacerChrome.CardFace;
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(
            face with { W = face.W * (hovered ? 1f : 0.92f) }), round);
    }

    private static void ShadowedText(ImDrawListPtr dl, Vector2 at, Vector4 ink, string text)
    {
        dl.AddText(new Vector2(MathF.Round(at.X), MathF.Round(at.Y)), ImGui.ColorConvertFloat4ToU32(ink), text);
    }

    /// <summary>The ground's name in the tint the race stage will run it in, lifted to heading
    /// luminance and shadowed on a dark plate ringed in the same tint, left-aligned to
    /// <paramref name="leftTop"/>. Like the headings, it buys its legibility from the plate, never
    /// from the art behind it.</summary>
    private static void DrawElementChip(ImDrawListPtr dl, Vector2 leftTop, ElementLook look, string name, float dim)
    {
        var line = ImGui.GetTextLineHeight();
        var padX = Px(8);
        var tl = leftTop;
        var br = new Vector2(tl.X + ImGui.CalcTextSize(name).X + (padX * 2f), tl.Y + line + Px(6));
        var round = line * 0.5f;
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(.04f, .07f, .12f, .92f * dim)), round);
        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(look.Tint with { W = 0.75f * dim }), round,
            ImDrawFlags.RoundCornersAll, Px(1.2f));
        ShadowedText(dl, new Vector2(tl.X + padX, tl.Y + Px(3)), new Vector4(1, .96f, .87f, dim), name);
    }

    /// <summary>A grade's flag colour as card text, lifted to read over the art and never darkened.</summary>
    private static Vector4 HeadingInk(Vector4 flag) =>
        ElementFx.Luminance(flag) >= HeadingLuminance
            ? flag
            : ElementFx.AtLuminance(flag, HeadingLuminance);

    private void DrawHowDifficulty(OsAppContext ctx)
    {
        if (GrandstandFrame.ActionButton(ctx, host, "##racerHowDifficulty", ctx.Localize("os.racer_how_difficulty"), secondary: true, secondaryIcon: Dalamud.Interface.FontAwesomeIcon.Question))
        {
            openDifficultyHelp();
        }
    }

    private void DrawBack(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(1f, Px(10)));
        if (GrandstandFrame.ActionButton(ctx, host, "##racerPickBack", ctx.Localize("os.racer_back"), secondary: true))
        {
            back();
        }
    }

    private void Drain()
    {
        if (_pendingState is { } state)
        {
            _pendingState = null;
            _state = state;
            _serverOffset = state.ServerNowUtc - DateTimeOffset.UtcNow;
        }
        if (_pendingError is { } error)
        {
            _pendingError = null;
            _error = error;
        }
    }

    private DateTimeOffset ServerNow => DateTimeOffset.UtcNow + _serverOffset;

    /// <summary>Why no card can be pressed, or null when they can. An empty string dims them without
    /// printing anything, for the two pet refusals the home screen's popup already explains.</summary>
    private string? RaceReason(OsAppContext ctx, LumiRaceStateDto state)
    {
        if (!state.Enabled)
        {
            return ctx.Localize("os.racer_no_races");
        }
        if (!state.PetHatched || !state.PetAdult)
        {
            return string.Empty;
        }
        return null;
    }

    private void Refresh()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                _pendingState = await host.GetStateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _pendingError = host.DescribeError(ex);
            }
        });
    }
}
