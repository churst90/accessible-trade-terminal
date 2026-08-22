using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    public sealed class SkenderVolatilityProvider : IIndicatorProvider
    {
        private static readonly List<IndicatorMetadata> _indicators = BuildIndicators();
        private readonly SkenderDetailFactProvider _detailFacts = new();

        public string Name => "Skender Volatility";

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

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        private static List<IndicatorMetadata> BuildIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = "Atr", Name = "ATR", Category = "Volatility", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Atr", DefaultColorHex = "#FF5722", SpeechTemplate = "ATR {value:F2}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "StdDev", Name = "Std Dev", Category = "Volatility", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "StdDev", DefaultColorHex = "#9C27B0" },
                    new() { Name = "Mean",   DefaultColorHex = "#607D8B" },
                    new() { Name = "ZScore", DefaultColorHex = "#FF9800" },
                },
            },
            new IndicatorMetadata
            {
                Code = "ChandelierExit", Name = "Chandelier Exit", Category = "Volatility", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int),    DefaultValue = 22  },
                    new() { Name = "multiplier",      DisplayName = "Multiplier",       DataType = typeof(double), DefaultValue = 3.0 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "ChandelierExit", DefaultColorHex = "#FF7043" },
                },
            },
            new IndicatorMetadata
            {
                Code = "Hv", Name = "Historical Volatility", Category = "Volatility", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 20 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Hv", DefaultColorHex = "#9E9E9E" },
                },
            },
            new IndicatorMetadata
            {
                Code = "UlcerIndex", Name = "Ulcer Index", Category = "Volatility", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "UlcerIndex", DisplayType = ComponentDisplayType.Oscillator, DefaultColorHex = "#EF5350",
                            SpeechTemplate = "{name}. {value:F2}." },
                },
            },
        };
    }
}
