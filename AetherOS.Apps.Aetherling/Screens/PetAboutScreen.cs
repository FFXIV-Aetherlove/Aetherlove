using System;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>What there is to know about it: the tip, then the facts. There is deliberately nothing to grow
/// here yet, and no switches either; this page reports.</summary>
internal sealed class PetAboutScreen(PetRuntime pet)
{
    public void Draw(OsAppContext ctx, AetherlingDto core, Action onBack)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void));

        var name = core.PetName ?? AetherlingLimits.DefaultName;
        var y = PetPageUi.Header(ctx, dl, origin, name,
            string.Format(ctx.Localize("os.aetherling_menu_about"), name), onBack);

        y += PetPageUi.TipCard(ctx, dl, origin, size, y,
            string.Format(ctx.Localize("os.aetherling_status_growth"), name), ImGui.GetTime());

        var born = (core.HatchedAtUtc ?? core.CreatedAtUtc).ToLocalTime().ToString("d MMM yyyy");
        y += PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Egg,
            ctx.Localize("os.aetherling_status_born"), born);
        y += PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Seedling,
            ctx.Localize("os.aetherling_status_stage"), ctx.Localize("os.aetherling_status_stage_baby"));
        PetPageUi.Row(dl, origin, size, y, FontAwesomeIcon.Heart,
            ctx.Localize("os.aetherling_status_mood"), ctx.Localize($"os.aetherling_feel_{(int)pet.Mood}"));
    }
}
