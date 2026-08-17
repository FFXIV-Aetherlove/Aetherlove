using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Eordle.Words;

/// <summary>The word-list language, chosen on the splash independently of the phone language.</summary>
public enum WordLanguage
{
    En = 0,
    De = 1,
    Fr = 2,
}

/// <summary>The word banks. Each language has a curated ANSWERS pool and a wider VALID set (answers plus
/// accepted guesses); the shared FFXIV list below is merged into both for every language. Everything is
/// uppercase A-Z only: German words are chosen to need no umlaut or eszett, French accents are normalized
/// away (the Motus convention). Raw lists live in the sibling files as whitespace-separated constants and
/// are parsed once at static init, dropping any token that is not exactly five A-Z letters.</summary>
internal static partial class EordleWords
{
    public const int Length = 5;

    /// <summary>Five-letter FFXIV terms every language accepts and occasionally asks.</summary>
    private const string FfxivRaw = @"
IFRIT SHIVA TITAN RAMUH VIERA LEVIN SYLPH IXALI GNATH KOJIN LUPIN PIXIE
DWARF XAELA LIMSA ULDAH MHACH ALLAG ELPIS BOZJA PAGOS PYROS TURAL GUBAL
AURUM MIDAS ASURA HADES VENAT VRTRA ZENOS GAIUS LIVIA BIGGS WEDGE HILDA
KRILE GRAHA PIPIN ASAHI ANIMA UMBRA TOPAZ OMEGA IXION KEFKA RAMZA SIREN
ALPHA NINJA VIPER MARID GIGAS HYDRA GOLEM
";

    private static readonly string[] AnswersEn = BuildAnswers(AnswersEnRaw);
    private static readonly string[] AnswersDe = BuildAnswers(AnswersDeRaw);
    private static readonly string[] AnswersFr = BuildAnswers(AnswersFrRaw);

    private static readonly HashSet<string> ValidEn = BuildValid(AnswersEnRaw, GuessesEnRaw);
    private static readonly HashSet<string> ValidDe = BuildValid(AnswersDeRaw, GuessesDeRaw);
    private static readonly HashSet<string> ValidFr = BuildValid(AnswersFrRaw, GuessesFrRaw);

    public static IReadOnlyList<string> AnswersFor(WordLanguage language) => language switch
    {
        WordLanguage.De => AnswersDe,
        WordLanguage.Fr => AnswersFr,
        _ => AnswersEn,
    };

    public static bool IsValid(WordLanguage language, string word) => language switch
    {
        WordLanguage.De => ValidDe.Contains(word),
        WordLanguage.Fr => ValidFr.Contains(word),
        _ => ValidEn.Contains(word),
    };

    private static string[] BuildAnswers(string answersRaw)
    {
        var set = new HashSet<string>();
        AddParsed(set, answersRaw);
        AddParsed(set, FfxivRaw);
        var array = new string[set.Count];
        set.CopyTo(array);
        Array.Sort(array);
        return array;
    }

    private static HashSet<string> BuildValid(string answersRaw, string guessesRaw)
    {
        var set = new HashSet<string>();
        AddParsed(set, answersRaw);
        AddParsed(set, guessesRaw);
        AddParsed(set, FfxivRaw);
        return set;
    }

    private static void AddParsed(HashSet<string> into, string raw)
    {
        var start = -1;
        for (var i = 0; i <= raw.Length; i++)
        {
            var isLetter = i < raw.Length && raw[i] >= 'A' && raw[i] <= 'Z';
            if (isLetter)
            {
                if (start < 0)
                {
                    start = i;
                }
                continue;
            }
            if (start >= 0)
            {
                if (i - start == Length)
                {
                    into.Add(raw.Substring(start, Length));
                }
                start = -1;
            }
        }
    }
}
