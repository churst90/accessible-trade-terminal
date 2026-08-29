using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.MyData;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The My Data feature: CSV parsing (shape detection, date/number tolerance,
    /// hard errors on ambiguity), the store's persistence + quotas, and the
    /// provider's symbol/shape/fetch contract that the market cascade consumes.
    /// The parser's failure philosophy is pinned throughout: a silently-wrong
    /// chart is worse than a refused import.
    /// </summary>
    public class MyDataTests : IDisposable
    {
        private readonly string _dir;

        public MyDataTests() => _dir = TestTemp.NewDir("att-mydata-");
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

        private sealed class TempPaths : IPlatformPathService
        {
            public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private MyDataStore NewStore() => new(new TempPaths(_dir), NullLogger<MyDataStore>.Instance);

        // ── Parser: shape detection ──────────────────────────────────────────

        [Fact]
        public void Ohlcv_header_parses_as_candles()
        {
            var p = CsvDataParser.Parse(
                "date,open,high,low,close,volume\n" +
                "2026-01-01,10,12,9,11,1000\n" +
                "2026-01-02,11,13,10,12,1500\n");

            Assert.Equal(MyDataShape.Ohlcv, p.Shape);
            Assert.Equal(2, p.Bars.Count);
            Assert.Equal(12, p.Bars[1].Close);
            Assert.Equal(1500, p.Bars[1].Volume);
            Assert.Equal("1d", p.InferredTimeframe);
        }

        [Fact]
        public void Value_columns_parse_as_named_series()
        {
            var p = CsvDataParser.Parse(
                "date,Income,Expenses,Net\n" +
                "2026-01-01,5000,4200,800\n" +
                "2026-02-01,5100,4400,700\n" +
                "2026-03-01,5100,4000,1100\n");

            Assert.Equal(MyDataShape.Values, p.Shape);
            Assert.Equal(new[] { "Income", "Expenses", "Net" }, p.Columns);
            Assert.Equal(new double[] { 800, 700, 1100 }, p.ColumnData["Net"]);
            Assert.Equal("1M", p.InferredTimeframe);
        }

        [Fact]
        public void Text_second_column_parses_as_events()
        {
            var p = CsvDataParser.Parse(
                "date,label,value\n" +
                "2026-03-01,Bought 0.5 BTC,42000\n" +
                "2026-05-10,Sold 0.25 BTC,61000\n" +
                "2026-06-01,Rebalanced,\n");

            Assert.Equal(MyDataShape.Events, p.Shape);
            Assert.Equal(3, p.Events.Count);
            Assert.Equal("Bought 0.5 BTC", p.Events[0].Label);
            Assert.Equal(42000, p.Events[0].Value);
            Assert.Null(p.Events[2].Value);
        }

        // ── Parser: tolerance + hard errors ──────────────────────────────────

        [Fact]
        public void Delimiters_and_formats_are_tolerated()
        {
            // Semicolons, quoted thousands separators, $ signs, MM/dd/yyyy dates.
            var p = CsvDataParser.Parse(
                "date;Savings\n" +
                "01/15/2026;\"$1,234.56\"\n" +
                "02/15/2026;$1,300\n");

            Assert.Equal(MyDataShape.Values, p.Shape);
            Assert.Equal(1234.56, p.ColumnData["Savings"][0]);
        }

        [Fact]
        public void Unix_timestamps_are_accepted()
        {
            var p = CsvDataParser.Parse("date,value\n1750000000,1\n1750086400,2\n");
            Assert.Equal(MyDataShape.Values, p.Shape);
            Assert.Equal(2025, p.FirstDate.Year);
        }

        [Fact]
        public void Missing_header_is_a_hard_error_with_guidance()
        {
            var ex = Assert.Throws<FormatException>(() =>
                CsvDataParser.Parse("2026-01-01,10\n2026-01-02,11\n"));
            Assert.Contains("header", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Unreadable_date_names_the_line()
        {
            var ex = Assert.Throws<FormatException>(() =>
                CsvDataParser.Parse("date,value\n2026-01-01,1\nnot-a-date,2\n"));
            Assert.Contains("Line 3", ex.Message);
        }

        [Fact]
        public void High_below_low_is_refused()
        {
            Assert.Throws<FormatException>(() => CsvDataParser.Parse(
                "date,open,high,low,close\n2026-01-01,10,9,11,10\n"));
        }

        /// <summary>
        /// Survivor N26. The row cap is refused AT the boundary, not somewhere past it.
        ///
        /// <para>
        /// The sibling guard one line away (high-below-low, above) was caught by the 2026-08-29
        /// campaign and this one was not, for the reason every survivor in that run shared: no
        /// test fed an input from the wrong side of the limit. Import is the one path where a
        /// user hands the terminal an arbitrarily large file, and an unbounded parse builds the
        /// whole series in memory before anything downstream can object.
        /// </para>
        ///
        /// <para>
        /// The accept side is the half that makes this a boundary rather than a smoke test, and
        /// it is deliberately made to fail for a DIFFERENT reason. A file of exactly
        /// <c>MaxRows</c> single-column rows gets past the cap and is then refused for having
        /// one column — which proves the cap did not fire, without paying to parse 200,000 rows
        /// of real data on every suite run.
        /// </para>
        /// </summary>
        [Fact]
        public void The_row_cap_refuses_one_row_past_the_limit_and_admits_the_limit_itself()
        {
            static string Csv(int dataRows)
            {
                var sb = new System.Text.StringBuilder("date\n", dataRows * 2 + 8);
                for (int i = 0; i < dataRows; i++) sb.Append("x\n");
                return sb.ToString();
            }

            var tooMany = Assert.Throws<FormatException>(
                () => CsvDataParser.Parse(Csv(CsvDataParser.MaxRows + 1)));
            Assert.Contains("Too many rows", tooMany.Message);

            // Exactly at the limit: past the cap, and refused later for its single column.
            var atLimit = Assert.Throws<FormatException>(
                () => CsvDataParser.Parse(Csv(CsvDataParser.MaxRows)));
            Assert.DoesNotContain("Too many rows", atLimit.Message);
            Assert.Contains("column", atLimit.Message);
        }

        [Fact]
        public void Blank_cells_become_gaps_with_a_warning_and_rows_sort_by_date()
        {
            var p = CsvDataParser.Parse(
                "date,value\n2026-01-03,3\n2026-01-01,1\n2026-01-02,\n");
            Assert.Equal(new[] { 1.0, double.NaN, 3.0 },
                p.ColumnData["value"], new NaNTolerantComparer());
            Assert.Contains(p.Warnings, w => w.Contains("gaps"));
        }

        private sealed class NaNTolerantComparer : System.Collections.Generic.IEqualityComparer<double>
        {
            public bool Equals(double a, double b) => a.Equals(b); // NaN.Equals(NaN) is true
            public int GetHashCode(double v) => v.GetHashCode();
        }

        // ── Number formats: the two shapes that used to chart wrong ──────────
        //
        // TryParseNumber stripped commas only when a dot was ALSO present, then parsed with
        // NumberStyles.Float — which does not include AllowThousands. "1,234" therefore
        // reached the parser with its comma intact, failed, and became a silent gap; and
        // "1.234,56" (a German export meaning 1234.56) had the comma stripped and parsed as
        // 1.23456, a value a thousand times too small that charts perfectly cleanly. The
        // second is the one this class exists to prevent, and dd.MM.yyyy has always been in
        // DateFormats — so the parser accepted the date half of a German export while
        // corrupting the number half.

        [Theory]
        [InlineData("1234", 1234)]
        [InlineData("1,234", 1234)]            // thousands-separated integer: was a gap
        [InlineData("1,234,567", 1234567)]
        [InlineData("1,234.56", 1234.56)]
        [InlineData("1.234,56", 1234.56)]      // German: was 1.23456
        [InlineData("1.234.567,89", 1234567.89)]
        [InlineData("1 234,56", 1234.56)]      // French, ordinary space
        [InlineData("1,5", 1.5)]               // decimal comma, not a thousands group
        [InlineData("1,23", 1.23)]
        [InlineData("-1.234,5", -1234.5)]
        [InlineData("1.234", 1.234)]           // single dot stays the invariant decimal point
        [InlineData("$1,234.50", 1234.5)]
        [InlineData("12.5%", 12.5)]
        [InlineData("1.5e3", 1500)]
        public void Numbers_parse_whatever_the_export_used_for_separators(string cell, double expected)
        {
            Assert.True(CsvDataParser.TryParseNumber(cell, out var v), $"'{cell}' did not parse.");
            Assert.Equal(expected, v, 9);
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("1,2,3")]     // not a grouping shape and not a number
        public void Non_numbers_are_still_refused(string cell)
        {
            Assert.False(CsvDataParser.TryParseNumber(cell, out _), $"'{cell}' parsed as a number.");
        }

        [Fact]
        public void A_german_export_charts_at_the_right_magnitude()
        {
            // Both halves of the same file: dd.MM.yyyy dates and comma decimals.
            var p = CsvDataParser.Parse("date;value\n01.01.2026;1.234,56\n02.01.2026;2.345,67\n");
            Assert.Equal(new[] { 1234.56, 2345.67 }, p.ColumnData["value"], new NaNTolerantComparer());
            Assert.Empty(p.Warnings);
        }

        // ── Store: persistence + quotas ──────────────────────────────────────

        [Fact]
        public async Task Import_persists_across_store_instances()
        {
            var store = NewStore();
            await store.ImportAsync("Budget", "date,Net\n2026-01-01,800\n2026-02-01,700\n");

            var reopened = NewStore();
            var ds = Assert.Single(reopened.Datasets);
            Assert.Equal("Budget", ds.Name);
            Assert.Equal(MyDataShape.Values, ds.Shape);
            Assert.Equal(2, reopened.GetParsed(ds.Id)!.RowCount);
        }

        [Fact]
        public async Task Duplicate_names_are_refused_and_delete_frees_the_name()
        {
            var store = NewStore();
            var (ds, _) = await store.ImportAsync("A", "date,v\n2026-01-01,1\n");
            await Assert.ThrowsAsync<FormatException>(() =>
                store.ImportAsync("a", "date,v\n2026-01-01,1\n")); // case-insensitive

            await store.DeleteAsync(ds.Id);
            Assert.Empty(store.Datasets);
            await store.ImportAsync("A", "date,v\n2026-01-01,1\n"); // name reusable
        }

        [Fact]
        public async Task Changed_event_fires_on_import_and_delete()
        {
            var store = NewStore();
            int fired = 0;
            store.Changed += () => fired++;
            var (ds, _) = await store.ImportAsync("A", "date,v\n2026-01-01,1\n");
            await store.DeleteAsync(ds.Id);
            Assert.Equal(2, fired);
        }

        // ── Provider: the cascade contract ───────────────────────────────────

        private async Task<(MyDataProvider Provider, MyDataStore Store)> ProviderWithDataAsync()
        {
            var store = NewStore();
            await store.ImportAsync("Portfolio",
                "date,open,high,low,close\n2026-01-01,10,12,9,11\n2026-01-02,11,13,10,12\n");
            await store.ImportAsync("Budget",
                "date,Income,Expenses\n2026-01-01,5000,4200\n2026-02-01,5100,4400\n");
            await store.ImportAsync("Trades",
                "date,label\n2026-01-15,Bought BTC\n");
            return (new MyDataProvider(store), store);
        }

        [Fact]
        public async Task Symbols_list_datasets_and_columns_but_never_events()
        {
            var (provider, _) = await ProviderWithDataAsync();
            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.MyData);

            Assert.Contains("Portfolio", symbols);
            Assert.Contains("Budget — Income", symbols);
            Assert.Contains("Budget — Expenses", symbols);
            Assert.DoesNotContain(symbols, s => s.Contains("Trades")); // events are markers, not charts
        }

        [Fact]
        public async Task Shape_resolves_per_symbol()
        {
            var (provider, _) = await ProviderWithDataAsync();
            Assert.Equal(ProviderDataShape.Ohlcv, provider.GetDataShapeForSymbol("Portfolio"));
            Assert.Equal(ProviderDataShape.SingleValueLine, provider.GetDataShapeForSymbol("Budget — Income"));
        }

        [Fact]
        public async Task Fetch_returns_ohlcv_bars_and_column_values()
        {
            var (provider, _) = await ProviderWithDataAsync();

            var (candles, _) = await provider.FetchOhlcvAsync(
                new MarketDataRequest("MyData", "Portfolio", "1d", Limit: 500));
            Assert.Equal(2, candles.Count);
            Assert.Equal(12, candles[1].Close);

            var (line, _) = await provider.FetchOhlcvAsync(
                new MarketDataRequest("MyData", "Budget — Expenses", "1M", Limit: 500));
            Assert.Equal(2, line.Count);
            Assert.Equal(4400, line[1].Close);
            Assert.Equal(line[1].Open, line[1].Close); // value series: flat bars
        }

        [Fact]
        public async Task Unknown_symbol_returns_empty_not_throw()
        {
            var (provider, _) = await ProviderWithDataAsync();
            var (bars, _) = await provider.FetchOhlcvAsync(
                new MarketDataRequest("MyData", "Nope", "1d"));
            Assert.Empty(bars);
        }
    }
}
