using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    /// <summary>
    /// The levels a Fibonacci retracement or extension draws, where each one sits, and what it is
    /// called — one place, so the line drawn, the line spoken and the line alerted on are the same.
    ///
    /// <para>
    /// TradingView's orientation and TradingView's default levels (Cody, 2026-11-01: fibs drawn
    /// here have to read like the ones in the tutorials). A RETRACEMENT puts 0 at the swing END
    /// and 1 at the swing START, so "61.8%" is how far price has given the swing back: drawn
    /// from a low to a high, 61.8% sits below the high. This drew it the other way round
    /// (0 at the start), so every level from 23.6 to 78.6 had the other level's name. An
    /// EXTENSION projects the swing from its third point: 1 is one whole swing from there.
    /// </para>
    ///
    /// <para>Custom levels are <see cref="DrawingData.FibLevels"/>, set in Properties; null or
    /// empty means these defaults.</para>
    /// </summary>
    public static class FibLevels
    {
        public static readonly IReadOnlyList<double> Defaults =
            new[] { 0, 0.236, 0.382, 0.5, 0.618, 0.786, 1, 1.618, 2.618, 3.618, 4.236 };

        /// <summary>The levels this drawing draws: its own, sorted and without repeats, or the
        /// defaults.</summary>
        public static IReadOnlyList<double> For(DrawingData d)
        {
            var own = d.FibLevels?.Where(double.IsFinite).Distinct().OrderBy(l => l).ToList();
            return own is { Count: > 0 } ? own : Defaults;
        }

        /// <summary>Where <paramref name="level"/> sits on this drawing, or null without the
        /// points it needs.</summary>
        public static double? PriceAt(DrawingData d, double level)
        {
            if (d.AnchorPrice1 is not double p1 || d.AnchorPrice2 is not double p2) return null;
            if (d.Type == DrawingType.FibExtension)
                return d.AnchorPrice3 is double p3 ? p3 + (p2 - p1) * level : null;
            return p2 + (p1 - p2) * level;
        }

        /// <summary>The component key a level's line is stored under. The format the calculators
        /// always used, so a saved drawing keeps its line styles for the levels it had.</summary>
        public static string Key(double level, DrawingType type) =>
            string.Create(CultureInfo.InvariantCulture,
                $"{level * 100:F1}% {(type == DrawingType.FibExtension ? "Ext" : "Level")}");

        /// <summary>A key read back to its level, or null for a key that is not a fib level's.</summary>
        public static double? LevelOf(string key)
        {
            int pct = key.IndexOf('%');
            if (pct <= 0) return null;
            return double.TryParse(key[..pct], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v / 100 : null;
        }

        /// <summary>A level as said: "61.8%", "0%", "161.8%".</summary>
        public static string Name(double level) =>
            (level * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";

        /// <summary>The per-bar lines: one flat line per level.</summary>
        public static Dictionary<string, double[]> Lines(DrawingData d, int count)
        {
            var results = new Dictionary<string, double[]>();
            foreach (var level in For(d))
            {
                if (PriceAt(d, level) is not double price) return new Dictionary<string, double[]>();
                var data = new double[count];
                Array.Fill(data, price);
                results[Key(level, d.Type)] = data;
            }
            return results;
        }

        /// <summary>
        /// Levels typed in Properties: numbers separated by commas or spaces, as fractions the way
        /// TradingView writes them (0.618), or as percentages with a % sign (61.8%). Null when
        /// any entry is not a number; an empty list when nothing was typed (the defaults).
        /// </summary>
        public static List<double>? Parse(string? raw)
        {
            var result = new List<double>();
            foreach (var part in (raw ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool percent = part.EndsWith('%');
                string num = percent ? part[..^1] : part;
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
                    return null;
                result.Add(percent ? v / 100 : v);
            }
            return result.Distinct().OrderBy(l => l).ToList();
        }

        /// <summary>The levels as Properties shows them: "0, 0.236, 0.382, …".</summary>
        public static string Format(IEnumerable<double> levels) =>
            string.Join(", ", levels.Select(l => l.ToString("0.####", CultureInfo.InvariantCulture)));
    }
}
