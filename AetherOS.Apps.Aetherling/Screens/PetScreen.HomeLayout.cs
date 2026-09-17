using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.PetKit.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

internal sealed partial class PetScreen
{
    private static (float Width, float CentreAboveBase) HomeFormFraming(string form) => form switch
    {
        "1" => (0.60f, 0.45f),
        "2" => (0.58f, 0.45f),
        "3" => (0.56f, 0.45f),
        "4" => (0.54f, 0.43f),
        "jellyv1" => (0.45f, 0.53f),
        "pufferv1" => (0.58f, 0.45f),
        "crabv1" => (0.28f, 0.49f),
        "serpentv1" => (0.62f, 0.43f),
        "nautilusv1" => (0.56f, 0.48f),
        "mothv1" => (0.68f, 0.45f),
        "spintopv1" => (0.55f, 0.40f),
        "lanternv1" => (0.60f, 0.47f),
        "smoulderv1" => (0.46f, 0.44f),
        "pennantv1" => (0.60f, 0.60f),
        "grumblev1" => (0.52f, 0.48f),
        "mufflev1" => (0.52f, 0.53f),
        "chimev1" => (0.60f, 0.53f),
        _ => (0.90f, 0.45f),
    };

    private Vector2 _homePetBottom;
    private float _homePetSize;
    private AetherlingDto? _nearestCore;
    private IReadOnlyList<StoreInventoryItemDto>? _nearestInventory;
    private (Elements.ElementDef Element, string Ref)? _nearestReward;

    private float DrawHomeHeader(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float width, AetherlingDto core)
    {
        var name = core.PetName ?? AetherlingLimits.DefaultName;
        var centre = tl.X + width * 0.5f;
        var nameY = tl.Y + Px(16f);
        float titleHeight;
        using (ctx.TitleFont?.Push())
        {
            var nameWidth = MathF.Max(Px(40f), width - Px(160f));
            var measured = ImGui.CalcTextSize(name, false, nameWidth);
            titleHeight = measured.Y;
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(centre - measured.X * 0.5f, nameY),
                Look.U32(Look.CrystalPale), name, nameWidth);
            if (core.NameChosen && ModesAvailable(core))
            {
                DrawRenamePill(ctx, dl, new Vector2(centre + measured.X * 0.5f + Px(6f), nameY), measured.Y);
            }
        }
        if (WheelButtonVisible(core))
        {
            DrawWheelButton(ctx, dl, tl + new Vector2(Px(14f), Px(16f)), core, ImGui.GetTime());
        }

        var status = HomeStatus(ctx, core, name);
        var statusWidth = width - Px(36f);
        var statusY = nameY + titleHeight + Px(10f);
        var fontSize = ImGui.GetFontSize() * 0.86f;
        var measuredStatus = ImGui.CalcTextSize(status, false, statusWidth / 0.86f) * 0.86f;
        dl.AddText(ImGui.GetFont(), fontSize,
            new Vector2(centre - measuredStatus.X * 0.5f, statusY),
            Look.U32(Look.Body, 0.75f), status, statusWidth);
        return MathF.Max(Px(82f), statusY + measuredStatus.Y + Px(14f) - tl.Y);
    }

    private string HomeStatus(OsAppContext ctx, AetherlingDto core, string name)
    {
        if (_feedToastLeft > 0f && !string.IsNullOrEmpty(_feedToast))
            return _feedToast;
        var left = PetState.AdultFeedsLeft(core);
        if (_postGameHint && left > 0 && !BasketEmpty())
            return string.Format(ctx.Localize("os.aetherling_after_game_hungry"), name);
        if (left <= 0)
            return string.Format(ctx.Localize("os.aetherling_home_full"), name);
        if (pet.Mood <= MoodLevel.Dozy)
            return string.Format(ctx.Localize("os.aetherling_feeling"), name,
                ctx.Localize($"os.aetherling_feel_{(int)pet.Mood}"));
        if (pet.Mood >= MoodLevel.Bright)
            return string.Format(ctx.Localize("os.aetherling_home_happy"), name);
        return string.Format(ctx.Localize("os.aetherling_home_hungry"), name);
    }

    private void DrawFoodSlots(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size, AetherlingDto core)
    {
        if (core.Adult is not { } adult)
        {
            return;
        }
        var slots = Math.Clamp((int)adult.FeedsPerDay, 1, AetherlingLimits.ShownFeedsPerDay);
        var eaten = Math.Clamp((int)adult.FeedsToday, 0, slots);
        var gap = MathF.Min(Px(25f), MathF.Max(Px(14f), (size.Y - Px(55f)) / slots));
        var x = tl.X + size.X - Px(20f);
        var y = tl.Y + MathF.Max(Px(12f), (size.Y - gap * slots - Px(25f)) * 0.5f);
        var r = MathF.Min(Px(8f), gap * 0.34f);
        for (var i = 0; i < slots; i++)
        {
            var c = new Vector2(x, y + i * gap);
            var top = c - new Vector2(0, r);
            var right = c + new Vector2(r * 0.72f, 0);
            var bottom = c + new Vector2(0, r);
            var left = c - new Vector2(r * 0.72f, 0);
            if (i >= slots - eaten)
            {
                dl.AddQuadFilled(top, right, bottom, left, Look.U32(Look.Crystal));
                dl.AddTriangleFilled(top, c, left, Look.U32(Look.CrystalPale));
                dl.AddTriangleFilled(c, right, bottom, Look.U32(Look.Crystal, 0.55f));
            }
            dl.AddQuad(top, right, bottom, left,
                Look.U32(i >= slots - eaten ? Look.CrystalPale : Look.Whisper, 0.85f), Px(1.3f));
        }
        Look.Centred(dl, $"{eaten}/{slots}", x, y + (slots - 1) * gap + Px(14f), Look.U32(Look.Body, 0.8f), 0.8f);
        var hitTl = new Vector2(x - Px(18f), y - Px(12f));
        var hitBr = new Vector2(x + Px(18f), y + (slots - 1) * gap + Px(34f));
        FoodSlotsRect = (hitTl, hitBr);
        if (_emptyCrystal is null && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(hitTl, hitBr))
        {
            ImGui.SetTooltip(FeedingTooltip(ctx, core));
        }
    }

    private static string FeedingTooltip(OsAppContext ctx, AetherlingDto core)
    {
        var maximum = Math.Clamp((int)(core.Adult?.FeedsPerDay ?? AetherlingLimits.ShownFeedsPerDay), 1,
            AetherlingLimits.ShownFeedsPerDay);
        var eaten = Math.Clamp((int)(core.Adult?.FeedsToday ?? 0), 0, maximum);
        return string.Format(ctx.Localize("os.aetherling_feed_daily_tip"),
            core.PetName ?? AetherlingLimits.DefaultName, eaten, maximum);
    }

    private void DrawCrystalStoreOffer(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        if (_emptyCrystal is not { } element) return;
        var width = size.X - Px(36f);
        var message = string.Format(ctx.Localize("os.aetherling_crystal_offer"), ctx.Localize(Elements.NameKey(element)));
        var textHeight = ImGui.CalcTextSize(message, false, width - Px(32f)).Y;
        var height = textHeight + Px(100f);
        var tl = origin + new Vector2(Px(18f), (size.Y - height) * 0.45f);
        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void, 0.72f));
        dl.AddRectFilled(tl, tl + new Vector2(width, height), 0xFF291C25u, Px(16f));
        DrawCrystal(ctx, dl, element, tl + new Vector2(width * 0.5f, Px(23f)), Px(28f), 1f);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), tl + new Vector2(Px(16f), Px(44f)),
            Look.U32(Look.Body), message, width - Px(32f));
        var buttonWidth = (width - Px(42f)) * 0.5f;
        var yes = DrawCardButton(ctx, dl, tl + new Vector2(Px(16f), height - Px(44f)), buttonWidth,
            ctx.Localize("os.aetherling_crystal_offer_yes"), primary: true);
        var no = DrawCardButton(ctx, dl, tl + new Vector2(Px(26f) + buttonWidth, height - Px(44f)), buttonWidth,
            ctx.Localize("os.aetherling_crystal_offer_no"), primary: false);
        if (yes)
        {
            _emptyCrystal = null;
            ctx.Shell.SendIntent("store", OsIntents.CreatePath(OsIntents.StoreOpen, "consumables"));
        }
        else if (no || ImGui.IsKeyPressed(ImGuiKey.Escape))
            _emptyCrystal = null;
    }

    private (Elements.ElementDef Element, string Ref)? NearestUnlock(AetherlingDto core)
    {
        if (!ReferenceEquals(core, _nearestCore) || !ReferenceEquals(_inventory, _nearestInventory))
        {
            _nearestCore = core;
            _nearestInventory = _inventory;
            _nearestReward = FindNearestUnlock(core);
        }
        return _nearestReward;
    }

    private (Elements.ElementDef Element, string Ref)? FindNearestUnlock(AetherlingDto core)
    {
        if (_inventory is null || core.Adult is not { } adult) return null;
        var owned = PetState.OwnedRefs(_inventory, StoreItemKind.AetherlingShell);
        (Elements.ElementDef Element, string Ref)? result = null;
        var remaining = int.MaxValue;
        foreach (var element in Elements.All)
        {
            var count = PetState.DietCount(core, element);
            foreach (var (reference, threshold) in new[]
            {
                (ShellCatalog.FirstFor(element.Key), adult.ShellFeedThreshold),
                (ShellCatalog.SecondFor(element.Key), adult.ShellFeedThreshold2),
            })
            {
                if (owned.Contains(reference)) continue;
                var needed = Math.Max(0, threshold - count);
                if (needed >= remaining) continue;
                remaining = needed;
                result = (element, reference);
            }
        }
        return result;
    }

    private void DrawNearestUnlock(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float y, AetherlingDto core)
    {
        if (NearestUnlock(core) is not { } reward)
        {
            return;
        }
        var tl = new Vector2(origin.X + Px(28f), y);
        var width = size.X - Px(56f);
        var height = Px(BowlHeight);
        UnlockRect = (tl, tl + new Vector2(width, height));
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##aetherlingHomeUnlocks", new Vector2(width, height)))
        {
            UnlocksRequested?.Invoke();
        }
        if (ImGui.IsItemHovered())
        {
            HandOnHover();
            ImGui.SetTooltip(ctx.Localize("os.aetherling_unlocks"));
        }
        var text = ctx.Localize("os.aetherling_home_unlock");
        var available = width - Px(98f);
        var fontSize = ImGui.GetFontSize() * 0.82f;
        var textSize = ImGui.CalcTextSize(text, false, available / 0.82f) * 0.82f;
        dl.AddText(ImGui.GetFont(), fontSize, tl + new Vector2(0, (height - textSize.Y) * 0.5f),
            Look.U32(Look.Body, 0.9f), text, available);
        DrawCrystal(ctx, dl, reward.Element, tl + new Vector2(width - Px(78f), height * 0.5f), Px(25f), 1f);
        var previewTl = tl + new Vector2(width - Px(56f), Px(5f));
        dl.AddRectFilled(previewTl, previewTl + new Vector2(Px(36f)), 0xFFE8DAE5u, Px(9f));
        ShellPreview.PaintSilhouette(dl, reward.Ref, previewTl + new Vector2(Px(18f)), Px(30f), 0xFF050505u);
        IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(10f),
            tl + new Vector2(width - Px(4f), height * 0.5f), Look.U32(Look.Crystal, 0.8f));
    }
}
