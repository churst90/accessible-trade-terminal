using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.MyData;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// My Data v2 — the overlay/alignment engine. Pins forward-fill (weekly data
    /// holds its value across daily bars, NaN before the first point, gaps carry
    /// the last real value), the overlay rebase (first aligned value maps to the
    /// chart close there — relative performance), the ratio math, per-column
    /// components, and the Normalize-to-100 parameter.
    /// </summary>
    public class MyDataSeriesProviderTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("att-mydata-sr-").FullName;
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

        private sealed class TempPaths : IPlatformPathService
        {
            public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private MyDataStore NewStore() => new(new TempPaths(_dir), NullLogger<MyDataStore>.Instance);

        private static Ohlcv[] DailyBars(int days, double startClose = 100) => Enumerable.Range(0, days)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                startClose + i, startClose + i + 1, startClose + i - 1, startClose + i, 1000))
            .ToArray();

        private static IndicatorResultBuffer Buffer(int length)
            => new(new Dictionary<string, double[]>(), length);

        // ── Forward-fill alignment (the shared compare engine) ───────────────

        [Fact]
        public async Task Weekly_data_forward_fills_across_daily_bars()
        {
            var store = NewStore();
            var (ds, _) = await store.ImportAsync("W",
                "date,value\n2026-01-02,10\n2026-01-09,20\n");
            var parsed = store.GetParsed(ds.Id)!;
            var bars = DailyBars(12); // Jan 1..12

            var aligned = MyDataSeriesProvider.AlignForwardFill(parsed, "value", bars);

            Assert.True(double.IsNaN(aligned[0])); // Jan 1: before first data point
            Assert.Equal(10, aligned[1]);          // Jan 2: first point
            Assert.Equal(10, aligned[7]);          // Jan 8: still carrying
            Assert.Equal(20, aligned[8]);          // Jan 9: second point
            Assert.Equal(20, aligned[11]);         // Jan 12: carries to the end
        }

        [Fact]
        public async Task Gaps_carry_the_last_real_value()
        {
            var store = NewStore();
            var (ds, _) = await store.ImportAsync("G",
                "date,value\n2026-01-01,5\n2026-01-03,\n2026-01-05,7\n");
            var parsed = store.GetParsed(ds.Id)!;
            var bars = DailyBars(6);

            var aligned = MyDataSeriesProvider.AlignForwardFill(parsed, "value", bars);

            Assert.Equal(5, aligned[2]); // the blank Jan 3 row does NOT reset the carry
            Assert.Equal(7, aligned[4]);
        }

        // ── The three indicator families ─────────────────────────────────────

        private async Task<(MyDataSeriesProvider Provider, MyDataStore Store, string Id)> BuildAsync(
            string name, string csv)
        {
            var store = NewStore();
            var (ds, _) = await store.ImportAsync(name, csv);
            return (new MyDataSeriesProvider(store), store, ds.Id);
        }

        [Fact]
        public async Task Metadata_lists_series_overlay_and_ratio_families()
        {
            var (provider, _, _) = await BuildAsync("Alt",
                "date,open,high,low,close\n2026-01-01,10,11,9,10\n2026-01-02,10,12,10,12\n");

            var metas = provider.GetIndicators();
            Assert.Equal(3, metas.Count); // series + overlay + ratio (OHLCV dataset)
            Assert.Contains(metas, m => m.Name == "My Data: Alt" && m.DefaultPane == "Alt");
            Assert.Contains(metas, m => m.Name == "My Data overlay: Alt" && m.DefaultPane == "Main");
            Assert.Contains(metas, m => m.Name == "My Data ratio: Alt");
        }

        [Fact]
        public async Task Value_dataset_gets_one_component_per_column_and_no_ratio()
        {
            var (provider, _, _) = await BuildAsync("Budget",
                "date,Income,Expenses\n2026-01-01,5000,4200\n2026-02-01,5100,4400\n");

            var metas = provider.GetIndicators();
            Assert.Equal(2, metas.Count); // no ratio for value datasets
            var series = metas.Single(m => m.Name == "My Data: Budget");
            Assert.Equal(new[] { "Income", "Expenses" }, series.Components.Select(c => c.Name));
        }

        [Fact]
        public async Task Overlay_rebases_to_the_chart_close_at_first_alignment()
        {
            // Dataset starts at 10 on Jan 2; chart close there is 101. Rebase:
            // every dataset value scales by 101/10 — same starting point, then
            // relative performance diverges.
            var (provider, _, id) = await BuildAsync("Alt",
                "date,value\n2026-01-02,10\n2026-01-05,15\n");
            var bars = DailyBars(7);
            var buffer = Buffer(bars.Length);

            provider.Calculate(MyDataSeriesProvider.OverlayPrefix + id, bars,
                new Dictionary<string, object>(), buffer);
            var span = buffer.GetComponentSpan("value");

            Assert.True(double.IsNaN(span[0]));
            Assert.Equal(101, span[1], 10);              // first aligned bar = chart close
            Assert.Equal(101 * 1.5, span[4], 10);        // +50% in the data → +50% on the overlay
        }

        [Fact]
        public async Task Ratio_divides_chart_close_by_dataset_value()
        {
            var (provider, _, id) = await BuildAsync("Alt",
                "date,open,high,low,close\n2026-01-01,10,11,9,10\n2026-01-03,20,21,19,20\n");
            var bars = DailyBars(5); // closes 100..104
            var buffer = Buffer(bars.Length);

            provider.Calculate(MyDataSeriesProvider.RatioPrefix + id, bars,
                new Dictionary<string, object>(), buffer);
            var span = buffer.GetComponentSpan("Ratio");

            Assert.Equal(100.0 / 10, span[0], 10);
            Assert.Equal(101.0 / 10, span[1], 10);
            Assert.Equal(102.0 / 20, span[2], 10); // dataset moved to 20 on Jan 3
        }

        [Fact]
        public async Task Normalize_parameter_rebases_every_column_to_100()
        {
            var (provider, _, id) = await BuildAsync("Budget",
                "date,Income,Expenses\n2026-01-01,5000,4000\n2026-01-03,5500,3600\n");
            var bars = DailyBars(4);
            var buffer = Buffer(bars.Length);

            provider.Calculate(MyDataSeriesProvider.SeriesPrefix + id, bars,
                new Dictionary<string, object> { [MyDataSeriesProvider.ParamNormalize] = 1 }, buffer);

            Assert.Equal(100, buffer.GetComponentSpan("Income")[0], 10);
            Assert.Equal(100, buffer.GetComponentSpan("Expenses")[0], 10);
            Assert.Equal(110, buffer.GetComponentSpan("Income")[2], 10);   // +10%
            Assert.Equal(90, buffer.GetComponentSpan("Expenses")[2], 10);  // −10%
        }

        [Fact]
        public async Task Raw_series_defaults_to_actual_values()
        {
            var (provider, _, id) = await BuildAsync("Budget",
                "date,Income\n2026-01-01,5000\n");
            var bars = DailyBars(2);
            var buffer = Buffer(bars.Length);

            provider.Calculate(MyDataSeriesProvider.SeriesPrefix + id, bars,
                new Dictionary<string, object>(), buffer);

            Assert.Equal(5000, buffer.GetComponentSpan("Income")[0]);
            Assert.Equal(5000, buffer.GetComponentSpan("Income")[1]); // forward-filled
        }
    }
}
