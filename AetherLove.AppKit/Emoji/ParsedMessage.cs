// Attribution: Derived from XIVInstantMessenger's ParsedMessage
// Source: https://github.com/NightmareXIV/XIVInstantMessenger

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using AetherLove.Emoji.Segments;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Emoji;

/// <summary>A chat or bio string broken into renderable segments (text + emoji).</summary>
public sealed class ParsedMessage
{
    private static readonly Regex EmojiPattern =
        new(@"(:[a-z0-9_-]+:)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, ParsedMessage> Cache = new();

    // Manual word-wrap breaks this many px before ImGui's PushTextWrapPos edge would, so the two wrap
    // authorities can't disagree at a sub-pixel boundary and force a word to break mid-glyph at line end.
    internal const float WrapSlack = 2f;

    /// <summary>End of the wrap token starting at <paramref name="i"/>: a single Japanese character
    /// (Japanese breaks between any two characters), or the run up to the next space or Japanese character.
    /// Shared by the renderer and <see cref="MeasureHeight"/> so their line breaks can't diverge; a
    /// spaceless Japanese sentence measured as one giant word would clip the bubble's tail.</summary>
    internal static int WrapTokenEnd(string line, int i)
    {
        if (IsJapanese(line, i, out var len))
        {
            return i + len;
        }
        while (i < line.Length && line[i] != ' ' && !IsJapanese(line, i, out _))
        {
            i++;
        }
        return i;
    }

    private static bool IsJapanese(string s, int i, out int len)
    {
        len = 1;
        int cp = s[i];
        if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            cp = char.ConvertToUtf32(s[i], s[i + 1]);
            len = 2;
        }
        return cp is (>= 0x3000 and <= 0x30FF)  // CJK punctuation, hiragana, katakana
            or (>= 0x31F0 and <= 0x31FF)        // katakana phonetic extensions
            or (>= 0x3400 and <= 0x4DBF)        // CJK ideographs extension A
            or (>= 0x4E00 and <= 0x9FFF)        // CJK unified ideographs
            or (>= 0xF900 and <= 0xFAFF)        // CJK compatibility ideographs
            or (>= 0xFF00 and <= 0xFFEF)        // full-width forms, half-width katakana
            or (>= 0x20000 and <= 0x2FA1F);     // CJK ideographs extensions B and beyond
    }

    /// <summary>Greedily splits an over-wide, unbroken token (e.g. a long URL) into chunks that each fit
    /// within <paramref name="lineW"/>. Surrogate-pair aware; shared by the renderer and
    /// <see cref="MeasureHeight"/> so their line counts stay identical.</summary>
    internal static List<string> BreakToken(string token, float lineW)
    {
        var chunks = new List<string>();
        if (lineW < 1f || token.Length == 0)
        {
            chunks.Add(token);
            return chunks;
        }

        var start = 0;
        while (start < token.Length)
        {
            var end = start;
            while (end < token.Length)
            {
                var step = char.IsHighSurrogate(token[end]) && end + 1 < token.Length
                           && char.IsLowSurrogate(token[end + 1]) ? 2 : 1;
                // Always keep at least one glyph; stop once adding this one would overflow the line.
                if (end > start && ImGui.CalcTextSize(token.Substring(start, end + step - start)).X > lineW)
                {
                    break;
                }
                end += step;
            }
            chunks.Add(token.Substring(start, end - start));
            start = end;
        }
        return chunks;
    }

    public static ParsedMessage Parse(string text)
    {
        var key = text.Trim();
        if (!Cache.TryGetValue(key, out var msg))
        {
            Cache[key] = msg = new ParsedMessage(key);
        }
        return msg;
    }

    public readonly ISegment[] Segments;

    private ParsedMessage(string text)
    {
        var parts = EmojiPattern.Split(text).Where(p => p.Length > 0).ToArray();
        var segments = new List<ISegment>(parts.Length);

        foreach (var part in parts)
        {
            // Length > 2 guards against a bare ":" slicing part[1..^1] negative.
            if (part.Length > 2 && part[0] == ':' && part[^1] == ':')
            {
                var name = part[1..^1];
                segments.Add(parts.Length == 1
                    ? new SegmentDoubleEmoji(name)
                    : new SegmentEmoji(name));
            }
            else
            {
                segments.Add(new SegmentText(part));
            }
        }

        Segments = [.. segments];
    }

    public void Draw()
    {
        // Zero-size anchor so a leading SegmentEmoji's SameLine() has a reference.
        ImGui.Dummy(Vector2.Zero);
        ImGui.SameLine(0, 0);

        foreach (var seg in Segments)
        {
            seg.Draw();
        }

        ImGui.NewLine();
    }

    /// <summary>Renders wrapped inside a padding-free child of the measured height, so the manual
    /// word/emoji layout and ImGui's wrap share the same right edge (avoids mid-word breaks).</summary>
    public void DrawWrapped(string childId, float width)
    {
        var height = MeasureHeight(width);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using (var child = Dalamud.Interface.Utility.Raii.ImRaii.Child(
                   childId, new Vector2(width, height), false,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (child.Success)
            {
                ImGui.PushTextWrapPos(width);
                Draw();
                ImGui.PopTextWrapPos();
            }
        }
        ImGui.PopStyleVar();
    }

    /// <summary>Wrapped pixel height at the given width, mirroring <see cref="Draw"/>'s word/emoji
    /// layout. Adds one trailing ItemSpacing to match the renderer's final NewLine.</summary>
    public float MeasureHeight(float width)
    {
        var lineH = ImGui.GetTextLineHeight();
        if (width < 1f)
        {
            return lineH;
        }

        var spaceW = ImGui.CalcTextSize(" ").X;
        var spacingY = ImGui.GetStyle().ItemSpacing.Y;

        var x = 0f; // pen x on the current line
        var completed = 0f; // summed height of finished lines (each incl. trailing ItemSpacing.Y)
        var curLineH = lineH; // tallest element on the current line (a 2x emoji makes it taller)

        void Break()
        {
            completed += curLineH + spacingY;
            x = 0f;
            curLineH = lineH;
        }

        foreach (var seg in Segments)
        {
            if (seg is SegmentText textSeg)
            {
                // Mirror SegmentText.Draw(): split on \n so hard newlines aren't measured as wide words.
                var textLines = textSeg.Text.Split('\n');
                for (int li = 0; li < textLines.Length; li++)
                {
                    if (li > 0)
                    {
                        Break();
                    }
                    var line = textLines[li];
                    var i = 0;
                    while (i < line.Length)
                    {
                        if (line[i] == ' ')
                        {
                            var spaceStart = i;
                            while (i < line.Length && line[i] == ' ')
                            {
                                i++;
                            }
                            x += (i - spaceStart) * spaceW;
                            continue;
                        }

                        var wordStart = i;
                        i = WrapTokenEnd(line, i);
                        var word = line.Substring(wordStart, i - wordStart);
                        var wordW = ImGui.CalcTextSize(word).X;
                        if (x > 0f && width - x < wordW + WrapSlack)
                        {
                            Break();
                        }
                        if (wordW > width)
                        {
                            // Mirror the renderer's per-character break so the reserved height matches.
                            var chunks = BreakToken(word, width);
                            for (var r = 1; r < chunks.Count; r++)
                            {
                                Break();
                            }
                            x = ImGui.CalcTextSize(chunks[^1]).X;
                        }
                        else
                        {
                            x += wordW;
                        }
                    }
                }
            }
            else
            {
                // floor(lineH * mult) square; double-emoji is 2x.
                var size = MathF.Floor(lineH * (seg is SegmentDoubleEmoji ? 2f : 1f));
                var lead = seg is SegmentDoubleEmoji ? 0f : 2f;
                if (x > 0f && width - x < size)
                {
                    Break();
                }
                x += size + lead;
                if (size > curLineH)
                {
                    curLineH = size;
                }
            }
        }

        return completed + curLineH + spacingY;
    }

    public string PlainText =>
        string.Concat(Segments.Select(s => s is SegmentText st ? st.Text : " "));

    /// <summary>True when the message renders anything; whitespace plus unknown <c>:shortcodes:</c> (which
    /// draw as nothing) counts as empty.</summary>
    public bool HasVisibleContent => Segments.Any(s => s switch
    {
        SegmentText t => !string.IsNullOrWhiteSpace(t.Text),
        SegmentEmoji e => UiHost.EmojiService.GetEmoji(e.EmojiName) is not null,
        _ => false,
    });
}
