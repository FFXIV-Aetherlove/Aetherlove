using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.Shared.Aetherling;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Screens.Games.Gyre;
using AetherOS.Apps.Aetherling.Screens.Games.LumiLink;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.PetKit.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Aetherling.Screens.Games.AetherPop;

/// <summary>Aether Pop's explainer in the Lumi-Link guide's shell: backdrop, halo, pips and a soft pill,
/// with the game's own bubbles doing the teaching. Six pages, none of which waits for the player: the
/// shot banking into a pop, a cluster cut loose, the compactor and the line, the three specials, the
/// six powers read off the creature's real unlocks, and the ladder with the swap tip. It carries the
/// action it interrupted, so the last button both closes it and starts the run asked for.</summary>
internal sealed class PopGuide
{
    private const int Pages = 6;

    private int _page;
    private double _pageShown;
    private Action? _then;

    private static readonly Vector4[] KindColours = PopPieces.KindColours;

    public bool Active { get; private set; }

    public void Show(Action? then)
    {
        Active = true;
        _page = 0;
        _then = then;
        _pageShown = ImGui.GetTime();
    }

    public void Dismiss()
    {
        Active = false;
        _then = null;
    }

    public void Draw(OsAppContext ctx, Vector2 origin, Vector2 size, string assetRoot, AetherlingDto? core,
        PetRuntime? runtime = null)
    {
        if (!Active)
        {
            return;
        }
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##popGuide", size, false,
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
        Look.Centred(dl, Loc.T($"os.aetherling_pop_guide_title_{_page}"), centreX, titleY,
            Look.U32(Look.CrystalPale, 0.95f * fade), 1.35f);
        var bodyY = titleY + (ImGui.GetTextLineHeight() * 1.35f) + Px(10f);
        var bodyW = size.X - Px(56f);
        var rows = Look.CentredWrapped(dl, Loc.T($"os.aetherling_pop_guide_body_{_page}"), centreX, bodyY, bodyW,
            Look.U32(Look.Body, 0.85f * fade), 0.9f);
        var contentTop = bodyY + (rows * Look.LineStep(0.9f)) + Px(14f);
        var buttonTop = origin.Y + size.Y - Px(38f) - Px(30f);
        var content = new Vector2(origin.X + Px(18f), contentTop);
        var contentSize = new Vector2(size.X - Px(36f), buttonTop - contentTop - Px(12f));

        switch (_page)
        {
            case 0:
                DrawShot(ctx, dl, content, contentSize, assetRoot, now, fade);
                break;
            case 1:
                DrawDrop(ctx, dl, content, contentSize, assetRoot, now, fade);
                break;
            case 2:
                DrawCompactor(ctx, dl, content, contentSize, assetRoot, runtime, now, fade);
                break;
            case 3:
                DrawSpecials(ctx, dl, content, contentSize, assetRoot, now, fade);
                break;
            case 4:
                DrawPowers(ctx, dl, content, contentSize, assetRoot, core, fade);
                break;
            default:
                DrawLadder(ctx, dl, content, contentSize, assetRoot, runtime, now, fade);
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
        var pressed = ImGui.InvisibleButton("##popGuideBack", new Vector2(side, side));
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
    }

    private void DrawButton(ImDrawListPtr dl, Vector2 origin, Vector2 size, float fade)
    {
        var last = _page == Pages - 1;
        var label = Loc.T(last
            ? (_then is null ? "os.aetherling_pop_guide_done" : "os.aetherling_game_start")
            : "os.party_intro_next");
        var height = Px(38f);
        var width = size.X - (Px(48f) * 2f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - height - Px(30f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##popGuideNext", new Vector2(width, height));
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

    /// <summary>A small hanging cluster, and the shot banking off the wall into the two blues: the bounce
    /// is the one thing a first-timer never tries on their own.</summary>
    private void DrawShot(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot, double now, float fade)
    {
        const float Loop = 3.4f;
        const float FlyFor = 1.1f;
        const float PopFor = 0.6f;

        var d = MathF.Min(Px(38f), avail.X / 8.5f);
        var left = tl.X + ((avail.X - (d * 7f)) * 0.5f);
        var top = tl.Y + Px(6f);
        int[][] rows =
        [
            [0, 0, 3, 3, 1, 4, 4],
            [3, 0, 1, 1, 4, 3],
        ];
        var shooter = new Vector2(tl.X + (avail.X * 0.5f), tl.Y + (avail.Y * 0.88f));
        var wall = new Vector2(left - (d * 0.5f), 0f);
        var target = new Vector2(left + (d * 1.5f), top + (d * 0.866f * 2f));
        var bounceAt = new Vector2(wall.X + (d * 0.5f), (shooter.Y + target.Y) * 0.5f);

        var beat = (float)((now - _pageShown) % Loop);
        var flying = beat < FlyFor;
        var popping = beat >= FlyFor && beat < FlyFor + PopFor;

        dl.AddLine(new Vector2(wall.X, top - d), new Vector2(wall.X, shooter.Y - d), Look.U32(Look.CrystalPale, 0.4f * fade), 2f);
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var at = new Vector2(left + (c * d) + (r == 1 ? d * 0.5f : 0f), top + (r * d * 0.866f));
                var kind = rows[r][c];
                var isTarget = kind == 1;
                if (isTarget && beat >= FlyFor)
                {
                    if (popping)
                    {
                        var pop = (beat - FlyFor) / PopFor;
                        Look.Halo(dl, at, d * (0.6f + pop), KindColours[1], (1f - pop) * 0.5f * fade);
                    }
                    continue;
                }
                GyrePieces.Marble(ctx, dl, assetRoot, at, d * 0.94f, kind, false, fade);
            }
        }

        GyrePieces.Ellipse(dl, shooter + new Vector2(0f, d * 0.5f), new Vector2(d * 1.3f, d * 0.5f),
            Look.U32(Look.CrystalPale, 0.4f * fade), 2f);
        if (!flying)
        {
            if (!popping)
            {
                GyrePieces.Marble(ctx, dl, assetRoot, shooter, d * 0.94f, 1, false, fade);
            }
            return;
        }
        var travel = beat / FlyFor;
        var at2 = travel < 0.5f
            ? Vector2.Lerp(shooter, bounceAt, travel * 2f)
            : Vector2.Lerp(bounceAt, target, (travel - 0.5f) * 2f);
        dl.AddLine(shooter, bounceAt, Look.U32(KindColours[1], 0.2f * fade), 2f);
        dl.AddLine(bounceAt, target, Look.U32(KindColours[1], 0.2f * fade), 2f);
        GyrePieces.Marble(ctx, dl, assetRoot, at2, d * 0.9f, 1, false, fade, travel * 300f);
    }

    /// <summary>A cluster hanging from two reds in the top-left corner, and the shot banking off the right
    /// wall to reach them over an empty stretch of the top row: the reds go, the cluster falls, and the
    /// doubled score counts up beside it. The bank is the point: a straight shot would have to pass
    /// through the cluster it is meant to cut loose.</summary>
    private void DrawDrop(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot, double now, float fade)
    {
        const float Loop = 4.2f;
        const float FlyFor = 1.2f;

        var d = MathF.Min(Px(38f), avail.X / 8.5f);
        var left = tl.X + ((avail.X - (d * 7f)) * 0.5f);
        var top = tl.Y + Px(6f);
        var wallX = left + (d * 7.5f);
        var shooter = new Vector2(left + (d * 3.5f), top + (d * 5.2f));
        var beat = (float)((now - _pageShown) % Loop);
        var landed = beat >= FlyFor;
        var fall = landed ? MathF.Min(1f, (beat - FlyFor) / 1.2f) : 0f;

        // The top row: two reds on the left, two blues far right, nothing between; the cluster hangs
        // from the reds alone.
        int[] row0 = [0, 0, -1, -1, -1, -1, 3, 3];
        int[] row1 = [2, -1, -1, -1, -1, -1, -1];
        int[] row2 = [5, 2, -1, -1, -1, -1, -1, -1];
        int[] row3 = [5, -1, -1, -1, -1, -1, -1];
        var target = new Vector2(left + (d * 1f), top);
        // A real bank: the bounce point is where the line to the target's mirror image crosses the wall,
        // so the angle in equals the angle out.
        var railX = wallX - (d * 0.47f);
        var mirrorX = (2f * railX) - target.X;
        var bounceAt = new Vector2(railX, shooter.Y + ((target.Y - shooter.Y) * ((railX - shooter.X) / (mirrorX - shooter.X))));

        dl.AddLine(new Vector2(wallX, top - d), new Vector2(wallX, shooter.Y - d), Look.U32(Look.CrystalPale, 0.4f * fade), 2f);

        for (var c = 0; c < row0.Length; c++)
        {
            if (row0[c] < 0)
            {
                continue;
            }
            var at = new Vector2(left + (c * d), top);
            if (row0[c] == 0 && landed)
            {
                if (fall < 0.3f)
                {
                    Look.Halo(dl, at, d * (0.6f + fall), KindColours[0], (1f - (fall / 0.3f)) * 0.5f * fade);
                }
                continue;
            }
            GyrePieces.Marble(ctx, dl, assetRoot, at, d * 0.94f, row0[c], false, fade);
        }

        var drop = fall * fall * (avail.Y * 0.6f);
        var dropAlpha = MathF.Max(0f, 1f - (MathF.Max(0f, fall - 0.6f) / 0.4f));
        void HangingRow(int[] row, int index)
        {
            var odd = (index & 1) == 1;
            for (var c = 0; c < row.Length; c++)
            {
                if (row[c] < 0)
                {
                    continue;
                }
                var at = new Vector2(left + (c * d) + (odd ? d * 0.5f : 0f), top + (index * d * 0.866f) + drop);
                GyrePieces.Marble(ctx, dl, assetRoot, at, d * 0.94f, row[c], false, fade * dropAlpha, drop * 2f);
            }
        }
        HangingRow(row1, 1);
        HangingRow(row2, 2);
        HangingRow(row3, 3);

        GyrePieces.Ellipse(dl, shooter + new Vector2(0f, d * 0.5f), new Vector2(d * 1.3f, d * 0.5f),
            Look.U32(Look.CrystalPale, 0.4f * fade), 2f);
        if (!landed)
        {
            var travel = beat / FlyFor;
            var at = travel < 0.5f
                ? Vector2.Lerp(shooter, bounceAt, travel * 2f)
                : Vector2.Lerp(bounceAt, target, (travel - 0.5f) * 2f);
            dl.AddLine(shooter, bounceAt, Look.U32(KindColours[0], 0.2f * fade), 2f);
            dl.AddLine(bounceAt, target, Look.U32(KindColours[0], 0.2f * fade), 2f);
            GyrePieces.Marble(ctx, dl, assetRoot, at, d * 0.9f, 0, false, fade, travel * 300f);
        }
        else if (fall > 0.15f)
        {
            var textAt = new Vector2(left + (d * 4f), top + (d * 2.6f));
            Look.Centred(dl, string.Format(Loc.T("os.aetherling_pop_drop"), 4, PopBoard.DropPoints(4)), textAt.X, textAt.Y,
                Look.U32(Look.Spark, fade * MathF.Min(1f, (fall - 0.15f) * 4f)), 1.2f);
        }
        Look.CentredWrapped(dl, Loc.T("os.aetherling_pop_guide_drop_tip"), left + (d * 3.5f),
            shooter.Y + (d * 1.2f), avail.X - Px(20f), Look.U32(Look.Body, 0.9f * fade), 0.85f);
    }

    /// <summary>The compactor stepping down pip by pip, the line, and the creature squashed when a bubble
    /// reaches it: the whole way to lose, on a loop.</summary>
    private void DrawCompactor(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        PetRuntime? runtime, double now, float fade)
    {
        const float Loop = 6f;
        const int Pips = 4;
        var d = MathF.Min(Px(30f), avail.X / 10f);
        var centreX = tl.X + (avail.X * 0.5f);
        var beat = (float)((now - _pageShown) % Loop);
        var steps = Math.Clamp((int)(beat / 1.2f), 0, 4);
        var squashed = steps >= 4;

        var barW = avail.X - Px(40f);
        var barH = Px(16f);
        var barTop = tl.Y + Px(4f) + (steps * d * 0.866f);
        var barTl = new Vector2(centreX - (barW * 0.5f), barTop);
        dl.AddRectFilled(barTl, barTl + new Vector2(barW, barH), Look.U32(new Vector4(0.16f, 0.15f, 0.2f, 0.97f * fade)), Px(4f));
        dl.AddRect(barTl, barTl + new Vector2(barW, barH), Look.U32(new Vector4(1f, 1f, 1f, 0.28f * fade)), Px(4f));
        var pipsTaken = (int)((beat % 1.2f) / 1.2f * (Pips + 1));
        for (var i = 0; i < Pips; i++)
        {
            var c = new Vector2(centreX - (Px(12f) * (Pips - 1) * 0.5f) + (i * Px(12f)), barTop + (barH * 0.5f));
            var lit = i < pipsTaken;
            dl.AddCircleFilled(c, Px(4f), Look.U32(lit ? (i == Pips - 1 ? new Vector4(1f, 0.32f, 0.3f, 1f) : Look.Spark) : new Vector4(1f, 1f, 1f, 0.15f), fade), 12);
        }

        // Three rows hanging under it, riding down with it.
        int[][] rows = [[2, 4, 2, 4, 2, 4], [4, 2, 4, 2, 4]];
        var gridLeft = centreX - (d * 3f);
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var at = new Vector2(gridLeft + (c * d) + (r == 1 ? d * 0.5f : 0f) + (d * 0.5f),
                    barTop + barH + (d * 0.55f) + (r * d * 0.866f));
                GyrePieces.Marble(ctx, dl, assetRoot, at, d * 0.94f, rows[r][c], false, fade);
            }
        }

        var lineY = tl.Y + Px(4f) + (d * 0.866f * 5.4f) + barH;
        var lineColour = squashed ? new Vector4(1f, 0.32f, 0.3f, 1f) : Look.CrystalPale with { W = 0.5f };
        for (var x = barTl.X; x < barTl.X + barW; x += Px(14f))
        {
            dl.AddLine(new Vector2(x, lineY), new Vector2(MathF.Min(x + Px(7f), barTl.X + barW), lineY), Look.U32(lineColour, fade), 2f);
        }
        Look.Centred(dl, Loc.T("os.aetherling_pop_guide_lbl_line"), centreX, lineY + Px(6f), Look.U32(Look.Whisper, fade), 0.85f);

        var petPx = MathF.Min(Px(64f), avail.Y * 0.22f);
        var feet = new Vector2(centreX, MathF.Min(tl.Y + avail.Y - Px(4f), lineY + Px(24f) + petPx));
        if (runtime is { Ready: true })
        {
            var pose = runtime.Pose;
            if (squashed && !ctx.ReduceMotion)
            {
                pose.Scale = new Vector2(1.35f, 0.45f);
            }
            runtime.Draw(dl, ctx.Capabilities.Textures, feet, petPx, pose, props: false);
        }
        if (squashed)
        {
            Look.Centred(dl, Loc.T("os.aetherling_pop_squashed"), centreX, feet.Y - petPx - Px(22f),
                Look.U32(new Vector4(1f, 0.32f, 0.3f, fade)), 1.05f);
        }
    }

    /// <summary>One row of a list: the piece on the left, the name and its one sentence on the right,
    /// the piece centred on the INK of the two lines. Measured rather than guessed, so a tip that wraps
    /// in German pushes the next row down instead of running into it.</summary>
    private static float ListRow(ImDrawListPtr dl, Vector2 tl, float y, float wrapW, string name, Vector4 nameColour,
        string tip, float alpha, float fade, Action<Vector2> piece)
    {
        var textX = tl.X + Px(46f);
        var nameH = ImGui.GetTextLineHeight();
        var tipH = Look.BlockHeight(tip, wrapW, 0.82f);
        var inkH = nameH + Px(2f) + tipH;
        piece(new Vector2(tl.X + Px(20f), y + (inkH * 0.5f)));
        dl.AddText(new Vector2(textX, y), Look.U32(nameColour with { W = nameColour.W * alpha * fade }), name);
        Look.LeftWrapped(dl, tip, textX, y + nameH + Px(2f), wrapW, Look.U32(Look.Body, 0.85f * alpha * fade), 0.82f);
        return inkH + Px(12f);
    }

    /// <summary>The star, the rainbow, the letters and the sleeping hatchling, each with its one sentence.</summary>
    private void DrawSpecials(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot, double now, float fade)
    {
        var d = MathF.Min(Px(34f), avail.X / 9f);
        var wrapW = avail.X - Px(52f);
        var y = tl.Y;

        y += ListRow(dl, tl, y, wrapW, Loc.T("os.aetherling_pop_guide_lbl_star"), Look.CrystalPale,
            Loc.T("os.aetherling_pop_guide_star_tip"), 1f, fade,
            at => PopPieces.Bubble(ctx, dl, assetRoot, at, d, -1, PopSpecial.Star, -1, false, now, fade));
        y += ListRow(dl, tl, y, wrapW, Loc.T("os.aetherling_pop_guide_lbl_rainbow"), Look.CrystalPale,
            Loc.T("os.aetherling_pop_guide_rainbow_tip"), 1f, fade,
            at => PopPieces.Bubble(ctx, dl, assetRoot, at, d, -1, PopSpecial.Rainbow, -1, false, now, fade));
        y += ListRow(dl, tl, y, wrapW, Loc.T("os.aetherling_pop_guide_lbl_letters"), Look.CrystalPale,
            Loc.T("os.aetherling_pop_guide_letters_tip"), 1f, fade,
            at =>
            {
                var letter = (int)((now - _pageShown) * 1.5) % PopBoard.Letters.Length;
                PopPieces.Bubble(ctx, dl, assetRoot, at, d, letter % 6, PopSpecial.Letter, letter, false, now, fade);
            });
        ListRow(dl, tl, y, wrapW, Loc.T("os.aetherling_pop_guide_lbl_lumi"), Look.CrystalPale,
            Loc.T("os.aetherling_pop_guide_lumi_tip"), 1f, fade,
            at =>
            {
                var kind = (int)((now - _pageShown) * 0.8) % 6;
                PopPieces.Bubble(ctx, dl, assetRoot, at, d, kind, PopSpecial.Lumi, -1, false, now, fade);
            });
    }

    /// <summary>The six element powers as a list: the creature's own crystal, its name, and the one thing
    /// the power does. Locked ones are dimmed and carry a lock, so the page says what there is to earn.</summary>
    private void DrawPowers(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        AetherlingDto? core, float fade)
    {
        var discR = Px(15f);
        var wrapW = avail.X - Px(52f);
        var y = tl.Y;
        for (var i = 0; i < 6; i++)
        {
            var element = LumiLinkGame.ElementOrder[i];
            var unlocked = LumiLinkGame.ElementUnlocked(core, element);
            var alpha = unlocked ? 1f : 0.55f;
            var name = PopPieces.Elements[i];
            var index = i;
            y += ListRow(dl, tl, y, wrapW, Loc.T($"os.aetherling_element_{name}"), KindColours[i],
                Loc.T($"os.aetherling_pop_power_{name}_short"), alpha, fade,
                c =>
                {
                    dl.AddCircleFilled(c, discR, Look.U32(new Vector4(1f, 1f, 1f, unlocked ? 0.14f : 0.05f), fade), 24);
                    dl.AddCircle(c, discR, Look.U32(KindColours[index] with { W = unlocked ? 0.6f : 0.2f }, fade), 24, 1.2f);
                    PopPieces.ElementIcon(ctx, dl, assetRoot, c, discR * 1.56f, index, unlocked ? fade : 0.4f * fade);
                    if (!unlocked)
                    {
                        IconDraw.AddCentered(dl, FontAwesomeIcon.Lock, discR * 0.8f,
                            c + new Vector2(discR * 0.7f, discR * 0.7f), Look.U32(new Vector4(1f, 1f, 1f, 0.8f * fade)));
                    }
                });
        }
    }

    /// <summary>Twenty pips for the twenty rounds with the last one named, and the creature with both
    /// marbles trading places under the one control nobody finds alone.</summary>
    private void DrawLadder(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        PetRuntime? runtime, double now, float fade)
    {
        const float Loop = 2.6f;
        var centreX = tl.X + (avail.X * 0.5f);

        var pipGap = MathF.Min(Px(14f), (avail.X - Px(20f)) / 20f);
        var startX = centreX - (pipGap * 19f * 0.5f);
        var pipY = tl.Y + Px(14f);
        var lit = (int)((now - _pageShown) * 4.0) % 24;
        for (var i = 0; i < 20; i++)
        {
            var c = new Vector2(startX + (i * pipGap), pipY);
            var last = i == 19;
            var colour = last ? Look.Spark : Look.Crystal;
            dl.AddCircleFilled(c, Px(last ? 5f : 3.4f), Look.U32(colour, (i <= lit ? 0.9f : 0.25f) * fade), 12);
        }
        Look.Centred(dl, Loc.T("os.aetherling_pop_guide_lbl_final"), centreX, pipY + Px(14f), Look.U32(Look.Spark, 0.9f * fade), 0.85f);

        var marble = MathF.Min(Px(34f), avail.X / 9f);
        var petPx = MathF.Min(avail.Y * 0.36f, avail.X * 0.36f);
        var feet = new Vector2(centreX, tl.Y + (avail.Y * 0.62f));
        if (runtime is { Ready: true })
        {
            Look.GroundGlow(dl, feet + new Vector2(0f, Px(2f)), petPx * 0.6f, petPx * 0.12f, Look.Crystal, 0.3f * fade);
            runtime.Draw(dl, ctx.Capabilities.Textures, feet, petPx, runtime.Pose, props: false);
        }

        var beat = (float)((now - _pageShown) % Loop) / Loop;
        var swapping = beat > 0.55f;
        var swap = swapping ? MathF.Min(1f, (beat - 0.55f) / 0.25f) : 0f;
        var eased = swap * swap * (3f - (2f * swap));
        var heldAt = new Vector2(centreX, feet.Y + Px(22f));
        var nextAt = new Vector2(centreX + (marble * 1.8f), feet.Y + Px(10f));
        var a = Vector2.Lerp(heldAt, nextAt, eased);
        var b = Vector2.Lerp(nextAt, heldAt, eased);
        GyrePieces.Marble(ctx, dl, assetRoot, b, marble, 1, false, fade);
        GyrePieces.Marble(ctx, dl, assetRoot, a, marble * 0.72f, 4, false, 0.9f * fade);

        var labelY = MathF.Max(nextAt.Y, heldAt.Y) + marble + Px(10f);
        Look.Centred(dl, Loc.T("os.aetherling_pop_guide_lbl_swap"), centreX, labelY, Look.U32(Look.CrystalPale, fade), 0.95f);
        Look.CentredWrapped(dl, Loc.T("os.aetherling_pop_guide_swap_tip"), centreX,
            labelY + Look.LineStep(0.95f), avail.X - Px(20f), Look.U32(Look.Body, 0.9f * fade), 0.85f);
    }
}
