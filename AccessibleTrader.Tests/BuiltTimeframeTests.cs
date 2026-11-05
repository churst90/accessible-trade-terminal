using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A timeframe the venue does not list is built, not refused (2026-11-04).</b> Cody, on an 8h tab:
/// "why does it say bitstamp doesn't offer 8h, switching to 1h......I calculate custom timeframe
/// candles, why are they not being used. it should happen seemlessly in the background. the supported
/// timeframes from the exchange should be in the quick pick bar like we've got, but if I enter 7 hours
/// then it should load 7 hour candles". The fetch already built them; the toolbar refused them first.
/// And the chart's own fetch kept a short first bar, which the browser-closed monitor already dropped.
/// </summary>
public sealed class BuiltTimeframeTests
{
    // Bitstamp's own lists: no 8h or 7h in either.
    private static readonly List<string> Listed = new() { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "12h", "1d", "3d", "1w", "1M" };
    private static readonly List<string> Native = new() { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "12h", "1d", "3d" };

    private static (MarketOrchestrator Orch, List<string> Said) Toolbar(List<string> listed, List<string> native)
    {
        var data = Substitute.For<IDataService>();
        data.LoadAvailableMarketsAsync().Returns(_ => Task.FromResult(new List<string> { "Crypto" }));
        data.LoadProvidersByMarketTypeAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(new List<string> { "Bitstamp" }));
        data.GetSupportedSubTypesAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(_ => Task.FromResult(new List<string> { "Spot" }));
        data.LoadSymbolsAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(_ => Task.FromResult(new List<string> { "BTCUSDT" }));
        data.GetSupportedTimeframesAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(new List<string>(listed)));
        data.GetNativeTimeframes(Arg.Any<string>()).Returns(_ => new List<string>(native));
        data.ProviderRequiresApiKeyAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(false));
        data.IsProviderConfiguredAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(true));

        var bus = new EventBus();
        var said = new List<string>();
        bus.Subscribe<FeedbackRequestEvent>(e => { lock (said) said.Add(e.Message ?? ""); });
        var store = new WorkspaceStore(bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());
        var orch = new MarketOrchestrator(data, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(isDemo: false));
        return (orch, said);
    }

    [Theory]
    [InlineData("8h")]
    [InlineData("7h")]
    [InlineData("45m")]
    [InlineData("2w")]
    public async Task A_timeframe_the_venue_can_build_is_kept_silently_and_the_quick_picks_stay_the_venues(string tf)
    {
        var (orch, said) = Toolbar(Listed, Native);
        await orch.RefreshPipelineAsync();
        orch.SelectedTimeframe = tf;

        await orch.RefreshSymbolsAsync();

        Assert.Equal(tf, orch.SelectedTimeframe);
        Assert.DoesNotContain(said, m => m.Contains("does not offer"));
        Assert.Equal(Listed, orch.AvailableTimeframes);
    }

    [Fact]
    public async Task A_timeframe_finer_than_anything_the_venue_serves_is_still_changed_and_said()
    {
        var (orch, said) = Toolbar(new() { "1d", "1w" }, new() { "1d" });
        await orch.RefreshPipelineAsync();
        orch.SelectedTimeframe = "1h";

        await orch.RefreshSymbolsAsync();

        Assert.Equal("1d", orch.SelectedTimeframe);
        Assert.Contains(said, m => m.Contains("does not offer 1h"));
    }

    /// <summary>
    /// My Data declares every timeframe native and serves only the file's own spacing, which is all
    /// it lists. Trusting the native list kept 1h on a daily import and charted nothing: the browser
    /// suite's seeded chart came up empty on 85b9afc7.
    /// </summary>
    [Fact]
    public async Task A_provider_that_claims_everything_native_but_lists_one_spacing_is_still_moved_to_it()
    {
        var (orch, said) = Toolbar(new() { "1d" }, new() { "1m", "1h", "1d", "1w", "1M", "1y" });
        await orch.RefreshPipelineAsync();
        orch.SelectedTimeframe = "1h";

        await orch.RefreshSymbolsAsync();

        Assert.Equal("1d", orch.SelectedTimeframe);
        Assert.Contains(said, m => m.Contains("provides 1d data only"));
    }

    [Theory]
    [InlineData("8h", true)]
    [InlineData("7h", true)]
    [InlineData("4h", true)]
    [InlineData("90m", true)]
    [InlineData("1w", true)]
    [InlineData("30s", false)]
    [InlineData("", false)]
    [InlineData("8 hours", false)]
    public void CanBuildFrom_accepts_only_what_a_native_timeframe_divides(string tf, bool expected)
    {
        Assert.Equal(expected, TimeframeUtility.CanBuildFrom(tf, Native));
    }

    [Fact]
    public void CanBuildFrom_refuses_a_timeframe_finer_than_the_venue_or_one_nothing_divides()
    {
        Assert.False(TimeframeUtility.CanBuildFrom("1h", new[] { "1d" }));
        Assert.False(TimeframeUtility.CanBuildFrom("90m", new[] { "1h", "1d" }));
        Assert.True(TimeframeUtility.CanBuildFrom("90m", new[] { "30m", "1h" }));
    }

    private static (HistoricalDataFetcher Fetcher, List<MarketDataRequest> Asked) Fetcher(List<Ohlcv> bars, List<string> native)
    {
        var provider = Substitute.For<AccessibleTrader.Sdk.Plugins.IMarketDataProvider>();
        provider.NativelySupportedTimeframes.Returns(native);
        provider.MaxBarsPerRequest.Returns(1000);
        var asked = new List<MarketDataRequest>();
        var data = Substitute.For<IDataService>();
        data.GetProviderAsync("Bitstamp").Returns(provider);
        data.FetchOhlcvAsync("Bitstamp", Arg.Any<MarketDataRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => { asked.Add((MarketDataRequest)ci[1]); return (bars, new List<(long, double)>()); });
        return (new HistoricalDataFetcher(data, new ResamplerService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HistoricalDataFetcher>.Instance), asked);
    }

    /// <summary>4h bars from 04:00: the 00:00 8h bucket holds only its second half.</summary>
    private static List<Ohlcv> FourHourBarsFrom0400(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new Ohlcv(new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc).AddHours(4 * i),
                1000 + i, 1010 + i * 3, 990 - i, 1000 + i * 2, 10))
            .ToList();

    [Fact]
    public async Task An_8h_chart_is_built_from_4h_bars_and_the_short_first_bar_is_dropped()
    {
        var four = FourHourBarsFrom0400(9);   // 04:00 … 12:00 next day
        var (fetcher, asked) = Fetcher(four, Native);

        var built = await fetcher.FetchOhlcvAsync("Crypto|Spot", "Bitstamp", "BTCUSDT", "8h", limit: 200);

        Assert.Equal("4h", Assert.Single(asked).Timeframe);
        Assert.Equal(402, asked[0].Limit);   // one bucket more than asked, in 4h bars
        // 04:00 alone was the 00:00 bucket; it is gone. The rest pair up: 08:00+12:00, 16:00+20:00, …
        Assert.Equal(new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc), built[0].Date);
        Assert.All(built, b => Assert.Equal(0, b.Date.Hour % 8));
        var first = built[0];
        Assert.Equal(four[1].Open, first.Open);
        Assert.Equal(Math.Max(four[1].High, four[2].High), first.High);
        Assert.Equal(Math.Min(four[1].Low, four[2].Low), first.Low);
        Assert.Equal(four[2].Close, first.Close);
        Assert.Equal(20, first.Volume);
    }

    [Fact]
    public async Task A_first_bucket_whose_bars_start_on_its_boundary_is_whole_and_kept()
    {
        var four = FourHourBarsFrom0400(9).Skip(1).ToList();   // from 08:00, an 8h boundary
        var (fetcher, _) = Fetcher(four, Native);

        var built = await fetcher.FetchOhlcvAsync("Crypto|Spot", "Bitstamp", "BTCUSDT", "8h", limit: 200);

        Assert.Equal(new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc), built[0].Date);
        Assert.Equal(4, built.Count);
    }

    [Fact]
    public async Task A_7h_chart_is_built_from_1h_bars()
    {
        // 7h does not divide a day, so its buckets are counted from 1970: on 2026-11-01 they start at
        // 05:00, 12:00 and 19:00. Start on one so every bucket is whole.
        var start = new DateTime(2026, 11, 1, 5, 0, 0, DateTimeKind.Utc);
        Assert.Equal(start, TimeframeUtility.GetPeriodStart(start.AddHours(3), "7h"));
        var hours = Enumerable.Range(0, 21)
            .Select(i => new Ohlcv(start.AddHours(i), 500 + i, 510 + i, 490 + i, 501 + i, 1)).ToList();
        var (fetcher, asked) = Fetcher(hours, Native);

        var built = await fetcher.FetchOhlcvAsync("Crypto|Spot", "Bitstamp", "BTCUSDT", "7h", limit: 200);

        Assert.Equal("1h", Assert.Single(asked).Timeframe);
        Assert.Equal(3, built.Count);
        Assert.Equal(start, built[0].Date);
        Assert.Equal(500, built[0].Open);
        Assert.Equal(516, built[0].High);
        Assert.Equal(507, built[0].Close);
        Assert.Equal(7, built[0].Volume);
    }
}
