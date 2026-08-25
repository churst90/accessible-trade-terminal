using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Symbol-vs-symbol compare — the last Wave 2 compare piece. Pins the request
    /// assembly (explicit provider beats the chart hint; chart provider/timeframe
    /// hints as defaults; no symbol → no fetch), the overlay rebase, the ratio,
    /// and the fetch-failed → NaN (never throw) contract.
    /// </summary>
    public class SymbolCompareProviderTests
    {
        private static Ohlcv[] DailyBars(int days, double startClose = 100) => Enumerable.Range(0, days)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                startClose + i, startClose + i + 1, startClose + i - 1, startClose + i, 1000))
            .ToArray();

        private static long Ms(int day) =>
            new DateTimeOffset(new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        private static (SymbolCompareProvider Provider, ICrossSeriesCache Cache) Build(
            params (long Ts, double Value)[] otherSeries)
        {
            var cache = Substitute.For<ICrossSeriesCache>();
            cache.GetOrFetch(Arg.Any<CrossSeriesRequest>())
                .Returns(otherSeries.ToList());
            return (new SymbolCompareProvider(cache), cache);
        }

        private static Dictionary<string, object> Params(
            string symbol = "ETH/USD", string provider = "", string market = "Crypto")
            => new()
            {
                [SymbolCompareProvider.ParamSymbol] = symbol,
                [SymbolCompareProvider.ParamProvider] = provider,
                [SymbolCompareProvider.ParamMarket] = market,
                ["__provider"] = "Bitstamp",
                ["__timeframe"] = "1d",
            };

        [Fact]
        public void Request_defaults_to_the_charts_provider_and_timeframe()
        {
            var (provider, _) = Build();
            var req = provider.BuildRequest(Params())!;
            Assert.Equal("Bitstamp", req.Provider); // __provider hint
            Assert.Equal("1d", req.Timeframe);      // __timeframe hint
            Assert.Equal("ETH/USD", req.Symbol);
        }

        [Fact]
        public void Explicit_provider_parameter_wins_over_the_hint()
        {
            var (provider, _) = Build();
            var req = provider.BuildRequest(Params(provider: "Binance"))!;
            Assert.Equal("Binance", req.Provider);
        }

        [Fact]
        public void No_symbol_means_no_fetch()
        {
            var (provider, _) = Build();
            Assert.Null(provider.BuildRequest(Params(symbol: "")));
        }

        [Fact]
        public void Overlay_rebases_the_second_symbol_to_the_chart()
        {
            // Comparison series: 10 on Jan 1, 15 on Jan 3 (+50%). Chart closes 100..104.
            var (provider, _) = Build((Ms(1), 10), (Ms(3), 15));
            var bars = DailyBars(5);
            var buffer = new IndicatorResultBuffer(new Dictionary<string, double[]>(), bars.Length);

            provider.Calculate(SymbolCompareProvider.OverlayCode, bars, Params(), buffer);
            var span = buffer.GetComponentSpan(SymbolCompareProvider.CompLine);

            Assert.Equal(100, span[0], 10);        // starts at the chart's close
            Assert.Equal(100, span[1], 10);        // forward-filled (no Jan 2 data)
            Assert.Equal(150, span[2], 10);        // +50% in the comparison → +50% here
        }

        [Fact]
        public void Ratio_divides_chart_close_by_the_second_symbol()
        {
            var (provider, _) = Build((Ms(1), 10), (Ms(3), 20));
            var bars = DailyBars(5);
            var buffer = new IndicatorResultBuffer(new Dictionary<string, double[]>(), bars.Length);

            provider.Calculate(SymbolCompareProvider.RatioCode, bars, Params(), buffer);
            var span = buffer.GetComponentSpan(SymbolCompareProvider.CompLine);

            Assert.Equal(10.0, span[0], 10);       // 100 / 10
            Assert.Equal(102.0 / 20, span[2], 10); // dataset doubled → ratio halves
        }

        [Fact]
        public void Failed_fetch_renders_NaN_and_never_throws()
        {
            var cache = Substitute.For<ICrossSeriesCache>();
            cache.GetOrFetch(Arg.Any<CrossSeriesRequest>())
                .Returns(new List<(long, double)>()); // empty = failed per the cache contract
            var provider = new SymbolCompareProvider(cache);
            var bars = DailyBars(3);
            var buffer = new IndicatorResultBuffer(new Dictionary<string, double[]>(), bars.Length);

            provider.Calculate(SymbolCompareProvider.OverlayCode, bars, Params(), buffer);

            Assert.All(buffer.GetComponentSpan(SymbolCompareProvider.CompLine).ToArray(),
                v => Assert.True(double.IsNaN(v)));
        }

        [Fact]
        public void Both_indicator_entries_carry_string_parameters_for_the_dialog()
        {
            var (provider, _) = Build();
            var metas = provider.GetIndicators();
            Assert.Equal(2, metas.Count);
            Assert.All(metas, m => Assert.Contains(m.Parameters,
                p => p.Name == SymbolCompareProvider.ParamSymbol && p.DataType == typeof(string)));
            Assert.Contains(metas, m => m.DefaultPane == "Main");        // overlay
            Assert.Contains(metas, m => m.DefaultPane == "Compare ratio"); // ratio pane
        }
    }
}
