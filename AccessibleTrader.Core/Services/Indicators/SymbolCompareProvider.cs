using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Compare a SECOND exchange symbol against the loaded chart — the last piece
    /// of the Wave 2 compare item. Fetches the comparison bars through
    /// <see cref="ICrossSeriesCache"/> (synchronous first-fetch, cached after)
    /// and feeds the same forward-fill + rebase/ratio engine My Data uses:
    ///
    /// <list type="bullet">
    /// <item><b>Compare symbol (overlay)</b> — the second symbol ON the price
    /// pane, rebased so its first aligned close equals the chart's close there:
    /// pure relative performance, audible as two lines starting at the same
    /// pitch and drifting apart.</item>
    /// <item><b>Compare symbol (ratio)</b> — chart close ÷ comparison close in
    /// its own pane: the classic strength read (BTC/ETH rising = BTC leading).</item>
    /// </list>
    ///
    /// Provider and symbol are string parameters (defaults follow the loaded
    /// chart's provider via the __provider hint when present). The comparison
    /// uses the CHART's timeframe via the __timeframe hint so bars align 1:1.
    /// </summary>
    public sealed class SymbolCompareProvider : IIndicatorProvider
    {
        public const string OverlayCode = "COMPARE";
        public const string RatioCode = "COMPARE_RATIO";
        public const string CompLine = "Compare";
        public const string ParamProvider = "Provider";
        public const string ParamSymbol = "Symbol";
        public const string ParamMarket = "Market";

        private readonly ICrossSeriesCache _cache;

        public SymbolCompareProvider(ICrossSeriesCache cache) => _cache = cache;

        public string Name => "Core.SymbolCompare";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            Meta(OverlayCode, "Compare symbol (overlay)", "Main",
                "Second symbol drawn on the price pane, rebased to start where this chart was — relative performance."),
            Meta(RatioCode, "Compare symbol (ratio)", "Compare ratio",
                "This chart's close divided by the comparison symbol's close — rising means the loaded symbol is leading."),
        };

        private static IndicatorMetadata Meta(string code, string name, string pane, string description) => new()
        {
            Code = code,
            Name = name,
            Category = "Overlays",
            DefaultPane = pane,
            Components = new List<IndicatorComponentMetadata>
            {
                new()
                {
                    Name = CompLine,
                    DisplayName = name,
                    Role = ComponentRole.PriceAction,
                    DisplayType = ComponentDisplayType.Line,
                    DefaultColorHex = code == OverlayCode ? "#4DD0E1" : "#BA68C8",
                    DefaultThickness = 1.6f,
                    DefaultPitchMapping = PitchMapping.Value,
                    SpeechTemplate = "{name}. {type}. {value:price}.",
                },
            },
            Parameters = new List<IndicatorParameterMetadata>
            {
                new()
                {
                    Name = ParamMarket, DisplayName = "Market",
                    Description = "Market category of the comparison symbol (Crypto, Stock, Forex, …).",
                    DataType = typeof(string), DefaultValue = "Crypto",
                },
                new()
                {
                    Name = ParamProvider, DisplayName = "Provider",
                    Description = "Data provider to fetch the comparison symbol from (e.g. Bitstamp, Binance). Defaults to the chart's own provider.",
                    DataType = typeof(string), DefaultValue = "",
                },
                new()
                {
                    Name = ParamSymbol, DisplayName = "Symbol",
                    Description = "The symbol to compare against, in the provider's own format (e.g. ETH/USD).",
                    DataType = typeof(string), DefaultValue = "ETH/USD",
                },
            },
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var span = buffer.GetComponentSpan(CompLine);
            span.Fill(double.NaN);
            if (data.Length == 0) return;

            var request = BuildRequest(parameters);
            if (request == null) return;

            var other = _cache.GetOrFetch(request);
            if (other == null || other.Count == 0) return; // fetch failed → NaN, never throw

            var aligned = MyDataSeriesProvider.AlignForwardFill(
                other.Select(t => DateTimeOffset.FromUnixTimeMilliseconds(t.Ts).UtcDateTime).ToList(),
                other.Select(t => t.Value).ToList(), data);

            int first = -1;
            for (int i = 0; i < aligned.Length; i++)
                if (!double.IsNaN(aligned[i])) { first = i; break; }
            if (first < 0) return;

            bool ratio = code.Equals(RatioCode, StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < data.Length; i++)
            {
                double v = aligned[i];
                if (double.IsNaN(v) || v == 0) continue;
                span[i] = ratio
                    ? data[i].Close / v
                    : data[first].Close * (v / aligned[first]);
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            // Comparison data refreshes on the cache's own schedule; the last-bar
            // value only moves the ratio's numerator, updated cheaply here.
            if (data.Length == 0) return;
            var request = BuildRequest(parameters);
            if (request == null) return;
            var other = _cache.GetOrFetch(request);
            if (other == null || other.Count == 0) return;
            double v = other[^1].Value;
            if (v == 0) return;
            bool ratio = code.Equals(RatioCode, StringComparison.OrdinalIgnoreCase);
            if (ratio)
                buffer.SetValue(CompLine, data.Length - 1, data[^1].Close / v);
        }

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            string symbol = ReadString(parameters, ParamSymbol, "the comparison symbol");
            return code.Equals(RatioCode, StringComparison.OrdinalIgnoreCase)
                ? $"Chart close divided by {symbol}. Rising means the loaded symbol is outperforming."
                : $"{symbol}, rebased to this chart for relative performance.";
        }

        // ── Request assembly ─────────────────────────────────────────────────

        internal CrossSeriesRequest? BuildRequest(Dictionary<string, object> parameters)
        {
            string symbol = ReadString(parameters, ParamSymbol, "");
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            // Provider defaults to the chart's own (the __provider hint); market
            // and timeframe hints come from the orchestrator the same way.
            string provider = ReadString(parameters, ParamProvider, "");
            if (string.IsNullOrWhiteSpace(provider))
                provider = ReadString(parameters, "__provider", "");
            if (string.IsNullOrWhiteSpace(provider)) return null;

            string market = ReadString(parameters, ParamMarket, "Crypto");
            string timeframe = ReadString(parameters, "__timeframe", "1d");

            return new CrossSeriesRequest(market, provider, symbol, timeframe, MaxPages: 5);
        }

        private static string ReadString(Dictionary<string, object> parameters, string name, string fallback)
        {
            if (parameters != null && parameters.TryGetValue(name, out var raw)
                && raw is string s && !string.IsNullOrWhiteSpace(s))
                return s.Trim();
            return fallback;
        }
    }
}
