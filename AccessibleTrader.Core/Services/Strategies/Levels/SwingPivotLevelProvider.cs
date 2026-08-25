using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies.Levels
{
    /// <summary>
    /// Computes recent swing highs and swing lows from raw OHLCV history and exposes them as
    /// <see cref="PriceLevel"/>s. A swing high is a bar whose high is higher than the
    /// surrounding <see cref="LookbackBars"/> bars on each side; same mirror logic for swing
    /// lows. The resulting levels are the de facto support/resistance for any chart that
    /// doesn't have a richer pivot indicator (Cipher SR) loaded.
    ///
    /// Used as a fallback by <c>RiskPlanResolver.BelowSupport</c> and the
    /// <c>NextResistance</c> target source when the user hasn't drawn explicit lines and
    /// Cipher SR isn't on the chart.
    /// </summary>
    public class SwingPivotLevelProvider : ILevelProvider
    {
        /// <summary>Number of bars on each side of a candidate that must be lower (high) / higher (low) to qualify as a pivot.</summary>
        public int LookbackBars { get; set; } = 5;

        /// <summary>How many recent pivots to surface (most recent first). Limits noise on long charts.</summary>
        public int MaxPivots { get; set; } = 12;

        public string SourceId => "swing";

        public IReadOnlyList<PriceLevel> GetLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            if (history.Count < LookbackBars * 2 + 1) return System.Array.Empty<PriceLevel>();
            int n = history.Count;
            int win = LookbackBars;

            var sink = new List<PriceLevel>();

            // Walk newest-to-oldest so the MaxPivots cap retains the most recent pivots.
            for (int i = n - win - 1; i >= win && sink.Count < MaxPivots; i--)
            {
                var bar = history[i];

                // Swing high: this bar's high is the max in the [-win, +win] window.
                bool isHigh = true;
                for (int j = i - win; j <= i + win; j++)
                {
                    if (j == i) continue;
                    if (history[j].High >= bar.High) { isHigh = false; break; }
                }
                if (isHigh)
                {
                    sink.Add(new PriceLevel(bar.High, LevelKind.Resistance,
                        Strength: 0.5, Source: $"Swing High @ {bar.Date:MM/dd}"));
                    continue;
                }

                // Swing low (mirror).
                bool isLow = true;
                for (int j = i - win; j <= i + win; j++)
                {
                    if (j == i) continue;
                    if (history[j].Low <= bar.Low) { isLow = false; break; }
                }
                if (isLow)
                {
                    sink.Add(new PriceLevel(bar.Low, LevelKind.Support,
                        Strength: 0.5, Source: $"Swing Low @ {bar.Date:MM/dd}"));
                }
            }

            return sink;
        }
    }
}
