using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies.Levels
{
    /// <summary>
    /// Surfaces the Ichimoku Kijun-sen and the Kumo cloud boundaries as <see cref="PriceLevel"/>s
    /// when the Ichimoku indicator is loaded on the active chart. The Kijun is by far the most
    /// commonly used Ichimoku S/R reference; the Kumo top/bottom give the cloud boundary stops
    /// (<see cref="StopSourceKind.BelowKumo"/> and similar) something to resolve against.
    ///
    /// Reads component data via the standard <c>ChartSeries.GetComponentData(name)</c> path.
    /// Component names are case-sensitive — "Kijun-sen", "Senkou Span A", "Senkou Span B" — and
    /// match the strings declared in <c>IchimokuProvider.GetIndicators()</c>.
    /// </summary>
    public class IchimokuLevelProvider : ILevelProvider
    {
        private const string IndicatorCode = "ICHIMOKU";
        private const string CompKijun     = "Kijun-sen";
        private const string CompSenkouA   = "Senkou Span A";
        private const string CompSenkouB   = "Senkou Span B";

        public string SourceId => "ichimoku";

        public IReadOnlyList<PriceLevel> GetLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            if (state?.ActiveSeries == null) return System.Array.Empty<PriceLevel>();
            var series = state.ActiveSeries.FirstOrDefault(s =>
                string.Equals(s.IndicatorCode, IndicatorCode, System.StringComparison.OrdinalIgnoreCase));
            if (series == null) return System.Array.Empty<PriceLevel>();

            // Future-leak fix: only consider component values up to the bar index the strategy
            // is currently evaluating. In live mode, history.Count == data.Length so the clip
            // is a no-op. In backtest mode, history is a bar-i-truncated slice, and reading the
            // full data array would surface Ichimoku values from bars in the future. We clip the
            // valid range to (history.Count - 1) so the LastNonNan walk respects causality.
            int upTo = System.Math.Max(0, history.Count);

            var sink = new List<PriceLevel>();

            double kijun = LastNonNan(series.GetComponentData(CompKijun), upTo);
            if (!double.IsNaN(kijun))
                sink.Add(new PriceLevel(kijun, LevelKind.Kijun, Strength: 0.7, Source: "Ichimoku Kijun"));

            double senkouA = LastNonNan(series.GetComponentData(CompSenkouA), upTo);
            double senkouB = LastNonNan(series.GetComponentData(CompSenkouB), upTo);
            if (!double.IsNaN(senkouA) && !double.IsNaN(senkouB))
            {
                double top = System.Math.Max(senkouA, senkouB);
                double bot = System.Math.Min(senkouA, senkouB);
                sink.Add(new PriceLevel(top, LevelKind.KumoTop,    Strength: 0.7, Source: "Ichimoku Kumo Top"));
                sink.Add(new PriceLevel(bot, LevelKind.KumoBottom, Strength: 0.7, Source: "Ichimoku Kumo Bottom"));
            }

            return sink;
        }

        private static double LastNonNan(double[]? data, int upToExclusive)
        {
            if (data == null || data.Length == 0) return double.NaN;
            int max = System.Math.Min(upToExclusive, data.Length);
            for (int i = max - 1; i >= 0; i--)
                if (!double.IsNaN(data[i])) return data[i];
            return double.NaN;
        }
    }
}
