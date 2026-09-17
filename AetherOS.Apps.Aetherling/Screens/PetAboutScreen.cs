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

/// <summary>What there is to know about it: the facts, and once it is grown the radar of everything it
/// has ever eaten and the next unlock on every element. This page reports; the switches live in
/// settings.</summary>
internal sealed class PetAboutScreen(IAetherlingHost host, PetRuntime pet)
{
    private float _reveal;
    private double _lastFrameTime;
    private IReadOnlyList<StoreInventoryItemDto>? _inventory;
    private IReadOnlyList<StoreInventoryItemDto>? _pendingInventory;
    private bool _inventoryLoading;

    public event Action<AetherlingElement>? UnlocksRequested;

    public void OnShow()
    {
        _reveal = 0f;
        _lastFrameTime = ImGui.GetTime();
        RefreshInventory();
    }

    public void Draw(OsAppContext ctx, AetherlingDto core)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var now = ImGui.GetTime();
        var dt = Math.Clamp((float)(now - _lastFrameTime), 0f, 0.25f);
        _lastFrameTime = now;
        Look.Backdrop(dl, ctx.Theme, origin, size);

        var name = core.PetName ?? AetherlingLimits.DefaultName;
        var bodyTop = PetPageUi.Header(ctx, dl, origin,
            string.Format(ctx.Localize("os.aetherling_menu_about"), name));

        // The body is taller than the phone once a grown pet has a radar and six food rows under it, and it
        // is drawn to the list rather than stacked, so it needs a child of its own to scroll inside. The
        // header stays out on the parent, which is what keeps the way back pinned while the rest moves.
        ImGui.SetCursorScreenPos(new Vector2(origin.X, bodyTop));
        var body = ImGui.BeginChild("##aetherlingAbout"u8,
            new Vector2(size.X, origin.Y + size.Y - bodyTop - PetNavBar.Reserved), false,
            ImGuiWindowFlags.NoBackground);
        try
        {
            if (body)
            {
                DrawBody(ctx, ImGui.GetWindowDrawList(), origin, size, core, dt);
            }
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    /// <summary>Where the radar was last drawn, for the tour's ring.</summary>
    public (Vector2 TL, Vector2 BR)? RadarRect { get; private set; }

    /// <summary>Where the attuned-element row was last drawn, for the tour's ring.</summary>
    public (Vector2 TL, Vector2 BR)? ElementRowRect { get; private set; }

    /// <summary>Everything under the header, laid out from the scrolled cursor so it moves as a block.</summary>
    private void DrawBody(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size,
        AetherlingDto core, float dt)
    {
        var top = ImGui.GetCursorScreenPos().Y;
        var y = top;
        ElementRowRect = null;

        var born = (core.Adult?.AdultAtUtc ?? core.HatchedAtUtc ?? core.CreatedAtUtc)
            .ToLocalTime().ToString("d MMM yyyy");
        y += PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Egg,
            ctx.Localize("os.aetherling_status_born"), born);
        y += PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Heart,
            ctx.Localize("os.aetherling_status_mood"), ctx.Localize($"os.aetherling_feel_{(int)pet.Mood}"));

        if (core.Adult is { } adult)
        {
            // The attuned element leads, because it is the one in play; the born one is only named when a
            // form has moved it, where "it hatched as this" is the thing that would otherwise be lost.
            var attuned = Elements.Find(PetState.AttunedElement(core));
            var elementRowTop = y;
            y += PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Bolt,
                ctx.Localize("os.aetherling_status_element"),
                attuned is { } def ? ctx.Localize(Elements.NameKey(def)) : "");
            ElementRowRect = (new Vector2(origin.X + Px(18f), elementRowTop),
                new Vector2(origin.X + size.X - Px(18f), y - Px(8f)));
            if (Elements.Find(adult.Element) is { } hatchedAs && hatchedAs.Value != attuned?.Value)
            {
                y += PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Dna,
                    ctx.Localize("os.aetherling_status_hatched_as"), ctx.Localize(Elements.NameKey(hatchedAs)));
            }

            // The radar: the one place the diet is numbers, and it grows for life.
            _reveal = ctx.ReduceMotion ? 1f : MathF.Min(1f, _reveal + (dt / 0.6f));
            var counts = new int[Elements.All.Count];
            for (var i = 0; i < Elements.All.Count; i++)
            {
                counts[i] = PetState.DietCount(core, Elements.All[i]);
            }
            var radius = MathF.Min(size.X * 0.28f, Px(96f));
            var centre = new Vector2(origin.X + (size.X * 0.5f), y + radius + Px(34f));
            RadarRect = (centre - new Vector2(radius + Px(10f)), centre + new Vector2(radius + Px(10f)));
            RadarChart.Draw(ctx, dl, centre, radius, counts,
                Math.Max(1, adult.ShellFeedThreshold2 > 0 ? adult.ShellFeedThreshold2 : adult.DietTurnThreshold),
                _reveal * _reveal * (3f - (2f * _reveal)));

            y = DrawFoodHistory(ctx, dl, origin, size, core, adult, centre.Y + radius + Px(20f), counts);
        }

        // Nothing above submitted an item, so the child has no idea how far it reaches: hand it the height
        // as one dummy, or there is nothing to scroll.
        ImGui.SetCursorScreenPos(new Vector2(origin.X, top));
        ImGui.Dummy(new Vector2(1f, y - top + Px(12f)));
    }

    /// <summary>The next reward on each element's ladder. A row leads to the complete set of mystery
    /// unlocks without disclosing a form before its ticket is revealed.</summary>
    private float DrawFoodHistory(
        OsAppContext ctx,
        ImDrawListPtr dl,
        Vector2 origin,
        Vector2 size,
        AetherlingDto core,
        AetherlingAdultDto adult,
        float top,
        int[] counts)
    {
        DrainInventory();
        var owned = PetState.OwnedRefs(_inventory, StoreItemKind.AetherlingReaction);
        var ownedShells = PetState.OwnedRefs(_inventory, StoreItemKind.AetherlingShell);
        var pad = Px(18f);
        var rowH = Px(76f);
        var y = top;

        var headingH = Px(38f);
        var headingTl = new Vector2(origin.X + pad, y);
        var headingW = size.X - (pad * 2f);
        ImGui.SetCursorScreenPos(headingTl);
        if (ImGui.InvisibleButton("##aetherlingUnlocksHeading", new Vector2(headingW, headingH)))
        {
            UnlocksRequested?.Invoke(AetherlingElement.None);
        }
        var headingHovered = ImGui.IsItemHovered();
        if (headingHovered)
        {
            HandOnHover();
        }
        dl.AddRectFilled(headingTl, headingTl + new Vector2(headingW, headingH),
            Look.U32(Look.Crystal with { W = headingHovered ? 0.13f : 0.06f }), Px(11f));
        dl.AddText(new Vector2(headingTl.X + Px(12f), y + Px(9f)), Look.U32(Look.CrystalPale, 0.94f),
            ctx.Localize("os.aetherling_unlocks"));
        IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(11f),
            new Vector2(headingTl.X + headingW - Px(18f), y + (headingH * 0.5f)), Look.U32(Look.Whisper, 0.8f));
        y += headingH + Px(8f);

        for (var i = 0; i < Elements.All.Count; i++)
        {
            var element = Elements.All[i];
            var count = counts[i];
            var rowTl = new Vector2(origin.X + pad, y);
            var rowW = size.X - (pad * 2f);
            ImGui.SetCursorScreenPos(rowTl);
            if (ImGui.InvisibleButton($"##aetherlingUnlockRow{element.Key}", new Vector2(rowW, rowH)))
            {
                UnlocksRequested?.Invoke(element.Value);
            }
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
            }
            dl.AddRectFilled(rowTl, rowTl + new Vector2(rowW, rowH),
                Look.U32(element.Accent with { W = hovered ? 0.14f : 0.07f }), Px(13f));

            var threshold = Math.Max(1, adult.DietTurnThreshold);
            var earned = owned.Contains(ReactionDef.FindSignature(element.Key)?.ItemRef ?? "");
            string? shellRef = null;
            if (earned && adult.ShellFeedThreshold > 0
                && !ownedShells.Contains(ShellCatalog.FirstFor(element.Key)))
            {
                threshold = Math.Max(1, adult.ShellFeedThreshold);
                earned = false;
                shellRef = ShellCatalog.FirstFor(element.Key);
            }
            else if (earned && adult.ShellFeedThreshold2 > 0
                && !ownedShells.Contains(ShellCatalog.SecondFor(element.Key)))
            {
                threshold = Math.Max(1, adult.ShellFeedThreshold2);
                earned = false;
                shellRef = ShellCatalog.SecondFor(element.Key);
            }
            var waiting = TicketWaiting(core, element.Value);

            var artSide = Px(50f);
            var artTl = rowTl + new Vector2(Px(10f), (rowH - artSide) * 0.5f);
            if (shellRef is { Length: > 0 })
            {
                dl.AddRectFilled(artTl, artTl + new Vector2(artSide),
                    Look.U32(new Vector4(0.91f, 0.93f, 0.95f, 1f)), Px(10f));
                ShellPreview.PaintSilhouette(dl, shellRef, artTl + new Vector2(artSide * 0.5f),
                    artSide * 0.82f, 0xFF050505u);
            }
            else
            {
                DrawCrystal(ctx, dl, element, artTl + new Vector2(artSide * 0.5f), Px(30f));
            }

            var textX = artTl.X + artSide + Px(12f);
            dl.AddText(new Vector2(textX, y + Px(12f)), Look.U32(Look.Body, 0.94f),
                ctx.Localize(Elements.NameKey(element)));
            var status = earned
                ? ctx.Localize("os.aetherling_unlock_earned")
                : waiting ? ctx.Localize("os.aetherling_unlock_ready")
                : shellRef is { Length: > 0 }
                    ? string.Format(ctx.Localize("os.aetherling_unlock_more_form"), Math.Max(0, threshold - count))
                    : string.Format(ctx.Localize("os.aetherling_unlock_more_reaction"),
                        Math.Max(0, threshold - count), ctx.Localize(Elements.NameKey(element)));
            var statusScale = 0.78f;
            var statusRoom = rowTl.X + rowW - Px(30f) - textX;
            if (ImGui.CalcTextSize(status).X * statusScale > statusRoom)
            {
                statusScale = MathF.Max(0.62f, statusRoom / ImGui.CalcTextSize(status).X);
            }
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * statusScale, new Vector2(textX, y + Px(37f)),
                Look.U32(Look.Whisper, 0.86f), status);

            var right = rowTl.X + rowW - Px(30f);
            if (!earned && !waiting)
            {
                var label = $"{Math.Min(count, threshold)}/{threshold}";
                var labelW = ImGui.CalcTextSize(label).X;
                dl.AddText(new Vector2(right - labelW, y + Px(12f)), Look.U32(Look.Whisper, 0.85f), label);
                var trackW = MathF.Min(Px(82f), right - textX);
                var trackX = textX;
                var trackY = y + rowH - Px(13f);
                dl.AddRectFilled(new Vector2(trackX, trackY - Px(2f)), new Vector2(trackX + trackW, trackY + Px(2f)),
                    Look.U32(Look.Whisper, 0.25f), Px(2f));
                var fill = trackW * Math.Clamp(count / (float)threshold, 0f, 1f);
                if (fill > 0f)
                {
                    dl.AddRectFilled(new Vector2(trackX, trackY - Px(2f)), new Vector2(trackX + fill, trackY + Px(2f)),
                        Look.U32(element.Accent, 0.9f), Px(2f));
                }
            }
            IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(10f),
                new Vector2(rowTl.X + rowW - Px(15f), y + (rowH * 0.5f)), Look.U32(Look.Whisper, 0.72f));
            y += rowH + Px(7f);
        }
        return y;
    }

    private static bool TicketWaiting(AetherlingDto core, AetherlingElement element)
    {
        foreach (var card in core.Cards ?? [])
        {
            var mine = card.Slot == ReactionTicketOverlay.SlotBase + (short)element
                || card.Slot == ReactionTicketOverlay.ShellSlotBase + (short)element
                || card.Slot == ReactionTicketOverlay.ShellSlot2Base + (short)element;
            if (mine && card.RevealedAtUtc is null)
            {
                return true;
            }
        }
        return false;
    }

    private static void DrawChip(ImDrawListPtr dl, float right, float centreY, string label, Vector4 accent)
    {
        var textW = ImGui.CalcTextSize(label).X;
        var w = textW + Px(18f);
        var h = Px(20f);
        var tl = new Vector2(right - w, centreY - (h * 0.5f));
        dl.AddRectFilled(tl, tl + new Vector2(w, h), Look.U32(accent, 0.22f), h * 0.5f);
        dl.AddText(new Vector2(tl.X + Px(9f), tl.Y + Px(2f)), Look.U32(accent, 0.98f), label);
    }

    private static void DrawCrystal(
        OsAppContext ctx, ImDrawListPtr dl, Elements.ElementDef element, Vector2 centre, float size)
    {
        if (CoreAssets.CrystalPath(element.Key) is { } path
            && ctx.Capabilities.Textures.Get(path) is { } texture)
        {
            var half = size * 0.5f;
            dl.AddImage(texture, centre - new Vector2(half, half), centre + new Vector2(half, half),
                Vector2.Zero, Vector2.One, Look.U32(new Vector4(1f, 1f, 1f, 1f)));
            return;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Gem, size * 0.55f, centre, Look.U32(element.Accent, 0.95f));
    }

    private void DrainInventory()
    {
        if (Interlocked.Exchange(ref _pendingInventory, null) is { } items)
        {
            _inventory = items;
            pet.SetOwnedReactions(PetState.OwnedRefs(items, StoreItemKind.AetherlingReaction));
        }
    }

    private void RefreshInventory()
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
}
