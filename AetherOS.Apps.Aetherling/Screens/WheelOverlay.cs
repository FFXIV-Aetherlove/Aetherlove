using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.PetKit.Engine;
using AetherOS.PetKit.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>The daily wheel, on the pet's own page. The server composes the wedges, rolls, and grants at
/// the spin; this overlay only performs. A press starts the jingle, a three-two-one in the hub, and a spin
/// planned to stop on the server's wedge exactly <see cref="SpinSeconds"/> after the press, which is the
/// jingle's length. A crystal shows itself at once; a mystery gift hides under a scratch foil.
/// <para>Every hub reply parks in a field that <see cref="Draw"/> drains: the sparks, the card and the
/// pet's own pool all belong to the draw thread.</para></summary>
internal sealed class WheelOverlay(IAetherlingHost host, PetRuntime pet)
{
    private enum Phase
    {
        Hidden,
        Loading,
        Idle,
        Countdown,
        Spinning,
        Landed,
        Prize,
    }

    public const int WedgeCount = 8;

    /// <summary>Press to stop, which is the jingle's length: one second of quiet, three seconds of count,
    /// then the wheel.</summary>
    public const float SpinSeconds = 16f;
    private const float CountdownStartSeconds = 1f;
    private const float CountdownSeconds = 3f;

    /// <summary>The twenty-second jingle, win sting included: one shot at the press, nothing else plays.</summary>
    private const string JingleFile = "wheel_spin.ogg";
    private const string TickFile = "wheel_tick.ogg";

    /// <summary>The scratch card's id, clear of the 0..16 band the reaction tickets use.</summary>
    private const int ScratchSlot = 40;

    private const float SpinUpSeconds = 1.2f;
    private const float CruiseTurnsPerSecond = 2.4f;
    private const float MinDecelSeconds = 3f;
    private const float SettleSeconds = 0.45f;
    private const float ShrinkSeconds = 0.35f;
    private const float ReboundWedgeFraction = 0.10f;
    /// <summary>The tick clip is about this long; one per wedge crossing, never two on top of each other.</summary>
    private const float TickMinGapSeconds = 0.07f;
    private const float WindDownSeconds = 1.2f;

    private const int MaxSparks = 420;

    /// <summary>The jingle sits under the ticks rather than over them.</summary>
    private const float JingleLevel = 0.55f;

    private static readonly float Wedge = MathF.Tau / WedgeCount;
    private static readonly float CruiseOmega = CruiseTurnsPerSecond * MathF.Tau;

    private struct Spark
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Life;
        public float MaxLife;
        public float Size;
        public Vector4 Colour;
        public bool Star;
        public bool Trail;
        public float Gravity;
        public float Drag;
    }

    private Phase _phase;
    private AetherlingDto? _core;
    private AetherlingWheelDto? _wheel;
    private ScratchCard? _card;
    private readonly ConfettiBurst _confetti = new();
    private readonly List<Spark> _sparks = new(MaxSparks);
    private readonly Random _rng = new();
    private float _in;
    private bool _busy;
    private bool _revealed;
    private string? _error;
    private float _errorLeft;

    private float _theta;
    private float _omega;
    private float _phaseT;
    private float _pressT;
    private int _target = -1;
    private float _thetaTarget;
    private float _decelFrom;
    private float _decelDistance;
    private float _decelSeconds;
    private float _decelT;
    private bool _decelerating;
    private bool _windingDown;
    private int _lastUnder;
    private float _tickGap;
    private float _pointerTilt;
    private int _countShown;
    private float _countPop;
    private bool _cheered;
    private float _wheelScale = 1f;
    private float _wheelAlpha = 1f;
    private float _emitCarry;
    private bool _digitBurst;
    private int _landWaves;

    private AetherlingWheelDto? _pendingWheel;
    private AetherlingWheelDto? _pendingSpin;
    private AetherlingDto? _pendingLook;
    private string? _pendingError;

    public bool Visible => _phase != Phase.Hidden;

    /// <summary>The wheel as last read from the server, so the page's button can grey itself without a call.</summary>
    public AetherlingWheelDto? Wheel => _wheel;

    /// <summary>Raised when a spin lands, with the wheel that carries the prize; the page re-reads its inventory.</summary>
    public event Action<AetherlingWheelDto>? Spun;

    /// <summary>Raised when Equip now has saved the look.</summary>
    public event Action<AetherlingDto>? LookSaved;

    /// <summary>Raised when the pet is wearing all it can: the app takes the player to the wardrobe.</summary>
    public event Action? WardrobeRequested;

    public void Open(AetherlingDto core)
    {
        _core = core;
        _card = new ScratchCard(ScratchSlot);
        _sparks.Clear();
        _in = 0f;
        _busy = false;
        _revealed = false;
        _error = null;
        _errorLeft = 0f;
        _theta = 0f;
        _omega = 0f;
        _target = -1;
        _decelerating = false;
        _windingDown = false;
        _cheered = false;
        _wheelScale = 1f;
        _wheelAlpha = 1f;
        _phase = Phase.Loading;
        Reload();
    }

    public void Adopt(AetherlingDto core) => _core = core;

    public void Close()
    {
        _phase = Phase.Hidden;
        _card = null;
        _sparks.Clear();
    }

    /// <summary>Re-reads the wheel while idle: the UTC day may have rolled since it was opened.</summary>
    public void Refresh()
    {
        if (_phase == Phase.Idle)
        {
            Reload();
        }
    }

    private void Reload()
    {
        _ = Task.Run(async () =>
        {
            var wheel = await host.GetWheelAsync().ConfigureAwait(false);
            if (wheel is null)
            {
                Interlocked.Exchange(ref _pendingError, string.Empty);
                return;
            }
            Interlocked.Exchange(ref _pendingWheel, wheel);
        });
    }

    public void Draw(OsAppContext ctx, Vector2 origin, Vector2 size, float dt)
    {
        if (_phase == Phase.Hidden || _core is null)
        {
            return;
        }
        DrainPending(ctx);
        if (_phase == Phase.Hidden)
        {
            return;
        }

        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##aetherlingWheel", size, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }

        var now = ImGui.GetTime();
        _in = ctx.ReduceMotion ? 1f : MathF.Min(1f, _in + (dt * 3.4f));
        var ease = Look.EaseOut(_in);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void with { W = 0.9f }, ease));

        var accent = Elements.Find(_core.Adult?.Element ?? 0)?.Accent ?? Look.Crystal;
        float titleH;
        using (ctx.TitleFont?.Push())
        {
            titleH = ImGui.GetTextLineHeight();
        }

        var panelW = size.X - Px(16f);
        var prizePhase = _phase == Phase.Prize;
        var radius = MathF.Min((panelW * 0.5f) - Px(22f), (size.Y - titleH - Px(150f)) * 0.5f);
        var panelH = prizePhase ? Px(318f) : Px(20f) + titleH + Px(28f) + (radius * 2f) + Px(48f);
        var panelTl = new Vector2(
            origin.X + ((size.X - panelW) * 0.5f),
            origin.Y + ((size.Y - panelH) * 0.5f) + ((1f - ease) * Px(16f)));
        var panelBr = panelTl + new Vector2(panelW, panelH);
        dl.AddRectFilled(panelTl, panelBr, Look.U32(new Vector4(0.07f, 0.06f, 0.11f, 0.98f), ease), Px(18f));
        dl.AddRect(panelTl, panelBr, Look.U32(accent, 0.45f * ease), Px(18f), ImDrawFlags.RoundCornersAll, Px(1.2f));
        Look.Motes(dl, panelTl, panelBr - panelTl, 24, Look.CrystalPale, 0.35f * ease, now, ctx.ReduceMotion);

        var centreX = panelTl.X + (panelW * 0.5f);
        using (ctx.TitleFont?.Push())
        {
            Look.Centred(dl, ctx.Localize("os.aetherling_wheel_title"), centreX, panelTl.Y + Px(14f),
                Look.U32(Look.CrystalPale, ease));
        }
        var belowTitle = panelTl.Y + Px(14f) + titleH;

        var closable = _phase == Phase.Idle || (prizePhase && _revealed);
        if (prizePhase)
        {
            DrawPrizePhase(ctx, dl, panelTl, panelW, panelH, belowTitle, accent, ease);
        }
        else
        {
            var centre = new Vector2(centreX, belowTitle + Px(28f) + radius);
            TickSpin(ctx, dt);
            EmitSparks(ctx, centre, radius * _wheelScale, dt);
            DrawWheel(ctx, dl, centre, radius * _wheelScale, accent, now, ease * _wheelAlpha);
            if (_phase == Phase.Landed)
            {
                DrawLanding(dl, panelTl, panelBr);
            }
        }

        StepSparks(dt);
        DrawSparks(dl, panelTl, panelBr);
        if (_phase is Phase.Landed or Phase.Prize)
        {
            _confetti.Draw(panelTl, panelBr);
        }

        _errorLeft = MathF.Max(0f, _errorLeft - dt);
        if (_error is { } error && _errorLeft > 0f)
        {
            Look.CentredWrapped(dl, error, centreX, panelBr.Y - Px(30f), panelW - Px(28f),
                Look.U32(Look.Spark, 0.9f), 0.82f);
        }

        // The scrim last, so everything on the panel takes its own click first. Outside the panel it
        // closes only while nothing is at stake; mid-spin and mid-scratch a stray press is swallowed.
        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##aetherlingWheelScrim", size) && closable)
        {
            var mouse = ImGui.GetIO().MousePos;
            var inside = mouse.X >= panelTl.X && mouse.X <= panelBr.X && mouse.Y >= panelTl.Y && mouse.Y <= panelBr.Y;
            if (!inside)
            {
                Close();
            }
        }
    }

    private void DrawWheel(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 centre, float radius, Vector4 accent, double now, float alpha)
    {
        if (radius <= 1f || alpha <= 0f)
        {
            return;
        }
        var speed = MathF.Min(1f, _omega / CruiseOmega);
        Look.Halo(dl, centre, radius * (1.25f + (0.15f * speed)), accent, (0.10f + (0.10f * speed)) * alpha, 6);

        var wedges = _wheel?.Wedges;
        for (var i = 0; i < WedgeCount; i++)
        {
            var start = -MathF.PI * 0.5f + _theta + (i * Wedge);
            var wedge = wedges is { Length: WedgeCount } ? wedges[i] : null;
            DrawWedge(ctx, dl, centre, radius, i, start, wedge, now, alpha);
        }

        // The rim: a gold band, a breathing glow, and a ring of studs that reads as motion when it spins.
        var rimAlpha = (0.55f + (0.35f * Look.Breathe(now, 1.8f))) * alpha;
        dl.AddCircle(centre, radius + Px(5f), Look.U32(Look.Spark, 0.18f * alpha), 96, Px(10f));
        dl.AddCircle(centre, radius + Px(3f), Look.U32(Look.Spark, rimAlpha), 96, Px(3f));
        dl.AddCircle(centre, radius, Look.U32(Look.Void, 0.7f * alpha), 96, Px(1.5f));
        for (var i = 0; i < WedgeCount * 2; i++)
        {
            var a = _theta + (i * Wedge * 0.5f);
            var at = centre + ((radius + Px(3f)) * new Vector2(MathF.Cos(a), MathF.Sin(a)));
            dl.AddCircleFilled(at, Px(2.4f), Look.U32(Look.CrystalPale, 0.9f * alpha), 8);
        }

        DrawHub(ctx, dl, centre, radius, now, alpha);
        DrawPointer(dl, centre, radius, alpha);
    }

    private void DrawWedge(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 centre, float radius, int index, float start,
        AetherlingWheelWedgeDto? wedge, double now, float alpha)
    {
        var crystal = wedge is { Kind: (short)AetherlingWheelEntryKind.Crystal };
        var mystery = wedge is { Kind: (short)AetherlingWheelEntryKind.Category };
        var element = crystal ? Elements.All.FirstOrDefault(e => e.Key == wedge!.Ref) : default;
        var baseColour = crystal && element.Key is not null ? element.Accent : Look.Spark;
        var lit = _phase == Phase.Landed && index == _target;
        var fillAlpha = mystery
            ? 0.55f + (0.25f * Look.Breathe(now, 1.1f, index))
            : 0.45f;
        if (lit)
        {
            fillAlpha = 0.8f + (0.2f * Look.Breathe(now, 0.4f));
        }

        dl.PathArcTo(centre, radius, start, start + Wedge, 12);
        dl.PathLineTo(centre);
        dl.PathFillConvex(Look.U32(baseColour with { W = fillAlpha * alpha }));

        // A darker inner disc gives the wedge a rim band, so the icons sit on a ring rather than a pie.
        dl.PathArcTo(centre, radius * 0.86f, start, start + Wedge, 12);
        dl.PathLineTo(centre);
        dl.PathFillConvex(Look.U32(Look.Void with { W = 0.35f * alpha }));

        var edge = centre + (radius * new Vector2(MathF.Cos(start), MathF.Sin(start)));
        dl.AddLine(centre, edge, Look.U32(Look.Spark, 0.45f * alpha), Px(1.6f));

        if (wedge is null)
        {
            return;
        }
        var mid = start + (Wedge * 0.5f);
        var dir = new Vector2(MathF.Cos(mid), MathF.Sin(mid));
        var at = centre + (radius * 0.60f * dir);
        var box = radius * 0.30f;
        if (crystal)
        {
            DrawCrystalIcon(ctx, dl, at, box, element.Key ?? wedge.Ref, baseColour, alpha);
        }
        else
        {
            DrawMysteryWedge(dl, centre, radius, start, at, box, wedge.Ref, now, alpha);
        }
    }

    private static void DrawCrystalIcon(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 centre, float size, string elementKey, Vector4 tint, float alpha)
    {
        if (CoreAssets.CrystalPath(elementKey) is { } path && ctx.Capabilities.Textures.Get(path) is { } texture)
        {
            var half = size * 0.5f;
            dl.AddImage(texture, centre - new Vector2(half, half), centre + new Vector2(half, half),
                Vector2.Zero, Vector2.One, Look.U32(new Vector4(1f, 1f, 1f, alpha)));
            return;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Gem, size * 0.55f, centre, Look.U32(tint, 0.95f * alpha));
    }

    /// <summary>The seats worth wanting: a gold sheen sweeping the wedge, a glowing socket glyph and a
    /// question mark that breathes. Everything here is louder than a crystal on purpose.</summary>
    private void DrawMysteryWedge(
        ImDrawListPtr dl, Vector2 centre, float radius, float start, Vector2 at, float box, string categoryKey,
        double now, float alpha)
    {
        // The sheen: a narrow bright sector that sweeps across the wedge every couple of seconds.
        var sweep = (float)((now * 0.55) % 1.0);
        var sheenStart = start + (Wedge * sweep * 0.9f);
        dl.PathArcTo(centre, radius * 0.98f, sheenStart, sheenStart + (Wedge * 0.12f), 6);
        dl.PathLineTo(centre);
        dl.PathFillConvex(Look.U32(new Vector4(1f, 1f, 1f, 0.22f * alpha)));

        Look.Halo(dl, at, box * 1.4f, Look.Spark, (0.25f + (0.15f * Look.Breathe(now, 0.9f))) * alpha, 4);

        var ink = Look.U32(Look.Void, 0.9f * alpha);
        var slot = SlotFor(categoryKey);
        if (slot.Paint is { } paint)
        {
            paint(dl, at, box, ink);
        }
        else
        {
            IconDraw.AddCentered(dl, slot.Icon, box * 0.42f, at, ink);
        }

        var pulse = 1f + (0.15f * Look.Breathe(now, 0.9f));
        var pip = at + new Vector2(box * 0.40f, -box * 0.40f);
        dl.AddCircleFilled(pip, Px(8f) * pulse, Look.U32(Look.Void, 0.95f * alpha), 20);
        dl.AddCircle(pip, Px(8f) * pulse, Look.U32(Look.Spark, alpha), 20, Px(1.2f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Question, Px(9f) * pulse, pip, Look.U32(Look.Spark, alpha));
    }

    /// <summary>The socket glyph a store category maps to. The hands shelf holds arms, and the ears and
    /// tails share a shelf under the ears' triangles.</summary>
    private static EquipSlots.SlotDef SlotFor(string categoryKey)
    {
        var slotKey = categoryKey switch
        {
            "acc-hands" => AccessoryDef.ArmsSlot,
            "acc-ears-tails" => AccessoryDef.EarsSlot,
            _ when categoryKey.StartsWith("acc-", StringComparison.Ordinal) => categoryKey[4..],
            _ => categoryKey,
        };
        foreach (var slot in EquipSlots.All)
        {
            if (slot.Key == slotKey)
            {
                return slot;
            }
        }
        return EquipSlots.All[0];
    }

    private void DrawHub(OsAppContext ctx, ImDrawListPtr dl, Vector2 centre, float radius, double now, float alpha)
    {
        var hub = radius * 0.24f;
        var idle = _phase == Phase.Idle && !_busy;
        var spent = _wheel is { Unlimited: false, Today: not null };
        var enabled = idle && !spent;

        var side = hub * 2f;
        ImGui.SetCursorScreenPos(centre - new Vector2(hub, hub));
        var pressed = ImGui.InvisibleButton("##aetherlingWheelSpin", new Vector2(side, side));
        var hovered = ImGui.IsItemHovered();
        if (hovered && enabled)
        {
            SharedUiHelpers.HandOnHover();
        }

        var ring = enabled ? 0.55f + (0.35f * Look.Breathe(now, 1.4f)) : 0.4f;
        if (enabled)
        {
            Look.Halo(dl, centre, hub * 1.8f, Look.Spark, 0.16f * alpha, 4);
        }
        dl.AddCircleFilled(centre, hub + Px(3f), Look.U32(Look.Spark, (hovered && enabled ? 0.95f : ring) * alpha), 48);
        dl.AddCircleFilled(centre, hub, Look.U32(Look.Void, 0.96f * alpha), 48);

        if (_phase == Phase.Loading)
        {
            LoadingSpinner.Draw(centre, Px(8f), Px(2.2f), Look.U32(Look.CrystalPale, alpha));
            return;
        }
        if (_phase == Phase.Countdown)
        {
            DrawCountdown(dl, centre, hub, alpha);
            return;
        }

        var label = _phase is Phase.Spinning or Phase.Landed
            ? ctx.Localize("os.aetherling_wheel_spinning")
            : spent
                ? ctx.Localize("os.aetherling_wheel_spent")
                : ctx.Localize("os.aetherling_wheel_spin");
        var scale = MathF.Min(1.3f, (hub * 1.5f) / MathF.Max(1f, ImGui.CalcTextSize(label).X));
        Look.Centred(dl, label, centre.X, centre.Y - (ImGui.GetTextLineHeight() * scale * 0.5f),
            Look.U32(enabled ? Look.Spark : Look.Whisper, alpha), scale);

        if (pressed && enabled)
        {
            StartSpin(ctx);
        }
    }

    /// <summary>Three, two, one in the bearing, each digit popping in and shrinking to rest.</summary>
    private void DrawCountdown(ImDrawListPtr dl, Vector2 centre, float hub, float alpha)
    {
        var digit = _countShown;
        if (digit <= 0)
        {
            return;
        }
        var pop = 1f + (0.6f * MathF.Max(0f, _countPop));
        var text = digit.ToString();
        var scale = MathF.Min(2.6f, (hub * 1.4f) / MathF.Max(1f, ImGui.CalcTextSize(text).X)) * pop;
        var height = ImGui.GetTextLineHeight() * scale;
        Look.Halo(dl, centre, hub * (0.9f + (0.6f * _countPop)), Look.Spark, 0.35f * alpha, 4);
        Look.GlowText(dl, text, centre.X, centre.Y - (height * 0.5f), Look.U32(Look.CrystalPale, alpha), scale,
            Look.Spark, 0.8f);
    }

    private void DrawPointer(ImDrawListPtr dl, Vector2 centre, float radius, float alpha)
    {
        var apexY = centre.Y - radius + Px(14f);
        var baseY = centre.Y - radius - Px(14f);
        var halfBase = Px(11f);
        var pivot = new Vector2(centre.X, baseY);
        var tilt = _pointerTilt;
        Vector2 Rotate(Vector2 p)
        {
            var d = p - pivot;
            var c = MathF.Cos(tilt);
            var s = MathF.Sin(tilt);
            return pivot + new Vector2((d.X * c) - (d.Y * s), (d.X * s) + (d.Y * c));
        }
        var a = Rotate(new Vector2(centre.X, apexY));
        var b = Rotate(new Vector2(centre.X - halfBase, baseY));
        var c2 = Rotate(new Vector2(centre.X + halfBase, baseY));
        dl.AddTriangleFilled(a, b, c2, Look.U32(Look.Spark, alpha));
        dl.AddTriangle(a, b, c2, Look.U32(Look.Void, 0.8f * alpha), Px(1.5f));
        dl.AddCircleFilled(pivot, Px(4f), Look.U32(Look.CrystalPale, alpha), 12);
    }

    private void StartSpin(OsAppContext ctx)
    {
        _phase = Phase.Countdown;
        _phaseT = 0f;
        _pressT = 0f;
        _target = -1;
        _decelerating = false;
        _windingDown = false;
        _cheered = false;
        _error = null;
        _tickGap = 0f;
        _countShown = 0;
        _countPop = 0f;
        _digitBurst = false;
        _landWaves = 0;
        _lastUnder = WedgeUnder(_theta);
        _busy = true;
        PlayOneShot(ctx, JingleFile, JingleLevel, 1f);
        _ = Task.Run(async () =>
        {
            try
            {
                var wheel = await host.SpinWheelAsync().ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingSpin, wheel);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }

    /// <summary>The spin's clock, counted from the press. A second of quiet, the count, the spin-up, cruise
    /// until the reply names the wedge, then a wind-down planned to stop on the jingle's last beat. An
    /// error winds the wheel down where it is and hands the hub back.</summary>
    private void TickSpin(OsAppContext ctx, float dt)
    {
        if (_phase is Phase.Countdown or Phase.Spinning or Phase.Landed)
        {
            _pressT += dt;
        }
        _countPop = MathF.Max(0f, _countPop - (dt * 3f));

        if (_phase == Phase.Countdown)
        {
            var since = _pressT - CountdownStartSeconds;
            var digit = since < 0f ? 0 : (int)MathF.Ceiling(CountdownSeconds - since);
            if (since >= 0f && digit != _countShown && digit > 0)
            {
                _countShown = digit;
                _countPop = 1f;
                _digitBurst = true;
                PlayOneShot(ctx, TickFile, 1f, 0.7f);
            }
            if (_windingDown)
            {
                _windingDown = false;
                _busy = false;
                _phase = Phase.Idle;
                return;
            }
            if (since >= CountdownSeconds)
            {
                _phase = Phase.Spinning;
                _phaseT = 0f;
                _countShown = 0;
            }
            return;
        }

        if (_phase == Phase.Spinning)
        {
            _phaseT += dt;
            if (_windingDown)
            {
                _omega = CruiseOmega * MathF.Max(0f, 1f - (_phaseT / WindDownSeconds));
                _theta += _omega * dt;
                if (_omega <= 0f)
                {
                    _windingDown = false;
                    _busy = false;
                    _phase = Phase.Idle;
                }
            }
            else if (_decelerating)
            {
                _decelT += dt;
                var s = MathF.Min(1f, _decelT / _decelSeconds);
                _theta = _decelFrom + (_decelDistance * (1f - ((1f - s) * (1f - s))));
                _omega = 2f * _decelDistance / _decelSeconds * (1f - s);
                if (s >= 1f)
                {
                    _theta = _thetaTarget;
                    _omega = 0f;
                    _phase = Phase.Landed;
                    _phaseT = 0f;
                }
            }
            else
            {
                var ramp = MathF.Min(1f, _phaseT / SpinUpSeconds);
                _omega = CruiseOmega * ramp * ramp;
                _theta += _omega * dt;
                // The reply usually lands during the count; the wind-down is planned from cruise, so it waits.
                if (_target >= 0 && ramp >= 1f)
                {
                    AimAt(_target);
                }
            }
        }
        else if (_phase == Phase.Landed)
        {
            _phaseT += dt;
            var s = MathF.Min(1f, _phaseT / SettleSeconds);
            _theta = _thetaTarget - (ReboundWedgeFraction * Wedge * Look.EaseOut(s) * (1f - s));
            _omega = 0f;
            if (_phaseT > SettleSeconds + 0.9f)
            {
                var shrink = MathF.Min(1f, (_phaseT - SettleSeconds - 0.9f) / ShrinkSeconds);
                _wheelScale = 1f - (0.25f * Look.EaseOut(shrink));
                _wheelAlpha = 1f - shrink;
                if (shrink >= 1f)
                {
                    EnterPrize();
                }
            }
        }

        _theta %= MathF.Tau;
        if (_theta < 0f)
        {
            _theta += MathF.Tau;
        }

        _pointerTilt = MathF.Max(0f, _pointerTilt - (dt * 4f));
        _tickGap = MathF.Max(0f, _tickGap - dt);
        if (_phase == Phase.Spinning && _omega > 0f)
        {
            var under = WedgeUnder(_theta);
            if (under != _lastUnder)
            {
                _lastUnder = under;
                if (_tickGap <= 0f)
                {
                    _tickGap = TickMinGapSeconds;
                    _pointerTilt = 0.35f;
                    PlayOneShot(ctx, TickFile, 1f, 0.9f + (0.5f * MathF.Min(1f, _omega / CruiseOmega)));
                }
            }
        }
    }

    /// <summary>The reply is in and the wheel is at cruise: plan the wind-down so it stops on the target
    /// at the jingle's end.</summary>
    private void AimAt(int wedgeIndex)
    {
        var offset = 0.25f + (0.5f * _rng.NextSingle());
        _thetaTarget = MathF.Tau - ((wedgeIndex + offset) * Wedge);
        var remaining = MathF.Max(MinDecelSeconds, SpinSeconds - _pressT);
        var rem = (_thetaTarget - _theta) % MathF.Tau;
        if (rem < 0f)
        {
            rem += MathF.Tau;
        }
        // A constant slow-down from cruise over T covers cruise·T/2, so the turns are whatever fill that.
        var wanted = CruiseOmega * remaining * 0.5f;
        var turns = MathF.Max(1f, MathF.Ceiling((wanted - rem) / MathF.Tau));
        _decelFrom = _theta;
        _decelDistance = rem + (turns * MathF.Tau);
        _decelSeconds = remaining;
        _decelT = 0f;
        _decelerating = true;
    }

    private static int WedgeUnder(float theta)
    {
        var local = (-theta) % MathF.Tau;
        if (local < 0f)
        {
            local += MathF.Tau;
        }
        return (int)(local / Wedge) % WedgeCount;
    }

    private void DrawLanding(ImDrawListPtr dl, Vector2 panelTl, Vector2 panelBr)
    {
        if (_phaseT < 0.22f)
        {
            var flash = 0.5f * (1f - (_phaseT / 0.22f));
            dl.AddRectFilled(panelTl, panelBr, Look.U32(new Vector4(1f, 1f, 1f, flash)), Px(18f));
        }
    }

    /// <summary>Into the prize card. A crystal is shown at once; a gift keeps its foil.</summary>
    private void EnterPrize()
    {
        _phase = Phase.Prize;
        _phaseT = 0f;
        _busy = false;
        if (_wheel?.Today is { } today && today.PrizeKind == (short)StoreItemKind.AetherlingConsumable)
        {
            _revealed = true;
            _card?.Celebrate();
            Reveal();
        }
    }

    /// <summary>Everything that flies. Sparks off the rim with speed, a comet trail off the pointer, an
    /// orbit of motes at cruise, a burst from the hub on every digit, three waves on the land, and a
    /// drizzle of gold off every mystery seat while the wheel rests. A fixed pool, so nothing can grow.</summary>
    private void EmitSparks(OsAppContext ctx, Vector2 centre, float radius, float dt)
    {
        if (ctx.ReduceMotion || radius <= 1f)
        {
            return;
        }
        var speed = MathF.Min(1f, _omega / CruiseOmega);

        // Rim sparks, thrown off along the tangent.
        _emitCarry += speed * 160f * dt;
        while (_emitCarry >= 1f)
        {
            _emitCarry -= 1f;
            var a = (float)(_rng.NextDouble() * MathF.Tau);
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            var tangent = new Vector2(-dir.Y, dir.X);
            var vel = (tangent * (Px(60f) + (Px(160f) * speed))) + (dir * Px(40f) * (float)_rng.NextDouble());
            Add(centre + (dir * (radius + Px(4f))), vel, 0.4f + ((float)_rng.NextDouble() * 0.5f),
                Px(1.4f) + ((float)_rng.NextDouble() * Px(2.4f)),
                _rng.NextDouble() < 0.6 ? Look.Spark : Look.CrystalPale, star: _rng.NextDouble() < 0.3,
                trail: _rng.NextDouble() < 0.35, gravity: Px(40f), drag: 2.2f);
        }

        // An orbit of motes that rides the rim at cruise and scatters as the wheel slows.
        if (speed > 0.5f && _rng.NextDouble() < dt * 24f)
        {
            var a = -MathF.PI * 0.5f + (float)(_rng.NextDouble() * MathF.Tau);
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            var tangent = new Vector2(-dir.Y, dir.X);
            Add(centre + (dir * (radius + Px(14f))), tangent * Px(90f) * speed, 0.8f, Px(2.6f),
                Look.Crystal, star: false, trail: true, gravity: 0f, drag: 0.3f);
        }

        // The pointer's comet tail while it is being rattled.
        if (_pointerTilt > 0.05f && _rng.NextDouble() < dt * 40f)
        {
            var tip = centre + new Vector2(0f, -radius + Px(14f));
            var vel = new Vector2(((float)_rng.NextDouble() - 0.5f) * Px(80f), -Px(30f) - ((float)_rng.NextDouble() * Px(60f)));
            Add(tip, vel, 0.35f, Px(2f), Look.Spark, star: true, trail: false, gravity: Px(80f), drag: 1.5f);
        }

        // Each digit of the count pops a ring of sparks out of the bearing.
        if (_digitBurst)
        {
            _digitBurst = false;
            Ring(centre, 36, Px(120f), Px(220f), 0.6f, Look.Spark, Look.CrystalPale);
        }

        // The land: a big ring, then a second slower wave, then a gold rain from the top of the panel.
        if (_phase == Phase.Landed)
        {
            if (_landWaves == 0)
            {
                _landWaves = 1;
                Ring(centre, 90, Px(180f), Px(340f), 0.9f, Look.Spark, Look.CrystalPale);
            }
            else if (_landWaves == 1 && _phaseT > 0.25f)
            {
                _landWaves = 2;
                Ring(centre, 60, Px(80f), Px(160f), 1.2f, Look.CrystalPale, Look.Spark);
            }
            else if (_landWaves == 2 && _phaseT > 0.5f)
            {
                _landWaves = 3;
                var litDir = new Vector2(0f, -1f);
                for (var i = 0; i < 40; i++)
                {
                    var along = ((float)_rng.NextDouble() - 0.5f) * radius * 0.9f;
                    var pos = centre + (litDir * (radius * (0.3f + ((float)_rng.NextDouble() * 0.7f)))) + new Vector2(along, 0f);
                    Add(pos, new Vector2(((float)_rng.NextDouble() - 0.5f) * Px(30f), -Px(20f) - ((float)_rng.NextDouble() * Px(60f))),
                        1.2f + ((float)_rng.NextDouble() * 0.8f), Px(2.5f), Look.Spark, star: true, trail: true,
                        gravity: Px(20f), drag: 0.8f);
                }
            }
            return;
        }

        // The mystery seats drizzle gold upward all the time, heavier while the wheel rests.
        if (_wheel?.Wedges is not { Length: WedgeCount } wedges)
        {
            return;
        }
        var drizzle = speed > 0.2f ? 6f : 16f;
        for (var i = 0; i < WedgeCount; i++)
        {
            if (wedges[i].Kind != (short)AetherlingWheelEntryKind.Category || _rng.NextDouble() > dt * drizzle)
            {
                continue;
            }
            var mid = -MathF.PI * 0.5f + _theta + ((i + 0.5f) * Wedge) + (((float)_rng.NextDouble() - 0.5f) * Wedge * 0.8f);
            var dir = new Vector2(MathF.Cos(mid), MathF.Sin(mid));
            var r = radius * (0.3f + ((float)_rng.NextDouble() * 0.6f));
            Add(centre + (dir * r), (dir * Px(18f)) + new Vector2(0f, -Px(26f)), 0.9f + ((float)_rng.NextDouble() * 0.7f),
                Px(2f) + ((float)_rng.NextDouble() * Px(2.4f)), Look.Spark, star: true, trail: false,
                gravity: -Px(10f), drag: 0.6f);
        }
    }

    private void Ring(Vector2 centre, int count, float minSpeed, float maxSpeed, float life, Vector4 a, Vector4 b)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = (i / (float)count * MathF.Tau) + ((float)_rng.NextDouble() * 0.2f);
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var speed = minSpeed + ((float)_rng.NextDouble() * (maxSpeed - minSpeed));
            Add(centre, dir * speed, life * (0.7f + ((float)_rng.NextDouble() * 0.6f)),
                Px(1.8f) + ((float)_rng.NextDouble() * Px(2.6f)), i % 2 == 0 ? a : b, star: i % 3 == 0,
                trail: i % 2 == 0, gravity: Px(60f), drag: 1.6f);
        }
    }

    private void Add(Vector2 pos, Vector2 vel, float life, float size, Vector4 colour, bool star, bool trail, float gravity, float drag)
    {
        if (_sparks.Count >= MaxSparks)
        {
            return;
        }
        _sparks.Add(new Spark
        {
            Pos = pos,
            Vel = vel,
            Life = 0f,
            MaxLife = life,
            Size = size,
            Colour = colour,
            Star = star,
            Trail = trail,
            Gravity = gravity,
            Drag = drag,
        });
    }

    private void StepSparks(float dt)
    {
        for (var i = _sparks.Count - 1; i >= 0; i--)
        {
            var s = _sparks[i];
            s.Life += dt;
            if (s.Life >= s.MaxLife)
            {
                _sparks.RemoveAt(i);
                continue;
            }
            s.Vel *= MathF.Max(0f, 1f - (dt * s.Drag));
            s.Vel.Y += s.Gravity * dt;
            s.Pos += s.Vel * dt;
            _sparks[i] = s;
        }
    }

    private void DrawSparks(ImDrawListPtr dl, Vector2 clipMin, Vector2 clipMax)
    {
        if (_sparks.Count == 0)
        {
            return;
        }
        dl.PushClipRect(clipMin, clipMax, true);
        foreach (var s in _sparks)
        {
            var t = s.Life / s.MaxLife;
            var alpha = t < 0.15f ? t / 0.15f : 1f - ((t - 0.15f) / 0.85f);
            var size = s.Size * (1f - (0.5f * t));
            var colour = Look.U32(s.Colour, alpha);
            if (s.Trail)
            {
                var tail = s.Pos - (s.Vel * 0.06f);
                dl.AddLine(tail, s.Pos, Look.U32(s.Colour, alpha * 0.5f), size);
            }
            if (s.Star)
            {
                dl.AddLine(s.Pos - new Vector2(size * 2f, 0f), s.Pos + new Vector2(size * 2f, 0f), colour, Px(1f));
                dl.AddLine(s.Pos - new Vector2(0f, size * 2f), s.Pos + new Vector2(0f, size * 2f), colour, Px(1f));
            }
            dl.AddCircleFilled(s.Pos, size, colour, 8);
        }
        dl.PopClipRect();
    }

    private void DrawPrizePhase(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 panelTl, float panelW, float panelH, float belowTitle,
        Vector4 accent, float ease)
    {
        if (_card is null || _wheel?.Today is not { } today)
        {
            return;
        }
        var centreX = panelTl.X + (panelW * 0.5f);
        var gift = today.PrizeKind != (short)StoreItemKind.AetherlingConsumable;
        var y = belowTitle + Px(6f);
        var body = _revealed
            ? string.Format(ctx.Localize("os.aetherling_wheel_won"), PrizeName(ctx, today))
            : ctx.Localize("os.aetherling_wheel_prize_hint");
        y += Look.CentredWrapped(dl, body, centreX, y, panelW - Px(28f), Look.U32(Look.Body, 0.9f * ease), 0.95f)
            * Look.LineStep(0.95f);

        var cardW = panelW - Px(32f);
        var cardH = Px(150f);
        var cardTl = new Vector2(panelTl.X + Px(16f), y + Px(10f));
        _card.Draw(ctx, dl, cardTl, new Vector2(cardW, cardH), _revealed, false,
            (faceTl, faceSize) => DrawPrizeFace(ctx, dl, faceTl, faceSize, today, accent));

        if (gift && _card.WantsReveal && !_revealed)
        {
            _card.MarkRevealRequested();
            _revealed = true;
            _card.Celebrate();
            _confetti.Reset();
            pet.Celebrate();
            pet.AuditionGlyph("burst");
            Reveal();
        }

        if (!_revealed)
        {
            return;
        }
        // A gift gets Equip now beside Close; a crystal only needs Close, the basket is right there on the page.
        var buttonY = panelTl.Y + panelH - Px(52f);
        var half = (cardW - Px(10f)) * 0.5f;
        if (gift)
        {
            var equip = DrawPill(dl, "##aetherlingWheelPrimary", ctx.Localize("os.aetherling_wheel_equip"),
                new Vector2(panelTl.X + Px(16f), buttonY), new Vector2(half, Px(38f)), Look.Spark, ease, _busy);
            if (equip && !_busy)
            {
                EquipNow(ctx, today.PrizeRef);
            }
        }
        var closeTl = gift ? new Vector2(panelTl.X + Px(26f) + half, buttonY) : new Vector2(panelTl.X + Px(16f), buttonY);
        var closeW = gift ? half : cardW;
        var close = DrawPill(dl, "##aetherlingWheelClose", ctx.Localize("os.aetherling_wheel_close"),
            closeTl, new Vector2(closeW, Px(38f)), gift ? Look.Crystal with { W = 0.35f } : Look.Spark, ease, false);
        if (close)
        {
            Close();
        }
    }

    private static bool DrawPill(
        ImDrawListPtr dl, string id, string label, Vector2 tl, Vector2 size, Vector4 fill, float ease, bool busy)
    {
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered && !busy)
        {
            SharedUiHelpers.HandOnHover();
        }
        var alpha = fill.W * (hovered && !busy ? 1f : 0.8f) * ease;
        dl.AddRectFilled(tl, tl + size, Look.U32(fill with { W = alpha }), size.Y * 0.5f);
        if (busy)
        {
            LoadingSpinner.Draw(tl + (size * 0.5f), Px(8f), Px(2.2f), Look.U32(Look.Void));
            return false;
        }
        var dark = fill.W > 0.5f;
        Look.Centred(dl, label, tl.X + (size.X * 0.5f), tl.Y + ((size.Y - ImGui.GetTextLineHeight()) * 0.5f),
            Look.U32(dark ? Look.Void : Look.CrystalPale, 0.95f * ease));
        return pressed && !busy;
    }

    /// <summary>The prize as art and name: the art fills the left of the card, the name sits beside it.
    /// Under a foil only a caption shows, so rubbing is the reveal and not a formality.</summary>
    private void DrawPrizeFace(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size, AetherlingWheelResultDto today, Vector4 accent)
    {
        var centreX = tl.X + (size.X * 0.5f);
        Look.Centred(dl, ctx.Localize("os.aetherling_wheel_prize_caption"), centreX, tl.Y + Px(8f),
            Look.U32(Look.Whisper, 0.7f), 0.8f);
        if (!_revealed)
        {
            return;
        }

        var captionH = ImGui.GetTextLineHeight() * 0.8f;
        var art = size.Y - captionH - Px(24f);
        var artTl = new Vector2(tl.X + Px(16f), tl.Y + Px(8f) + captionH + Px(8f));
        var artCentre = artTl + new Vector2(art * 0.5f, art * 0.5f);
        var crystal = today.PrizeKind == (short)StoreItemKind.AetherlingConsumable;
        Look.Halo(dl, artCentre, art * 0.7f, crystal ? accent : Look.Spark, 0.25f, 5);
        if (crystal)
        {
            var element = Elements.All.FirstOrDefault(e => Elements.CrystalRef(e) == today.PrizeRef);
            DrawCrystalIcon(ctx, dl, artCentre, art, element.Key ?? string.Empty, element.Key is null ? accent : element.Accent, 1f);
        }
        else
        {
            DrawAccessoryThumb(ctx, dl, artTl, art, today.PrizeRef);
        }

        var name = PrizeName(ctx, today);
        var labelX = artTl.X + art + Px(12f);
        var labelW = tl.X + size.X - Px(14f) - labelX;
        var lines = Look.WrappedHeight(name, labelW, 1.1f);
        var labelY = artCentre.Y - (lines * 0.5f);
        Look.CentredWrapped(dl, name, labelX + (labelW * 0.5f), labelY, labelW, Look.U32(Look.CrystalPale), 1.1f);
    }

    private void DrawAccessoryThumb(OsAppContext ctx, ImDrawListPtr dl, Vector2 thumbTl, float thumb, string itemRef)
    {
        var thumbCentre = thumbTl + new Vector2(thumb * 0.5f, thumb * 0.5f);
        if (pet.Catalogue is not { } catalogue || catalogue.Accessory(itemRef) is not { } def)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Gift, thumb * 0.5f, thumbCentre, Look.U32(Look.Spark, 0.95f));
            return;
        }
        if (def.IsDrawnPart)
        {
            if (ctx.Capabilities.Textures.Get(catalogue.AccessoryThumbPath(def)) is { } partTex)
            {
                dl.AddImage(partTex, thumbTl, thumbTl + new Vector2(thumb, thumb));
            }
            else if (def.Slot == AccessoryDef.EarsSlot)
            {
                EquipSlots.PaintEars(dl, thumbCentre, thumb, Look.U32(Look.CrystalPale, 0.85f));
            }
            else
            {
                EquipSlots.PaintTail(dl, thumbCentre, thumb, Look.U32(Look.CrystalPale, 0.85f));
            }
            return;
        }
        if (ctx.Capabilities.Textures.Get(catalogue.AccessoryImagePath(def)) is { } tex)
        {
            var fit = MathF.Min(thumb / Math.Max(1, def.Width), thumb / Math.Max(1, def.Height));
            var w = def.Width * fit;
            var h = def.Height * fit;
            var at = thumbTl + new Vector2((thumb - w) * 0.5f, (thumb - h) * 0.5f);
            dl.AddImage(tex, at, at + new Vector2(w, h));
            return;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Gift, thumb * 0.5f, thumbCentre, Look.U32(Look.Spark, 0.95f));
    }

    private string PrizeName(OsAppContext ctx, AetherlingWheelResultDto today)
    {
        if (today.PrizeKind == (short)StoreItemKind.AetherlingConsumable)
        {
            var element = Elements.All.FirstOrDefault(e => Elements.CrystalRef(e) == today.PrizeRef);
            return element.Key is null
                ? today.PrizeRef
                : string.Format(ctx.Localize("os.aetherling_wheel_crystal"), ctx.Localize(Elements.NameKey(element)));
        }
        return pet.Catalogue?.Accessory(today.PrizeRef)?.Name ?? today.PrizeRef;
    }

    /// <summary>Puts the prize on right here, as a whole-look write. Arms swap the hand they go in and
    /// stop following the job, the way the wardrobe does it.</summary>
    private void EquipNow(OsAppContext ctx, string itemRef)
    {
        if (_core?.Look is not { } look || pet.Catalogue is not { } catalogue || catalogue.Accessory(itemRef) is not { } def)
        {
            Close();
            return;
        }
        var worn = new List<string>(look.Accessories);
        if (worn.Contains(itemRef, StringComparer.OrdinalIgnoreCase))
        {
            Close();
            return;
        }
        worn.RemoveAll(a => catalogue.Accessory(a) is { } other && def.Displaces(other));
        if (worn.Count >= AetherlingLimits.MaxEquippedAccessories)
        {
            _error = string.Format(ctx.Localize("os.aetherling_wheel_full"), AetherlingLimits.MaxEquippedAccessories);
            _errorLeft = 4f;
            WardrobeRequested?.Invoke();
            Close();
            return;
        }
        worn.Add(itemRef);
        var followJob = look.ArmsFollowJob && def.Slot != AccessoryDef.ArmsSlot;
        var next = new AetherlingLookDto(look.Palette, [.. worn], look.Reaction, followJob, look.DisabledReactions);
        _busy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.SetLookAsync(next).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingLook, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }

    private void Reveal()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var wheel = await host.RevealWheelAsync().ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingWheel, wheel);
            }
            catch (Exception)
            {
            }
        });
    }

    private void PlayOneShot(OsAppContext ctx, string file, float level, float pitch)
    {
        if (host.SoundsMuted)
        {
            return;
        }
        ctx.Capabilities.Audio.Play(Path.Combine(host.SoundRoot, file), host.SoundVolume * level, pitch);
    }

    private void DrainPending(OsAppContext ctx)
    {
        if (Interlocked.Exchange(ref _pendingWheel, null) is { } wheel)
        {
            _wheel = wheel;
            if (_phase == Phase.Loading)
            {
                _phase = Phase.Idle;
            }
        }
        if (Interlocked.Exchange(ref _pendingSpin, null) is { } spun)
        {
            _wheel = spun;
            Spun?.Invoke(spun);
            if (spun.Today is { } today && _phase is Phase.Countdown or Phase.Spinning)
            {
                if (ctx.ReduceMotion)
                {
                    _thetaTarget = MathF.Tau - ((today.WedgeIndex + 0.5f) * Wedge);
                    _theta = _thetaTarget;
                    _target = today.WedgeIndex;
                    _omega = 0f;
                    EnterPrize();
                }
                else
                {
                    _target = today.WedgeIndex;
                }
            }
        }
        if (_phase == Phase.Landed && !_cheered)
        {
            _cheered = true;
            _busy = false;
            _confetti.Reset();
            pet.Celebrate();
            pet.AuditionGlyph("burst");
        }
        if (Interlocked.Exchange(ref _pendingLook, null) is { } dto)
        {
            _busy = false;
            _core = dto;
            LookSaved?.Invoke(dto);
            Close();
        }
        if (Interlocked.Exchange(ref _pendingError, null) is { } error)
        {
            _busy = false;
            _error = error.Length == 0 ? ctx.Localize("os.aetherling_wheel_error") : error;
            _errorLeft = 4f;
            if (_phase == Phase.Loading)
            {
                _phase = Phase.Idle;
            }
            else if (_phase is Phase.Countdown or Phase.Spinning && !_decelerating)
            {
                _windingDown = true;
                _phaseT = 0f;
            }
        }
    }
}
