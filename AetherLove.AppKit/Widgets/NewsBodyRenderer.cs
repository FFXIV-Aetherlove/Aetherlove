using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using AetherLove.Emoji;
using AetherLove.Services;
using AetherLove.Shared.News;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Widgets;

/// <summary>Renders a news body as justified long-form copy with emoji support, hanging indents, and image rounding; structure inferred from plain text.</summary>
public sealed class NewsBodyRenderer
{
    private static readonly Regex EmojiToken = new(@"(:[a-z0-9_+\-]+:)", RegexOptions.Compiled);
    private static readonly Regex LeadingEmoji = new(@"^:[a-z0-9_+\-]+:", RegexOptions.Compiled);
    private static readonly Regex TrailingEmoji = new(@":[a-z0-9_+\-]+:$", RegexOptions.Compiled);

    /// <summary>Longest emoji-flanked line still treated as a heading rather than a paragraph.</summary>
    private const int HeadingMaxChars = 48;

    /// <summary>Leading between body lines, as a multiple of the font line height.</summary>
    private const float LineSpacing = 1.25f;

    /// <summary>A line is left ragged instead of justified once its gaps would stretch past this many
    /// space widths (a near-empty line before a hard break would tear into rivers).</summary>
    private const float MaxJustifyStretch = 2.5f;

    private readonly record struct Token(string? Word, string? Emoji, float Width);

    private readonly Dictionary<string, ISharedImmediateTexture?> _images = new();
    private readonly string _cacheDir =
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "NewsCache");

    public void Draw(string idScope, NewsLineDto[] lines, float leftPad, float contentWidth)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            ImGui.SetCursorPosX(leftPad);
            if (line.Kind == NewsLineKind.Image && line.ImageBytes is { Length: > 0 })
            {
                DrawImage($"{idScope}_{i}", line, leftPad, contentWidth);
                ImGui.Spacing();
            }
            else if (line.Kind == NewsLineKind.Text && !string.IsNullOrWhiteSpace(line.Text))
            {
                DrawTextLine($"{idScope}_t{i}", line.Text!, leftPad, contentWidth);
            }
            else
            {
                // A blank line is an authored paragraph break.
                ImGui.Dummy(new Vector2(1f, ImGui.GetTextLineHeight() * 0.4f));
            }
        }
    }

    private static void DrawTextLine(string id, string text, float leftPad, float contentWidth)
    {
        var trimmed = text.Trim();
        var stripped = EmojiToken.Replace(trimmed, string.Empty).Trim();

        if (LeadingEmoji.IsMatch(trimmed) && TrailingEmoji.IsMatch(trimmed) && !trimmed.Contains('\n')
            && stripped.Length is > 0 and <= HeadingMaxChars)
        {
            DrawHeading(id, trimmed, leftPad, contentWidth);
        }
        else
        {
            DrawParagraph(trimmed, leftPad, contentWidth);
        }
    }

    /// <summary>A short emoji-flanked line renders as a section heading: H3 accent text over a hairline
    /// that fades out to the right.</summary>
    private static void DrawHeading(string id, string text, float leftPad, float contentWidth)
    {
        var t = ThemeService.Current;
        ImGui.Dummy(new Vector2(1f, Px(3f)));
        ImGui.SetCursorPosX(leftPad);
        using (UiFonts.H3?.Push())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, t.AccentLight);
            ParsedMessage.Parse(text).DrawWrapped(id, contentWidth);
            ImGui.PopStyleColor();
        }
        ImGui.SetCursorPosX(leftPad);
        var p = ImGui.GetCursorScreenPos();
        var edge = ImGui.GetColorU32(t.Accent with { W = 0.55f });
        var mid = ImGui.GetColorU32(t.Accent with { W = 0f });
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(
            p, p + new Vector2(contentWidth, Px(1.4f)), edge, mid, mid, edge);
        ImGui.Dummy(new Vector2(1f, Px(9f)));
    }

    /// <summary>Manual justified paragraph layout with hard-break item support and hanging emoji indents.</summary>
    private static void DrawParagraph(string text, float leftPad, float width)
    {
        using var font = UiFonts.Reader?.Push();
        var dl = ImGui.GetWindowDrawList();
        var lineH = ImGui.GetTextLineHeight();
        var advance = lineH * LineSpacing;
        var spaceW = ImGui.CalcTextSize(" ").X;
        var emojiSz = MathF.Floor(lineH);
        var col = ImGui.GetColorU32(UiColors.Body);

        ImGui.SetCursorPosX(leftPad);
        var origin = ImGui.GetCursorScreenPos();
        var y = origin.Y;

        var items = text.Split('\n');
        for (var itemIdx = 0; itemIdx < items.Length; itemIdx++)
        {
            var item = items[itemIdx].Trim();
            if (item.Length == 0)
            {
                y += advance * 0.5f;
                continue;
            }

            var tokens = Tokenize(item, emojiSz, width);
            var hangIndent = tokens.Count > 1 && tokens[0].Emoji is not null ? emojiSz + spaceW : 0f;

            var i = 0;
            var lineIdx = 0;
            while (i < tokens.Count)
            {
                var indent = lineIdx > 0 ? hangIndent : 0f;
                var lineW = width - indent;

                var start = i;
                var natural = 0f;
                while (i < tokens.Count)
                {
                    var need = natural == 0f ? tokens[i].Width : natural + spaceW + tokens[i].Width;
                    if (natural > 0f && need > lineW)
                    {
                        break;
                    }
                    natural = need;
                    i++;
                }

                var count = i - start;
                var gapW = spaceW;
                if (i < tokens.Count && count > 1)
                {
                    var extra = (lineW - natural) / (count - 1);
                    if (extra > 0f && extra <= spaceW * MaxJustifyStretch)
                    {
                        gapW = spaceW + extra;
                    }
                }

                var x = origin.X + indent;
                for (var k = start; k < i; k++)
                {
                    var tok = tokens[k];
                    if (tok.Emoji is not null)
                    {
                        var tex = UiHost.EmojiService.GetEmoji(tok.Emoji)?.GetWrapOrDefault();
                        if (tex != null)
                        {
                            dl.AddImage(tex.Handle, new Vector2(x, y), new Vector2(x + emojiSz, y + emojiSz));
                        }
                    }
                    else
                    {
                        dl.AddText(new Vector2(x, y), col, tok.Word);
                    }
                    x += tok.Width + gapW;
                }
                y += advance;
                lineIdx++;
            }

            if (itemIdx < items.Length - 1)
            {
                y += lineH * 0.18f;
            }
        }

        var totalH = y - origin.Y - (advance - lineH);
        ImGui.Dummy(new Vector2(width, MathF.Max(lineH, totalH)));
        ImGui.Dummy(new Vector2(1f, Px(6f)));
    }

    /// <summary>Splits an item into word and emoji tokens with measured widths; a shortcode the catalog
    /// doesn't know stays literal text, and an over-wide word is chunked so it can still wrap.</summary>
    private static List<Token> Tokenize(string item, float emojiSz, float maxLineW)
    {
        var tokens = new List<Token>();
        foreach (var part in EmojiToken.Split(item))
        {
            if (part.Length == 0)
            {
                continue;
            }
            if (part.Length > 2 && part[0] == ':' && part[^1] == ':'
                && UiHost.EmojiService.GetEmoji(part[1..^1]) is not null)
            {
                tokens.Add(new Token(null, part[1..^1], emojiSz));
                continue;
            }
            foreach (var word in part.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var w = ImGui.CalcTextSize(word).X;
                if (w <= maxLineW)
                {
                    tokens.Add(new Token(word, null, w));
                    continue;
                }
                foreach (var chunk in ParsedMessage.BreakToken(word, maxLineW))
                {
                    tokens.Add(new Token(chunk, null, ImGui.CalcTextSize(chunk).X));
                }
            }
        }
        return tokens;
    }

    private void DrawImage(string key, NewsLineDto line, float leftPad, float contentWidth)
    {
        if (!_images.TryGetValue(key, out var tex))
        {
            tex = AvatarDiskCache.Store(_cacheDir, key, line.ImageBytes!);
            _images[key] = tex;
        }
        var wrap = tex?.GetWrapOrDefault();
        if (wrap is null)
        {
            return;
        }

        float natW = line.Width is > 0 ? line.Width.Value : contentWidth;
        float natH = line.Height is > 0 ? line.Height.Value : contentWidth;
        var drawW = Math.Min(contentWidth, natW);
        var drawH = natH * (drawW / natW);

        var indent = Math.Max(0f, (contentWidth - drawW) * 0.5f);
        ImGui.SetCursorPosX(leftPad + indent);

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var rounding = Px(10f);
        dl.AddImageRounded(wrap.Handle, pos, pos + new Vector2(drawW, drawH),
            Vector2.Zero, Vector2.One, 0xFFFFFFFFu, rounding, ImDrawFlags.RoundCornersAll);
        dl.AddRect(pos, pos + new Vector2(drawW, drawH),
            ImGui.GetColorU32(t.Accent with { W = 0.30f }), rounding, ImDrawFlags.None, Px(1.2f));
        ImGui.Dummy(new Vector2(drawW, drawH));
    }
}
