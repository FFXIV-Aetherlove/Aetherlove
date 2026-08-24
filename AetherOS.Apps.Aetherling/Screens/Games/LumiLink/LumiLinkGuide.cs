using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.Shared.Aetherling;
using AetherLove.UI;
using AetherOS.Apps.Aetherling.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Aetherling.Screens.Games.LumiLink;

/// <summary>Lumi-Link's explainer in the Aetherling's own voice: the hatch onboarding's backdrop, halo,
/// pips and soft pill, with the game's real pieces doing the teaching. The first page hands the player
/// a board and waits for them to make a three; the second replays the four special shapes on loop; the
/// third reads the creature's actual unlocked elements; the last shows the bar and the three looks.
/// Raised the first time somebody presses play and any time after from the title's help button. It
/// carries the action it interrupted, so the last button both closes it and starts the run asked for.</summary>
internal sealed class LumiLinkGuide
{
    private const int Pages = 4;
    private const float ShowSeconds = 1.1f;
    private const float SwapSeconds = 0.38f;
    private const float HoldSeconds = 0.22f;
    private const float PopSeconds = 0.42f;
    private const float LinkedSeconds = 1.6f;
    private const float DemoCycleSeconds = 4.2f;

    private sealed class Demo
    {
        public int W;
        public int H;
        public int[] Kinds = [];
        public (int C, int R) A;
        public (int C, int R) B;
        public (int C, int R)[] Clear = [];
        public (int C, int R) Mint = (-1, -1);
        public Special MintSpecial;
        public string LabelKey = "";
    }

    private static readonly Demo TryMe = new()
    {
        W = 5,
        H = 4,
        Kinds =
        [
            3, 4, 2, 5, 1,
            2, 0, 4, 1, 2,
            0, 1, 0, 5, 3,
            4, 2, 5, 3, 1,
        ],
        A = (1, 1),
        B = (1, 2),
        Clear = [(0, 2), (1, 2), (2, 2)],
    };

    private static readonly Demo[] Specials =
    [
        new()
        {
            W = 5,
            H = 3,
            Kinds =
            [
                2, 0, 3, 4, 5,
                0, 3, 0, 0, 2,
                4, 5, 1, 2, 3,
            ],
            A = (1, 0),
            B = (1, 1),
            Clear = [(0, 1), (1, 1), (2, 1), (3, 1)],
            Mint = (1, 1),
            MintSpecial = Special.BoltRow,
            LabelKey = "os.aetherling_lumilink_guide_lbl_bolt",
        },
        new()
        {
            W = 5,
            H = 4,
            Kinds =
            [
                1, 3, 0, 4, 2,
                2, 5, 0, 1, 3,
                0, 0, 3, 0, 5,
                4, 1, 2, 5, 1,
            ],
            A = (3, 2),
            B = (2, 2),
            Clear = [(0, 2), (1, 2), (2, 2), (2, 1), (2, 0)],
            Mint = (2, 2),
            MintSpecial = Special.Burst,
            LabelKey = "os.aetherling_lumilink_guide_lbl_burst",
        },
        new()
        {
            W = 5,
            H = 4,
            Kinds =
            [
                3, 1, 0, 5, 2,
                5, 0, 4, 0, 2,
                1, 3, 0, 2, 4,
                2, 5, 0, 1, 3,
            ],
            A = (2, 0),
            B = (2, 1),
            Clear = [(1, 1), (2, 1), (3, 1), (2, 2), (2, 3)],
            Mint = (2, 1),
            MintSpecial = Special.TBurst,
            LabelKey = "os.aetherling_lumilink_guide_lbl_tburst",
        },
        new()
        {
            W = 5,
            H = 3,
            Kinds =
            [
                2, 4, 0, 1, 5,
                0, 0, 3, 0, 0,
                5, 1, 2, 4, 3,
            ],
            A = (2, 0),
            B = (2, 1),
            Clear = [(0, 1), (1, 1), (2, 1), (3, 1), (4, 1)],
            Mint = (2, 1),
            MintSpecial = Special.Prism,
            LabelKey = "os.aetherling_lumilink_guide_lbl_prism",
        },
    ];

    private int _page;
    private Action? _then;
    private double _pageShown;

    // The try-me board: armed until the player trades the pair, then it plays out and re-arms.
    private double _tryFired = -1;
    private (int C, int R)? _tryPicked;
    private bool _tryDragging;
    private int _linked;

    private int _pickedElement = -1;

    public bool Active { get; private set; }

    public void Show(Action? then)
    {
        Active = true;
        _page = 0;
        _then = then;
        _pageShown = ImGui.GetTime();
        _tryFired = -1;
        _tryPicked = null;
        _linked = 0;
        _pickedElement = -1;
    }

    public void Dismiss()
    {
        Active = false;
        _then = null;
    }

    public void Draw(OsAppContext ctx, Vector2 origin, Vector2 size, string assetRoot, AetherlingDto? core)
    {
        if (!Active)
        {
            return;
        }
        ImGui.SetCursorScreenPos(origin);
        using var layer = ImRaii.Child("##lumiLinkGuide", size, false,
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
        DrawBack(ctx, dl, origin);

        var centreX = origin.X + (size.X * 0.5f);
        var titleY = origin.Y + Px(48f);
        var title = Loc.T($"os.aetherling_lumilink_guide_title_{_page}");
        Look.Centred(dl, title, centreX, titleY, Look.U32(Look.CrystalPale, 0.95f * fade), 1.35f);
        var bodyY = titleY + (ImGui.GetTextLineHeight() * 1.35f) + Px(10f);
        var bodyW = size.X - Px(56f);
        var rows = Look.CentredWrapped(dl, Loc.T($"os.aetherling_lumilink_guide_body_{_page}"), centreX, bodyY, bodyW,
            Look.U32(Look.Body, 0.85f * fade), 0.9f);
        var contentTop = bodyY + (rows * Look.LineStep(0.9f)) + Px(14f);
        var buttonTop = origin.Y + size.Y - Px(38f) - Px(30f);
        var content = new Vector2(origin.X + Px(18f), contentTop);
        var contentSize = new Vector2(size.X - Px(36f), buttonTop - contentTop - Px(12f));

        switch (_page)
        {
            case 0:
                DrawTryMe(ctx, dl, content, contentSize, assetRoot, now, fade);
                break;
            case 1:
                DrawSpecials(ctx, dl, content, contentSize, assetRoot, now, fade);
                break;
            case 2:
                DrawPowers(ctx, dl, content, contentSize, assetRoot, core, now, fade);
                break;
            default:
                DrawBarAndThemes(ctx, dl, content, contentSize, assetRoot, now, fade);
                break;
        }

        DrawButton(ctx, dl, origin, size, fade);
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

    private void DrawBack(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin)
    {
        var side = Px(30f);
        var tl = origin + new Vector2(Px(10f), Px(8f));
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##lumiLinkGuideBack", new Vector2(side, side));
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
        _tryFired = -1;
        _tryPicked = null;
        _linked = 0;
    }

    private void DrawButton(OsAppContext ctx, ImDrawListPtr dl, Vector2 origin, Vector2 size, float fade)
    {
        var last = _page == Pages - 1;
        var label = Loc.T(last
            ? (_then is null ? "os.aetherling_lumilink_guide_done" : "os.aetherling_game_start")
            : "os.party_intro_next");
        var height = Px(38f);
        var width = size.X - (Px(48f) * 2f);
        var tl = new Vector2(origin.X + ((size.X - width) * 0.5f), origin.Y + size.Y - height - Px(30f));

        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton("##lumiLinkGuideNext", new Vector2(width, height));
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

    private void DrawTryMe(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        double now, float fade)
    {
        var demo = TryMe;
        var tile = MathF.Min(avail.X / demo.W, (avail.Y - Px(70f)) / demo.H);
        tile = MathF.Min(tile, Px(58f));
        var boardTl = new Vector2(tl.X + ((avail.X - (tile * demo.W)) * 0.5f), tl.Y);
        var elapsed = _tryFired < 0 ? -1f : (float)(now - _tryFired);
        var total = SwapSeconds + HoldSeconds + PopSeconds + LinkedSeconds;
        if (elapsed > total)
        {
            _tryFired = -1;
            elapsed = -1f;
        }

        var armed = elapsed < 0f;
        if (armed)
        {
            HandleTryInput(demo, boardTl, tile, now);
        }
        DrawDemoBoard(ctx, dl, demo, boardTl, tile, assetRoot, now, ctx.ReduceMotion, fade,
            armed ? -1f : elapsed, armed, _tryPicked);

        var captionY = boardTl.Y + (tile * demo.H) + Px(14f);
        var centreX = tl.X + (avail.X * 0.5f);
        var popped = elapsed >= SwapSeconds + HoldSeconds + PopSeconds;
        if (popped)
        {
            var t = (elapsed - (SwapSeconds + HoldSeconds + PopSeconds)) / LinkedSeconds;
            var bounce = ctx.ReduceMotion ? 1f : 1f + (0.18f * MathF.Sin(Look.EaseOut(t * 2f) * MathF.PI));
            Look.Pill(dl, Loc.T("os.aetherling_lumilink_guide_linked"), centreX, captionY, Look.Spark, fade * (1f - (t * 0.4f)), bounce);
            return;
        }
        var hint = _linked > 0 ? Loc.T("os.aetherling_lumilink_guide_f0_swap") : Loc.T("os.aetherling_lumilink_guide_try");
        Look.CentredWrapped(dl, hint, centreX, captionY, avail.X, Look.U32(Look.Whisper, fade), 0.9f);
    }

    private void HandleTryInput(Demo demo, Vector2 boardTl, float tile, double now)
    {
        (int C, int R)? under = null;
        var mouse = ImGui.GetMousePos();
        foreach (var cell in new[] { demo.A, demo.B })
        {
            var cellTl = boardTl + new Vector2(cell.C * tile, cell.R * tile);
            if (mouse.X >= cellTl.X && mouse.Y >= cellTl.Y && mouse.X < cellTl.X + tile && mouse.Y < cellTl.Y + tile)
            {
                under = cell;
            }
        }
        if (under is not null)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (under is { } u)
            {
                if (_tryPicked is { } picked && picked != u)
                {
                    Fire(now);
                    return;
                }
                _tryPicked = u;
                _tryDragging = true;
            }
            else
            {
                _tryPicked = null;
            }
        }
        if (_tryDragging && ImGui.IsMouseDown(ImGuiMouseButton.Left) && under is { } over && _tryPicked is { } from && over != from)
        {
            Fire(now);
            return;
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _tryDragging = false;
        }
    }

    private void Fire(double now)
    {
        _tryFired = now;
        _tryPicked = null;
        _tryDragging = false;
        _linked++;
    }

    private void DrawSpecials(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        double now, float fade)
    {
        var cycle = (float)(now - _pageShown);
        var index = ((int)(cycle / DemoCycleSeconds)) % Specials.Length;
        var local = cycle - (((int)(cycle / DemoCycleSeconds)) * DemoCycleSeconds);
        var demo = Specials[index];

        var tile = MathF.Min(avail.X / demo.W, (avail.Y - Px(92f)) / 4f);
        tile = MathF.Min(tile, Px(54f));
        var boardTl = new Vector2(tl.X + ((avail.X - (tile * demo.W)) * 0.5f), tl.Y + ((4 - demo.H) * tile * 0.5f));
        var elapsed = local - ShowSeconds;
        DrawDemoBoard(ctx, dl, demo, boardTl, tile, assetRoot, now, ctx.ReduceMotion, fade,
            elapsed < 0f ? -1f : elapsed, elapsed < 0f, null);

        var centreX = tl.X + (avail.X * 0.5f);
        var y = tl.Y + (tile * 4f) + Px(12f);
        var gap = Px(12f);
        var startX = centreX - (gap * (Specials.Length - 1) * 0.5f);
        for (var i = 0; i < Specials.Length; i++)
        {
            dl.AddCircleFilled(new Vector2(startX + (i * gap), y), Px(2.6f),
                Look.U32(LumiLinkPieces.KindColours[i], i == index ? 0.95f * fade : 0.25f * fade), 12);
        }
        y += Px(14f);
        var labelFade = ctx.ReduceMotion ? 1f : Look.EaseOut(local / 0.35f);
        var height = Look.Pill(dl, Loc.T(demo.LabelKey), centreX, y, LumiLinkPieces.KindColours[demo.Kinds[0] % 6] with { W = 1f },
            fade * labelFade, 0.95f);
        y += height + Px(10f);
        Look.CentredWrapped(dl, Loc.T("os.aetherling_lumilink_guide_f1_combo"), centreX, y, avail.X,
            Look.U32(Look.Whisper, fade), 0.85f);
    }

    /// <summary>One scripted board. <paramref name="elapsed"/> below zero is the resting pose with the pair
    /// marked; from zero it runs swap, hold, pop and the minted special, and holds the end state.</summary>
    private static void DrawDemoBoard(OsAppContext ctx, ImDrawListPtr dl, Demo demo, Vector2 boardTl, float tile,
        string assetRoot, double now, bool reduce, float fade, float elapsed, bool showHint,
        (int C, int R)? picked)
    {
        var pad = tile * 0.08f;
        dl.AddRectFilled(boardTl - new Vector2(pad), boardTl + new Vector2(tile * demo.W, tile * demo.H) + new Vector2(pad),
            Look.U32(new Vector4(1f, 1f, 1f, 0.05f * fade)), tile * 0.2f);
        for (var r = 0; r < demo.H; r++)
        {
            for (var c = 0; c < demo.W; c++)
            {
                if ((c + r) % 2 == 0)
                {
                    var ctl = boardTl + new Vector2(c * tile, r * tile);
                    dl.AddRectFilled(ctl, ctl + new Vector2(tile), Look.U32(new Vector4(1f, 1f, 1f, 0.035f * fade)), tile * 0.12f);
                }
            }
        }

        var swapT = elapsed < 0f ? 0f : Look.EaseInOut(Math.Clamp(elapsed / SwapSeconds, 0f, 1f));
        var popStart = SwapSeconds + HoldSeconds;
        var popT = elapsed < popStart ? 0f : Math.Clamp((elapsed - popStart) / PopSeconds, 0f, 1f);
        var minted = elapsed >= popStart + PopSeconds;
        var mintT = minted ? Math.Clamp((elapsed - popStart - PopSeconds) / 0.3f, 0f, 1f) : 0f;
        if (reduce && elapsed >= 0f)
        {
            swapT = 1f;
        }

        for (var r = 0; r < demo.H; r++)
        {
            for (var c = 0; c < demo.W; c++)
            {
                var kind = demo.Kinds[(r * demo.W) + c];
                var centre = boardTl + new Vector2((c + 0.5f) * tile, (r + 0.5f) * tile);
                var scale = 1f;
                var alpha = fade;
                var isA = (c, r) == demo.A;
                var isB = (c, r) == demo.B;
                if (isA)
                {
                    centre += new Vector2((demo.B.C - c) * tile, (demo.B.R - r) * tile) * swapT;
                    scale *= 1f + (0.14f * MathF.Sin(swapT * MathF.PI));
                }
                else if (isB)
                {
                    centre += new Vector2((demo.A.C - c) * tile, (demo.A.R - r) * tile) * swapT;
                }

                // Where this tile ENDS UP, which is not where it started for the two being swapped. The
                // line forms under the tile that arrives, so a pop or a minted special keyed on the cell
                // a tile came FROM lands one square away from the match it belongs to.
                var seat = isA ? demo.B : isB ? demo.A : (c, r);
                var cleared = false;
                foreach (var cell in demo.Clear)
                {
                    if (cell == seat)
                    {
                        cleared = true;
                    }
                }
                if (cleared && popT > 0f)
                {
                    if (minted && seat == demo.Mint)
                    {
                        scale = 0.6f + (0.4f * Look.EaseOut(mintT));
                    }
                    else
                    {
                        scale *= 1f - Look.EaseOut(popT);
                        alpha *= 1f - popT;
                        if (popT < 0.4f)
                        {
                            Look.Halo(dl, centre, tile * 0.7f, LumiLinkPieces.KindColours[kind], 0.3f * fade * (1f - (popT / 0.4f)));
                        }
                    }
                }
                if (scale <= 0.01f || alpha <= 0.01f)
                {
                    continue;
                }
                var pick = picked is { } p && p == (c, r);
                if (showHint && (isA || isB))
                {
                    var pulse = reduce ? 0.5f : 0.5f + (0.5f * MathF.Sin((float)(now * 4.0)));
                    var half = new Vector2(tile * 0.46f);
                    dl.AddRect(centre - half, centre + half, Look.U32(Look.Spark, (pick ? 1f : 0.45f + (0.4f * pulse)) * fade),
                        tile * 0.18f, ImDrawFlags.RoundCornersAll, pick ? 2.6f : 1.8f);
                }
                var special = minted && seat == demo.Mint ? demo.MintSpecial : Special.None;
                LumiLinkPieces.Draw(ctx, dl, assetRoot, 0, centre, tile * 0.84f * scale, kind, special, alpha, 0f,
                    Vector2.One, now, reduce);
            }
        }

        if (showHint && !reduce)
        {
            DrawSwapArrow(dl, demo, boardTl, tile, now, fade);
        }
    }

    /// <summary>A pair of chevrons sliding between the marked tiles: the swipe, shown rather than described.</summary>
    private static void DrawSwapArrow(ImDrawListPtr dl, Demo demo, Vector2 boardTl, float tile, double now, float fade)
    {
        var a = boardTl + new Vector2((demo.A.C + 0.5f) * tile, (demo.A.R + 0.5f) * tile);
        var b = boardTl + new Vector2((demo.B.C + 0.5f) * tile, (demo.B.R + 0.5f) * tile);
        var t = (float)((now * 0.9) % 1.0);
        var ease = Look.EaseInOut(t);
        var pos = Vector2.Lerp(a, b, ease);
        var dir = Vector2.Normalize(b - a);
        var side = new Vector2(-dir.Y, dir.X);
        var alpha = MathF.Sin(t * MathF.PI) * 0.9f * fade;
        var len = tile * 0.16f;
        var colour = Look.U32(Look.Spark, alpha);
        for (var i = 0; i < 2; i++)
        {
            var tip = pos + (dir * (len * (0.6f + i)));
            dl.AddLine(tip - (dir * len) - (side * len), tip, colour, 2.4f);
            dl.AddLine(tip - (dir * len) + (side * len), tip, colour, 2.4f);
        }
    }

    private void DrawPowers(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        AetherlingDto? core, double now, float fade)
    {
        var discR = MathF.Min(Px(24f), (avail.X - (5f * Px(10f))) / 12f);
        var gap = Px(10f);
        var rowW = (6f * discR * 2f) + (5f * gap);
        var left = tl.X + ((avail.X - rowW) * 0.5f);
        var discY = tl.Y + discR + Px(8f);
        var elements = LumiLinkGame.ElementOrder;
        var anyUnlocked = -1;
        for (var i = 0; i < 6; i++)
        {
            var element = elements[i];
            var unlocked = LumiLinkGame.ElementUnlocked(core, element);
            if (unlocked && anyUnlocked < 0)
            {
                anyUnlocked = i;
            }
            var c = new Vector2(left + discR + (i * ((discR * 2f) + gap)), discY);
            var hovered = ImGui.IsMouseHoveringRect(c - new Vector2(discR), c + new Vector2(discR));
            var selected = _pickedElement == i;
            var colour = LumiLinkPieces.KindColours[i];
            if (selected)
            {
                var pulse = ctx.ReduceMotion ? 0.5f : 0.5f + (0.5f * MathF.Sin((float)(now * 5.0)));
                Look.Halo(dl, c, discR * (1.7f + (0.2f * pulse)), colour, (0.25f + (0.15f * pulse)) * fade);
            }
            dl.AddCircleFilled(c, discR, Look.U32(new Vector4(1f, 1f, 1f, (unlocked ? 0.14f : 0.05f) * fade)), 28);
            dl.AddCircle(c, discR, Look.U32(colour with { W = (unlocked ? 0.65f : 0.15f) * fade }), 28, selected ? 2f : 1.2f);
            var icon = ctx.Capabilities.Textures.Get(System.IO.Path.Combine(assetRoot, "crystals", LumiLinkPieces.Elements[i] + ".png"));
            if (icon is { } handle)
            {
                var half = discR * 0.78f;
                var tint = unlocked ? Look.U32(new Vector4(1f, 1f, 1f, fade)) : Look.U32(new Vector4(0.5f, 0.5f, 0.55f, 0.5f * fade));
                dl.AddImage(handle, c - new Vector2(half), c + new Vector2(half), Vector2.Zero, Vector2.One, tint);
            }
            if (!unlocked)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.Lock, discR * 0.7f, c + new Vector2(discR * 0.55f, discR * 0.55f),
                    Look.U32(new Vector4(1f, 1f, 1f, 0.75f * fade)));
            }
            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _pickedElement = i;
                }
            }
        }
        if (_pickedElement < 0)
        {
            _pickedElement = Math.Max(anyUnlocked, 0);
        }

        var centreX = tl.X + (avail.X * 0.5f);
        var y = discY + discR + Px(16f);
        var pickedElement = elements[_pickedElement];
        var pickedUnlocked = LumiLinkGame.ElementUnlocked(core, pickedElement);
        var name = Loc.T($"os.aetherling_element_{LumiLinkPieces.Elements[_pickedElement]}");
        var h = Look.Pill(dl, name, centreX, y, LumiLinkPieces.KindColours[_pickedElement], fade, 0.95f);
        y += h + Px(10f);
        var text = pickedUnlocked
            ? Loc.T($"os.aetherling_lumilink_power_{LumiLinkPieces.Elements[_pickedElement]}")
            : string.Format(Loc.T("os.aetherling_lumilink_locked"), LumiLinkGame.FeedsLeft(core, pickedElement), name);
        var rows = Look.CentredWrapped(dl, text, centreX, y, avail.X, Look.U32(Look.Body, 0.9f * fade), 0.92f);
        y += (rows * Look.LineStep(0.92f)) + Px(14f);
        Look.CentredWrapped(dl, Loc.T("os.aetherling_lumilink_guide_tap_element"), centreX, y, avail.X,
            Look.U32(Look.Whisper, fade), 0.82f);
    }

    private void DrawBarAndThemes(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 avail, string assetRoot,
        double now, float fade)
    {
        var centreX = tl.X + (avail.X * 0.5f);
        var barW = MathF.Min(avail.X * 0.8f, Px(260f));
        var barH = Px(16f);
        var barX = centreX - (barW * 0.5f);
        var barY = tl.Y + Px(6f);
        var radius = barH * 0.5f;
        var fill = ctx.ReduceMotion ? 1f : (float)((now - _pageShown) % 5.0) / 4.2f;
        fill = Math.Clamp(Look.EaseOut(fill), 0f, 1f);
        var full = fill >= 0.999f;
        dl.AddRectFilled(new Vector2(barX, barY), new Vector2(barX + barW, barY + barH),
            Look.U32(new Vector4(1f, 1f, 1f, 0.08f * fade)), radius);
        var fillW = MathF.Max(barH, barW * fill);
        var hot = new Vector4(0.98f, 0.9f, 0.55f, 1f);
        dl.AddRectFilledMultiColor(new Vector2(barX, barY), new Vector2(barX + fillW, barY + barH),
            Look.U32(Look.Crystal, fade), Look.U32(hot, fade), Look.U32(hot, fade), Look.U32(Look.Crystal, fade));
        dl.AddRect(new Vector2(barX, barY), new Vector2(barX + barW, barY + barH),
            Look.U32(new Vector4(1f, 1f, 1f, 0.22f * fade)), radius, ImDrawFlags.RoundCornersAll, 1.1f);
        if (!ctx.ReduceMotion)
        {
            Look.Halo(dl, new Vector2(barX + fillW - radius, barY + radius), radius * (full ? 2.8f : 1.8f), hot, (full ? 0.5f : 0.25f) * fade);
        }
        var y = barY + barH + Px(8f);
        var label = Loc.T(full ? "os.aetherling_lumilink_power_ready" : "os.aetherling_lumilink_power_charging");
        Look.Centred(dl, label, centreX, y, Look.U32(full ? Look.Spark : Look.Whisper, fade), 0.85f);
        y += Look.LineStep(0.85f) + Px(4f);
        var rows = Look.CentredWrapped(dl, Loc.T("os.aetherling_lumilink_guide_f3_feed"), centreX, y, avail.X,
            Look.U32(Look.Body, 0.85f * fade), 0.85f);
        y += (rows * Look.LineStep(0.85f)) + Px(16f);

        // The three looks, one row each, drifting the way a level change sways them in.
        var tile = MathF.Min(Px(40f), (avail.X - Px(20f)) / 6f);
        var rowsTop = y;
        for (var theme = 0; theme < LumiLinkPieces.Themes; theme++)
        {
            var rowY = rowsTop + (theme * (tile + Px(8f))) + (tile * 0.5f);
            var sway = ctx.ReduceMotion ? 0f : MathF.Sin((float)(now * 1.1) + (theme * 1.3f)) * Px(4f);
            for (var kind = 0; kind < 6; kind++)
            {
                var centre = new Vector2(centreX + ((kind - 2.5f) * (tile + Px(4f))) + sway, rowY);
                LumiLinkPieces.Draw(ctx, dl, assetRoot, theme, centre, tile * 0.86f, kind, Special.None, fade, 0f,
                    Vector2.One, now, ctx.ReduceMotion);
            }
        }
        y = rowsTop + (LumiLinkPieces.Themes * (tile + Px(8f))) + Px(6f);
        Look.CentredWrapped(dl, Loc.T("os.aetherling_lumilink_guide_f3_themes"), centreX, y, avail.X,
            Look.U32(Look.Whisper, fade), 0.82f);
    }
}
