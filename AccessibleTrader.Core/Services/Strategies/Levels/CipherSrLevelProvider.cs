using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies.Levels
{
    /// <summary>
    /// Surfaces Cipher SR's levels as <see cref="PriceLevel"/>s: one per level the "Resistance Zone"
    /// and "Support Zone" lines took up inside the look-back, at the price they held.
    ///
    /// <para>
    /// The zone lines are causal — a level appears on the bar that could first know it
    /// (CipherSrProvider) — so a level is visible from the bar it starts on and needs no
    /// confirmation lag. Until 2026-10-17 this read the pivot-bar DOTS, which sat on the pivot bar
    /// itself, and had to back off by a guessed confirmation lag to avoid reading a level before it
    /// could be known (the 2026-07-26 fix, worth +0.739R of illusory edge in one backtest). The dots
    /// were retired when Cipher SR was rebuilt to Market Cipher SR's design; the lag went with them.
    /// </para>
    ///
    /// Strength is recency-weighted: recent levels matter more for stops and targets.
    /// </summary>
    public class CipherSrLevelProvider : ILevelProvider
    {
        private const string IndicatorCode = "CIPHER_SR";
        private const string CompResistanceZone = "Resistance Zone";
        private const string CompSupportZone    = "Support Zone";

        /// <summary>How many bars back to look for levels. Older ones are dropped to limit noise.</summary>
        public int LookbackBars { get; set; } = 200;

        public string SourceId => "cipher_sr";

        public IReadOnlyList<PriceLevel> GetLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            if (state?.ActiveSeries == null) return System.Array.Empty<PriceLevel>();
            var series = state.ActiveSeries.FirstOrDefault(s =>
                string.Equals(s.IndicatorCode, IndicatorCode, System.StringComparison.OrdinalIgnoreCase));
            if (series == null) return System.Array.Empty<PriceLevel>();

            int upTo = history.Count;   // the bars that exist; the zone lines never run ahead of them
            var sink = new List<PriceLevel>();
            CollectLevels(series.GetComponentData(CompResistanceZone), LevelKind.Resistance, sink, "Cipher SR Resistance", upTo);
            CollectLevels(series.GetComponentData(CompSupportZone),    LevelKind.Support,    sink, "Cipher SR Support",    upTo);
            return sink;
        }

        /// <summary>One level per run of the zone line: emitted at the run's first bar.</summary>
        private void CollectLevels(double[]? data, LevelKind kind, List<PriceLevel> sink, string source, int upToExclusive)
        {
            if (data == null || data.Length == 0) return;
            int total = System.Math.Min(upToExclusive, data.Length);
            int from = System.Math.Max(0, total - LookbackBars);
            for (int i = from; i < total; i++)
            {
                double v = data[i];
                if (double.IsNaN(v)) continue;
                bool starts = i == from || double.IsNaN(data[i - 1]) || data[i - 1] != v;
                if (!starts) continue;
                double recency = (double)(i - from) / System.Math.Max(1, total - from - 1);
                double strength = 0.4 + 0.5 * recency;
                sink.Add(new PriceLevel(v, kind, Strength: strength, Source: source));
            }
        }
    }
}
