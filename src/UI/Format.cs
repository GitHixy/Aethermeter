using System;
using System.Globalization;
using Aethermeter.Models;

namespace Aethermeter.UI;

public static class Format
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Number(double value, NumberFormat format)
    {
        if (format == NumberFormat.Full || Math.Abs(value) < 1000)
            return value.ToString(Math.Abs(value) < 100 && value % 1 != 0 ? "0.#" : "#,0", Invariant);

        return Math.Abs(value) switch
        {
            >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.00", Invariant) + "B",
            >= 10_000_000 => (value / 1_000_000).ToString("0.0", Invariant) + "M",
            >= 1_000_000 => (value / 1_000_000).ToString("0.00", Invariant) + "M",
            >= 100_000 => (value / 1_000).ToString("0", Invariant) + "k",
            _ => (value / 1_000).ToString("0.0", Invariant) + "k",
        };
    }

    public static string Percent(double share) => (share * 100).ToString(share >= 0.995 ? "0" : "0.0", Invariant) + "%";

    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", Invariant) : t.ToString(@"m\:ss", Invariant);
    }

    /// <summary>Seconds relative to a moment, for death recaps: "-3.2s".</summary>
    public static string Offset(double seconds) => (seconds >= 0 ? "+" : "") + seconds.ToString("0.0", Invariant) + "s";
}
