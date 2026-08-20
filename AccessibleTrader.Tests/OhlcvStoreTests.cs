using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Persistence;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The on-disk OHLCV store (2026-08-20), which exists so the same history is not re-fetched
    /// from a provider every time a chart is opened or scrolled back.
    ///
    /// The table and its composite key already existed and nothing ever wrote to it — the only
    /// writer had no callers, the read path was restricted to 1m, and the database file on disk
    /// was 0 bytes with no tables. These tests pin the two properties that make the store safe to
    /// serve from: it never returns a partial answer, and it never stores a forming bar.
    /// </summary>
    public sealed class OhlcvStoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly OhlcvStore _store;

        private const string Market = "Crypto";
        private const string Provider = "MEXC";
        private const string Symbol = "BTC/USDT";
        private const string Hourly = "1h";
        private const long HourMs = 3_600_000L;

        public OhlcvStoreTests()
        {
            _dir = Directory.CreateTempSubdirectory("att-ohlcv-").FullName;
            _factory = new TempDbFactory(Path.Combine(_dir, "trader_local.db"));
            _store = new OhlcvStore(_factory, NullLogger<OhlcvStore>.Instance);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* temp dir */ }
        }

        // ── Round trip ───────────────────────────────────────────────────────

        [Fact]
        public async Task SavedBars_AreServedBackForAClosedWindow()
        {
            var bars = ClosedBars(count: 200, endingHoursAgo: 5);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, bars);

            long until = Ms(bars[^1].Date);
            var read = await _store.TryReadClosedWindowAsync(Market, Provider, Symbol, Hourly, until, 200);

            Assert.Equal(200, read.Count);
            Assert.Equal(bars[0].Date, read[0].Date);
            Assert.Equal(bars[^1].Date, read[^1].Date);
            Assert.Equal(bars[^1].Close, read[^1].Close);
        }

        [Fact]
        public async Task ItWorksAtAnyTimeframe_NotJust1m()
        {
            // The old read path hard-coded "1m", so a 1h or 1d chart could never be served.
            foreach (var tf in new[] { "5m", "1h", "4h", "1d" })
            {
                var bars = ClosedBars(count: 50, endingHoursAgo: 72, stepMs: TimeframeUtility.ToMilliseconds(tf));
                await _store.SaveAsync(Market, Provider, Symbol, tf, bars);

                var read = await _store.TryReadClosedWindowAsync(Market, Provider, Symbol, tf, Ms(bars[^1].Date), 50);
                Assert.Equal(50, read.Count);
            }
        }

        [Fact]
        public async Task SeriesAreKeyedSeparately()
        {
            var bars = ClosedBars(count: 30, endingHoursAgo: 10);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, bars);

            long until = Ms(bars[^1].Date);
            Assert.Empty(await _store.TryReadClosedWindowAsync(Market, "Kraken", Symbol, Hourly, until, 30));
            Assert.Empty(await _store.TryReadClosedWindowAsync(Market, Provider, "ETH/USDT", Hourly, until, 30));
            Assert.Empty(await _store.TryReadClosedWindowAsync(Market, Provider, Symbol, "4h", until, 30));
        }

        // ── Never a partial answer ───────────────────────────────────────────

        [Fact]
        public async Task NotEnoughHistory_ReturnsNothingRatherThanAShortBlock()
        {
            // A short block would be silently treated by the chart as "that is all the history
            // there is", and scrollback would stop dead at a point that isn't the real start.
            var bars = ClosedBars(count: 40, endingHoursAgo: 5);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, bars);

            var read = await _store.TryReadClosedWindowAsync(Market, Provider, Symbol, Hourly, Ms(bars[^1].Date), 200);

            Assert.Empty(read);
        }

        [Fact]
        public async Task StoredBlockNotAdjacentToTheRequestedEdge_IsRefused()
        {
            // Bars from some earlier session, far from the window being asked for. Serving them
            // would prepend a disjoint block and leave an invisible hole in the chart.
            var bars = ClosedBars(count: 200, endingHoursAgo: 500);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, bars);

            long farLater = Ms(bars[^1].Date) + 100 * HourMs;
            var read = await _store.TryReadClosedWindowAsync(Market, Provider, Symbol, Hourly, farLater, 200);

            Assert.Empty(read);
        }

        [Fact]
        public async Task EmptyStore_ReturnsNothing()
        {
            var read = await _store.TryReadClosedWindowAsync(Market, Provider, Symbol, Hourly,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - HourMs, 200);
            Assert.Empty(read);
        }

        // ── Never store a bar that can still change ──────────────────────────

        [Fact]
        public async Task TheFormingBar_IsNeverStored()
        {
            // The current period's bar is still moving — its close, high, low and volume all
            // change with the next tick. Storing it would freeze a wrong price into history.
            var closed = ClosedBars(count: 10, endingHoursAgo: 1);
            var forming = new Ohlcv(DateTime.UtcNow, 1, 2, 0.5, 1.5, 10);
            var withForming = closed.Concat(new[] { forming }).ToList();

            await _store.SaveAsync(Market, Provider, Symbol, Hourly, withForming);

            using var db = _factory.CreateDbContext();
            var stamps = db.OhlcvData.Select(e => e.Timestamp).ToList();
            Assert.Equal(10, stamps.Count);
            Assert.DoesNotContain(Ms(forming.Date), stamps);
        }

        [Fact]
        public async Task SavingTwice_DoesNotDuplicateOrThrow()
        {
            // Re-charting the same window is the normal case, and the composite primary key would
            // throw on a blind re-insert.
            var bars = ClosedBars(count: 100, endingHoursAgo: 8);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, bars);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, bars);

            using var db = _factory.CreateDbContext();
            Assert.Equal(100, db.OhlcvData.Count());
        }

        [Fact]
        public async Task OverlappingSaves_KeepTheUnionWithoutDuplicates()
        {
            // Two scrollback fetches that overlap by 50 bars — the normal case, since a window is
            // requested by bar count rather than by an exact boundary.
            var older = ClosedBars(count: 100, endingHoursAgo: 100);       // hours -199 … -100
            var newer = ClosedBars(count: 100, endingHoursAgo: 50);        // hours -149 …  -50
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, older);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, newer);

            using var db = _factory.CreateDbContext();
            var stamps = db.OhlcvData.Select(e => e.Timestamp).ToList();

            Assert.Equal(150, stamps.Count);                          // union, not the sum
            Assert.Equal(stamps.Count, stamps.Distinct().Count());    // and no row written twice
        }

        [Fact]
        public async Task EmptyInput_IsANoOp()
        {
            // Not even a database file: there is nothing to record, so nothing is touched.
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, new List<Ohlcv>());

            var read = await _store.TryReadClosedWindowAsync(Market, Provider, Symbol, Hourly,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - HourMs, 1);
            Assert.Empty(read);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>`count` consecutive bars, the last of which closed `endingHoursAgo` ago.</summary>
        private static List<Ohlcv> ClosedBars(int count, int endingHoursAgo, long? stepMs = null)
        {
            long step = stepMs ?? HourMs;
            long end = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - endingHoursAgo * HourMs;
            end -= end % step;   // align to the bar grid

            var bars = new List<Ohlcv>(count);
            for (int i = count - 1; i >= 0; i--)
            {
                var t = DateTimeOffset.FromUnixTimeMilliseconds(end - i * step).UtcDateTime;
                double p = 100 + i;
                bars.Add(new Ohlcv(t, p, p + 1, p - 1, p + 0.5, 1000 + i));
            }
            return bars;
        }

        private static long Ms(DateTime d) =>
            new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        private sealed class TempDbFactory : IDbContextFactory<AppDbContext>
        {
            private readonly string _path;
            public TempDbFactory(string path) => _path = path;

            public AppDbContext CreateDbContext()
            {
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={_path}")
                    .Options;
                return new AppDbContext(options);
            }
        }
    }
}
