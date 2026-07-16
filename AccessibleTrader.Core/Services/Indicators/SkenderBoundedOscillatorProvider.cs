using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    public sealed class SkenderBoundedOscillatorProvider : IIndicatorProvider
    {
        private static readonly List<IndicatorMetadata> _indicators = BuildIndicators();
        private readonly SkenderDetailFactProvider _detailFacts = new();

        public string Name => "Skender Bounded Oscillators";

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

        // Sound contract for bounded oscillators: the line itself is CLEAN
        // (DefaultNoiseAmount 0 on every component below) and noise texture appears
        // ONLY inside an overbought/oversold zone (ZoneNoiseAmount on the levels
        // here). A constant baseline tinge — the old 0.08 — defeats the cue: once
        // the engine's noise makeup gain made it audible, "rough everywhere" told
        // the user nothing. Zone level 0.45 is tuned to read clearly over the tone.
        public List<LevelDescriptor> GetDefaultLevels(string code) => code.ToUpperInvariant() switch
        {
            "RSI" => new List<LevelDescriptor>
            {
                new("Overbought", 70.0, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
                new("Midpoint",   50.0, "#888888", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.7f),
                new("Oversold",   30.0, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
            },
            "STOCH" or "STOCHRSI" => new List<LevelDescriptor>
            {
                new("Overbought", 80.0, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
                new("Midpoint",   50.0, "#888888", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.7f),
                new("Oversold",   20.0, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
            },
            "ULTOSC" => new List<LevelDescriptor>
            {
                new("Overbought", 70.0, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
                new("Oversold",   30.0, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
            },
            "WILLIAMSR" => new List<LevelDescriptor>
            {
                new("Overbought", -20.0, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
                new("Midpoint",   -50.0, "#888888", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.7f),
                new("Oversold",   -80.0, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
            },
            "MFI" => new List<LevelDescriptor>
            {
                new("Overbought", 80.0, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
                new("Midpoint",   50.0, "#888888", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.7f),
                new("Oversold",   20.0, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
            },
            "CCI" => new List<LevelDescriptor>
            {
                new("Overbought",  100.0, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
                new("Zero",          0.0, "#888888", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
                new("Oversold",   -100.0, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.45f, ZoneNoiseType: "pink"),
            },
            _ => new List<LevelDescriptor>(),
        };

        private static List<IndicatorMetadata> BuildIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = "Rsi", Name = "RSI", Category = "Oscillators", DefaultPane = "Oscillator",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Rsi", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#9C27B0",
                            DefaultThickness = 1.5f, DefaultTriggerBoundaryClick = true,
                            DefaultNoiseAmount = 0f,
                            SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Stoch", Name = "Stochastic", Category = "Oscillators", DefaultPane = "Oscillator",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                    new() { Name = "signalPeriods",   DisplayName = "Signal Periods",   DataType = typeof(int), DefaultValue = 3  },
                    new() { Name = "smoothPeriods",   DisplayName = "Smooth Periods",   DataType = typeof(int), DefaultValue = 3  },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Oscillator", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#00B8D4",
                            DefaultTriggerBoundaryClick = true, DefaultNoiseAmount = 0f,
                            SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                    new() { Name = "Signal",     DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#E65100",
                            DefaultNoiseAmount = 0f, SpeechTemplate = "{name}. {type}. {value:F2}." },
                    new() { Name = "PercentK",   DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#00B8D4",
                            DefaultTriggerBoundaryClick = true, DefaultNoiseAmount = 0f,
                            SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                    new() { Name = "PercentD",   DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#E65100",
                            DefaultNoiseAmount = 0f, SpeechTemplate = "{name}. {type}. {value:F2}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "StochRsi", Name = "Stoch RSI", Category = "Oscillators", DefaultPane = "Oscillator",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "rsiPeriods",   DisplayName = "RSI Periods",    DataType = typeof(int), DefaultValue = 14 },
                    new() { Name = "stochPeriods", DisplayName = "Stoch Periods",  DataType = typeof(int), DefaultValue = 14 },
                    new() { Name = "signalPeriods",DisplayName = "Signal Periods", DataType = typeof(int), DefaultValue = 3  },
                    new() { Name = "smoothPeriods",DisplayName = "Smooth Periods", DataType = typeof(int), DefaultValue = 1  },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "StochRsi", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#00B8D4",
                            DefaultTriggerBoundaryClick = true, DefaultNoiseAmount = 0f,
                            SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                    new() { Name = "Signal",   DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#E65100",
                            DefaultNoiseAmount = 0f, SpeechTemplate = "{name}. {type}. {value:F2}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "UltOsc", Name = "Ultimate Oscillator", Category = "Oscillators", DefaultPane = "Oscillator",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "shortPeriods",  DisplayName = "Short Periods",  DataType = typeof(int), DefaultValue = 7  },
                    new() { Name = "middlePeriods", DisplayName = "Middle Periods", DataType = typeof(int), DefaultValue = 14 },
                    new() { Name = "longPeriods",   DisplayName = "Long Periods",   DataType = typeof(int), DefaultValue = 28 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "UltOsc", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#AB47BC",
                            DefaultTriggerBoundaryClick = true, DefaultNoiseAmount = 0f,
                            SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "WilliamsR", Name = "Williams %R", Category = "Oscillators", DefaultPane = "Oscillator",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "WilliamsR", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#26C6DA",
                            DefaultTriggerBoundaryClick = true, DefaultNoiseAmount = 0f,
                            SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Mfi", Name = "MFI", Category = "Oscillators", DefaultPane = "Oscillator",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Mfi", DisplayType = ComponentDisplayType.Oscillator,
                            DefaultColorHex = "#26A69A", DefaultColorHexSecondary = "#EF5350",
                            DefaultColorSource = ColorSource.Value, DefaultTriggerBoundaryClick = true,
                            DefaultNoiseAmount = 0f, ColorBaseline = 50.0 },
                },
            },
            new IndicatorMetadata
            {
                Code = "Cci", Name = "CCI", Category = "Oscillators", DefaultPane = "Oscillator",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 20 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Cci", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#AB47BC",
                            DefaultTriggerBoundaryClick = true, DefaultNoiseAmount = 0f,
                            SpeechTemplate = "{name}. {type}. {value:F2}. {zone}." },
                },
            },
        };
    }
}
