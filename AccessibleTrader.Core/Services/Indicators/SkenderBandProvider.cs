using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    public sealed class SkenderBandProvider : IIndicatorProvider
    {
        private static readonly List<IndicatorMetadata> _indicators = BuildIndicators();
        private readonly SkenderDetailFactProvider _detailFacts = new();

        public string Name => "Skender Bands";

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
                Code = "Bb", Name = "Bollinger Bands", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods",    DisplayName = "Lookback Periods",    DataType = typeof(int),    DefaultValue = 20  },
                    new() { Name = "standardDeviations", DisplayName = "Standard Deviations", DataType = typeof(double), DefaultValue = 2.0 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "UpperBand",  DefaultColorHex = "#1565C0", DefaultDashStyle = DashStyle.Dash, DefaultThickness = 1f,
                            SpeechTemplate = "At upper band. {value:price}." },
                    new() { Name = "LowerBand",  DefaultColorHex = "#1565C0", DefaultDashStyle = DashStyle.Dash, DefaultThickness = 1f,
                            SpeechTemplate = "At lower band. {value:price}." },
                    // Every visible Bollinger component is an absolute price on the price axis,
                    // so all four take {value:price}. F2 collapsed the whole envelope of a
                    // sub-dollar asset to "0.00" — three lines that all read the same.
                    // Declared ONCE. It was declared twice, so the Bollinger centre line was
                    // registered as two components: navigable twice, sonified twice, two
                    // voices playing the same value.
                    new() { Name = "Sma",        DefaultColorHex = "#42A5F5", DefaultThickness = 1.5f,
                            SpeechTemplate = "{name}. {type}. {value:price}." },
                    new() { Name = "PercentB",  DefaultColorHex = "#42A5F5", DefaultThickness = 1f },
                    new() { Name = "ZScore",     DefaultColorHex = "#42A5F5", DefaultThickness = 1f },
                    new() { Name = "Width",      DefaultColorHex = "#90CAF9", DefaultThickness = 1f },
                },
            },
            new IndicatorMetadata
            {
                Code = "Kc", Name = "Keltner Channel", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int),    DefaultValue = 20  },
                    new() { Name = "multiplier",      DisplayName = "Multiplier",       DataType = typeof(double), DefaultValue = 2.0 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "UpperBand",  DefaultColorHex = "#FF9800", DefaultDashStyle = DashStyle.Dash, DefaultThickness = 1f },
                    new() { Name = "LowerBand",  DefaultColorHex = "#FF9800", DefaultDashStyle = DashStyle.Dash, DefaultThickness = 1f },
                    new() { Name = "Centerline", DefaultColorHex = "#FFD700", DefaultThickness = 1.5f },
                },
            },
            new IndicatorMetadata
            {
                Code = "Donchian", Name = "Donchian Channel", Category = "Overlays", DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "lookbackPeriods", DisplayName = "Lookback Periods", DataType = typeof(int), DefaultValue = 20 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "UpperBand",  DefaultColorHex = "#B71C1C", DefaultDashStyle = DashStyle.Dash, DefaultThickness = 1f },
                    new() { Name = "LowerBand",  DefaultColorHex = "#1B5E20", DefaultDashStyle = DashStyle.Dash, DefaultThickness = 1f },
                    new() { Name = "Centerline", DefaultColorHex = "#607D8B", DefaultThickness = 1f },
                },
            },
        };
    }
}
