using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Apps.Aetherling.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The mode layer: the frosted pill and what each mode makes of a touch. Petting is the
/// default and needs nothing; Feeding brings the basket and the thrown-crystal physics; Game is a
/// button that does nothing yet, on purpose.</summary>
internal sealed partial class PetScreen
{
    private const float FlightSeconds = 0.38f;
    private const float ThrowGravity = 2200f;
    private const float ThrowMinSpeed = 520f;

    private const float PetStrokeStep = 46f;
    private const float PetLineGapSeconds = 2.5f;
    private const float PetLineLongGapSeconds = 6f;

    private PetMode _mode = PetMode.Petting;

    private IReadOnlyList<StoreInventoryItemDto>? _inventory;
    private bool _inventoryLoading;
    private IReadOnlyList<StoreInventoryItemDto>? _pendingInventory;

    // The crystal in hand: grabbed from a basket chip, riding the cursor with a short velocity
    // memory so a flick throws it.
    private Elements.ElementDef? _carried;
    private Vector2 _carryVelocity;
    private Vector2 _lastMouse;

    // One crystal in the air at a time: either the guided arc to the mouth or a ballistic throw.
    private Elements.ElementDef? _flying;
    private bool _flyingBallistic;
    private float _flightT;
    private Vector2 _flightFrom;
    private Vector2 _flightPos;
    private Vector2 _flightVel;

    private bool _feedBusy;
    private AetherlingDto? _pendingFed;
    private string? _pendingFeedError;

    /// <summary>The growing-up ceremony. Built once and reused; it owns the page while it runs.</summary>
    internal EvolutionScene Evolution => _evolution ??= BuildEvolution();

    /// <summary>The earned-flourish ticket, built once for the same reason.</summary>
    internal ReactionTicketOverlay Ticket => _ticket ??= BuildTicket();

    private ReactionTicketOverlay? _ticket;

    private ReactionTicketOverlay BuildTicket()
    {
        var overlay = new ReactionTicketOverlay(host, pet);
        overlay.Revealed += dto =>
        {
            AdoptCore(dto);
            RefreshInventory();
        };
        return overlay;
    }

    /// <summary>Whether a growing-up is on screen right now. Asked without building the scene, so a
    /// page that has never grown anything does not construct one to answer no.</summary>
    public bool CeremonyRunning => _evolution?.Playing == true;

    private EvolutionScene? _evolution;

    private EvolutionScene BuildEvolution()
    {
        var scene = new EvolutionScene(pet);
        scene.Flashed += () =>
        {
            pet.CommitHeldForm(host.AssetRoot);
            host.PlayCrack();
        };
        return scene;
    }

    private float _petStroke;
    private float _petLineCooldown;
    private int _petLinesShown;
    private readonly List<int> _petLineBag = [];
    private readonly List<(string Text, Vector2 At, float Age)> _petLines = [];
    private readonly Random _petRng = new();

    /// <summary>Raised when the adulting moment has played out, so the app can hand over to the
    /// onboarding.</summary>
    public event Action? AdultingFinished;

    private const float BasketChipSize = 44f;
    private const float ShopChipHeight = 24f;
    private const float CountdownHeight = 46f;

    /// <summary>Inside this many seconds the countdown warms in colour: the last stretch is the only
    /// part of a wait anybody actually watches.</summary>
    private const float CountdownFinalSeconds = 10f;
    private const float ModesBottomMargin = 12f;
    private const float ModesRowGap = 10f;

    private bool ModesAvailable(AetherlingDto core) =>
        IntroSeen && !_namingOpen && _arrive >= 1f && _settle >= 1f && core.Growth is not null;

    /// <summary>True while the basket has nothing in it, which is the only time the shop chip
    /// takes a row of its own.</summary>
    private bool BasketEmpty()
    {
        foreach (var element in Elements.All)
        {
            if (PetState.CrystalCount(_inventory, element) > 0)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>How much of the page's foot the mode layer occupies, so the stage stops above it
    /// rather than under it. Every row it draws is counted here; a row that is drawn but not
    /// reserved is a row that lands on top of something.</summary>
    private float ModesReserved(AetherlingDto core)
    {
        if (!ModesAvailable(core))
        {
            return 0f;
        }

        var height = ModePill.Height + Px(ModesBottomMargin);
        if (_mode == PetMode.Feeding)
        {
            height += Px(ModesRowGap) + Px(BasketChipSize);
            if (FeedWaitRemaining(core) > TimeSpan.Zero)
            {
                height += Px(4f) + Px(CountdownHeight);
            }
            if (BasketEmpty())
            {
                height += Px(6f) + Px(ShopChipHeight);
            }
        }
        return height;
    }

    /// <summary>How long until it will eat again: the hour gate while it is growing, the wait for
    /// the next UTC day once a grown pet has had its three. Zero means it is hungry now. Measured
    /// against the server's clock through the snapshot's own stamp, so a skewed system clock cannot
    /// move it.</summary>
    private TimeSpan FeedWaitRemaining(AetherlingDto core)
    {
        if (core.Adult is null)
        {
            return PetState.FeedGateRemaining(core, ServerOffset(core));
        }
        if (PetState.AdultFeedsLeft(core) > 0)
        {
            return TimeSpan.Zero;
        }

        var serverNow = DateTimeOffset.UtcNow + ServerOffset(core);
        return serverNow.UtcDateTime.Date.AddDays(1) - serverNow.UtcDateTime;
    }

    /// <summary>The whole wait this countdown is a fraction of, so the track can drain rather than
    /// just sit there.</summary>
    private static TimeSpan FeedWaitTotal(AetherlingDto core) =>
        core.Adult is null
            ? TimeSpan.FromMinutes(Math.Max(1, core.Growth?.FeedGateMinutes ?? 60))
            : TimeSpan.FromDays(1);

    /// <summary>The wait as h:mm:ss, or m:ss under an hour. Seconds on purpose: a line that only
    /// said "about 60 minutes" for the first minute of an hour reads as though it is stuck.</summary>
    private static string FormatWait(TimeSpan wait)
    {
        var left = wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        return left.TotalHours >= 1d
            ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}"
            : $"{left.Minutes}:{left.Seconds:00}";
    }

    /// <summary>The countdown: a caption, the time in plain large digits, and a track that drains as
    /// the wait runs out. The bar carries the drama; the numbers just say what they say.</summary>
    private static void DrawCountdown(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float top,
        TimeSpan wait, TimeSpan total, float trackWidth)
    {
        var centreX = origin.X + (size.X * 0.5f);
        var final = wait.TotalSeconds <= CountdownFinalSeconds;

        // Deliberately still: the numbers change once a second on their own, and anything beating
        // along with them turns a quiet wait into something nagging for attention.
        var warm = new Vector4(1f, 0.84f, 0.45f, 1f);
        var digitColour = final ? Vector4.Lerp(Look.CrystalPale, warm, 0.85f) : Look.CrystalPale;
        var trackColour = final ? warm : Look.Crystal;

        Look.Centred(dl, ctx.Localize("os.aetherling_feed_countdown"), centreX, top,
            Look.U32(Look.Whisper, 0.65f), 0.72f);
        Look.Centred(dl, FormatWait(wait), centreX, top + Px(13f), Look.U32(digitColour, 0.95f), 1.3f);

        // The track drains left to right: what is left of the wait is what is left of the bar.
        var left = centreX - (trackWidth * 0.5f);
        var trackY = top + Px(38f);
        var height = Px(3f);
        var remaining = total.TotalSeconds <= 0d
            ? 0f
            : Math.Clamp((float)(wait.TotalSeconds / total.TotalSeconds), 0f, 1f);
        dl.AddRectFilled(new Vector2(left, trackY), new Vector2(left + trackWidth, trackY + height),
            Look.U32(new Vector4(1f, 1f, 1f, 0.07f)), height * 0.5f);
        if (remaining > 0f)
        {
            dl.AddRectFilled(new Vector2(left, trackY),
                new Vector2(left + (trackWidth * remaining), trackY + height),
                Look.U32(trackColour with { W = 0.7f }), height * 0.5f);
        }
    }

    /// <summary>The pill and, in feeding mode, the basket above it. Laid out bottom-up from the
    /// page's foot: pill, then basket, then the shop chip when there is nothing to feed.</summary>
    private void DrawModes(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherlingDto core)
    {
        var pillTop = origin.Y + size.Y - ModePill.Height - Px(ModesBottomMargin);
        if (_mode == PetMode.Feeding)
        {
            var basketTop = pillTop - Px(ModesRowGap) - Px(BasketChipSize);
            DrawBasket(ctx, dl, origin, size, basketTop, core);
        }

        var was = _mode;
        _mode = ModePill.Draw(ctx, dl, new Vector2(origin.X + (size.X * 0.5f), pillTop), _mode,
            core.Adult is not null, out var action);
        switch (action)
        {
            case PetPillAction.Games:
                GamesRequested?.Invoke();
                break;
            case PetPillAction.Wardrobe:
                WardrobeRequested?.Invoke();
                break;
            case PetPillAction.Stats:
                AboutRequested?.Invoke();
                break;
        }
        if (_mode != was)
        {
            _carried = null;
            if (_mode == PetMode.Feeding)
            {
                RefreshInventory();
            }
        }
    }

    // ------------------------------------------------------------------ feeding

    private void DrawBasket(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float top, AetherlingDto core)
    {
        var chip = Px(BasketChipSize);
        var gap = Px(8f);
        var count = Elements.All.Count;
        var total = (chip * count) + (gap * (count - 1));

        // The row never runs off a narrow phone: it tightens the gap first, then the chips.
        if (total > size.X - Px(24f))
        {
            gap = MathF.Max(Px(3f), (size.X - Px(24f) - (chip * count)) / (count - 1));
            total = (chip * count) + (gap * (count - 1));
            if (total > size.X - Px(24f))
            {
                chip = (size.X - Px(24f) - (gap * (count - 1))) / count;
                total = (chip * count) + (gap * (count - 1));
            }
        }
        var left = origin.X + ((size.X - total) * 0.5f);

        var gate = PetState.FeedGateRemaining(core, ServerOffset(core));
        var full = core.Adult is not null && PetState.AdultFeedsLeft(core) <= 0;
        // A growing-up takes the page over, and a crystal thrown into it is thrown at a creature that is
        // not there: the feed lands on a body held at its old shape, behind the ceremony, with nothing to
        // watch it happen. Read off the field rather than the property, which builds the scene on first
        // touch and would build it here every frame before there was ever anything to play.
        var evolving = _evolution is { Playing: true };
        var blocked = gate > TimeSpan.Zero || full || _feedBusy || _flying is not null || evolving;

        var anyOwned = false;
        for (var i = 0; i < count; i++)
        {
            var element = Elements.All[i];
            var owned = PetState.CrystalCount(_inventory, element);
            anyOwned |= owned > 0;
            var tl = new Vector2(left + (i * (chip + gap)), top);

            ImGui.SetCursorScreenPos(tl);
            ImGui.InvisibleButton($"##aetherlingCrystal{element.Key}", new Vector2(chip, chip));
            var hovered = ImGui.IsItemHovered();
            var usable = owned > 0 && !blocked;
            if (hovered && usable)
            {
                HandOnHover();
            }
            if (hovered)
            {
                ImGui.SetTooltip(BasketTooltip(ctx, element, owned, gate, full));
            }
            if (ImGui.IsItemActivated() && usable && _carried is null)
            {
                _carried = element;
                _carryVelocity = Vector2.Zero;
                _lastMouse = ImGui.GetMousePos();
            }

            var alpha = usable ? 1f : 0.35f;
            dl.AddRectFilled(tl, tl + new Vector2(chip, chip),
                Look.U32(element.Accent with { W = 0.12f * alpha }), Px(12f));
            dl.AddRect(tl, tl + new Vector2(chip, chip),
                Look.U32(element.Accent with { W = 0.45f * alpha }), Px(12f), ImDrawFlags.RoundCornersAll, Px(1.2f));
            DrawCrystal(ctx, dl, element, tl + new Vector2(chip * 0.5f, chip * 0.44f), chip * 0.66f, alpha);
            if (owned > 0)
            {
                // Inside the chip, along its foot: a badge hung off the top edge clips into
                // whatever row sits above.
                var badge = owned > 99 ? "99+" : owned.ToString();
                Look.Centred(dl, badge, tl.X + (chip * 0.5f), tl.Y + chip - Px(13f),
                    Look.U32(Look.CrystalPale, 0.95f), 0.74f);
            }
        }

        var above = top;
        var wait = FeedWaitRemaining(core);
        if (wait > TimeSpan.Zero)
        {
            above -= Px(4f) + Px(CountdownHeight);
            DrawCountdown(ctx, dl, origin, size, above, wait, FeedWaitTotal(core), total);
        }

        if (!anyOwned)
        {
            DrawShopChip(ctx, dl, origin, size, above - Px(6f) - Px(ShopChipHeight));
        }
    }

    /// <summary>The crystal itself, centred on a point at the given height. Falls back to the gem
    /// glyph when the art is missing or still decoding, so a fresh install never shows a hole.</summary>
    private static void DrawCrystal(
        OsAppContext ctx, ImDrawListPtr dl, Elements.ElementDef element, Vector2 centre, float size, float alpha)
    {
        var tint = new Vector4(1f, 1f, 1f, alpha);
        if (CoreAssets.CrystalPath(element.Key) is { } path
            && ctx.Capabilities.Textures.Get(path) is { } texture)
        {
            var half = size * 0.5f;
            dl.AddImage(texture, centre - new Vector2(half, half), centre + new Vector2(half, half),
                Vector2.Zero, Vector2.One, Look.U32(tint));
            return;
        }

        IconDraw.AddCentered(dl, FontAwesomeIcon.Gem, size * 0.55f, centre, Look.U32(element.Accent, 0.95f * alpha));
    }

    private string BasketTooltip(
        OsAppContext ctx, Elements.ElementDef element, int count, TimeSpan gate, bool full)
    {
        var name = ctx.Localize(Elements.NameKey(element));
        if (full)
        {
            return ctx.Localize("os.aetherling_feed_full_tip");
        }
        if (gate > TimeSpan.Zero)
        {
            return string.Format(ctx.Localize("os.aetherling_feed_gate_tip"),
                Math.Max(1, (int)Math.Ceiling(gate.TotalMinutes)));
        }
        if (count <= 0)
        {
            return string.Format(ctx.Localize("os.aetherling_feed_none_tip"), name);
        }
        return string.Format(ctx.Localize("os.aetherling_feed_chip_tip"), name, count);
    }

    private void DrawShopChip(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float y)
    {
        var label = ctx.Localize("os.aetherling_feed_shop");
        var height = Px(24f);
        var width = ImGui.CalcTextSize(label).X + Px(28f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), y);
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingFeedShop", new Vector2(width, height));
        if (ImGui.IsItemHovered())
        {
            HandOnHover();
        }
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Spark with { W = 0.18f }), height * 0.5f);
        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - (ImGui.GetTextLineHeight() * 0.82f)) * 0.5f), Look.U32(Look.CrystalPale, 0.9f), 0.82f);
        if (pressed)
        {
            // The shelf's own name: the store resolves a deep link against category names and falls back
            // to a text search, and "crystal" matched nothing, so this chip has always been a search.
            ctx.Shell.SendIntent("store", OsIntents.CreatePath(OsIntents.StoreOpen, "consumables"));
        }
    }

    /// <summary>The crystal in hand and in the air. Called every frame after the stage, so the
    /// gem rides over everything on the page.</summary>
    private void TickCarriedAndFlying(OsAppContext ctx, float dt, Vector2 stageTl, Vector2 stageSize)
    {
        var mouse = ImGui.GetMousePos();
        if (_carried is { } carried)
        {
            // A short memory of how the hand is moving, so letting go mid-flick throws.
            var frameVel = dt > 0f ? (mouse - _lastMouse) / dt : Vector2.Zero;
            _carryVelocity = Vector2.Lerp(_carryVelocity, frameVel, 0.35f);
            _lastMouse = mouse;

            DrawCrystal(ctx, ImGui.GetForegroundDrawList(), carried,
                mouse + new Vector2(Px(12f), Px(10f)), Px(30f), 1f);

            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _carried = null;
                ReleaseCrystal(ctx, carried, mouse, stageTl, stageSize);
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _carried = null;
            }
        }

        if (_flying is { } flying)
        {
            var mouthAt = MouthScreenPoint(stageTl, stageSize);
            if (_flyingBallistic)
            {
                _flightVel.Y += ThrowGravity * dt;
                _flightPos += _flightVel * dt;
                DrawCrystal(ctx, ImGui.GetForegroundDrawList(), flying, _flightPos, Px(28f), 1f);

                if (PetHit(_flightPos, stageTl, stageSize))
                {
                    LandCrystal(ctx, flying);
                }
                else if (_flightPos.Y > stageTl.Y + stageSize.Y + Px(10f)
                    || _flightPos.X < stageTl.X - Px(60f)
                    || _flightPos.X > stageTl.X + stageSize.X + Px(60f))
                {
                    // A miss fizzles: nothing consumed, a puff where it fell.
                    _flying = null;
                    _runtimeFizzleAt = _flightPos;
                    _runtimeFizzleIn = 0f;
                }
            }
            else
            {
                _flightT = MathF.Min(1f, _flightT + (dt / FlightSeconds));
                var eased = _flightT * _flightT;
                _flightPos = Vector2.Lerp(_flightFrom, mouthAt, eased);
                _flightPos.X += MathF.Sin(_flightT * MathF.PI) * Px(14f);

                // Shrinking as it goes in, so it reads as being eaten rather than parked on the face.
                DrawCrystal(ctx, ImGui.GetForegroundDrawList(), flying, _flightPos,
                    Px(28f) * (1f - (0.35f * _flightT)), 1f);
                if (_flightT >= 1f)
                {
                    LandCrystal(ctx, flying);
                }
            }
        }

        if (_runtimeFizzleIn >= 0f)
        {
            _runtimeFizzleIn += dt;
            var t = _runtimeFizzleIn / 0.4f;
            if (t >= 1f)
            {
                _runtimeFizzleIn = -1f;
            }
            else
            {
                var fg = ImGui.GetForegroundDrawList();
                fg.AddCircleFilled(_runtimeFizzleAt, Px(6f) * (1f + t),
                    Look.U32(new Vector4(1f, 1f, 1f, 0.35f * (1f - t))), 12);
            }
        }
    }

    private Vector2 _runtimeFizzleAt;
    private float _runtimeFizzleIn = -1f;

    private void ReleaseCrystal(
        OsAppContext ctx, Elements.ElementDef element, Vector2 at, Vector2 stageTl, Vector2 stageSize)
    {
        if (_flying is not null || _core is null)
        {
            return;
        }

        if (PetHit(at, stageTl, stageSize) || _carryVelocity.Length() < ThrowMinSpeed)
        {
            // Over the creature, or let go gently anywhere: the crystal finds the mouth itself.
            _flying = element;
            _flyingBallistic = false;
            _flightT = 0f;
            _flightFrom = at;
            _flightPos = at;
        }
        else
        {
            // A real throw: ballistic, and the aim is the player's own.
            _flying = element;
            _flyingBallistic = true;
            _flightPos = at;
            _flightVel = _carryVelocity;
        }
        _ = ctx;
    }

    /// <summary>Where the creature is on screen, roughly: the middle band of the stage's lower
    /// half, wide enough to be kind at every form.</summary>
    private bool PetHit(Vector2 at, Vector2 stageTl, Vector2 stageSize)
    {
        var centreX = stageTl.X + (stageSize.X * 0.5f);
        var half = stageSize.X * 0.24f;
        var top = stageTl.Y + (stageSize.Y * 0.34f);
        var bottom = stageTl.Y + stageSize.Y - Px(6f);
        return at.X >= centreX - half && at.X <= centreX + half && at.Y >= top && at.Y <= bottom;
    }

    private Vector2 MouthScreenPoint(Vector2 stageTl, Vector2 stageSize) => new(
        stageTl.X + (stageSize.X * 0.5f),
        stageTl.Y + (stageSize.Y * 0.68f));

    private void LandCrystal(OsAppContext ctx, Elements.ElementDef element)
    {
        _flying = null;
        if (_core is null || _feedBusy)
        {
            return;
        }

        // The chew is optimistic; the counts stay server-truthful. A raced refusal shows its
        // line after the crunch, which is the accepted cost of a mouth that answers instantly.
        pet.PlayFeedLand(element.Accent, ctx.ReduceMotion);
        _feedBusy = true;
        var value = (short)element.Value;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.FeedAsync(value).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingFed, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingFeedError, host.DescribeError(ex));
            }
        });
    }

    /// <summary>Takes what the feed round trip left: the new snapshot, an evolution if the form
    /// changed, the adulting hand-off, or a warm refusal.</summary>
    private void DrainFeeding(OsAppContext ctx, float dt)
    {
        if (Interlocked.Exchange(ref _pendingFed, null) is { } fed)
        {
            _feedBusy = false;
            var before = _core;
            AdoptCore(fed);
            RefreshInventory();

            var wasAdult = before?.Adult is not null;
            var oldForm = PetState.FormFolder(before);
            var newForm = PetState.FormFolder(fed);
            if (oldForm != newForm)
            {
                // The body is held at the old shape until the ceremony's flash asks for it, so no
                // surface can swap it early and nobody sees the change happen.
                var accent = Elements.Find(fed.Adult?.Element ?? 0)?.Accent
                    ?? new Vector4(0.62f, 0.88f, 0.85f, 1f);
                _adultingHandOff = !wasAdult && fed.Adult is not null;
                pet.HoldForm(newForm);
                Evolution.Begin(_adultingHandOff, accent);
            }
            else if (NewTicketSlot(before, fed) is { } ticket)
            {
                Ticket.Open(fed, ticket);
            }
            else
            {
                ShowFeedToast(ctx, fed);
            }
        }
        if (Interlocked.Exchange(ref _pendingFeedError, null) is { } error)
        {
            _feedBusy = false;
            pet.PlayRefusal(ctx.ReduceMotion);
            _feedToast = error;
            _feedToastLeft = 4f;
        }

        if (_feedToastLeft > 0f)
        {
            _feedToastLeft -= dt;
        }
    }

    /// <summary>A ticket the reply carries that the feed before it did not: the diet just earned a
    /// flourish. Unrevealed only, since a revealed card is a prize already in hand.</summary>
    private static short? NewTicketSlot(AetherlingDto? before, AetherlingDto fed)
    {
        foreach (var card in fed.Cards ?? [])
        {
            if (card.Slot <= ReactionTicketOverlay.SlotBase || card.RevealedAtUtc is not null)
            {
                continue;
            }
            var had = false;
            foreach (var old in before?.Cards ?? [])
            {
                had |= old.Slot == card.Slot;
            }
            if (!had)
            {
                return card.Slot;
            }
        }
        return null;
    }

    /// <summary>An earned flourish nobody has scratched for yet. The prize is only granted at the
    /// reveal, so an unclaimed ticket is a reaction the owner does not own.</summary>
    internal short? UnclaimedTicketSlot()
    {
        foreach (var card in _core?.Cards ?? [])
        {
            if (card.Slot > ReactionTicketOverlay.SlotBase && card.RevealedAtUtc is null)
            {
                return card.Slot;
            }
        }
        return null;
    }

    private bool _adultingHandOff;
    private string? _feedToast;
    private float _feedToastLeft;

    private void ShowFeedToast(OsAppContext ctx, AetherlingDto fed)
    {
        if (fed.Adult is null)
        {
            _feedToast = ctx.Localize("os.aetherling_feed_growth_toast");
        }
        else
        {
            _feedToast = ctx.Localize("os.aetherling_feed_treat_toast");
        }
        _feedToastLeft = 3.5f;
    }

    /// <summary>The clock offset sampled when the last reply landed; see AdoptCore.</summary>
    private TimeSpan _serverOffset;

    private TimeSpan ServerOffset(AetherlingDto core)
    {
        _ = core;
        return _serverOffset;
    }

    /// <summary>Re-reads what the account owns. Public because coming back from the store is the
    /// one moment the app's copy is known to be stale.</summary>
    public void RefreshInventory()
    {
        if (_inventoryLoading)
        {
            return;
        }
        _inventoryLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                if (await host.GetOwnedItemsAsync().ConfigureAwait(false) is { } items)
                {
                    Interlocked.Exchange(ref _pendingInventory, items);
                }
            }
            finally
            {
                _inventoryLoading = false;
            }
        });
    }

    /// <summary>Opens the page with the basket out, for the store's "use them" button.</summary>
    public void OpenFeeding()
    {
        _mode = PetMode.Feeding;
        _carried = null;
        RefreshInventory();
    }

    private void DrainInventory()
    {
        if (Interlocked.Exchange(ref _pendingInventory, null) is { } items)
        {
            _inventory = items;
            pet.SetOwnedReactions(PetState.OwnedRefs(items, StoreItemKind.AetherlingReaction));
        }
    }

    // ------------------------------------------------------------------ petting

    /// <summary>A stroke over the creature: distance accumulates into little lifts and a line of
    /// enjoyment now and then. Client-only, unfarmable, and rate-limited so it stays fresh.</summary>
    private void TickPetting(OsAppContext ctx, float dt, Vector2 stageTl, Vector2 stageSize)
    {
        _petLineCooldown = MathF.Max(0f, _petLineCooldown - dt);

        for (var i = _petLines.Count - 1; i >= 0; i--)
        {
            var (text, at, age) = _petLines[i];
            age += dt;
            if (age >= 2.2f)
            {
                _petLines.RemoveAt(i);
                continue;
            }
            _petLines[i] = (text, at, age);
            var alpha = age < 0.25f ? age / 0.25f : 1f - ((age - 0.25f) / 1.95f);
            var fg = ImGui.GetForegroundDrawList();
            Look.Centred(fg, text, at.X, at.Y - (age * Px(18f)),
                Look.U32(Look.CrystalPale, 0.9f * Math.Clamp(alpha, 0f, 1f)), 0.9f);
        }

        if (_mode != PetMode.Petting)
        {
            return;
        }

        var mouse = ImGui.GetMousePos();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) || !PetHit(mouse, stageTl, stageSize))
        {
            return;
        }

        _petStroke += ImGui.GetIO().MouseDelta.Length();
        if (_petStroke < PetStrokeStep)
        {
            return;
        }
        _petStroke = 0f;
        pet.Pet();

        if (_petLineCooldown > 0f)
        {
            return;
        }
        _petLinesShown += 1;
        _petLineCooldown = _petLinesShown % 4 == 0 ? PetLineLongGapSeconds : PetLineGapSeconds;
        _petLines.Add((NextPetLine(ctx), mouse + new Vector2(0f, -Px(24f)), 0f));
        host.PlayResponse();
    }

    /// <summary>A shuffle bag over the thirty lines, so the whole set plays out before any
    /// repeat and the same line never lands twice in a row.</summary>
    private string NextPetLine(OsAppContext ctx)
    {
        if (_petLineBag.Count == 0)
        {
            for (var i = 0; i < 30; i++)
            {
                _petLineBag.Add(i);
            }
            for (var i = _petLineBag.Count - 1; i > 0; i--)
            {
                var j = _petRng.Next(i + 1);
                (_petLineBag[i], _petLineBag[j]) = (_petLineBag[j], _petLineBag[i]);
            }
        }
        var index = _petLineBag[^1];
        _petLineBag.RemoveAt(_petLineBag.Count - 1);
        var name = _core?.PetName ?? AetherlingLimits.DefaultName;
        return string.Format(ctx.Localize($"os.aetherling_pet_line_{index}"), name);
    }
}
