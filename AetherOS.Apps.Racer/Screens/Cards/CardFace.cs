using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Shared.Racing.Cards;
using AetherOS.Apps.Racer.Rendering;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>The localization keys of one card's copy. Every card string is <c>os.racer_card_&lt;slug&gt;_&lt;field&gt;</c>
/// where the slug is the catalogue id without its set prefix and with hyphens as underscores.</summary>
internal static class CardStrings
{
    private const string SetPrefix = "core.";
    private const string KeyPrefix = "os.racer_card_";
    public const string Name = "name";
    public const string Short = "short";
    public const string Condition = "cond";
    public const string Description = "desc";
    public const string Exact = "exact";
    public const string Story = "story";

    /// <summary>What joins the parts of a one-line card summary.</summary>
    public const string Separator = " · ";

    public static string Key(string cardId, string field)
    {
        var slug = cardId.StartsWith(SetPrefix, StringComparison.Ordinal) ? cardId[SetPrefix.Length..] : cardId;
        return KeyPrefix + slug.Replace('-', '_') + "_" + field;
    }

    public static string NameOf(OsAppContext ctx, RaceCard card) => ctx.Localize(Key(card.Id, Name));

    public static string ElementName(OsAppContext ctx, string element) =>
        ctx.Localize(element.Length == 0 ? "os.racer_element_neutral" : "os.racer_element_" + element);

    public static string BandName(OsAppContext ctx, RaceCard card) =>
        ctx.Localize(card.IsGold ? "os.racer_card_gold" : "os.racer_card_silver");

    public static string TimingName(OsAppContext ctx, RaceCard card) =>
        ctx.Localize(card.IsGold ? "os.racer_card_once" : "os.racer_card_passive");

    /// <summary>The card's number within its element's set, as "No. 3" in the player's language.</summary>
    public static string Number(OsAppContext ctx, RaceCard card) => string.Format(ctx.Localize("os.racer_card_rank"), card.Rank);

    /// <summary>The one-line type: element, card number, band and timing, joined by middle dots.</summary>
    public static string TypeLine(OsAppContext ctx, RaceCard card) => string.Join(Separator,
        ElementName(ctx, card.Element),
        Number(ctx, card),
        BandName(ctx, card),
        TimingName(ctx, card));
}

/// <summary>Draws a whole card face: the vector frame, the picture or its placeholder, the foil and halo the
/// level earns, the name plaque, the effect lines and the footer, then the tilt over all of it while the card
/// is active. No string is drawn smaller than <see cref="MinTextPx"/>, except the effect on a renderer that
/// <see cref="AlwaysShowsEffect"/>: a face too small to hold its name at
/// that size carries no text at all and reads by its art, frame and crystal, and the caller's tooltip or name
/// line says what it is. A gallery face whose effect does not fit whole leaves its effect lines empty rather
/// than cutting the rule short. Text is wrapped once per size and language and cached; the light position is
/// remembered per card so an unhovered face keeps the last light it was shown under.</summary>
internal sealed class CardFaceRenderer
{
    /// <summary>The smallest text, in design px, any card surface draws.</summary>
    public const float MinTextPx = 12f;

    /// <summary>The smallest effect text a face drawn with <see cref="AlwaysShowsEffect"/> steps down to before it
    /// cuts the effect with an ellipsis.</summary>
    private const float SqueezedBodyPx = 10f;
    private const float TextInsetUnits = 10f;
    private const float TitleUnitScale = 16f;
    private const float BodyUnitScale = 12f;
    private const float FooterUnitScale = 9.5f;
    private const float TitleLineSpacing = 1.12f;
    private const float BodyLineSpacing = 1.2f;
    private const float FooterBarUnits = 12f;
    private const float GalleryTitlePx = 18f;
    private const float DetailTitlePx = 26f;
    private const float GalleryBodyPx = 16f;
    private const float DetailBodyPx = 18f;
    private const float FooterBodyShare = 0.9f;
    private const float BodyStepPx = 1f;
    private const float FooterLabelGap = 4f;
    private const float PlaqueClipInset = 2f;
    private const float PlaqueTextMargin = PlaqueClipInset * 2f;
    private const float BandMarkX = 15f;
    private const float BandLabelX = 21f;
    private const float GoldMarkRadius = 2.7f;
    private const float SilverMarkRadius = 2.4f;
    private const float CrystalImageHalf = 10f;
    private const float ReducedFoil = 0.45f;
    private const float ReducedHalo = 0.5f;
    private static readonly Vector4 BodyInk = new(0.79f, 0.82f, 0.87f, 1f);
    private static readonly Vector4 RankInk = Vector4.One;

    private sealed record TextLayout(
        (float Width, float TitlePx, float BodyPx, float BodyFloorPx, float TitleMetric, float BodyMetric, string Culture) Key,
        string[] Title,
        float[] TitleWidths,
        string[] Body,
        float BodyPx);

    private readonly Dictionary<(string Id, bool Detail), TextLayout> _text = new();
    private readonly Dictionary<string, Vector2> _light = new(StringComparer.Ordinal);

    /// <summary>A gallery face whose effect does not fit whole at the text floor still shows it: smaller, down to
    /// <see cref="SqueezedBodyPx"/>, and cut with an ellipsis past that. For the race cards screen, where a small
    /// slot face without its effect reads as a picture only and the hover tooltip carries the effect in full.</summary>
    public bool AlwaysShowsEffect { get; init; }

    /// <summary>Draws the face at <paramref name="tl"/> of <paramref name="size"/>. <paramref name="active"/>
    /// is hovered or inspected: it enables the foil, the halo and the tilt. <paramref name="lightKey"/> names
    /// the face for its remembered light. <paramref name="textGrowth"/> above 1 raises a gallery face's name and
    /// effect sizes by that factor; the plaque, the name's width and a whole-effect fit still bound them.</summary>
    public void Draw(OsAppContext ctx, ImDrawListPtr dl, string lightKey, RaceCard card, int level, Vector2 tl, Vector2 size,
        bool detail, bool active, bool packReady, float textGrowth = 1f)
    {
        var br = tl + size;
        var unit = size.X / CardFaceLayout.Width;
        var layout = CardFaceLayout.For(detail);
        var metal = CardFramePalette.For(card.Band);
        var accent = CardElementStyle.Accent(card.Element);
        var tilt = active && !ctx.ReduceMotion;
        var outerClipMin = dl.GetClipRectMin();
        var outerClipMax = dl.GetClipRectMax();
        var vertices = dl.VtxBuffer.Size;
        if (tilt)
        {
            dl.AddDrawCmd();
        }

        var firstCommand = dl.CmdBuffer.Size - 1;
        var pointer = Vector2.Zero;
        if (!ctx.ReduceMotion)
        {
            _light.TryGetValue(lightKey, out pointer);
            var mouse = ImGui.GetMousePos();
            if (active && Hit(mouse, tl, br))
            {
                pointer = Vector2.Clamp((mouse - ((tl + br) * 0.5f)) / (size * 0.5f), -Vector2.One, Vector2.One);
                _light[lightKey] = pointer;
            }
        }

        var canvas = new DrawListCanvas(dl, tl, unit);
        CardFrameDesign.Base(ref canvas, layout, metal, accent, level);
        var artTL = tl + (layout.ArtMin * unit);
        var artBR = tl + (layout.ArtMax * unit);
        if (!CardArt.Draw(ctx, dl, card, artTL, artBR, detail, pointer, packReady))
        {
            CardArt.DrawPlaceholder(dl, card.Element, artTL, artBR, unit);
        }

        if (active && CardFrameDesign.HasFoil(level))
        {
            DrawFoil(dl, artTL, artBR, unit, pointer, level - 1, ctx.ReduceMotion ? ReducedFoil : 1f);
        }

        CardFrameDesign.Trim(ref canvas, layout, metal, accent, level, detail);
        if (active && CardFrameDesign.HasHalo(level))
        {
            DrawHalo(dl, tl, br, unit, pointer, accent, ctx.ReduceMotion ? ReducedHalo : 1f);
        }

        DrawCrystal(ctx, dl, card, tl, unit, accent, packReady, ref canvas);
        var name = CardStrings.NameOf(ctx, card);
        var textWidth = size.X - (unit * TextInsetUnits * 2f);
        var growth = detail ? 1f : MathF.Max(1f, textGrowth);
        var titlePx = TitleSize(name, textWidth, unit, layout, detail, growth);
        if (titlePx >= Px(MinTextPx))
        {
            DrawRank(ctx, dl, card, tl, unit);
            DrawTitleAndBody(ctx, dl, card, name, tl, br, textWidth, titlePx, unit, layout, metal, detail, growth);
            DrawFooter(ctx, dl, card, tl, unit, metal, detail, growth);
        }

        if (tilt && pointer.LengthSquared() > 0.00001f)
        {
            ApplyTilt(dl, vertices, firstCommand, (tl + br) * 0.5f, size, pointer, outerClipMin, outerClipMax);
        }

        if (tilt)
        {
            dl.AddDrawCmd();
        }
    }

    /// <summary>The common back at <paramref name="tl"/>, lit under the pointer while hovered.</summary>
    public static void DrawBack(OsAppContext ctx, ImDrawListPtr dl, Vector2 tl, Vector2 size, bool hovered)
    {
        var br = tl + size;
        var canvas = new DrawListCanvas(dl, tl, size.X / CardFaceLayout.Width);
        CardFrameDesign.Back(ref canvas);
        if (hovered)
        {
            var pointer = ctx.ReduceMotion ? Vector2.Zero : (ImGui.GetMousePos() - ((tl + br) * 0.5f)) / ((br - tl) * 0.5f);
            CardFrameDesign.BackLight(ref canvas, pointer);
        }
    }

    /// <summary>The picture alone, cover-fitted into the box, for the inspect page's Art tab.</summary>
    public static void DrawArtOnly(OsAppContext ctx, ImDrawListPtr dl, RaceCard card, Vector2 tl, Vector2 size, bool packReady)
    {
        var br = tl + size;
        if (!CardArt.Draw(ctx, dl, card, tl, br, true, Vector2.Zero, packReady))
        {
            CardArt.DrawPlaceholder(dl, card.Element, tl, br, size.X / CardFaceLayout.Width);
        }
    }

    public static bool Hit(Vector2 point, Vector2 tl, Vector2 br) =>
        point.X >= tl.X && point.Y >= tl.Y && point.X < br.X && point.Y < br.Y;

    private static void DrawCrystal(OsAppContext ctx, ImDrawListPtr dl, RaceCard card, Vector2 tl, float unit, Vector4 accent, bool packReady, ref DrawListCanvas canvas)
    {
        if (packReady && CardArt.TryImage(ctx, CardArt.CrystalPath(card.Element), out var crystal, out _))
        {
            var centre = tl + (CardFrameDesign.CrystalCenter * unit);
            var half = new Vector2(CrystalImageHalf * unit);
            dl.AddImage(crystal, centre - half, centre + half);
            return;
        }

        CardFrameDesign.Crystal(ref canvas, CardFrameDesign.CrystalCenter, CardFrameDesign.CrystalRadius, CardElementStyle.CrystalKind(card.Element), accent);
    }

    /// <summary>The name's size: its scale with the card, raised to the text floor, then held to what the plaque
    /// can hold. A result under the floor means the face is too small to carry any text. The inspect face also
    /// shrinks a long name to its width, never under the floor; the ellipsis takes the rest. A grown gallery name
    /// shrinks the same way.</summary>
    private static float TitleSize(string name, float width, float unit, CardFaceLayout layout, bool detail, float growth)
    {
        var floor = Px(MinTextPx);
        var usual = MathF.Max(floor, MathF.Min(Px(detail ? DetailTitlePx : GalleryTitlePx), unit * TitleUnitScale));
        var px = MathF.Max(floor, MathF.Min(Px(detail ? DetailTitlePx : GalleryTitlePx) * growth, unit * TitleUnitScale * growth));
        if (detail || px > usual)
        {
            using var titleFont = RacerFonts.Get(RacerTextSize.Button)?.Push();
            px = MathF.Max(floor, MathF.Min(px, width * ImGui.GetFontSize() / MathF.Max(1f, ImGui.CalcTextSize(name).X)));
        }

        return MathF.Min(px, (layout.NameBottom - layout.NameTop - PlaqueTextMargin) * unit / TitleLineSpacing);
    }

    private static float BodySize(float unit, bool detail, float growth = 1f) =>
        MathF.Max(Px(MinTextPx), MathF.Min(Px(detail ? DetailBodyPx : GalleryBodyPx) * growth, unit * BodyUnitScale * growth));

    /// <summary>The card number sits in the footer bar, so it shows only while the bar can hold it at the text
    /// floor.</summary>
    private static void DrawRank(OsAppContext ctx, ImDrawListPtr dl, RaceCard card, Vector2 tl, float unit)
    {
        var px = MathF.Max(Px(MinTextPx), unit * CardFrameDesign.RankFontSize);
        if (px > unit * FooterBarUnits)
        {
            return;
        }

        var text = CardStrings.Number(ctx, card);
        using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
        var measured = ImGui.CalcTextSize(text) * (px / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), px, tl + (CardFrameDesign.RankCenter * unit) - (measured * 0.5f), ElementFx.U32(RankInk), text);
    }

    private void DrawTitleAndBody(OsAppContext ctx, ImDrawListPtr dl, RaceCard card, string name, Vector2 tl, Vector2 br, float width, float titlePx,
        float unit, CardFaceLayout layout, CardFramePalette metal, bool detail, float growth)
    {
        var text = Layout(ctx, card, name, width, titlePx, BodySize(unit, detail, growth), BodySize(unit, detail), detail, layout, unit);
        var bodyPx = text.BodyPx;

        var titleTop = tl.Y + (layout.NameCenter * unit) - (text.Title.Length * titlePx * TitleLineSpacing * 0.5f);
        dl.PushClipRect(tl + (new Vector2(TextInsetUnits, layout.NameTop + PlaqueClipInset) * unit), tl + (new Vector2(CardFaceLayout.Width - TextInsetUnits, layout.NameBottom - PlaqueClipInset) * unit), true);
        using (RacerFonts.Get(RacerTextSize.Button)?.Push())
        {
            var font = ImGui.GetFont();
            for (var i = 0; i < text.Title.Length; i++)
            {
                dl.AddText(font, titlePx, new Vector2((tl.X + br.X - text.TitleWidths[i]) * 0.5f, titleTop + (i * titlePx * TitleLineSpacing)),
                    ElementFx.U32(metal.Lettering), text.Title[i]);
            }
        }

        dl.PopClipRect();

        var y = tl.Y + (layout.BodyTop * unit);
        dl.PushClipRect(new Vector2(tl.X + (unit * TextInsetUnits), y), tl + (new Vector2(CardFaceLayout.Width - TextInsetUnits, CardFaceLayout.BodyBottom) * unit), true);
        using (RacerFonts.Get(RacerTextSize.Body)?.Push())
        {
            var font = ImGui.GetFont();
            foreach (var line in text.Body)
            {
                dl.AddText(font, bodyPx, new Vector2(tl.X + (unit * TextInsetUnits), y), ElementFx.U32(BodyInk), line);
                y += bodyPx * BodyLineSpacing;
            }
        }

        dl.PopClipRect();
    }

    private static void DrawFooter(OsAppContext ctx, ImDrawListPtr dl, RaceCard card, Vector2 tl, float unit, CardFramePalette metal, bool detail, float growth)
    {
        var at = tl + (new Vector2(BandMarkX, CardFaceLayout.FooterY) * unit);
        if (card.IsGold)
        {
            var r = unit * GoldMarkRadius;
            dl.AddQuadFilled(at - new Vector2(0f, r), at + new Vector2(r, 0f), at + new Vector2(0f, r), at - new Vector2(r, 0f), ElementFx.U32(metal.Bright));
        }
        else
        {
            dl.AddCircleFilled(at, unit * SilverMarkRadius, ElementFx.U32(metal.Bright), 12);
        }

        var bodyPx = BodySize(unit, detail, growth);
        var footerPx = MathF.Max(Px(MinTextPx), MathF.Min(bodyPx * FooterBodyShare, unit * FooterUnitScale));
        if (footerPx > unit * FooterBarUnits)
        {
            return;
        }

        using var font = RacerFonts.Get(RacerTextSize.Caption)?.Push();
        var baked = ImGui.GetFontSize();
        var rankPx = MathF.Max(Px(MinTextPx), unit * CardFrameDesign.RankFontSize);
        var rankHalf = ImGui.CalcTextSize(CardStrings.Number(ctx, card)).X * rankPx / baked * 0.5f;
        var room = ((CardFrameDesign.RankCenter.X - BandLabelX) * unit) - rankHalf - Px(FooterLabelGap);
        var label = CardText.Ellipsize(ctx.Localize(card.IsGold ? "os.racer_album_filter_gold" : "os.racer_album_filter_silver"), room,
            value => ImGui.CalcTextSize(value).X * footerPx / baked);
        if (label.Length == 0)
        {
            return;
        }

        dl.AddText(ImGui.GetFont(), footerPx, tl + (new Vector2(BandLabelX, CardFaceLayout.FooterY) * unit) - new Vector2(0f, footerPx * 0.5f), ElementFx.U32(metal.Bright), label);
    }

    private TextLayout Layout(OsAppContext ctx, RaceCard card, string name, float width, float titlePx, float bodyPx, float bodyFloorPx, bool detail,
        CardFaceLayout layout, float unit)
    {
        float titleMetric;
        using (RacerFonts.Get(RacerTextSize.Button)?.Push())
        {
            titleMetric = ImGui.CalcTextSize(CardText.MetricSample).X / ImGui.GetFontSize();
        }

        float bodyMetric;
        using (RacerFonts.Get(RacerTextSize.Body)?.Push())
        {
            bodyMetric = ImGui.CalcTextSize(CardText.MetricSample).X / ImGui.GetFontSize();
        }

        var key = (MathF.Round(width), titlePx, bodyPx, bodyFloorPx, titleMetric, bodyMetric, ctx.Culture.Name);
        var id = (card.Id, detail);
        if (_text.TryGetValue(id, out var cached) && cached.Key == key)
        {
            return cached;
        }

        var built = Build(ctx, card, name, key, layout, unit, detail, AlwaysShowsEffect);
        _text[id] = built;
        return built;
    }

    /// <summary>Wraps the name to one line and the short effect under it. The inspect face wraps the effect into
    /// its lines and cuts the rest with an ellipsis; a gallery face shows the effect only when the whole of it
    /// fits, since a cut rule reads as a different rule and the tooltip carries it in full. A grown gallery effect
    /// that does not fit whole steps its size down toward the ungrown size before giving up; with
    /// <paramref name="alwaysEffect"/> it keeps stepping down to <see cref="SqueezedBodyPx"/> and then cuts.</summary>
    private static TextLayout Build(OsAppContext ctx, RaceCard card, string name,
        (float Width, float TitlePx, float BodyPx, float BodyFloorPx, float TitleMetric, float BodyMetric, string Culture) key, CardFaceLayout layout, float unit, bool detail,
        bool alwaysEffect)
    {
        string[] title;
        float[] widths;
        using (RacerFonts.Get(RacerTextSize.Button)?.Push())
        {
            var scale = key.TitlePx / ImGui.GetFontSize();
            title = CardText.Wrap(name, key.Width, value => ImGui.CalcTextSize(value).X * scale, 1);
            widths = Array.ConvertAll(title, value => ImGui.CalcTextSize(value).X * scale);
        }

        string[] body = [];
        var bodyPx = key.BodyPx;
        using (RacerFonts.Get(RacerTextSize.Body)?.Push())
        {
            var effect = ctx.Localize(CardStrings.Key(card.Id, CardStrings.Short));
            var baked = ImGui.GetFontSize();
            if (detail)
            {
                body = CardText.Wrap(effect, key.Width, value => ImGui.CalcTextSize(value).X * key.BodyPx / baked, layout.BodyLines(unit, key.BodyPx));
            }
            else
            {
                var step = Px(BodyStepPx);
                for (var px = key.BodyPx; px >= key.BodyFloorPx - (step * 0.5f); px -= step)
                {
                    var size = MathF.Max(px, key.BodyFloorPx);
                    if (CardText.TryWrapWhole(effect, key.Width, value => ImGui.CalcTextSize(value).X * size / baked, layout.BodyLines(unit, size), out var lines))
                    {
                        body = lines;
                        bodyPx = size;
                        break;
                    }
                }

                if (body.Length == 0 && alwaysEffect)
                {
                    var squeezed = MathF.Min(key.BodyFloorPx, Px(SqueezedBodyPx));
                    for (var px = key.BodyFloorPx - step; px >= squeezed - (step * 0.5f); px -= step)
                    {
                        var size = MathF.Max(px, squeezed);
                        if (CardText.TryWrapWhole(effect, key.Width, value => ImGui.CalcTextSize(value).X * size / baked, layout.BodyLines(unit, size), out var lines))
                        {
                            body = lines;
                            bodyPx = size;
                            break;
                        }
                    }

                    if (body.Length == 0)
                    {
                        body = CardText.Wrap(effect, key.Width, value => ImGui.CalcTextSize(value).X * squeezed / baked, Math.Max(1, layout.BodyLines(unit, squeezed)));
                        bodyPx = squeezed;
                    }
                }
            }
        }

        return new TextLayout(key, title, widths, body, bodyPx);
    }

    /// <summary>The foil over the art: reflected bands whose place follows the pointer and nothing else, so an
    /// unchanged pointer draws exactly the same light. <paramref name="grade"/> 1 is a white sheen, 2 and 3
    /// iridescent with more bands.</summary>
    private static void DrawFoil(ImDrawListPtr dl, Vector2 tl, Vector2 br, float unit, Vector2 pointer, int grade, float intensity)
    {
        const int slices = 12;
        var progress = 0.5f + (pointer.X * 0.4f);
        var x = tl.X + ((br.X - tl.X) * progress);
        var lean = unit * (12f + (pointer.Y * 25f));
        var width = unit * (grade == 1 ? 18f : 25f);
        var bands = grade >= 3 ? 3 : grade == 2 ? 2 : 1;
        var peak = grade == 1 ? 0.10f : grade == 2 ? 0.16f : 0.22f;
        dl.PushClipRect(tl, br, true);
        for (var band = 0; band < bands; band++)
        {
            for (var i = 0; i < slices; i++)
            {
                var t = (i + 0.5f) / slices;
                var alpha = MathF.Pow(MathF.Sin(t * MathF.PI), 2f) * peak * intensity;
                var hue = (t + (band * 0.28f) + (pointer.Y * 0.18f)) * MathF.PI * 2f;
                var tint = grade == 1
                    ? new Vector3(0.84f, 0.94f, 1f)
                    : new Vector3(0.72f + (0.28f * MathF.Sin(hue)), 0.72f + (0.28f * MathF.Sin(hue + 2.094f)), 0.72f + (0.28f * MathF.Sin(hue + 4.189f)));
                var left = x + ((band - ((bands - 1) * 0.5f)) * unit * 40f) + ((t - 0.5f) * width * 2f);
                var step = (width * 2f / slices) + 0.2f;
                dl.AddQuadFilled(
                    new Vector2(Math.Clamp(left + lean, tl.X, br.X), tl.Y),
                    new Vector2(Math.Clamp(left + step + lean, tl.X, br.X), tl.Y),
                    new Vector2(Math.Clamp(left + step - lean, tl.X, br.X), br.Y),
                    new Vector2(Math.Clamp(left - lean, tl.X, br.X), br.Y),
                    ElementFx.U32(new Vector4(tint, alpha)));
            }
        }

        dl.PopClipRect();
    }

    /// <summary>The top level's outer glow: soft coloured strokes that fade into the page.</summary>
    private static void DrawHalo(ImDrawListPtr dl, Vector2 tl, Vector2 br, float unit, Vector2 pointer, Vector4 accent, float intensity)
    {
        const int layers = 7;
        var strength = (0.65f + (0.35f * (pointer.X + 1f) * 0.5f)) * intensity;
        for (var layer = layers; layer >= 1; layer--)
        {
            var spread = Vector2.One * unit * layer;
            var falloff = 1f - (layer / (float)(layers + 1));
            dl.AddRect(tl - spread, br + spread, ElementFx.U32(accent with { W = strength * 0.07f * falloff * falloff }),
                unit * (6f + layer), ImDrawFlags.RoundCornersAll, MathF.Max(1f, unit * 2.5f));
        }
    }

    private static void ApplyTilt(ImDrawListPtr dl, int firstVertex, int firstCommand, Vector2 centre, Vector2 size, Vector2 pointer,
        Vector2 outerClipMin, Vector2 outerClipMax)
    {
        for (var i = firstVertex; i < dl.VtxBuffer.Size; i++)
        {
            var vertex = dl.VtxBuffer[i];
            vertex.Pos = CardTilt.Project(vertex.Pos, centre, size, pointer);
            dl.VtxBuffer[i] = vertex;
        }

        for (var i = Math.Max(0, firstCommand); i < dl.CmdBuffer.Size; i++)
        {
            var command = dl.CmdBuffer[i];
            var clip = command.ClipRect;
            var a = CardTilt.Project(new Vector2(clip.X, clip.Y), centre, size, pointer);
            var b = CardTilt.Project(new Vector2(clip.Z, clip.Y), centre, size, pointer);
            var c = CardTilt.Project(new Vector2(clip.Z, clip.W), centre, size, pointer);
            var d = CardTilt.Project(new Vector2(clip.X, clip.W), centre, size, pointer);
            var min = Vector2.Max(outerClipMin, Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d)));
            var max = Vector2.Min(outerClipMax, Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d)));
            max = Vector2.Max(min, max);
            command.ClipRect = new Vector4(min.X, min.Y, max.X, max.Y);
            dl.CmdBuffer[i] = command;
        }
    }
}
