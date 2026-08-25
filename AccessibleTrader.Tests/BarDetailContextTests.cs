using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tier 2 coverage for the Ctrl+Shift+D path: <see cref="BarDetailService.AnnounceDetails"/>
    /// and <see cref="IndicatorContextAnalyzer"/>. Together these drive the deep bar summary
    /// that a blind trader relies on for structural context; a broken <c>GetBarDetailFact</c>
    /// or context classifier ships wrong information.
    /// </summary>
    public class BarDetailContextTests
    {
        // ── BarDetailService ────────────────────────────────────────────────

        [Fact]
        public void BarDetail_EmptyData_PublishesNothing()
        {
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var state = BaseState();  // no bars, no series
            svc.AnnounceDetails(state);
            Assert.Empty(bus.Log);
        }

        [Fact]
        public void BarDetail_MissingSeries_PublishesNothing()
        {
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var bars = MakeBars(new[] { (100d, 100d, 100d, 100d) });
            var state = BaseState() with
            {
                Data = bars,
                CurrentDataIndex = 0,
                FocusedSeriesId = "unknown-series",
                PrimarySeriesId = "unknown-series",
            };
            svc.AnnounceDetails(state);
            Assert.Empty(bus.Log);
        }

        [Fact]
        public void BarDetail_BullishMarubozu_AnnouncesPatternAndWickPercents()
        {
            // O=100, H=110, L=100, C=110 → range=10, body=10 → bodyPct=100% → Marubozu.
            // Bullish (Close >= Open), upper wick 0%, lower wick 0%.
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var bars = MakeBars(new[] { (100d, 110d, 100d, 110d) });
            var state = CandleState(bars, idx: 0);

            svc.AnnounceDetails(state);

            var msg = LastAnnouncement(bus);
            Assert.Contains("Bullish", msg);
            Assert.Contains("Marubozu", msg);
            Assert.Contains("Body 100%", msg);
            Assert.Contains("Upper wick 0%", msg);
            Assert.Contains("Lower wick 0%", msg);
        }

        [Fact]
        public void BarDetail_BearishHammer_AnnouncesHammer()
        {
            // Hammer: bodyPct < 0.30, lowerPct > 0.60, upperPct < 0.10.
            // O=101, H=101, L=90, C=100 → range=11, body=1, bodyPct=9%
            //   upperWick=101-max(101,100)=0, lowerWick=min(101,100)-90=10 → 91% → Hammer.
            //   Close(100) < Open(101) → Bearish.
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var bars = MakeBars(new[] { (101d, 101d, 90d, 100d) });
            var state = CandleState(bars, idx: 0);

            svc.AnnounceDetails(state);

            var msg = LastAnnouncement(bus);
            Assert.Contains("Bearish", msg);
            Assert.Contains("Hammer", msg);
        }

        [Fact]
        public void BarDetail_FlatRange_ClassifiesAsFlat()
        {
            // O=H=L=C=100 → range=0 → "Flat".
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var bars = MakeBars(new[] { (100d, 100d, 100d, 100d) });
            var state = CandleState(bars, idx: 0);

            svc.AnnounceDetails(state);
            Assert.Contains("Flat", LastAnnouncement(bus));
        }

        [Fact]
        public void BarDetail_IndicatorSeries_ReadsVisibleComponentValues()
        {
            // Non-candle series: detail string is comp-name + F2 value list.
            // Hidden components are skipped. NaN values are skipped.
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var bars = MakeBars(new[] { (100d, 110d, 90d, 105d) });

            var cfg = new SeriesConfig { Id = "rsi", Name = "rsi", IndicatorCode = "RSI", Pane = "RSI" };
            cfg.Components.Add(new ComponentConfig { Name = "RSI",    DisplayName = "RSI",    IsVisible = true });
            cfg.Components.Add(new ComponentConfig { Name = "Hidden", DisplayName = "Hidden", IsVisible = false });
            cfg.Components.Add(new ComponentConfig { Name = "NaNed",  DisplayName = "NaNed",  IsVisible = true });
            var buf = new SeriesDataBuffer { SeriesId = "rsi" };
            buf.ComponentData["RSI"]    = new[] { 64.25 };
            buf.ComponentData["Hidden"] = new[] { 99.99 };
            buf.ComponentData["NaNed"]  = new[] { double.NaN };
            var series = new ChartSeries(cfg, buf);

            var state = BaseState() with
            {
                Data = bars,
                CurrentDataIndex = 0,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = "rsi",
            };
            svc.AnnounceDetails(state);

            var msg = LastAnnouncement(bus);
            Assert.Contains("RSI 64.25", msg);
            Assert.DoesNotContain("Hidden", msg);
            Assert.DoesNotContain("NaNed",  msg);
        }

        // ── IndicatorContextAnalyzer ────────────────────────────────────────

        [Fact]
        public void Analyzer_Rsi_Overbought_MapsToHint()
        {
            var analyzer = new IndicatorContextAnalyzer();
            var series = MakeIndicatorSeries("rsi", "RSI",
                component: "RSI",
                values: new[] { 40.0, 50.0, 60.0, 72.0 });
            var state = StateAtIndex(series, 3);

            var ctx = analyzer.Analyze(series, state);
            Assert.NotNull(ctx);
            Assert.Equal(ZoneStatus.Overbought, ctx!.Zone);
            Assert.Equal("approaching overbought territory", ctx.NarrativeHint);
        }

        [Fact]
        public void Analyzer_Rsi_Oversold_MapsToHint()
        {
            var analyzer = new IndicatorContextAnalyzer();
            var series = MakeIndicatorSeries("rsi", "RSI",
                component: "RSI",
                values: new[] { 45.0, 40.0, 35.0, 25.0 });
            var state = StateAtIndex(series, 3);

            var ctx = analyzer.Analyze(series, state);
            Assert.NotNull(ctx);
            Assert.Equal(ZoneStatus.Oversold, ctx!.Zone);
            Assert.Equal("approaching oversold territory", ctx.NarrativeHint);
        }

        [Fact]
        public void Analyzer_Rsi_RisingSeries_MarksTrendUp()
        {
            var analyzer = new IndicatorContextAnalyzer();
            var series = MakeIndicatorSeries("rsi", "RSI",
                component: "RSI",
                values: new[] { 40.0, 50.0, 55.0, 60.0 });  // strictly rising, in Normal zone
            var state = StateAtIndex(series, 3);

            var ctx = analyzer.Analyze(series, state);
            Assert.NotNull(ctx);
            Assert.Equal(TrendDirection.Rising, ctx!.Trend);
            Assert.Equal(ZoneStatus.Normal, ctx.Zone);
            Assert.Equal("trending higher", ctx.NarrativeHint);
        }

        [Fact]
        public void Analyzer_Macd_BullishCrossover_DetectedByComparingPrevAndCurrent()
        {
            // Crossover definition: A was below B on prev bar; A >= B on current bar → bullish.
            // Registered def: IndicatorCode=MACD, ComponentName=Histogram, crossover A=MACD vs B=Signal.
            // We need MACD and Signal components (the crossover check reads THESE, not Histogram).
            var analyzer = new IndicatorContextAnalyzer();
            var cfg = new SeriesConfig { Id = "macd", Name = "MACD", IndicatorCode = "MACD", Pane = "MACD" };
            cfg.Components.Add(new ComponentConfig { Name = "Histogram", DisplayName = "Histogram", IsVisible = true });
            cfg.Components.Add(new ComponentConfig { Name = "MACD",      DisplayName = "MACD",      IsVisible = true });
            cfg.Components.Add(new ComponentConfig { Name = "Signal",    DisplayName = "Signal",    IsVisible = true });
            var buf = new SeriesDataBuffer { SeriesId = "macd" };
            buf.ComponentData["Histogram"] = new[] { -0.1, 0.1 };
            buf.ComponentData["MACD"]      = new[] { -0.5, 0.8 };  // MACD was below Signal, now above
            buf.ComponentData["Signal"]    = new[] {  0.3, 0.3 };
            var series = new ChartSeries(cfg, buf);
            var state = StateAtIndex(series, 1);

            var ctx = analyzer.Analyze(series, state);
            Assert.NotNull(ctx);
            Assert.Equal(CrossoverStatus.BullishCrossover, ctx!.Crossover);
            Assert.Equal("bullish crossover detected", ctx.NarrativeHint);
        }

        [Fact]
        public void Analyzer_BollingerUpper_MapsToAtUpperBandHint()
        {
            // BB Upper has no OB/OS threshold; the component-name branch in DetermineZone
            // drives AtUpperBand regardless of value. Hint: "at upper band - potential resistance".
            var analyzer = new IndicatorContextAnalyzer();
            var series = MakeIndicatorSeries("bb_upper", "BB",
                component: "Upper",
                values: new[] { 110.0, 111.0, 112.0 });
            var state = StateAtIndex(series, 2);

            var ctx = analyzer.Analyze(series, state);
            Assert.NotNull(ctx);
            Assert.Equal(ZoneStatus.AtUpperBand, ctx!.Zone);
            Assert.Equal("at upper band - potential resistance", ctx.NarrativeHint);
        }

        [Fact]
        public void Analyzer_NaNValue_ReturnsNull()
        {
            // A leaf bar whose current value is NaN must not surface a context — otherwise
            // speech reads "0.00 at upper band" nonsense on warmup bars.
            var analyzer = new IndicatorContextAnalyzer();
            var series = MakeIndicatorSeries("rsi", "RSI",
                component: "RSI",
                values: new[] { double.NaN });
            var state = StateAtIndex(series, 0);

            var ctx = analyzer.Analyze(series, state);
            Assert.Null(ctx);
        }

        [Fact]
        public void Analyzer_UnregisteredIndicator_FallsBackToFirstVisibleComponent()
        {
            // Foreign indicator code with no definition → fallback path yields the first
            // visible + unmuted component with Zone.Normal and empty hint.
            var analyzer = new IndicatorContextAnalyzer();
            var cfg = new SeriesConfig { Id = "x", Name = "Custom", IndicatorCode = "CUSTOM", Pane = "Main" };
            cfg.Components.Add(new ComponentConfig { Name = "Value", IsVisible = true });
            var buf = new SeriesDataBuffer { SeriesId = "x" };
            buf.ComponentData["Value"] = new[] { 42.0 };
            var series = new ChartSeries(cfg, buf);
            var state = StateAtIndex(series, 0);

            var ctx = analyzer.Analyze(series, state);
            Assert.NotNull(ctx);
            Assert.Equal("Value", ctx!.ComponentName);
            Assert.Equal(42.0, ctx.CurrentValue);
            Assert.Equal(ZoneStatus.Normal, ctx.Zone);
        }

        [Fact]
        public void Analyzer_CurrentDataIndexOutOfRange_ReturnsNull()
        {
            // Current bar index beyond the component array length → null (never crash).
            var analyzer = new IndicatorContextAnalyzer();
            var series = MakeIndicatorSeries("rsi", "RSI", "RSI", new[] { 50.0, 55.0 });
            var state = StateAtIndex(series, dataIndex: 5);  // out-of-bounds

            var ctx = analyzer.Analyze(series, state);
            Assert.Null(ctx);
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        private static WorkspaceState BaseState() => WorkspaceState.Initial;

        private static WorkspaceState CandleState(TimeSeriesBuffer<Ohlcv> data, int idx)
        {
            var cfg = new SeriesConfig { Id = CoreSeriesIds.Candles, IndicatorCode = "CANDLES", Pane = "Main" };
            var series = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles });
            return BaseState() with
            {
                Data = data,
                CurrentDataIndex = idx,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = CoreSeriesIds.Candles,
                PrimarySeriesId = CoreSeriesIds.Candles,
            };
        }

        private static WorkspaceState StateAtIndex(ChartSeries series, int dataIndex)
        {
            // Synthetic OHLCV (not read by the analyzer, but required on state.Data).
            var bars = MakeBars(Enumerable.Repeat((100d, 100d, 100d, 100d), Math.Max(1, dataIndex + 1)).ToArray());
            return BaseState() with
            {
                Data = bars,
                CurrentDataIndex = dataIndex,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
            };
        }

        private static TimeSeriesBuffer<Ohlcv> MakeBars((double o, double h, double l, double c)[] bars)
        {
            var t0 = new DateTime(2026, 4, 23, 9, 30, 0);
            var list = new List<Ohlcv>(bars.Length);
            for (int i = 0; i < bars.Length; i++)
            {
                var (o, h, l, c) = bars[i];
                list.Add(new Ohlcv(t0.AddMinutes(i), o, h, l, c, 1000));
            }
            return new TimeSeriesBuffer<Ohlcv>(list);
        }

        private static ChartSeries MakeIndicatorSeries(string id, string code, string component, double[] values)
        {
            var cfg = new SeriesConfig { Id = id, Name = id, IndicatorCode = code, Pane = code };
            cfg.Components.Add(new ComponentConfig { Name = component, DisplayName = component, IsVisible = true });
            var buf = new SeriesDataBuffer { SeriesId = id };
            buf.ComponentData[component] = values;
            return new ChartSeries(cfg, buf);
        }

        private static string LastAnnouncement(SpyEventBus bus)
        {
            var ev = bus.Log.OfType<AnnouncementEvent>().LastOrDefault();
            Assert.NotNull(ev);
            return ev!.Message;
        }
    }
}
