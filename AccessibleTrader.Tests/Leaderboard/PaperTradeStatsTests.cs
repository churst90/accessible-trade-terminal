using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Leaderboard;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests.Leaderboard;

/// <summary>
/// Part 1 of the leaderboard handoff: lifetime trade stats live on the paper account and
/// survive a restart. Old files have no <c>Stats</c>, so they are seeded from the kept
/// <c>History</c> — a seeded count only covers those kept fills.
/// </summary>
public sealed class PaperTradeStatsTests
{
    private const string Btc = "BTC/USDT";

    private static PaperTradingProvider Make(TempWorkspacePaths paths, out MockWorkspaceStore store)
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

    [Fact]
    public void The_starting_balance_has_one_named_home()
    {
        Assert.Equal(100_000.0, PaperTradingProvider.DefaultStartingBalance);

        var paths = new TempWorkspacePaths();
        var paper = Make(paths, out _);
        Assert.Equal(PaperTradingProvider.DefaultStartingBalance, paper.StartingBalance);
    }

    [Fact]
    public async Task Every_fill_sets_the_time_and_a_closing_fill_counts()
    {
        var paths = new TempWorkspacePaths();
        var paper = Make(paths, out var store);
        store.EmitState(ChartAt(100));

        Assert.Equal(0, paper.Stats.ClosedTrades);
        Assert.Null(paper.Stats.FirstFillAt);

        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
        Assert.Equal(0, paper.Stats.ClosedTrades);
        Assert.NotNull(paper.Stats.FirstFillAt);
        Assert.NotNull(paper.Stats.LastFillAt);

        store.EmitState(ChartAt(110));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

        Assert.Equal(1, paper.Stats.ClosedTrades);
        Assert.Equal(1, paper.Stats.Wins);
        Assert.Equal(10.0, paper.Stats.RealizedTotal, 6);
        Assert.True(paper.Stats.FirstFillAt <= paper.Stats.LastFillAt);
    }

    [Fact]
    public async Task A_break_even_close_counts_as_a_trade_but_not_a_win()
    {
        var paths = new TempWorkspacePaths();
        var paper = Make(paths, out var store);
        store.EmitState(ChartAt(100));

        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

        Assert.Equal(1, paper.Stats.ClosedTrades);
        Assert.Equal(0, paper.Stats.Wins);
        Assert.Equal(0.0, paper.Stats.RealizedTotal);
    }

    [Fact]
    public async Task A_losing_close_counts_and_subtracts()
    {
        var paths = new TempWorkspacePaths();
        var paper = Make(paths, out var store);
        store.EmitState(ChartAt(100));

        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
        store.EmitState(ChartAt(90));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

        Assert.Equal(1, paper.Stats.ClosedTrades);
        Assert.Equal(0, paper.Stats.Wins);
        Assert.Equal(-10.0, paper.Stats.RealizedTotal, 6);
    }

    [Fact]
    public async Task Stats_survive_a_restart()
    {
        var paths = new TempWorkspacePaths();
        var first = Make(paths, out var store);
        store.EmitState(ChartAt(100));
        await first.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
        store.EmitState(ChartAt(110));
        await first.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

        var second = Make(paths, out _);
        Assert.Equal(1, second.Stats.ClosedTrades);
        Assert.Equal(1, second.Stats.Wins);
        Assert.Equal(10.0, second.Stats.RealizedTotal, 6);
        Assert.Equal(first.Stats.FirstFillAt, second.Stats.FirstFillAt);
        Assert.Equal(first.Stats.LastFillAt, second.Stats.LastFillAt);
    }

    [Fact]
    public async Task Reset_zeroes_lifetime_stats()
    {
        var paths = new TempWorkspacePaths();
        var paper = Make(paths, out var store);
        store.EmitState(ChartAt(100));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
        store.EmitState(ChartAt(110));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

        paper.ResetAccount();

        Assert.Equal(0, paper.Stats.ClosedTrades);
        Assert.Equal(0, paper.Stats.Wins);
        Assert.Equal(0.0, paper.Stats.RealizedTotal);
        Assert.Null(paper.Stats.FirstFillAt);
        Assert.Null(paper.Stats.LastFillAt);

        var reloaded = Make(paths, out _);
        Assert.Equal(0, reloaded.Stats.ClosedTrades);
        Assert.Null(reloaded.Stats.FirstFillAt);
    }

    [Fact]
    public void An_old_file_is_seeded_from_history_oldest_first()
    {
        var paths = new TempWorkspacePaths();
        File.WriteAllText(StateFile(paths), """
            {
              "Cash": 100010.0,
              "Positions": [],
              "History": [
                {
                  "Id": "sell", "Symbol": "BTC/USDT", "Side": 1, "Quantity": 1.0,
                  "Price": 110.0, "FilledAt": "2026-01-02T00:00:00Z", "RealizedPnL": 10.0
                },
                {
                  "Id": "buy", "Symbol": "BTC/USDT", "Side": 0, "Quantity": 1.0,
                  "Price": 100.0, "FilledAt": "2026-01-01T00:00:00Z", "RealizedPnL": 0.0
                }
              ]
            }
            """);

        var paper = Make(paths, out _);
        var stats = paper.Stats;

        Assert.Equal(1, stats.ClosedTrades);
        Assert.Equal(1, stats.Wins);
        Assert.Equal(10.0, stats.RealizedTotal, 6);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), stats.FirstFillAt);
        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), stats.LastFillAt);
    }

    [Fact]
    public void A_seeded_replay_uses_the_position_not_the_realized_number()
    {
        // A break-even close is RealizedPnL 0 and STILL closed a position. Counting
        // RealizedPnL != 0 would report zero trades here.
        var history = new List<PaperFillFacts>(new[]
        {
            new PaperFillFacts(Btc, OrderSide.Sell, 1.0,
                new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 0.0),
            new PaperFillFacts(Btc, OrderSide.Buy, 1.0,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0.0),
        });

        var stats = PaperTradeStats.SeedFromHistory(history);

        Assert.Equal(1, stats.ClosedTrades);
        Assert.Equal(0, stats.Wins);
        Assert.Equal(0.0, stats.RealizedTotal);
    }

    [Fact]
    public void A_replay_that_flips_a_position_closes_only_the_reduced_part()
    {
        var history = new List<PaperFillFacts>(new[]
        {
            // Newest first. Oldest-first replay: long 1, then sell 3 closes the
            // long and opens a short of 2, then buy 2 closes the short.
            new PaperFillFacts(Btc, OrderSide.Buy, 2.0,
                new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), 7.0),
            new PaperFillFacts(Btc, OrderSide.Sell, 3.0,
                new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 5.0),
            new PaperFillFacts(Btc, OrderSide.Buy, 1.0,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0.0),
        });

        var stats = PaperTradeStats.SeedFromHistory(history);

        Assert.Equal(2, stats.ClosedTrades);
        Assert.Equal(2, stats.Wins);
        Assert.Equal(12.0, stats.RealizedTotal, 6);
    }

    [Fact]
    public void A_capped_history_that_starts_mid_position_counts_the_right_fills()
    {
        // The cap dropped the buy that opened the long the oldest kept fill closes. The
        // account holds a NEW long of 1 now. A replay forward from flat reads the sell as
        // opening a short and then counts the newest buy as closing that phantom (a
        // break-even "trade", no win). Walking back from what is held is exact.
        var history = new List<PaperFillFacts>(new[]
        {
            new PaperFillFacts(Btc, OrderSide.Buy, 1.0,
                new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), 0.0),
            new PaperFillFacts(Btc, OrderSide.Sell, 1.0,
                new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 5.0),
        });
        var held = new Dictionary<string, double> { [Btc] = 1.0 };

        var stats = PaperTradeStats.SeedFromHistory(history, held);

        Assert.Equal(1, stats.ClosedTrades);
        Assert.Equal(1, stats.Wins);
        Assert.Equal(5.0, stats.RealizedTotal, 6);
        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), stats.FirstFillAt);
        Assert.Equal(new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), stats.LastFillAt);
    }

    [Fact]
    public async Task A_seeded_file_keeps_accumulating_past_the_kept_200_fills()
    {
        var paths = new TempWorkspacePaths();
        File.WriteAllText(StateFile(paths), """
            {
              "Cash": 100010.0,
              "Positions": [],
              "History": [
                {
                  "Id": "sell", "Symbol": "BTC/USDT", "Side": 1, "Quantity": 1.0,
                  "Price": 110.0, "FilledAt": "2026-01-02T00:00:00Z", "RealizedPnL": 10.0
                },
                {
                  "Id": "buy", "Symbol": "BTC/USDT", "Side": 0, "Quantity": 1.0,
                  "Price": 100.0, "FilledAt": "2026-01-01T00:00:00Z", "RealizedPnL": 0.0
                }
              ]
            }
            """);

        var paper = Make(paths, out var store);
        Assert.Equal(1, paper.Stats.ClosedTrades);

        // One more round trip after the seed.
        store.EmitState(ChartAt(100));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
        store.EmitState(ChartAt(105));
        await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

        Assert.Equal(2, paper.Stats.ClosedTrades);
        Assert.Equal(2, paper.Stats.Wins);
        Assert.Equal(15.0, paper.Stats.RealizedTotal, 6);

        var saved = JObject.Parse(File.ReadAllText(StateFile(paths)));
        Assert.Equal(2, saved["Stats"]!["ClosedTrades"]!.Value<int>());
    }
}
