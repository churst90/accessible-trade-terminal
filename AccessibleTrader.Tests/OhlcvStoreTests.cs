using AccessibleTrader.Core.Persistence;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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
            _dir = TestTemp.NewDir("att-ohlcv-");
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

        // ── Monthly bars: the forming filter has to walk the calendar ────────
        //
        // ToMilliseconds approximates "1M" as 30 days, so `periodStart + barMs`
        // called a 31-day month closed a day early. The store then wrote the
        // still-forming month, and the insert-only dedup below made it permanent.
        // Both halves are pinned here, and both are only observable on specific
        // calendar days — hence the injected clock.

        [Theory]
        [InlineData("2026-01-01", "1M", "2026-02-01")]   // 31 days, the case that broke
        [InlineData("2026-02-01", "1M", "2026-03-01")]   // 28 days
        [InlineData("2024-02-01", "1M", "2024-03-01")]   // 29 — leap year
        [InlineData("2026-04-01", "1M", "2026-05-01")]   // 30, where the approximation is exact
        [InlineData("2026-01-01", "3M", "2026-04-01")]   // multi-month walks whole months
        [InlineData("2026-01-01", "1d", "2026-01-02")]
        [InlineData("2026-01-05", "1w", "2026-01-12")]
        [InlineData("2026-01-01", "4h", "2026-01-01T04:00:00")]
        public void GetPeriodEnd_walks_the_calendar_for_months_and_the_clock_for_everything_else(
            string start, string timeframe, string expectedEnd)
        {
            var s = DateTime.SpecifyKind(DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
            var e = DateTime.SpecifyKind(DateTime.Parse(expectedEnd, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
            Assert.Equal(e, TimeframeUtility.GetPeriodEnd(s, timeframe));
        }

        [Fact]
        public async Task A_still_forming_31_day_month_is_not_stored_on_its_last_day()
        {
            // 31 January: the month has a day left to run, and its close, high, low
            // and volume are all still moving. `1 Jan + 30 days` says it is over.
            // Saved alongside a genuinely closed December, which is the realistic
            // shape — a provider returns a window whose newest bar is the forming
            // one — and which also gets the schema created so "no January row" is
            // a real absence rather than an empty database.
            var december = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);
            var january = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            _store.UtcNow = () => new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

            await _store.SaveAsync(Market, Provider, Symbol, "1M", new List<Ohlcv>
            {
                new(december, 80, 110, 70, 100, 4000),
                new(january, 100, 150, 90, 120, 5000),
            });

            using var db = _factory.CreateDbContext();
            var row = Assert.Single(db.OhlcvData);
            Assert.Equal(Ms(december), row.Timestamp);
        }

        [Fact]
        public async Task The_same_month_is_stored_once_it_has_actually_ended()
        {
            // Anti-vacuity for the test above: if SaveAsync were refusing monthly
            // bars for some unrelated reason, "nothing was stored" would pass for
            // the wrong reason. One day later, the month is closed and it lands.
            var january = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            _store.UtcNow = () => new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

            await _store.SaveAsync(Market, Provider, Symbol, "1M",
                new List<Ohlcv> { new(january, 100, 150, 90, 120, 5000) });

            using var db = _factory.CreateDbContext();
            var row = Assert.Single(db.OhlcvData);
            Assert.Equal(Ms(january), row.Timestamp);
            Assert.Equal(120, row.Close);
        }

        [Fact]
        public async Task A_closed_short_month_is_not_held_back_by_the_30_day_approximation()
        {
            // The other direction, and the reason the fix is a calendar walk rather
            // than "add a day": February closes on 1 March, but `1 Feb + 30 days`
            // is 3 March, so a genuinely finished month was refused for two days.
            var february = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
            _store.UtcNow = () => new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

            await _store.SaveAsync(Market, Provider, Symbol, "1M",
                new List<Ohlcv> { new(february, 100, 150, 90, 120, 5000) });

            using var db = _factory.CreateDbContext();
            Assert.Single(db.OhlcvData);
        }

        // ── A re-fetch corrects a bar it already holds ───────────────────────

        [Fact]
        public async Task A_refetch_with_different_values_corrects_the_stored_bar()
        {
            // The dedup used to skip any timestamp already present, which made the
            // FIRST value ever written permanent for that series. A venue that
            // consolidates trades late, or a bar stored while it was still forming,
            // could never be healed — the chart served the wrong candle forever.
            var bars = ClosedBars(count: 3, endingHoursAgo: 8);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, bars);

            var revised = new Ohlcv(bars[1].Date, 200, 250, 190, 220, 9999);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, new List<Ohlcv> { revised });

            using var db = _factory.CreateDbContext();
            Assert.Equal(3, db.OhlcvData.Count());   // corrected, not appended
            var row = db.OhlcvData.Single(e => e.Timestamp == Ms(revised.Date));
            Assert.Equal(200, row.Open);
            Assert.Equal(250, row.High);
            Assert.Equal(190, row.Low);
            Assert.Equal(220, row.Close);
            Assert.Equal(9999, row.Volume);
        }

        [Fact]
        public async Task A_correction_leaves_its_neighbours_alone()
        {
            // Guard on the blast radius: rewriting one row must not disturb the bars
            // either side of it, which is what a too-broad update would do.
            var bars = ClosedBars(count: 3, endingHoursAgo: 8);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, bars);

            await _store.SaveAsync(Market, Provider, Symbol, Hourly,
                new List<Ohlcv> { new(bars[1].Date, 200, 250, 190, 220, 9999) });

            using var db = _factory.CreateDbContext();
            foreach (int i in new[] { 0, 2 })
            {
                var row = db.OhlcvData.Single(e => e.Timestamp == Ms(bars[i].Date));
                Assert.Equal(bars[i].Open, row.Open);
                Assert.Equal(bars[i].Close, row.Close);
                Assert.Equal(bars[i].Volume, row.Volume);
            }
        }

        [Fact]
        public async Task A_still_forming_bar_cannot_overwrite_the_closed_one_it_shadows()
        {
            // The correction path must not become a back door around the forming
            // filter: a re-fetch whose newest bar is the CURRENT period is dropped
            // before the dedup runs, so it can never rewrite a closed row.
            var bars = ClosedBars(count: 3, endingHoursAgo: 8);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, bars);

            var forming = new Ohlcv(DateTime.UtcNow, 1, 2, 0.5, 1.5, 10);
            await _store.SaveAsync(Market, Provider, Symbol, Hourly, new List<Ohlcv> { forming });

            using var db = _factory.CreateDbContext();
            Assert.Equal(3, db.OhlcvData.Count());
            Assert.DoesNotContain(Ms(forming.Date), db.OhlcvData.Select(e => e.Timestamp).ToList());
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
