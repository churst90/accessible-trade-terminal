using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The higher-timeframe cache a multi-timeframe strategy leaf reads through.
    ///
    /// <para>
    /// Written for mutation campaign A2l (2026-09-24): the class had no test that constructed it.
    /// Both of its quiet failure modes end the same way — the HTF leaf evaluates NaN and is
    /// therefore false on every bar for the life of the strategy, with nothing said — so both are
    /// pinned here: a cached series SHORTER than the leaf asked for being served as if it were
    /// enough, and an HTF indicator computed with no parameters at all when the caller passed an
    /// empty map (which is what ConfigurableStrategy passes).
    /// </para>
    /// </summary>
    public class MultiTimeframeDataServiceTests
    {
        private static List<Ohlcv> Bars(int n) => Enumerable.Range(0, n)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddHours(4 * i), 100 + i, 101 + i, 99 + i, 100 + i, 10))
            .ToList();

        private static IDataOrchestrator Orchestrator(Func<int, List<Ohlcv>> answer, List<int?> asked)
        {
            var o = Substitute.For<IDataOrchestrator>();
            o.FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<long?>(), Arg.Any<int?>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
             .Returns(ci =>
             {
                 int? limit = ci.ArgAt<int?>(5);
                 asked.Add(limit);
                 return Task.FromResult(answer(limit ?? 0));
             });
            return o;
        }

        [Fact]
        public async Task A_cached_series_shorter_than_the_request_is_refetched_not_served()
        {
            var asked = new List<int?>();
            var svc = new MultiTimeframeDataService(Orchestrator(n => Bars(n), asked), Substitute.For<IAppLogger>());

            var first = await svc.GetBarsAsync("Spot", "Kraken", "BTC/USD", "4h", 50);
            Assert.Equal(50, first.Count);

            // Same key, inside the TTL, but a leaf that needs 200 bars of history — a 200-period
            // HTF SMA on 50 bars is NaN on every bar.
            var second = await svc.GetBarsAsync("Spot", "Kraken", "BTC/USD", "4h", 200);

            Assert.Equal(200, second.Count);
            Assert.Equal(new int?[] { 50, 200 }, asked);

            // And a SMALLER request inside the TTL is answered from the cache (the positive
            // control: the refetch above is about length, not about the cache being off).
            var third = await svc.GetBarsAsync("Spot", "Kraken", "BTC/USD", "4h", 100);
            Assert.Equal(200, third.Count);
            Assert.Equal(2, asked.Count);
        }

        [Fact]
        public async Task An_htf_indicator_asked_for_with_no_parameters_is_computed_with_its_declared_defaults()
        {
            var asked = new List<int?>();
            var engine = Substitute.For<IIndicatorEngine>();
            var provider = Substitute.For<IIndicatorProvider>();
            provider.GetIndicators().Returns(new List<IndicatorMetadata>
            {
                new()
                {
                    Code = "SMA", Name = "SMA",
                    Parameters = new List<IndicatorParameterMetadata> { new() { Name = "Period", DefaultValue = 20 } },
                },
            });
            engine.GetProvider("SMA").Returns(provider);

            Dictionary<string, object>? used = null;
            engine.CalculateAsync("SMA", Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
                  .Returns(ci =>
                  {
                      used = ci.ArgAt<Dictionary<string, object>>(2);
                      return Task.FromResult(new Dictionary<string, double[]> { ["SMA"] = new double[] { 1, 2, 3 } });
                  });

            var svc = new MultiTimeframeDataService(Orchestrator(n => Bars(n), asked), Substitute.For<IAppLogger>(), engine);

            // ConfigurableStrategy passes an EMPTY map, deliberately — see PrewarmIndicatorAsync.
            await svc.PrewarmIndicatorAsync("Spot", "Kraken", "BTC/USD", "1d", "SMA", new Dictionary<string, object>(), 300);

            Assert.NotNull(used);
            Assert.Equal(20, used!["Period"]);
            Assert.NotNull(svc.GetCachedIndicator("Kraken", "BTC/USD", "1d", "SMA"));
        }
    }
}
