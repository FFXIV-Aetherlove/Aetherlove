using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Racing;
using AetherLove.Shared.Racing.Cards;
using AetherLove.UI;
using AetherOS.Apps.Racer.Screens.Cards;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Racer.Screens;

/// <summary>The onboarding's racing card pages: what cards are, picking three before a race or a cup, where cards
/// come from, and levels. Every face is the real card renderer, so the pages show the pack's
/// art once it is downloaded and the frame's own placeholder until then.</summary>
internal sealed partial class RaceOnboardingScreen
{
    private const int HandSize = 3;
    private const float CardSlide = 14f;
    private const float PackRatio = 0.74f;

    /// <summary>How much of a card's height stands above the pack once it has risen.</summary>
    private const float CardRise = 0.78f;

    private static readonly Vector4 CardShadow = new(0f, 0f, 0f, 0.22f);

    private readonly CardFaceRenderer _faces = new();
    private string _handElement = string.Empty;
    private RaceCard[] _hand = [];

    /// <summary>A sample hand in the racer's own element, Gold first: the first Gold and the first two Silvers
    /// the catalogue lists for it, falling back to the whole catalogue for an element it does not carry.</summary>
    private RaceCard[] SampleHand()
    {
        var element = OwnElement() is { } own ? RacingElements.NameOf(own) : string.Empty;
        if (element.Length == 0)
        {
            element = ElementKeys[0];
        }
        if (element == _handElement && _hand.Length == HandSize)
        {
            return _hand;
        }

        RaceCard? gold = null;
        var silvers = new List<RaceCard>(2);
        foreach (var sameElement in new[] { true, false })
        {
            foreach (var card in RaceCardCatalogue.Cards)
            {
                if (sameElement && card.Element != element)
                {
                    continue;
                }
                if (card.IsGold)
                {
                    gold ??= card;
                }
                else if (silvers.Count < 2 && !silvers.Contains(card))
                {
                    silvers.Add(card);
                }
            }
            if (gold is not null && silvers.Count == 2)
            {
                break;
            }
        }

        if (gold is null || silvers.Count < 2)
        {
            return _hand;
        }
        _handElement = element;
        _hand = [gold, silvers[0], silvers[1]];
        return _hand;
    }

    private void Face(OsAppContext ctx, ImDrawListPtr dl, string key, RaceCard card, int level, Vector2 tl, float width,
        bool active, bool ready)
    {
        var size = new Vector2(width, width * CardFaceLayout.Ratio);
        var shadow = new Vector2(Px(3f), Px(5f));
        dl.AddRectFilled(tl + shadow, tl + size + shadow, ImGui.ColorConvertFloat4ToU32(CardShadow), width * 0.06f);
        _faces.Draw(ctx, dl, key, card, level, tl, size, false, active, ready);
    }

    private static float Ease(float t)
    {
        var clamped = Math.Clamp(t, 0f, 1f);
        return 1f - ((1f - clamped) * (1f - clamped) * (1f - clamped));
    }

    private static void CentredLabel(ImDrawListPtr dl, string text, float centreX, float top, Vector4 ink)
    {
        using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
        var width = ImGui.CalcTextSize(text).X;
        dl.AddText(new Vector2(MathF.Round(centreX - (width * 0.5f)), MathF.Round(top)),
            ImGui.ColorConvertFloat4ToU32(ink), text);
    }

    private static float CaptionHeight()
    {
        using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
        return ImGui.GetTextLineHeight();
    }

    /// <summary>The player's own creature in front of a fanned hand, Gold raised in the middle: cards belong
    /// to your Lumi.</summary>
    private void DrawCardsIntro(OsAppContext ctx, ImDrawListPtr dl, Vector2 stage, Vector2 size, double now, float fade)
    {
        var hand = SampleHand();
        if (hand.Length < HandSize)
        {
            return;
        }
        var ready = CardArt.PackReady(ctx);
        var pet = MathF.Min(Px(100f), size.Y * 0.36f);
        var width = MathF.Min(size.X * 0.34f, (size.Y - (pet * 0.5f) - Px(12f)) / CardFaceLayout.Ratio);
        var height = width * CardFaceLayout.Ratio;
        var centreX = stage.X + (size.X * 0.5f);
        var group = height + (pet * 0.5f);
        var start = stage.Y + MathF.Max(Px(4f), (size.Y - group) * 0.5f);
        var top = start + (Px(CardSlide) * (1f - fade));

        RacerChrome.Halo(dl, new Vector2(centreX, top + (height * 0.45f)), width * 1.5f,
            RacerChrome.CupGold, 0.30f * fade, 4);
        DrawFan(ctx, dl, hand, "intro-fan", centreX, top, width, ctx.ReduceMotion ? -1.0 : now, ready);

        var feet = new Vector2(centreX, MathF.Min(start + group, stage.Y + size.Y - Px(4f)));
        RacerChrome.GroundGlow(dl, feet, pet * 0.55f, pet * 0.13f, RacerChrome.CupGold, 0.40f * fade);
        var runner = Own();
        runner.Tick(ctx.ReduceMotion);
        runner.Draw(dl, ctx.Capabilities.Textures, feet, pet, runner.Pose, props: false);
    }

    /// <summary>The cup's four race dots over one set of three cards, a line from every dot to it: the cards picked at
    /// entry count for every race. The lit line walks the races on a loop.</summary>
    private void DrawCupHand(OsAppContext ctx, ImDrawListPtr dl, Vector2 stage, Vector2 size, double now, float fade)
    {
        var hand = SampleHand();
        if (hand.Length < HandSize)
        {
            return;
        }
        var ready = CardArt.PackReady(ctx);
        var rowY = stage.Y + Px(18f);
        var span = MathF.Min(size.X * 0.6f, Px(240f));
        var left = stage.X + ((size.X - span) * 0.5f);
        var centreX = stage.X + (size.X * 0.5f);
        var width = MathF.Min(size.X * 0.26f, (size.Y - Px(84f)) / (CardFaceLayout.Ratio * 1.1f));
        var height = width * CardFaceLayout.Ratio;
        var handTop = stage.Y + size.Y - (height * 1.1f) - Px(6f) + (Px(CardSlide) * (1f - fade));
        var lit = ctx.ReduceMotion ? -1 : (int)(now * 0.8 % LumiCupRules.RaceCount);

        for (var i = 0; i < LumiCupRules.RaceCount; i++)
        {
            var from = new Vector2(left + (span * i / (LumiCupRules.RaceCount - 1)), rowY + Px(13f));
            var colour = i == lit ? RacerChrome.DutchRed with { W = fade } : RacerChrome.CardBlue with { W = 0.30f * fade };
            dl.AddLine(from, new Vector2(centreX + ((i - 1.5f) * width * 0.35f), handTop - Px(8f)),
                ImGui.ColorConvertFloat4ToU32(colour), Px(i == lit ? 2.5f : 1.5f));
        }
        DrawCupDots(dl, stage, size, fade);

        DrawFan(ctx, dl, hand, "intro-cup", centreX, handTop, width, -1.0, ready);
    }

    /// <summary>A hand held up: the Gold at <paramref name="width"/> in the middle and the two Silvers smaller
    /// and lower behind it, so the Gold's name stays whole. <paramref name="now"/> below zero holds it still.</summary>
    private void DrawFan(OsAppContext ctx, ImDrawListPtr dl, RaceCard[] hand, string key, float centreX, float top,
        float width, double now, bool ready)
    {
        var height = width * CardFaceLayout.Ratio;
        var back = width * 0.7f;
        int[] order = [1, 2, 0];
        foreach (var slot in order)
        {
            var bob = now < 0.0 ? 0f : MathF.Sin((float)(now * 1.8) + (slot * 1.9f)) * Px(3f);
            if (slot == 0)
            {
                Face(ctx, dl, $"{key}-{slot}", hand[slot], 1, new Vector2(centreX - (width * 0.5f), top + bob), width,
                    false, ready);
                continue;
            }
            var side = slot == 1 ? -1f : 1f;
            var at = new Vector2(centreX + (side * width * 0.64f) - (back * 0.5f), top + (height * 0.2f) + bob);
            Face(ctx, dl, $"{key}-{slot}", hand[slot], 1, at, back, false, ready);
        }
    }

    /// <summary>A prize pack with a racing card rising out of it and a second card's back behind it, the pack and the
    /// risen card centred together on the stage.</summary>
    private void DrawCardPack(OsAppContext ctx, ImDrawListPtr dl, Vector2 stage, Vector2 size, double now, float fade)
    {
        var hand = SampleHand();
        if (hand.Length < HandSize)
        {
            return;
        }
        var ready = CardArt.PackReady(ctx);
        var packHeight = MathF.Min(size.Y * 0.55f, Px(190f));
        var packSize = new Vector2(packHeight * PackRatio, packHeight);
        var centreX = stage.X + (size.X * 0.5f);
        var width = packSize.X * 0.8f;
        var height = width * CardFaceLayout.Ratio;
        var group = packHeight + (height * CardRise);
        var groupTop = stage.Y + MathF.Max(Px(4f), (size.Y - group) * 0.5f);
        var packBottom = MathF.Min(groupTop + group, stage.Y + size.Y - Px(4f));
        var packTl = new Vector2(centreX - (packSize.X * 0.5f), packBottom - packHeight);
        var rise = ctx.ReduceMotion ? 1f : Ease((float)(now % 3.4) / 1.2f) * fade;
        var sunk = packTl.Y + packHeight - height - Px(4f);
        var risen = MathF.Max(stage.Y + Px(4f), packTl.Y - (height * CardRise));
        var faceTop = sunk + ((risen - sunk) * rise);

        RacerChrome.Halo(dl, new Vector2(centreX, faceTop + (height * 0.35f)), width * 1.3f,
            RacerChrome.CupGold, 0.40f * rise * fade, 4);
        var splay = Px(13f);
        var backAt = new Vector2(centreX - (width * 0.5f) + splay, sunk + ((risen + (height * 0.18f) - sunk) * rise));
        CardFaceRenderer.DrawBack(ctx, dl, backAt, new Vector2(width, height), false);
        var faceAt = new Vector2(centreX - (width * 0.5f) - splay, faceTop);
        Face(ctx, dl, "intro-pack", hand[0], 1, faceAt, width, false, ready);
        GrandstandFrame.Art(ctx, host, "pack-gold", packTl, packSize);
    }

    /// <summary>The same Gold three times, at level 1, 2 and 3, each with its level pips and name, chevrons
    /// between them. The raised card walks the levels on a loop; under reduced motion none is raised.</summary>
    private void DrawCardLevels(OsAppContext ctx, ImDrawListPtr dl, Vector2 stage, Vector2 size, double now, float fade)
    {
        var hand = SampleHand();
        if (hand.Length < HandSize)
        {
            return;
        }
        var ready = CardArt.PackReady(ctx);
        var label = CaptionHeight();
        var pips = CardChrome.PipHeight;
        var gap = Px(24f);
        var lift = Px(10f);
        var below = Px(8f) + pips + Px(6f) + label;
        var width = MathF.Min((size.X - Px(24f) - (gap * 2f)) / 3f,
            (size.Y - below - lift - Px(16f)) / CardFaceLayout.Ratio);
        var height = width * CardFaceLayout.Ratio;
        var left = stage.X + ((size.X - ((width * 3f) + (gap * 2f))) * 0.5f);
        var top = stage.Y + lift + MathF.Max(0f, (size.Y - height - below - lift) * 0.4f)
            + (Px(CardSlide) * (1f - fade));
        var raised = ctx.ReduceMotion ? -1 : (int)(now * 0.7 % RaceCardLevels.MaxLevel);
        var chevron = ImGui.ColorConvertFloat4ToU32(RacerChrome.CardBlue with { W = 0.5f * fade });

        for (var i = 0; i < RaceCardLevels.MaxLevel; i++)
        {
            var level = i + 1;
            var x = left + (i * (width + gap));
            if (i > 0)
            {
                IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronRight, Px(13f),
                    new Vector2(x - (gap * 0.5f), top + (height * 0.5f)), chevron);
            }
            var up = i == raised ? lift : 0f;
            if (i == raised)
            {
                RacerChrome.Halo(dl, new Vector2(x + (width * 0.5f), top + (height * 0.45f)), width,
                    RacerChrome.CupGold, 0.35f * fade, 4);
            }
            Face(ctx, dl, $"intro-level-{level}", hand[0], level, new Vector2(x, top - up), width, true, ready);
            var centreX = x + (width * 0.5f);
            CardChrome.LevelPips(dl, centreX, top + height + Px(8f), level);
            CentredLabel(dl, string.Format(ctx.Localize("os.racer_intro_cards_level"), level), centreX,
                top + height + Px(8f) + pips + Px(6f), PageInk with { W = fade });
        }
    }
}
