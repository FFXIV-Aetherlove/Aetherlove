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
                // Mirror SegmentText.Draw(): split on \n first so hard newlines are accounted
                // for in the height measurement, not treated as wide "words".
                var textLines = textSeg.Text.Split('\n');
                for (int li = 0; li < textLines.Length; li++)
                {
                    if (li > 0) // hard newline resets the pen to x=0
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
                            x += (i - spaceStart) * spaceW; // every typed space advances the pen
                            continue;
                        }

                        var wordStart = i;
                        while (i < line.Length && line[i] != ' ')
                        {
                            i++;
                        }
                        var word = line.Substring(wordStart, i - wordStart);
                        var wordW = ImGui.CalcTextSize(word).X;
                        if (x > 0f && width - x < wordW + WrapSlack)
                        {
                            Break();
                        }
                        if (wordW > width)
                        {
                            // Word wider than the line wraps mid-word (via PushTextWrapPos).
                            var rows = (int)MathF.Ceiling(wordW / width);
                            for (var r = 1; r < rows; r++)
                            {
                                Break();
                            }
                            x = wordW - (rows - 1) * width;
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

    /// <summary>True if the message renders anything: visible text, or at least one shortcode that resolves
    /// to a real emoji. A message of only whitespace and unknown <c>:shortcodes:</c> (which draw as nothing)
    /// is treated as empty, so it can be blocked before sending an empty bubble.</summary>
    public bool HasVisibleContent => Segments.Any(s => s switch
    {
        SegmentText t => !string.IsNullOrWhiteSpace(t.Text),
        SegmentEmoji e => Plugin.EmojiService.GetEmoji(e.EmojiName) is not null,
        _ => false,
    });
}
