using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.PetKit.Engine;
using AetherOS.PetKit.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The foot of the pet page: the basket and its thrown-crystal physics, sitting above the
/// nav bar. There are no modes: a stroke anywhere on the stage pets, and the basket is always out,
/// so a touch never has to be told what it meant.</summary>
internal sealed partial class PetScreen
{
    private const float FlightSeconds = 0.38f;
    private const float ThrowGravity = 2200f;
    private const float ThrowMinSpeed = 520f;

    private const float PetStrokeStep = 46f;
    private const float PetLineGapSeconds = 2.5f;
    private const float PetLineLongGapSeconds = 6f;

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

    /// <summary>The earned-flourish ticket, built once and reused.</summary>
    internal ReactionTicketOverlay Ticket => _ticket ??= BuildTicket();

    private ReactionTicketOverlay? _ticket;

    /// <summary>A won form, on its way to the wardrobe with the page's blessing.</summary>
    public event Action<string>? WardrobeFormRequested;

    private ReactionTicketOverlay BuildTicket()
    {
        var overlay = new ReactionTicketOverlay(host, pet);
        overlay.Revealed += dto =>
        {
            AdoptCore(dto);
            RefreshInventory();
        };
        overlay.WearRequested += shellRef => WardrobeFormRequested?.Invoke(shellRef);
        return overlay;
    }

    private float _petStroke;
    private float _petLineCooldown;
    private int _petLinesShown;
    private readonly List<int> _petLineBag = [];
    private readonly List<(string Text, Vector2 At, float Age)> _petLines = [];
    private readonly Random _petRng = new();

    private const float BasketChipSize = 54f;
    private const float BowlHeight = 46f;
    private const float CountdownHeight = 54f;
    private Elements.ElementDef? _emptyCrystal;

    /// <summary>Inside this many seconds the countdown warms in colour: the last stretch is the only
    /// part of a wait anybody actually watches.</summary>
    private const float CountdownFinalSeconds = 10f;
    private const float ModesRowGap = 10f;
    private const float AppetiteCueGapSeconds = 300f;

    private double _nextAppetiteCueAt;

    /// <summary>Whether something on the page owns it: the arrival, the naming card, an unopened ticket
    /// or the wheel. The stage runs full height while one of those is up and its whole-card target is
    /// submitted first, so a nav bar drawn over it would be structurally dead as well as in the way.</summary>
    public bool HoldingPage =>
        _core is not { } core || !ModesAvailable(core) || Ticket.Visible || WheelOpen || FootChipVisible || _emptyCrystal is not null;

    /// <summary>The ticket chip standing in the page's foot. It is drawn on the nav bar's own row, so
    /// while it is up the page is held and the bar stands down: two things on one row is one of them
    /// unreadable and, since the bar submits later, unclickable too.</summary>
    private bool FootChipVisible =>
        !_namingOpen && !Ticket.Visible && _settle >= 1f && UnclaimedTicketSlot() is not null;

    private bool ModesAvailable(AetherlingDto core) =>
        core.Adult is not null && !_namingOpen && !RenameOverlayOpen && _arrive >= 1f && _settle >= 1f;

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
    private float FootReserved(AetherlingDto core)
    {
        if (!ModesAvailable(core))
        {
            return 0f;
        }

        var height = PetNavBar.Reserved + Px(ModesRowGap) + Px(BasketChipSize);
        if (NearestUnlock(core) is not null)
            height += Px(6f) + Px(BowlHeight);
        if (FeedWaitRemaining(core) > TimeSpan.Zero)
        {
            height += Px(4f) + Px(CountdownHeight);
        }
        return height;
    }

    /// <summary>How long until it will eat again: the wait for the next UTC day once it has had its
    /// three. Zero means it is hungry now. Measured against the server's clock through the snapshot's
    /// own stamp, so a skewed system clock cannot move it.</summary>
    private TimeSpan FeedWaitRemaining(AetherlingDto core)
    {
        if (PetState.AdultFeedsLeft(core) > 0)
        {
            return TimeSpan.Zero;
        }

        var serverNow = DateTimeOffset.UtcNow + ServerOffset(core);
        return serverNow.UtcDateTime.Date.AddDays(1) - serverNow.UtcDateTime;
    }

    /// <summary>The whole wait this countdown is a fraction of, so the track can drain rather than
    /// just sit there.</summary>
    private static TimeSpan FeedWaitTotal => TimeSpan.FromDays(1);

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
            Look.U32(Look.Body), 0.95f);
        Look.Centred(dl, FormatWait(wait), centreX, top + Px(19f), Look.U32(digitColour), 1.4f);

        // The track drains left to right: what is left of the wait is what is left of the bar.
        var left = centreX - (trackWidth * 0.5f);
        var trackY = top + Px(47f);
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

    private void DrawFoot(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherlingDto core)
    {
        var barTop = origin.Y + size.Y - PetNavBar.Reserved;
        var hasUnlock = NearestUnlock(core) is not null;
        var unlockTop = barTop - Px(ModesRowGap) - Px(BowlHeight);
        var basketTop = barTop - Px(ModesRowGap) - Px(BasketChipSize)
            - (hasUnlock ? Px(6f) + Px(BowlHeight) : 0f);
        DrawBasket(ctx, dl, origin, size, basketTop, core);
        if (hasUnlock)
            DrawNearestUnlock(ctx, dl, origin, size, unlockTop, core);

        var above = basketTop;
        var wait = FeedWaitRemaining(core);
        if (wait > TimeSpan.Zero)
        {
            above -= Px(4f) + Px(CountdownHeight);
            DrawCountdown(ctx, dl, origin, size, above, wait, FeedWaitTotal, size.X - Px(36f));
        }
    }

    private void DrawBasket(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float top, AetherlingDto core)
    {
        var chip = Px(BasketChipSize);
        var gap = Px(8f);
        var count = Elements.All.Count;
        var total = (chip * count) + (gap * (count - 1));

        // The row never runs off a narrow phone: it tightens the gap first, then the chips.
        if (total > size.X - Px(56f))
        {
            gap = MathF.Max(Px(3f), (size.X - Px(56f) - (chip * count)) / (count - 1));
            total = (chip * count) + (gap * (count - 1));
            if (total > size.X - Px(56f))
            {
                chip = (size.X - Px(56f) - (gap * (count - 1))) / count;
                total = (chip * count) + (gap * (count - 1));
            }
        }
        var left = origin.X + ((size.X - total) * 0.5f);
        BasketRect = (new Vector2(left, top), new Vector2(left + total, top + chip));

        var full = PetState.AdultFeedsLeft(core) <= 0;
        var blocked = full || _feedBusy || _flying is not null;

        var trackedElement = ShellCatalog.ElementOf(TrackedUnlockRef);
        for (var i = 0; i < count; i++)
        {
            var element = Elements.All[i];
            var owned = PetState.CrystalCount(_inventory, element);
            var tl = new Vector2(left + (i * (chip + gap)), top);

            ImGui.SetCursorScreenPos(tl);
            var pressed = ImGui.InvisibleButton($"##aetherlingCrystal{element.Key}", new Vector2(chip, chip));
            var hovered = ImGui.IsItemHovered();
            var usable = owned > 0 && !blocked;
            var canShop = owned <= 0 && _inventory is not null && !_inventoryLoading && !_feedBusy
                && !Ticket.Visible && !WheelOpen && !RenameOverlayOpen;
            if (hovered && (usable || canShop))
            {
                HandOnHover();
            }
            if (hovered)
            {
                ImGui.SetTooltip(FeedingTooltip(ctx, core) + "\n\n" + BasketTooltip(ctx, element, owned, full));
            }
            if (pressed && canShop)
                _emptyCrystal = element;
            if (ImGui.IsItemActivated() && usable && _carried is null)
            {
                _carried = element;
                _carryVelocity = Vector2.Zero;
                _lastMouse = ImGui.GetMousePos();
                pet.AnticipateCrystal(ctx.ReduceMotion, element.Key);
            }

            var alpha = usable ? 1f : 0.35f;
            dl.AddRectFilled(tl, tl + new Vector2(chip, chip),
                Look.U32(element.Accent with { W = 0.12f * alpha }), Px(12f));
            var tracked = string.Equals(trackedElement, element.Key, StringComparison.OrdinalIgnoreCase);
            dl.AddRect(tl, tl + new Vector2(chip, chip),
                Look.U32(element.Accent with { W = (tracked ? 0.90f : 0.45f) * alpha }), Px(12f),
                ImDrawFlags.RoundCornersAll, Px(tracked ? 2.2f : 1.2f));
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

    private static string BasketTooltip(OsAppContext ctx, Elements.ElementDef element, int count, bool full)
    {
        var name = ctx.Localize(Elements.NameKey(element));
        if (full)
        {
            return ctx.Localize("os.aetherling_feed_full_tip");
        }
        if (count <= 0)
        {
            return string.Format(ctx.Localize("os.aetherling_feed_none_tip"), name);
        }
        return string.Format(ctx.Localize("os.aetherling_feed_chip_tip"), name, count);
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

    private bool PetHit(Vector2 at, Vector2 stageTl, Vector2 stageSize)
    {
        var centreX = _homePetBottom.X;
        var half = _homePetSize * 0.45f;
        var top = _homePetBottom.Y - _homePetSize;
        var bottom = _homePetBottom.Y + Px(8f);
        return at.X >= centreX - half && at.X <= centreX + half && at.Y >= top && at.Y <= bottom;
    }

    private Vector2 MouthScreenPoint(Vector2 stageTl, Vector2 stageSize) =>
        _homePetBottom - new Vector2(0, _homePetSize * 0.25f);

    private void LandCrystal(OsAppContext ctx, Elements.ElementDef element)
    {
        _flying = null;
        if (_core is null || _feedBusy)
        {
            return;
        }

        // The chew is optimistic; the counts stay server-truthful. A raced refusal shows its
        // line after the crunch, which is the accepted cost of a mouth that answers instantly.
        pet.PlayFeedLand(element.Accent, ctx.ReduceMotion, element.Key);
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

    /// <summary>Takes what the feed round trip left: the new snapshot, a ticket if the diet earned one,
    /// or a warm refusal.</summary>
    private void DrainFeeding(OsAppContext ctx, float dt)
    {
        if (Interlocked.Exchange(ref _pendingFed, null) is { } fed)
        {
            _feedBusy = false;
            var before = _core;
            AdoptCore(fed);
            RefreshInventory();
            _postGameHint = false;
            _nextAppetiteCueAt = ImGui.GetTime() + AppetiteCueGapSeconds;

            if (before is not null && PetState.AdultFeedsLeft(before) > 0 && PetState.AdultFeedsLeft(fed) == 0)
            {
                pet.PlayFullMeal(Random.Shared.Next(3), ctx.ReduceMotion);
            }

            if (NewTicketSlot(before, fed) is { } ticket)
            {
                Ticket.Open(fed, ticket);
            }
            else
            {
                ShowFeedToast(ctx);
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

    private string? _feedToast;
    private float _feedToastLeft;

    private void ShowFeedToast(OsAppContext ctx)
    {
        _feedToast = ctx.Localize("os.aetherling_feed_treat_toast");
        _feedToastLeft = 3.5f;
    }

    private void TickAppetite(OsAppContext ctx, AetherlingDto core)
    {
        if (_inventory is null || BasketEmpty() || PetState.AdultFeedsLeft(core) <= 0 || pet.Napping
            || _feedBusy || _carried is not null || _flying is not null || Ticket.Visible || WheelOpen)
        {
            return;
        }
        var now = ImGui.GetTime();
        if (now < _nextAppetiteCueAt)
        {
            return;
        }
        _nextAppetiteCueAt = now + AppetiteCueGapSeconds;
        pet.PlayHungerCue(ctx.ReduceMotion);
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

    /// <summary>Opens the page ready to feed, for the store's "use them" button. The basket is
    /// always out now, so this only drops anything still in hand and refreshes what is in it.</summary>
    public void OpenFeeding()
    {
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

        // A hand with a crystal in it is aiming, not stroking.
        if (_carried is not null || _flying is not null || InputHeld)
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
