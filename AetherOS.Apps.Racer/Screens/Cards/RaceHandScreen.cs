using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Shared;
using AetherLove.Shared.Racing;
using AetherLove.Shared.Racing.Cards;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>The race cards, chosen right before a solo race or a cup: a short explanation naming the player's Lumi,
/// one Gold slot and two Silver slots filled from the saved hand, and the button that saves the hand and then starts
/// the race or enters the cup. Tapping a slot opens the card picker over the page, and a picker tile's Info opens a
/// small card popup over the picker, so the player never leaves the page. Hub replies park in fields and are
/// drained at the top of <see cref="Draw"/>.
/// <para>With cards switched off on the server there is nothing to choose, so the screen goes straight on to the
/// race or the cup the moment it learns that.</para></summary>
internal sealed class RaceHandScreen(
    IRacerHost host,
    IAppStorage storage,
    Action<LumiRaceStartResultDto> openRace,
    Action enterCup)
{
    private const int NoSlot = -1;
    private const int NoAnchor = -1;
    private const string PickerPerRowKey = "racer.pickerCardsPerRow";
    private const int PickerDefaultPerRow = CardsPerRow.Max;
    private const float Inset = 12f;
    private const float SlotGap = 10f;
    private const float SlotFaceMax = 110f;
    private const float SlotFaceMin = 48f;
    private const float LineGap = 4f;
    private const float SectionGap = 10f;
    private const float PickerWidth = 420f;
    private const float PickerSideMargin = 24f;
    private const float PickerFallbackHeight = 360f;
    private const float PickerPanelPad = 16f;
    private const float PickerGridMin = 140f;
    private const float PickerFaceMin = 60f;
    private const float PickerGap = 10f;
    private const float PickerTilePad = 5f;
    private const float CancelChipHeight = CardChrome.ChipHeight + 8f;
    private const float PickerPaperAlpha = 0.93f;
    private const float InfoWidth = 340f;
    private const float InfoFallbackHeight = 200f;
    private static readonly Vector4 FaultInk = RacerChrome.DutchRed with { W = 1f };

    private readonly CardFaceRenderer _faces = new() { AlwaysShowsEffect = true };
    private readonly CardFaceRenderer _pickerFaces = new() { AlwaysShowsEffect = true };
    private readonly string[] _hand = new string[RaceCardHandRules.SlotCount];
    private readonly List<OwnedCard> _pickerCards = [];
    private LumiRaceCardsDto? _cards;
    private LumiRaceCardsDto? _pendingCards;
    private string? _pendingLoadError;
    private string? _loadError;
    private Dictionary<string, OwnedCard> _owned = new(StringComparer.Ordinal);
    private LumiRaceOfferDto? _offer;
    private bool _forCup;
    private bool _busy;
    private bool _autoStarted;
    private Exception? _pendingSaveFault;
    private string? _pendingStartError;
    private LumiRaceStartResultDto? _pendingStart;
    private bool _pendingEnterCup;
    private string? _fault;
    private int _pickerSlot = NoSlot;
    private float _pickerHeight;
    private int _pickerPerRow = CardsPerRow.Load(storage, PickerPerRowKey, PickerDefaultPerRow);
    private int _pickerAnchor = NoAnchor;
    private float _pickerLastScroll;
    private float _pickerLastPitch;
    private int _pickerLastColumns;
    private OwnedCard? _infoCard;
    private float _infoHeight;
    private string? _petName;
    private string? _pendingPetName;

    /// <summary>Whether the screen was opened for the cup rather than a solo offer.</summary>
    public bool ForCup => _forCup;

    public bool PickerOpen => _pickerSlot != NoSlot && _cards is not null;

    public void ShowSolo(LumiRaceOfferDto offer)
    {
        _offer = offer;
        _forCup = false;
        Reset();
    }

    public void ShowCup()
    {
        _offer = null;
        _forCup = true;
        Reset();
    }

    private void Reset()
    {
        _cards = null;
        _loadError = null;
        _fault = null;
        _busy = false;
        _autoStarted = false;
        _pickerSlot = NoSlot;
        _pickerHeight = 0f;
        _infoCard = null;
        Array.Fill(_hand, string.Empty);
        Load();
    }

    /// <summary>Loads the cards, then the racer state for the Lumi's name. The explanation names the Lumi when the
    /// state answers and says Lumi when it does not, so a failed state call never blocks the hand.</summary>
    private void Load()
    {
        _loadError = null;
        _ = Task.Run(async () =>
        {
            try
            {
                _pendingCards = await host.GetCardsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _pendingLoadError = host.DescribeError(ex);
                return;
            }

            try
            {
                var state = await host.GetStateAsync().ConfigureAwait(false);
                _pendingPetName = state.PetName;
            }
            catch (Exception)
            {
            }
        });
    }

    public void Draw(OsAppContext ctx, Action back)
    {
        Drain(ctx);
        var avail = ImGui.GetContentRegionAvail();
        using var body = ImRaii.Child("##raceHand", avail, false, ImGuiWindowFlags.NoBackground);
        if (!body)
        {
            return;
        }

        if (_loadError is { } failed && _cards is null)
        {
            ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
            RacerChrome.CenteredWrapped(failed);
            ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
            if (GrandstandFrame.ActionButton(ctx, host, "##handRetry", ctx.Localize("os.racer_cup_retry")))
            {
                Load();
            }

            DrawBack(ctx, back);
            return;
        }

        if (_cards is not { Enabled: true })
        {
            ImGui.Dummy(new Vector2(1f, avail.Y * 0.3f));
            if (_cards is null || _busy)
            {
                RacerChrome.CenteredText(ctx.Localize("os.racer_loading"));
            }

            DrawFault();
            DrawBack(ctx, back);
            return;
        }

        using (RacerFonts.Get(RacerTextSize.Caption)?.Push())
        {
            var name = _petName is { Length: > 0 } petName ? petName : ctx.Localize("os.racer_hand_lumi");
            RacerChrome.CenteredWrapped(string.Format(ctx.Localize("os.racer_hand_explain"), name));
        }

        ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
        DrawSlots(ctx);
        DrawFault();
        ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
        var label = ctx.Localize(_forCup ? "os.racer_hand_enter_the_cup" : "os.racer_race_now");
        if (GrandstandFrame.ActionButton(ctx, host, "##handStart", label, !_busy && !PickerOpen))
        {
            Start();
        }

        if (_busy)
        {
            ImGui.Dummy(new Vector2(1f, Px(LineGap)));
            RacerChrome.CenteredText(ctx.Localize("os.racer_loading"));
        }

        DrawBack(ctx, back);
    }

    private void Drain(OsAppContext ctx)
    {
        if (_pendingCards is { } fresh)
        {
            _pendingCards = null;
            Apply(fresh);
        }

        if (_pendingPetName is { } petName)
        {
            _pendingPetName = null;
            _petName = petName;
        }

        if (_pendingLoadError is { } loadError)
        {
            _pendingLoadError = null;
            _loadError = loadError;
        }

        if (_pendingSaveFault is { } saveFault)
        {
            _pendingSaveFault = null;
            _busy = false;
            _fault = FaultText(ctx, saveFault);
        }

        if (_pendingStartError is { } startError)
        {
            _pendingStartError = null;
            _busy = false;
            _fault = startError;
        }

        if (_pendingStart is { } start)
        {
            _pendingStart = null;
            _busy = false;
            openRace(start);
        }

        if (_pendingEnterCup)
        {
            _pendingEnterCup = false;
            _busy = false;
            enterCup();
        }

        if (_cards is { Enabled: false } && !_autoStarted)
        {
            _autoStarted = true;
            Start();
        }
    }

    private void Apply(LumiRaceCardsDto cards)
    {
        _cards = cards;
        _owned = OwnedCard.Collect(cards);
        var owned = RaceCardHandRules.Index(cards.Cards.Select(c => new RaceCardOwned(c.CardId, RaceCardLevels.Clamp(c.Level))));
        var ids = RaceCardHandRules.Normalize(cards.Hand.GoldCardId, cards.Hand.SilverCardIds, owned);
        _hand[RaceCardHandRules.GoldSlot] = ids.Gold;
        _hand[RaceCardHandRules.GoldSlot + 1] = ids.Silver1;
        _hand[RaceCardHandRules.GoldSlot + 2] = ids.Silver2;
    }

    /// <summary>Saves the hand, then starts the race or enters the cup. A refused save stops there and says why;
    /// with cards switched off nothing is saved.</summary>
    private void Start()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _fault = null;
        var save = _cards is { Enabled: true };
        var dto = new LumiRaceCardHandDto(_hand[0], [_hand[1], _hand[2]], DateTimeOffset.UtcNow);
        var offer = _offer;
        var forCup = _forCup;
        _ = Task.Run(async () =>
        {
            if (save)
            {
                try
                {
                    await host.SetCardHandAsync(dto).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _pendingSaveFault = ex;
                    return;
                }
            }

            if (forCup)
            {
                _pendingEnterCup = true;
                return;
            }

            try
            {
                _pendingStart = await host.StartRaceAsync(offer!.Difficulty, offer.CourseKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _pendingStartError = host.DescribeError(ex);
            }
        });
    }

    private void DrawSlots(OsAppContext ctx)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var inset = Px(Inset);
        var gap = Px(SlotGap);
        var width = Math.Clamp((avail - (inset * 2f) - (gap * 2f)) / RaceCardHandRules.SlotCount, Px(SlotFaceMin), Px(SlotFaceMax));
        var face = new Vector2(width, width * CardFaceLayout.Ratio);
        var total = (width * RaceCardHandRules.SlotCount) + (gap * 2f);
        var origin = ImGui.GetCursorScreenPos();
        var left = origin.X + ((avail - total) * 0.5f);
        var dl = ImGui.GetWindowDrawList();
        var ink = ImGui.GetColorU32(ImGuiCol.Text);
        var packReady = CardArt.PackReady(ctx);
        var nameH = CardChrome.NameLineHeight;
        var chipH = Px(CardChrome.ChipHeight);
        var live = !_busy && !PickerOpen;
        var bottom = origin.Y;
        for (var slot = 0; slot < RaceCardHandRules.SlotCount; slot++)
        {
            var x = left + (slot * (width + gap));
            CardChrome.NameLine(dl, CardChrome.SlotName(ctx, slot), new Vector2(x, origin.Y), width, ink);
            var at = new Vector2(x, origin.Y + nameH + Px(LineGap));
            ImGui.SetCursorScreenPos(at);
            var pressed = ImGui.InvisibleButton("##handSlot" + slot, face) && live;
            var hovered = ImGui.IsItemHovered() && live;
            if (hovered)
            {
                HandOnHover();
            }

            var held = _hand[slot].Length == 0 ? null : _owned.GetValueOrDefault(_hand[slot]);
            var below = at.Y + face.Y + Px(LineGap);
            var chipTop = below + nameH + CardChrome.PipHeight + (Px(LineGap) * 2f);
            if (held is null)
            {
                var prompt = slot == RaceCardHandRules.GoldSlot ? "os.racer_hand_choose_gold" : "os.racer_hand_choose_silver";
                CardChrome.EmptyPlate(ctx, dl, at, face, ctx.Localize(prompt), hovered);
            }
            else
            {
                if (hovered)
                {
                    CardChrome.Tooltip(CardStrings.NameOf(ctx, held.Card), ctx.Localize(CardStrings.Key(held.Card.Id, CardStrings.Short)));
                }

                _faces.Draw(ctx, dl, "hand" + slot, held.Card, held.Level, at, face, false, hovered, packReady);
                CardChrome.NameLine(dl, CardStrings.NameOf(ctx, held.Card), new Vector2(x, below), width, ink);
                CardChrome.LevelPips(dl, x + (width * 0.5f), below + nameH + Px(LineGap), held.Level);
                var chipAt = new Vector2(x, chipTop);
                if (CardChrome.Chip(ctx, "##handClear" + slot, ctx.Localize("os.racer_hand_remove"), chipAt, new Vector2(width, chipH), live, FontAwesomeIcon.Times))
                {
                    _hand[slot] = string.Empty;
                    _fault = null;
                }
            }

            bottom = MathF.Max(bottom, chipTop + chipH);
            if (pressed)
            {
                OpenPicker(slot);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, bottom + Px(LineGap)));
        ImGui.Dummy(new Vector2(1f, 1f));
    }

    private void OpenPicker(int slot)
    {
        _pickerSlot = slot;
        _pickerHeight = 0f;
        _pickerCards.Clear();
        var band = RaceCardHandRules.BandOf(slot);
        foreach (var card in RaceCardCatalogue.Cards)
        {
            if (card.Band == band && _owned.TryGetValue(card.Id, out var owned))
            {
                _pickerCards.Add(owned);
            }
        }
    }

    private void DrawFault()
    {
        if (_fault is not { } fault)
        {
            return;
        }

        ImGui.Dummy(new Vector2(1f, Px(LineGap)));
        using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
        using var ink = ImRaii.PushColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(FaultInk));
        RacerChrome.CenteredWrapped(fault);
    }

    private void DrawBack(OsAppContext ctx, Action back)
    {
        ImGui.Dummy(new Vector2(1f, Px(LineGap)));
        if (GrandstandFrame.ActionButton(ctx, host, "##handBack", ctx.Localize("os.racer_back"), !_busy && !PickerOpen, secondary: true))
        {
            back();
        }

        ImGui.Dummy(new Vector2(1f, Px(LineGap)));
    }

    /// <summary>The card picker over the whole grandstand: every owned card of the slot's band, one tile each. A card
    /// already in another slot is shaded and cannot be picked. A tile's Info opens the card popup over the picker.</summary>
    public void DrawOverlays(OsAppContext ctx)
    {
        if (!PickerOpen)
        {
            return;
        }

        var slot = _pickerSlot;
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var width = MathF.Min(winSize.X - Px(PickerSideMargin), Px(PickerWidth));
        var ink = RacerChrome.CardBlue with { W = 1f };
        var close = false;
        var dismissed = DrawPageOverlayPanel("racerHandPicker", winPos, winSize, ref _pickerHeight, Px(PickerFallbackHeight), innerW =>
        {
            using var text = ImRaii.PushColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(ink));
            using (RacerFonts.Get(RacerTextSize.Heading)?.Push())
            {
                RacerChrome.CenteredText(ctx.Localize(slot == RaceCardHandRules.GoldSlot ? "os.racer_hand_choose_gold" : "os.racer_hand_choose_silver"));
            }

            ImGui.Dummy(new Vector2(1f, Px(LineGap)));
            if (_pickerCards.Count == 0)
            {
                using var font = RacerFonts.Get(RacerTextSize.Body)?.Push();
                RacerChrome.CenteredWrapped(ctx.Localize(slot == RaceCardHandRules.GoldSlot ? "os.racer_hand_none_gold" : "os.racer_hand_none_silver"));
            }
            else
            {
                DrawPickerChooser(ctx, innerW);
                if (DrawPickerGrid(ctx, slot, innerW, winSize.Y))
                {
                    close = true;
                }
            }

            ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
            var chipAt = ImGui.GetCursorScreenPos();
            if (CardChrome.Chip(ctx, "##pickerCancel", ctx.Localize("os.racer_party_cancel"), chipAt, new Vector2(innerW, Px(CancelChipHeight)), true, FontAwesomeIcon.Times))
            {
                close = true;
            }

            ImGui.SetCursorScreenPos(chipAt + new Vector2(0f, Px(CancelChipHeight)));
            ImGui.Dummy(new Vector2(1f, 1f));
        },
        RacerChrome.Paper with { W = PickerPaperAlpha }, RacerChrome.CardBlue with { W = 0.55f }, width);
        if (dismissed || close)
        {
            _pickerSlot = NoSlot;
            _infoCard = null;
            return;
        }

        DrawInfo(ctx, winPos, winSize, ink);
    }

    /// <summary>The card popup a picker tile's Info opens: the card's name, its type line and its short effect,
    /// with a Close chip. Tapping outside it closes it too, and the picker stays open underneath.</summary>
    private void DrawInfo(OsAppContext ctx, Vector2 winPos, Vector2 winSize, Vector4 ink)
    {
        if (_infoCard is not { } owned)
        {
            return;
        }

        var card = owned.Card;
        var width = MathF.Min(winSize.X - Px(PickerSideMargin), Px(InfoWidth));
        var close = false;
        var dismissed = DrawPageOverlayPanel("racerHandCardInfo", winPos, winSize, ref _infoHeight, Px(InfoFallbackHeight), innerW =>
        {
            using var text = ImRaii.PushColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(ink));
            using (RacerFonts.Get(RacerTextSize.Button)?.Push())
            {
                RacerChrome.CenteredText(CardStrings.NameOf(ctx, card));
            }

            using (RacerFonts.Get(RacerTextSize.Caption)?.Push())
            {
                RacerChrome.CenteredText(CardStrings.TypeLine(ctx, card));
            }

            ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
            using (RacerFonts.Get(RacerTextSize.Body)?.Push())
            {
                var effect = ctx.Localize(CardStrings.Key(card.Id, CardStrings.Short));
                if (ImGui.CalcTextSize(effect).X <= innerW)
                {
                    RacerChrome.CenteredText(effect);
                }
                else
                {
                    RacerChrome.CenteredWrapped(effect);
                }
            }

            ImGui.Dummy(new Vector2(1f, Px(SectionGap)));
            var chipAt = ImGui.GetCursorScreenPos();
            if (CardChrome.Chip(ctx, "##cardInfoClose", ctx.Localize("os.racer_hand_info_close"), chipAt, new Vector2(innerW, Px(CancelChipHeight)), true, FontAwesomeIcon.Times))
            {
                close = true;
            }

            ImGui.SetCursorScreenPos(chipAt + new Vector2(0f, Px(CancelChipHeight)));
            ImGui.Dummy(new Vector2(1f, 1f));
        },
        RacerChrome.Paper with { W = 1f }, RacerChrome.CardBlue with { W = 0.55f }, width);
        if (dismissed || close)
        {
            _infoCard = null;
        }
    }

    /// <summary>The picker's 1, 2 or 3 per row chips, centred under its title.</summary>
    private void DrawPickerChooser(OsAppContext ctx, float innerW)
    {
        var size = new Vector2((Px(CardsPerRow.ChooserChipWidth) * CardsPerRow.Max) + (Px(CardsPerRow.ChooserGap) * (CardsPerRow.Max - 1)), Px(CardsPerRow.ChooserHeight));
        var row = ImGui.GetCursorScreenPos();
        var at = row + new Vector2((innerW - size.X) * 0.5f, 0f);
        var perRow = CardsPerRow.DrawChooser(ctx, "##pickerPerRow", _pickerPerRow, at, size);
        ImGui.SetCursorScreenPos(row);
        ImGui.Dummy(new Vector2(innerW, size.Y + Px(LineGap)));
        if (perRow != _pickerPerRow)
        {
            _pickerAnchor = CardsPerRow.TopIndex(_pickerLastScroll, _pickerLastPitch, _pickerLastColumns);
            _pickerPerRow = perRow;
            CardsPerRow.Save(storage, PickerPerRowKey, perRow);
        }
    }

    /// <summary>The picker's scrolling grid. True when a card was picked, which closes the picker. A face is never
    /// taller than the room the panel leaves, so one whole tile always fits in view; a narrower face is centred in
    /// its column.</summary>
    private bool DrawPickerGrid(OsAppContext ctx, int slot, float innerW, float windowHeight)
    {
        var style = CardsPerRow.StyleFor(_pickerPerRow);
        var columns = _pickerPerRow;
        var gap = Px(PickerGap);
        var pad = Px(PickerTilePad);
        var usable = innerW - ImGui.GetStyle().ScrollbarSize - (pad * 2f);
        var cellW = (usable - (gap * (columns - 1))) / columns;
        var nameH = CardChrome.NameLineHeightFor(style.NameText);
        var pipH = CardChrome.PipHeightAt(style.PipScale);
        var chipH = Px(style.ChipHeight);
        var lineGap = Px(LineGap);
        var extras = lineGap + nameH + lineGap + pipH + lineGap + chipH;
        var chrome = ImGui.GetCursorPosY() + Px(SectionGap) + Px(CancelChipHeight) + Px(PickerPanelPad);
        var room = MathF.Max(Px(PickerGridMin), windowHeight - Px(PickerSideMargin * 2f) - chrome);
        var tileW = MathF.Min(cellW, MathF.Max(Px(PickerFaceMin), (room - (pad * 2f) - extras) / CardFaceLayout.Ratio));
        var face = new Vector2(tileW, tileW * CardFaceLayout.Ratio);
        var pitch = face.Y + extras + gap;
        var rows = (_pickerCards.Count + columns - 1) / columns;
        var needed = (rows * pitch) - gap + (pad * 2f);
        var height = MathF.Min(needed, room);
        using var grid = ImRaii.Child("##handPickerGrid", new Vector2(innerW, height), false, ImGuiWindowFlags.NoBackground);
        if (!grid)
        {
            return false;
        }

        var scroll = ImGui.GetScrollY();
        if (_pickerAnchor != NoAnchor)
        {
            scroll = (Math.Min(_pickerAnchor, _pickerCards.Count - 1) / columns) * pitch;
            ImGui.SetScrollY(scroll);
            _pickerAnchor = NoAnchor;
        }

        _pickerLastScroll = scroll;
        _pickerLastPitch = pitch;
        _pickerLastColumns = columns;
        var dl = ImGui.GetWindowDrawList();
        var top = ImGui.GetCursorScreenPos();
        var origin = top + new Vector2(pad);
        var ink = ImGui.GetColorU32(ImGuiCol.Text);
        var packReady = CardArt.PackReady(ctx);
        var picked = false;
        var (first, end) = CardLayout.VisibleRows(_pickerCards.Count, columns, pitch, scroll, ImGui.GetWindowSize().Y);
        for (var row = first; row < end; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = (row * columns) + column;
                if (index >= _pickerCards.Count)
                {
                    break;
                }

                var owned = _pickerCards[index];
                var at = origin + new Vector2((column * (cellW + gap)) + ((cellW - tileW) * 0.5f), row * pitch);
                var id = owned.Card.Id;
                var elsewhere = Array.IndexOf(_hand, id);
                var taken = elsewhere >= 0 && elsewhere != slot;
                ImGui.SetCursorScreenPos(at);
                var pressed = ImGui.InvisibleButton("##pickFace" + id, face) && !taken;
                var hovered = ImGui.IsItemHovered() && !taken;
                if (hovered)
                {
                    HandOnHover();
                }

                _pickerFaces.Draw(ctx, dl, "pick" + id, owned.Card, owned.Level, at, face, false, hovered, packReady, style.FaceTextGrowth);
                if (elsewhere == slot)
                {
                    CardChrome.Ring(dl, at, face);
                }

                if (taken)
                {
                    CardChrome.ShadedPlate(ctx, dl, at, face, CardChrome.SlotName(ctx, elsewhere), style.PlateText);
                }

                var below = at.Y + face.Y + lineGap;
                CardChrome.NameLine(dl, CardStrings.NameOf(ctx, owned.Card), new Vector2(at.X, below), tileW, ink, style.NameText);
                var pipsTop = below + nameH + lineGap;
                CardChrome.LevelPips(dl, at.X + (tileW * 0.5f), pipsTop, owned.Level, style.PipScale);
                var chipAt = new Vector2(at.X, pipsTop + pipH + lineGap);
                if (CardChrome.Chip(ctx, "##pickInfo" + id, ctx.Localize("os.racer_inspect_tab_info"), chipAt, new Vector2(tileW, chipH), true, FontAwesomeIcon.InfoCircle, style.ChipText, style.IconScale))
                {
                    _infoCard = owned;
                    _infoHeight = 0f;
                }

                if (pressed)
                {
                    _hand[slot] = id;
                    _fault = null;
                    picked = true;
                }
            }
        }

        ImGui.SetCursorScreenPos(top);
        ImGui.Dummy(new Vector2(usable, needed));
        return picked;
    }

    private string FaultText(OsAppContext ctx, Exception ex)
    {
        var at = ex.Message.IndexOf(HubErrors.Sentinel, StringComparison.Ordinal);
        if (at >= 0)
        {
            var code = ex.Message[(at + HubErrors.Sentinel.Length)..].Split('|')[0];
            var key = code switch
            {
                HubErrors.LumiRaceCardUnknown => "os.racer_hand_fault_unknown",
                HubErrors.LumiRaceCardNotOwned => "os.racer_hand_fault_not_owned",
                HubErrors.LumiRaceCardWrongBand => "os.racer_hand_fault_wrong_band",
                HubErrors.LumiRaceCardDuplicate => "os.racer_hand_fault_duplicate",
                HubErrors.LumiRaceHandShape => "os.racer_hand_fault_shape",
                _ => null,
            };
            if (key is not null)
            {
                return ctx.Localize(key);
            }
        }

        return ctx.Localize("os.racer_hand_fault_failed") + " " + host.DescribeError(ex);
    }
}
