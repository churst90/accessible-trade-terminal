using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    public sealed class SkenderZeroCrossProvider : IIndicatorProvider
    {
        private static readonly List<IndicatorMetadata> _indicators = BuildIndicators();
        private readonly SkenderDetailFactProvider _detailFacts = new();

        public string Name => "Skender Zero-Cross Oscillators";

        public List<IndicatorMetadata> GetIndicators() => _indicators;

        public void Calculate(string code, System.ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => SkenderCalculationCore.Calculate(code, data, parameters, buffer);

        public void UpdateLast(string code, System.ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => SkenderCalculationCore.UpdateLast(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
            => SkenderCalculationCore.GetStabilityWindow(code, parameters);

        public string GetDetailFact(string code, System.ReadOnlySpan<Ohlcv> data,
            System.Collections.Generic.IReadOnlyDictionary<string, double[]> results,
            int index, Dictionary<string, object> parameters)
            => _detailFacts.GetDetailFact(code, data, results, index, parameters) ?? string.Empty;

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            System.Collections.Generic.IReadOnlyDictionary<string, double[]> compData, int index)
            => null;

        private static readonly List<LevelDescriptor> _zeroLevel = new()
        {
            new("Zero", 0.0, "#888888", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
        };

        public List<LevelDescriptor> GetDefaultLevels(string code) => code.ToUpperInvariant() switch
        {
            "CMO" => new List<LevelDescriptor>
            {
                new("Overbought",  50.0, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.12f, ZoneNoiseType: "pink"),
                new("Zero",         0.0, "#888888", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
                new("Oversold",   -50.0, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.12f, ZoneNoiseType: "pink"),
            },
            "AROON" => new List<LevelDescriptor>
            {
                new("Midpoint", 50.0, "#888888", DashStyle.Dot, PlayEarcon: true, EarconVolume: 0.7f),
            },
            "MACD" or "MOM" or "ROC" or "DPO" or "PPO" or "TRIX" or "CHAIKINOSC" or "CMF" or "CONNORSRSI" => _zeroLevel,
            _ => new List<LevelDescriptor>(),
        };

        private static List<IndicatorMetadata> BuildIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = "Macd", Name = "MACD", Category = "Trend", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "fastPeriods",   DisplayName = "Fast Periods",   DataType = typeof(int), DefaultValue = 12 },
                    new() { Name = "slowPeriods",   DisplayName = "Slow Periods",   DataType = typeof(int), DefaultValue = 26 },
                    new() { Name = "signalPeriods", DisplayName = "Signal Periods", DataType = typeof(int), DefaultValue = 9  },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    // MACD is a difference of two moving averages, so it lives in PRICE units —
                    // unlike every bounded oscillator in this file. On a sub-dollar asset all
                    // three components sit far below 0.01 and F2 spoke "0.00" for the line, the
                    // signal and the histogram alike, including across the cross that is the
                    // entire point of the indicator.
                    new() { Name = "Macd",      DisplayType = ComponentDisplayType.Line,      DefaultColorHex = "#00BCD4", DefaultThickness = 1.5f,
                            DefaultTriggerBoundaryClick = true, SpeechTemplate = "{name}. {type}. {value:price}." },
                    new() { Name = "Signal",    DisplayType = ComponentDisplayType.Line,      DefaultColorHex = "#FF9800", DefaultThickness = 1.5f,
                            SpeechTemplate = "{name}. {type}. {value:price}." },
                    new() { Name = "Histogram", DisplayType = ComponentDisplayType.Histogram, DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350",
                            DefaultColorSource = ColorSource.Value, SpeechTemplate = "{name}. {type}. {value:price}. {zone}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Mom", Name = "Momentum", Category = "Oscillators", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Momentum", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#FF7043",
                            SpeechTemplate = "{name}. {type}. {value:F2}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Roc", Name = "ROC", Category = "Oscillators", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                    // Without smaPeriods, Skender leaves RocSma null and the declared line is blank.
                    new() { Name = "smaPeriods", DisplayName = "SMA Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Momentum", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#FF7043" },
                    new() { Name = "Roc",      DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#FF7043" },
                    // Was "RocP", which RocResult does not expose; RocSma is its smoothed line.
                    new() { Name = "RocSma",   DisplayName = "ROC SMA", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#FF7043" },
                },
            },
            new IndicatorMetadata
            {
                Code = "Dpo", Name = "DPO", Category = "Oscillators", DefaultPane = "Oscillator",
                // The detrended price oscillator is centred by definition: bar j is compared with an
                // SMA shifted back lookback/2 + 1 bars, which is an average of bars that include
                // ones after j. Its final lookback/2 + 1 bars are therefore blank and a bar's value
                // only settles once that many more bars exist. Real indicator, honest chart, not a
                // strategy leaf — the prefix test flags both its components without this.
                Causality = ComponentCausality.Lookahead,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Dpo", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#42A5F5",
                            SpeechTemplate = "{name}. {type}. {value:F2}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Cmo", Name = "CMO", Category = "Oscillators", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Cmo", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#FF9800",
                            DefaultTriggerBoundaryClick = true, SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Ppo", Name = "PPO", Category = "Trend", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "fastPeriods",   DisplayName = "Fast Periods",   DataType = typeof(int), DefaultValue = 12 },
                    new() { Name = "slowPeriods",   DisplayName = "Slow Periods",   DataType = typeof(int), DefaultValue = 26 },
                    new() { Name = "signalPeriods", DisplayName = "Signal Periods", DataType = typeof(int), DefaultValue = 9  },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Ppo",       DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#00BCD4" },
                    new() { Name = "Signal",    DisplayType = ComponentDisplayType.Line,        DefaultColorHex = "#FF9800" },
                    new() { Name = "Histogram", DisplayType = ComponentDisplayType.Histogram,   DefaultColorHex = "#26A69A",
                            DefaultColorHexSecondary = "#EF5350", DefaultColorSource = ColorSource.Value },
                },
            },
            new IndicatorMetadata
            {
                Code = "Trix", Name = "TRIX", Category = "Trend", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                    // Without signalPeriods, Skender leaves Signal null — the declared signal line
                    // has been empty since it was added.
                    new() { Name = "signalPeriods", DisplayName = "Signal Periods", DataType = typeof(int), DefaultValue = 9 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Trix",   DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#AB47BC" },
                    new() { Name = "Signal", DisplayType = ComponentDisplayType.Line,        DefaultColorHex = "#FF9800" },
                },
            },
            new IndicatorMetadata
            {
                Code = "ChaikinOsc", Name = "Chaikin Oscillator", Category = "Volume", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "fastPeriods", DisplayName = "Fast Periods", DataType = typeof(int), DefaultValue = 3  },
                    new() { Name = "slowPeriods", DisplayName = "Slow Periods", DataType = typeof(int), DefaultValue = 10 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Oscillator", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350",
                            DefaultColorSource = ColorSource.Value, SpeechTemplate = "{name}. {type}. {value:F2}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Cmf", Name = "CMF", Category = "Volume", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 20 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Cmf", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350",
                            DefaultColorSource = ColorSource.Value, SpeechTemplate = "{name}. {type}. {value:F2}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "ConnorsRsi", Name = "Connors RSI", Category = "Oscillators", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "rsiPeriods",    DisplayName = "RSI Periods",    DataType = typeof(int), DefaultValue = 3  },
                    new() { Name = "streakPeriods", DisplayName = "Streak Periods", DataType = typeof(int), DefaultValue = 2  },
                    new() { Name = "rankPeriods",   DisplayName = "Rank Periods",   DataType = typeof(int), DefaultValue = 100 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "ConnorsRsi", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#9C27B0",
                            DefaultTriggerBoundaryClick = true, SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Aroon", Name = "Aroon", Category = "Trend", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 25 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "AroonUp",    DisplayType = ComponentDisplayType.Line,      DefaultColorHex = "#26A69A",
                            SpeechTemplate = "{name}. {type}. {value:F2}." },
                    new() { Name = "AroonDown",  DisplayType = ComponentDisplayType.Line,      DefaultColorHex = "#EF5350",
                            SpeechTemplate = "{name}. {type}. {value:F2}." },
                    new() { Name = "Oscillator", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#42A5F5",
                            SpeechTemplate = "{name}. {type}. {value:F2}." },
                },
            },
        };
    }
}
