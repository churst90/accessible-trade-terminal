using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    public sealed class SkenderTrendProvider : IIndicatorProvider
    {
        private static readonly List<IndicatorMetadata> _indicators = BuildIndicators();
        private readonly SkenderDetailFactProvider _detailFacts = new();

        public string Name => "Skender Trend";

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

        public List<LevelDescriptor> GetDefaultLevels(string code) => code.ToUpperInvariant() switch
        {
            "ADX" => new List<LevelDescriptor>
            {
                new("Developing",   20.0, "#888888", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.5f),
                new("Strong",       25.0, "#888888", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f),
                new("Very Strong",  50.0, "#FF9800", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
            },
            "CHOP" => new List<LevelDescriptor>
            {
                new("Trending", 38.2, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f),
                new("Ranging",  61.8, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f),
            },
            "STC" => new List<LevelDescriptor>
            {
                new("Overbought", 75.0, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.12f, ZoneNoiseType: "pink"),
                new("Oversold",   25.0, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.12f, ZoneNoiseType: "pink"),
            },
            _ => new List<LevelDescriptor>(),
        };

        private static List<IndicatorMetadata> BuildIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = "Ema", Name = "EMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 9 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Ema", DefaultColorHex = "#FF9800", DefaultThickness = 1.5f,
                            SpeechTemplate = "{name}. {type}. {value:F2}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Sma", Name = "SMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 20 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Sma", DefaultColorHex = "#FFD700", DefaultThickness = 1.5f,
                            SpeechTemplate = "{name}. {type}. {value:F2}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Wma", Name = "WMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Wma", DefaultColorHex = "#4CAF50", DefaultThickness = 1.5f },
                },
            },
            new IndicatorMetadata
            {
                Code = "Hma", Name = "HMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Hma", DefaultColorHex = "#4CAF50", DefaultThickness = 1.5f },
                },
            },
            new IndicatorMetadata
            {
                Code = "Alma", Name = "ALMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int),    DefaultValue = 9    },
                    new() { Name = "offset",          DisplayName = "Offset",           DataType = typeof(double), DefaultValue = 0.85 },
                    new() { Name = "sigma",           DisplayName = "Sigma",            DataType = typeof(int),    DefaultValue = 6    },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Alma", DefaultColorHex = "#26C6DA", DefaultThickness = 1.5f },
                },
            },
            new IndicatorMetadata
            {
                Code = "Dema", Name = "DEMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Dema", DefaultColorHex = "#FF7043", DefaultThickness = 1.5f },
                },
            },
            new IndicatorMetadata
            {
                Code = "Tema", Name = "TEMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Tema", DefaultColorHex = "#EC407A", DefaultThickness = 1.5f },
                },
            },
            new IndicatorMetadata
            {
                Code = "Kama", Name = "KAMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 10 },
                    new() { Name = "fastPeriods",     DisplayName = "Fast Periods",     DataType = typeof(int), DefaultValue = 2  },
                    new() { Name = "slowPeriods",     DisplayName = "Slow Periods",     DataType = typeof(int), DefaultValue = 30 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Kama", DefaultColorHex = "#7E57C2", DefaultThickness = 1.5f },
                },
            },
            new IndicatorMetadata
            {
                Code = "Vwap", Name = "VWAP", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>(),
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Vwap", DefaultColorHex = "#F48FB1", DefaultThickness = 1.5f,
                            SpeechTemplate = "VWAP. {value:price}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "ParabolicSar", Name = "Parabolic SAR", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "accelerationStep",      DisplayName = "Acceleration Step",      DataType = typeof(double), DefaultValue = 0.02 },
                    new() { Name = "maxAccelerationFactor", DisplayName = "Max Acceleration Factor", DataType = typeof(double), DefaultValue = 0.2  },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Sar", DisplayType = ComponentDisplayType.Dot, DefaultColorHex = "#FFD700" },
                },
            },
            new IndicatorMetadata
            {
                Code = "Adx", Name = "ADX", Category = "Trend", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Adx", DefaultColorHex = "#9E9E9E", SpeechTemplate = "ADX {value:F1}. {zone}." },
                    new() { Name = "Pdi", DefaultColorHex = "#26A69A" },
                    new() { Name = "Mdi", DefaultColorHex = "#EF5350" },
                    // Was "Adl"/"Adh" — names AdxResult has never exposed, so both rendered blank.
                    // ("Adl" also collides conceptually with the Accumulation/Distribution Line,
                    // which is a different indicator entirely.) Adxr is the smoothed ADX rating
                    // the result actually carries.
                    new() { Name = "Adxr", DefaultColorHex = "#78909C" },
                },
            },
            new IndicatorMetadata
            {
                Code = "Vortex", Name = "Vortex", Category = "Trend", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    // VortexResult exposes Pvi/Nvi; "Vip"/"Vim" matched nothing and both lines
                    // rendered empty.
                    new() { Name = "Pvi", DisplayName = "VI+", DefaultColorHex = "#26A69A", SpeechTemplate = "{name}. {value:F2}." },
                    new() { Name = "Nvi", DisplayName = "VI-", DefaultColorHex = "#EF5350", SpeechTemplate = "{name}. {value:F2}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Chop", Name = "Choppiness Index", Category = "Trend", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    // ChopResult exposes "Chop"; the declared "ChopIndex" was its only component.
                    new() { Name = "Chop", DisplayName = "Choppiness", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#9E9E9E",
                            SpeechTemplate = "{name}. {value:F2}. {zone}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Stc", Name = "STC", Category = "Trend", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "cyclePeriods", DisplayName = "Cycle Periods", DataType = typeof(int), DefaultValue = 10 },
                    new() { Name = "fastPeriods",  DisplayName = "Fast Periods",  DataType = typeof(int), DefaultValue = 23 },
                    new() { Name = "slowPeriods",  DisplayName = "Slow Periods",  DataType = typeof(int), DefaultValue = 50 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Stc", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#00BCD4",
                            DefaultTriggerBoundaryClick = true, SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Zlema", Name = "ZLEMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Zlema", DefaultColorHex = "#26C6DA", DefaultThickness = 1.5f },
                },
            },
            new IndicatorMetadata
            {
                Code = "Smma", Name = "SMMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Smma", DefaultColorHex = "#4CAF50", DefaultThickness = 1.5f },
                },
            },
            new IndicatorMetadata
            {
                Code = "Tma", Name = "TMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Tma", DefaultColorHex = "#FFD700", DefaultThickness = 1.5f },
                },
            },
        };
    }
}
