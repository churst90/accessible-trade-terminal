using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies.Levels
{
    /// <summary>
    /// Surfaces Volume Profile (VPVR / VPFR) and TPO Market Profile bins as <see cref="PriceLevel"/>s.
    /// Reads <c>ChartSeries.ProfileBins</c>, which is populated eagerly by
    /// <c>IndicatorOrchestrator</c> when the user adds VPVR/VPFR/TPO to the chart (the bins are
    /// not render-time only — they live on the series after Calculate() runs).
    ///
    /// Emits one PriceLevel per:
    ///   - POC bin → <see cref="LevelKind.Poc"/> (always exists for a non-empty profile)
    ///   - Highest value-area bin → <see cref="LevelKind.Vah"/>
    ///   - Lowest value-area bin → <see cref="LevelKind.Val"/>
    ///   - HVN bins (high-volume nodes inside the value area) → <see cref="LevelKind.Hvn"/>
    ///   - LVN bins (low-volume nodes / single prints) → <see cref="LevelKind.Lvn"/>
    ///
    /// HVN / LVN classification matches <c>ProfileBinClassifier</c>:
    ///   HVN ⇔ <c>IsValueArea AND TotalVolume &gt; mean × 1.3</c>
    ///   LVN ⇔ <c>IsSinglePrint OR TotalVolume &lt; mean × 0.4</c>
    ///
    /// **Backtest correctness caveat**: VPVR is computed against the workspace's *current*
    /// viewport, not the bar-i view. In backtest mode the bins reflect the final profile
    /// state at every historical bar — strategies that gate on POC / Value Area / HVN / LVN
    /// will future-leak. This is a known issue documented in docs/TODO.md Phase 11 and is the
    /// most important pending S/R correctness item.
    /// </summary>
    public class VolumeProfileLevelProvider : ILevelProvider
    {
        private static readonly string[] ProfileCodes = { "VPVR", "VPFR", "TPO" };
        private readonly IBacktestProfileCache? _backtestCache;

        public string SourceId => "profile";

        public VolumeProfileLevelProvider(IBacktestProfileCache? backtestCache = null)
        {
            _backtestCache = backtestCache;
        }

        public IReadOnlyList<PriceLevel> GetLevels(IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            if (state?.ActiveSeries == null) return System.Array.Empty<PriceLevel>();

            var sink = new List<PriceLevel>();
            foreach (var series in state.ActiveSeries)
            {
                if (string.IsNullOrEmpty(series.IndicatorCode)) continue;
                if (!ProfileCodes.Contains(series.IndicatorCode.ToUpperInvariant())) continue;

                // Backtest replay path: when StrategyBacktester is feeding bar-i profile snapshots
                // into the cache, prefer them over the live series.ProfileBins (which is the
                // workspace's *current* viewport profile and would future-leak in backtest mode).
                IReadOnlyList<ProfileBin>? bins = null;
                if (_backtestCache != null && _backtestCache.IsActive)
                {
                    bins = _backtestCache.Get(series.IndicatorCode);
                }
                bins ??= series.ProfileBins;
                if (bins == null || bins.Count == 0) continue;
                CollectFromProfile(bins, series.IndicatorCode, sink);
            }
            return sink;
        }

        private static void CollectFromProfile(IReadOnlyList<ProfileBin> bins, string code, List<PriceLevel> sink)
        {
            // Mean volume across all bins — used for HVN/LVN classification thresholds.
            double mean = bins.Average(b => b.TotalVolume);
            double hvnThreshold = mean * 1.3;
            double lvnThreshold = mean * 0.4;

            // Pre-scan to find VAH and VAL prices (highest and lowest value-area bin midpoints).
            double vah = double.NegativeInfinity, val = double.PositiveInfinity;
            foreach (var bin in bins)
            {
                if (!bin.IsValueArea) continue;
                if (bin.PriceMid > vah) vah = bin.PriceMid;
                if (bin.PriceMid < val) val = bin.PriceMid;
            }

            string source = code.ToUpperInvariant();

            foreach (var bin in bins)
            {
                if (bin.IsPOC)
                {
                    sink.Add(new PriceLevel(bin.PriceMid, LevelKind.Poc,
                        Strength: 0.9, Source: $"{source} POC"));
                }

                // Add VAH / VAL once each.
                if (bin.IsValueArea)
                {
                    if (bin.PriceMid == vah)
                        sink.Add(new PriceLevel(bin.PriceMid, LevelKind.Vah,
                            Strength: 0.75, Source: $"{source} VAH"));
                    if (bin.PriceMid == val)
                        sink.Add(new PriceLevel(bin.PriceMid, LevelKind.Val,
                            Strength: 0.75, Source: $"{source} VAL"));
                }

                // HVN: in-VA bin with markedly higher than average volume.
                if (bin.IsValueArea && bin.TotalVolume > hvnThreshold)
                {
                    sink.Add(new PriceLevel(bin.PriceMid, LevelKind.Hvn,
                        Strength: 0.65, Source: $"{source} HVN"));
                }

                // LVN: explicit single print, or any bin with markedly low volume.
                if (bin.IsSinglePrint || bin.TotalVolume < lvnThreshold)
                {
                    sink.Add(new PriceLevel(bin.PriceMid, LevelKind.Lvn,
                        Strength: 0.65, Source: $"{source} LVN"));
                }
            }
        }
    }
}
