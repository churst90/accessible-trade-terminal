using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// The two ways a percentage can size a trade, and the fact that they are different.
///
/// <para>
/// <b>Where this came from.</b> A maintainer armed "0.5 percent" on a $100,000 account expecting a
/// $500 position, and got 0.714 BTC — a $45,700 one. Nothing was miscalculated. The terminal sized by
/// <i>risk at the stop</i> (quantity = risk ÷ stop distance, so the position loses exactly $500 if
/// stopped), while the expectation was <i>position value</i> (quantity = $500 ÷ price, the way an
/// exchange order ticket behaves).
/// </para>
///
/// <para>
/// Both are standard and neither is wrong. The defect was calling both of them "percent" and
/// supporting only one. Position value is now the default, because it is what an order ticket does
/// and what most people mean; risk-at-stop remains available and is what the "1% rule" of risk
/// management actually refers to.
/// </para>
/// </summary>
public class QuickTradeSizingModeTests
{
    private const double Equity = 100_000;
    private const double Entry = 64_000;

    private static QuickTradeState State(QuickTradeSizingMode mode, double stop, double pct = 0.5) =>
        new(QuickTradeStage.Ready, pct, Equity, stop, Entry, IsLong: true, SizingMode: mode);

    /// <summary>The maintainer's expectation: 0.5% of the account buys 0.5% of the account.</summary>
    [Fact]
    public void PositionValueModeBuysExactlyTheBudget()
    {
        var s = State(QuickTradeSizingMode.PositionValue, stop: 63_300);

        Assert.Equal(500, s.RiskCash, 6);
        Assert.Equal(500.0 / 64_000, s.PositionSize!.Value, 10);   // 0.0078125 BTC
        Assert.Equal(500, s.Notional!.Value, 6);
    }

    /// <summary>
    /// In position-value mode the stop does not change the size — it changes only what you lose.
    /// This is the property the maintainer expected and the terminal did not have.
    /// </summary>
    [Fact]
    public void InPositionValueModeTheStopDoesNotChangeTheSize()
    {
        var tight = State(QuickTradeSizingMode.PositionValue, stop: 63_800);
        var wide  = State(QuickTradeSizingMode.PositionValue, stop: 50_000);

        Assert.Equal(tight.PositionSize!.Value, wide.PositionSize!.Value, 12);
        Assert.True(wide.RiskAtStop!.Value > tight.RiskAtStop!.Value,
            "A wider stop must risk more, since the size is fixed.");
    }

    /// <summary>The number that surprised the maintainer, reproduced exactly.</summary>
    [Fact]
    public void RiskModeSizesFromTheStopAndCanFarExceedTheBudget()
    {
        var s = State(QuickTradeSizingMode.RiskAtStop, stop: 63_300);   // $700 away

        Assert.Equal(500, s.RiskCash, 6);
        Assert.Equal(500.0 / 700, s.PositionSize!.Value, 10);           // 0.714 BTC
        Assert.Equal(500.0 / 700 * 64_000, s.Notional!.Value, 4);       // ≈ $45,714
        Assert.True(s.Notional!.Value > 45_000);
    }

    /// <summary>In risk mode the loss at the stop is the budget, by construction.</summary>
    [Theory]
    [InlineData(63_800)]
    [InlineData(63_300)]
    [InlineData(50_000)]
    public void RiskModeLosesTheBudgetWhateverTheStop(double stop)
        => Assert.Equal(500, State(QuickTradeSizingMode.RiskAtStop, stop).RiskAtStop!.Value, 6);

    /// <summary>Tightening the stop doubles the position — the mechanism behind the surprise.</summary>
    [Fact]
    public void HalvingTheStopDistanceDoublesTheRiskModePosition()
    {
        var wide = State(QuickTradeSizingMode.RiskAtStop, stop: 63_000);   // 1000 away
        var half = State(QuickTradeSizingMode.RiskAtStop, stop: 63_500);   //  500 away

        Assert.Equal(wide.PositionSize!.Value * 2, half.PositionSize!.Value, 8);
    }

    /// <summary>Position-value mode needs no stop to know the size.</summary>
    [Fact]
    public void PositionValueModeHasASizeBeforeAStopExists()
    {
        var s = new QuickTradeState(QuickTradeStage.AwaitingStop, 0.5, Equity, null, Entry, true,
                                    QuickTradeSizingMode.PositionValue);
        Assert.NotNull(s.PositionSize);
        Assert.Null(s.RiskAtStop);   // unknowable until there is a stop
    }

    /// <summary>Risk mode cannot size without one, and must not invent a number.</summary>
    [Fact]
    public void RiskModeHasNoSizeWithoutAStop()
    {
        var s = new QuickTradeState(QuickTradeStage.AwaitingStop, 0.5, Equity, null, Entry, true,
                                    QuickTradeSizingMode.RiskAtStop);
        Assert.Null(s.PositionSize);
    }

    /// <summary>Position value is the default, since it is what an order ticket does.</summary>
    [Fact]
    public void TheDefaultIsPositionValue()
        => Assert.Equal(QuickTradeSizingMode.PositionValue,
                        new QuickTradeState(QuickTradeStage.Idle, 0, 0, null, null, true).SizingMode);

    // ── Units ────────────────────────────────────────────────────────────────

    /// <summary>
    /// "0.714 units" names a number and withholds the noun. On BTCUSDT the position is in BTC and
    /// the money is in USDT — two assets in one sentence, and nothing on screen to tell them apart.
    /// </summary>
    [Theory]
    [InlineData("BTCUSDT", "BTC", "USDT")]
    [InlineData("KASUSDT", "KAS", "USDT")]
    [InlineData("ETHBTC", "ETH", "BTC")]
    [InlineData("BTC/USD", "BTC", "USD")]
    [InlineData("AAPL-USD", "AAPL", "USD")]
    public void SymbolsSplitIntoTheAssetsTheyTradeBetween(string symbol, string expectedBase, string expectedQuote)
    {
        var p = SymbolAssets.Split(symbol);
        Assert.True(p.Recognised);
        Assert.Equal(expectedBase, p.Base);
        Assert.Equal(expectedQuote, p.Quote);
    }

    /// <summary>Longest match wins, or BTCUSDT would resolve to "BTCUSD" priced in "T".</summary>
    [Fact]
    public void TheLongestQuoteMatchWins()
        => Assert.Equal("USDT", SymbolAssets.Split("BTCUSDT").Quote);

    /// <summary>An unknown symbol gets neutral wording — a wrong unit is worse than a vague one.</summary>
    [Fact]
    public void AnUnrecognisedSymbolFallsBackToUnits()
    {
        Assert.False(SymbolAssets.Split("WEIRDTHING").Recognised);
        Assert.Equal("5 units", SymbolAssets.WithBaseUnit(5, "WEIRDTHING", "5"));
    }

    [Fact]
    public void ARecognisedSymbolNamesTheAsset()
        => Assert.Equal("0.714 BTC", SymbolAssets.WithBaseUnit(0.714, "BTCUSDT", "0.714"));

    [Fact]
    public void AQuoteAloneIsNotASymbol()
        => Assert.False(SymbolAssets.Split("USDT").Recognised);

    // ── What is spoken ───────────────────────────────────────────────────────

    private static (QuickTradeService Svc, SpyEventBus Bus) Build(QuickTradeSizingMode mode)
    {
        var bars = Enumerable.Range(0, 50).Select(i => new Ohlcv
        {
            Date = new DateTime(2024, 1, 1).AddDays(i),
            Open = Entry, High = Entry + 100, Low = i == 25 ? 63_300 : Entry - 100,
            Close = Entry, Volume = 1
        }).ToArray();

        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = 25,
            Identity = new ChartIdentity("Crypto", "MEXC", "BTCUSDT", "4h"),
        });

        var bus = new SpyEventBus();
        return (new QuickTradeService(store, bus, () => Equity, sizingMode: () => mode), bus);
    }

    private static string Spoken(SpyEventBus bus) =>
        string.Join(" ", bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message));

    /// <summary>The asset is named, so a quantity is never a bare number.</summary>
    [Fact]
    public void TheSpokenSizeNamesTheAsset()
    {
        var (svc, bus) = Build(QuickTradeSizingMode.PositionValue);
        svc.Arm(0.5);
        svc.SetStopAtCursor();

        Assert.Contains("BTC", Spoken(bus));
        Assert.DoesNotContain("units", Spoken(bus));
    }

    /// <summary>
    /// Both figures, always. Each mode controls one of them and lets the other float, and the one it
    /// does not control is precisely the one that surprises.
    /// </summary>
    [Theory]
    [InlineData(QuickTradeSizingMode.PositionValue)]
    [InlineData(QuickTradeSizingMode.RiskAtStop)]
    public void BothThePositionValueAndTheLossAtTheStopAreSpoken(QuickTradeSizingMode mode)
    {
        var (svc, bus) = Build(mode);
        svc.Arm(0.5);
        svc.SetStopAtCursor();

        string spoken = Spoken(bus);
        Assert.Contains("Position value", spoken, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lose", spoken, StringComparison.OrdinalIgnoreCase);
    }
}
