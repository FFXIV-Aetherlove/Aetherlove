using System;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The wheel's way in: a round button under the stage that turns while a spin is waiting and
/// greys with a countdown once today's is used. The overlay itself lives in <see cref="WheelOverlay"/>.</summary>
internal sealed partial class PetScreen
{
    private const float WheelButtonSize = 44f;

    /// <summary>How much taller the band under the stage gets while the button is shown.</summary>
    private const float WheelRowExtra = 30f;

    private WheelOverlay? _wheel;

    /// <summary>The overlay, built on first use so a page that never opens it builds nothing.</summary>
    internal WheelOverlay Wheel => _wheel ??= BuildWheel();

    /// <summary>Asked without building the overlay, for the same reason as <see cref="CeremonyRunning"/>.</summary>
    public bool WheelOpen => _wheel?.Visible == true;

    /// <summary>Re-reads an open, idle wheel on foreground: the UTC day may have rolled meanwhile.</summary>
    public void RefreshWheel() => _wheel?.Refresh();

    /// <summary>Whether the player has ever opened the wheel. Until then the button wears a "new!" pip.</summary>
    public bool WheelSeen { get; set; }

    /// <summary>Raised the first time the wheel is opened, so the app can remember it.</summary>
    public event Action? WheelFirstOpened;

    /// <summary>Raised when the pet is wearing all it can and the prize needs the wardrobe.</summary>
    public event Action? WardrobeRequested;

    private WheelOverlay BuildWheel()
    {
        var overlay = new WheelOverlay(host, pet);
        overlay.Spun += _ => RefreshInventory();
        overlay.LookSaved += dto =>
        {
            AdoptCore(dto);
            RefreshInventory();
        };
        overlay.WardrobeRequested += () => WardrobeRequested?.Invoke();
        return overlay;
    }

    private bool WheelButtonVisible(AetherlingDto core) =>
        core is { Adult: not null, Wheel: not null }
        && ModesAvailable(core)
        && !_namingOpen
        && !RenameOverlayOpen
        && !Ticket.Visible
        && !WheelOpen
        && _settle >= 1f;

    private bool WheelSpent(AetherlingDto core)
    {
        if (_wheel?.Wheel is { } wheel)
        {
            return !wheel.Unlimited && wheel.Today is not null;
        }
        return core.Wheel?.SpunToday == true;
    }

    private DateTimeOffset WheelNextSpin(AetherlingDto core) =>
        _wheel?.Wheel?.NextSpinAtUtc ?? core.Wheel?.NextSpinAtUtc ?? DateTimeOffset.MinValue;

    private void DrawWheelButton(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, AetherlingDto core, double now)
    {
        var side = Px(WheelButtonSize);
        var centre = tl + new Vector2(side * 0.5f, side * 0.5f);
        var radius = side * 0.5f;
        var serverNow = DateTimeOffset.UtcNow + ServerOffset(core);
        var wait = WheelNextSpin(core) - serverNow;
        var spent = WheelSpent(core) && wait > TimeSpan.Zero;
        var alpha = spent ? 0.35f : 1f;

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingWheelButton", new Vector2(side, side));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            if (spent)
            {
                ImGui.SetTooltip(string.Format(ctx.Localize("os.aetherling_wheel_next"), FormatWait(wait)));
            }
            else
            {
                HandOnHover();
                ImGui.SetTooltip(ctx.Localize("os.aetherling_wheel_tip"));
            }
        }

        if (!spent)
        {
            Look.Halo(dl, centre, radius * 1.6f, Look.Spark, 0.10f + (0.04f * Look.Breathe(now, 2.4f)));
        }
        var turn = spent || ctx.ReduceMotion ? 0f : (float)(now * 0.35);
        for (var i = 0; i < 8; i++)
        {
            var start = turn + (i * MathF.PI * 0.25f);
            var colour = i % 2 == 0 ? Elements.All[(i / 2) % Elements.All.Count].Accent : Look.Spark;
            dl.PathArcTo(centre, radius, start, start + (MathF.PI * 0.25f), 6);
            dl.PathLineTo(centre);
            dl.PathFillConvex(Look.U32(colour with { W = (hovered && !spent ? 0.95f : 0.8f) * alpha }));
        }
        dl.AddCircle(centre, radius, Look.U32(Look.Void, 0.7f * alpha), 32, Px(1.5f));
        dl.AddCircle(centre, radius + Px(2f),
            Look.U32(Look.Spark, (spent ? 0.25f : 0.5f + (0.3f * Look.Breathe(now, 2.4f))) * alpha), 32, Px(2f));
        dl.AddCircleFilled(centre, radius * 0.22f, Look.U32(Look.CrystalPale, alpha), 16);

        if (!WheelSeen && !spent)
        {
            DrawNewPip(ctx, dl, tl);
        }

        if (pressed && !spent)
        {
            if (!WheelSeen)
            {
                WheelSeen = true;
                WheelFirstOpened?.Invoke();
            }
            Wheel.Open(core);
        }
    }

    /// <summary>The home screen's "new" pill, redrawn here because an app cannot reach the shell's.</summary>
    private static void DrawNewPip(OsAppContext ctx, ImDrawListPtr dl, Vector2 corner)
    {
        var label = ctx.Localize("os.aetherling_wheel_new");
        var scale = 0.72f;
        var textSize = ImGui.CalcTextSize(label) * scale;
        var padX = Px(5f);
        var height = textSize.Y + Px(4f);
        var tl = new Vector2(corner.X - padX, corner.Y - (height * 0.5f));
        var br = tl + new Vector2(textSize.X + (padX * 2f), height);
        dl.AddRectFilled(tl, br, Look.U32(new Vector4(0.86f, 0.13f, 0.16f, 1f)), height * 0.5f);
        dl.AddRect(tl, br, Look.U32(new Vector4(1f, 1f, 1f, 0.55f)), height * 0.5f, ImDrawFlags.RoundCornersAll, 1f);
        Look.Centred(dl, label, tl.X + ((br.X - tl.X) * 0.5f), tl.Y + Px(2f), Look.U32(new Vector4(1f, 1f, 1f, 1f)), scale);
    }
}
