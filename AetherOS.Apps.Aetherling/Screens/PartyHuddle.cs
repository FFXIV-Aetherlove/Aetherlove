using System;
using System.Collections.Generic;
using System.Numerics;
using AetherOS.PetKit.Engine;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The party's Aetherlings, gathered around your own out on the game screen. Each one gets its own
/// runtime, because a runtime IS a creature: one animator, one mood, one worn look. Each is drawn in its own
/// window that carries NoInputs on every frame the cursor is not on the creature (the floating pet's own
/// pattern), so a companion can be hovered for its name and booped, while the rest of the screen keeps its
/// clicks. Their mood and nap mirror the owner's own creature, so the huddle sleeps together.</summary>
internal sealed class PartyHuddle(IAetherlingHost host)
{
    /// <summary>How close they stand: the first pair keeps a clear step of air from the owner, and each
    /// further pair steps out by a little under its own width, so a full party reads as a huddle rather
    /// than a queue.</summary>
    private const float FirstGapFraction = 0.72f;
    private const float StepFraction = 0.62f;

    /// <summary>How far back a further rank stands, as a fraction of its size. A flat line of pets looks
    /// like a police lineup; a couple of pixels of stagger reads as a group.</summary>
    private const float RankLift = 0.06f;

    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoNav
        | ImGuiWindowFlags.NoDocking
        | ImGuiWindowFlags.NoSavedSettings;

    private readonly Dictionary<Guid, Companion> _companions = [];
    private readonly List<Guid> _stale = [];

    private sealed class Companion
    {
        public readonly PetRuntime Runtime = new();
        public string Folder = string.Empty;
        public string Look = string.Empty;
    }

    /// <summary>Draws whoever is in the party around the point the owner's feet stand on.
    /// <paramref name="owner"/> is the local creature the companions take their mood and nap from.</summary>
    public void Draw(Vector2 feet, float ownerSize, PetRuntime owner)
    {
        var pets = host.PartyPets;
        if (pets.Count == 0)
        {
            _companions.Clear();
            return;
        }

        var scale = FloatingPet.SizeScales[Math.Clamp(host.PartyPetSize, 0, FloatingPet.SizeScales.Length - 1)];
        var size = FloatingPet.PetSize * scale * ImGuiHelpers.GlobalScale;

        for (var i = 0; i < pets.Count; i++)
        {
            var pet = pets[i];
            var companion = Adopt(pet);
            if (!companion.Runtime.Ready)
            {
                continue;
            }
            companion.Runtime.Tick(host.ReduceMotion);
            companion.Runtime.MimicFrom(owner);

            var side = i % 2 == 0 ? -1f : 1f;
            var rank = (i / 2) + 1;
            var offset = (ownerSize * FirstGapFraction) + (size * StepFraction * (rank - 1));
            var stand = new Vector2(feet.X + (side * offset), feet.Y - (size * RankLift * (rank - 1)));
            DrawCompanion(pet, companion, stand, size);
        }

        Forget(pets);
    }

    /// <summary>One companion in its own window, sized to its worn footprint so nothing clips, input-gated
    /// to its silhouette so nothing beyond it blocks a click.</summary>
    private void DrawCompanion(AetherlingPartyPet pet, Companion companion, Vector2 feet, float size)
    {
        var margin = MathF.Max(10f, size * 0.12f);
        var footprint = companion.Runtime.AccessoryFootprint();
        var sidePad = MathF.Max(margin, (size * MathF.Max(footprint.X, footprint.Z)) + 10f);
        var headroom = margin + (size * footprint.Y);
        var footPad = MathF.Max(margin, size * footprint.W);
        var canvas = new Vector2(size + (sidePad * 2f), headroom + size + footPad);

        var hitbox = new Vector2(size * 0.78f, size * 0.95f);
        var mouse = ImGui.GetIO().MousePos;
        var overPet = mouse.X >= feet.X - (hitbox.X * 0.5f) && mouse.X <= feet.X + (hitbox.X * 0.5f)
            && mouse.Y >= feet.Y - hitbox.Y && mouse.Y <= feet.Y;
        var flags = overPet ? BaseFlags : BaseFlags | ImGuiWindowFlags.NoInputs;

        var windowTl = feet - new Vector2(canvas.X * 0.5f, headroom + size);
        ImGui.SetNextWindowPos(windowTl, ImGuiCond.Always);
        ImGui.SetNextWindowSize(canvas, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var began = ImGui.Begin($"##aetherlingCompanion{pet.AccountId:N}", flags);
        ImGui.PopStyleVar();
        if (!began)
        {
            ImGui.End();
            return;
        }

        var topLeft = ImGui.GetCursorScreenPos();
        var bottomCentre = topLeft + new Vector2(canvas.X * 0.5f, headroom + size);

        ImGui.SetCursorScreenPos(bottomCentre - new Vector2(hitbox.X * 0.5f, hitbox.Y));
        ImGui.InvisibleButton($"##companionHit{pet.AccountId:N}", hitbox);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        if (ImGui.IsItemDeactivated() && !companion.Runtime.Napping)
        {
            companion.Runtime.Boop();
            host.PlayChirp();
        }

        var dl = ImGui.GetWindowDrawList();
        companion.Runtime.Draw(dl, host.Textures, bottomCentre, size, companion.Runtime.Pose);
        if (hovered && pet.Name is { Length: > 0 } name)
        {
            DrawNameTag(ImGui.GetForegroundDrawList(), bottomCentre + new Vector2(0f, margin * 0.35f), name);
        }
        ImGui.End();
    }

    /// <summary>A small pill under the feet naming the creature, in the huddle's own quiet colours. It goes
    /// on the foreground list rather than the window's, because the companion's canvas is only as big as its
    /// worn footprint and a label hanging below the feet would be clipped away by it.</summary>
    private static void DrawNameTag(ImDrawListPtr dl, Vector2 centreTop, string name)
    {
        var textSize = ImGui.CalcTextSize(name);
        var padX = 8f * ImGuiHelpers.GlobalScale;
        var padY = 3f * ImGuiHelpers.GlobalScale;
        var half = new Vector2((textSize.X * 0.5f) + padX, (textSize.Y * 0.5f) + padY);
        var centre = centreTop + new Vector2(0f, half.Y);
        dl.AddRectFilled(centre - half, centre + half,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.1f, 0.88f)), half.Y);
        dl.AddRect(centre - half, centre + half,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.36f, 0.82f, 0.46f, 0.5f)), half.Y);
        dl.AddText(centre - (textSize * 0.5f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f)), name);
    }

    private Companion Adopt(AetherlingPartyPet pet)
    {
        if (!_companions.TryGetValue(pet.AccountId, out var companion))
        {
            companion = new Companion();
            companion.Runtime.SuppressNook = true;
            companion.Runtime.SetPhaseSeed(pet.AccountId.ToString());
            _companions[pet.AccountId] = companion;
        }

        var folder = PetState.FormFolderForStage(pet.Stage, pet.Shell);
        if (companion.Folder != folder)
        {
            companion.Folder = folder;
            companion.Runtime.EnsureLoaded(host.AssetRoot, folder);
        }

        // The draft-look path, which is the only one that takes a look without a snapshot behind it. A
        // companion has no snapshot at all: what it wears is what the party said it wears.
        var look = $"{pet.Palette}|{string.Join(',', pet.Accessories)}";
        if (companion.Look != look)
        {
            companion.Look = look;
            companion.Runtime.ApplyDraftLook(pet.Palette, [.. pet.Accessories], string.Empty, []);
        }
        return companion;
    }

    /// <summary>Drops the runtimes of anyone no longer in the list, so a party that empties out does not
    /// leave a dozen loaded creatures behind for the rest of the session.</summary>
    private void Forget(IReadOnlyList<AetherlingPartyPet> pets)
    {
        if (_companions.Count == pets.Count)
        {
            return;
        }
        _stale.Clear();
        foreach (var id in _companions.Keys)
        {
            var present = false;
            foreach (var pet in pets)
            {
                if (pet.AccountId == id)
                {
                    present = true;
                    break;
                }
            }
            if (!present)
            {
                _stale.Add(id);
            }
        }
        foreach (var id in _stale)
        {
            _companions.Remove(id);
        }
    }
}
