using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AccessibleTrader.Tests;

/// <summary>
/// Go bar FUND: the chart's perpetual right now, from its venue. The numbers are Hyperliquid's
/// BTC context as recorded on 2026-11-26 (Fixtures/Hyperliquid/recorded_metaAndAssetCtxs.json).
/// </summary>
public class FundingNowServiceTests
{
    private static readonly PerpetualContext RecordedBtc =
        new("BTC-PERP", -0.0000107689, 1, 35269.2580999999, 60427, 60452, 3405189961.0582513809);

    private static readonly ChartIdentity Btc = new("Derivatives", "Hyperliquid", "BTC-PERP", "1h");

    [Fact]
    public void The_recorded_btc_context_is_said_per_8_hours_with_who_pays_and_what_it_comes_to()
    {
        string s = FundingNowService.Sentence(RecordedBtc, "Hyperliquid");

        Assert.Equal(
            "BTC-PERP on Hyperliquid, now. " +
            "Funding 0.0086 percent per 8 hours, shorts pay longs, paid every hour, about 9.4 percent a year. " +
            "Open interest 35,269 BTC, about $2.13 billion. " +
            "Mark 60427.00, index 60452.00, mark 0.041 percent below. " +
            "24-hour volume $3.41 billion.", s);
    }

    [Theory]
    [InlineData(0.0001, 8, "Funding 0.01 percent per 8 hours, longs pay shorts, paid every 8 hours, about 11 percent a year.")]
    [InlineData(0.0000125, 1, "Funding 0.01 percent per 8 hours, longs pay shorts, paid every hour, about 11 percent a year.")]
    [InlineData(-0.0002, 4, "Funding 0.04 percent per 8 hours, shorts pay longs, paid every 4 hours, about 44 percent a year.")]
    [InlineData(0.0, 1, "Funding zero, paid every hour.")]
    public void Funding_is_brought_to_8_hours_whatever_the_period(double rate, double hours, string expected)
    {
        string s = FundingNowService.Sentence(RecordedBtc with { FundingRate = rate, FundingIntervalHours = hours }, "V");

        Assert.Contains(expected, s);
    }

    [Theory]
    [InlineData("BTC-PERP", "BTC")]
    [InlineData("xyz:TSLA-PERP", "TSLA")]
    [InlineData("ETH/USDT", "ETH")]
    [InlineData("HYPE", "HYPE")]
    public void Open_interest_is_counted_in_the_coin_the_market_trades(string symbol, string unit)
        => Assert.Equal(unit, FundingNowService.BaseOf(symbol));

    [Fact]
    public void A_market_with_no_index_and_no_volume_leaves_them_out()
    {
        string s = FundingNowService.Sentence(RecordedBtc with { IndexPrice = 0, DayVolumeQuote = 0 }, "V");

        Assert.EndsWith("Mark 60427.00.", s);
        Assert.DoesNotContain("index", s);
        Assert.DoesNotContain("volume", s);
    }

    private static (FundingNowService Service, IDataService Data) Rig(IMarketDataProvider? provider)
    {
        var data = Substitute.For<IDataService>();
        data.GetProviderAsync("Hyperliquid").Returns(provider);
        return (new FundingNowService(data, new SpyEventBus()), data);
    }

    private static IMarketDataProvider Venue(Func<PerpetualContext?>? reply = null, Exception? fails = null)
    {
        var p = Substitute.For<IMarketDataProvider, IPerpetualContextProvider>();
        var call = ((IPerpetualContextProvider)p).GetPerpetualContextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        if (fails != null) call.ThrowsAsync(fails);
        else call.Returns(_ => Task.FromResult(reply?.Invoke()));
        return p;
    }

    [Fact]
    public async Task The_chart_venue_is_asked_for_the_chart_symbol()
    {
        var venue = Venue(() => RecordedBtc);
        var (service, _) = Rig(venue);

        string s = await service.SentenceAsync(Btc);

        Assert.StartsWith("BTC-PERP on Hyperliquid, now. Funding", s);
        await ((IPerpetualContextProvider)venue).Received(1).GetPerpetualContextAsync("BTC-PERP", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_venue_that_offers_it_only_as_a_capability_is_found()
    {
        var context = Substitute.For<IPerpetualContextProvider>();
        context.GetPerpetualContextAsync("BTC-PERP", Arg.Any<CancellationToken>()).Returns(RecordedBtc);
        var venue = Substitute.For<IMarketDataProvider>();
        venue.GetCapability<IPerpetualContextProvider>().Returns(context);
        var (service, _) = Rig(venue);

        Assert.StartsWith("BTC-PERP on Hyperliquid, now. Funding", await service.SentenceAsync(Btc));
    }

    [Fact]
    public async Task A_venue_without_it_says_so_and_names_one_that_has_it()
    {
        var venue = Substitute.For<IMarketDataProvider>();
        venue.GetCapability<IPerpetualContextProvider>().Returns((IPerpetualContextProvider?)null);
        var (service, data) = Rig(null);
        data.GetProviderAsync("Kraken").Returns(venue);

        Assert.Equal("Kraken does not give the terminal its current funding and open interest. Hyperliquid does.",
            await service.SentenceAsync(Btc with { Provider = "Kraken" }));
    }

    [Fact]
    public async Task A_spot_pair_has_no_funding_and_a_failed_read_says_it_failed()
    {
        var (spot, _) = Rig(Venue(() => null));
        var (broken, _) = Rig(Venue(fails: new HttpRequestException("boom")));

        Assert.Equal("PURR/USDC is not a perpetual future on Hyperliquid, so it has no funding or open interest.",
            await spot.SentenceAsync(Btc with { Symbol = "PURR/USDC" }));
        Assert.Equal("Funding and open interest could not be read from Hyperliquid.", await broken.SentenceAsync(Btc));
    }

    [Fact]
    public async Task The_go_bar_command_speaks_it_on_the_interface_tier()
    {
        var bus = new SpyEventBus();
        var data = Substitute.For<IDataService>();
        var venue = Venue(() => RecordedBtc);
        data.GetProviderAsync("Hyperliquid").Returns(venue);
        var store = new MockWorkspaceStore();
        store.EmitState(store.State with { Identity = Btc });
        using var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus),
            fundingNow: new FundingNowService(data, bus));

        dispatcher.Dispatch(SystemCommand.SpeakFundingNow);

        AnnouncementEvent? said = null;
        for (int i = 0; i < 300 && said is null; i++)
        {
            said = bus.Log.OfType<AnnouncementEvent>().FirstOrDefault();
            if (said is null) await Task.Delay(10);
        }
        Assert.NotNull(said);
        Assert.StartsWith("BTC-PERP on Hyperliquid, now.", said!.Message);
        Assert.Equal(SpeechChannel.Interface, said.Channel);
        Assert.Contains(Core.Services.GoBar.GoBarRegistry.All,
            e => e.Command == SystemCommand.SpeakFundingNow && e.Codes.Contains("FUND"));
    }
}
