using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.Shared.Aetherling;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Screens.Games.LumiLink;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.PetKit.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Aetherling.Screens.Games.Gyre;

/// <summary>Gyre's explainer in the same voice as Lumi-Link's: backdrop, halo, pips and a soft pill,
/// with the game's real marbles doing the teaching. The first page hands the player a short chain and
/// waits for them to shoot the match themselves; the second replays the slam, the powerup, the dud and
/// the fissure on loop; the third reads the creature's actual unlocked elements; the last explains
/// lives, the ladder and The Core. It carries the action it interrupted, so the last button both closes
/// it and starts the run asked for.</summary>
internal sealed class GyreGuide
{
    private const int Pages = 6;
    private const float DemoCycleSeconds = 4.2f;

    private int _page;
    private double _pageShown;
    private Action? _then;

    private static readonly Vector4[] KindColours = GyrePieces.KindColours;

    public bool Active { get; private set; }

    public void Show(Action? then)
    {
        Active = true;
        _page = 0;
        _then = then;
        _pageShown = ImGui.GetTime();
        ResetDemos();
    }

    public void Dismiss()
    {
        Active = false;
        _then = null;
    }

    private void ResetDemos()
    {
    }

    public void Draw(OsAppContext ctx, Vector2 origin, Vector2 size, string assetRoot, AetherlingDto? core,
        PetRuntime? runtime = null)
    {
        if (!Active)
        {
            return;
        }
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##gyreGuide", size, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }
        var dl = ImGui.GetWindowDrawList();
        var now = ImGui.GetTime();
        var fade = ctx.ReduceMotion ? 1f : Look.EaseOut((float)(now - _pageShown) / 0.45f);

        Look.Backdrop(dl, ctx.Theme, origin, size);
        Look.Halo(dl, origin + new Vector2(size.X * 0.5f, size.Y * 0.38f), size.X * 0.7f, Look.Crystal, 0.08f * fade);

        DrawPips(dl, origin, size, now);
        DrawBack(dl, origin);

        var centreX = origin.X + (size.X * 0.5f);
        var titleY = origin.Y + Px(48f);
        Look.Centred(dl, Loc.T($"os.aetherling_gyre_guide_title_{_page}"), centreX, titleY,
            Look.U32(Look.CrystalPale, 0.95f * fade), 1.35f);
        var bodyY = titleY + (ImGui.GetTextLineHeight() * 1.35f) + Px(10f);
        var bodyW = size.X - Px(56f);
        var rows = Look.CentredWrapped(dl, Loc.T($"os.aetherling_gyre_guide_body_{_page}"), centreX, bodyY, bodyW,
            Look.U32(Look.Body, 0.85f * fade), 0.9f);
        var contentTop = bodyY + (rows * Look.LineStep(0.9f)) + Px(14f);
        var buttonTop = origin.Y + size.Y - Px(38f) - Px(30f);
        var content = new Vector2(origin.X + Px(18f), contentTop);
        var contentSize = new Vector2(size.X - Px(36f), buttonTop - contentTop - Px(12f));

        switch (_page)
        {
            case 0:
                DrawTryMe(ctx, dl, content, contentSize, assetRoot, fade);
                break;
            case 1:
                DrawShooter(ctx, dl, content, contentSize, assetRoot, runtime, now, fade);
                break;
            case 2:
                DrawHazards(ctx, dl, content, contentSize, assetRoot, now, fade);
                break;
            case 3:
                DrawPowers(ctx, dl, content, contentSize, assetRoot, core, now, fade);
                break;
            case 4:
                DrawLadder(ctx, dl, content, contentSize, assetRoot, now, fade);
                break;
            default:
                DrawTokens(ctx, dl, content, contentSize, assetRoot, fade);
                break;
        }

        DrawButton(dl, origin, size, fade);
    }

    private void DrawPips(ImDrawListPtr dl, Vector2 origin, Vector2 size, double now)
    {
        var gap = Px(14f);
        var startX = origin.X + (size.X * 0.5f) - (gap * (Pages - 1) * 0.5f);
        var y = origin.Y + Px(22f);
        for (var i = 0; i < Pages; i++)
        {
            var centre = new Vector2(startX + (i * gap), y);
            var hovered = ImGui.IsMouseHoveringRect(centre - new Vector2(Px(7f)), centre + new Vector2(Px(7f)));
            dl.AddCircleFilled(centre, Px(i == _page ? 4f : 3.2f),
                Look.U32(Look.Crystal, i <= _page ? 0.85f : hovered ? 0.5f : 0.22f), 16);
            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    GoTo(i, now);
                }
            }
        }
    }

    private void DrawBack(ImDrawListPtr dl, Vector2 origin)
    {
        var side = Px(30f);
        var tl = origin + new Vector2(Px(10f), Px(8f));
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##gyreGuideBack", new Vector2(side, side));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        dl.AddCircleFilled(tl + new Vector2(side * 0.5f), side * 0.5f,
            Look.U32(Look.Crystal, hovered ? 0.22f : 0.1f), 24);
        IconDraw.AddCentered(dl, _page == 0 ? FontAwesomeIcon.Times : FontAwesomeIcon.ChevronLeft, side * 0.42f,
            tl + new Vector2(side * 0.5f), Look.U32(Look.CrystalPale, hovered ? 1f : 0.7f));
        if (!pressed)
        {
            return;
        }
        if (_page == 0)
        {
            Dismiss();
            return;
        }
        GoTo(_page - 1, ImGui.GetTime());
    }

    private void GoTo(int page, double now)
    {
        if (page == _page)
        {
            return;
        }
        _page = page;
        _pageShown = now;
        ResetDemos();
    }

    private void DrawButton(ImDrawListPtr dl, Vector2 origin, Vector2 size, float fade)
    {
        var last = _page == Pages - 1;
        var label = Loc.T(last
            ? (_then is null ? "os.aetherling_gyre_guide_done" : "os.aetherling_game_start")
            : "os.party_intro_next");
        var height = Px(38f);
        var width = size.X - (Px(48f) * 2f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - height - Px(30f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##gyreGuideNext", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        var radius = height * 0.5f;
        dl.AddRectFilled(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal with { W = hovered ? 0.20f : 0.11f }, fade), radius);
        dl.AddRect(tl, tl + new Vector2(width, height),
            Look.U32(Look.Crystal, (hovered ? 0.75f : 0.40f) * fade), radius, ImDrawFlags.RoundCornersAll, Px(1.2f));
        Look.Centred(dl, label, tl.X + (width * 0.5f),
            tl.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f), Look.U32(Look.CrystalPale, fade));

        if (!pressed)
        {
            return;
        }
        if (!last)
        {
            GoTo(_page + 1, ImGui.GetTime());
            return;
        }
        Active = false;
        var then = _then;
        _then = null;
        then?.Invoke();
    }

    /// <summary>The shot, played out on its own: the held marble flies into the gap, three of a colour
    /// pop, and it arms again. Nothing to aim and nothing to click. A demo that waits for a click is a
    /// demo most people walk past, and the page is here to SHOW the rule, not to test it.</summary>
    private void DrawTryMe(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot, float fade)
    {
        const float Loop = 3.2f;
        const float FlyFor = 0.9f;
        const float PopFor = 0.7f;

        var marble = MathF.Min(Px(44f), avail.X / 8f);
        var arcCentre = new Vector2(tl.X + (avail.X * 0.5f), tl.Y + (avail.Y * 0.24f));
        var shooter = new Vector2(tl.X + (avail.X * 0.5f), tl.Y + (avail.Y * 0.84f));
        int[] kinds = [0, 3, 1, 1, 3, 0];

        Span<Vector2> spots = stackalloc Vector2[6];
        for (var i = 0; i < 6; i++)
        {
            var a = MathF.PI * (0.16f + (0.68f * i / 5f));
            spots[i] = arcCentre + new Vector2(MathF.Cos(a), MathF.Sin(a) * 0.55f) * (avail.X * 0.36f);
        }
        var gapAt = (spots[2] + spots[3]) * 0.5f;

        var beat = (float)((ImGui.GetTime() - _pageShown) % Loop);
        var flying = beat < FlyFor;
        var popping = beat >= FlyFor && beat < FlyFor + PopFor;

        for (var i = 0; i < 6; i++)
        {
            var isTarget = kinds[i] == 1;
            if (popping && isTarget)
            {
                var pop = (beat - FlyFor) / PopFor;
                Look.Halo(dl, spots[i], marble * (1f + pop), KindColours[1], (1f - pop) * 0.5f * fade);
                continue;
            }
            GyrePieces.Marble(ctx, dl, assetRoot, spots[i], marble, kinds[i], false, fade);
        }

        GyrePieces.Ellipse(dl, shooter + new Vector2(0f, marble * 0.5f), new Vector2(marble * 1.3f, marble * 0.5f),
            Look.U32(Look.CrystalPale, 0.4f * fade), 2f);
        if (!flying)
        {
            GyrePieces.Marble(ctx, dl, assetRoot, shooter, marble, 1, false, fade);
        }
        else
        {
            var travel = beat / FlyFor;
            GyrePieces.Marble(ctx, dl, assetRoot, Vector2.Lerp(shooter, gapAt, travel), marble * 0.95f, 1, false, fade);
            dl.AddLine(shooter, gapAt, Look.U32(KindColours[1], 0.18f * fade), 2f);
        }
    }

    /// <summary>The creature in the cradle with both marbles: the one it is about to fire and the one
    /// waiting behind it. The swap is the one control nobody finds on their own, so the page draws the two
    /// marbles trading places on a loop and says which button does it.</summary>
    private void DrawShooter(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        PetRuntime? runtime, double now, float fade)
    {
        const float Loop = 2.6f;

        var centreX = tl.X + (avail.X * 0.5f);
        var marble = MathF.Min(Px(34f), avail.X / 9f);
        var petPx = MathF.Min(avail.Y * 0.42f, avail.X * 0.42f);
        var feet = new Vector2(centreX, tl.Y + (avail.Y * 0.50f));

        if (runtime is { Ready: true })
        {
            Look.GroundGlow(dl, feet + new Vector2(0f, Px(2f)), petPx * 0.6f, petPx * 0.12f, Look.Crystal,
                0.3f * fade);
            runtime.Draw(dl, ctx.Capabilities.Textures, feet, petPx, runtime.Pose, props: false);
        }

        // The two marbles swapping, over and over: held in front, next behind, and back again.
        var beat = (float)((now - _pageShown) % Loop) / Loop;
        var swapping = beat > 0.55f;
        var swap = swapping ? MathF.Min(1f, (beat - 0.55f) / 0.25f) : 0f;
        var eased = swap * swap * (3f - (2f * swap));

        var heldAt = new Vector2(centreX, feet.Y + Px(26f));
        var nextAt = new Vector2(centreX + (marble * 1.8f), feet.Y + Px(12f));
        GyrePieces.Ellipse(dl, heldAt + new Vector2(0f, marble * 0.5f),
            new Vector2(marble * 1.25f, marble * 0.45f), Look.U32(Look.CrystalPale, 0.4f * fade), 2f);

        var a = Vector2.Lerp(heldAt, nextAt, eased);
        var b = Vector2.Lerp(nextAt, heldAt, eased);
        GyrePieces.Marble(ctx, dl, assetRoot, b, marble, 1, false, fade);
        GyrePieces.Marble(ctx, dl, assetRoot, a, marble * 0.72f, 4, false, 0.9f * fade);

        var labelY = MathF.Max(nextAt.Y, heldAt.Y) + marble + Px(14f);
        Look.Centred(dl, Loc.T("os.aetherling_gyre_guide_lbl_swap"), centreX, labelY,
            Look.U32(Look.CrystalPale, fade), 0.95f);
        Look.CentredWrapped(dl, Loc.T("os.aetherling_gyre_guide_swap_tip"), centreX,
            labelY + Look.LineStep(0.95f), avail.X - Px(20f), Look.U32(Look.Body, 0.9f * fade), 0.85f);
    }

    /// <summary>The two things the chain does that nobody guesses: ends slamming together after a pop,
    /// and grey marbles, which never match and have to be stranded rather than shot. Both are on screen at
    /// once, each with the sentence that says what it means: a carousel made the reader wait to find out
    /// whether there was anything else, and a label alone ("The slam") explains nothing.</summary>
    private void DrawHazards(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        double now, float fade)
    {
        var t = (float)((now - _pageShown) % DemoCycleSeconds) / DemoCycleSeconds;
        var marble = MathF.Min(Px(30f), avail.X / 11f);
        var centreX = tl.X + (avail.X * 0.5f);
        var wrapW = avail.X - Px(20f);

        // The slam: a pop has left a gap, the two ends run together, and the matching pair goes as well.
        var slamY = tl.Y + (avail.Y * 0.16f);
        var gap = MathF.Max(0f, (0.5f - t) * avail.X * 0.26f);
        for (var i = 0; i < 3; i++)
        {
            GyrePieces.Marble(ctx, dl, assetRoot,
                new Vector2(centreX - gap - (marble * (1.05f * (i + 0.5f))), slamY), marble,
                i == 0 ? 4 : 2, false, fade, (float)now * 40f);
        }
        for (var i = 0; i < 2; i++)
        {
            GyrePieces.Marble(ctx, dl, assetRoot,
                new Vector2(centreX + gap + (marble * (1.05f * (i + 0.5f))), slamY), marble,
                i == 1 ? 4 : 2, false, fade, (float)now * -40f);
        }
        if (t >= 0.5f && t < 0.75f)
        {
            var ringT = (t - 0.5f) / 0.25f;
            dl.AddCircle(new Vector2(centreX, slamY), marble * (0.7f + (ringT * 2f)),
                Look.U32(KindColours[2], (1f - ringT) * 0.8f * fade), 28, 3f);
        }
        var slamTextY = slamY + marble + Px(10f);
        Look.Centred(dl, Loc.T("os.aetherling_gyre_guide_lbl_slam"), centreX, slamTextY,
            Look.U32(Look.CrystalPale, fade), 0.95f);
        Look.CentredWrapped(dl, Loc.T("os.aetherling_gyre_guide_slam_tip"), centreX,
            slamTextY + Look.LineStep(0.95f), wrapW, Look.U32(Look.Body, 0.9f * fade), 0.85f);

        // Grey marbles: the ones around them go, and then they crumble on their own.
        var dudY = tl.Y + (avail.Y * 0.60f);
        var crumbling = t > 0.6f;
        for (var i = 0; i < 5; i++)
        {
            var dud = i is 2 or 3;
            if (!dud && crumbling)
            {
                continue;
            }
            var alpha = dud && t > 0.82f ? fade * MathF.Max(0f, 1f - ((t - 0.82f) / 0.18f)) : fade;
            GyrePieces.Marble(ctx, dl, assetRoot,
                new Vector2(centreX + (marble * 1.05f * (i - 2)), dudY), marble, dud ? 0 : 5, dud, alpha);
        }
        var dudTextY = dudY + marble + Px(10f);
        Look.Centred(dl, Loc.T("os.aetherling_gyre_guide_lbl_dud"), centreX, dudTextY,
            Look.U32(Look.CrystalPale, fade), 0.95f);
        Look.CentredWrapped(dl, Loc.T("os.aetherling_gyre_guide_dud_tip"), centreX,
            dudTextY + Look.LineStep(0.95f), wrapW, Look.U32(Look.Body, 0.9f * fade), 0.85f);
    }

    /// <summary>The six element powers as a list, read top to bottom, the boost tokens' shape: the
    /// creature's own crystal, its name, and the one thing the power does. Locked ones are dimmed and
    /// carry a lock rather than being hidden, so the page says what there is to earn. It used to make the
    /// reader tap a disc to find out anything at all.</summary>
    private void DrawPowers(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        AetherlingDto? core, double now, float fade)
    {
        var rowH = MathF.Min(Px(48f), avail.Y / 6f);
        var discR = MathF.Min(Px(15f), rowH * 0.34f);
        var textX = tl.X + Px(44f);
        var wrapW = avail.X - Px(50f);
        for (var i = 0; i < 6; i++)
        {
            var element = LumiLinkGame.ElementOrder[i];
            var unlocked = LumiLinkGame.ElementUnlocked(core, element);
            var rowY = tl.Y + (i * rowH);
            var c = new Vector2(tl.X + Px(20f), rowY + (rowH * 0.42f));
            var alpha = (unlocked ? 1f : 0.55f) * fade;

            dl.AddCircleFilled(c, discR, Look.U32(new Vector4(1f, 1f, 1f, unlocked ? 0.14f : 0.05f), fade), 24);
            dl.AddCircle(c, discR, Look.U32(KindColours[i] with { W = unlocked ? 0.6f : 0.2f }, fade), 24, 1.2f);
            var icon = ctx.Capabilities.Textures.Get(
                System.IO.Path.Combine(assetRoot, "crystals", GyrePieces.Elements[i] + ".png"));
            if (icon is { } handle)
            {
                var half = discR * 0.78f;
                var tint = unlocked
                    ? Look.U32(new Vector4(1f, 1f, 1f, fade))
                    : Look.U32(new Vector4(0.5f, 0.5f, 0.55f, 0.5f * fade));
                dl.AddImage(handle, c - new Vector2(half), c + new Vector2(half), Vector2.Zero, Vector2.One, tint);
            }
            if (!unlocked)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.Lock, discR * 0.8f,
                    c + new Vector2(discR * 0.7f, discR * 0.7f), Look.U32(new Vector4(1f, 1f, 1f, 0.8f * fade)));
            }

            var name = GyrePieces.Elements[i];
            dl.AddText(new Vector2(textX, rowY + Px(2f)),
                Look.U32(KindColours[i] with { W = alpha }), Loc.T($"os.aetherling_element_{name}"));
            Look.LeftWrapped(dl, Loc.T($"os.aetherling_gyre_power_{name}_short"), textX,
                rowY + Px(2f) + Look.LineStep(1f), wrapW, Look.U32(Look.Body, 0.85f * alpha), 0.82f);
        }
    }

    /// <summary>What the hole costs, played out: marbles roll into it one after another and the health bar
    /// drops a notch for each, down to empty and the words for it. The page used to carry a twenty-step
    /// ladder and the name of the last stage, which explained neither.</summary>
    private void DrawLadder(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        double now, float fade)
    {
        const float Loop = 5f;
        const int Notches = 5;

        var centreX = tl.X + (avail.X * 0.5f);
        var beat = (float)((now - _pageShown) % Loop);
        var taken = Math.Clamp((int)(beat / (Loop / (Notches + 1))), 0, Notches);
        var empty = taken >= Notches;

        // The bar, drawn as the run wears it: a heart naming it, a trough, and a fill that turns as it goes.
        var barW = MathF.Min(Px(170f), avail.X - Px(70f));
        var barH = Px(14f);
        var barTl = new Vector2(centreX - (barW * 0.5f) + Px(9f), tl.Y + (avail.Y * 0.10f));
        var frac = 1f - (taken / (float)Notches);
        var tone = frac > 0.5f
            ? new Vector4(0.45f, 0.85f, 0.45f, 1f)
            : Vector4.Lerp(new Vector4(0.92f, 0.28f, 0.28f, 1f), new Vector4(0.95f, 0.78f, 0.30f, 1f), frac * 2f);
        dl.AddRectFilled(barTl, barTl + new Vector2(barW, barH),
            Look.U32(new Vector4(0f, 0f, 0f, 0.45f * fade)), barH * 0.5f);
        if (frac > 0f)
        {
            dl.AddRectFilled(barTl, barTl + new Vector2(barW * frac, barH), Look.U32(tone with { W = fade }),
                barH * 0.5f);
        }
        for (var i = 1; i < Notches; i++)
        {
            var x = barTl.X + (barW * i / Notches);
            dl.AddLine(new Vector2(x, barTl.Y), new Vector2(x, barTl.Y + barH),
                Look.U32(new Vector4(0f, 0f, 0f, 0.35f * fade)), 1f);
        }
        dl.AddRect(barTl, barTl + new Vector2(barW, barH),
            Look.U32(new Vector4(1f, 1f, 1f, 0.4f * fade)), barH * 0.5f, ImDrawFlags.RoundCornersAll, 1.2f);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Heart, Px(14f),
            new Vector2(barTl.X - Px(14f), barTl.Y + (barH * 0.5f)),
            Look.U32(new Vector4(1f, 0.55f, 0.65f, fade)));

        if (empty)
        {
            Look.Centred(dl, Loc.T("os.aetherling_gyre_guide_game_over"), centreX,
                barTl.Y + barH + Px(12f), Look.U32(new Vector4(0.95f, 0.4f, 0.4f, fade)), 1.05f);
        }

        // The track and the hole at the end of it, with the marbles crawling in.
        var trackY = tl.Y + (avail.Y * 0.58f);
        var marble = MathF.Min(Px(22f), avail.X / 14f);
        var holeAt = new Vector2(centreX + (avail.X * 0.30f), trackY);
        dl.AddLine(new Vector2(tl.X + Px(10f), trackY), holeAt,
            Look.U32(new Vector4(1f, 1f, 1f, 0.10f * fade)), marble * 1.5f);
        GyrePieces.EllipseFilled(dl, holeAt, new Vector2(marble * 1.25f, marble * 0.72f),
            Look.U32(new Vector4(0.03f, 0.02f, 0.08f, 0.95f * fade)));
        Look.Halo(dl, holeAt, marble * 2.4f, new Vector4(1f, 0.3f, 0.28f, 1f), 0.30f * fade, 3);

        // One marble is always on its way in; the ones behind it wait their turn.
        var step = (beat % (Loop / (Notches + 1))) / (Loop / (Notches + 1));
        for (var i = 0; i < 4; i++)
        {
            var slot = i - step;
            var at = holeAt - new Vector2(marble * 1.35f * (slot + 0.4f), 0f);
            var alpha = i == 0 ? fade * MathF.Max(0f, 1f - step) : fade;
            GyrePieces.Marble(ctx, dl, assetRoot, at, marble * (i == 0 ? 1f - (step * 0.35f) : 1f),
                (i + 2) % 5, false, alpha, (float)now * 60f);
        }

        Look.Centred(dl, Loc.T("os.aetherling_gyre_guide_lbl_fissure"), centreX,
            trackY + marble + Px(14f), Look.U32(Look.Whisper, 0.9f * fade), 0.9f);
    }

    /// <summary>The six boost tokens, each with the one thing it does. They ride IN the chain and are taken
    /// by matching them out, so a player meets them without being told what any of them is: this is where
    /// they are told, in the order the enum keeps them.</summary>
    private void DrawTokens(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        float fade)
    {
        // Sorted by the marble colour each one wears, red first: a list of six reads as a list when the
        // eye can run down the colours. Threadneedle is the red one and Sparkfall the orange one by
        // owner's pick; the rest fill the remaining colours in wheel order.
        (string Name, int Kind)[] tokens =
        [
            ("threadneedle", 0),
            ("sparkfall", 5),
            ("aetherlight", 4),
            ("driftmoss", 3),
            ("recoil", 1),
            ("shatterstone", 2),
        ];

        var rowH = MathF.Min(Px(52f), avail.Y / tokens.Length);
        var marble = MathF.Min(Px(26f), rowH * 0.62f);
        var textX = tl.X + Px(46f);
        var wrapW = avail.X - Px(52f);
        for (var i = 0; i < tokens.Length; i++)
        {
            var (name, kind) = tokens[i];
            var rowY = tl.Y + (i * rowH);
            var power = Enum.Parse<GyrePowerup>(name, ignoreCase: true);
            GyrePieces.Marble(ctx, dl, assetRoot, new Vector2(tl.X + Px(20f), rowY + (rowH * 0.5f)),
                marble, kind, false, fade, 0f, power);
            dl.AddText(new Vector2(textX, rowY + Px(4f)),
                Look.U32(Look.CrystalPale, fade), Loc.T($"os.aetherling_gyre_pu_{name}"));
            Look.LeftWrapped(dl, Loc.T($"os.aetherling_gyre_pu_{name}_tip"), textX,
                rowY + Px(4f) + Look.LineStep(1f), wrapW, Look.U32(Look.Body, 0.85f * fade), 0.82f);
        }
    }
}
