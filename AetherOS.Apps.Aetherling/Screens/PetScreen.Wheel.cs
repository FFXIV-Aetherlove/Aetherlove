using System;
using System.Numerics;
using AetherLove.Shared.Aetherling;
using AetherLove.UI;
using AetherOS.PetKit.Engine;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Screens;

internal sealed partial class PetScreen
{
    private const float WheelButtonSize = 44f;

    private WheelOverlay? _wheel;
    private bool _wheelEntrancePending = true;
    private double _wheelEntranceAt = double.NegativeInfinity;

    public void AnimateWheelOnEntry() => _wheelEntrancePending = true;

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
        WheelRect = (tl, tl + new Vector2(side, side));
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
        if (_wheelEntrancePending)
        {
            _wheelEntrancePending = false;
            _wheelEntranceAt = spent || ctx.ReduceMotion ? double.NegativeInfinity : now;
        }
        var progress = Math.Clamp((float)((now - _wheelEntranceAt - 1.0) / 1.8), 0f, 1f);
        var turn = spent || ctx.ReduceMotion || progress >= 1f
            ? 0f : MathF.Tau * 2f * (1f - MathF.Pow(1f - progress, 3f));
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

    private static void DrawNewPip(OsAppContext ctx, ImDrawListPtr dl, Vector2 corner)
    {
        dl.AddCircleFilled(corner + new Vector2(Px(WheelButtonSize), Px(2f)), Px(3.5f), Look.U32(Look.Crystal));
    }
}
