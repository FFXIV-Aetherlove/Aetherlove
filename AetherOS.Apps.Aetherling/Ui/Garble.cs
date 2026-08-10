using System;
using System.Text;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>Text that refuses to be read. Every result is a pure function of its seed, so a line does not
/// shimmer between frames; it looks corrupted, not animated.</summary>
internal static class Garble
{
    /// <summary>Replacements are drawn from here. Deliberately Latin-1 and ASCII only: the phone's font is
    /// the game's, and a glyph it lacks renders as an empty box, which reads as a bug rather than as static.
    /// </summary>
    private const string Runes = "?¿!¡#%&*+=<>@\\|/~^§°±×÷¤·";

    private const string Consonants = "kzthvrsnmxqdgl";
    private const string Vowels = "aeiouy";

    /// <summary>Corrupts a real string, keeping its shape. <paramref name="legibility"/> 1 returns it
    /// untouched, 0 leaves nothing readable. Spaces and line breaks always survive so it still scans as
    /// language.</summary>
    public static string Corrupt(string source, int seed, float legibility)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c is ' ' or '\n' or '\r')
            {
                sb.Append(c);
                continue;
            }
            var roll = Roll(seed, i);
            if (roll < legibility)
            {
                sb.Append(c);
                continue;
            }
            sb.Append(Runes[(int)(Roll(seed, i + 7919) * Runes.Length) % Runes.Length]);
        }
        return sb.ToString();
    }

    /// <summary>A paragraph of nothing: pseudo-words that carry the rhythm of a sentence and none of its
    /// meaning, then corrupted on top. Used where there is no real string to hide, so no language leaks.</summary>
    public static string Block(int seed, int words, float legibility)
    {
        var sb = new StringBuilder();
        for (var w = 0; w < words; w++)
        {
            if (w > 0)
            {
                sb.Append(' ');
            }
            var length = 2 + (int)(Roll(seed, w * 31) * 6f);
            for (var i = 0; i < length; i++)
            {
                var pool = i % 2 == 0 ? Consonants : Vowels;
                sb.Append(pool[(int)(Roll(seed, (w * 97) + (i * 13)) * pool.Length) % pool.Length]);
            }
            if (Roll(seed, w * 53) > 0.82f)
            {
                sb.Append(',');
            }
        }
        if (sb.Length > 0 && sb[^1] == ',')
        {
            sb.Length--;
        }
        sb.Append('.');
        return Corrupt(sb.ToString(), seed + 1, legibility);
    }

    /// <summary>Wraps to a column without measuring: the caller draws with a fixed font size, and a rough
    /// break is fine for text nobody can read anyway.</summary>
    public static string Wrap(string source, int columns)
    {
        var sb = new StringBuilder(source.Length + 8);
        var column = 0;
        foreach (var word in source.Split(' '))
        {
            if (column > 0 && column + word.Length > columns)
            {
                sb.Append('\n');
                column = 0;
            }
            else if (column > 0)
            {
                sb.Append(' ');
                column++;
            }
            sb.Append(word);
            column += word.Length;
        }
        return sb.ToString();
    }

    /// <summary>A stable 0..1 from two integers. Not random, just well mixed: the same pair always gives the
    /// same answer, which is the whole point.</summary>
    private static float Roll(int seed, int index)
    {
        unchecked
        {
            var h = (uint)(seed * 374761393) + (uint)(index * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
