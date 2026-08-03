using System;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Quick trades from the chart.
///
/// <para>
/// This is the only feature in the terminal where a keystroke sizes and sends a real order without
/// a dialog in between, so the properties worth pinning are not about speech — they are about the
/// arithmetic being right and the state machine being impossible to walk into a bad place. Three
/// things could lose a user money here and each has a test below: sizing from the wrong number,
/// placing without a stop, and forgetting the system is armed.
/// </para>
/// </summary>
public class QuickTradeTests
{
    private const double Equity = 100_000;

    private static (QuickTradeService Svc, SpyEventBus Bus, MockWorkspaceStore Store) Build(
        double equity = Equity, double lastClose = 100)
    {
        var bars = Enumerable.Range(0, 50).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i),
            Open = 100, High = 105, Low = 95, Close = i == 49 ? lastClose : 100, Volume = 1
        }).ToArray();

        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = 25,
            Identity = new ChartIdentity("Crypto", "MEXC", "BTCUSDT", "4h"),
        });

        var bus = new SpyEventBus();
        return (new QuickTradeService(store, bus, () => equity), bus, store);
    }

    private static string Spoken(SpyEventBus bus) =>
        string.Join(" ", bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));

    // ── The arithmetic ──────────────────────────────────────────────────────────

    /// <summary>
    /// The calculation the whole feature exists to do, worked by hand.
    ///
    /// <para>
    /// $100,000 equity, 1% risk = $1,000 at stake. Entry 100, stop 95 → $5 of loss per unit →
    /// 200 units. That is the sum a sighted trader does in a position-size calculator, and getting
    /// it wrong by a factor is the difference between a planned loss and an account event.
    /// </para>
    /// </summary>
    [Fact]
    public void PositionSizeIsRiskCashDividedByStopDistance()
    {
        var state = new QuickTradeState(QuickTradeStage.Ready, RiskPercent: 1.0,
            AccountEquity: 100_000, StopPrice: 95, EntryPrice: 100, IsLong: true);

        Assert.Equal(1_000, state.RiskCash, 6);
        Assert.Equal(5, state.StopDistance!.Value, 6);
        Assert.Equal(200, state.PositionSize!.Value, 6);
    }

    /// <summary>
    /// A stop at the entry implies infinite size. Returning any number there — including a large
    /// one — would be the most dangerous rounding this application could perform, so it returns
    /// none and <see cref="QuickTradeState.CanPlace"/> is false.
    /// </summary>
    [Fact]
    public void AZeroStopDistanceHasNoSizeRatherThanAHugeOne()
    {
        var state = new QuickTradeState(QuickTradeStage.Ready, 1.0, 100_000,
            StopPrice: 100, EntryPrice: 100, IsLong: true);

        Assert.Null(state.PositionSize);
        Assert.False(state.CanPlace);
    }

    [Fact]
    public void SizeScalesWithRiskAndInverselyWithStopDistance()
    {
        var tight = new QuickTradeState(QuickTradeStage.Ready, 1.0, 100_000, 99, 100, true);
        var wide  = new QuickTradeState(QuickTradeStage.Ready, 1.0, 100_000, 90, 100, true);
        var twice = new QuickTradeState(QuickTradeStage.Ready, 2.0, 100_000, 99, 100, true);

        Assert.Equal(1_000, tight.PositionSize!.Value, 6);   // $1000 / $1
        Assert.Equal(100, wide.PositionSize!.Value, 6);      // $1000 / $10
        Assert.Equal(2_000, twice.PositionSize!.Value, 6);   // $2000 / $1
    }

    // ── The state machine ───────────────────────────────────────────────────────

    /// <summary>
    /// Arming a percentage is NOT enough to place. A risk percentage is a cash budget; it becomes a
    /// quantity only once the stop distance is known. Allowing an order here would mean sizing from
    /// equity alone, and a screen-reader user cannot glance at a ticket to catch the difference.
    /// </summary>
    [Fact]
    public void ArmingWithoutAStopCannotPlace()
    {
        var (svc, bus, _) = Build();

        svc.Arm(1.0);
        Assert.Equal(QuickTradeStage.AwaitingStop, svc.State.Stage);

        svc.Place(market: true);

        Assert.Empty(bus.Log.OfType<QuickTradeRequestedEvent>());
        Assert.Contains("No stop set yet", Spoken(bus));
    }

    [Fact]
    public void SettingAStopMakesItReadyAndComputesTheSize()
    {
        var (svc, _, _) = Build(lastClose: 100);

        svc.Arm(1.0);
        svc.SetStopAtCursor();          // cursor bar low is 95

        Assert.Equal(QuickTradeStage.Ready, svc.State.Stage);
        Assert.Equal(95, svc.State.StopPrice!.Value, 6);
        Assert.True(svc.State.CanPlace);
        Assert.Equal(200, svc.State.PositionSize!.Value, 6);
    }

    /// <summary>
    /// Direction is inferred from where the stop sits, never asked for. A stop below the price can
    /// only be protecting a long; above it, a short. Asking would be a question with exactly one
    /// correct answer.
    /// </summary>
    [Fact]
    public void DirectionIsInferredFromTheStopSide()
    {
        var (longSvc, _, _) = Build(lastClose: 100);   // bar low 95 is below → long
        longSvc.Arm(1.0);
        longSvc.SetStopAtCursor();
        Assert.True(longSvc.State.IsLong);

        var (shortSvc, _, _) = Build(lastClose: 90);   // bar high 105 is above → short
        shortSvc.Arm(1.0);
        shortSvc.SetStopAtCursor();
        Assert.False(shortSvc.State.IsLong);
    }

    [Fact]
    public void PlacingPublishesTheOrderAndDisarms()
    {
        var (svc, bus, _) = Build();

        svc.Arm(1.0);
        svc.SetStopAtCursor();
        svc.Place(market: true);

        var req = Assert.Single(bus.Log.OfType<QuickTradeRequestedEvent>());
        Assert.Equal("BTCUSDT", req.Symbol);
        Assert.True(req.IsLong);
        Assert.Equal(200, req.Quantity, 6);
        Assert.Equal(95, req.StopPrice, 6);
        Assert.Null(req.EntryPrice);                 // market
        Assert.Equal(1_000, req.RiskCash, 6);

        // Disarmed, so the next stray Enter cannot re-send.
        Assert.Equal(QuickTradeStage.Idle, svc.State.Stage);
    }

    [Fact]
    public void DisarmingClearsEverything()
    {
        var (svc, _, _) = Build();

        svc.Arm(1.0);
        svc.SetStopAtCursor();
        svc.Disarm();

        Assert.Equal(QuickTradeStage.Idle, svc.State.Stage);
        Assert.False(svc.State.CanPlace);
        Assert.Null(svc.State.StopPrice);
    }

    // ── The safety rails ────────────────────────────────────────────────────────

    /// <summary>
    /// These are hotkeys with no confirmation dialog, so a mis-typed risk must not be able to arm
    /// something account-ending.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(50)]
    public void AnAbsurdRiskPercentageIsRefused(double pct)
    {
        var (svc, bus, _) = Build();

        svc.Arm(pct);

        Assert.Equal(QuickTradeStage.Idle, svc.State.Stage);
        Assert.Contains("Risk must be between", Spoken(bus));
    }

    /// <summary>
    /// With no balance reported, sizing would have to invent an account value. Refusing is the only
    /// honest option — a position sized from a made-up balance is worse than no feature at all.
    /// </summary>
    [Fact]
    public void NoEquityMeansNoArming()
    {
        var (svc, bus, _) = Build(equity: 0);

        svc.Arm(1.0);

        Assert.Equal(QuickTradeStage.Idle, svc.State.Stage);
        Assert.Contains("No account equity", Spoken(bus));
    }

    /// <summary>
    /// The failure mode the feature is designed against: forgetting you are armed, pressing Enter
    /// for something else, and money moving. The reminder is unconditional while armed and silent
    /// when not.
    /// </summary>
    [Fact]
    public void TheArmedStateIsAnnouncedOnEveryBarUntilItIsResolved()
    {
        var (svc, _, _) = Build();

        Assert.Equal("", svc.ArmedSuffix());

        svc.Arm(1.0);
        Assert.Contains("stop needed", svc.ArmedSuffix());

        svc.SetStopAtCursor();
        Assert.Contains("ready", svc.ArmedSuffix());

        svc.Disarm(announce: false);
        Assert.Equal("", svc.ArmedSuffix());
    }

    // ── Equity source ───────────────────────────────────────────────────────────

    /// <summary>
    /// Balances arrive per asset and in their own units. Summing 0.5 BTC, 12 ETH and 3,000 USDT
    /// gives a number that is not money in any currency — and it would then be multiplied by a risk
    /// percentage to size a real order.
    /// </summary>
    [Fact]
    public void OnlyCashAssetsCountTowardEquity()
    {
        Assert.True(QuickTradeEquity.IsCashAsset("USD"));
        Assert.True(QuickTradeEquity.IsCashAsset("usdt"));
        Assert.True(QuickTradeEquity.IsCashAsset("EUR"));

        Assert.False(QuickTradeEquity.IsCashAsset("BTC"));
        Assert.False(QuickTradeEquity.IsCashAsset("ETH"));
        // Named like a dollar, is not one.
        Assert.False(QuickTradeEquity.IsCashAsset("USDe"));
        Assert.False(QuickTradeEquity.IsCashAsset(null));
    }

    /// <summary>A provider hiccup must not be able to turn into a position size.</summary>
    [Fact]
    public void NonFiniteOrNegativeEquityIsIgnored()
    {
        QuickTradeEquity.Reset();
        QuickTradeEquity.Report(50_000);

        QuickTradeEquity.Report(double.NaN);
        QuickTradeEquity.Report(double.PositiveInfinity);
        QuickTradeEquity.Report(-1);

        Assert.Equal(50_000, QuickTradeEquity.Latest, 6);
        QuickTradeEquity.Reset();
    }
}
