using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Crowding Index — first composite cross-series indicator. Combines funding rate and
    /// open interest delta into a single z-scored "how crowded is this move?" score.
    ///
    /// The strategy thesis (project_strategy_thesis_2026_04_08.md) found that 8 versions of
    /// pure-Cipher confluence walk-forward to break-even because price-derived indicators
    /// are auto-correlated. This indicator is the FIRST signal in the codebase that combines
    /// two genuinely orthogonal non-price sources (funding cost + open positioning) into one
    /// scalar — pure-price strategies cannot replicate it from any combination of Cipher/RSI/
    /// MACD/etc, no matter how cleverly weighted. v9 leafs on it for exactly that reason.
    ///
    /// Math
    /// ────
    /// Over a rolling 30-bar window:
    ///
    ///   funding_z   = (funding[i] − mean(funding)) / stdev(funding)
    ///   oi_delta    = oi[i] − oi[i−1]
    ///   oi_delta_z  = (oi_delta[i] − mean(oi_delta)) / stdev(oi_delta)
    ///   price_dir   = sign(close[i] − close[i−1])
    ///   crowding[i] = funding_z + price_dir × oi_delta_z
    ///
    /// The price_dir multiplier is the key trick: a positive funding_z means longs are paying
    /// (the long side is crowded). A positive oi_delta_z means OI is growing. If price is also
    /// rising (price_dir = +1), then the growing OI is *new longs piling in on the rally* and
    /// the funding+OI signals reinforce each other → high positive crowding. But if price is
    /// falling (price_dir = −1), the growing OI is *new shorts entering the dump* and we
    /// flip the OI z-score sign so it now contributes to the *short* crowding instead, giving
    /// a strongly negative composite. This makes one number that reads positively for "longs
    /// piled in" and negatively for "shorts piled in" regardless of which side is moving the
    /// market.
    ///
    /// Trading interpretation:
    ///
    ///   crowding ≥ +2  → long-side squeeze risk. Funding paying longs through the nose AND
    ///                    new longs piling in. Reversal-down probability elevated.
    ///   crowding ≤ −2  → short-side squeeze risk. Funding paying shorts AND new shorts
    ///                    piling in. Reversal-up probability elevated.
    ///   |crowding| &lt; 1 → quiet positioning, no edge from this indicator.
    ///
    /// Why this is genuinely orthogonal to Cipher
    /// ──────────────────────────────────────────
    /// All Cipher components, RSI, MACD, etc. are arithmetic transformations of the same OHLCV
    /// data. They differ in lookback and weighting but they're auto-correlated by construction.
    /// The Crowding Index reads two completely separate exchange-internal datasets (funding
    /// payments, contract counts) that aren't computable from price history at any lookback.
    /// The cross-source agreement that triggers this indicator is information v7-v8 cannot see.
    /// </summary>
    public class CrowdingIndexProvider : IIndicatorProvider
    {
        public string Name => "Crowding Index";

        public const string CompCrowdingScore = "Crowding Score";
        public const string CompLongCrowded   = "Long Crowded";
        public const string CompShortCrowded  = "Short Crowded";

        // Same source requests as the underlying indicators — sharing them via the cache
        // means CrowdingIndex never makes a redundant fetch even if it loads before the
        // FundingRate/OpenInterest indicators, AND benefits from the funding walk-back
        // pagination set up there.
        private static readonly CrossSeriesRequest FundingRequest = new(
            Market: "Derivatives",
            Provider: "OkxDerivatives",
            Symbol: "BTC-USDT-SWAP_FUNDING",
            Timeframe: "1h",
            MaxPages: 10);

        private static readonly CrossSeriesRequest OiRequest = new(
            Market: "Derivatives",
            Provider: "OkxDerivatives",
            Symbol: "BTC-USDT-SWAP_OI",
            Timeframe: "1h",
            MaxPages: 1);

        private readonly ICrossSeriesCache _xs;

        public CrowdingIndexProvider(ICrossSeriesCache xs)
        {
            _xs = xs;
        }

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "CROWDING_INDEX",
                Name        = "Crowding Index",
                Category    = "Derivatives",
                DefaultPane = "Pane_CROWDING",
                Description = "Composite long/short positioning crowdedness. Combines funding-rate " +
                              "z-score with open-interest-delta z-score (signed by price direction) " +
                              "over a 30-bar rolling window. Positive values = longs crowded (squeeze " +
                              "risk down). Negative values = shorts crowded (squeeze risk up). |score| ≥ 2 " +
                              "fires a marker. The first composite cross-series indicator: combines " +
                              "two non-price data sources that pure-price indicators cannot replicate.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompCrowdingScore,
                            DisplayName = "Crowding Score",
                            DisplayType = ComponentDisplayType.Oscillator,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#E1BEE7",
                            DefaultThickness = 2.0f,
                            DefaultWaveform = "sine",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0,
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultNoiseAmount = 0.10f,
                            DefaultTriggerBoundaryClick = true,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultIsAreaFill = false,
                            DefaultUsePolarityColoring = true,
                            SpeechTemplate = "Crowding {value:F2}.",
                            IsVisible = true },

                    // Long Crowded: funding+OI agree that longs are piled in. Visually red
                    // because long-crowded is a contrarian *bearish* warning, matching the
                    // FundingRate Extreme Long color family.
                    new() { Name = CompLongCrowded,
                            DisplayName = "Long Crowded",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF4444",
                            DefaultThickness = 7.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "dual_tone_bell",
                            DefaultDecayMs = 450,
                            DefaultBaseFrequency = 480.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Long crowded, squeeze risk down",
                            IsVisible = true },

                    new() { Name = CompShortCrowded,
                            DisplayName = "Short Crowded",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E5FF",
                            DefaultThickness = 7.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "dual_tone_bell",
                            DefaultDecayMs = 450,
                            DefaultBaseFrequency = 280.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Short crowded, squeeze risk up",
                            IsVisible = true },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "ZWindow", DisplayName = "Z-Score Window (bars)",
                            DataType = typeof(int), DefaultValue = 30.0,
                            Description = "Rolling window length for the z-score normalization. 30 bars " +
                                          "is roughly one trading day on a 1h chart." },
                    new() { Name = "Threshold", DisplayName = "Crowding Threshold (sigma)",
                            DataType = typeof(double), DefaultValue = 2.0,
                            Description = "Absolute composite score above which the Long/Short Crowded markers fire. " +
                                          "2.0σ corresponds to ~95th-percentile readings under a normal distribution; " +
                                          "empirically, BTC funding-rate z-scores exceeded 2.0 about 4-6% of the time on " +
                                          "1h bars across 2023-2025 (validated in walk-forward v9). Tightening to 2.5 " +
                                          "captures roughly the top 1% of extremes at the cost of fewer signals." },
                    new() { Name = "MaxStaleBars", DisplayName = "Max Stale Bars",
                            DataType = typeof(int), DefaultValue = 6.0,
                            Description = "Safety guard: emit NaN if the funding or OI source has had the same value for " +
                                          "more than this many consecutive bars (a proxy for feed staleness). Default 6 " +
                                          "allows 6 hours of flat funding at the native cadence before suppressing signals." }
                }
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals("CROWDING_INDEX", StringComparison.OrdinalIgnoreCase)) return new();
            return new()
            {
                new("Long Crowded",   2.0, "#FF2222", DashStyle.Dot,
                    PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
                new("Long Pressure",  1.0, "#FF8888", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.5f),
                new("Neutral",        0.0, "#666666", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.7f),
                new("Short Pressure",-1.0, "#88FFFF", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.5f),
                new("Short Crowded", -2.0, "#22FFFF", DashStyle.Dot,
                    PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
            };
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int zWindow = Math.Max(5, GetInt(parameters, "ZWindow", 30));
            double threshold = GetDbl(parameters, "Threshold", 2.0);
            int maxStale = Math.Max(1, GetInt(parameters, "MaxStaleBars", 6));

            int n = data.Length;
            var scoreSpan = buffer.GetComponentSpan(CompCrowdingScore);
            var longSpan  = buffer.GetComponentSpan(CompLongCrowded);
            var shortSpan = buffer.GetComponentSpan(CompShortCrowded);

            for (int i = 0; i < n && i < scoreSpan.Length; i++)
            {
                scoreSpan[i] = double.NaN;
                longSpan[i]  = double.NaN;
                shortSpan[i] = double.NaN;
            }

            if (n < zWindow + 2) return;

            // Pull both source caches. If either is empty, leave everything NaN — Crowding
            // is meaningless without both inputs.
            var fundingTicks = _xs.GetOrFetch(FundingRequest);
            var oiTicks      = _xs.GetOrFetch(OiRequest);
            if (fundingTicks.Count == 0 || oiTicks.Count == 0) return;

            // Forward-fill both sources onto the chart bar timeline using local arrays so
            // we don't pollute the buffer with intermediate values.
            var funding = new double[n];
            var oi = new double[n];
            for (int i = 0; i < n; i++) { funding[i] = double.NaN; oi[i] = double.NaN; }
            CrossSeriesForwardFill.Fill(fundingTicks, data, funding);
            CrossSeriesForwardFill.Fill(oiTicks, data, oi);

            // Pre-compute OI delta from the forward-filled OI series.
            var oiDelta = new double[n];
            for (int i = 0; i < n; i++) oiDelta[i] = double.NaN;
            for (int i = 1; i < n; i++)
            {
                if (!double.IsNaN(oi[i]) && !double.IsNaN(oi[i - 1]))
                    oiDelta[i] = oi[i] - oi[i - 1];
            }

            // Staleness guard: after forward fill, a stopped feed still produces a constant
            // value on every bar. Count consecutive bars of identical funding or OI and NaN
            // them out past the MaxStaleBars threshold so crowding signals don't fire on
            // dead data. Compare with epsilon to tolerate floating-point representation noise.
            const double eps = 1e-12;
            int fundingFlat = 0, oiFlat = 0;
            for (int i = 1; i < n; i++)
            {
                if (!double.IsNaN(funding[i]) && !double.IsNaN(funding[i - 1]) &&
                    Math.Abs(funding[i] - funding[i - 1]) < eps)
                    fundingFlat++;
                else
                    fundingFlat = 0;

                if (!double.IsNaN(oi[i]) && !double.IsNaN(oi[i - 1]) &&
                    Math.Abs(oi[i] - oi[i - 1]) < eps)
                    oiFlat++;
                else
                    oiFlat = 0;

                if (fundingFlat > maxStale) funding[i] = double.NaN;
                if (oiFlat      > maxStale) { oi[i] = double.NaN; oiDelta[i] = double.NaN; }
            }

            // Rolling z-score + composite, starting from the first bar with a full window.
            for (int i = zWindow; i < n && i < scoreSpan.Length; i++)
            {
                if (double.IsNaN(funding[i]) || double.IsNaN(oiDelta[i])) continue;

                // Funding window stats
                double fSum = 0, fSumSq = 0; int fCount = 0;
                for (int j = i - zWindow + 1; j <= i; j++)
                {
                    double f = funding[j];
                    if (double.IsNaN(f)) continue;
                    fSum += f; fSumSq += f * f; fCount++;
                }

                // OI delta window stats
                double oSum = 0, oSumSq = 0; int oCount = 0;
                for (int j = i - zWindow + 1; j <= i; j++)
                {
                    double o = oiDelta[j];
                    if (double.IsNaN(o)) continue;
                    oSum += o; oSumSq += o * o; oCount++;
                }

                // Need a minimum sample size in BOTH windows for a meaningful z-score.
                if (fCount < zWindow / 2 || oCount < zWindow / 2) continue;

                double fMean = fSum / fCount;
                double fVar = (fSumSq / fCount) - (fMean * fMean);
                double oMean = oSum / oCount;
                double oVar = (oSumSq / oCount) - (oMean * oMean);
                if (fVar <= 0 || oVar <= 0) continue;

                double fStd = Math.Sqrt(fVar);
                double oStd = Math.Sqrt(oVar);

                double fundingZ = (funding[i] - fMean) / fStd;
                double oiDeltaZ = (oiDelta[i] - oMean) / oStd;

                // Price direction comes from the chart bars themselves — same definition the
                // class comment uses (sign of single-bar close-to-close change).
                double pNow  = data[i].Close;
                double pPrev = data[i - 1].Close;
                int priceDir = pNow > pPrev ? 1 : pNow < pPrev ? -1 : 0;

                double crowding = fundingZ + priceDir * oiDeltaZ;
                scoreSpan[i] = crowding;

                if (crowding >=  threshold) longSpan[i]  = crowding;
                if (crowding <= -threshold) shortSpan[i] = crowding;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) =>
            GetInt(parameters, "ZWindow", 30) + 2;

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (componentName == CompCrowdingScore)
            {
                if (double.IsNaN(value))
                    return "Crowding score, no data for this bar. Both funding and open interest series are required.";
                string interp = value >=  2 ? "longs heavily crowded, squeeze risk down"
                              : value >=  1 ? "long pressure building"
                              : value <= -2 ? "shorts heavily crowded, squeeze risk up"
                              : value <= -1 ? "short pressure building"
                              :                "neutral positioning";
                return $"Crowding {value:F2} sigma, {interp}.";
            }
            if (componentName == CompLongCrowded && !double.IsNaN(value))
                return $"Long crowded at {value:F2} sigma. Squeeze risk down — both funding and new positioning piled into longs.";
            if (componentName == CompShortCrowded && !double.IsNaN(value))
                return $"Short crowded at {value:F2} sigma. Squeeze risk up — both funding and new positioning piled into shorts.";
            return null;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (!calculatedResults.TryGetValue(CompCrowdingScore, out var arr) || arr == null
                || index < 0 || index >= arr.Length || double.IsNaN(arr[index]))
                return "Crowding index: no data";
            double v = arr[index];
            string interp = v >=  2 ? "long-crowded extreme"
                          : v >=  1 ? "long pressure"
                          : v <= -2 ? "short-crowded extreme"
                          : v <= -1 ? "short pressure"
                          :            "neutral";
            return $"Crowding index {v:F2} sigma ({interp}). Composite of funding-rate z-score and price-signed OI-delta z-score over a 30-bar window.";
        }

        private static double GetDbl(Dictionary<string, object> p, string k, double def) =>
            p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
        private static int GetInt(Dictionary<string, object> p, string k, int def) =>
            p.TryGetValue(k, out var v) ? (int)Convert.ToDouble(v) : def;
    }
}
