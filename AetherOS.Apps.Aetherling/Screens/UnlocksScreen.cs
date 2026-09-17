using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.PetKit.Engine;
using AetherOS.PetKit.Rendering;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

internal sealed class UnlocksScreen(IAetherlingHost host, PetRuntime pet)
{
    private IReadOnlyList<StoreInventoryItemDto>? _inventory;
    private IReadOnlyList<StoreInventoryItemDto>? _pendingInventory;
    private bool _inventoryLoading;
    private AetherlingElement _focus;
    private bool _scrollToFocus;

    public string TrackedRef { get; set; } = string.Empty;

    public event Action<string>? TrackRequested;

    public event Action<string>? WearRequested;

    public event Action<short>? RevealRequested;

    public void OnShow(AetherlingElement focus = AetherlingElement.None)
    {
        _focus = focus;
        _scrollToFocus = focus != AetherlingElement.None;
        RefreshInventory();
    }

    public void Draw(OsAppContext ctx, AetherlingDto core)
    {
        DrainInventory();
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        Look.Backdrop(dl, ctx.Theme, origin, size);
        var bodyTop = PetPageUi.Header(ctx, dl, origin, ctx.Localize("os.aetherling_unlocks"));

        ImGui.SetCursorScreenPos(new Vector2(origin.X, bodyTop));
        var body = ImGui.BeginChild("##aetherlingUnlocks"u8,
            new Vector2(size.X, origin.Y + size.Y - bodyTop - PetNavBar.Reserved), false,
            ImGuiWindowFlags.NoBackground);
        try
        {
            if (body)
            {
                DrawBody(ctx, ImGui.GetWindowDrawList(), origin, size, core);
            }
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private void DrawBody(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherlingDto core)
    {
        var top = ImGui.GetCursorScreenPos().Y;
        var y = top;
        var pad = Px(18f);
        y += DrawCrystalGuide(ctx, dl, origin, size, y, core);

        var reactions = PetState.OwnedRefs(_inventory, StoreItemKind.AetherlingReaction);
        var shells = PetState.OwnedRefs(_inventory, StoreItemKind.AetherlingShell);
        foreach (var element in Elements.All)
        {
            var sectionTop = y;
            var elementName = ctx.Localize(Elements.NameKey(element));
            DrawCrystal(ctx, dl, element, new Vector2(origin.X + pad + Px(12f), y + Px(12f)), Px(22f));
            dl.AddText(new Vector2(origin.X + pad + Px(32f), y + Px(3f)),
                Look.U32(element.Accent, 0.95f), elementName);
            y += Px(30f);

            var count = PetState.DietCount(core, element);
            var adult = core.Adult!;
            var reactionRef = ReactionDef.FindSignature(element.Key)?.ItemRef ?? string.Empty;
            y += DrawReactionRow(ctx, dl, origin, size, y, core, element, count,
                Math.Max(1, adult.DietTurnThreshold), reactions.Contains(reactionRef));
            y += Px(7f);
            y += DrawFormRow(ctx, dl, origin, size, y, core, element, count, 1,
                Math.Max(1, adult.ShellFeedThreshold), ShellCatalog.FirstFor(element.Key), shells);
            y += Px(7f);
            y += DrawFormRow(ctx, dl, origin, size, y, core, element, count, 2,
                Math.Max(1, adult.ShellFeedThreshold2), ShellCatalog.SecondFor(element.Key), shells);
            y += Px(18f);

            if (_focus == element.Value)
            {
                if (_scrollToFocus)
                {
                    ImGui.SetScrollY(MathF.Max(0f, sectionTop - top));
                    _scrollToFocus = false;
                }
                dl.AddRect(new Vector2(origin.X + pad - Px(5f), sectionTop - Px(5f)),
                    new Vector2(origin.X + size.X - pad + Px(5f), y - Px(9f)),
                    Look.U32(element.Accent, 0.42f), Px(15f), ImDrawFlags.RoundCornersAll, Px(1.5f));
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, top));
        ImGui.Dummy(new Vector2(1f, y - top + Px(12f)));
    }

    private static float DrawCrystalGuide(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin,
        Vector2 size, float y, AetherlingDto core)
    {
        var tl = new Vector2(origin.X + Px(18f), y);
        var width = size.X - Px(36f);
        var textWidth = width - Px(28f);
        var title = ctx.Localize("os.aetherling_unlocks_guide_title");
        var text = string.Format(ctx.Localize("os.aetherling_unlocks_intro"),
            Math.Clamp((int)(core.Adult?.FeedsPerDay ?? 5), 1, 5));
        var titleHeight = Look.WrappedHeight(title, textWidth, 1f);
        var textHeight = Look.WrappedHeight(text, textWidth, 0.88f);
        var height = Px(78f) + titleHeight + textHeight;
        dl.AddRectFilled(tl, tl + new Vector2(width, height), 0x0CFFFFFFu, Px(14f));
        var step = textWidth / Elements.All.Count;
        for (var i = 0; i < Elements.All.Count; i++)
        {
            var element = Elements.All[i];
            var centre = tl + new Vector2(Px(14f) + step * (i + 0.5f), Px(30f));
            dl.AddRectFilled(centre + new Vector2(-step * 0.30f, Px(14f)),
                centre + new Vector2(step * 0.30f, Px(18f)), Look.U32(element.Accent, 0.18f), Px(2f));
            DrawCrystal(ctx, dl, element, centre, MathF.Min(Px(40f), step * 0.82f));
        }
        var centreX = tl.X + width * 0.5f;
        Look.CentredWrapped(dl, title, centreX, y + Px(58f), textWidth, Look.U32(Look.CrystalPale), 1f);
        Look.CentredWrapped(dl, text, centreX, y + Px(66f) + titleHeight, textWidth,
            Look.U32(Look.Body, 0.88f), 0.88f);
        return height + Px(18f);
    }

    private float DrawReactionRow(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size,
        float y, AetherlingDto core, Elements.ElementDef element, int count, int threshold, bool earned)
    {
        var waiting = TicketWaiting(core, ReactionTicketOverlay.SlotBase, element.Value);
        var waitingSlot = waiting ? (short)(ReactionTicketOverlay.SlotBase + (short)element.Value) : (short?)null;
        var label = ctx.Localize("os.aetherling_unlock_reaction");
        return DrawRewardRow(ctx, dl, origin, size, y, element, count, threshold, label, earned, waiting,
            (cardTl, cardSide) => IconDraw.AddCentered(dl, FontAwesomeIcon.Star, cardSide * 0.34f,
                cardTl + new Vector2(cardSide * 0.5f), Look.U32(element.Accent, 0.92f)),
            waiting, false, null, waitingSlot);
    }

    private float DrawFormRow(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size,
        float y, AetherlingDto core, Elements.ElementDef element, int count, int ordinal, int threshold,
        string itemRef, IReadOnlySet<string> shells)
    {
        var earned = shells.Contains(itemRef);
        var waiting = TicketWaiting(core,
            ordinal == 1 ? ReactionTicketOverlay.ShellSlotBase : ReactionTicketOverlay.ShellSlot2Base,
            element.Value);
        var label = earned
            ? ShellCatalog.Find(itemRef)?.Name ?? string.Format(ctx.Localize("os.aetherling_unlock_form"), ordinal)
            : string.Format(ctx.Localize("os.aetherling_unlock_form"), ordinal);
        var tracked = string.Equals(TrackedRef, itemRef, StringComparison.OrdinalIgnoreCase);
        var waitingSlot = waiting
            ? (short)((ordinal == 1 ? ReactionTicketOverlay.ShellSlotBase : ReactionTicketOverlay.ShellSlot2Base)
                + (short)element.Value)
            : (short?)null;
        return DrawRewardRow(ctx, dl, origin, size, y, element, count, threshold, label, earned, waiting,
            (cardTl, cardSide) =>
            {
                dl.AddRectFilled(cardTl, cardTl + new Vector2(cardSide),
                    Look.U32(new Vector4(0.91f, 0.93f, 0.95f, 1f)), cardSide * 0.20f);
                ShellPreview.PaintSilhouette(dl, itemRef, cardTl + new Vector2(cardSide * 0.5f),
                    cardSide * 0.82f, 0xFF000000u);
            }, true, tracked, itemRef, waitingSlot);
    }

    private float DrawRewardRow(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float y,
        Elements.ElementDef element, int count, int threshold, string label, bool earned, bool waiting,
        Action<Vector2, float> drawArt, bool actionable, bool tracked, string? itemRef, short? waitingSlot)
    {
        var pad = Px(18f);
        var rowH = Px(82f);
        var tl = new Vector2(origin.X + pad, y);
        var width = size.X - (pad * 2f);
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton($"##unlock{element.Key}{threshold}", new Vector2(width, rowH));
        var hovered = ImGui.IsItemHovered();
        if (hovered && actionable)
        {
            HandOnHover();
        }
        dl.AddRectFilled(tl, tl + new Vector2(width, rowH),
            Look.U32(element.Accent with { W = hovered ? 0.14f : 0.07f }), Px(14f));

        var artSide = Px(58f);
        var artTl = tl + new Vector2(Px(12f), (rowH - artSide) * 0.5f);
        drawArt(artTl, artSide);
        var textX = artTl.X + artSide + Px(13f);
        dl.AddText(new Vector2(textX, tl.Y + Px(11f)), Look.U32(Look.CrystalPale, 0.96f), label);

        var progress = $"{Math.Min(count, threshold)}/{threshold}";
        var state = earned
            ? ctx.Localize("os.aetherling_unlock_earned")
            : waiting ? ctx.Localize("os.aetherling_unlock_ready")
            : string.Format(ctx.Localize("os.aetherling_unlock_more"), Math.Max(0, threshold - count));
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.82f, new Vector2(textX, tl.Y + Px(34f)),
            Look.U32(Look.Whisper, 0.88f), state);

        var right = tl.X + width - Px(12f);
        var progressW = ImGui.CalcTextSize(progress).X;
        dl.AddText(new Vector2(right - progressW, tl.Y + Px(11f)), Look.U32(Look.Whisper, 0.84f), progress);
        var trackW = MathF.Max(Px(66f), right - textX);
        var trackY = tl.Y + rowH - Px(15f);
        dl.AddRectFilled(new Vector2(textX, trackY - Px(2.5f)), new Vector2(textX + trackW, trackY + Px(2.5f)),
            Look.U32(Look.Whisper, 0.20f), Px(3f));
        var fill = trackW * Math.Clamp(count / (float)threshold, 0f, 1f);
        if (fill > 0f)
        {
            dl.AddRectFilled(new Vector2(textX, trackY - Px(2.5f)),
                new Vector2(textX + fill, trackY + Px(2.5f)), Look.U32(element.Accent, 0.9f), Px(3f));
        }

        if (actionable)
        {
            var action = waiting ? ctx.Localize("os.aetherling_unlock_ready")
                : earned ? ctx.Localize("os.aetherling_menu_wardrobe")
                : ctx.Localize(tracked ? "os.aetherling_unlock_tracked" : "os.aetherling_unlock_track");
            var actionW = ImGui.CalcTextSize(action).X * 0.78f;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.78f,
                new Vector2(right - actionW, tl.Y + Px(35f)),
                Look.U32(tracked ? element.Accent : Look.Crystal, 0.9f), action);
            if (pressed && waitingSlot is { } slot)
            {
                RevealRequested?.Invoke(slot);
            }
            else if (pressed && itemRef is { Length: > 0 })
            {
                if (earned)
                {
                    WearRequested?.Invoke(itemRef);
                }
                else
                {
                    TrackRequested?.Invoke(itemRef);
                }
            }
        }
        return rowH;
    }

    private static bool TicketWaiting(AetherlingDto core, short slotBase, AetherlingElement element)
    {
        foreach (var card in core.Cards ?? [])
        {
            if (card.Slot == slotBase + (short)element && card.RevealedAtUtc is null)
            {
                return true;
            }
        }
        return false;
    }

    private static void DrawCrystal(OsAppContext ctx, ImDrawListPtr dl, Elements.ElementDef element,
        Vector2 centre, float side)
    {
        if (CoreAssets.CrystalPath(element.Key) is { } path && ctx.Capabilities.Textures.Get(path) is { } texture)
        {
            var half = side * 0.5f;
            dl.AddImage(texture, centre - new Vector2(half), centre + new Vector2(half));
            return;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Gem, side * 0.6f, centre, Look.U32(element.Accent));
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
