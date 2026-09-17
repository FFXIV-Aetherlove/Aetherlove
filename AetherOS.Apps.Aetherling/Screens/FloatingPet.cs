using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.PetKit.Engine;
using AetherOS.PetKit.Rendering;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The creature out on the game screen, in its own borderless window: click to poke, drag to move,
/// and the position is kept between sessions.
///
/// The window blocks clicks to whatever is under it, so it is only ever as big as the hop needs and the hit
/// box is only the creature itself. Anything more generous is an invisible rectangle stealing presses off
/// somebody's hotbar.</summary>
internal sealed class FloatingPet(IAetherlingHost host, PetRuntime pet) : IAetherlingOverlay
{
    /// <summary>How big it stands out on the screen at <see cref="DefaultSizeIndex"/>, in unscaled pixels.</summary>
    internal const float PetSize = 132f;

    /// <summary>What each of the five sizes multiplies that by, a third larger each step. S is the size it has
    /// always come out at, so it is where everyone starts and what everything else is measured against.</summary>
    public static readonly float[] SizeScales = [0.78f, 1.00f, 1.32f, 1.72f, 2.24f];

    /// <summary>S, the size it comes out at until somebody says otherwise.</summary>
    public const int DefaultSizeIndex = 1;

    /// <summary>Room around it for the hop, as a fraction of its size: the arc reaches 42/256 sideways and
    /// 30/256 up, and every spare pixel past that is a stolen click.</summary>
    private const float MarginFraction = 0.20f;

    private const string MenuId = "##aetherlingFloatMenu";
    private const float SnackFirstDelaySeconds = 18f;
    private const float SnackVisibleSeconds = 9f;
    private const float SnackGapSeconds = 300f;

    private readonly PartyHuddle _huddle = new(host);

    private enum MenuPage { Root, Emotes }

    private readonly List<OsMenu.MenuRow> _menuRows = [];
    private readonly List<Action> _menuActions = [];

    private MenuPage _menuPage;

    private Vector2? _position;
    private bool _dragging;
    private bool _holding;
    private bool _recentre;
    private IReadOnlyList<StoreInventoryItemDto>? _inventory;
    private IReadOnlyList<StoreInventoryItemDto>? _pendingInventory;
    private bool _inventoryLoading;
    private Elements.ElementDef? _snack;
    private double _snackUntil;
    private double _nextSnackAt;
    private bool _snackClockStarted;
    private bool _feedBusy;
    private AetherlingDto? _feedBefore;
    private AetherlingDto? _pendingFed;
    private string? _pendingFeedError;
    private string? _feedError;
    private double _feedErrorUntil;

    /// <summary>Whether the player wants it out here at all, set by the app from its stored settings.</summary>
    public bool Enabled { get; set; }

    /// <summary>Pinned in place, so a poke can never turn into a drag.</summary>
    public bool Locked { get; set; }

    /// <summary>Which of <see cref="SizeScales"/> it wears, clamped on use.</summary>
    public int SizeIndex { get; set; } = DefaultSizeIndex;

    /// <summary>Where it was left: the point its FEET stand on, not the window's corner. Anchoring the
    /// window would make every size change shove it sideways and up.</summary>
    public Vector2? Position
    {
        get => _position;
        set => _position = value;
    }

    /// <summary>Raised when the player drags it somewhere, so the app can persist the new spot.</summary>
    public event Action<Vector2>? Moved;

    /// <summary>Raised by the right-click menu's hide row: the quick way to put it away without going back
    /// into the app for the switch.</summary>
    public event Action? HideRequested;

    /// <summary>Raised by the right-click menu's statistics row.</summary>
    public event Action? StatusRequested;

    /// <summary>Raised with the new state when the menu pins or unpins it, so the app can persist the
    /// choice and the settings page shows the same answer.</summary>
    public event Action<bool>? LockToggled;

    /// <summary>Kept in for a moment: set while a ceremony or the grown-up's welcome owns the phone, so
    /// the same creature is not standing on the game screen while it is busy being celebrated inside.</summary>
    public bool Hidden { get; set; }

    public bool Visible => Enabled && !Hidden && host.Snapshot is { Adult: not null };

    /// <summary>Whether the creature speaks out over the game. The one off switch (the glyph channel is
    /// never silenced in the app, where it is the pet's voice rather than a feature); the app persists it.</summary>
    public bool WorldGlyphs { get; set; } = true;

    public string PreferredElementKey { get; set; } = string.Empty;

    /// <summary>Puts it back in the middle of the screen, for anyone who has lost it off an edge.</summary>
    public void Recentre() => _recentre = true;

    public void Draw()
    {
        DrainSnackState();
        EnsureSnackInventory();

        // The same form AND the same look the phone page asks for. Both are the runtime's, not a page's:
        // dressing it from the pet page alone meant the creature out here wore the default blue until the
        // app had been opened at least once.
        pet.EnsureLoaded(host.AssetRoot, Engine.PetState.FormFolder(host.Snapshot));
        pet.ApplyLook(host.Snapshot);
        if (!pet.Ready)
        {
            return;
        }
        pet.Tick(host.ReduceMotion);
        // A fresh recording per frame while a picture is pending, so a compositor always replays one whole
        // frame rather than the tail of one and the head of the next.
        Rendering.PetFrameRecorder.Begin();

        // The game's own UI scale, not the phone's: out here it shares a screen with the game, and a player
        // who likes a small phone did not ask for a small creature.
        var scale = SizeScales[Math.Clamp(SizeIndex, 0, SizeScales.Length - 1)];
        var size = PetSize * scale * ImGuiHelpers.GlobalScale;
        var margin = MathF.Max(10f, size * MarginFraction);
        TickSnackCue();

        // The worn look can reach past the creature's own square (a lance, a nook), so the
        // canvas folds the footprint in. Input stays silhouette-gated, so a bigger canvas
        // never blocks more of the game.
        var footprint = pet.AccessoryFootprint();
        var sidePad = MathF.Max(margin, (size * MathF.Max(footprint.X, footprint.Z)) + 10f);
        var headroom = margin + (size * footprint.Y);
        var footPad = MathF.Max(margin, size * footprint.W);
        var canvas = new Vector2(size + (sidePad * 2f), headroom + size + footPad);

        var viewport = ImGui.GetMainViewport();
        if (_recentre || _position is null)
        {
            _recentre = false;
            _position = viewport.Pos + new Vector2(viewport.Size.X * 0.5f, (viewport.Size.Y + size) * 0.5f);
            Moved?.Invoke(_position.Value);
        }

        // The canvas keeps the hop's headroom: shrinking it would clip the art and the arc, not the blocking.
        // What blocks is the WINDOW, which eats the mouse over its whole rect whatever is drawn in it, so the
        // hit box alone was never enough. Instead the window only accepts input on the frames the cursor is
        // actually on the creature; every other frame it is NoInputs and clicks fall through to the game.
        var feet = _position.Value;
        var hitbox = new Vector2(size * 0.78f, size * 0.95f);
        var mouse = ImGui.GetIO().MousePos;
        var overPet = mouse.X >= feet.X - (hitbox.X * 0.5f) && mouse.X <= feet.X + (hitbox.X * 0.5f)
            && mouse.Y >= feet.Y - hitbox.Y && mouse.Y <= feet.Y;
        var snackSide = size * 0.34f;
        var snackCentre = feet + new Vector2(size * 0.43f, -size * 0.90f);
        var snackTl = snackCentre - new Vector2(snackSide * 0.5f);
        var overSnack = _snack is not null && mouse.X >= snackTl.X && mouse.X <= snackTl.X + snackSide
            && mouse.Y >= snackTl.Y && mouse.Y <= snackTl.Y + snackSide;
        var interactive = overPet || overSnack || _dragging || _holding || ImGui.IsPopupOpen(MenuId);

        var windowTl = feet - new Vector2(canvas.X * 0.5f, headroom + size);
        ImGui.SetNextWindowPos(windowTl, ImGuiCond.Always);
        ImGui.SetNextWindowSize(canvas, ImGuiCond.Always);
        const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoSavedSettings;
        var flags = interactive ? BaseFlags : BaseFlags | ImGuiWindowFlags.NoInputs;

        // Zero padding, so the canvas the window hands back is the rect that was asked for and the drawn
        // creature lands exactly on the point its position names.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var began = ImGui.Begin("##aetherlingFloatingPet", flags);
        ImGui.PopStyleVar();
        if (!began)
        {
            ImGui.End();
            return;
        }

        var topLeft = ImGui.GetCursorScreenPos();
        var bottomCentre = topLeft + new Vector2(canvas.X * 0.5f, headroom + size);

        ImGui.SetCursorScreenPos(bottomCentre - new Vector2(hitbox.X * 0.5f, hitbox.Y));
        ImGui.InvisibleButton("##aetherlingFloatingHit", hitbox);
        _holding = ImGui.IsItemActive();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _menuPage = MenuPage.Root;
            OsMenu.Open(MenuId);
        }
        DrawMenu();

        if (ImGui.IsItemActive() && !Locked && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _dragging = true;
            _position += ImGui.GetIO().MouseDelta;
        }
        if (ImGui.IsItemDeactivated())
        {
            if (_dragging && _position is { } moved)
            {
                Moved?.Invoke(moved);
            }
            else if (!_dragging)
            {
                pet.Boop();
                host.PlayChirp();
            }
            _dragging = false;
        }

        pet.Draw(ImGui.GetWindowDrawList(), host.Textures, bottomCentre, size, pet.Pose);

        if (_snack is { } snack)
        {
            DrawSnackBubble(snack, snackTl, snackSide);
        }

        // On the FOREGROUND list, not the window's: the canvas is deliberately tight (every spare pixel
        // of margin is a stolen click on someone's hotbar), and out here a symbol needs no window
        // headroom and takes no clicks, the Kindling-flash precedent.
        if (WorldGlyphs)
        {
            pet.DrawGlyph(ImGui.GetForegroundDrawList(), bottomCentre, size, bubbleFrame: false);
        }
        ImGui.End();

        // After this window closes: each companion opens one of its own, which cannot be nested inside
        // another window's scope. They stand beside the owner rather than inside its canvas, which is
        // deliberately tight (every spare pixel of margin is a stolen click on somebody's hotbar).
        _huddle.Draw(bottomCentre, size, pet);
    }

    private void TickSnackCue()
    {
        var now = ImGui.GetTime();
        if (!_snackClockStarted)
        {
            _snackClockStarted = true;
            _nextSnackAt = now + SnackFirstDelaySeconds;
        }
        if (_snack is not null && now >= _snackUntil)
        {
            _snack = null;
            _nextSnackAt = now + SnackGapSeconds;
        }
        if (_snack is not null || now < _nextSnackAt || !SnackEligible())
        {
            return;
        }
        _snack = PickSnack();
        if (_snack is null)
        {
            return;
        }
        _snackUntil = now + SnackVisibleSeconds;
        pet.AnticipateCrystal(host.ReduceMotion, _snack.Value.Key);
    }

    private bool SnackEligible() =>
        !_feedBusy && !pet.Napping && host.Snapshot is { Adult: not null } core
        && PetState.AdultFeedsLeft(core) > 0 && _inventory is not null && PickSnack() is not null;

    private Elements.ElementDef? PickSnack()
    {
        if (PreferredElementKey.Length > 0)
        {
            foreach (var element in Elements.All)
            {
                if (string.Equals(element.Key, PreferredElementKey, StringComparison.OrdinalIgnoreCase)
                    && PetState.CrystalCount(_inventory, element) > 0)
                {
                    return element;
                }
            }
        }
        foreach (var element in Elements.All)
        {
            if (PetState.CrystalCount(_inventory, element) > 0)
            {
                return element;
            }
        }
        return null;
    }

    private void DrawSnackBubble(Elements.ElementDef element, Vector2 tl, float side)
    {
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingFloatingSnack", new Vector2(side));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var name = Loc.T(Elements.NameKey(element));
            ImGui.SetTooltip(_feedError is { Length: > 0 } && ImGui.GetTime() < _feedErrorUntil
                ? _feedError
                : Loc.T("os.aetherling_float_feed_tip", name));
        }

        var dl = ImGui.GetWindowDrawList();
        var centre = tl + new Vector2(side * 0.5f);
        dl.AddCircleFilled(centre, side * 0.48f, 0xE8F2F5F7u, 24);
        dl.AddCircle(centre, side * 0.48f, ImGui.ColorConvertFloat4ToU32(element.Accent with { W = 0.78f }),
            24, MathF.Max(1f, side * 0.035f));
        DrawSnackCrystal(dl, element, centre, side * 0.62f);

        if (pressed && !_feedBusy)
        {
            StartSnackFeed(element);
        }
    }

    private void DrawSnackCrystal(ImDrawListPtr dl, Elements.ElementDef element, Vector2 centre, float side)
    {
        if (CoreAssets.CrystalPath(element.Key) is { } path && host.Textures.Get(path) is { } texture)
        {
            var half = side * 0.5f;
            dl.AddImage(texture, centre - new Vector2(half), centre + new Vector2(half), Vector2.Zero, Vector2.One,
                0xFFFFFFFFu);
            return;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Gem, side * 0.62f, centre,
            ImGui.ColorConvertFloat4ToU32(element.Accent));
    }

    private void StartSnackFeed(Elements.ElementDef element)
    {
        if (host.Snapshot is not { } core || PetState.AdultFeedsLeft(core) <= 0
            || PetState.CrystalCount(_inventory, element) <= 0)
        {
            _snack = null;
            EnsureSnackInventory(force: true);
            return;
        }
        _feedBusy = true;
        _feedBefore = core;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.FeedAsync((short)element.Value).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingFed, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingFeedError, host.DescribeError(ex));
            }
        });
    }

    private void DrainSnackState()
    {
        if (Interlocked.Exchange(ref _pendingInventory, null) is { } items)
        {
            _inventory = items;
        }
        if (Interlocked.Exchange(ref _pendingFed, null) is { } fed)
        {
            _feedBusy = false;
            var element = _snack;
            _snack = null;
            _nextSnackAt = ImGui.GetTime() + SnackGapSeconds;
            if (element is { } eaten)
            {
                pet.PlayFeedLand(eaten.Accent, host.ReduceMotion, eaten.Key);
            }
            if (_feedBefore is not null && PetState.AdultFeedsLeft(_feedBefore) > 0
                && PetState.AdultFeedsLeft(fed) == 0)
            {
                pet.PlayFullMeal(Random.Shared.Next(3), host.ReduceMotion);
            }
            _feedBefore = null;
            EnsureSnackInventory(force: true);
        }
        if (Interlocked.Exchange(ref _pendingFeedError, null) is { } error)
        {
            _feedBusy = false;
            _feedBefore = null;
            _feedError = error;
            _feedErrorUntil = ImGui.GetTime() + 4f;
            pet.PlayRefusal(host.ReduceMotion);
            EnsureSnackInventory(force: true);
        }
    }

    private void EnsureSnackInventory(bool force = false)
    {
        if (_inventoryLoading || (!force && _inventory is not null))
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

    /// <summary>The right-click menu, on the phone's own card rather than ImGui's grey box. It reads its own
    /// strings rather than taking them from a frame context, because out here there is no frame context to
    /// take them from.
    ///
    /// <para>The emote list is a page of the same menu rather than a flyout: out here the menu hangs in the
    /// middle of the game screen with no page to hang a second panel off, and a list this long wants the
    /// whole card anyway.</para></summary>
    private void DrawMenu()
    {
        _menuRows.Clear();
        _menuActions.Clear();
        switch (_menuPage)
        {
            case MenuPage.Emotes:
                BuildEmotePage();
                break;
            default:
                BuildRootPage();
                break;
        }

        var picked = OsMenu.Draw(MenuId, _menuRows);
        if (picked >= 0)
        {
            _menuActions[picked]();
        }
    }

    private void Add(OsMenu.MenuRow row, Action act)
    {
        _menuRows.Add(row);
        _menuActions.Add(act);
    }

    /// <summary>Steps to another page of the same card, replaying the opening so the new rows arrive the
    /// way the menu did rather than swapping under the cursor.</summary>
    private void GoTo(MenuPage page)
    {
        _menuPage = page;
        OsMenu.Restart(MenuId);
    }

    private void BuildRootPage()
    {
        Add(new OsMenu.MenuRow(FontAwesomeIcon.ChartLine, Loc.T("os.aetherling_float_stats")),
            () => StatusRequested?.Invoke());
        if (LearnedEmotes().Count > 0)
        {
            Add(new OsMenu.MenuRow(FontAwesomeIcon.Star, Loc.T("os.aetherling_float_emotes"), true, true),
                () => GoTo(MenuPage.Emotes));
        }
        Add(new OsMenu.MenuRow(Locked ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock,
                Loc.T(Locked ? "os.aetherling_float_unlock" : "os.aetherling_float_lock")),
            () =>
            {
                Locked = !Locked;
                LockToggled?.Invoke(Locked);
            });
        Add(new OsMenu.MenuRow(FontAwesomeIcon.EyeSlash, Loc.T("os.aetherling_float_hide")),
            () => HideRequested?.Invoke());
    }

    private void BuildEmotePage()
    {
        AddBackRow();
        foreach (var def in LearnedEmotes())
        {
            var emote = def;
            Add(new OsMenu.MenuRow(FontAwesomeIcon.Star, emote.Name), () =>
            {
                // A play the runtime refuses (napping, mid-emote, reduce motion) is refused silently,
                // exactly as everywhere else.
                pet.AuditionGlyph("burst");
                pet.PlayEmote(emote);
                _menuPage = MenuPage.Root;
            });
        }
    }

    private void AddBackRow()
    {
        Add(new OsMenu.MenuRow(FontAwesomeIcon.ChevronLeft, Loc.T("common.back"), false, true),
            () => GoTo(MenuPage.Root));
    }

    /// <summary>What it has actually learned, in library order. The emote page is absent until there is a
    /// first one, so an empty pet's menu never carries a door to an empty room.</summary>
    private List<EmoteDef> LearnedEmotes()
    {
        var learned = new List<EmoteDef>();
        if (host.Snapshot?.Emotes is not { } emotes)
        {
            return learned;
        }
        foreach (var def in EmoteChoreographies.All)
        {
            foreach (var progress in emotes.Emotes)
            {
                if (progress.Key == def.Key && progress.LearnedAtUtc is not null)
                {
                    learned.Add(def);
                    break;
                }
            }
        }
        return learned;
    }
}
