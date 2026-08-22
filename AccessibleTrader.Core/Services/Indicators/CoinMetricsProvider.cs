using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// CoinMetrics community-tier on-chain indicator. Reads cached daily metrics from
    /// <see cref="ICrossSeriesCache"/> (populated by the StrategyLab `coinmetrics`
    /// downloader) and exposes them as indicator components for strategy leaves and
    /// chart speech.
    ///
    /// Why this exists: 13 strategy iterations on pure-OHLCV confluence walked-forward
    /// to break-even. The strategy thesis (project_strategy_thesis_2026_04_08) says
    /// edge requires data sources that are not auto-correlated with price. CoinMetrics
    /// MVRV is the single most-cited on-chain cycle measurement in the field — it
    /// represents what fraction of all coins are currently in profit at the average
    /// cost basis. The standard heuristics:
    ///
    ///   MVRV &lt; 1.0   — average holder underwater (capitulation phase, accumulation buys)
    ///   1.0 - 2.0     — fair value to early markup
    ///   2.0 - 3.0     — markup, holders accumulating profits
    ///   3.0 - 3.5     — distribution, late cycle warning
    ///   3.5 - 4.0     — typical cycle top zone
    ///   &gt; 4.0       — euphoric blowoff (rare, &lt;1% of bars historically)
    ///
    /// This is exactly the kind of structural/regime measurement Pulse v1-v12 lacked.
    /// MVRV moves on a months-to-years cadence — the opposite of price's hours-to-days
    /// cadence — so it's almost-orthogonal to anything pulled from OHLCV.
    ///
    /// Components (all forward-filled onto chart bars):
    ///   • <see cref="CompMvrv"/>          — MVRV ratio (continuous)
    ///   • <see cref="CompMvrvRegime"/>    — 4-band classifier {1,2,3,4} for strategy gating
    ///   • <see cref="CompMvrvZ"/>         — z-score of MVRV over rolling 365d window (relative position vs own history)
    ///   • <see cref="CompAdrActCnt"/>     — daily active addresses
    ///   • <see cref="CompAdrActCntZ"/>    — z-score of active addresses (network usage trend)
    ///   • <see cref="CompHashRate"/>      — hash rate (BTC only, NaN for ETH post-merge)
    ///
    /// Asset detection: same median-close hack as the other cross-series providers.
    /// Symbols supported: BTC and ETH. The downloader fetched both as of shipping.
    /// </summary>
    public sealed class CoinMetricsProvider : IIndicatorProvider
    {
        public const string Code = "COINMETRICS";
        public const string CompMvrv         = "MVRV";
        public const string CompMvrvRegime   = "MVRVRegime";
        public const string CompMvrvZ        = "MVRVZ";
        public const string CompAdrActCnt    = "ActiveAddresses";
        public const string CompAdrActCntZ   = "ActiveAddressesZ";
        public const string CompHashRate     = "HashRate";

        private const int ZWindow = 365;  // 1 year of daily bars
        private readonly ICrossSeriesCache _xs;

        public CoinMetricsProvider(ICrossSeriesCache xs) { _xs = xs; }

        public string Name => "CoinMetrics On-Chain";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Causality = ComponentCausality.Causal,
                Name = "CoinMetrics On-Chain",
                Category = "On-Chain",
                DefaultPane = "Pane_COINMETRICS",
                Description = "MVRV ratio + active addresses + hash rate from CoinMetrics community tier. Free, daily, ~11yr history. Use as a regime classifier — MVRV<1 = capitulation, MVRV>3.5 = top zone.",
                Parameters = new List<IndicatorParameterMetadata>(),
                Components = new List<IndicatorComponentMetadata>
                {
                    new()
                    {
                        Name = CompMvrv, DisplayName = "MVRV Ratio",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        DefaultColorHex = "#FFD700",
                        DefaultThickness = 2.0f,
                        SpeechTemplate = "M V R V {value:F2}.",
                    },
                    new()
                    {
                        Name = CompMvrvRegime, DisplayName = "MVRV Regime (1-4)",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "M V R V regime {value:F0}.",
                    },
                    new()
                    {
                        Name = CompMvrvZ, DisplayName = "MVRV Z-Score (365)",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SpeechTemplate = "M V R V Z {value:F2}.",
                    },
                    new()
                    {
                        Name = CompAdrActCnt, DisplayName = "Active Addresses",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        DefaultColorHex = "#42A5F5",
                        DefaultThickness = 1.5f,
                        SpeechTemplate = "Active addresses {value:F0}.",
                    },
                    new()
                    {
                        Name = CompAdrActCntZ, DisplayName = "Active Addresses Z (365)",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SpeechTemplate = "Active addresses Z {value:F2}.",
                    },
                    new()
                    {
                        Name = CompHashRate, DisplayName = "Hash Rate",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SpeechTemplate = "Hash rate {value:F0}.",
                    },
                }
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            var mvrv    = buffer.GetComponentSpan(CompMvrv);
            var mvrvReg = buffer.GetComponentSpan(CompMvrvRegime);
            var mvrvZ   = buffer.GetComponentSpan(CompMvrvZ);
            var aac     = buffer.GetComponentSpan(CompAdrActCnt);
            var aacZ    = buffer.GetComponentSpan(CompAdrActCntZ);
            var hash    = buffer.GetComponentSpan(CompHashRate);

            for (int i = 0; i < n; i++)
            {
                mvrv[i] = double.NaN; mvrvReg[i] = double.NaN; mvrvZ[i] = double.NaN;
                aac[i]  = double.NaN; aacZ[i]   = double.NaN; hash[i]  = double.NaN;
            }
            if (n == 0) return;

            // Asset resolution: __symbol hint authoritative when present, median fallback
            // only for callers without hint plumbing (live MAUI app path until that lands).
            string? asset;
            if (parameters != null && parameters.ContainsKey("__symbol"))
            {
                asset = ResolveAssetFromParameters(parameters);
                if (asset == null) return;  // hint present but unsupported — clean all-NaN
            }
            else
            {
                var sortedCloses = new double[n];
                for (int i = 0; i < n; i++) sortedCloses[i] = (double)data[i].Close;
                Array.Sort(sortedCloses);
                double medianClose = sortedCloses[n / 2];
                asset = DetectAsset(medianClose);
                if (asset == null)
                {
                    System.Diagnostics.Trace.TraceWarning($"[COINMETRICS] Could not detect asset from median close {medianClose:0.00} — leaving all-NaN");
                    return;
                }
            }

            ForwardFillMetric(asset, "CapMVRVCur", data, mvrv);
            ForwardFillMetric(asset, "AdrActCnt",  data, aac);
            ForwardFillMetric(asset, "HashRate",   data, hash);

            // Regime classification per the standard MVRV cycle bands.
            for (int i = 0; i < n; i++)
            {
                double v = mvrv[i];
                if (double.IsNaN(v)) continue;
                int reg = v switch
                {
                    < 1.0  => 1, // capitulation
                    < 2.0  => 2, // fair / early markup
                    < 3.0  => 3, // markup / distribution warning
                    _      => 4, // late distribution / euphoric top
                };
                mvrvReg[i] = reg;
            }

            RollingZ(mvrv, mvrvZ, ZWindow);
            RollingZ(aac,  aacZ,  ZWindow);
        }

        /// <summary>Reads a CoinMetrics metric stream from the cache and forward-fills onto the bar series.</summary>
        private void ForwardFillMetric(string asset, string metric, ReadOnlySpan<Ohlcv> data, Span<double> dst)
        {
            int n = data.Length;
            var request = new CrossSeriesRequest(
                Market: "OnChain",
                Provider: "CoinMetrics",
                Symbol: $"{asset}_{metric}",
                Timeframe: "1d",
                MaxPages: 1);

            var ticks = _xs.GetOrFetch(request);
            if (ticks == null || ticks.Count == 0) return;

            // Forward-fill: walk bar series and metric series in lockstep, latest-known-value semantics.
            int tickIdx = 0;
            for (int i = 0; i < n; i++)
            {
                long barTs = new DateTimeOffset(data[i].Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
                while (tickIdx + 1 < ticks.Count && ticks[tickIdx + 1].Ts <= barTs) tickIdx++;
                if (ticks[tickIdx].Ts <= barTs) dst[i] = ticks[tickIdx].Value;
            }
        }

        /// <summary>O(n) rolling z-score using running sum + sum-of-squares over the most recent
        /// non-NaN values up to each bar. Standardizes the metric against its own history rather
        /// than against a constant — useful for cross-asset comparison and for catching deviations
        /// from the asset's normal range.</summary>
        private static void RollingZ(Span<double> src, Span<double> dst, int window)
        {
            int n = src.Length;
            var w = new Queue<double>();
            double sum = 0, sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                double v = src[i];
                if (!double.IsNaN(v))
                {
                    w.Enqueue(v);
                    sum += v;
                    sumSq += v * v;
                    if (w.Count > window)
                    {
                        var dropped = w.Dequeue();
                        sum -= dropped;
                        sumSq -= dropped * dropped;
                    }
                }
                if (w.Count >= 30)  // need a meaningful sample
                {
                    double mean = sum / w.Count;
                    double var2 = (sumSq / w.Count) - mean * mean;
                    if (var2 > 0)
                    {
                        double sd = Math.Sqrt(var2);
                        dst[i] = (v - mean) / sd;
                    }
                }
            }
        }

        /// <summary>Reads the "__symbol" hint from the parameters dict (set by WorkspaceFactory
        /// from the snapshot's Symbol field, e.g. "LTC/USDT") and extracts the lowercase base
        /// asset code. Returns null if the parameter is missing or malformed, in which case
        /// the caller falls back to median-close detection. The supported asset whitelist mirrors
        /// the DetectAsset switch — anything outside it returns null even if the hint is present.</summary>
        private static string? ResolveAssetFromParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null) return null;
            if (!parameters.TryGetValue("__symbol", out var symObj)) return null;
            var sym = symObj?.ToString();
            if (string.IsNullOrEmpty(sym)) return null;
            // "LTC/USDT" → "ltc". Also handles "LTCUSDT" by stripping a trailing USDT/USD.
            var slash = sym.IndexOf('/');
            string baseAsset = slash >= 0 ? sym[..slash] : sym;
            baseAsset = baseAsset.ToLowerInvariant();
            if (baseAsset.EndsWith("usdt")) baseAsset = baseAsset[..^4];
            else if (baseAsset.EndsWith("usd")) baseAsset = baseAsset[..^3];
            return baseAsset switch
            {
                "btc" or "eth" or "ltc" or "xrp" => baseAsset,
                _ => null,
            };
        }

        // Asset detection by median close — fallback path used when __symbol hint is absent.
        // Median ranges chosen to be unambiguous across the 8-12yr snapshots we backtest:
        //   BTC: $200 (2015) → $130k (2025), median 2015-2026 ≈ $15k
        //   ETH: $0.50 (2015) → $5k (2025),  median 2015-2026 ≈ $500
        //   LTC: $20 (2017)  → $400 (2021),  median 2018-2026 ≈ $90
        //   XRP: $0.20 (2017) → $3 (2025),    median 2016-2026 ≈ $0.50
        // The bands have a few hundred dollars of headroom on each side; if we add more
        // assets we'll need to plumb a parameter through WorkspaceFactory instead.
        private static string? DetectAsset(double medianClose) => medianClose switch
        {
            > 5000           => "btc",
            > 200 and <= 5000 => "eth",
            > 20 and <= 200  => "ltc",
            > 0 and <= 20    => "xrp",
            _ => null,
        };

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => ZWindow;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return string.Empty;
            if (!calculatedResults.TryGetValue(CompMvrv, out var mvrvArr)) return string.Empty;
            if (index >= mvrvArr.Length) return string.Empty;
            double v = mvrvArr[index];
            if (double.IsNaN(v)) return "MVRV warming up.";
            string regime = v switch
            {
                < 1.0 => "capitulation",
                < 2.0 => "fair value",
                < 3.0 => "markup",
                < 3.5 => "distribution warning",
                _     => "euphoric top zone",
            };
            return $"MVRV {v:F2}, {regime}.";
        }

        public string GetSpeechFact(string code, string componentName, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters) => "";
    }
}
