using System;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The chrome every racer screen shares: the flag palette, the page button, the corner chips
/// and the small effects the pages draw with.</summary>
internal static class RacerChrome
{
    /// <summary>The flag, in the order it flies.</summary>
    public static readonly Vector4 DutchRed = new(0.68f, 0.11f, 0.16f, 0.95f);
    public static readonly Vector4 DutchWhite = new(0.96f, 0.96f, 0.96f, 0.95f);
    public static readonly Vector4 DutchBlue = new(0.13f, 0.27f, 0.55f, 0.95f);

    /// <summary>The card art's own ink, sampled from race-card-bg.png, so the button that opens the
    /// statistics page is the colour the page it opens is drawn in.</summary>
    public static readonly Vector4 CardBlue = new(0.043f, 0.200f, 0.537f, 0.95f);
    public static readonly Vector4 WhiteInk = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 DarkInk = new(0.12f, 0.12f, 0.16f, 1f);

    /// <summary>The panel an offer card is drawn on: dark enough that a flag ink and an element chip
    /// both read against it, wherever the picture underneath happens to be bright.</summary>
    public static readonly Vector4 CardFace = new(0.09f, 0.08f, 0.14f, 0.90f);

    /// <summary>The flag colour a grade wears: the white face, the blue, then the red the chequered
    /// flag flies with.</summary>
    public static Vector4 GradeFlag(short difficulty) => difficulty switch
    {
        (short)AetherLove.Shared.Racing.LumiRaceDifficulty.Easy => DutchWhite,
        (short)AetherLove.Shared.Racing.LumiRaceDifficulty.Hard => DutchRed,
        _ => DutchBlue,
    };

    /// <summary>The name of a difficulty grade, for a race button's face or a stats column's head.</summary>
    public static string DifficultyLabel(OsAppContext ctx, short difficulty) => ctx.Localize(difficulty switch
    {
        (short)AetherLove.Shared.Racing.LumiRaceDifficulty.Easy => "os.racer_difficulty_easy",
        (short)AetherLove.Shared.Racing.LumiRaceDifficulty.Hard => "os.racer_difficulty_hard",
        _ => "os.racer_difficulty_normal",
    });

    /// <summary>The speaker chip, top-right of the screen's own child. Drawn-not-submitted (draw list
    /// plus a hand hit-test) so it never fights a screen's own items for the click; <paramref name="slot"/>
    /// counts chips from the right edge for screens that already keep one there.</summary>
    public static void DrawMuteChip(OsAppContext ctx, bool muted, System.Action toggle, float volume = 1f,
        System.Action<float>? setVolume = null, int slot = 0)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var chip = Px(30);
        var pad = Px(10);
        var a = new Vector2(origin.X + size.X - ((chip + pad) * (slot + 1)), origin.Y + pad);
        var b = a + new Vector2(chip, chip);
        var hovered = ImGui.IsMouseHoveringRect(a, b);
        dl.AddRectFilled(a, b, hovered ? 0xC84A3E68u : 0x96382E52u, chip * 0.5f);

        AetherLove.UI.IconDraw.AddCentered(dl, muted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp,
            Px(14), a + new Vector2(chip * 0.5f, chip * 0.5f), 0xFFE6E0F5);

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                toggle();
            }
        }

        if (setVolume is null)
        {
            return;
        }

        var barMuted = muted;
        var level = volume;
        if (AetherLove.Widgets.VolumeBar.Draw($"racerMute{slot}", dl, a, new Vector2(chip, chip),
            ref barMuted, ref level, 0xFFE6E0F5, 0x64382E52, 0xFFE6E0F5, AetherLove.UI.UiScale.S))
        {
            setVolume(level);
            if (barMuted != muted)
            {
                toggle();
            }
        }
    }

    /// <summary>A page button in one of the flag's colours: the shape every racer screen uses to get
    /// anywhere. <paramref name="blocked"/> dims it and rides underneath as the reason.</summary>
    public static bool FlagButton(OsAppContext ctx, string id, string label, Vector4 fill, Vector4 ink,
        string? blocked = null, bool enabled = true, bool chequered = false, bool fullWidth = false)
    {
        // A button on a page indents itself off the phone's edge; one inside a panel is already inset by
        // the panel, so it takes the width it is given.
        var width = fullWidth ? ImGui.GetContentRegionAvail().X : ImGui.GetContentRegionAvail().X - Px(56);
        var height = Px(44);
        if (!fullWidth)
        {
            ImGui.SetCursorPosX(Px(28));
        }

        var tl = ImGui.GetCursorScreenPos();
        // A button that will not answer must not look or feel as though it will: the hand, the dim and
        // the hover lift all read the same liveness the click does.
        var live = blocked is null && enabled;
        var pressed = ImGui.InvisibleButton(id, new Vector2(width, height)) && live;
        var hovered = ImGui.IsItemHovered();
        if (hovered && live)
        {
            HandOnHover();
        }

        var dl = ImGui.GetWindowDrawList();
        var br = tl + new Vector2(width, height);
        var dim = live ? 1f : 0.45f;
        var body = fill with { W = fill.W * dim * (hovered && live ? 1f : 0.92f) };
        dl.AddRectFilled(tl + new Vector2(0f, Px(3)), br + new Vector2(0f, Px(3)), 0x66000000u, height * 0.28f);
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(body), height * 0.28f);
        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.55f * dim)),
            height * 0.28f, ImDrawFlags.RoundCornersAll, Px(1.6f));

        if (chequered)
        {
            var band = height * 0.52f;
            var inset = height * 0.24f;
            Chequer(dl, new Vector2(tl.X + inset, tl.Y + ((height - band) * 0.5f)), band, dim, false);
            Chequer(dl, new Vector2(br.X - inset - band, tl.Y + ((height - band) * 0.5f)), band, dim, true);
        }

        var size = ImGui.CalcTextSize(label);
        dl.AddText(tl + new Vector2((width - size.X) * 0.5f, (height - size.Y) * 0.5f),
            ImGui.ColorConvertFloat4ToU32(ink with { W = ink.W * dim }), label);

        if (blocked is { Length: > 0 })
        {
            ImGui.Dummy(new Vector2(1f, Px(2)));
            CenteredMuted(blocked);
        }
        return pressed;
    }

    /// <summary>A chequered flag on its pole, four by four. Only the dark squares and the pole are
    /// drawn; the light squares are the button showing through. <paramref name="mirror"/> stands the
    /// pole on the right so a pair leans outward from the label.</summary>
    private static void Chequer(ImDrawListPtr dl, Vector2 tl, float side, float dim, bool mirror)
    {
        const int cells = 4;
        var cell = side / cells;
        var ink = ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.09f, 0.92f * dim));
        var pole = MathF.Max(1.5f, side * 0.11f);
        var drop = side * 0.42f;

        // The pole stands past the cloth at both ends: a staff cropped to the flag reads as a border.
        var poleX = mirror ? tl.X + side : tl.X - pole;
        dl.AddRectFilled(new Vector2(poleX, tl.Y - (drop * 0.35f)),
            new Vector2(poleX + pole, tl.Y + side + drop), ink, pole * 0.5f);

        for (var row = 0; row < cells; row++)
        {
            for (var col = 0; col < cells; col++)
            {
                if ((row + col) % 2 != 0)
                {
                    continue;
                }

                var a = tl + new Vector2(col * cell, row * cell);
                dl.AddRectFilled(a, a + new Vector2(cell, cell), ink);
            }
        }
    }

    /// <summary>The near-white the race card is printed on; every page that writes in <see cref="CardBlue"/>
    /// lays this under the words first.</summary>
    public static readonly Vector4 Paper = new(0.953f, 0.969f, 0.996f, 0.88f);

    /// <summary>A racing green, for the grade that reads as "go". Easy used to print in the page's own
    /// blue, which sat a shade away from Normal's and made the two look like one grade.</summary>
    public static readonly Vector4 GradeGreen = new(0.05f, 0.42f, 0.20f, 1f);

    /// <summary>The paper sheet the explainer and the onboarding write on: rounded, shadowed, edged in the
    /// card blue. <paramref name="inset"/> 0 covers the window whole, which is what a page with chrome of
    /// its own at the very top needs; anything larger leaves a frame of picture around it.</summary>
    public static void PaperSheet(ImDrawListPtr dl, Vector2 origin, Vector2 size, float inset = 10f)
    {
        var tl = origin + new Vector2(Px(inset), Px(inset));
        var br = origin + size - new Vector2(Px(inset), Px(inset));
        var round = Px(16);
        dl.AddRectFilled(tl + new Vector2(0f, Px(3)), br + new Vector2(0f, Px(3)), 0x50000000u, round);
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(Paper), round);
        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(CardBlue with { W = 0.55f }), round,
            ImDrawFlags.RoundCornersAll, Px(1.6f));
    }

    public static void CenteredText(string text)
    {
        var size = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX(MathF.Max(0f, (ImGui.GetWindowWidth() - size.X) * 0.5f));
        ImGui.TextUnformatted(text);
    }

    public static void CenteredWrapped(string text)
    {
        ImGui.SetCursorPosX(Px(24));
        ImGui.PushTextWrapPos(ImGui.GetWindowWidth() - Px(24));
        ImGui.TextWrapped(text);
        ImGui.PopTextWrapPos();
    }

    public static void CenteredMuted(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled)))
        {
            CenteredWrapped(text);
        }
    }

    /// <summary>A line that has to survive the stage behind it: white on its own dark plate. The muted
    /// grey it used to be is legible on a panel and invisible over course art, which is where the
    /// practice notice actually sits.</summary>
    public static void CenteredNotice(string text)
    {
        var padX = Px(14);
        var padY = Px(7);
        var wrap = ImGui.GetWindowWidth() - (Px(24) * 2f) - (padX * 2f);
        var size = ImGui.CalcTextSize(text, false, wrap);
        var plate = new Vector2(size.X + (padX * 2f), size.Y + (padY * 2f));

        ImGui.SetCursorPosX(MathF.Max(0f, (ImGui.GetWindowWidth() - plate.X) * 0.5f));
        var tl = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(tl, tl + plate,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.04f, 0.04f, 0.07f, 0.86f)), Px(10));

        ImGui.SetCursorScreenPos(tl + new Vector2(padX, padY));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrap);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(WhiteInk)))
        {
            ImGui.TextWrapped(text);
        }
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(1f, padY));
    }

    /// <summary>The card's stamp. The art is a white coverage mask, so one file serves the red on the
    /// finish page and a per-element tint on the card; <paramref name="squash"/> carries the landing
    /// wobble. Falls back to the old drawn shard while the texture is still decoding.</summary>
    public static void Stamp(ImDrawListPtr dl, OsAppContext ctx, string assetRoot, Vector2 centre,
        float r, uint ink, Vector2 squash = default)
    {
        if (squash == default)
        {
            squash = Vector2.One;
        }

        var path = System.IO.Path.Combine(assetRoot, "racer", "stamp.png");
        if (ctx.Capabilities.Textures.Get(path) is { } tex)
        {
            var half = new Vector2(r * 1.28f * squash.X, r * 1.28f * squash.Y);
            dl.AddImage(tex, centre - half, centre + half, Vector2.Zero, Vector2.One, ink);
            return;
        }

        Span<Vector2> pts =
        [
            centre + (new Vector2(0f, -r) * squash),
            centre + (new Vector2(r * 0.8f, -r * 0.15f) * squash),
            centre + (new Vector2(r * 0.5f, r) * squash),
            centre + (new Vector2(-r * 0.5f, r) * squash),
            centre + (new Vector2(-r * 0.8f, -r * 0.15f) * squash),
        ];
        dl.AddConvexPolyFilled(ref pts[0], pts.Length, ink);
    }

    /// <summary>A soft radial bloom, drawn as a few fading rings because a drawlist has no gradient
    /// brush.</summary>
    public static void Halo(ImDrawListPtr dl, Vector2 centre, float radius, Vector4 colour, float alpha,
        int rings = 4)
    {
        for (var i = rings; i >= 1; i--)
        {
            var t = i / (float)rings;
            dl.AddCircleFilled(centre, radius * t,
                ImGui.ColorConvertFloat4ToU32(colour with { W = alpha * (1f - t) * 0.6f }), 24);
        }
    }

    /// <summary>The pool a creature stands in: a flat ellipse, so it reads as ground rather than as a
    /// disc floating behind the feet.</summary>
    public static void GroundGlow(ImDrawListPtr dl, Vector2 feet, float rx, float ry, Vector4 colour, float alpha)
    {
        for (var i = 3; i >= 1; i--)
        {
            var t = i / 3f;
            var ink = ImGui.ColorConvertFloat4ToU32(colour with { W = alpha * (1f - t) * 0.8f });
            Span<Vector2> pts = stackalloc Vector2[20];
            for (var j = 0; j < pts.Length; j++)
            {
                var a = MathF.Tau * j / pts.Length;
                pts[j] = feet + new Vector2(MathF.Cos(a) * rx * t, MathF.Sin(a) * ry * t);
            }
            dl.AddConvexPolyFilled(ref pts[0], pts.Length, ink);
        }
    }
}
