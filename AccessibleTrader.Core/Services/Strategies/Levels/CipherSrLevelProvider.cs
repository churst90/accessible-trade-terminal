using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies.Levels
{
    /// <summary>
    /// Surfaces Cipher SR pivot lines as <see cref="PriceLevel"/>s. Cipher SR writes its
    /// pivots into two component arrays — "Resistance" and "Support" — with NaN at non-pivot
    /// bars and the pivot price at the bar where the pivot was confirmed. We walk the most
    /// recent slice of those arrays and emit one descriptor per non-NaN entry.
    ///
    /// Pivot strength scales with how far back the pivot was — recent pivots are more relevant
    /// than ancient ones for setting current stops/targets.
    /// </summary>
    public class CipherSrLevelProvider : ILevelProvider
    {
        private const string IndicatorCode  = "CIPHER_SR";
        private const string CompResistance = "Resistance";
        private const string CompSupport    = "Support";

        /// <summary>How many bars back to scan for pivots. Older pivots are dropped to limit noise.</summary>
        public int LookbackBars { get; set; } = 200;

        public string SourceId => "cipher_sr";

        public IReadOnlyList<PriceLevel> GetLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            if (state?.ActiveSeries == null) return System.Array.Empty<PriceLevel>();
            var series = state.ActiveSeries.FirstOrDefault(s =>
                string.Equals(s.IndicatorCode, IndicatorCode, System.StringComparison.OrdinalIgnoreCase));
            if (series == null) return System.Array.Empty<PriceLevel>();

            // Future-leak fix: clip the pivot scan to the bar index the strategy is currently
            // evaluating. In live mode this is a no-op (history.Count == data.Length). In
            // backtest mode it prevents the provider from emitting pivots that hadn't been
            // detected yet at the strategy's current bar.
            int upTo = System.Math.Max(0, history.Count);

            var sink = new List<PriceLevel>();
            CollectPivots(series.GetComponentData(CompResistance), LevelKind.Resistance, sink, "Cipher SR Resistance", upTo);
            CollectPivots(series.GetComponentData(CompSupport),    LevelKind.Support,    sink, "Cipher SR Support",    upTo);
            return sink;
        }

        private void CollectPivots(double[]? data, LevelKind kind, List<PriceLevel> sink, string source, int upToExclusive)
        {
            if (data == null || data.Length == 0) return;
            int total = System.Math.Min(upToExclusive, data.Length);
            int from = System.Math.Max(0, total - LookbackBars);
            for (int i = from; i < total; i++)
            {
                double v = data[i];
                if (double.IsNaN(v)) continue;
                // Recency-weighted strength: most recent pivots get up to 0.9, oldest in window get 0.4.
                double recency = (double)(i - from) / System.Math.Max(1, total - from - 1);
                double strength = 0.4 + 0.5 * recency;
                sink.Add(new PriceLevel(v, kind, Strength: strength, Source: source));
            }
        }
    }
}
