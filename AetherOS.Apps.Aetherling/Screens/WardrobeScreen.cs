using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>Dressing the pet: a live preview, palette swatches, the worn accessories by slot, the
/// arms with their follow-the-job switch, and the flourish row. Every choice lands on every
/// surface as it is made; the server write follows a quiet second later and validates ownership,
/// so a stale cache can never dress a pet in something it does not own.</summary>
internal sealed class WardrobeScreen(IAetherlingHost host, PetRuntime pet)
{
    private const float SaveDelaySeconds = 1f;

    private AetherlingDto? _core;
    private IReadOnlyList<StoreInventoryItemDto>? _inventory;
    private bool _inventoryLoading;
    private IReadOnlyList<StoreInventoryItemDto>? _pendingInventory;

    /// <summary>Which socket the strip has open. Head to begin with, because that is where most
    /// people's first hat goes.</summary>
    private string _slot = "head";

    private string _palette = "dawn";
    private readonly List<string> _accessories = [];
    private string _reaction = "";
    private readonly List<string> _disabled = [];
    private bool _armsFollowJob;
    private string _lastJob = "";

    private bool _dirty;
    private float _saveDue = -1f;
    private bool _saving;
    private AetherlingDto? _pendingSaved;
    private string? _pendingError;
    private string? _error;
    private float _errorLeft;

    private double _lastFrameTime;
    private float _scrollY;

    public void OnShow(AetherlingDto? core)
    {
        _core = core;
        _lastFrameTime = ImGui.GetTime();
        _error = null;
        _scrollY = 0f;
        if (core?.Look is { } look)
        {
            _palette = look.Palette;
            _accessories.Clear();
            _accessories.AddRange(look.Accessories);
            _reaction = look.Reaction;
            _disabled.Clear();
            _disabled.AddRange(look.DisabledReactions ?? []);
            _armsFollowJob = look.ArmsFollowJob;
        }
        _dirty = false;
        _saveDue = -1f;
        RefreshInventory();
        ApplyPreview();
    }

    /// <summary>Leaving the wardrobe: anything unsaved goes now, because leaving must never lose a choice,
    /// and the body goes back to the snapshot so the saved look is what the rest of the OS shows.</summary>
    public void Flush()
    {
        if (_dirty && !_saving)
        {
            Save();
        }

        // Only hand the body back when nothing is in flight. A draft that has been sent already IS what the
        // snapshot is about to say, so releasing it here would show the old look for the length of the round
        // trip and then change back.
        if (!_saving)
        {
            pet.ClearDraftLook();
        }
    }

    public void Draw(OsAppContext ctx, Action onBack)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var now = ImGui.GetTime();
        var dt = Math.Clamp((float)(now - _lastFrameTime), 0f, 0.25f);
        _lastFrameTime = now;

        DrainPending();
        TickSave(dt);
        TickFollowJob();

        Look.Backdrop(dl, ctx.Theme, origin, size);
        if (_core is not { } core)
        {
            onBack();
            return;
        }

        pet.Tick(ctx.ReduceMotion);

        var name = core.PetName ?? AetherlingLimits.DefaultName;
        var headerBottom = PetPageUi.Header(ctx, dl, origin, name,
            ctx.Localize("os.aetherling_wardrobe_title"), () =>
            {
                Flush();
                onBack();
            });

        // The live preview, fixed above the strip. It is sized against what it is WEARING: a lance and a
        // wizard's hat reach well past the creature's own square, and a size measured on a bare pet put
        // them straight through the sockets underneath.
        var previewH = MathF.Max(Px(146f), size.Y * 0.32f);
        var previewBottom = headerBottom + previewH;
        var footprint = pet.AccessoryFootprint();
        var petSize = MathF.Min(
            (previewH - Px(8f)) / (1f + footprint.Y + footprint.W),
            size.X * 0.70f / MathF.Max(1f, 1f + footprint.X + footprint.Z));
        var previewCentre = new Vector2(
            origin.X + (size.X * 0.5f), previewBottom - Px(6f) - (petSize * footprint.W));
        var pose = pet.Pose;

        // Belt and braces: whatever the footprint failed to predict is trimmed at the band rather than
        // landing on the sockets.
        dl.PushClipRect(new Vector2(origin.X, headerBottom), new Vector2(origin.X + size.X, previewBottom), true);
        Look.GroundGlow(dl, previewCentre, petSize * 0.5f, petSize * 0.11f, Look.Crystal, 0.4f);
        pet.Draw(dl, ctx.Capabilities.Textures, previewCentre, petSize, pose);
        dl.PopClipRect();

        ImGui.SetCursorScreenPos(new Vector2(previewCentre.X - (petSize * 0.42f), previewCentre.Y - petSize));
        if (ImGui.InvisibleButton("##wardrobePreview", new Vector2(petSize * 0.84f, petSize)))
        {
            pet.Boop();
            host.PlayChirp();
        }

        if (_error is { Length: > 0 } && _errorLeft > 0f)
        {
            _errorLeft -= dt;
            Look.Centred(dl, _error, origin.X + (size.X * 0.5f), previewBottom + Px(2f),
                Look.U32(new Vector4(0.95f, 0.6f, 0.55f, 0.95f)), 0.82f);
        }

        // The sockets sit outside the scroller, so what is equipped where is always on screen while
        // the list under them scrolls.
        var stripTop = previewBottom + Px(10f);
        if (core.Adult is not null)
        {
            _slot = EquipSlots.Draw(ctx, dl, new Vector2(origin.X, stripTop), size.X, _slot, WornInSlot, OwnsForSlot);
            stripTop += EquipSlots.Height;
        }

        var shelfTop = stripTop + Px(12f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, shelfTop));
        var shelf = ImGui.BeginChild("##wardrobeShelf"u8,
            new Vector2(size.X, origin.Y + size.Y - shelfTop), false, ImGuiWindowFlags.NoBackground);
        try
        {
            if (shelf)
            {
                DrawSlotContents(ctx);
                DrawPalettes(ctx);
                DrawReactions(ctx);
                ImGui.Dummy(new Vector2(1f, Px(18f)));
                _scrollY = ImGui.GetScrollY();
            }
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    // ------------------------------------------------------------------ sections

    private void DrawPalettes(OsAppContext ctx)
    {
        var owned = PetState.OwnedRefs(_inventory, StoreItemKind.AetherlingPalette);
        var catalogue = pet.Catalogue;
        if (catalogue is null)
        {
            return;
        }

        var choices = catalogue.Palettes
            .Where(p => p.ItemTier == ItemTier.Free || owned.Contains(Slug(p.Name)))
            .ToList();
        // A lone base colour is not a choice, but the section stays: the shop pill under it is the
        // whole answer for the player who has exactly one.
        SectionLabel(ctx.Localize("os.aetherling_wardrobe_palettes"));
        var dl = ImGui.GetWindowDrawList();
        var side = Px(40f);
        var gap = Px(10f);
        var pad = Px(18f);
        var perRow = Math.Max(1, (int)((ImGui.GetWindowSize().X - (pad * 2f)) / (side + gap)));
        var start = ImGui.GetCursorScreenPos() + new Vector2(pad, 0f);

        for (var i = 0; i < choices.Count; i++)
        {
            var palette = choices[i];
            var slug = Slug(palette.Name);
            var tl = start + new Vector2((i % perRow) * (side + gap), (i / perRow) * (side + gap));
            ImGui.SetCursorScreenPos(tl);
            var pressed = ImGui.InvisibleButton($"##palette{slug}", new Vector2(side, side));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
                ImGui.SetTooltip(palette.Name);
            }

            var selected = string.Equals(_palette, slug, StringComparison.OrdinalIgnoreCase);
            var centre = tl + new Vector2(side * 0.5f, side * 0.5f);
            dl.AddCircleFilled(centre, side * 0.42f, Look.U32(palette.BodyColor), 24);
            dl.AddCircleFilled(centre + new Vector2(side * 0.12f, -side * 0.12f), side * 0.16f,
                Look.U32(palette.AccentColor), 16);
            dl.AddCircleFilled(centre + new Vector2(-side * 0.1f, side * 0.08f), side * 0.08f,
                Look.U32(palette.EyeColor), 12);
            if (selected || hovered)
            {
                dl.AddCircle(centre, (side * 0.42f) + Px(3f),
                    Look.U32(Look.CrystalPale, selected ? 0.95f : 0.4f), 24, Px(selected ? 2f : 1.2f));
            }

            if (pressed && !selected)
            {
                _palette = slug;
                Touch();
            }
        }

        var rows = (choices.Count + perRow - 1) / perRow;
        ImGui.SetCursorScreenPos(start + new Vector2(-pad, rows * (side + gap)));
        DrawShopPill(ctx, "palettes");
    }

    /// <summary>Everything the player owns for the socket the strip has selected. One socket at a time,
    /// because the alternative is forty-three weapons and a hat in a single scroll.</summary>
    private void DrawSlotContents(OsAppContext ctx)
    {
        if (pet.Catalogue is null || _core?.Adult is null)
        {
            return;
        }

        var items = OwnedIn(_slot);
        SectionLabel(ctx.Localize(NameKeyFor(_slot)));

        if (_slot == AccessoryDef.ArmsSlot)
        {
            var dl = ImGui.GetWindowDrawList();
            var origin = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var y = ImGui.GetCursorScreenPos().Y;
            if (PetPageUi.Toggle(dl, origin, size, y, ctx.Localize("os.aetherling_arms_follow"), _armsFollowJob))
            {
                _armsFollowJob = !_armsFollowJob;
                _lastJob = "";
                Touch();
            }
            ImGui.SetCursorScreenPos(new Vector2(origin.X, y + Px(46f)));
        }

        if (items.Count == 0)
        {
            // The empty socket keeps its pill: this is the one place in the app where a player has
            // learned a socket exists and has nothing to put in it.
            var dl = ImGui.GetWindowDrawList();
            var origin = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var y = ImGui.GetCursorScreenPos().Y;
            var lines = Look.CentredWrapped(dl, ctx.Localize("os.aetherling_slot_empty"),
                origin.X + (size.X * 0.5f), y + Px(6f), size.X - Px(48f), Look.U32(Look.Whisper, 0.75f), 0.88f);
            ImGui.SetCursorScreenPos(new Vector2(origin.X, y + Px(10f) + (lines * Look.LineStep(0.88f))));
        }

        foreach (var (def, itemRef) in items)
        {
            DrawItemRow(ctx, def, itemRef);
        }
        DrawShopPill(ctx, ShelfFor(_slot));
    }

    /// <summary>The store shelf a socket's items live on. The accessory shelves are named for these very
    /// sockets (<c>acc-head</c>, <c>acc-glasses</c>, ...), so the pill lands on the hats rather than on the
    /// whole of Accessories; arms are a root shelf of their own.</summary>
    private static string ShelfFor(string slot) =>
        slot == AccessoryDef.ArmsSlot ? AccessoryDef.ArmsSlot : $"acc-{slot}";

    /// <summary>The way out of an owned list and into the shelf it came from. Named for the store's own
    /// category key, which is what the deep link resolves against.</summary>
    private static void DrawShopPill(OsAppContext ctx, string categoryKey)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var label = ctx.Localize("os.aetherling_shop_for_items");
        var height = Px(34f);
        var width = ImGui.CalcTextSize(label).X + Px(56f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), ImGui.GetCursorScreenPos().Y + Px(4f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton($"##shop{categoryKey}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var br = tl + new Vector2(width, height);
        dl.AddRectFilled(tl, br, Look.U32(Look.Spark with { W = hovered ? 0.28f : 0.16f }), height * 0.5f);
        dl.AddRect(tl, br, Look.U32(Look.Spark, hovered ? 0.7f : 0.35f), height * 0.5f,
            ImDrawFlags.RoundCornersAll, Px(1f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Store, Px(13f),
            new Vector2(tl.X + Px(20f), tl.Y + (height * 0.5f)), Look.U32(Look.Spark, 0.95f));
        Look.Centred(dl, label, tl.X + (width * 0.5f) + Px(8f),
            tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.CrystalPale, 0.95f));

        ImGui.SetCursorScreenPos(new Vector2(origin.X, br.Y + Px(14f)));
        if (pressed)
        {
            ctx.Shell.SendIntent("store", OsIntents.CreatePath(OsIntents.StoreOpen, categoryKey));
        }
    }

    /// <summary>What the player owns for one socket, weapons and worn things alike: the two live under
    /// different store kinds but hang on the same body.</summary>
    private List<(AccessoryDef Def, string Ref)> OwnedIn(string slot)
    {
        if (pet.Catalogue is not { } catalogue)
        {
            return [];
        }
        var kind = slot == AccessoryDef.ArmsSlot
            ? StoreItemKind.AetherlingArms
            : StoreItemKind.AetherlingAccessory;
        var owned = PetState.OwnedRefs(_inventory, kind);
        return catalogue.Accessories
            .Where(def => def.Slot == slot)
            .Select(def => (Def: def, Ref: catalogue.RefOf(def)))
            .Where(x => x.Ref is not null && owned.Contains(x.Ref))
            .OrderBy(x => x.Def.Name, StringComparer.Ordinal)
            .Select(x => (x.Def, x.Ref!))
            .ToList();
    }

    private int WornInSlot(string slot)
    {
        if (pet.Catalogue is not { } catalogue)
        {
            return 0;
        }
        var count = 0;
        foreach (var itemRef in _accessories)
        {
            if (catalogue.Accessory(itemRef) is { } def && def.Slot == slot)
            {
                count++;
            }
        }
        return count;
    }

    private bool OwnsForSlot(string slot) => OwnedIn(slot).Count > 0;

    private static string NameKeyFor(string slot)
    {
        foreach (var def in EquipSlots.All)
        {
            if (string.Equals(def.Key, slot, StringComparison.OrdinalIgnoreCase))
            {
                return def.NameKey;
            }
        }
        return "os.aetherling_wardrobe_accessories";
    }

    private void DrawReactions(OsAppContext ctx)
    {
        var owned = PetState.OwnedRefs(_inventory, StoreItemKind.AetherlingReaction);
        if (owned.Count == 0)
        {
            return;
        }

        SectionLabel(ctx.Localize("os.aetherling_wardrobe_reactions"));

        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var hintY = ImGui.GetCursorScreenPos().Y;
        var hint = string.Format(
            ctx.Localize("os.aetherling_wardrobe_reactions_hint"),
            _core?.PetName ?? AetherlingLimits.DefaultName);
        var lines = Look.CentredWrapped(dl, hint, origin.X + (size.X * 0.5f), hintY,
            size.X - Px(48f), Look.U32(Look.Whisper, 0.8f), 0.86f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, hintY + (lines * Look.LineStep(0.86f)) + Px(10f)));

        foreach (var def in ReactionDef.All)
        {
            if (owned.Contains(def.ItemRef))
            {
                DrawReactionRow(ctx, def.ItemRef, def.Name);
            }
        }
        ImGui.Dummy(new Vector2(1f, Px(8f)));
    }

    // ------------------------------------------------------------------ rows

    private void DrawItemRow(OsAppContext ctx, AccessoryDef def, string itemRef)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var pad = Px(18f);
        var height = Px(44f);
        var tl = new Vector2(origin.X + pad, ImGui.GetCursorScreenPos().Y);
        var width = size.X - (pad * 2f);

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton($"##wear{itemRef}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var worn = _accessories.Contains(itemRef, StringComparer.OrdinalIgnoreCase);
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal with { W = hovered ? 0.12f : 0.05f }), Px(10f));
        if (worn)
        {
            dl.AddRect(tl, tl + new Vector2(width, height), Look.U32(Look.Crystal, 0.6f), Px(10f),
                ImDrawFlags.RoundCornersAll, Px(1.2f));
        }

        // The real sprite as its own thumbnail, boxed to the row.
        var thumb = height - Px(10f);
        var thumbTl = tl + new Vector2(Px(6f), Px(5f));
        if (pet.Catalogue is { } catalogue
            && ctx.Capabilities.Textures.Get(catalogue.AccessoryImagePath(def)) is { } tex)
        {
            var fit = MathF.Min(thumb / Math.Max(1, def.Width), thumb / Math.Max(1, def.Height));
            var w = def.Width * fit;
            var h = def.Height * fit;
            var at = thumbTl + new Vector2((thumb - w) * 0.5f, (thumb - h) * 0.5f);
            dl.AddImage(tex, at, at + new Vector2(w, h));
        }

        dl.AddText(new Vector2(tl.X + thumb + Px(16f), tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f)),
            Look.U32(Look.CrystalPale, 0.95f), def.Name);
        if (worn)
        {
            var chip = ctx.Localize("os.aetherling_wardrobe_worn");
            var chipW = ImGui.CalcTextSize(chip).X * 0.8f;
            Look.Centred(dl, chip, tl.X + width - Px(16f) - (chipW * 0.5f),
                tl.Y + ((height - (ImGui.GetTextLineHeight() * 0.8f)) * 0.5f),
                Look.U32(Look.Crystal, 0.9f), 0.8f);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, tl.Y + height + Px(6f)));

        if (!pressed)
        {
            return;
        }
        if (worn)
        {
            _accessories.RemoveAll(a => string.Equals(a, itemRef, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // Wearing something takes off what it displaces: one arm per hand, by the anchor.
            if (pet.Catalogue is { } cat)
            {
                _accessories.RemoveAll(a => cat.Accessory(a) is { } other && def.Displaces(other));
            }
            _accessories.Add(itemRef);
            if (def.Slot == AccessoryDef.ArmsSlot)
            {
                // Picking a weapon by hand is picking it over the job's.
                _armsFollowJob = false;
            }
        }
        Touch();
    }

    /// <summary>One earned flourish, on or off. Independent toggles rather than a picker: everything
    /// switched on can come out of a boop, and switching them all off is the plain squish.</summary>
    private void DrawReactionRow(OsAppContext ctx, string itemRef, string label)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var y = ImGui.GetCursorScreenPos().Y;
        var on = !_disabled.Contains(itemRef, StringComparer.OrdinalIgnoreCase);
        if (PetPageUi.Toggle(dl, origin, size, y, label, on))
        {
            if (on)
            {
                _disabled.Add(itemRef);
            }
            else
            {
                _disabled.RemoveAll(r => string.Equals(r, itemRef, StringComparison.OrdinalIgnoreCase));
                if (ReactionDef.Find(itemRef) is { } def)
                {
                    pet.PlayReaction(def);
                }
            }
            Touch();
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X, y + Px(44f)));
        _ = ctx;
    }

    private void SectionLabel(string text)
    {
        var dl = ImGui.GetWindowDrawList();
        var at = ImGui.GetCursorScreenPos() + new Vector2(Px(18f), Px(4f));
        dl.AddText(at, Look.U32(Look.Whisper, 0.8f), text);
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, at.Y + Px(26f)));
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Arms-follow-job: on frames the page already draws, the owned halves of the
    /// player's current job replace the worn arms when the job changed. Empty is a gap in
    /// knowledge and never a change, so a loading screen cannot disarm the pet.</summary>
    private void TickFollowJob()
    {
        if (!_armsFollowJob || pet.Catalogue is null)
        {
            return;
        }
        var job = host.CurrentJobAbbreviation;
        if (job.Length == 0 || job == _lastJob)
        {
            return;
        }
        _lastJob = job;
        if (!PetCatalogue.JobArms.TryGetValue(job, out var wanted))
        {
            return;
        }

        var owned = PetState.OwnedRefs(_inventory, StoreItemKind.AetherlingArms);
        var take = wanted.Where(owned.Contains).ToList();
        if (take.Count == 0)
        {
            // Nothing owned for this job changes nothing; the pet keeps what it holds.
            return;
        }
        _accessories.RemoveAll(a => pet.Catalogue.Accessory(a)?.Slot == AccessoryDef.ArmsSlot);
        _accessories.AddRange(take);
        Touch();
        pet.PlayHopClip();
    }

    private void Touch()
    {
        ApplyPreview();
        _dirty = true;
        _saveDue = SaveDelaySeconds;
    }

    private void ApplyPreview() =>
        pet.ApplyDraftLook(_palette, _core?.Adult is not null ? _accessories : [], _reaction, _disabled);

    private void TickSave(float dt)
    {
        if (!_dirty || _saving || _saveDue < 0f)
        {
            return;
        }
        _saveDue -= dt;
        if (_saveDue <= 0f)
        {
            Save();
        }
    }

    private void Save()
    {
        _saving = true;
        _dirty = false;
        var look = new AetherlingLookDto(
            _palette, [.. _accessories], _reaction, _armsFollowJob, [.. _disabled]);
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.SetLookAsync(look).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingSaved, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }

    private void DrainPending()
    {
        if (Interlocked.Exchange(ref _pendingSaved, null) is { } saved)
        {
            _saving = false;
            _core = saved;
        }
        if (Interlocked.Exchange(ref _pendingError, null) is { } error)
        {
            // The server said no; its copy is the truth, so the draft snaps back to it.
            _saving = false;
            _error = error;
            _errorLeft = 4f;
            if (_core?.Look is { } look)
            {
                _palette = look.Palette;
                _accessories.Clear();
                _accessories.AddRange(look.Accessories);
                _reaction = look.Reaction;
                _disabled.Clear();
                _disabled.AddRange(look.DisabledReactions ?? []);
                _armsFollowJob = look.ArmsFollowJob;
                ApplyPreview();
            }
        }
        if (Interlocked.Exchange(ref _pendingInventory, null) is { } items)
        {
            _inventory = items;
            pet.SetOwnedReactions(PetState.OwnedRefs(items, StoreItemKind.AetherlingReaction));
        }
    }

    /// <summary>Re-reads what the account owns; the store is the other place it changes.</summary>
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

    private static string Slug(string name)
    {
        var chars = new List<char>(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                chars.Add(c);
            }
            else if (c is ' ' or '-' && chars.Count > 0 && chars[^1] != '-')
            {
                chars.Add('-');
            }
        }
        return new string(chars.ToArray()).TrimEnd('-');
    }
}
