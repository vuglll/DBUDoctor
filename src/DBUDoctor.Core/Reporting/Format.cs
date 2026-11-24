using System.Globalization;

namespace DBUDoctor.Core.Reporting;

/// <summary>
/// Number and money formatting. Amounts of 100 or more round to whole units, smaller ones to two
/// decimals, midpoints away from zero. Rounding happens here only: totals come from unrounded values.
/// </summary>
public static class Format
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static decimal Round(decimal value) =>
        Math.Abs(value) >= 100
            ? Math.Round(value, 0, MidpointRounding.AwayFromZero)
            : Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>The amount without a currency sign: <c>1,657</c> or <c>12.60</c>.</summary>
    public static string Amount(decimal value)
    {
        var rounded = Round(Math.Abs(value));
        return rounded >= 100 ? rounded.ToString("#,0", Invariant) : rounded.ToString("#,0.00", Invariant);
    }

    /// <summary><c>$1,657</c>, <c>-$12.60</c>. Non-USD currencies are written as a code suffix.</summary>
    public static string Money(decimal value, string currency = "USD")
    {
        var sign = Round(value) < 0 ? "-" : "";
        return currency == "USD" ? $"{sign}${Amount(value)}" : $"{sign}{Amount(value)} {currency}";
    }

    /// <summary><c>+$1,657</c>, <c>-$12.60</c>, <c>$0</c>.</summary>
    public static string SignedMoney(decimal value, string currency = "USD")
    {
        var rounded = Round(value);
        if (rounded == 0)
        {
            return Money(0, currency).Replace("0.00", "0", StringComparison.Ordinal);
        }

        return rounded > 0 ? "+" + Money(value, currency) : Money(value, currency);
    }

    /// <summary>Counts such as runs per month: <c>2,920</c>, <c>30.4</c>.</summary>
    public static string Number(decimal value)
    {
        return Math.Abs(value) >= 100
            ? Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("#,0", Invariant)
            : Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("#,0.#", Invariant);
    }

    /// <summary><c>4.0×</c>.</summary>
    public static string Multiplier(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.0", Invariant) + "×";

    /// <summary>A fraction as a whole percentage: 0.545 → <c>55%</c>.</summary>
    public static string Percent(decimal fraction) =>
        Math.Round(fraction * 100, 0, MidpointRounding.AwayFromZero).ToString("0", Invariant) + "%";

    /// <summary>A signed percentage: <c>+480%</c>.</summary>
    public static string SignedPercent(decimal fraction)
    {
        var text = Percent(fraction);
        return fraction > 0 && text != "0%" ? "+" + text : text;
    }

    /// <summary>Decimal without trailing zeros, for JSON and catalog values.</summary>
    public static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;
}
