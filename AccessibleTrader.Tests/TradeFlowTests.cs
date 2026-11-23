using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Plugins.Bitstamp;
using AccessibleTrader.Plugins.Mexc;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Google.Protobuf;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>True buy/sell volume (2026-11-23).</b> Cody: <i>"true buy sell volume if only pertaining to
/// one exchange no. if it can be useful for any exchange, yes."</i> Counted from each venue's
/// public trades (<see cref="ITradeStreamProvider"/>) into a <see cref="TradeTape"/>, read per bar
/// by <see cref="TradeFlowProvider"/>. Checked live against MEXC on 2026-11-23: spot sides agreed
/// with MEXC's REST isBuyerMaker on 72 of 72 matched trades, and a minute's buy plus sell equalled
/// its candle's volume on spot (17.1151) and futures (2,329,360 contracts).
/// </summary>
public class TradeFlowTests
{
    private static readonly DateTime T0 = new(2026, 11, 23, 14, 0, 0, DateTimeKind.Utc);

    private sealed class Clock { public DateTime Now = T0; }

    private static TradeTape Tape(Clock c) => new("MEXC", () => c.Now);

    private static PublicTrade Buy(DateTime t, double q) => new(t, 100, q, OrderSide.Buy);
    private static PublicTrade Sell(DateTime t, double q) => new(t, 100, q, OrderSide.Sell);

    // ── MEXC decoding ───────────────────────────────────────────────────────────

    private static PushDataV3ApiWrapper SpotDeals(params (string Price, string Qty, int Type, long Ms)[] deals)
    {
        var body = new PublicAggreDealsV3Api { EventType = "spot@public.aggre.deals.v3.api.pb@100ms" };
        foreach (var d in deals)
            body.Deals.Add(new PublicAggreDealsV3ApiItem { Price = d.Price, Quantity = d.Qty, TradeType = d.Type, Time = d.Ms });
        return MexcProtobuf.TryParse(new PushDataV3ApiWrapper
        {
            Channel = "spot@public.aggre.deals.v3.api.pb@100ms@BTCUSDT", Symbol = "BTCUSDT", PublicAggreDeals = body,
        }.ToByteArray())!;
    }

    [Fact]
    public void MEXC_spot_tradeType_1_is_a_buyer_taking_and_2_a_seller()
    {
        var trades = MexcProtobuf.ReadDeals(SpotDeals(("62807.20", "0.0112", 1, 1_795_450_807_634), ("62807.19", "0.0127", 2, 1_795_450_807_635)));
        Assert.Equal(2, trades.Count);
        Assert.Equal(new PublicTrade(DateTimeOffset.FromUnixTimeMilliseconds(1_795_450_807_634).UtcDateTime, 62807.20, 0.0112, OrderSide.Buy), trades[0]);
        Assert.Equal(OrderSide.Sell, trades[1].TakerSide);
        Assert.Equal(DateTimeKind.Utc, trades[0].Time.Kind);
    }

    [Fact]
    public void MEXC_spot_drops_a_trade_with_no_side_or_no_size()
    {
        var trades = MexcProtobuf.ReadDeals(SpotDeals(("1", "1", 0, 1), ("1", "0", 1, 1), ("1", "1", 3, 1), ("1", "2", 2, 1)));
        Assert.Equal(2, Assert.Single(trades).Quantity);
    }

    [Fact]
    public void A_MEXC_kline_frame_has_no_trades()
    {
        var kline = MexcProtobuf.TryParse(new PushDataV3ApiWrapper
        {
            Channel = "spot@public.kline.v3.api.pb@BTCUSDT@Min1",
            PublicSpotKline = new PublicSpotKlineV3Api { Interval = "Min1", WindowStart = 1, OpeningPrice = "1", HighestPrice = "1", LowestPrice = "1", ClosingPrice = "1", Volume = "1" },
        }.ToByteArray())!;
        Assert.Empty(MexcProtobuf.ReadDeals(kline));
    }

    [Fact]
    public void MEXC_futures_T_1_is_a_buyer_and_2_a_seller_in_contracts()
    {
        var one = MexcProvider.ReadFuturesDeals("""{"channel":"push.deal","data":{"M":1,"O":1,"T":1,"p":62789.3,"t":1795450807634,"v":613},"symbol":"BTC_USDT"}""");
        Assert.Equal(new PublicTrade(DateTimeOffset.FromUnixTimeMilliseconds(1795450807634).UtcDateTime, 62789.3, 613, OrderSide.Buy), Assert.Single(one));

        var batch = MexcProvider.ReadFuturesDeals("""{"channel":"push.deal","data":[{"T":2,"p":1.5,"t":1000,"v":4},{"T":1,"p":1.5,"t":1001,"v":6}]}""");
        Assert.Equal(new[] { OrderSide.Sell, OrderSide.Buy }, batch.Select(t => t.TakerSide));
    }

    [Theory]
    [InlineData("""{"channel":"push.kline","data":{"T":1,"p":1,"t":1,"v":1}}""")]
    [InlineData("""{"channel":"push.deal","data":{"T":0,"p":1,"t":1,"v":1}}""")]
    [InlineData("""{"channel":"pong","data":1}""")]
    [InlineData("not json")]
    public void MEXC_futures_frames_that_are_not_trades_give_none(string frame)
        => Assert.Empty(MexcProvider.ReadFuturesDeals(frame));

    // ── Bitstamp ────────────────────────────────────────────────────────────────

    /// <summary>A frame as Bitstamp sent it on 2026-11-23.</summary>
    private const string BitstampBuy = """{"data":{"id":680317230,"timestamp":"1795451539","amount":0.00081782,"amount_str":"0.00081782","price":62543.79,"price_str":"62543.79","type":0,"microtimestamp":"1795451539288000","buy_order_id":2075101448728577,"sell_order_id":2075101441953799},"channel":"live_trades_btcusd","event":"trade"}""";

    [Fact]
    public void Bitstamp_type_0_is_a_buyer_taking_and_1_a_seller()
    {
        Assert.True(BitstampProvider.TryReadPublicTrade(BitstampBuy, "live_trades_btcusd", out var buy));
        Assert.Equal(new PublicTrade(DateTimeOffset.FromUnixTimeMilliseconds(1795451539288).UtcDateTime, 62543.79, 0.00081782, OrderSide.Buy), buy);
        Assert.Equal(DateTimeKind.Utc, buy.Time.Kind);
        Assert.True(BitstampProvider.TryReadPublicTrade(BitstampBuy.Replace("\"type\":0", "\"type\":1"), "live_trades_btcusd", out var sell));
        Assert.Equal(OrderSide.Sell, sell.TakerSide);
    }

    [Theory]
    [InlineData("live_trades_ethusd", "\"type\":0", "\"type\":0")]           // another book's trade
    [InlineData("live_trades_btcusd", "\"event\":\"trade\"", "\"event\":\"bts:subscription_succeeded\"")]
    [InlineData("live_trades_btcusd", "\"type\":0", "\"type\":2")]
    [InlineData("live_trades_btcusd", "\"amount\":0.00081782", "\"amount\":0")]
    [InlineData("live_trades_btcusd", "{", "[")]
    public void Bitstamp_frames_that_are_not_this_books_trades_give_none(string channel, string from, string to)
        => Assert.False(BitstampProvider.TryReadPublicTrade(BitstampBuy.Replace(from, to), channel, out _));

    [Fact]
    public void Bitstamp_falls_back_to_whole_seconds()
    {
        Assert.True(BitstampProvider.TryReadPublicTrade(BitstampBuy.Replace("\"microtimestamp\":\"1795451539288000\",", ""), "live_trades_btcusd", out var t));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795451539).UtcDateTime, t.Time);
    }

    // ── The tape ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_bar_the_stream_saw_from_its_first_second_is_split_by_side()
    {
        var c = new Clock { Now = T0.AddSeconds(-5) };
        var tape = Tape(c);
        tape.SetCounting(true);
        tape.Add(Buy(T0.AddSeconds(1), 2));
        tape.Add(Sell(T0.AddSeconds(30), 1.5));
        tape.Add(Buy(T0.AddSeconds(59), 0.5));
        tape.Add(Buy(T0.AddSeconds(60), 7));          // the next bar's first second
        c.Now = T0.AddMinutes(2);
        Assert.Equal((2.5, 1.5), tape.Sum(T0, T0.AddMinutes(1)));
        Assert.Equal((7.0, 0.0), tape.Sum(T0.AddMinutes(1), T0.AddMinutes(2)));
    }

    [Fact]
    public void A_bar_that_started_before_the_stream_is_not_counted()
    {
        var c = new Clock { Now = T0.AddSeconds(20) };
        var tape = Tape(c);
        tape.SetCounting(true);
        tape.Add(Buy(T0.AddSeconds(30), 1));
        c.Now = T0.AddMinutes(2);
        Assert.Null(tape.Sum(T0, T0.AddMinutes(1)));
        Assert.Equal((0.0, 0.0), tape.Sum(T0.AddMinutes(1), T0.AddMinutes(2)));
    }

    [Fact]
    public void The_forming_bar_is_counted_up_to_now()
    {
        var c = new Clock { Now = T0 };
        var tape = Tape(c);
        tape.SetCounting(true);
        tape.Add(Sell(T0.AddSeconds(10), 3));
        c.Now = T0.AddSeconds(15);
        Assert.Equal((0.0, 3.0), tape.Sum(T0, T0.AddMinutes(1)));
    }

    [Fact]
    public void A_drop_inside_a_bar_leaves_it_uncounted_and_counting_resumes_after()
    {
        var c = new Clock { Now = T0 };
        var tape = Tape(c);
        tape.SetCounting(true);
        c.Now = T0.AddSeconds(90);
        tape.SetCounting(false);                       // dropped in the second minute
        c.Now = T0.AddSeconds(100);
        tape.SetCounting(true);
        c.Now = T0.AddMinutes(4);
        Assert.NotNull(tape.Sum(T0, T0.AddMinutes(1)));
        Assert.Null(tape.Sum(T0.AddMinutes(1), T0.AddMinutes(2)));
        Assert.NotNull(tape.Sum(T0.AddMinutes(2), T0.AddMinutes(3)));
        Assert.Equal(TradeTape.TapeState.Counting, tape.State);
    }

    [Fact]
    public void A_trade_arriving_late_lands_in_its_own_second()
    {
        var c = new Clock { Now = T0 };
        var tape = Tape(c);
        tape.SetCounting(true);
        tape.Add(Buy(T0.AddSeconds(70), 1));
        tape.Add(Buy(T0.AddSeconds(50), 4));           // out of order, a batch
        c.Now = T0.AddMinutes(2);
        Assert.Equal((4.0, 0.0), tape.Sum(T0, T0.AddMinutes(1)));
        Assert.Equal((1.0, 0.0), tape.Sum(T0.AddMinutes(1), T0.AddMinutes(2)));
    }

    [Fact]
    public void A_bar_older_than_the_tape_keeps_is_not_known()
    {
        var c = new Clock { Now = T0 };
        var tape = Tape(c);
        tape.SetCounting(true);
        c.Now = T0 + TradeTape.Retention + TimeSpan.FromHours(1);
        Assert.Null(tape.Sum(T0, T0.AddHours(1)));
    }

    // ── The indicator ───────────────────────────────────────────────────────────

    private static List<Ohlcv> Minutes(int n) => Enumerable.Range(0, n)
        .Select(i => new Ohlcv(T0.AddMinutes(i), 100, 101, 99, 100, 10)).ToList();

    private static Dictionary<string, object> Hints() => new()
    {
        ["__provider"] = "MEXC", ["__symbol"] = "BTCUSDT", ["__market"] = "Crypto", ["__timeframe"] = "1m",
    };

    private static (TradeFlowProvider Provider, ITradeFlowService Flow) With(TradeTape tape)
    {
        var flow = Substitute.For<ITradeFlowService>();
        flow.Watch(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(tape);
        return (new TradeFlowProvider(flow), flow);
    }

    private static Dictionary<string, double[]> Run(TradeFlowProvider p, string code, List<Ohlcv> bars, Dictionary<string, object> parameters)
    {
        var results = new Dictionary<string, double[]>();
        p.Calculate(code, bars.ToArray(), parameters, new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    /// <summary>The stream came up 20 seconds into bar 1 of 4, so bars 0 and 1 are blank, bar 2
    /// is complete, and bar 3 is forming.</summary>
    private static TradeTape FourMinutes(Clock c)
    {
        c.Now = T0.AddSeconds(80);
        var tape = Tape(c);
        tape.SetCounting(true);
        tape.Add(Buy(T0.AddSeconds(90), 5));           // bar 1: before counting covered it
        tape.Add(Buy(T0.AddSeconds(130), 3));
        tape.Add(Sell(T0.AddSeconds(150), 1));
        tape.Add(Sell(T0.AddSeconds(185), 4));
        c.Now = T0.AddSeconds(200);
        return tape;
    }

    [Fact]
    public void Buy_volume_is_drawn_up_and_sell_volume_down_and_the_bars_before_counting_are_blank()
    {
        var c = new Clock();
        var (p, flow) = With(FourMinutes(c));
        var r = Run(p, TradeFlowProvider.VolumeCode, Minutes(4), Hints());
        Assert.Equal(new[] { double.NaN, double.NaN, 3, 0 }, r[TradeFlowProvider.CompBuy]);
        Assert.Equal(new[] { double.NaN, double.NaN, -1, -4 }, r[TradeFlowProvider.CompSell]);
        flow.Received().Watch("Crypto", "MEXC", "BTCUSDT");
    }

    [Fact]
    public void The_delta_adds_up_from_the_first_counted_bar()
    {
        var c = new Clock();
        var (p, _) = With(FourMinutes(c));
        var r = Run(p, TradeFlowProvider.DeltaCode, Minutes(4), Hints());
        Assert.Equal(new[] { double.NaN, double.NaN, 2, -2 }, r[TradeFlowProvider.CompDelta]);
    }

    [Fact]
    public void A_bars_split_does_not_change_when_later_bars_arrive()
    {
        var c = new Clock();
        var (p, _) = With(FourMinutes(c));
        foreach (var (code, comps) in new[] { (TradeFlowProvider.VolumeCode, new[] { TradeFlowProvider.CompBuy, TradeFlowProvider.CompSell }), (TradeFlowProvider.DeltaCode, new[] { TradeFlowProvider.CompDelta }) })
        {
            var all = Run(p, code, Minutes(4), Hints());
            var three = Run(p, code, Minutes(3), Hints());
            foreach (var comp in comps) Assert.Equal(all[comp].Take(3), three[comp]);
        }
    }

    [Fact]
    public void The_delta_starts_again_from_zero_after_a_bar_that_was_not_counted()
    {
        var delta = TradeFlowProvider.Accumulate(
            new[] { 5.0, 1, double.NaN, 2, 2 },
            new[] { 1.0, 3, double.NaN, 1, 0 });
        Assert.Equal(new[] { 4, 2, double.NaN, 1, 3 }, delta);
    }

    [Fact]
    public void A_chart_with_no_venue_draws_nothing_and_opens_no_stream()
    {
        var c = new Clock();
        var (p, flow) = With(FourMinutes(c));
        var r = Run(p, TradeFlowProvider.VolumeCode, Minutes(4), new Dictionary<string, object>());
        Assert.All(r[TradeFlowProvider.CompBuy], v => Assert.True(double.IsNaN(v)));
        flow.DidNotReceiveWithAnyArgs().Watch(default!, default!, default!);
    }

    [Fact]
    public void A_venue_with_no_trade_sides_is_named_in_the_detail()
    {
        var c = new Clock();
        var tape = Tape(c);
        tape.MarkUnsupported();
        var (p, _) = With(tape);
        var bars = Minutes(4);
        var r = Run(p, TradeFlowProvider.VolumeCode, bars, Hints());
        Assert.All(r[TradeFlowProvider.CompBuy], v => Assert.True(double.IsNaN(v)));
        string fact = p.GetDetailFact(TradeFlowProvider.VolumeCode, bars.ToArray(), r, 3, new());
        Assert.Contains("MEXC does not send which side took each trade", fact);
    }

    [Fact]
    public void The_detail_says_the_split_and_who_took_more()
    {
        var c = new Clock();
        var (p, _) = With(FourMinutes(c));
        var bars = Minutes(4);
        var r = Run(p, TradeFlowProvider.VolumeCode, bars, Hints());
        Assert.Equal("Buy 3.00, sell 1.00: buyers took 75 percent.", p.GetDetailFact(TradeFlowProvider.VolumeCode, bars.ToArray(), r, 2, new()));
        Assert.Equal("Buy 0, sell 4.00: sellers took 100 percent.", p.GetDetailFact(TradeFlowProvider.VolumeCode, bars.ToArray(), r, 3, new()));
        Assert.StartsWith("Buy and sell volume: not counted for this bar.", p.GetDetailFact(TradeFlowProvider.VolumeCode, bars.ToArray(), r, 0, new()));
    }

    [Fact]
    public void Arrowing_onto_either_bar_says_both_sides()
    {
        var c = new Clock();
        var (p, _) = With(FourMinutes(c));
        var bars = Minutes(4);
        var r = Run(p, TradeFlowProvider.VolumeCode, bars, Hints());
        string fromBuy = p.GetComponentSpeech(TradeFlowProvider.CompBuy, r[TradeFlowProvider.CompBuy][2], bars[2], r, 2)!;
        string fromSell = p.GetComponentSpeech(TradeFlowProvider.CompSell, r[TradeFlowProvider.CompSell][2], bars[2], r, 2)!;
        Assert.Equal("Buy 3.00, sell 1.00: buyers took 75 percent.", fromBuy);
        Assert.Equal(fromBuy, fromSell);
    }

    [Fact]
    public void The_delta_says_who_is_ahead()
    {
        var p = new TradeFlowProvider(Substitute.For<ITradeFlowService>());
        Assert.Equal("Trade delta minus 2.00, sellers ahead.", p.GetComponentSpeech(TradeFlowProvider.CompDelta, -2, default, new Dictionary<string, double[]>(), 0));
        Assert.Single(p.GetDefaultLevels(TradeFlowProvider.DeltaCode));
        Assert.Empty(p.GetDefaultLevels(TradeFlowProvider.VolumeCode));
    }

    [Fact]
    public void Neither_is_offered_to_the_strategy_builder_because_there_is_no_history()
    {
        var catalog = new AccessibleTrader.Core.Services.Strategies.SignalCatalog(new IIndicatorProvider[] { new TradeFlowProvider(Substitute.For<ITradeFlowService>()) });
        Assert.Empty(catalog.All);
        Assert.Equal(3, catalog.Excluded.Count);
        Assert.All(catalog.Excluded, d => Assert.Contains("no history to test a strategy on", catalog.RefusalReason(d.Id)));
    }

    // ── The service ─────────────────────────────────────────────────────────────

    public interface ITradingVenue : IMarketDataProvider, ITradeStreamProvider { }

    private static (TradeFlowService Service, ITradingVenue Venue, Clock Clock, IAsyncDisposable Sub) Service()
    {
        var venue = Substitute.For<ITradingVenue>();
        var sub = Substitute.For<IAsyncDisposable>();
        venue.SubscribeTradesAsync(default!, default!, default!, default!).ReturnsForAnyArgs(Task.FromResult(sub));
        var data = Substitute.For<IDataService>();
        data.GetProviderAsync("MEXC").Returns(Task.FromResult<IMarketDataProvider?>(venue));
        var c = new Clock();
        return (new TradeFlowService(data, null, null, () => c.Now), venue, c, sub);
    }

    private static async Task Settle(Func<bool> done)
    {
        for (int i = 0; i < 200 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    [Fact]
    public async Task One_stream_per_symbol_however_often_it_is_asked()
    {
        var (svc, venue, _, _) = Service();
        await using var _s = svc;
        var tape = svc.Watch("Crypto", "MEXC", "BTCUSDT");
        for (int i = 0; i < 5; i++) Assert.Same(tape, svc.Watch("Crypto", "MEXC", "BTCUSDT"));
        await Settle(() => venue.ReceivedCalls().Any());
        await Task.Delay(50);
        await venue.Received(1).SubscribeTradesAsync("Crypto", "BTCUSDT", Arg.Any<Action<PublicTrade>>(), Arg.Any<Action<bool>>());
        Assert.NotSame(tape, svc.Watch("Futures", "MEXC", "BTCUSDT"));
    }

    [Fact]
    public async Task The_venues_trades_reach_the_tape()
    {
        var (svc, venue, c, _) = Service();
        await using var _s = svc;
        Action<PublicTrade>? onTrade = null;
        Action<bool>? onCounting = null;
        venue.SubscribeTradesAsync(default!, default!, default!, default!).ReturnsForAnyArgs(ci =>
        {
            onTrade = ci.ArgAt<Action<PublicTrade>>(2);
            onCounting = ci.ArgAt<Action<bool>>(3);
            return Task.FromResult(Substitute.For<IAsyncDisposable>());
        });
        var tape = svc.Watch("Crypto", "MEXC", "BTCUSDT");
        await Settle(() => onTrade != null);
        onCounting!(true);
        onTrade!(Buy(T0.AddSeconds(5), 2));
        c.Now = T0.AddSeconds(10);
        Assert.Equal((2.0, 0.0), tape.Sum(T0, T0.AddMinutes(1)));
        Assert.Equal(TradeTape.TapeState.Counting, tape.State);
    }

    [Fact]
    public async Task A_venue_that_streams_no_trades_is_unsupported()
    {
        var plain = Substitute.For<IMarketDataProvider>();
        plain.GetCapability<ITradeStreamProvider>().Returns((ITradeStreamProvider?)null);   // a substitute would invent one
        var data = Substitute.For<IDataService>();
        data.GetProviderAsync("Alpaca").Returns(Task.FromResult<IMarketDataProvider?>(plain));
        await using var svc = new TradeFlowService(data, null, null, () => T0);
        var tape = svc.Watch("Stocks", "Alpaca", "AAPL");
        await Settle(() => tape.State == TradeTape.TapeState.Unsupported);
    }

    [Fact]
    public async Task A_venue_not_loaded_yet_is_asked_again_rather_than_written_off()
    {
        var (svc, venue, _, _) = Service();
        await using var _s = svc;
        var data = Substitute.For<IDataService>();
        int asks = 0;
        data.GetProviderAsync("MEXC").Returns(_ => Task.FromResult<IMarketDataProvider?>(++asks == 1 ? null : venue));
        await using var svc2 = new TradeFlowService(data, null, null, () => T0);
        var first = svc2.Watch("Crypto", "MEXC", "BTCUSDT");
        await Settle(() => asks == 1);
        await Task.Delay(30);
        Assert.Equal(TradeTape.TapeState.Connecting, first.State);
        svc2.Watch("Crypto", "MEXC", "BTCUSDT");
        await Settle(() => venue.ReceivedCalls().Any());
    }

    [Fact]
    public async Task A_failed_stream_waits_before_it_is_tried_again()
    {
        var (svc, venue, c, _) = Service();
        await using var _s = svc;
        venue.SubscribeTradesAsync(default!, default!, default!, default!).ReturnsForAnyArgs<Task<IAsyncDisposable>>(_ => throw new IOException("down"));
        var tape = svc.Watch("Crypto", "MEXC", "BTCUSDT");
        await Settle(() => tape.State == TradeTape.TapeState.Failed);
        svc.Watch("Crypto", "MEXC", "BTCUSDT");
        await Task.Delay(50);
        Assert.Single(venue.ReceivedCalls());
        c.Now += TradeFlowService.RetryDelay;
        Assert.NotSame(tape, svc.Watch("Crypto", "MEXC", "BTCUSDT"));
        await Settle(() => venue.ReceivedCalls().Count() == 2);
    }

    [Fact]
    public async Task A_stream_nothing_has_read_for_a_while_is_closed()
    {
        var (svc, venue, c, sub) = Service();
        await using var _s = svc;
        var tape = svc.Watch("Crypto", "MEXC", "BTCUSDT");
        await Settle(() => venue.ReceivedCalls().Any());
        await Task.Delay(30);
        c.Now += TradeFlowService.IdleTimeout - TimeSpan.FromMinutes(1);
        await svc.SweepIdleAsync();
        await sub.DidNotReceive().DisposeAsync();
        c.Now += TimeSpan.FromMinutes(2);
        await svc.SweepIdleAsync();
        await sub.Received(1).DisposeAsync();
        Assert.NotSame(tape, svc.Watch("Crypto", "MEXC", "BTCUSDT"));
    }
}
