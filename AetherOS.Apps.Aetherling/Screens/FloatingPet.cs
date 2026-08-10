using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherOS.Apps.Aetherling.Engine;
using Dalamud.Bindings.ImGui;
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
    private const float PetSize = 132f;

    /// <summary>What each of the five sizes multiplies that by, a third larger each step. S is the size it has
    /// always come out at, so it is where everyone starts and what everything else is measured against.</summary>
    public static readonly float[] SizeScales = [0.78f, 1.00f, 1.32f, 1.72f, 2.24f];

    /// <summary>S, the size it comes out at until somebody says otherwise.</summary>
    public const int DefaultSizeIndex = 1;

    /// <summary>Room around it for the hop, as a fraction of its size: the arc reaches 42/256 sideways and
    /// 30/256 up, and every spare pixel past that is a stolen click.</summary>
    private const float MarginFraction = 0.20f;

    private const string MenuId = "##aetherlingFloatMenu";

    private Vector2? _position;
    private bool _dragging;
    private bool _holding;
    private bool _recentre;

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

    public bool Visible => Enabled && host.Snapshot is { HatchedAtUtc: not null };

    /// <summary>Puts it back in the middle of the screen, for anyone who has lost it off an edge.</summary>
    public void Recentre() => _recentre = true;

    public void Draw()
    {
        pet.EnsureLoaded(host.AssetRoot);
        if (!pet.Ready)
        {
            return;
        }
        pet.Tick(host.ReduceMotion);

        // The game's own UI scale, not the phone's: out here it shares a screen with the game, and a player
        // who likes a small phone did not ask for a small creature.
        var scale = SizeScales[Math.Clamp(SizeIndex, 0, SizeScales.Length - 1)];
        var size = PetSize * scale * ImGuiHelpers.GlobalScale;
        var margin = MathF.Max(10f, size * MarginFraction);
        var canvas = new Vector2(size + (margin * 2f), size + (margin * 2f));

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
        var interactive = overPet || _dragging || _holding || ImGui.IsPopupOpen(MenuId);

        var windowTl = feet - new Vector2(canvas.X * 0.5f, margin + size);
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
        var bottomCentre = topLeft + new Vector2(canvas.X * 0.5f, margin + size);

        ImGui.SetCursorScreenPos(bottomCentre - new Vector2(hitbox.X * 0.5f, hitbox.Y));
        ImGui.InvisibleButton("##aetherlingFloatingHit", hitbox);
        _holding = ImGui.IsItemActive();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            ImGui.OpenPopup(MenuId);
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
            }
            _dragging = false;
        }

        pet.Draw(ImGui.GetWindowDrawList(), host.Textures, bottomCentre, size, pet.Pose);
        ImGui.End();
    }

    /// <summary>The right-click menu. It reads its own strings rather than taking them from a frame context,
    /// because out here there is no frame context to take them from.</summary>
    private void DrawMenu()
    {
        if (!ImGui.BeginPopup(MenuId))
        {
            return;
        }
        if (ImGui.Selectable(Loc.T("os.aetherling_float_stats")))
        {
            StatusRequested?.Invoke();
        }
        if (ImGui.Selectable(Loc.T("os.aetherling_float_hide")))
        {
            HideRequested?.Invoke();
        }
        ImGui.EndPopup();
    }
}
