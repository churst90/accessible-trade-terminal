using AccessibleTrader.Plugins.Alpaca;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Alpaca bracket construction — where the atomicity guarantee actually comes from.
///
/// <para>
/// Alpaca accepts an entry and both protective legs as a <b>single order</b> with
/// <c>order_class</c> set, so the broker itself guarantees there is never a moment where the entry
/// exists and the stop does not. <b>Verified against a paper account on 2026-08-03:</b> one POST
/// returned a parent plus two legs already in <c>held</c> status, and the order was cancelled
/// immediately afterwards leaving zero positions.
/// </para>
///
/// <para>
/// That is a stronger guarantee than the application could build on a broker that attaches legs in
/// a second call — where a failure leaves a naked position, which is what
/// <c>GeneralOrderService.VerifyProtectiveOrdersAsync</c> exists to catch. So the thing worth
/// pinning offline is not "does the net fire" (tested in <c>OrderSafetyTests</c>) but "is the
/// single request built correctly", because every rule below is a rejection or a silent
/// mis-protection waiting to happen.
/// </para>
/// </summary>
public class AlpacaBracketTests
{
    private static TradeSignal Signal(OrderType type, double? stop = null, double? target = null, double? price = null)
        => new("SPY", OrderSide.Buy, Quantity: 1, Type: type,
               Price: price, StopLoss: stop, TakeProfit: target);

    private static JObject Build(TradeSignal s)
    {
        var body = new JObject { ["symbol"] = s.Symbol, ["qty"] = s.Quantity };
        AlpacaProvider.ApplyProtectiveLegs(body, s);
        return body;
    }

    /// <summary>Both legs → <c>bracket</c>, and this is the atomic case.</summary>
    [Fact]
    public void TwoLegsProduceABracket()
    {
        var body = Build(Signal(OrderType.Limit, stop: 95, target: 120, price: 100));

        Assert.Equal("bracket", (string?)body["order_class"]);
        Assert.Equal(95.0, (double?)body["stop_loss"]!["stop_price"]);
        Assert.Equal(120.0, (double?)body["take_profit"]!["limit_price"]);
    }

    /// <summary>
    /// One leg → <c>oto</c>. A one-leg <c>bracket</c> is rejected outright by Alpaca, so getting
    /// this backwards turns a valid protective order into a failed submission.
    /// </summary>
    [Theory]
    [InlineData(95.0, null)]
    [InlineData(null, 120.0)]
    public void OneLegProducesOto(double? stop, double? target)
    {
        var body = Build(Signal(OrderType.Market, stop: stop, target: target));

        Assert.Equal("oto", (string?)body["order_class"]);
    }

    /// <summary>
    /// A stop or stop-limit ENTRY takes no legs.
    ///
    /// <para>
    /// It is itself a protective order — Alpaca rejects a bracket parented by a stop — and more
    /// importantly <c>signal.StopLoss</c> means something different there: it is the entry trigger,
    /// not a child leg. Passing it through would fail AND would have meant the wrong thing if it
    /// had not.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(OrderType.StopMarket)]
    [InlineData(OrderType.StopLimit)]
    public void AStopEntryTakesNoProtectiveLegs(OrderType type)
    {
        var body = Build(Signal(type, stop: 95, target: 120, price: 100));

        Assert.Null(body["order_class"]);
        Assert.Null(body["stop_loss"]);
        Assert.Null(body["take_profit"]);
    }

    /// <summary>
    /// The field names are not interchangeable, and the failure is asymmetric: a stop posted with a
    /// limit price does not protect anything, and nothing would report an error.
    /// </summary>
    [Fact]
    public void StopLegsCarryAStopPriceAndTargetLegsCarryALimitPrice()
    {
        var body = Build(Signal(OrderType.Limit, stop: 95, target: 120, price: 100));

        Assert.NotNull(body["stop_loss"]!["stop_price"]);
        Assert.Null(body["stop_loss"]!["limit_price"]);

        Assert.NotNull(body["take_profit"]!["limit_price"]);
        Assert.Null(body["take_profit"]!["stop_price"]);
    }

    /// <summary>An unprotected order stays unprotected — no stray order_class on a plain entry.</summary>
    [Fact]
    public void APlainEntryGetsNoOrderClass()
    {
        var body = Build(Signal(OrderType.Market));

        Assert.Null(body["order_class"]);
        Assert.Null(body["stop_loss"]);
        Assert.Null(body["take_profit"]);
    }
}
