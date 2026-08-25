using System.Reflection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tier 3 coverage for <see cref="HistoricalDataFetcher.ApplyFinalFilters"/>.
    /// Every fetch path funnels through this private helper, which enforces
    /// three invariants that pagination callers rely on:
    ///
    ///   1. <c>since</c> / <c>until</c> are INCLUSIVE (the boundary bar lands
    ///      in the result). Off-by-one here drops the start or end of a page.
    ///   2. Zero-price bars are stripped — some providers return a forming
    ///      all-zero candle when a new period has just started and no trades
    ///      executed yet. Letting one through would fire spurious signals on
    ///      load (e.g. Cipher A crossovers, SR break detection).
    ///   3. The returned list is at most <c>limit</c> long, keeping the TAIL
    ///      (<c>TakeLast</c>) rather than the head so the user sees the most
    ///      recent bars when the provider over-delivered.
    ///
    /// All three are tested via reflection into the private method — bypasses
    /// the policy / EF / HTTP scaffolding around <c>FetchOhlcvAsync</c>.
    /// </summary>
    public class PaginationBoundsTests
    {
        [Fact]
        public void ApplyFinalFilters_SinceInclusive_IncludesBoundaryBar()
        {
            // since = bars[1].Date → bars[1] must be in the result (inclusive).
            var bars = MakeBars(5, start: new DateTime(2026, 04, 23, 0, 0, 0, DateTimeKind.Utc));
            long sinceMs = UnixMs(bars[1].Date);

            var filtered = Invoke(bars, since: sinceMs, until: null, limit: 100);

            Assert.Equal(4, filtered.Count);  // bars[1..4]
            Assert.Equal(bars[1].Date, filtered[0].Date);
            Assert.Equal(bars[4].Date, filtered[^1].Date);
        }

        [Fact]
        public void ApplyFinalFilters_UntilInclusive_IncludesBoundaryBar()
        {
            // until = bars[3].Date → bars[3] must be in the result. A non-inclusive
            // predicate here would give 3 bars and silently drop the user's right edge.
            var bars = MakeBars(5, start: new DateTime(2026, 04, 23, 0, 0, 0, DateTimeKind.Utc));
            long untilMs = UnixMs(bars[3].Date);

            var filtered = Invoke(bars, since: null, until: untilMs, limit: 100);

            Assert.Equal(4, filtered.Count);  // bars[0..3]
            Assert.Equal(bars[0].Date, filtered[0].Date);
            Assert.Equal(bars[3].Date, filtered[^1].Date);
        }

        [Fact]
        public void ApplyFinalFilters_SinceAndUntil_BothInclusive()
        {
            // Window [bars[1], bars[3]] inclusive → 3 bars.
            var bars = MakeBars(5, start: new DateTime(2026, 04, 23, 0, 0, 0, DateTimeKind.Utc));
            long sinceMs = UnixMs(bars[1].Date);
            long untilMs = UnixMs(bars[3].Date);

            var filtered = Invoke(bars, since: sinceMs, until: untilMs, limit: 100);

            Assert.Equal(3, filtered.Count);
            Assert.Equal(bars[1].Date, filtered[0].Date);
            Assert.Equal(bars[3].Date, filtered[^1].Date);
        }

        [Fact]
        public void ApplyFinalFilters_ZeroPriceBars_AreDropped()
        {
            // A forming all-zero bar in the middle must be removed before the list
            // reaches indicators / scoring code.
            var bars = new List<Ohlcv>
            {
                new Ohlcv(D(0), 100, 100, 100, 100, 1),
                new Ohlcv(D(1),   0,   0,   0,   0, 0),  // zero-price forming bar
                new Ohlcv(D(2), 102, 102, 102, 102, 1),
            };
            var filtered = Invoke(bars, since: null, until: null, limit: 100);
            Assert.Equal(2, filtered.Count);
            Assert.DoesNotContain(filtered, b => b.Close == 0);
        }

        [Fact]
        public void ApplyFinalFilters_ZeroCloseAloneTriggersDrop()
        {
            // Partial zero: if ANY of OHLC is zero the bar is dropped. Protects against
            // providers that zero only the close field pre-tick.
            var bars = new List<Ohlcv>
            {
                new Ohlcv(D(0), 100, 110, 95,   0, 1),  // zero close — drop
                new Ohlcv(D(1), 100, 110, 95, 105, 1),
            };
            var filtered = Invoke(bars, since: null, until: null, limit: 100);
            Assert.Single(filtered);
            Assert.Equal(D(1), filtered[0].Date);
        }

        [Fact]
        public void ApplyFinalFilters_LimitCap_KeepsMostRecentBars()
        {
            // Provider over-delivered 10 bars but caller asked for 3 → keep the LAST
            // 3 (most recent) not the first 3. Pagination invariant: the user sees
            // the tail of history they asked for.
            var bars = MakeBars(10, start: new DateTime(2026, 04, 23, 0, 0, 0, DateTimeKind.Utc));
            var filtered = Invoke(bars, since: null, until: null, limit: 3);

            Assert.Equal(3, filtered.Count);
            Assert.Equal(bars[7].Date, filtered[0].Date);
            Assert.Equal(bars[9].Date, filtered[^1].Date);
        }

        [Fact]
        public void ApplyFinalFilters_LimitLargerThanData_ReturnsAll()
        {
            // When the provider under-delivers, limit is not a minimum — the result
            // contains whatever survived filtering.
            var bars = MakeBars(3, start: new DateTime(2026, 04, 23, 0, 0, 0, DateTimeKind.Utc));
            var filtered = Invoke(bars, since: null, until: null, limit: 100);
            Assert.Equal(3, filtered.Count);
        }

        [Fact]
        public void ApplyFinalFilters_EmptyInput_ReturnsEmpty()
        {
            var filtered = Invoke(new List<Ohlcv>(), since: null, until: null, limit: 100);
            Assert.Empty(filtered);
        }

        [Fact]
        public void ApplyFinalFilters_LimitAppliedAfterFiltering_NotBefore()
        {
            // 6 raw bars, but 2 of them are zero-price drops + since filter removes
            // the first 2 → 2 valid bars survive. Limit=100 → both returned.
            // If the helper applied the limit first it'd truncate then filter,
            // producing an inconsistent page size.
            var bars = new List<Ohlcv>
            {
                new Ohlcv(D(0), 100, 100, 100, 100, 1),  // dropped by since
                new Ohlcv(D(1), 101, 101, 101, 101, 1),  // dropped by since
                new Ohlcv(D(2),   0,   0,   0,   0, 0),  // dropped by zero filter
                new Ohlcv(D(3), 103, 103, 103, 103, 1),
                new Ohlcv(D(4),   0,   0,   0,   0, 0),  // dropped by zero filter
                new Ohlcv(D(5), 105, 105, 105, 105, 1),
            };
            long sinceMs = UnixMs(bars[2].Date);  // include from bar 2 onward

            var filtered = Invoke(bars, since: sinceMs, until: null, limit: 100);

            Assert.Equal(2, filtered.Count);
            Assert.Equal(bars[3].Date, filtered[0].Date);
            Assert.Equal(bars[5].Date, filtered[^1].Date);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Reflects <see cref="HistoricalDataFetcher.ApplyFinalFilters"/> (private
        /// instance method) and invokes it against a scratch instance. Constructor
        /// dependencies are passed as nulls — ApplyFinalFilters does not touch them.
        /// </summary>
        private static List<Ohlcv> Invoke(List<Ohlcv> bars, long? since, long? until, int limit)
        {
            // HistoricalDataFetcher is a plain class (not abstract). ApplyFinalFilters
            // is instance, not static, so we need an instance — but the method body
            // never reads any field. The four ctor args can safely be null.
            var fetcher = (HistoricalDataFetcher)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(HistoricalDataFetcher));
            var method = typeof(HistoricalDataFetcher).GetMethod(
                "ApplyFinalFilters",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException("ApplyFinalFilters not found.");

            object?[] args = { bars, since, until, limit };
            return (List<Ohlcv>)method.Invoke(fetcher, args)!;
        }

        private static List<Ohlcv> MakeBars(int count, DateTime start)
        {
            var list = new List<Ohlcv>(count);
            for (int i = 0; i < count; i++)
                list.Add(new Ohlcv(start.AddMinutes(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 1000 + i));
            return list;
        }

        private static DateTime D(int minutes)
            => new DateTime(2026, 04, 23, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);

        private static long UnixMs(DateTime dt)
            => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }
}
