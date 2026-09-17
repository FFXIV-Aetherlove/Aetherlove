using System.Collections.Generic;

namespace AetherOS.Apps.Racer.Localization;

public static partial class AppStrings
{
    /// <summary>Merges the statistics page's strings of one language into a copy of <paramref name="source"/>.
    /// Index 0 is English, then DE, ES, FR, PT, RU, the order <see cref="Packs"/> uses. The rows are local rather
    /// than a static field because <see cref="Packs"/>, in another file of this partial class, may initialise first.</summary>
    private static IReadOnlyDictionary<string, string> WithStats(IReadOnlyDictionary<string, string> source, int language)
    {
        // added after update 2.7.0 (statistics page)
        // os.racer_log_place takes an ordinal: os.racer_ordinal_1 to _3 for the first three places, os.racer_ordinal_n
        // with the place number for the rest.
        string[][] rows =
        [
            ["os.racer_log_place", "You came in {0} place.", "Du hast den {0} Platz belegt.", "Quedaste en {0} lugar.", "Tu as fini à la {0} place.", "Ficaste em {0} lugar.", "Твой Луми занял {0} место."],
            ["os.racer_ordinal_1", "1st", "1.", "1.º", "1re", "1.º", "1-е"],
            ["os.racer_ordinal_2", "2nd", "2.", "2.º", "2e", "2.º", "2-е"],
            ["os.racer_ordinal_3", "3rd", "3.", "3.º", "3e", "3.º", "3-е"],
            ["os.racer_ordinal_n", "{0}th", "{0}.", "{0}.º", "{0}e", "{0}.º", "{0}-е"],
        ];
        var result = new Dictionary<string, string>(source.Count + rows.Length);
        foreach (var pair in source)
        {
            result[pair.Key] = pair.Value;
        }
        foreach (var row in rows)
        {
            result[row[0]] = row[language + 1];
        }
        return result;
    }
}
