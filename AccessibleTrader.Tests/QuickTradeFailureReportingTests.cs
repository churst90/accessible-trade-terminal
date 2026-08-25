using System;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// An order that does not get placed must say so.
///
/// <para>
/// <b>The defect, from live paper trading.</b> "Market buy sent", then nothing — no fill, no
/// rejection, and no position in the dashboard. <c>PlaceOrderAsync</c> returns a status string and
/// <c>QuickTradeExecutor</c> <b>discarded it</b>. Every refusal came back through that return value
/// and was dropped: no live price, insufficient balance, quantity past the sanity ceiling, duplicate
/// suppression. The user had already been told the order was sent.
/// </para>
///
/// <para>
/// The executor's catch block carries a comment saying a silent failure here would be "the worst
/// possible combination — a confirmed order that never existed". It was exactly right, and it was
/// defeated by a return value nobody looked at, which no exception would ever reach.
/// </para>
/// </summary>
public class QuickTradeFailureReportingTests
{
    /// <summary>An order id means it went. Anything else is a refusal with a reason.</summary>
    [Fact]
    public void AnOrderIdIsNotTreatedAsAFailure()
    {
        Assert.Null(OrderResult.DescribeFailure("paper-9f2c1a4b7e03"));
        Assert.Null(OrderResult.DescribeFailure("12345678"));
    }

    /// <summary>
    /// The likeliest refusal for a risk-sized crypto position, and the one the maintainer hit. The
    /// message has to name the cause AND the remedy — "insufficient balance" alone does not tell you
    /// that a tighter stop is what made the position too big.
    /// </summary>
    [Fact]
    public void InsufficientBalanceExplainsWhyAndWhatToDo()
    {
        string? msg = OrderResult.DescribeFailure(
            "ORDER_FAILED:insufficient paper balance — that position needs 134,000.00 USDT and the account holds 100,000.00");

        Assert.NotNull(msg);
        Assert.Contains("more than the account holds", msg!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stop further away", msg!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoLivePriceIsReportedPlainly()
    {
        string? msg = OrderResult.DescribeFailure("ORDER_FAILED:no live price for symbol — load its chart first");
        Assert.NotNull(msg);
        Assert.Contains("no live price", msg!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ORDER_REJECTED_QUANTITY")]
    [InlineData("ORDER_REJECTED_PRICE")]
    [InlineData("ORDER_DUPLICATE_SUPPRESSED")]
    [InlineData("ORDER_FAILED")]
    public void EveryKnownFailureCodeProducesSomethingToSay(string code)
    {
        string? msg = OrderResult.DescribeFailure(code);
        Assert.False(string.IsNullOrWhiteSpace(msg), $"{code} would be silent.");
        Assert.Contains("Not placed", msg!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An unrecognised ORDER_FAILED reason must still be spoken verbatim rather than swallowed —
    /// a provider can invent one at any time and silence is never the right default here.
    /// </summary>
    [Fact]
    public void AnUnknownFailureReasonIsStillPassedOn()
    {
        string? msg = OrderResult.DescribeFailure("ORDER_FAILED:market is closed for maintenance");
        Assert.NotNull(msg);
        Assert.Contains("market is closed for maintenance", msg!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyAnswerIsAFailureNotASuccess(string? result)
        => Assert.False(string.IsNullOrWhiteSpace(OrderResult.DescribeFailure(result)));

    // ── The pre-flight caution ───────────────────────────────────────────────

    private static (QuickTradeService Svc, SpyEventBus Bus) Armed(double equity, double stopBarLow)
    {
        var bars = Enumerable.Range(0, 50).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i),
            Open = 100, High = 105, Low = i == 25 ? stopBarLow : 95, Close = 100, Volume = 1
        }).ToArray();

        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = 25,
            Identity = new ChartIdentity("Crypto", "MEXC", "KASUSDT", "1d"),
        });

        var bus = new SpyEventBus();
        // The overspend caution belongs to risk-at-stop sizing: only there can the position exceed
        // the account, because there the stop distance sets the size.
        var svc = new QuickTradeService(store, bus, () => equity,
            sizingMode: () => QuickTradeSizingMode.RiskAtStop);
        svc.Arm(1.0);
        return (svc, bus);
    }

    private static string Spoken(SpyEventBus bus) =>
        string.Join(" ", bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));

    /// <summary>
    /// Risk-based sizing means a tighter stop buys a bigger position. On a cash account that goes
    /// past the balance long before it looks unreasonable, so say it when the size is worked out —
    /// not after the broker refuses.
    /// </summary>
    [Fact]
    public void ATightStopThatOverspendsTheAccountIsFlaggedBeforePlacing()
    {
        // Entry 100, stop 99.9 → 0.1 distance. 1% of 100,000 = 1,000 risk → 10,000 units → 1,000,000
        // notional against a 100,000 account.
        var (svc, bus) = Armed(equity: 100_000, stopBarLow: 99.9);
        svc.SetStopAtCursor();

        string spoken = Spoken(bus);
        Assert.Contains("Caution", spoken, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("more than the", spoken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A position the account can afford must not be nagged about.</summary>
    [Fact]
    public void AnAffordablePositionGetsNoCaution()
    {
        // Entry 100, stop 50 → 50 distance. 1,000 risk → 20 units → 2,000 notional.
        var (svc, bus) = Armed(equity: 100_000, stopBarLow: 50);
        svc.SetStopAtCursor();

        Assert.DoesNotContain("Caution", Spoken(bus), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A caution, never a block. Margin and futures accounts routinely hold positions worth many
    /// times their cash, and refusing here would forbid ordinary leveraged trades on the strength of
    /// a spot-account assumption.
    /// </summary>
    [Fact]
    public void TheCautionDoesNotPreventPlacing()
    {
        var (svc, _) = Armed(equity: 100_000, stopBarLow: 99.9);
        svc.SetStopAtCursor();

        Assert.Equal(QuickTradeStage.Ready, svc.State.Stage);
        Assert.True(svc.State.CanPlace);
    }

    // ── The twin: the SAME dropped return value, in the automated path ───────

    /// <summary>
    /// <c>StrategyEngine.ExecuteSignalAsync</c> discarded <c>PlaceOrderAsync</c>'s return value
    /// exactly as <c>QuickTradeExecutor</c> once did — found by asking where else this class lives
    /// rather than by a new report. It is worse here, because nobody is at the keyboard: a strategy
    /// in Auto mode announces its signal on the event bus and then places the order, so a refusal
    /// left the user having heard "buy signal" and nothing after it. Believing you hold a position
    /// you do not hold is the most expensive wrong belief this application can produce.
    /// </summary>
    [Fact]
    public async Task AnAutoStrategyAnnouncesAnOrderItCouldNotPlace()
    {
        var h = new AutoStrategyHarness("ORDER_FAILED:insufficient paper balance");

        await h.FireSignalAsync();

        var spoken = await h.WaitForErrorAsync();
        Assert.NotNull(spoken);
        Assert.Contains("TestStrategy", spoken!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stop further away", spoken.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Vacuity check: a placed order must stay quiet. An engine that announced a failure
    /// for every fill would be noise, and would pass the test above for the wrong reason.</summary>
    [Fact]
    public async Task AnAutoStrategyThatPlacesSuccessfullySaysNothingExtra()
    {
        var h = new AutoStrategyHarness("paper-9f2c1a4b7e03");   // an order id — it went

        await h.FireSignalAsync();
        await Task.Delay(200);

        Assert.DoesNotContain(h.Bus.Log.OfType<FeedbackRequestEvent>(),
            e => e.Type == FeedbackType.Error);
    }

    /// <summary>
    /// A signal with no quantity is refused, out loud, and NOTHING is sent.
    ///
    /// <para>
    /// It used to default to <c>1.0</c> — one whole BTC, one whole ETH, one whole contract,
    /// chosen by nobody, from a strategy that simply did not set the field, in Auto mode with
    /// no one at the keyboard. 1.0 is far under <c>MaxOrderQuantity</c>, so the sanity clamp
    /// in <c>GeneralOrderService</c> waved it straight through. A strategy that has not stated
    /// a size has not stated an order.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public async Task AnAutoStrategySignalWithNoSizeIsRefusedAndNothingIsPlaced(double? quantity)
    {
        var h = new AutoStrategyHarness("paper-9f2c1a4b7e03", quantity);

        await h.FireSignalAsync();

        var spoken = await h.WaitForErrorAsync();
        Assert.NotNull(spoken);
        Assert.Contains("no position size", spoken!.Message, StringComparison.OrdinalIgnoreCase);
        await h.Orders.DidNotReceive().PlaceOrderAsync(
            Arg.Any<string>(), Arg.Any<AccessibleTrader.Sdk.Plugins.TradeSignal>());
    }

    /// <summary>
    /// Drives a real StrategyEngine in Auto mode through one bar close, with a scripted answer
    /// from the order service.
    /// </summary>
    private sealed class AutoStrategyHarness
    {
        public readonly SpyEventBus Bus = new();
        public AccessibleTrader.Core.Services.IOrderExecutionService Orders = null!;
        private readonly AccessibleTrader.Core.Services.Feeds.MarketFeedHub _hub;
        private readonly AccessibleTrader.Core.Services.Feeds.ChartFeed _feed;

        public AutoStrategyHarness(string orderResult) : this(orderResult, 0.25) { }

        /// <param name="quantity">
        /// The size the strategy states. Null is the "strategy did not set a size" case, which
        /// the engine refuses outright — these two tests are about what happens to an order that
        /// was actually attempted, so they state one.
        /// </param>
        public AutoStrategyHarness(string orderResult, double? quantity)
        {
            var identity = new ChartIdentity("Spot", "TestProv", "BTC/USD", "1h");
            _hub = new AccessibleTrader.Core.Services.Feeds.MarketFeedHub(
                NSubstitute.Substitute.For<AccessibleTrader.Core.Services.IDataOrchestrator>(),
                NSubstitute.Substitute.For<AccessibleTrader.Core.Services.IDataService>(),
                new AccessibleTrader.Core.Services.DemoPolicy(false),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            _feed = _hub.SetFocus(identity);

            var store = NSubstitute.Substitute.For<AccessibleTrader.Core.Services.IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial with { Identity = identity });

            Orders = NSubstitute.Substitute.For<AccessibleTrader.Core.Services.IOrderExecutionService>();
            Orders.PlaceOrderAsync(Arg.Any<string>(), Arg.Any<AccessibleTrader.Sdk.Plugins.TradeSignal>())
                  .Returns(Task.FromResult(orderResult));

            var strategy = NSubstitute.Substitute.For<AccessibleTrader.Sdk.Strategies.ITradingStrategy>();
            strategy.Name.Returns("TestStrategy");
            strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<System.Collections.Generic.IReadOnlyList<Ohlcv>>(),
                           Arg.Any<WorkspaceState>())
                    .Returns(new AccessibleTrader.Sdk.Strategies.StrategySignal(
                        AccessibleTrader.Sdk.Plugins.OrderSide.Buy,
                        AccessibleTrader.Sdk.Plugins.OrderType.Market,
                        quantity, null, null, null, "twin test", 0.9));

            var engine = new AccessibleTrader.Core.Services.StrategyEngine(
                Bus, Orders,
                NSubstitute.Substitute.For<AccessibleTrader.Sdk.Logging.IAppLogger>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AccessibleTrader.Core.Services.StrategyEngine>.Instance,
                NSubstitute.Substitute.For<AccessibleTrader.Core.Services.IDataManager>(), store,
                NSubstitute.Substitute.For<AccessibleTrader.Core.Services.IStrategyIndicatorCache>(),
                _hub);
            engine.AddStrategy(strategy, null, AccessibleTrader.Sdk.Strategies.StrategyExecutionMode.Auto);
        }

        /// <summary>One live tick into a new period, which closes the previous bar — the trigger
        /// StrategyEngine evaluates on.</summary>
        public Task FireSignalAsync()
        {
            var start = new DateTime(2026, 1, 1);
            _feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[]
            {
                new Ohlcv(start,                 100, 101, 99, 100, 1),
                new Ohlcv(start.AddHours(1),     100, 101, 99, 100, 1),
            }));
            _feed.ApplyLiveTick(new Ohlcv(start.AddHours(2), 100, 101, 99, 100, 1));
            return Task.CompletedTask;
        }

        public async Task<FeedbackRequestEvent?> WaitForErrorAsync(int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                var hit = Bus.Log.OfType<FeedbackRequestEvent>().FirstOrDefault(e => e.Type == FeedbackType.Error);
                if (hit != null) return hit;
                await Task.Delay(10);
            }
            return null;
        }
    }
}
