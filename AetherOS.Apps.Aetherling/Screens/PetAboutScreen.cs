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

/// <summary>What there is to know about it: the tip, the facts, and once it is grown the radar of
/// everything it has ever eaten. This page reports; the switches live in settings.</summary>
internal sealed class PetAboutScreen(IAetherlingHost host, PetRuntime pet)
{
    private float _reveal;
    private double _lastFrameTime;
    private IReadOnlyList<StoreInventoryItemDto>? _inventory;
    private IReadOnlyList<StoreInventoryItemDto>? _pendingInventory;
    private bool _inventoryLoading;

    public void OnShow()
    {
        _reveal = 0f;
        _lastFrameTime = ImGui.GetTime();
        RefreshInventory();
    }

    public void Draw(OsAppContext ctx, AetherlingDto core, Action onBack)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var now = ImGui.GetTime();
        var dt = Math.Clamp((float)(now - _lastFrameTime), 0f, 0.25f);
        _lastFrameTime = now;
        Look.Backdrop(dl, ctx.Theme, origin, size);

        var name = core.PetName ?? AetherlingLimits.DefaultName;
        var bodyTop = PetPageUi.Header(ctx, dl, origin, name,
            string.Format(ctx.Localize("os.aetherling_menu_about"), name), onBack);

        // The body is taller than the phone once a grown pet has a radar and six food rows under it, and it
        // is drawn to the list rather than stacked, so it needs a child of its own to scroll inside. The
        // header stays out on the parent, which is what keeps the way back pinned while the rest moves.
        ImGui.SetCursorScreenPos(new Vector2(origin.X, bodyTop));
        var body = ImGui.BeginChild("##aetherlingAbout"u8,
            new Vector2(size.X, origin.Y + size.Y - bodyTop), false, ImGuiWindowFlags.NoBackground);
        try
        {
            if (body)
            {
                DrawBody(ctx, ImGui.GetWindowDrawList(), origin, size, core, name, now, dt);
            }
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    /// <summary>Everything under the header, laid out from the scrolled cursor so it moves as a block.</summary>
    private void DrawBody(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size,
        AetherlingDto core, string name, double now, float dt)
    {
        var top = ImGui.GetCursorScreenPos().Y;
        var y = top;

        var tip = core.Adult is null
            ? string.Format(ctx.Localize("os.aetherling_about_growing_tip"), name)
            : string.Format(ctx.Localize("os.aetherling_about_adult_tip"), name);
        y += PetPageUi.TipCard(ctx, dl, origin, size, y, tip, now);

        var born = (core.HatchedAtUtc ?? core.CreatedAtUtc).ToLocalTime().ToString("d MMM yyyy");
        y += PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Egg,
            ctx.Localize("os.aetherling_status_born"), born);
        y += PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Heart,
            ctx.Localize("os.aetherling_status_mood"), ctx.Localize($"os.aetherling_feel_{(int)pet.Mood}"));

        if (core.Adult is { } adult)
        {
            var element = Elements.Find(adult.Element);
            y += PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Bolt,
                ctx.Localize("os.aetherling_status_element"),
                element is { } def ? ctx.Localize(Elements.NameKey(def)) : "");

            // The radar: the one place the diet is numbers, and it grows for life.
            _reveal = ctx.ReduceMotion ? 1f : MathF.Min(1f, _reveal + (dt / 0.6f));
            var counts = new int[Elements.All.Count];
            for (var i = 0; i < Elements.All.Count; i++)
            {
                counts[i] = PetState.DietCount(core, Elements.All[i]);
            }
            var radius = MathF.Min(size.X * 0.28f, Px(96f));
            var centre = new Vector2(origin.X + (size.X * 0.5f), y + radius + Px(34f));
            RadarChart.Draw(ctx, dl, centre, radius, counts, Math.Max(1, adult.DietTurnThreshold),
                _reveal * _reveal * (3f - (2f * _reveal)));

            y = DrawFoodHistory(ctx, dl, origin, size, core, adult, centre.Y + radius + Px(20f), counts);
        }
        else
        {
            y += PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Seedling,
                ctx.Localize("os.aetherling_status_stage"), ctx.Localize("os.aetherling_status_growing"));
        }

        // Nothing above submitted an item, so the child has no idea how far it reaches: hand it the height
        // as one dummy, or there is nothing to scroll.
        ImGui.SetCursorScreenPos(new Vector2(origin.X, top));
        ImGui.Dummy(new Vector2(1f, y - top + Px(12f)));
    }

    /// <summary>Everything it has eaten, element by element, and what that earned. The radar says the
    /// shape of a diet; this says the numbers, and for a flourish not yet earned how far off it is.
    /// Returns the y it ended at.</summary>
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
        var threshold = Math.Max(1, adult.DietTurnThreshold);
        var pad = Px(18f);
        var rowH = Px(34f);
        var y = top;

        dl.AddText(new Vector2(origin.X + pad, y), Look.U32(Look.Whisper, 0.8f),
            ctx.Localize("os.aetherling_about_diet_title"));
        y += Px(24f);

        for (var i = 0; i < Elements.All.Count; i++)
        {
            var element = Elements.All[i];
            var count = counts[i];
            var centre = new Vector2(origin.X + pad + Px(12f), y + (rowH * 0.5f));
            DrawCrystal(ctx, dl, element, centre, Px(22f));
            dl.AddText(new Vector2(origin.X + pad + Px(32f), y + Px(9f)),
                Look.U32(Look.Body, 0.92f), ctx.Localize(Elements.NameKey(element)));

            var earned = owned.Contains(ReactionDef.FindSignature(element.Key)?.ItemRef ?? "");
            var waiting = TicketWaiting(core, element.Value);
            var right = origin.X + size.X - pad;
            if (earned)
            {
                DrawChip(dl, right, y + (rowH * 0.5f), ctx.Localize("os.aetherling_about_learned"), element.Accent);
            }
            else if (waiting)
            {
                DrawChip(dl, right, y + (rowH * 0.5f), ctx.Localize("os.aetherling_ticket_chip"), Look.Spark);
            }
            else
            {
                var label = $"{Math.Min(count, threshold)}/{threshold}";
                var labelW = ImGui.CalcTextSize(label).X;
                dl.AddText(new Vector2(right - labelW, y + Px(9f)), Look.U32(Look.Whisper, 0.85f), label);
                var trackW = Px(56f);
                var trackX = right - labelW - Px(10f) - trackW;
                var trackY = y + (rowH * 0.5f);
                dl.AddRectFilled(new Vector2(trackX, trackY - Px(2f)), new Vector2(trackX + trackW, trackY + Px(2f)),
                    Look.U32(Look.Whisper, 0.25f), Px(2f));
                var fill = trackW * Math.Clamp(count / (float)threshold, 0f, 1f);
                if (fill > 0f)
                {
                    dl.AddRectFilled(new Vector2(trackX, trackY - Px(2f)), new Vector2(trackX + fill, trackY + Px(2f)),
                        Look.U32(element.Accent, 0.9f), Px(2f));
                }
            }
            y += rowH;
        }
        return y;
    }

    private static bool TicketWaiting(AetherlingDto core, AetherlingElement element)
    {
        foreach (var card in core.Cards ?? [])
        {
            if (card.Slot == ReactionTicketOverlay.SlotBase + (short)element && card.RevealedAtUtc is null)
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
