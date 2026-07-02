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
                // Hard newline: move to the next line and reset the SameLine anchor to x=0.
                ImGui.NewLine();
                ImGui.Dummy(System.Numerics.Vector2.Zero);
                ImGui.SameLine(0, 0);
            }
            DrawLine(lines[li], spaceW);
        }
    }

    // Draws one line preserving the typed spaces: a run of spaces becomes one space-wide gap before the next
    // item. The first token emits no SameLine of its own, so it inherits the gap the previous segment left —
    // that's how a space straddling a text↔emoji boundary survives.
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
            if (ImGui.GetContentRegionAvail().X < wordW + ParsedMessage.WrapSlack)
            {
                ImGui.NewLine();
            }
            ImGui.TextUnformatted(word);
            ImGui.SameLine(0, 0); // keep following content inline; a following space run widens the gap
        }
    }
}
