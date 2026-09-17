using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>Wraps a card's text once so the face draws cached lines. Breaks on a space a line may end on when
/// it can and on a text element otherwise, so a long token or a script without spaces still wraps and a
/// surrogate pair or a combining sequence is never split. A no-break space never ends a line, which keeps a
/// grouped number such as 1 000 whole.</summary>
internal static class CardText
{
    /// <summary>Measured to tell two fonts of the same size apart, so a cached wrap follows a font change.</summary>
    public const string MetricSample = "MW";
    private const string Ellipsis = "…";

    public static string[] Wrap(string text, float width, Func<string, float> measure, int maxLines) =>
        Wrap(text, width, measure, maxLines, out _);

    /// <summary>Wraps like <see cref="Wrap(string, float, Func{string, float}, int)"/> and says whether the whole
    /// text fits in <paramref name="maxLines"/> lines with every break at a space: no ellipsis and no word cut
    /// in two.</summary>
    public static bool TryWrapWhole(string text, float width, Func<string, float> measure, int maxLines, out string[] lines)
    {
        lines = Wrap(text, width, measure, maxLines, out var whole);
        return whole;
    }

    public static string Ellipsize(string text, float width, Func<string, float> measure)
    {
        if (measure(text) <= width)
        {
            return text;
        }

        if (measure(Ellipsis) > width)
        {
            return string.Empty;
        }

        var starts = StringInfo.ParseCombiningCharacters(text);
        for (var i = starts.Length - 1; i >= 0; i--)
        {
            var candidate = text[..starts[i]].TrimEnd() + Ellipsis;
            if (measure(candidate) <= width)
            {
                return candidate;
            }
        }

        return Ellipsis;
    }

    private static string[] Wrap(string text, float width, Func<string, float> measure, int maxLines, out bool whole)
    {
        whole = true;
        if (string.IsNullOrWhiteSpace(text) || width <= 0f || maxLines <= 0)
        {
            whole = string.IsNullOrWhiteSpace(text);
            return [];
        }

        var lines = new List<string>();
        var remaining = text.Replace("\r", string.Empty).Trim();
        while (remaining.Length > 0 && lines.Count < maxLines)
        {
            if (lines.Count == maxLines - 1)
            {
                var flat = remaining.Replace('\n', ' ');
                var last = Ellipsize(flat, width, measure);
                whole &= last == flat;
                lines.Add(last);
                remaining = string.Empty;
                break;
            }

            var starts = StringInfo.ParseCombiningCharacters(remaining);
            var end = remaining.Length;
            var lastSpace = -1;
            for (var i = 0; i < starts.Length; i++)
            {
                var next = i + 1 < starts.Length ? starts[i + 1] : remaining.Length;
                if (remaining[starts[i]] == '\n')
                {
                    end = starts[i];
                    break;
                }

                if (IsBreakSpace(remaining[starts[i]]))
                {
                    lastSpace = starts[i];
                }

                if (measure(remaining[..next]) > width)
                {
                    whole &= lastSpace > 0;
                    end = lastSpace > 0 ? lastSpace : i > 0 ? starts[i] : next;
                    break;
                }
            }

            var line = remaining[..end].TrimEnd();
            if (line.Length > 0)
            {
                var shown = Ellipsize(line, width, measure);
                whole &= shown == line;
                lines.Add(shown);
            }

            remaining = remaining[end..].TrimStart();
        }

        whole &= remaining.Length == 0;
        return lines.ToArray();
    }

    /// <summary>Whitespace a line may end on. No-break, figure and narrow no-break spaces hold their
    /// neighbours together.</summary>
    private static bool IsBreakSpace(char c) => char.IsWhiteSpace(c) && c is not (' ' or ' ' or ' ');
}

/// <summary>Wrapped paragraphs drawn at the cursor in the current font and ink. Lines break only where
/// <see cref="CardText"/> breaks, so a rule never splits a number the way ImGui's own wrap does after a
/// point or a comma ("1." then "025"). The lines of one paragraph sit as close as <c>ImGui.TextWrapped</c>
/// sets them. Wraps are cached per text, width and font.</summary>
internal sealed class CardParagraphs
{
    private const int MaxCached = 128;

    private readonly Dictionary<(string Text, float Width, float FontSize, float Metric), string[]> _wrapped = new();

    /// <summary>Draws <paramref name="text"/> from window-local x <paramref name="left"/>, wrapped to
    /// <paramref name="width"/>, left-aligned, one text item per line.</summary>
    public void Draw(string text, float left, float width)
    {
        var lines = Lines(text, width);
        if (lines.Length == 0)
        {
            ImGui.SetCursorPosX(left);
            ImGui.TextUnformatted(string.Empty);
            return;
        }

        var tight = ImGui.GetStyle().ItemSpacing with { Y = 0f };
        for (var i = 0; i < lines.Length; i++)
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, tight, i < lines.Length - 1))
            {
                ImGui.SetCursorPosX(left);
                ImGui.TextUnformatted(lines[i]);
            }
        }
    }

    /// <summary>The cached wrap of <paramref name="text"/> at <paramref name="width"/> in the pushed font, for a
    /// caller that places the lines itself.</summary>
    public string[] Lines(string text, float width)
    {
        var key = (text, MathF.Round(width), ImGui.GetFontSize(), ImGui.CalcTextSize(CardText.MetricSample).X);
        if (_wrapped.TryGetValue(key, out var lines))
        {
            return lines;
        }

        if (_wrapped.Count >= MaxCached)
        {
            _wrapped.Clear();
        }

        lines = CardText.Wrap(text, key.Item2, value => ImGui.CalcTextSize(value).X, int.MaxValue);
        _wrapped[key] = lines;
        return lines;
    }
}
