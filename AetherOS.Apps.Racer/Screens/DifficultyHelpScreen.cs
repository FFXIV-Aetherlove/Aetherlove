using System;
using System.Numerics;
using System.Threading.Tasks;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Racing;
using AetherOS.Apps.Racer.Rendering;
using AetherOS.Sdk;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>What the three grades mean, printed on the race card. The element wheel decides them: your
/// own ground grades Easy, either neighbour Normal, the far half Hard with no penalty attached.</summary>
internal sealed class DifficultyHelpScreen(IRacerHost host, Action back, Func<bool> muted, Action toggleMute, Func<float> volume, Action<float> setVolume)
{
    private const float TextInset = 30f;
    private const float BarInset = 18f;
    private const float BarWidth = 5f;

    private const float MutedInk = 0.66f;

    /// <summary>The card's own ink; the page writes on paper, not on the picture.</summary>
    private static readonly Vector4 PageInk = RacerChrome.CardBlue with { W = 1f };

    private const float WheelRadiusShare = 0.30f;

    /// <summary>Radians shaved off each arc end, so neighbouring grades never touch.</summary>

    private static readonly (short Grade, string Key)[] Tiers =
    [
        ((short)LumiRaceDifficulty.Easy, "os.racer_diff_help_easy"),
        ((short)LumiRaceDifficulty.Normal, "os.racer_diff_help_normal"),
        ((short)LumiRaceDifficulty.Hard, "os.racer_diff_help_hard"),
    ];

    private LumiRaceStateDto? _state;
    private LumiRaceStateDto? _pending;

    /// <summary>Fetches the state for the element line alone. The page explains the rules without it, so
    /// a read that fails stays silent instead of putting an error on an explainer.</summary>
    public void OnShow()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                _pending = await host.GetStateAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        });
    }

    public void Draw(OsAppContext ctx)
    {
        if (_pending is { } fresh)
        {
            _pending = null;
            _state = fresh;
        }

        var avail = ImGui.GetContentRegionAvail();
        using var body = ImRaii.Child("##racerDiffHelp", avail, false);
        if (!body)
        {
            return;
        }

        RacerBackdrop.Draw(ctx, host, ImGui.GetWindowPos(), ImGui.GetWindowSize(), dim: 0.20f, anchorY: 1f);
        RacerChrome.PaperSheet(ImGui.GetWindowDrawList(), ImGui.GetWindowPos(), ImGui.GetWindowSize());
        RacerChrome.DrawMuteChip(ctx, muted(), toggleMute, volume(), setVolume);
        using var ink = ImRaii.PushColor(ImGuiCol.Text, PageInk);

        ImGui.Dummy(new Vector2(1f, Px(20)));
        using (ctx.TitleFont?.Push())
        {
            RacerChrome.CenteredText(ctx.Localize("os.racer_diff_help_title"));
        }
        ImGui.Dummy(new Vector2(1f, Px(12)));

        DrawOwnElement(ctx);
        Paragraph(ctx.Localize("os.racer_diff_help_intro"));
        ImGui.Dummy(new Vector2(1f, Px(10)));
        DrawWheel(ctx);
        ImGui.Dummy(new Vector2(1f, Px(10)));

        foreach (var tier in Tiers)
        {
            DrawTier(ctx, tier.Grade, tier.Key);
        }

        ImGui.Dummy(new Vector2(1f, Px(14)));
        if (RacerChrome.FlagButton(ctx, "##racerDiffBack", ctx.Localize("os.racer_back"),
            RacerChrome.DutchBlue, RacerChrome.WhiteInk))
        {
            back();
        }
        ImGui.Dummy(new Vector2(1f, Px(12)));
    }

    /// <summary>The racer's own element, named in its own colour. Left out entirely until an offer
    /// stands, so a player who cannot race yet still gets the rules.</summary>
    private void DrawOwnElement(OsAppContext ctx)
    {
        if (OwnElement(_state) is not { } element)
        {
            return;
        }

        var name = RacingElements.NameOf(element);
        using (ImRaii.PushColor(ImGuiCol.Text, PageInk with { W = MutedInk }))
        {
            RacerChrome.CenteredText(ctx.Localize("os.racer_diff_help_yours"));
        }
        var tint = DifficultyWheel.Tone(ElementFx.For(name).Tint, WheelSurface.Paper);
        using (ImRaii.PushColor(ImGuiCol.Text, tint))
        {
            RacerChrome.CenteredText(ctx.Localize($"os.racer_element_{name}"));
        }
        ImGui.Dummy(new Vector2(1f, Px(12)));
    }

    /// <summary>One grade's paragraph, with that grade's own flag colour as a rule down its left, so the
    /// tiers read against the offer cards on race day.</summary>
    private static void DrawTier(OsAppContext ctx, short grade, string key)
    {
        var top = ImGui.GetCursorScreenPos().Y;
        Paragraph(ctx.Localize(key));
        var bottom = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y;

        var dl = ImGui.GetWindowDrawList();
        var x = ImGui.GetWindowPos().X + Px(BarInset);
        var a = new Vector2(x, top);
        var b = new Vector2(x + Px(BarWidth), bottom);
        var round = Px(BarWidth) * 0.5f;
        dl.AddRectFilled(a, b, ImGui.ColorConvertFloat4ToU32(GradeInk(grade)), round);
        ImGui.Dummy(new Vector2(1f, Px(12)));
    }

    /// <summary>The wheel itself, drawn rather than shipped: six wedges in the wheel's own clockwise
    /// order, turned so the racer's element sits at the top, that wedge filled in its element's colour
    /// with the icon knocked out. The grade arcs ride outside the rim: a heavy stroke over your wedge,
    /// a medium one over each neighbour, a dashed sweep over the far three, each labelled. Drawn from
    /// the same rules the deal runs on, so it can never drift from what the server does.</summary>
    private void DrawWheel(OsAppContext ctx)
    {
        var width = ImGui.GetWindowWidth() - (Px(TextInset) * 2f) - ImGui.GetStyle().ScrollbarSize;
        var radius = width * WheelRadiusShare;
        var half = radius + DifficultyWheel.Overhang(ImGui.GetTextLineHeight());
        ImGui.SetCursorPosX(Px(TextInset));
        var tl = ImGui.GetCursorScreenPos();
        DifficultyWheel.Draw(ctx, ImGui.GetWindowDrawList(),
            new Vector2(tl.X + (width * 0.5f), tl.Y + half), radius, OwnElement(_state),
            WheelSurface.Paper, PageInk);
        ImGui.Dummy(new Vector2(width, half * 2f));
    }

    private static Vector4 GradeInk(short grade) =>
        DifficultyWheel.GradeInk(grade, WheelSurface.Paper, PageInk);


    private static void Paragraph(string text)
    {
        ImGui.SetCursorPosX(Px(TextInset));
        ImGui.PushTextWrapPos(ImGui.GetWindowWidth() - Px(TextInset) - ImGui.GetStyle().ScrollbarSize);
        ImGui.TextWrapped(text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>The racer's own element, read off the Easy offer. Easy is dealt from the courses that
    /// grade to the racer's own ground, so that offer's element IS the racer's.</summary>
    private static AetherlingElement? OwnElement(LumiRaceStateDto? state)
    {
        if (state?.Offers is not { Length: > 0 } offers)
        {
            return null;
        }

        foreach (var offer in offers)
        {
            if (offer.Difficulty != (short)LumiRaceDifficulty.Easy)
            {
                continue;
            }

            var element = (AetherlingElement)offer.Element;
            return element == AetherlingElement.None ? null : element;
        }
        return null;
    }
}
