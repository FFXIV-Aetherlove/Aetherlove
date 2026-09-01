using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.PetKit.Engine;
using AetherOS.PetKit.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The grown-up's welcome: three sections explaining what an adult can do, then the
/// three scratch cards. Everything about it is resumable: the sections restart on a fresh visit,
/// revealed cards stay revealed (the server rolled them at the adulting and never re-rolls), and
/// the finish stamps server-side so a reinstall never replays it.
///
/// <para>It is drawn to the list rather than laid out by ImGui, because the bullets have to hang
/// their chip against the centre of a wrapped block and the stacked-layout version cannot: it
/// centres against one line and every two-line row sits crooked.</para></summary>
internal sealed class AdultOnboardingScreen(IAetherlingHost host, PetRuntime pet)
{
    private const int Sections = 3;
    private const float FlourishEverySeconds = 3.2f;

    private readonly ScratchCard[] _cards = [new(0), new(1), new(2)];
    private readonly ConfettiBurst _confetti = new();

    private AetherlingDto? _core;
    private int _step;
    private bool _onCards;
    private bool _finishing;
    private double _lastFlourish;
    private short _revealBusySlot = -1;
    private AetherlingDto? _pendingRevealed;
    private string? _pendingError;
    private string? _error;
    private bool _confettiArmed;

    /// <summary>Raised when the welcome is over. The app takes them straight into the wardrobe:
    /// they have just been handed three things to wear.</summary>
    public event Action? Finished;

    public void OnShow(AetherlingDto? core)
    {
        _core = core;
        _step = 0;
        _onCards = false;
        _finishing = false;
        _error = null;
        _revealBusySlot = -1;
        _confettiArmed = false;
        foreach (var card in _cards)
        {
            card.Reset();
        }
    }

    public void Apply(AetherlingDto? core)
    {
        if (core is not null)
        {
            _core = core;
        }
    }

    public void Draw(OsAppContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        Look.Backdrop(dl, ctx.Theme, origin, size);

        DrainPending();
        if (_core is not { Adult: not null } core)
        {
            Finish(immediate: true);
            return;
        }

        pet.Tick(ctx.ReduceMotion);

        if (_onCards)
        {
            DrawCards(ctx, dl, origin, size, core);
            return;
        }

        ImGui.SetCursorScreenPos(origin);
        if (OnboardingUi.DrawProgress(_step, Sections + 1, _step > 0))
        {
            _step = Math.Max(0, _step - 1);
        }

        var name = core.PetName ?? AetherlingLimits.DefaultName;
        var pad = Px(24f);
        // Measured from the window, not from the cursor: the progress bar only submits an item on the
        // steps that can go back, so a cursor-relative top would jump between step one and step two.
        var top = origin.Y + Px(38f);
        var bodyBottom = origin.Y + size.Y - Px(66f);
        var body = new Vector2(origin.X + pad, top);
        var width = size.X - (pad * 2f);

        switch (_step)
        {
            case 0:
                DrawElement(ctx, dl, body, width, bodyBottom, core, name);
                break;
            case 1:
                DrawCustomization(ctx, dl, body, width, name);
                break;
            case 2:
                DrawFeeding(ctx, dl, body, width, name);
                break;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + size.Y - Px(54f)));
        if (OnboardingUi.DrawPrimaryButton(ctx.Localize("onboarding.next"), true))
        {
            if (_step < Sections - 1)
            {
                _step += 1;
            }
            else
            {
                _onCards = true;
            }
        }
    }

    // ------------------------------------------------------------------ sections

    /// <summary>The element it turned out to be, and the creature itself as the whole picture. The
    /// badge is the element's own crystal, because a lightning bolt over the word Fire is the kind
    /// of detail that reads as a bug.</summary>
    private void DrawElement(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float width, float bottom, AetherlingDto core, string name)
    {
        var element = Elements.Find(core.Adult!.Element);
        var elementName = element is { } def ? ctx.Localize(Elements.NameKey(def)) : "";
        var accent = element?.Accent ?? Look.Crystal;
        var centreX = tl.X + (width * 0.5f);

        var badgeR = Px(30f);
        var badgeC = new Vector2(centreX, tl.Y + badgeR);
        dl.AddCircleFilled(badgeC, badgeR, Look.U32(accent, 0.16f), 32);
        DrawElementMark(ctx, dl, element, badgeC, badgeR * 1.4f, accent);

        var y = badgeC.Y + badgeR + Px(14f);
        using (ctx.TitleFont?.Push())
        {
            Look.Centred(dl, string.Format(ctx.Localize("os.aetherling_onb_element_title"), elementName),
                centreX, y, Look.U32(Look.CrystalPale));
            y += ImGui.GetTextLineHeight() + Px(8f);
        }

        var intro = string.Format(ctx.Localize("os.aetherling_onb_element_body"), name, elementName);
        y += Look.CentredWrapped(dl, intro, centreX, y, width - Px(8f), Look.U32(Look.Whisper, 0.9f), 0.95f)
            * Look.LineStep(0.95f);

        // Everything left over is the creature's. It is the point of the page, so it takes the room.
        var captionH = Look.LineStep(0.88f);
        var room = bottom - y - captionH - Px(16f);
        if (room < Px(60f))
        {
            return;
        }
        var petSize = MathF.Min(width * 0.92f, room);
        var feet = new Vector2(centreX, y + Px(8f) + ((room + petSize) * 0.5f));

        var now = ImGui.GetTime();
        if (!ctx.ReduceMotion && now - _lastFlourish >= FlourishEverySeconds)
        {
            _lastFlourish = now;
            if (element is { } flourishOf && ReactionDef.FindSignature(flourishOf.Key) is { } signature)
            {
                pet.PlayReaction(signature);
            }
        }

        Look.GroundGlow(dl, feet, petSize * 0.5f, petSize * 0.12f, accent, 0.45f);
        pet.Draw(dl, ctx.Capabilities.Textures, feet, petSize, pet.Pose);

        Look.Centred(dl, string.Format(ctx.Localize("os.aetherling_onb_element_caption"), name),
            centreX, feet.Y + Px(8f), Look.U32(Look.Whisper, 0.8f), 0.88f);
    }

    /// <summary>The element's crystal, the same art the feeding basket serves. The glyph is only
    /// the fallback for a fresh install whose art has not decoded yet.</summary>
    private static void DrawElementMark(
        OsAppContext ctx, ImDrawListPtr dl, Elements.ElementDef? element, Vector2 centre, float size, Vector4 accent)
    {
        if (element is { } def
            && CoreAssets.CrystalPath(def.Key) is { } path
            && ctx.Capabilities.Textures.Get(path) is { } texture)
        {
            var half = size * 0.5f;
            dl.AddImage(texture, centre - new Vector2(half), centre + new Vector2(half));
            return;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Gem, size * 0.5f, centre, Look.U32(accent, 0.95f));
    }

    private static void DrawCustomization(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float width, string name)
    {
        var y = DrawHeader(ctx, dl, tl, width, FontAwesomeIcon.HatWizard,
            string.Format(ctx.Localize("os.aetherling_onb_custom_title"), name),
            string.Format(ctx.Localize("os.aetherling_onb_custom_body"), name));

        y += DrawBullet(ctx, dl, tl.X, y, width, FontAwesomeIcon.Palette,
            string.Format(ctx.Localize("os.aetherling_onb_custom_palettes"), name));
        y += DrawBullet(ctx, dl, tl.X, y, width, FontAwesomeIcon.HatCowboy,
            ctx.Localize("os.aetherling_onb_custom_items"));
        DrawBullet(ctx, dl, tl.X, y, width, FontAwesomeIcon.Star,
            ctx.Localize("os.aetherling_onb_custom_more"));
    }

    private static void DrawFeeding(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float width, string name)
    {
        var y = DrawHeader(ctx, dl, tl, width, FontAwesomeIcon.Cookie,
            ctx.Localize("os.aetherling_onb_feed_title"),
            string.Format(ctx.Localize("os.aetherling_onb_feed_body"), name));

        y += DrawBullet(ctx, dl, tl.X, y, width, FontAwesomeIcon.Gem,
            ctx.Localize("os.aetherling_onb_feed_crystals"));
        y += DrawBullet(ctx, dl, tl.X, y, width, FontAwesomeIcon.ChartArea,
            ctx.Localize("os.aetherling_onb_feed_radar"));
        y += DrawBullet(ctx, dl, tl.X, y, width, FontAwesomeIcon.HandHoldingHeart,
            string.Format(ctx.Localize("os.aetherling_onb_feed_petting"), name));
        DrawBullet(ctx, dl, tl.X, y, width, FontAwesomeIcon.Heart,
            string.Format(ctx.Localize("os.aetherling_onb_feed_forever"), name));
    }

    /// <summary>Badge, title and paragraph. Returns the y the body under it starts at.</summary>
    private static float DrawHeader(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float width, FontAwesomeIcon icon, string title, string body)
    {
        var accent = Look.Crystal;
        var centreX = tl.X + (width * 0.5f);
        var badgeR = Px(28f);
        var badgeC = new Vector2(centreX, tl.Y + badgeR);
        dl.AddCircleFilled(badgeC, badgeR, Look.U32(accent, 0.16f), 32);
        IconDraw.AddCentered(dl, icon, Px(24f), badgeC, Look.U32(accent, 0.95f));

        var y = badgeC.Y + badgeR + Px(14f);
        using (ctx.TitleFont?.Push())
        {
            Look.Centred(dl, title, centreX, y, Look.U32(Look.CrystalPale));
            y += ImGui.GetTextLineHeight() + Px(8f);
        }
        y += Look.CentredWrapped(dl, body, centreX, y, width - Px(8f), Look.U32(Look.Body, 0.88f), 0.95f)
            * Look.LineStep(0.95f);
        return y + Px(14f);
    }

    /// <summary>One bullet: a chip hung against the MIDDLE of the wrapped text rather than its first
    /// line, which is the whole reason this is not the shared onboarding row. Returns its height.</summary>
    private static float DrawBullet(
        OsAppContext ctx, ImDrawListPtr dl, float x, float y, float width, FontAwesomeIcon icon, string text)
    {
        const float Scale = 0.92f;
        var chipR = Px(15f);
        var gap = Px(12f);
        var textX = x + (chipR * 2f) + gap;
        var textW = width - (chipR * 2f) - gap;
        // The INK's height, not the line box's: measuring with the last line's leading included hangs the
        // chip a quarter of a line below the text it belongs to.
        var textH = Look.BlockHeight(text, textW, Scale);
        var rowH = MathF.Max(chipR * 2f, textH);

        var chipC = new Vector2(x + chipR, y + (rowH * 0.5f));
        dl.AddCircleFilled(chipC, chipR, Look.U32(Look.Crystal, 0.18f), 24);
        IconDraw.AddCentered(dl, icon, Px(14f), chipC, Look.U32(Look.Crystal, 0.98f));

        Look.LeftWrapped(dl, text, textX, y + ((rowH - textH) * 0.5f), textW,
            Look.U32(Look.Body, 0.96f), Scale);
        _ = ctx;
        return rowH + Px(14f);
    }

    // ------------------------------------------------------------------ the cards

    private void DrawCards(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherlingDto core)
    {
        var pad = Px(18f);
        var name = core.PetName ?? AetherlingLimits.DefaultName;
        using (ctx.TitleFont?.Push())
        {
            dl.AddText(new Vector2(origin.X + pad, origin.Y + Px(18f)), Look.U32(Look.CrystalPale),
                ctx.Localize("os.aetherling_scratch_title"));
        }
        var rows = Look.CentredWrapped(dl, string.Format(ctx.Localize("os.aetherling_scratch_body"), name),
            origin.X + (size.X * 0.5f), origin.Y + Px(58f), size.X - (pad * 2f),
            Look.U32(Look.Body, 0.88f), 0.9f);

        var top = origin.Y + Px(62f) + (rows * Look.LineStep(0.9f)) + Px(10f);
        if (_error is { Length: > 0 })
        {
            Look.Centred(dl, _error, origin.X + (size.X * 0.5f), top,
                Look.U32(new Vector4(0.95f, 0.6f, 0.55f, 0.95f)), 0.82f);
            top += Look.LineStep(0.82f);
        }

        var gap = Px(12f);
        var room = (origin.Y + size.Y - Px(72f)) - top;
        var cardH = MathF.Max(Px(92f), (room - (gap * 2f)) / 3f);
        var allRevealed = true;
        for (short slot = 0; slot < 3; slot++)
        {
            var dto = CardFor(core, slot);
            var revealed = dto?.RevealedAtUtc is not null;
            allRevealed &= revealed;
            var tl = new Vector2(origin.X + pad, top + (slot * (cardH + gap)));
            var card = _cards[slot];
            var busy = _revealBusySlot == slot;
            var faceSlot = slot;
            card.Draw(ctx, dl, tl, new Vector2(size.X - (pad * 2f), cardH), revealed, busy,
                (faceTl, faceSize) => DrawFace(ctx, dl, faceTl, faceSize, CardFor(core, faceSlot), faceSlot));

            if (card.WantsReveal && _revealBusySlot < 0 && !revealed)
            {
                card.MarkRevealRequested();
                Reveal(slot);
            }
        }

        if (allRevealed)
        {
            if (!_confettiArmed)
            {
                _confettiArmed = true;
                if (!ctx.ReduceMotion)
                {
                    _confetti.Reset();
                }
            }
            _confetti.Draw(origin, origin + size);

            ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + size.Y - Px(54f)));
            if (OnboardingUi.DrawPrimaryButton(ctx.Localize("os.aetherling_scratch_done"), !_finishing))
            {
                Finish(immediate: false);
            }
        }
    }

    private static AetherlingScratchCardDto? CardFor(AetherlingDto core, short slot)
    {
        if (core.Cards is null)
        {
            return null;
        }
        foreach (var card in core.Cards)
        {
            if (card.Slot == slot)
            {
                return card;
            }
        }
        return null;
    }

    /// <summary>The prize face: the thing itself, drawn from the same art it will wear. Face-down
    /// cards tease the prize's kind and nothing else, because nothing else ever left the server.</summary>
    private void DrawFace(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size, AetherlingScratchCardDto? dto, short slot)
    {
        var kindKey = slot switch
        {
            0 => "os.aetherling_scratch_kind_accessory",
            1 => "os.aetherling_scratch_kind_arms",
            _ => "os.aetherling_scratch_kind_palette",
        };
        var centreX = tl.X + (size.X * 0.5f);
        Look.Centred(dl, ctx.Localize(kindKey), centreX, tl.Y + Px(10f), Look.U32(Look.Whisper, 0.7f), 0.8f);

        if (dto?.RevealedAtUtc is null)
        {
            return;
        }

        var artBox = size.Y - Px(34f);
        var artCentre = new Vector2(tl.X + Px(16f) + (artBox * 0.5f), tl.Y + Px(28f) + (artBox * 0.5f));
        var shown = DrawPrizeArt(ctx, dl, dto, artCentre, artBox);
        if (!shown)
        {
            var icon = (StoreItemKind)dto.PrizeKind switch
            {
                StoreItemKind.AetherlingArms => FontAwesomeIcon.Khanda,
                StoreItemKind.AetherlingPalette => FontAwesomeIcon.Palette,
                _ => FontAwesomeIcon.HatWizard,
            };
            IconDraw.AddCentered(dl, icon, artBox * 0.5f, artCentre, Look.U32(Look.Spark, 0.95f));
        }

        var labelX = tl.X + Px(24f) + artBox;
        var label = PrizeLabel(dto);
        var labelW = tl.X + size.X - Px(14f) - labelX;
        var labelH = Look.WrappedHeight(label, labelW, 1.0f);
        Look.LeftWrapped(dl, label, labelX, tl.Y + ((size.Y - labelH) * 0.5f) + Px(6f), labelW,
            Look.U32(Look.CrystalPale), 1.0f);
    }

    /// <summary>The prize as a picture: worn things draw their own sprite, a palette draws the
    /// colours it actually paints on. False when nothing could be drawn.</summary>
    private bool DrawPrizeArt(
        OsAppContext ctx, ImDrawListPtr dl, AetherlingScratchCardDto dto, Vector2 centre, float box)
    {
        var refs = dto.PrizeRefs ?? [];
        if (refs.Length == 0 || pet.Catalogue is not { } catalogue)
        {
            return false;
        }

        if ((StoreItemKind)dto.PrizeKind == StoreItemKind.AetherlingPalette)
        {
            var palette = catalogue.PaletteByRef(refs[0]);
            var r = box * 0.34f;
            dl.AddCircleFilled(centre, r, Look.U32(palette.BodyColor), 32);
            dl.PathArcTo(centre, r, -MathF.PI / 2f, MathF.PI / 2f, 20);
            dl.PathLineTo(centre);
            dl.PathFillConvex(Look.U32(palette.AccentColor));
            dl.AddCircleFilled(centre, r * 0.30f, Look.U32(palette.EyeColor), 20);
            dl.AddCircle(centre, r, Look.U32(Look.CrystalPale, 0.35f), 32, Px(1.2f));
            return true;
        }

        // Two-piece prizes (a sword and its shield) are drawn side by side at three quarters.
        var drawn = false;
        var pieces = Math.Min(refs.Length, 2);
        for (var i = 0; i < pieces; i++)
        {
            if (catalogue.Accessory(refs[i]) is not { } def
                || ctx.Capabilities.Textures.Get(catalogue.AccessoryImagePath(def)) is not { } texture)
            {
                continue;
            }
            var fit = box * (pieces > 1 ? 0.62f : 0.82f) / MathF.Max(def.Width, def.Height);
            var half = new Vector2(def.Width, def.Height) * fit * 0.5f;
            var at = pieces > 1
                ? centre + new Vector2((i == 0 ? -1f : 1f) * box * 0.18f, 0f)
                : centre;
            dl.AddImage(texture, at - half, at + half);
            drawn = true;
        }
        return drawn;
    }

    private string PrizeLabel(AetherlingScratchCardDto dto)
    {
        var refs = dto.PrizeRefs ?? [];
        if (refs.Length == 0)
        {
            return "";
        }
        var catalogue = pet.Catalogue;
        var names = new string[refs.Length];
        for (var i = 0; i < refs.Length; i++)
        {
            if ((StoreItemKind)dto.PrizeKind == StoreItemKind.AetherlingPalette)
            {
                names[i] = catalogue?.PaletteByRef(refs[i]).Name ?? refs[i];
            }
            else
            {
                names[i] = catalogue?.Accessory(refs[i])?.Name ?? refs[i];
            }
        }
        return string.Join(" + ", names);
    }

    // ------------------------------------------------------------------ round trips

    private void Reveal(short slot)
    {
        _revealBusySlot = slot;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.RevealScratchAsync(slot).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingRevealed, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }

    private void Finish(bool immediate)
    {
        if (immediate)
        {
            Finished?.Invoke();
            return;
        }
        if (_finishing)
        {
            return;
        }
        _finishing = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.CompleteOnboardingAsync().ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingRevealed, dto);
            }
            catch (Exception ex)
            {
                _finishing = false;
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }

    private void DrainPending()
    {
        if (Interlocked.Exchange(ref _pendingRevealed, null) is { } dto)
        {
            _core = dto;
            if (_revealBusySlot >= 0)
            {
                _cards[_revealBusySlot].Celebrate();
            }
            _revealBusySlot = -1;
            if (_finishing && dto.OnboardingDoneAtUtc is not null)
            {
                _finishing = false;
                Finished?.Invoke();
            }
        }
        if (Interlocked.Exchange(ref _pendingError, null) is { } error)
        {
            _revealBusySlot = -1;
            _error = error;
        }
    }
}
