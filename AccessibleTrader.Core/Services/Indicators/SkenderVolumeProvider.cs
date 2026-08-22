using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    public sealed class SkenderVolumeProvider : IIndicatorProvider
    {
        private static readonly List<IndicatorMetadata> _indicators = BuildIndicators();
        private readonly SkenderDetailFactProvider _detailFacts = new();

        public string Name => "Skender Volume";

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
            "EOM" => new List<LevelDescriptor>
            {
                new("Zero", 0.0, "#888888", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
            },
            _ => new List<LevelDescriptor>(),
        };

        private static List<IndicatorMetadata> BuildIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = "Obv", Name = "OBV", Category = "Volume", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>(),
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Obv", DefaultColorHex = "#26A69A", DefaultThickness = 1.5f,
                            SpeechTemplate = "OBV {value:F0}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Adl", Name = "ADL", Category = "Volume", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    // Skender leaves AdlSma null unless this is supplied, so without the parameter
                    // the smoothed line is declared and permanently empty.
                    new() { Name = "smaPeriods", DisplayName = "SMA Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Adl",  DefaultColorHex = "#26A69A" },
                    // Was "Adl3", which AdlResult does not expose. AdlSma is the smoothed line.
                    new() { Name = "AdlSma", DisplayName = "ADL SMA", DefaultColorHex = "#FF9800" },
                },
            },
            new IndicatorMetadata
            {
                Code = "Eom", Name = "EOM", Category = "Volume", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Eom", DisplayType = ComponentDisplayType.Histogram, DefaultColorHex = "#26A69A",
                            DefaultColorHexSecondary = "#EF5350", DefaultColorSource = ColorSource.Value,
                            SpeechTemplate = "{name}. {value:F4}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "ForceIndex", Name = "Force Index", Category = "Volume", DefaultPane = "Oscillator",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 13 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "ForceIndex", DisplayType = ComponentDisplayType.Histogram,
                            DefaultColorHex = "#EF5350", DefaultColorHexSecondary = "#26A69A",
                            DefaultColorSource = ColorSource.Value, SpeechTemplate = "{name}. {value:F0}." },
                },
            },
            new IndicatorMetadata
            {
                Code = "Vwma", Name = "VWMA", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 14 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Vwma", DefaultColorHex = "#F48FB1", DefaultThickness = 1.5f },
                },
            },
        };
    }
}
