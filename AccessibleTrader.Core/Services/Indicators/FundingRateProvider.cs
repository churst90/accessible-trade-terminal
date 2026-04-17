using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Funding Rate — perpetual swap funding cost as a leading sentiment / positioning signal.
    ///
    /// First cross-series indicator in the codebase, refactored to read through the shared
    /// <see cref="ICrossSeriesCache"/> service. The cache handles fetching, walk-back
    /// pagination, deduping, and the synchronous-wait-on-first-add pattern that gives us
    /// correct first-paint without any chart-recalc plumbing. This provider is now thin:
    /// metadata + Calculate + speech overrides.
    ///
    /// Why funding rate matters
    /// ────────────────────────
    /// Perpetual swap contracts settle a "funding payment" every 8 hours between long and short
    /// holders. The sign and magnitude tell you how positioned the derivatives crowd is:
    ///
    ///   Funding > 0  → longs pay shorts (longs are crowded, paying for the privilege)
    ///   Funding < 0  → shorts pay longs (shorts are crowded — vulnerable to a squeeze)
    ///
    /// Extreme positive funding tends to mark local tops; extreme negative funding tends to
    /// mark local bottoms. Not a precise timing tool, but as one input among several it adds
    /// an *orthogonal* dimension that pure-price indicators can't capture.
    /// </summary>
    public class FundingRateProvider : IIndicatorProvider
    {
        public string Name => "Funding Rate";

        public const string CompFundingRate  = "Funding Rate";
        public const string CompExtremeLong  = "Extreme Long";
        public const string CompExtremeShort = "Extreme Short";
        public const string CompSignFlip     = "Sign Flip";

        // Default to BinanceVision for deep multi-year funding history. Values land in
        // cross-series cache as percent per 8h (the BinanceVision plugin multiplies the
        // raw fraction ×100 at fetch time). StrategyLab's snapshotting cache serves the
        // same key from pre-fetched xs_binancevision_*.json files when running offline.
        // OKX fallback retained as a second request for resilience when BinanceVision
        // is unreachable (rare — it's a static S3 bucket with no rate limits).
        private static readonly CrossSeriesRequest FundingRequest = new(
            Market: "Derivatives",
            Provider: "BinanceVision",
            Symbol: "BTCUSDT_FUNDING",
            Timeframe: "8h",
            MaxPages: 10);

        private readonly ICrossSeriesCache _xs;

        public FundingRateProvider(ICrossSeriesCache xs)
        {
            _xs = xs;
        }

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "FUNDING_RATE",
                Name        = "Funding Rate",
                Category    = "Derivatives",
                DefaultPane = "Pane_FUNDING",
                Description = "Perpetual swap funding rate (cross-series). Fetches funding history " +
                              "from OkxDerivatives and forward-fills onto the active chart by timestamp. " +
                              "Positive = longs paying shorts (long-crowded). Negative = shorts paying " +
                              "longs (short-crowded). Units are percent per 8h. Extreme readings " +
                              "(±0.05%/8h by default) often coincide with local trend exhaustion.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompFundingRate,
                            DisplayName = "Funding Rate",
                            DisplayType = ComponentDisplayType.Oscillator,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFD740",
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
                            SpeechTemplate = "Funding {value:F4} percent.",
                            IsVisible = true },

                    new() { Name = CompExtremeLong,
                            DisplayName = "Extreme Long",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF4444",
                            DefaultThickness = 6.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "detuned_pair_bell",
                            DefaultDecayMs = 320,
                            DefaultBaseFrequency = 440.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Funding extreme long pressure",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    new() { Name = CompExtremeShort,
                            DisplayName = "Extreme Short",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E5FF",
                            DefaultThickness = 6.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 320,
                            DefaultBaseFrequency = 300.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Funding extreme short pressure",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    new() { Name = CompSignFlip,
                            DisplayName = "Sign Flip",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#CCCCCC",
                            DefaultThickness = 4.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 200,
                            DefaultBaseFrequency = 380.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Funding sign flip",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "ExtremeLevel", DisplayName = "Extreme Level (%/8h)",
                            DataType = typeof(double), DefaultValue = 0.05,
                            Description = "Absolute funding rate above which the Extreme Long / Extreme " +
                                          "Short markers fire. 0.05 (= 0.05%/8h) corresponds to roughly " +
                                          "the 90th-percentile reading in normal BTC regimes." }
                }
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals("FUNDING_RATE", StringComparison.OrdinalIgnoreCase)) return new();
            return new()
            {
                new("Extreme Long",  0.05, "#FF2222", DashStyle.Dot,
                    PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
                new("Mild Long",     0.01, "#FF8888", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.5f),
                new("Zero",          0.00, "#666666", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.7f),
                new("Mild Short",   -0.01, "#88FFFF", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.5f),
                new("Extreme Short",-0.05, "#22FFFF", DashStyle.Dot,
                    PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
            };
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            double extreme = GetDbl(parameters, "ExtremeLevel", 0.05);

            int n = data.Length;
            var rateSpan  = buffer.GetComponentSpan(CompFundingRate);
            var longSpan  = buffer.GetComponentSpan(CompExtremeLong);
            var shortSpan = buffer.GetComponentSpan(CompExtremeShort);
            var flipSpan  = buffer.GetComponentSpan(CompSignFlip);

            for (int i = 0; i < n && i < rateSpan.Length; i++)
            {
                rateSpan[i]  = double.NaN;
                longSpan[i]  = double.NaN;
                shortSpan[i] = double.NaN;
                flipSpan[i]  = double.NaN;
            }

            if (n == 0) return;

            // Pull from the shared cache. First-add synchronously waits up to 5s; subsequent
            // calls hit the in-memory cache and return immediately.
            var ticks = _xs.GetOrFetch(FundingRequest);
            if (ticks.Count == 0) return;

            CrossSeriesForwardFill.Fill(ticks, data, rateSpan);

            // Post-process the populated funding values into marker components. Sign Flip
            // detection tracks the previous non-zero sign across bars; Extreme Long/Short are
            // simple threshold matches.
            int previousSign = 0;
            for (int i = 0; i < n && i < rateSpan.Length; i++)
            {
                double v = rateSpan[i];
                if (double.IsNaN(v)) continue;

                if (v >=  extreme) longSpan[i]  = v;
                if (v <= -extreme) shortSpan[i] = v;

                int sign = v > 0 ? 1 : v < 0 ? -1 : 0;
                if (previousSign != 0 && sign != 0 && sign != previousSign)
                    flipSpan[i] = v;
                if (sign != 0) previousSign = sign;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (componentName == CompFundingRate)
            {
                if (double.IsNaN(value))
                    return "Funding rate, no data for this bar. Try a chart range within the last 30 days.";
                string side = value > 0 ? "longs paying shorts" : value < 0 ? "shorts paying longs" : "neutral";
                return $"Funding {value:F4} percent per 8 hours, {side}.";
            }
            if (componentName == CompExtremeLong && !double.IsNaN(value))
                return $"Extreme long pressure, funding {value:F4} percent.";
            if (componentName == CompExtremeShort && !double.IsNaN(value))
                return $"Extreme short pressure, funding {value:F4} percent.";
            if (componentName == CompSignFlip && !double.IsNaN(value))
                return $"Funding sign flip at {value:F4} percent.";
            return null;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (!calculatedResults.TryGetValue(CompFundingRate, out var arr) || arr == null
                || index < 0 || index >= arr.Length) return "Funding rate: no data";
            double v = arr[index];
            if (double.IsNaN(v)) return "Funding rate: no data for this bar";

            double approxApr = v * 3.0 * 365.0;
            string side = v > 0 ? "longs paying shorts" : v < 0 ? "shorts paying longs" : "neutral";
            return $"Funding rate {v:F4}% per 8h ({side}). Annualised approx {approxApr:F1}% APR.";
        }

        private static double GetDbl(Dictionary<string, object> p, string k, double def) =>
            p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
    }
}
