// Attribution: Derived from XIVInstantMessenger's SegmentText
// Source: https://github.com/NightmareXIV/XIVInstantMessenger

using System;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Emoji.Segments;

/// <summary>A plain-text segment that renders inline using the active font and wrap position.</summary>
public sealed class SegmentText : ISegment
{
    public readonly string Text;

    public SegmentText(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public void Draw()
    {
        // Manual word-by-word wrapping; ImGui.TextWrapped would mislay the trailing SameLine
        // for following segments (e.g. emoji) and push content off the right edge.
        var spaceW = ImGui.CalcTextSize(" ").X;
        var lines = Text.Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            if (li > 0)
            {
                // Hard newline: reset the SameLine anchor to x=0.
                ImGui.NewLine();
                ImGui.Dummy(System.Numerics.Vector2.Zero);
                ImGui.SameLine(0, 0);
            }
            DrawLine(lines[li], spaceW);
        }
    }

    // Preserves typed spaces: a run of spaces becomes one gap before the next item. The first token emits
    // no SameLine of its own, so a space straddling a text/emoji boundary survives.
    private static void DrawLine(string line, float spaceW)
    {
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
                ImGui.SameLine(0, (i - spaceStart) * spaceW);
                continue;
            }

            var start = i;
            i = ParsedMessage.WrapTokenEnd(line, i);
            var word = line.Substring(start, i - start);
            var wordW = ImGui.CalcTextSize(word).X;
            var lineW = ImGui.GetContentRegionMax().X;
            var avail = ImGui.GetContentRegionAvail().X;
            // Wrap to a new line only when we're mid-line and the token doesn't fit (mirrors MeasureHeight;
            // an over-wide token on a fresh line must NOT get a spurious blank line before it).
            if (avail < lineW - 0.5f && avail < wordW + ParsedMessage.WrapSlack)
            {
                ImGui.NewLine();
            }
            if (wordW <= lineW || lineW < 1f)
            {
                ImGui.TextUnformatted(word);
                ImGui.SameLine(0, 0);
            }
            else
            {
                // A token wider than the whole line: break it by character so it wraps instead of clipping.
                var chunks = ParsedMessage.BreakToken(word, lineW);
                for (int c = 0; c < chunks.Count; c++)
                {
                    if (c > 0)
                    {
                        ImGui.NewLine();
                    }
                    ImGui.TextUnformatted(chunks[c]);
                    ImGui.SameLine(0, 0);
                }
            }
        }
    }
}
