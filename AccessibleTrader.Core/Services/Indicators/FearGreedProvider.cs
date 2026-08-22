using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Fear &amp; Greed Index — daily crypto sentiment composite (0–100).
    ///
    /// Cross-series indicator that reads through <see cref="ICrossSeriesCache"/>. Currently
    /// sourced from the AlternativeMe plugin (free, daily resolution, history back to 2018).
    /// When the user moves to Glassnode the source name in <see cref="FngRequest"/> changes
    /// and nothing else needs to — the indicator code path is provider-agnostic.
    ///
    /// Why FNG matters
    /// ───────────────
    /// Aggregates volatility, momentum, social media activity, dominance, and survey data into
    /// a single 0–100 number that updates once per day. The classic contrarian read:
    ///
    ///   FNG &lt; 20  → extreme fear → historically near local bottoms (buy the panic)
    ///   FNG &gt; 80  → extreme greed → historically near local tops (sell the euphoria)
    ///
    /// Useful as a strategy leaf because it's not price-derived — survey/social inputs make
    /// it slightly orthogonal to the auto-correlated price indicators that dominate the rest
    /// of the toolset. Daily-resolution, so the forward-fill produces wide steps on intraday
    /// charts (correct, not a bug).
    /// </summary>
    public class FearGreedProvider : IIndicatorProvider
    {
        public string Name => "Fear and Greed Index";

        public const string CompSentiment     = "Sentiment";
        public const string CompExtremeFear   = "Extreme Fear";
        public const string CompExtremeGreed  = "Extreme Greed";
        public const string CompSentimentFlip = "Sentiment Flip";

        // alternative.me serves the full daily history in one response — single-page fetch.
        private static readonly CrossSeriesRequest FngRequest = new(
            Market: "Sentiment",
            Provider: "AlternativeMe",
            Symbol: "FNG_INDEX",
            Timeframe: "1d",
            MaxPages: 1);

        private readonly ICrossSeriesCache _xs;

        public FearGreedProvider(ICrossSeriesCache xs)
        {
            _xs = xs;
        }

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "FEAR_GREED",
                Causality = ComponentCausality.Causal,
                Name        = "Fear and Greed Index",
                Category    = "Sentiment",
                DefaultPane = "Pane_FEAR_GREED",
                Description = "Crypto Fear and Greed Index (cross-series). Fetches the daily 0-100 " +
                              "sentiment composite and forward-fills onto the active chart. Below 20 " +
                              "= extreme fear (historically near bottoms). Above 80 = extreme greed " +
                              "(historically near tops). Daily resolution, so intraday charts will " +
                              "show step-shaped values.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompSentiment,
                            DisplayName = "Sentiment",
                            DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#9CCC65",
                            DefaultThickness = 2.0f,
                            DefaultWaveform = "sine",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 50.0,
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultNoiseAmount = 0.10f,
                            DefaultTriggerBoundaryClick = true,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultUsePolarityColoring = true,
                            SpeechTemplate = "Sentiment {value:F0}.",
                            IsVisible = true },

                    new() { Name = CompExtremeFear,
                            DisplayName = "Extreme Fear",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E5FF",
                            DefaultThickness = 6.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 350,
                            DefaultBaseFrequency = 280.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Extreme fear, contrarian buy signal",
                            IsVisible = true },

                    new() { Name = CompExtremeGreed,
                            DisplayName = "Extreme Greed",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF4444",
                            DefaultThickness = 6.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "detuned_pair_bell",
                            DefaultDecayMs = 350,
                            DefaultBaseFrequency = 460.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Extreme greed, contrarian sell signal",
                            IsVisible = true },

                    new() { Name = CompSentimentFlip,
                            DisplayName = "Sentiment Flip",
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
                            DefaultSignalSpeechTemplate = "Sentiment flip across midline",
                            IsVisible = true },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "FearLevel", DisplayName = "Extreme Fear Level",
                            DataType = typeof(double), DefaultValue = 20.0,
                            Description = "FNG value at or below which the Extreme Fear marker fires. " +
                                          "20 is the convention for 'extreme fear'." },
                    new() { Name = "GreedLevel", DisplayName = "Extreme Greed Level",
                            DataType = typeof(double), DefaultValue = 80.0,
                            Description = "FNG value at or above which the Extreme Greed marker fires. " +
                                          "80 is the convention for 'extreme greed'." }
                }
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals("FEAR_GREED", StringComparison.OrdinalIgnoreCase)) return new();
            return new()
            {
                new("Extreme Greed", 80.0, "#FF2222", DashStyle.Dot,
                    PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
                new("Greed",         60.0, "#FF8888", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.5f),
                new("Neutral",       50.0, "#666666", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.7f),
                new("Fear",          40.0, "#88FFFF", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.5f),
                new("Extreme Fear",  20.0, "#22FFFF", DashStyle.Dot,
                    PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
            };
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            double fearLevel  = GetDbl(parameters, "FearLevel", 20.0);
            double greedLevel = GetDbl(parameters, "GreedLevel", 80.0);

            int n = data.Length;
            var sentimentSpan = buffer.GetComponentSpan(CompSentiment);
            var fearSpan      = buffer.GetComponentSpan(CompExtremeFear);
            var greedSpan     = buffer.GetComponentSpan(CompExtremeGreed);
            var flipSpan      = buffer.GetComponentSpan(CompSentimentFlip);

            for (int i = 0; i < n && i < sentimentSpan.Length; i++)
            {
                sentimentSpan[i] = double.NaN;
                fearSpan[i]      = double.NaN;
                greedSpan[i]     = double.NaN;
                flipSpan[i]      = double.NaN;
            }

            if (n == 0) return;

            var ticks = _xs.GetOrFetch(FngRequest);
            if (ticks.Count == 0) return;

            CrossSeriesForwardFill.Fill(ticks, data, sentimentSpan);

            int previousSide = 0;
            for (int i = 0; i < n && i < sentimentSpan.Length; i++)
            {
                double v = sentimentSpan[i];
                if (double.IsNaN(v)) continue;

                if (v <= fearLevel)  fearSpan[i]  = v;
                if (v >= greedLevel) greedSpan[i] = v;

                int side = v > 50 ? 1 : v < 50 ? -1 : 0;
                if (previousSide != 0 && side != 0 && side != previousSide)
                    flipSpan[i] = v;
                if (side != 0) previousSide = side;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (componentName == CompSentiment)
            {
                if (double.IsNaN(value))
                    return "Sentiment, no data for this bar. Try a more recent range or wait for the data source to populate.";
                string label = value <= 20 ? "extreme fear"
                             : value <= 40 ? "fear"
                             : value <= 60 ? "neutral"
                             : value <= 80 ? "greed"
                             :                "extreme greed";
                return $"Fear and greed {value:F0}, {label}.";
            }
            if (componentName == CompExtremeFear && !double.IsNaN(value))
                return $"Extreme fear at {value:F0}, contrarian buy zone.";
            if (componentName == CompExtremeGreed && !double.IsNaN(value))
                return $"Extreme greed at {value:F0}, contrarian sell zone.";
            if (componentName == CompSentimentFlip && !double.IsNaN(value))
            {
                bool toGreed = value > 50;
                return toGreed
                    ? "Sentiment flipped to greed."
                    : "Sentiment flipped to fear.";
            }
            return null;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (!calculatedResults.TryGetValue(CompSentiment, out var arr) || arr == null
                || index < 0 || index >= arr.Length || double.IsNaN(arr[index]))
                return "Fear and greed: no data";
            double v = arr[index];
            string label = v <= 20 ? "extreme fear"
                         : v <= 40 ? "fear"
                         : v <= 60 ? "neutral"
                         : v <= 80 ? "greed"
                         :            "extreme greed";
            return $"Fear and greed index {v:F0} ({label}).";
        }

        private static double GetDbl(Dictionary<string, object> p, string k, double def) =>
            p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
    }
}
