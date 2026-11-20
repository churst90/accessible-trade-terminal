using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Leaderboard;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests.Leaderboard;

/// <summary>
/// The pure leaderboard calculator. These tests were written BEFORE
/// <see cref="PaperAccountStanding"/> existed — red first, as the handoff requires.
///
/// <para>
/// The important half of this file drives a real <see cref="PaperTradingProvider"/>, persists
/// the account and parses the file back, because that is the only way to prove the calculator
/// reads the same money the broker reports. A hand-built fixture can agree with a formula and
/// still be a fixture for the wrong shape.
/// </para>
/// </summary>
public sealed class PaperAccountStandingTests
{
    private const string Btc = "BTC/USDT";

    private static PaperAccountFile Parse(string json)
    {
        Assert.True(PaperAccountFile.TryParse(json, out var file), "the fixture should parse");
        return file!;
    }

    private static PaperTradingProvider MakeAccount(
        TempWorkspacePaths paths, out MockWorkspaceStore store)
    {
        store = new MockWorkspaceStore();
        return new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance);
    }

    private static WorkspaceState ChartAt(double close) =>
        WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Test", Btc, "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(
                new Ohlcv(DateTime.UtcNow, close, close, close, close, 1000)),
        };

    private static string StateFile(TempWorkspacePaths paths) =>
        Path.Combine(paths.AppDataDirectory, "paper_account.json");

    /// <summary>The equity the broker itself reports: quote cash, locked collateral, and
    /// each position at its market value.</summary>
    private static async Task<double> ProviderEquityAsync(PaperTradingProvider paper)
    {
        var balances = await paper.GetBalancesAsync();
        var quote = Assert.Single(balances, b => b.Asset == "USDT");
        double equity = quote.Free + quote.Locked;
        foreach (var p in await paper.GetPositionsAsync()) equity += p.MarketValue;
        return equity;
    }

    // ── Valuation ────────────────────────────────────────────────────────────

    [Fact]
    public void A_long_marked_up_adds_cash_position_value_and_no_collateral()
    {
        var file = Parse("""
            {
              "Cash": 0.0,
              "Positions": [ { "Symbol": "BTC/USDT", "Qty": 1.0, "Avg": 90000.0 } ],
              "Collateral": []
            }
            """);

        var standing = PaperAccountStanding.Compute(file, _ => 110_000, 100_000);

        Assert.Equal(110_000, standing.Equity, 6);
        Assert.Equal(10.0, standing.ReturnPct, 6);
        Assert.False(standing.MarkToMarketStale);
    }

    [Fact]
    public void Each_held_position_is_listed_with_its_side_mark_and_profit_winning_positive_on_both_sides()
    {
        var file = Parse("""
            {
              "Cash": 0.0,
              "Positions": [
                { "Symbol": "ETH/USDT", "Qty": -2.0, "Avg": 3000.0 },
                { "Symbol": "BTC/USDT", "Qty": 0.5, "Avg": 100000.0 },
                { "Symbol": "SOL/USDT", "Qty": 0.0, "Avg": 150.0 }
              ],
              "Collateral": []
            }
            """);

        var standing = PaperAccountStanding.Compute(
            file, symbol => symbol == "BTC/USDT" ? 110_000 : 2_700, 100_000);

        Assert.Equal(2, standing.OpenPositions.Count);   // sorted by symbol, zero quantity left out

        var btc = standing.OpenPositions[0];
        Assert.Equal("BTC/USDT", btc.Symbol);
        Assert.False(btc.IsShort);
        Assert.Equal(0.5, btc.Quantity, 9);
        Assert.Equal(100_000, btc.Entry, 9);
        Assert.Equal(110_000, btc.Mark!.Value, 9);
        Assert.Equal(5_000, btc.UnrealizedPnL!.Value, 6);   // 0.5 x 10,000
        Assert.Equal(10.0, btc.UnrealizedPct!.Value, 6);

        var eth = standing.OpenPositions[1];
        Assert.True(eth.IsShort);
        Assert.Equal(2.0, eth.Quantity, 9);
        Assert.Equal(600, eth.UnrealizedPnL!.Value, 6);     // short 2 from 3,000 to 2,700
        Assert.Equal(10.0, eth.UnrealizedPct!.Value, 6);   // a winning short is positive
    }

    [Fact]
    public void A_held_position_without_a_price_has_unknown_profit_not_zero()
    {
        var file = Parse("""
            {
              "Cash": 50000.0,
              "Positions": [ { "Symbol": "BTC/USDT", "Qty": 1.0, "Avg": 50000.0 } ],
              "Collateral": []
            }
            """);

        var position = Assert.Single(PaperAccountStanding.Compute(file, _ => null, 100_000).OpenPositions);

        Assert.Null(position.Mark);
        Assert.Null(position.UnrealizedPnL);
        Assert.Null(position.UnrealizedPct);
        Assert.Equal(50_000, position.Entry, 9);
    }

    [Fact]
    public void A_long_at_a_missing_price_is_valued_at_entry_and_flagged_stale()
    {
        var file = Parse("""
            {
              "Cash": 50000.0,
              "Positions": [ { "Symbol": "BTC/USDT", "Qty": 1.0, "Avg": 50000.0 } ],
              "Collateral": []
            }
            """);

        var standing = PaperAccountStanding.Compute(file, _ => null, 100_000);

        Assert.Equal(100_000, standing.Equity, 6);   // at entry, never zero
        Assert.Equal(0.0, standing.ReturnPct, 6);
        Assert.True(standing.MarkToMarketStale);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void A_non_positive_price_is_missing_not_a_real_mark(double badPrice)
    {
        var file = Parse("""
            {
              "Cash": 50000.0,
              "Positions": [ { "Symbol": "BTC/USDT", "Qty": 1.0, "Avg": 50000.0 } ]
            }
            """);

        var standing = PaperAccountStanding.Compute(file, _ => badPrice, 100_000);

        Assert.Equal(100_000, standing.Equity, 6);
        Assert.True(standing.MarkToMarketStale);
    }

    [Fact]
    public void A_price_lookup_that_throws_falls_back_to_entry_instead_of_crashing()
    {
        var file = Parse("""
            {
              "Cash": 50000.0,
              "Positions": [ { "Symbol": "BTC/USDT", "Qty": 1.0, "Avg": 50000.0 } ]
            }
            """);

        var standing = PaperAccountStanding.Compute(
            file, _ => throw new InvalidOperationException("feed down"), 100_000);

        Assert.Equal(100_000, standing.Equity, 6);
        Assert.True(standing.MarkToMarketStale);
    }

    [Fact]
    public void An_untouched_account_returns_exactly_zero_percent()
    {
        var file = Parse("""{ "Cash": 100000.0 }""");

        var standing = PaperAccountStanding.Compute(file, _ => 123, 100_000);

        Assert.Equal(100_000, standing.Equity, 6);
        Assert.Equal(0.0, standing.ReturnPct);
        Assert.Equal(0, standing.TradeCount);
        Assert.Null(standing.WinRate);
        Assert.Null(standing.FirstTradeAt);
        Assert.Null(standing.LastTradeAt);
    }

    [Fact]
    public void Malformed_json_is_refused_and_never_throws()
    {
        Assert.False(PaperAccountFile.TryParse("{ not json", out var bad));
        Assert.Null(bad);
        Assert.False(PaperAccountFile.TryParse("[]", out _));
        Assert.False(PaperAccountFile.TryParse("null", out _));
        Assert.False(PaperAccountFile.TryParse("", out _));
    }

    // ── Against the real broker ──────────────────────────────────────────────

    [Theory]
    [InlineData(90.0)]   // short in profit
    [InlineData(110.0)]  // short in loss
    public async Task A_short_marked_at_the_live_price_matches_the_broker(double mark)
    {
        var paths = new TempWorkspacePaths();
        var paper = MakeAccount(paths, out var store);
        store.EmitState(ChartAt(100));

        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));
        store.EmitState(ChartAt(mark));

        double brokerEquity = await ProviderEquityAsync(paper);
        var file = Parse(File.ReadAllText(StateFile(paths)));
        var standing = PaperAccountStanding.Compute(file, _ => mark, paper.StartingBalance);

        Assert.Equal(brokerEquity, standing.Equity, 6);
        Assert.Equal(
            (brokerEquity - paper.StartingBalance) / paper.StartingBalance * 100.0,
            standing.ReturnPct, 6);
    }

    [Fact]
    public async Task A_break_even_close_counts_as_a_trade_but_not_a_win()
    {
        var paths = new TempWorkspacePaths();
        var paper = MakeAccount(paths, out var store);
        store.EmitState(ChartAt(100));

        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

        var file = Parse(File.ReadAllText(StateFile(paths)));
        var standing = PaperAccountStanding.Compute(file, _ => 100, paper.StartingBalance);

        Assert.Equal(1, standing.TradeCount);
        Assert.Equal(0, standing.WinsForTestOnly());   // see helper below
        Assert.Equal(0.0, standing.WinRate);
        Assert.Equal(0.0, standing.RealizedPnL, 6);
    }

    [Fact]
    public async Task A_file_with_no_stats_seeds_from_history_oldest_first()
    {
        var paths = new TempWorkspacePaths();
        var paper = MakeAccount(paths, out var store);
        store.EmitState(ChartAt(100));

        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
        store.EmitState(ChartAt(110));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

        var saved = JObject.Parse(File.ReadAllText(StateFile(paths)));
        saved.Remove("Stats");
        var file = Parse(saved.ToString());

        Assert.Null(file.Stats);   // the fixture really is an old-shaped file
        var standing = PaperAccountStanding.Compute(file, _ => 110, paper.StartingBalance);

        Assert.Equal(1, standing.TradeCount);
        Assert.Equal(1.0, standing.WinRate);
        Assert.Equal(10.0, standing.RealizedPnL, 6);
        Assert.NotNull(standing.FirstTradeAt);
        Assert.NotNull(standing.LastTradeAt);
        Assert.True(standing.FirstTradeAt <= standing.LastTradeAt);
    }

    /// <summary>
    /// The calculator and the provider must seed an old file with the SAME replay: one shared
    /// function, not two similar ones. Five fills cover open, partial close, full close,
    /// re-open and a break-even close.
    /// </summary>
    [Fact]
    public async Task The_provider_and_the_calculator_seed_old_history_identically()
    {
        var paths = new TempWorkspacePaths();
        var paper = MakeAccount(paths, out var store);
        store.EmitState(ChartAt(100));

        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 2.0));
        store.EmitState(ChartAt(110));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));  // partial, +10
        store.EmitState(ChartAt(90));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));  // full, -20
        store.EmitState(ChartAt(100));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));  // break-even

        var live = paper.Stats;
        var saved = JObject.Parse(File.ReadAllText(StateFile(paths)));
        saved.Remove("Stats");
        var standing = PaperAccountStanding.Compute(
            Parse(saved.ToString()), _ => 100, paper.StartingBalance);

        Assert.Equal(3, live.ClosedTrades);
        Assert.Equal(1, live.Wins);
        Assert.Equal(0.0, live.RealizedTotal, 6);   // +10, -10, break-even

        Assert.Equal(live.ClosedTrades, standing.TradeCount);
        Assert.Equal(live.Wins, (int)Math.Round(standing.WinRate!.Value * live.ClosedTrades));
        Assert.Equal(live.RealizedTotal, standing.RealizedPnL, 6);
        Assert.Equal(live.FirstFillAt, standing.FirstTradeAt);
        Assert.Equal(live.LastFillAt, standing.LastTradeAt);
    }

    /// <summary>
    /// An old file (no Stats) whose kept history starts mid-position: the cap dropped the buy
    /// that opened the long the oldest kept sell closed, and the account holds a NEW long now.
    /// Both the calculator and the broker's own load must walk back from what is held; a
    /// replay forward from flat counts the newest buy as closing a phantom short instead.
    /// </summary>
    [Fact]
    public void A_capped_old_history_is_seeded_from_the_held_position_by_both_readers()
    {
        const string json = """
            {
              "Cash": 99905.0,
              "Positions": [ { "Symbol": "BTC/USDT", "Qty": 1.0, "Avg": 100.0 } ],
              "History": [
                { "Id": "b", "Symbol": "BTC/USDT", "Side": 0, "Quantity": 1.0, "Price": 100.0,
                  "FilledAt": "2026-01-03T00:00:00Z", "RealizedPnL": 0.0 },
                { "Id": "s", "Symbol": "BTC/USDT", "Side": 1, "Quantity": 1.0, "Price": 105.0,
                  "FilledAt": "2026-01-02T00:00:00Z", "RealizedPnL": 5.0 }
              ]
            }
            """;

        var standing = PaperAccountStanding.Compute(Parse(json), _ => 100, 100_000);
        Assert.Equal(1, standing.TradeCount);
        Assert.Equal(1.0, standing.WinRate);
        Assert.Equal(5.0, standing.RealizedPnL, 6);

        var paths = new TempWorkspacePaths();
        File.WriteAllText(StateFile(paths), json);
        var paper = MakeAccount(paths, out _);
        Assert.Equal(1, paper.Stats.ClosedTrades);
        Assert.Equal(1, paper.Stats.Wins);
        Assert.Equal(5.0, paper.Stats.RealizedTotal, 6);
    }
}

internal static class PaperAccountStandingTestExtensions
{
    /// <summary>Wins are not on the public standing (the leaderboard only needs the rate);
    /// this keeps the assertion readable without widening the shipped record for a test.</summary>
    public static int WinsForTestOnly(this AccessibleTrader.Core.Services.Leaderboard.PaperAccountStanding standing)
        => (int)Math.Round((standing.WinRate ?? 0) * standing.TradeCount);
}
