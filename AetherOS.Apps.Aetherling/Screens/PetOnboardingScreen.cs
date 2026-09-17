using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.PetKit.Engine;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The two pages around the newborn's tour: the three gifts it brought, scratched before the
/// tour so the wardrobe already holds them when the tour gets there, and the one question at the end,
/// whether it may stand out on the game screen. Everything about it is resumable: revealed cards stay
/// revealed (the server rolled them at the birth and never re-rolls), and the finish stamps server-side
/// so a reinstall never replays it.
///
/// <para>The cards are drawn to the list rather than laid out by ImGui, because each prize face hangs
/// its art against the centre of a wrapped label and the stacked layout cannot.</para></summary>
internal sealed class PetOnboardingScreen(IAetherlingHost host, PetRuntime pet)
{
    public enum Page
    {
        Gifts,
        Floating,
    }

    private readonly ScratchCard[] _cards = [new(0), new(1), new(2)];
    private readonly ConfettiBurst _confetti = new();

    private AetherlingDto? _core;
    private Page _page;
    private bool _finishing;
    private short _revealBusySlot = -1;
    private AetherlingDto? _pendingRevealed;
    private string? _pendingError;
    private string? _error;
    private bool _confettiArmed;
    private bool _wantsFloating;
    private int _size = FloatingPet.DefaultSizeIndex;

    /// <summary>Raised when all three gifts are scratched and the player moves on. The tour follows.</summary>
    public event Action? GiftsDone;

    /// <summary>Raised when the floating question is answered, before the finish is sent. The app owns
    /// the switch this answers.</summary>
    public event Action<bool, int>? FloatingChosen;

    /// <summary>Raised when the server has stamped the onboarding done.</summary>
    public event Action? Finished;

    /// <summary>The size the last answer was given with, so the question opens on the current pick
    /// rather than back at the middle.</summary>
    public int SizeIndex
    {
        get => _size;
        set => _size = value;
    }

    /// <summary>The floating pet's current switch, so the question opens on the current answer.</summary>
    public bool FloatingEnabled { get; set; }

    /// <summary>True on the page that asks about the floating pet: from here on a yes may show it.</summary>
    public bool FloatingBeatReached => _page == Page.Floating;

    public void OnShow(AetherlingDto? core, Page page)
    {
        _core = core;
        _page = page;
        _finishing = false;
        _error = null;
        _revealBusySlot = -1;
        _confettiArmed = false;
        _wantsFloating = FloatingEnabled;
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
            Finished?.Invoke();
            return;
        }

        pet.Tick(ctx.ReduceMotion);

        if (_page == Page.Gifts)
        {
            DrawCards(ctx, dl, origin, size, core);
            return;
        }
        DrawFloating(ctx, dl, origin, size, core);
    }

    private void DrawFloating(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherlingDto core)
    {
        var name = core.PetName ?? AetherlingLimits.DefaultName;
        var pad = Px(24f);
        var width = size.X - (pad * 2f);
        var y = DrawHeader(ctx, dl, new Vector2(origin.X + pad, origin.Y + Px(38f)), width, FontAwesomeIcon.Eye,
            string.Format(ctx.Localize("os.aetherling_onb_float_title"), name),
            string.Format(ctx.Localize("os.aetherling_onb_float_body"), name));

        if (PetPageUi.Toggle(dl, origin, size, y, ctx.Localize("os.aetherling_status_show_outside"), _wantsFloating))
        {
            _wantsFloating = !_wantsFloating;
        }
        y += Px(46f);

        var footTop = origin.Y + size.Y - Px(66f);
        if (_wantsFloating)
        {
            dl.AddText(new Vector2(origin.X + pad, y), Look.U32(Look.Whisper, 0.85f),
                ctx.Localize("os.aetherling_size_label"));
            y += Px(24f);
            PetSizePicker.Draw(ctx, dl, new Vector2(origin.X + pad, y), width, ref _size);
            y += Px(50f);
        }

        // It stands for the size being picked, so it wears the pick: relative to the middle one, and
        // eased down, because a screen inside a phone cannot show a screen-sized creature honestly.
        if (pet.Ready)
        {
            var pick = _wantsFloating
                ? 1f + (((FloatingPet.SizeScales[_size] / FloatingPet.SizeScales[FloatingPet.DefaultSizeIndex]) - 1f)
                    * 0.55f)
                : 1f;
            var room = footTop - y - Px(12f);
            if (room > Px(60f))
            {
                var petSize = MathF.Min(width * 0.6f, room) * 0.82f * pick;
                var bottom = new Vector2(origin.X + (size.X * 0.5f), y + ((room + petSize) * 0.5f));
                Look.GroundGlow(dl, bottom, petSize * 0.5f, petSize * 0.12f, Look.Crystal, 0.45f);
                pet.Draw(dl, ctx.Capabilities.Textures, bottom, petSize, pet.Pose);
            }
        }

        if (_error is { Length: > 0 })
        {
            Look.Centred(dl, _error, origin.X + (size.X * 0.5f), footTop - Px(4f),
                Look.U32(new Vector4(0.95f, 0.6f, 0.55f, 0.95f)), 0.82f);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + size.Y - Px(54f)));
        if (OnboardingUi.DrawPrimaryButton(ctx.Localize("onboarding.finish"), !_finishing))
        {
            FloatingChosen?.Invoke(_wantsFloating, _size);
            Finish();
        }
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
            if (OnboardingUi.DrawPrimaryButton(ctx.Localize("os.aetherling_scratch_done"), true))
            {
                GiftsDone?.Invoke();
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

    private void Finish()
    {
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
