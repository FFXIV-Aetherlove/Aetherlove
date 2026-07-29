using System.Globalization;

namespace AetherLove.Services.Market;

public static class MarketFormat
{
    /// <summary>Compact gil: 999 stays "999", 12,345 becomes "12.3k", 1,234,567 becomes "1.23m".</summary>
    public static string Gil(long value)
    {
        return value switch
        {
            >= 1_000_000_000 => Compact(value / 1_000_000_000.0, "b"),
            >= 1_000_000 => Compact(value / 1_000_000.0, "m"),
            >= 10_000 => Compact(value / 1_000.0, "k"),
            _ => value.ToString("N0", CultureInfo.InvariantCulture),
        };

        static string Compact(double v, string suffix)
        {
            var digits = v >= 100 ? 0 : v >= 10 ? 1 : 2;
            return v.ToString("F" + digits, CultureInfo.InvariantCulture) + suffix;
        }
    }

    public static string GilFull(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
