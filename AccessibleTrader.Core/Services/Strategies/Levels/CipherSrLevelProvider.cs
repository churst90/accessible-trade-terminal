using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies.Levels
{
    /// <summary>
    /// Surfaces Cipher SR pivot lines as <see cref="PriceLevel"/>s. Cipher SR writes its
    /// pivots into two component arrays — "Resistance" and "Support" — with NaN at non-pivot
    /// bars and the pivot price AT THE PIVOT BAR ITSELF. We walk the most recent slice of those
    /// arrays and emit one descriptor per non-NaN entry.
    ///
    /// <para>
    /// CONFIRMATION LAG (fixed 2026-07-26). A pivot at bar p is not KNOWABLE until bar
    /// p + PivotBars, because every one of the next PivotBars bars must fail to exceed it.
    /// Clipping only to the current bar — which is what this provider used to do — still let a
    /// backtest read a pivot two bars old that needed three future bars to confirm. In effect it
    /// told the strategy "this is the low" while the low was still forming, which is the single
    /// most flattering bias a level-based system can have. Live is unaffected (the provider
    /// cannot see forward bars at all), so this only ever mattered in backtests — where it
    /// mattered a great deal: an experiment measuring signal quality near SR levels showed a
    /// +0.739R edge at p=0.0002 before this fix and nothing at all after it.
    /// </para>
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

            // Clip to the current bar AND back off by the pivot confirmation lag.
            int lag = ResolveConfirmationLag(series.Config?.Parameters, history.Count);
            int upTo = System.Math.Max(0, history.Count - lag);

            var sink = new List<PriceLevel>();
            CollectPivots(series.GetComponentData(CompResistance), LevelKind.Resistance, sink, "Cipher SR Resistance", upTo);
            CollectPivots(series.GetComponentData(CompSupport),    LevelKind.Support,    sink, "Cipher SR Support",    upTo);
            return sink;
        }

        /// <summary>
        /// Bars a Cipher SR pivot needs before it can be known. Mirrors the provider: the
        /// explicit PivotBars parameter unless AutoScale is on, in which case
        /// <c>clamp(barCount / 25, 2, 15)</c>. Defaults to the AutoScale form because AutoScale
        /// defaults to ON — guessing low here would silently reintroduce the leak.
        /// </summary>
        internal static int ResolveConfirmationLag(IReadOnlyDictionary<string, double>? parameters, int barCount)
        {
            bool autoScale = true;
            if (parameters != null && parameters.TryGetValue("AutoScale", out double auto))
                autoScale = auto != 0;

            if (!autoScale && parameters != null && parameters.TryGetValue("PivotBars", out double pivotBars))
                return System.Math.Clamp((int)pivotBars, 2, 60);

            return System.Math.Clamp(barCount / 25, 2, 15);
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
