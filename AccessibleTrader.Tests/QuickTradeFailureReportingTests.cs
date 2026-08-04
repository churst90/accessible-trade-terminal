using System;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
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
        var svc = new QuickTradeService(store, bus, () => equity);
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
}
