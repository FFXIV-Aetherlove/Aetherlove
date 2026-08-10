using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Apps.Aetherling.Engine;
using AetherOS.Apps.Aetherling.Rendering;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Aetherling.Screens;

/// <summary>Where it lives once it is out. One fitted page, never a scroller: a header, the stage it sits on,
/// and a line telling you how it seems. Feeding, dressing and growing are not built; this is the room.</summary>
internal sealed class PetScreen(IAetherlingHost host, PetRuntime pet)
{
    private const string MenuId = "##aetherlingPetMenu";

    /// <summary>How long after the birth the furniture arrives, so the card does not land on the pop.</summary>
    private const float SettleSeconds = 0.7f;

    /// <summary>The ceremony ends with it standing where the crystal was, which is not where it lives. It
    /// hops down in two, and the tail after the second landing is the squash.</summary>
    private const float ArriveSeconds = 1.25f;
    private static readonly float[] HopEnds = [0.44f, 0.86f];
    private static readonly float[] HopTravel = [0.55f, 1f];

    /// <summary>Height and sideways lean of a hop, as a fraction of the creature's own size: a pixel count
    /// would only ever suit the one phone scale it was measured on.</summary>
    private const float HopArc = 0.34f;
    private const float HopSway = 0.13f;

    private AetherlingDto? _core;
    private double _lastFrameTime;
    private float _settle = 1f;
    private float _arrive = 1f;
    private int _arriveHop = -1;

    private bool _namingOpen;
    private bool _namingConfirmLeave;
    private string _nameBuffer = string.Empty;
    private bool _nameFocusPending;
    private bool _busy;
    private string? _error;

    private AetherlingDto? _pendingNamed;
    private string? _pendingError;

    /// <summary>Whether the player has been told what any of this is. Until they have, the page carries one
    /// button and nothing else asks for attention.</summary>
    public bool IntroSeen { get; set; }

    /// <summary>Raised by the "what is this" button, and by the menu's tour row.</summary>
    public event Action? IntroRequested;

    public event Action? AboutRequested;

    public event Action? SettingsRequested;

    public void OnShow(AetherlingDto? core, bool justBorn)
    {
        _lastFrameTime = ImGui.GetTime();
        _core = core;
        _settle = justBorn ? 0f : 1f;
        _arrive = justBorn ? 0f : 1f;
        _arriveHop = -1;
        _error = null;
        _namingConfirmLeave = false;
        if (justBorn)
        {
            pet.Celebrate();
        }
    }

    public void Apply(AetherlingDto? core)
    {
        if (core is not null)
        {
            _core = core;
        }
    }

    public void Draw(OsAppContext ctx)
    {
        pet.EnsureLoaded(host.AssetRoot);

        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var now = ImGui.GetTime();
        var dt = Math.Clamp((float)(now - _lastFrameTime), 0f, 0.25f);
        _lastFrameTime = now;

        DrainPending();

        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void));
        if (_core is not { } core)
        {
            return;
        }

        pet.Tick(ctx.ReduceMotion);

        if (_arrive < 1f)
        {
            _arrive = ctx.ReduceMotion ? 1f : MathF.Min(1f, _arrive + (dt / ArriveSeconds));
            var hop = _arrive < HopEnds[0] ? 0 : 1;
            if (hop != _arriveHop && _arrive < HopEnds[^1])
            {
                _arriveHop = hop;
                pet.PlayHopClip();
            }
        }
        else if (_settle < 1f)
        {
            _settle = MathF.Min(1f, _settle + (dt / SettleSeconds));
            if (_settle >= 1f && !core.NameChosen)
            {
                OpenNaming(core);
            }
        }

        var pad = Px(18f);
        var headerY = origin.Y + Px(16f);
        var name = core.PetName ?? AetherlingLimits.DefaultName;
        using (ctx.TitleFont?.Push())
        {
            dl.AddText(new Vector2(origin.X + pad, headerY), Look.U32(Look.CrystalPale), name);
        }
        var born = (core.HatchedAtUtc ?? core.CreatedAtUtc).ToLocalTime().ToString("d MMM yyyy");
        dl.AddText(new Vector2(origin.X + pad, headerY + Px(40f)), Look.U32(Look.Whisper, 0.85f),
            string.Format(ctx.Localize("os.aetherling_kindled_on"), born));

        var stageTop = headerY + Px(70f);
        var stageBottom = origin.Y + size.Y - Px(58f);
        var stage = new Vector2(size.X - (pad * 2f), MathF.Max(Px(150f), stageBottom - stageTop));
        var stageTl = new Vector2(origin.X + pad, stageTop);

        // Submitted before the card underneath it, or the card's own whole-area target takes the click.
        if (!IntroSeen && !_namingOpen && _arrive >= 1f)
        {
            DrawIntroButton(ctx, dl, stageTl, stage, now);
        }
        DrawMenu(ctx, headerY + Px(14f), size.X, name);
        DrawStage(ctx, dl, stageTl, stage, origin, size, core);

        var hint = ctx.Localize("os.aetherling_tap_hint");
        Look.Centred(dl, hint, origin.X + (size.X * 0.5f), stageTl.Y + stage.Y + Px(12f),
            Look.U32(Look.Whisper, 0.7f * _settle), 0.9f);

        if (!core.NameChosen && !_namingOpen && _settle >= 1f)
        {
            DrawNameChip(ctx, dl, origin, size, core);
        }
        if (_namingOpen)
        {
            DrawNamingCard(ctx, dl, origin, size);
        }
    }

    /// <summary>The one way in to the explanation, sitting over its head until it has been read. It breathes
    /// rather than shouts: the page is quiet by design and this is the only thing on it asking to be pressed.</summary>
    private void DrawIntroButton(OsAppContext ctx, ImDrawListPtr dl, Vector2 stageTl, Vector2 stage, double now)
    {
        const float LabelScale = 1.2f;
        var label = ctx.Localize("os.aetherling_what_is_this");
        var height = Px(48f);
        var width = (ImGui.CalcTextSize(label).X * LabelScale) + Px(58f);
        var tl = new Vector2(
            stageTl.X + ((stage.X - width) * 0.5f),
            stageTl.Y + ((stage.Y - height) * 0.5f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingWhatIsThis", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var pulse = ctx.ReduceMotion ? 0.5f : Look.Breathe(now, 2.6f);
        var radius = height * 0.5f;
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal with { W = (hovered ? 0.30f : 0.16f) + (0.08f * pulse) }), radius);
        dl.AddRect(tl, tl + new Vector2(width, height),
            Look.U32(Look.CrystalPale, 0.45f + (0.35f * pulse)), radius, ImDrawFlags.RoundCornersAll, Px(1.6f));
        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - (ImGui.GetTextLineHeight() * LabelScale)) * 0.5f), Look.U32(Look.CrystalPale),
            LabelScale);

        if (pressed)
        {
            IntroRequested?.Invoke();
        }
    }

    /// <summary>The app menu, the same one every other app carries.</summary>
    private void DrawMenu(OsAppContext ctx, float centreY, float width, string name)
    {
        var menuTl = AppHeader.DrawMenuButton(width, 18f, MenuId, badge: !IntroSeen, centerY: centreY);
        if (!AppHeader.BeginMenuPopup(menuTl, MenuId))
        {
            return;
        }

        var about = string.Format(ctx.Localize("os.aetherling_menu_about"), name);
        var settings = string.Format(ctx.Localize("os.aetherling_menu_settings"), name);
        var tour = ctx.Localize("os.aetherling_menu_tour");
        var w = AppHeader.MenuWidth(about, settings, tour);
        var rowH = AppHeader.MenuRowHeight();
        if (AppHeader.MenuRow(FontAwesomeIcon.Heart, about, w, rowH))
        {
            AboutRequested?.Invoke();
            ImGui.CloseCurrentPopup();
        }
        if (AppHeader.MenuRow(FontAwesomeIcon.Cog, settings, w, rowH))
        {
            SettingsRequested?.Invoke();
            ImGui.CloseCurrentPopup();
        }
        if (AppHeader.MenuRow(FontAwesomeIcon.Route, tour, w, rowH))
        {
            IntroRequested?.Invoke();
            ImGui.CloseCurrentPopup();
        }
        AppHeader.EndMenuPopup(true);
    }

    /// <summary>The card it sits on. The whole card is the target, because aiming at a small creature that
    /// hops is a chore rather than a kindness.</summary>
    private void DrawStage(
        OsAppContext ctx,
        ImDrawListPtr dl,
        Vector2 tl,
        Vector2 size,
        Vector2 window,
        Vector2 windowSize,
        AetherlingDto core)
    {
        var br = tl + size;
        var now = ImGui.GetTime();
        var centreX = tl.X + (size.X * 0.5f);
        dl.AddRectFilled(tl, br, 0x14FFFFFFu, Px(18f));
        dl.AddRect(tl, br, 0x1AFFFFFFu, Px(18f), ImDrawFlags.RoundCornersAll, Px(1f));

        if (!_namingOpen && _arrive >= 1f)
        {
            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton("##aetherlingStage", size))
            {
                Boop();
            }
            if (ImGui.IsItemHovered())
            {
                HandOnHover();
            }
        }

        // Everything ambient is clipped to the card, or the motes climb out over the header.
        dl.PushClipRect(tl, br, true);
        Look.Motes(dl, tl, size, 22, Look.Crystal, 0.30f, now, ctx.ReduceMotion);
        if (_arrive >= 1f)
        {
            DrawMoodTitle(ctx, dl, centreX, tl.Y + Px(24f), size.X - Px(36f), core, now);
        }

        var bottom = new Vector2(centreX, br.Y - Px(20f));
        var petSize = MathF.Min(size.X * 0.74f, size.Y - Px(20f));
        if (pet.Ready)
        {
            var pose = pet.Pose;
            if (_arrive < 1f)
            {
                // Its own hop offset is dropped while it is travelling: two arcs over one sprite is a stumble.
                (bottom, petSize, pose.Scale, pose.FlipX) = Arrive(
                    BirthStage.BottomCentre(window, windowSize), BirthStage.DisplaySize(windowSize),
                    bottom, petSize);
                pose.Offset = Vector2.Zero;
            }
            // The pool of light it stands in, and the rings settling out of it. Drawn under the sprite, so
            // its feet sit in the light rather than on top of it.
            var glowW = petSize * 0.52f;
            var glowH = petSize * 0.13f;
            var pulse = ctx.ReduceMotion ? 0.5f : Look.Breathe(now, 4.2f);
            // Wider than it looks: the sides fade to nothing, so the visible core is about the creature's own
            // width and the rest is falloff.
            Look.LightShaft(dl, bottom + new Vector2(0f, glowH * 0.5f), petSize * 1.9f,
                (bottom.Y - tl.Y) * 0.86f, Look.Crystal, 0.11f + (0.04f * pulse));
            Look.GroundGlow(dl, bottom, glowW * (0.94f + (0.12f * pulse)), glowH, Look.Crystal,
                0.55f + (0.20f * pulse));
            if (!ctx.ReduceMotion)
            {
                Look.GroundRipples(dl, bottom, glowW * 1.7f, glowH * 1.7f, Look.Crystal, 0.20f, now);
            }
            pet.Draw(dl, ctx.Capabilities.Textures, bottom, petSize, pose);
        }

        dl.PopClipRect();
    }

    /// <summary>How it seems, as the page's own title. It used to appear only on hover, which meant the one
    /// line of life on the page was the one line nobody saw.</summary>
    private void DrawMoodTitle(
        OsAppContext ctx, ImDrawListPtr dl, float centreX, float y, float maxWidth, AetherlingDto core, double now)
    {
        var line = string.Format(
            ctx.Localize("os.aetherling_feeling"),
            core.PetName ?? AetherlingLimits.DefaultName,
            ctx.Localize($"os.aetherling_feel_{(int)pet.Mood}"));

        // Down a size at a time until it fits on one line: a glow around a wrapped block reads as a smudge.
        var scale = 1.25f;
        while (scale > 0.85f && ImGui.CalcTextSize(line).X * scale > maxWidth)
        {
            scale -= 0.05f;
        }

        var breath = ctx.ReduceMotion ? 0.5f : Look.Breathe(now, 5.0f);
        Look.GlowText(dl, line, centreX, y, Look.U32(Look.CrystalPale, 0.92f + (0.08f * breath)), scale,
            Look.Crystal, 0.55f + (0.45f * breath));
    }

    /// <summary>Where it is on its way down, and how big. The ceremony leaves it standing where the crystal
    /// was, so without this the change of screen would set it on the floor of its card in one frame.</summary>
    private (Vector2 Bottom, float Size, Vector2 Scale, bool FlipX) Arrive(
        Vector2 from, float fromSize, Vector2 to, float toSize)
    {
        var hop = _arrive < HopEnds[0] ? 0 : 1;
        var start = hop == 0 ? 0f : HopEnds[0];
        var t = Math.Clamp((_arrive - start) / (HopEnds[hop] - start), 0f, 1f);
        var travel = Lerp(hop == 0 ? 0f : HopTravel[0], HopTravel[hop], t);

        var bottom = Vector2.Lerp(from, to, travel);
        var petSize = Lerp(fromSize, toSize, travel);

        var arc = MathF.Sin(MathF.PI * t);
        var lean = hop == 0 ? -1f : 1f;
        bottom += new Vector2(arc * petSize * HopSway * lean, -arc * petSize * HopArc * (hop == 0 ? 1f : 0.6f));

        var squash = LandingSquash(_arrive);
        return (bottom, petSize, new Vector2(1f + (0.16f * squash), 1f - (0.20f * squash)), lean < 0f);
    }

    /// <summary>A pulse of squash just after each touchdown, so it lands with weight instead of stopping dead
    /// in the air.</summary>
    private static float LandingSquash(float progress)
    {
        const float Window = 0.10f;
        var strongest = 0f;
        foreach (var at in HopEnds)
        {
            var since = progress - at;
            if (since < 0f || since > Window)
            {
                continue;
            }
            strongest = MathF.Max(strongest, 1f - (since / Window));
        }
        return strongest;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private void Boop() => pet.Boop();

    /// <summary>The quiet way back to the card for anyone who put it off, and the reason skipping is safe.</summary>
    private void DrawNameChip(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, AetherlingDto core)
    {
        var label = ctx.Localize("os.aetherling_name_chip");
        var height = Px(30f);
        var width = ImGui.CalcTextSize(label).X + Px(34f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - height - Px(16f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##aetherlingNameChip", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal with { W = hovered ? 0.22f : 0.12f }), height * 0.5f);
        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.CrystalPale, 0.9f));
        if (pressed)
        {
            OpenNaming(core);
        }
    }

    private void OpenNaming(AetherlingDto core)
    {
        _namingOpen = true;
        _namingConfirmLeave = false;
        _nameFocusPending = true;
        _nameBuffer = core.PetName ?? AetherlingLimits.DefaultName;
        _error = null;
    }

    /// <summary>The naming card. While it is up nothing else on the page is submitted, which is what makes it
    /// modal here: an ImGui item under it would otherwise still take the click.</summary>
    private void DrawNamingCard(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        dl.AddRectFilled(origin, origin + size, Look.U32(Look.Void with { W = 1f }, 0.72f));

        var pad = Px(18f);
        var cardW = size.X - (pad * 2f);
        var cardH = Px(_namingConfirmLeave ? 170f : 190f);
        var tl = new Vector2(origin.X + pad, origin.Y + ((size.Y - cardH) * 0.42f));
        var br = tl + new Vector2(cardW, cardH);
        dl.AddRectFilled(tl, br, Look.U32(new Vector4(0.10f, 0.09f, 0.16f, 0.97f)), Px(16f));
        dl.AddRect(tl, br, Look.U32(Look.Crystal, 0.35f), Px(16f), ImDrawFlags.RoundCornersAll, Px(1.2f));

        if (_namingConfirmLeave)
        {
            DrawLeaveConfirm(ctx, dl, tl, cardW, cardH);
            return;
        }

        var y = tl.Y + Px(16f);
        Look.Centred(dl, ctx.Localize("os.aetherling_naming_title"), tl.X + (cardW * 0.5f), y,
            Look.U32(Look.CrystalPale), 1.15f);
        y += Px(30f);
        Look.CentredWrapped(dl, ctx.Localize("os.aetherling_naming_body"), tl.X + (cardW * 0.5f), y,
            cardW - Px(28f), Look.U32(Look.Whisper, 0.9f), 0.9f);
        y += Px(34f);

        ImGui.SetCursorScreenPos(new Vector2(tl.X + Px(14f), y));
        ImGui.SetNextItemWidth(cardW - Px(28f));
        if (_nameFocusPending)
        {
            _nameFocusPending = false;
            ImGui.SetKeyboardFocusHere();
        }
        var submitted = ImGui.InputText("##aetherlingName", ref _nameBuffer,
            AetherlingLimits.NameMaxLength, ImGuiInputTextFlags.EnterReturnsTrue);
        y += Px(34f);

        if (_error is { Length: > 0 })
        {
            Look.CentredWrapped(dl, _error, tl.X + (cardW * 0.5f), y, cardW - Px(28f),
                Look.U32(new Vector4(0.95f, 0.5f, 0.5f, 1f)), 0.85f);
        }

        var buttonY = br.Y - Px(46f);
        var half = (cardW - Px(38f)) * 0.5f;
        var confirm = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(14f), buttonY), half,
            ctx.Localize("os.aetherling_naming_confirm"), primary: true);
        var later = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(24f) + half, buttonY), half,
            ctx.Localize("os.aetherling_naming_later"), primary: false);

        if ((confirm || submitted) && !_busy)
        {
            Submit();
        }
        if (later && !_busy)
        {
            _namingConfirmLeave = true;
        }
    }

    private void DrawLeaveConfirm(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float cardW, float cardH)
    {
        var y = tl.Y + Px(16f);
        Look.Centred(dl, ctx.Localize("os.aetherling_naming_later_title"), tl.X + (cardW * 0.5f), y,
            Look.U32(Look.CrystalPale), 1.1f);
        y += Px(30f);
        Look.CentredWrapped(dl, ctx.Localize("os.aetherling_naming_later_warning"), tl.X + (cardW * 0.5f), y,
            cardW - Px(28f), Look.U32(Look.Whisper, 0.9f), 0.9f);

        var buttonY = tl.Y + cardH - Px(46f);
        var half = (cardW - Px(38f)) * 0.5f;
        var back = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(14f), buttonY), half,
            ctx.Localize("os.aetherling_naming_back"), primary: true);
        var leave = DrawCardButton(ctx, dl, new Vector2(tl.X + Px(24f) + half, buttonY), half,
            ctx.Localize("os.aetherling_naming_leave"), primary: false);

        if (back)
        {
            _namingConfirmLeave = false;
            _nameFocusPending = true;
        }
        if (leave)
        {
            // Nothing is sent: the name stays what the server called it, and the chip stays offering.
            _namingOpen = false;
            _namingConfirmLeave = false;
        }
    }

    private bool DrawCardButton(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, float width, string label, bool primary)
    {
        var height = Px(34f);
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton($"##aetherlingCard{label}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered && !_busy)
        {
            HandOnHover();
        }
        var fill = primary
            ? Look.Crystal with { W = hovered ? 0.30f : 0.20f }
            : new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.07f);
        dl.AddRectFilled(tl, tl + new Vector2(width, height), Look.U32(fill), height * 0.5f);
        if (_busy && primary)
        {
            LoadingSpinner.Draw(tl + new Vector2(width * 0.5f, height * 0.5f), Px(8f), Px(2.2f),
                Look.U32(Look.CrystalPale));
            return false;
        }
        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f),
            Look.U32(primary ? Look.CrystalPale : Look.Whisper));
        _ = ctx;
        return pressed && !_busy;
    }

    private void Submit()
    {
        var name = _nameBuffer;
        _busy = true;
        _error = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await host.NameAsync(name).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingNamed, dto);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _pendingError, host.DescribeError(ex));
            }
        });
    }

    /// <summary>Takes what the naming round trip left. Called from Draw, because accepting a name spawns
    /// particles and closes an overlay the draw thread owns.</summary>
    private void DrainPending()
    {
        if (Interlocked.Exchange(ref _pendingNamed, null) is { } named)
        {
            _busy = false;
            _core = named;
            _namingOpen = false;
            _namingConfirmLeave = false;
            pet.Celebrate();
        }
        if (Interlocked.Exchange(ref _pendingError, null) is { } message)
        {
            _busy = false;
            _error = message;
        }
    }
}
