using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.MyData;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Events-as-markers: an imported date,label[,value] dataset becomes an
    /// indicator whose dot lands on the bar covering each event date, whose
    /// marker Y is the event's own value (a fill price sits where it filled),
    /// and whose SPEECH is the event's own label — "Bought 0.5 BTC", not a
    /// generic marker phrase.
    /// </summary>
    public class MyDataEventsProviderTests : IDisposable
    {
        private readonly string _dir = TestTemp.NewDir("att-mydata-ev-");
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

        private sealed class TempPaths : IPlatformPathService
        {
            public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private async Task<(MyDataEventsProvider Provider, string Code)> BuildAsync()
        {
            var store = new MyDataStore(new TempPaths(_dir), NullLogger<MyDataStore>.Instance);
            var (ds, _) = await store.ImportAsync("Trades",
                "date,label,value\n" +
                "2026-01-02,Bought 0.5 BTC,42000\n" +
                "2026-01-02,Set stop,41000\n" +      // two events, same bar
                "2026-01-05,Sold,\n" +               // no value → marker at close
                "2025-12-01,Before history,1\n");    // before first bar → dropped
            var provider = new MyDataEventsProvider(store);
            return (provider, MyDataEventsProvider.CodePrefix + ds.Id);
        }

        private static Ohlcv[] DailyBars(int days) => Enumerable.Range(0, days)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                100 + i, 101 + i, 99 + i, 100 + i, 1000))
            .ToArray();

        [Fact]
        public async Task Metadata_lists_one_indicator_per_events_dataset_only()
        {
            var (provider, code) = await BuildAsync();
            var meta = Assert.Single(provider.GetIndicators());
            Assert.Equal(code, meta.Code);
            Assert.Equal("My Events: Trades", meta.Name);
            Assert.Equal("My Data", meta.Category);
            Assert.Equal("Main", meta.DefaultPane); // markers live on the price pane
        }

        [Fact]
        public async Task Markers_land_on_the_covering_bar_with_event_values()
        {
            var (provider, code) = await BuildAsync();
            var bars = DailyBars(7); // Jan 1..7
            var buffer = new IndicatorResultBuffer(new Dictionary<string, double[]>(), bars.Length);

            provider.Calculate(code, bars, new Dictionary<string, object>(), buffer);
            var span = buffer.GetComponentSpan(MyDataEventsProvider.CompEvent);

            Assert.Equal(41000, span[1]);          // Jan 2: LAST same-bar event's value wins the Y
            Assert.Equal(bars[4].Close, span[4]);  // Jan 5: no value → bar close
            Assert.True(double.IsNaN(span[0]));    // Jan 1: nothing (the 2025 event dropped)
            Assert.True(double.IsNaN(span[6]));
        }

        [Fact]
        public async Task Speech_is_the_event_label_including_both_same_bar_events()
        {
            var (provider, code) = await BuildAsync();
            var bars = DailyBars(7);
            var buffer = new IndicatorResultBuffer(new Dictionary<string, double[]>(), bars.Length);
            provider.Calculate(code, bars, new Dictionary<string, object>(), buffer);

            string? speech = provider.GetComponentSpeech(
                MyDataEventsProvider.CompEvent, 41000, bars[1],
                new Dictionary<string, double[]>(), 1);

            Assert.NotNull(speech);
            Assert.Contains("Bought 0.5 BTC", speech);
            Assert.Contains("Set stop", speech);
            Assert.Contains("42,000", speech!.Replace("42000", "42,000")); // value spoken with the price formatter

            // NaN bar (no event) falls through to the generic template.
            Assert.Null(provider.GetComponentSpeech(
                MyDataEventsProvider.CompEvent, double.NaN, bars[0],
                new Dictionary<string, double[]>(), 0));
        }

        [Fact]
        public async Task Detail_fact_reads_events_or_says_none()
        {
            var (provider, code) = await BuildAsync();
            var bars = DailyBars(7);
            var buffer = new IndicatorResultBuffer(new Dictionary<string, double[]>(), bars.Length);
            provider.Calculate(code, bars, new Dictionary<string, object>(), buffer);

            string onBar = provider.GetDetailFact(code, bars,
                new Dictionary<string, double[]>(), 1, new Dictionary<string, object>());
            Assert.Contains("Bought 0.5 BTC", onBar);

            string offBar = provider.GetDetailFact(code, bars,
                new Dictionary<string, double[]>(), 0, new Dictionary<string, object>());
            Assert.Equal("No event on this bar.", offBar);
        }

        [Fact]
        public async Task Unknown_code_calculates_to_all_NaN_without_throwing()
        {
            var (provider, _) = await BuildAsync();
            var bars = DailyBars(3);
            var buffer = new IndicatorResultBuffer(new Dictionary<string, double[]>(), bars.Length);
            provider.Calculate("MYDATA_EV_nope", bars, new Dictionary<string, object>(), buffer);
            Assert.All(buffer.GetComponentSpan(MyDataEventsProvider.CompEvent).ToArray(), v => Assert.True(double.IsNaN(v)));
        }
    }
}
