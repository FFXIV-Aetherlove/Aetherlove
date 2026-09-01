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
/// screen, because a card carrying its own track picture needs the room to be looked at.</summary>
internal sealed class RaceSelectionScreen(
    IRacerHost host,
    Action<LumiRaceStartResultDto> openRace,
    Action back,
    Action openDifficultyHelp,
    Func<bool> muted,
    Action toggleMute,
    Func<float> volume,
    Action<float> setVolume)
{
    private const int OfferCount = 3;

    private const float CardInset = 24f;
    private const float CardPad = 12f;
    private const float CardLineGap = 3f;
    private const float CardGap = 10f;
    private const float CardRound = 14f;

    /// <summary>The card's shape, and the shape its art is cut to: 8 wide by 3 tall.</summary>
    private const float CardAspect = 8f / 3f;

    /// <summary>How much of a card's width the text scrim covers. The art behind it is whatever the
    /// artist made it, so legibility is bought here rather than asked of every picture.</summary>
    private const float ScrimShare = 0.55f;

    private const float HeadingLuminance = 0.62f;

    private LumiRaceStateDto? _state;
    private LumiRaceStateDto? _pendingState;
    private LumiRaceStartResultDto? _pendingStart;
    private string? _error;
    private string? _pendingError;
    private bool _busy;

    public void OnShow()
    {
        _state = null;
        _error = null;
        _busy = false;
        Refresh();
    }

    public void Draw(OsAppContext ctx)
    {
        Drain();

        var avail = ImGui.GetContentRegionAvail();
        using var body = ImRaii.Child("##racerSelect", avail, false, ImGuiWindowFlags.NoScrollbar);
        if (!body)
        {
            return;
        }

        RacerBackdrop.Draw(ctx, host, ImGui.GetWindowPos(), ImGui.GetWindowSize(), dim: 0.30f);
        RacerChrome.DrawMuteChip(ctx, muted(), toggleMute, volume(), setVolume);
        ImGui.Dummy(new Vector2(1f, Px(34)));
        DrawHeading(ctx);

        ImGui.Dummy(new Vector2(1f, Px(12)));
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
            RacerChrome.CenteredNotice(ctx.Localize("os.racer_practice_note"));
            ImGui.Dummy(new Vector2(1f, Px(4)));
        }
        for (var i = 0; i < offers.Length; i++)
        {
            if (DrawOfferCard(ctx, i, offers[i], reason is not null))
            {
                StartSolo(offers[i].Difficulty, offers[i].CourseKey);
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

        DrawBack(ctx);
    }

    /// <summary>One offer as a card: its own track picture, then its grade, its track, the ground it
    /// runs on and the sky it runs under. The whole panel is the button.</summary>
    private bool DrawOfferCard(OsAppContext ctx, int index, LumiRaceOfferDto offer, bool blocked)
    {
        var width = ImGui.GetContentRegionAvail().X - Px(CardInset * 2f);
        var height = width / CardAspect;
        ImGui.SetCursorPosX(Px(CardInset));

        var tl = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton($"##racerOffer{index}", new Vector2(width, height))
            && !blocked && !_busy;
        var hovered = ImGui.IsItemHovered() && !blocked && !_busy;
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
        var scrimTo = tl.X + (width * ScrimShare);
        dl.AddRectFilledMultiColor(tl, new Vector2(scrimTo, br.Y),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.68f * dim)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.68f * dim)));

        var pad = Px(CardPad);
        var element = RacingElements.NameOf((AetherlingElement)offer.Element);
        var grade = HeadingInk(RacerChrome.GradeFlag(offer.Difficulty));
        float headingStep;
        using (ctx.HeadingFont?.Push())
        {
            headingStep = ImGui.GetTextLineHeight() + Px(CardLineGap);
            ShadowedText(dl, tl + new Vector2(pad, pad), grade with { W = dim },
                RacerChrome.DifficultyLabel(ctx, offer.Difficulty));
            ShadowedText(dl, tl + new Vector2(pad, pad + headingStep),
                new Vector4(0.97f, 0.97f, 0.99f, dim),
                ctx.Localize($"os.racer_course_{offer.CourseKey}"));
        }

        DrawElementChip(dl, tl + new Vector2(pad, pad + (headingStep * 2f) + Px(2)), ElementFx.For(element),
            ctx.Localize(element.Length == 0 ? "os.racer_element_neutral" : $"os.racer_element_{element}"), dim);

        DrawWeatherBadge(ctx, dl, tl, br, offer.WeatherKey, dim);

        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.35f * dim)), round,
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
        ImGui.SetTooltip($"{ctx.Localize(WeatherBadge.NameKey(key))}\n{ctx.Localize(WeatherBadge.TipKey(key))}");
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

        var face = RacerChrome.CardFace;
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(
            face with { W = face.W * (hovered ? 1f : 0.92f) }), round);
    }

    /// <summary>The page's two lines, on a soft dark plate of their own. White on the race-day picture
    /// alone lost against the sky, so the plate is what buys the contrast rather than a brighter ink
    /// that would still sit on clouds.</summary>
    private static void DrawHeading(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var title = ctx.Localize("os.racer_pick_title");
        var sub = ctx.Localize("os.racer_pick_sub");
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;

        Vector2 titleSize;
        using (ctx.TitleFont?.Push())
        {
            titleSize = ImGui.CalcTextSize(title);
        }
        var subSize = ImGui.CalcTextSize(sub);
        var gap = Px(2);
        var padX = Px(18);
        var padY = Px(8);
        var plateW = MathF.Min(width - Px(24), MathF.Max(titleSize.X, subSize.X) + (padX * 2f));
        var plateH = titleSize.Y + gap + subSize.Y + (padY * 2f);
        var plateTl = new Vector2(origin.X + ((width - plateW) * 0.5f), origin.Y - Px(2));
        dl.AddRectFilled(plateTl, plateTl + new Vector2(plateW, plateH), 0x8C000000u, Px(12));

        ImGui.SetCursorScreenPos(new Vector2(origin.X, plateTl.Y + padY));
        using (ctx.TitleFont?.Push())
        {
            RacerChrome.CenteredText(title);
        }
        ImGui.Dummy(new Vector2(1f, gap));
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 0.94f, 0.94f, 0.95f)))
        {
            RacerChrome.CenteredText(sub);
        }
        ImGui.Dummy(new Vector2(1f, padY));
    }

    private static void ShadowedText(ImDrawListPtr dl, Vector2 at, Vector4 ink, string text)
    {
        dl.AddText(at + new Vector2(Px(1.2f), Px(1.2f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.60f * ink.W)), text);
        dl.AddText(at, ImGui.ColorConvertFloat4ToU32(ink), text);
    }

    /// <summary>The ground's name in the tint the race stage will run it in, on a plate of the same
    /// colour, left-aligned to <paramref name="leftTop"/>.</summary>
    private static void DrawElementChip(ImDrawListPtr dl, Vector2 leftTop, ElementLook look, string name, float dim)
    {
        var line = ImGui.GetTextLineHeight();
        var padX = Px(8);
        var tl = leftTop;
        var br = new Vector2(tl.X + ImGui.CalcTextSize(name).X + (padX * 2f), tl.Y + line);
        var round = line * 0.5f;
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(look.Tint with { W = 0.22f * dim }), round);
        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(look.Tint with { W = 0.60f * dim }), round,
            ImDrawFlags.RoundCornersAll, Px(1.2f));
        dl.AddText(new Vector2(tl.X + padX, tl.Y),
            ImGui.ColorConvertFloat4ToU32(look.Tint with { W = dim }), name);
    }

    /// <summary>A grade's flag colour as card text, lifted to read over the art and never darkened.</summary>
    private static Vector4 HeadingInk(Vector4 flag) =>
        ElementFx.Luminance(flag) >= HeadingLuminance
            ? flag
            : ElementFx.AtLuminance(flag, HeadingLuminance);

    private void DrawHowDifficulty(OsAppContext ctx)
    {
        var label = ctx.Localize("os.racer_how_difficulty");
        var size = ImGui.CalcTextSize(label);
        ImGui.SetCursorPosX(MathF.Max(0f, (ImGui.GetWindowWidth() - size.X) * 0.5f));

        var tl = ImGui.GetCursorScreenPos();
        var pressed = ImGui.InvisibleButton("##racerHowDifficulty", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var dl = ImGui.GetWindowDrawList();
        var ink = new Vector4(1f, 1f, 1f, hovered ? 1f : 0.92f);
        // Its own plate, the heading's shape: a white line alone loses against a bright sky.
        var pad = new Vector2(Px(12), Px(5));
        dl.AddRectFilled(tl - pad, tl + size + pad, hovered ? 0x99000000u : 0x7A000000u, Px(10));
        dl.AddText(tl, ImGui.ColorConvertFloat4ToU32(ink), label);
        dl.AddLine(new Vector2(tl.X, tl.Y + size.Y), new Vector2(tl.X + size.X, tl.Y + size.Y),
            ImGui.ColorConvertFloat4ToU32(ink with { W = ink.W * 0.5f }), Px(1f));
        if (pressed)
        {
            openDifficultyHelp();
        }
    }

    private void DrawBack(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(1f, Px(10)));
        if (RacerChrome.FlagButton(ctx, "##racerPickBack", ctx.Localize("os.racer_back"),
            RacerChrome.DutchBlue, RacerChrome.WhiteInk, null, !_busy))
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
        }
        if (_pendingStart is { } start)
        {
            _pendingStart = null;
            _busy = false;
            openRace(start);
        }
        if (_pendingError is { } error)
        {
            _pendingError = null;
            _busy = false;
            _error = error;
        }
    }

    private DateTimeOffset ServerNow => _state?.ServerNowUtc ?? DateTimeOffset.UtcNow;

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
        if (state.NextRaceAtUtc is { } at && at > ServerNow)
        {
            var left = at - ServerNow;
            return string.Format(ctx.Localize("os.racer_next_race"), $"{(int)left.TotalMinutes:0}:{left.Seconds:00}");
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

    private void StartSolo(short difficulty, string? courseKey)
    {
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                _pendingStart = await host.StartRaceAsync(difficulty, courseKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _pendingError = host.DescribeError(ex);
            }
        });
    }
}
