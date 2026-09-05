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
        public void BarDetail_EmptyData_SaysSoRatherThanNothing()
        {
            // This used to assert Assert.Empty(bus.Log) — it PINNED the silent failure.
            // Ctrl+Shift+D is an explicit request, and answering an explicit request with
            // pure silence leaves the user unable to tell a broken key from an empty chart.
            // What must not happen is a bar DESCRIPTION when there is no bar; saying why is
            // the whole point.
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var state = BaseState();  // no bars, no series

            svc.AnnounceDetails(state);

            var ev = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
            Assert.Equal(FeedbackType.Error, ev.Type);
            Assert.Contains("No chart data", ev.Message!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BarDetail_MissingSeries_SaysSoRatherThanNothing()
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

            // Same correction as above: it named the silence as the expected behaviour.
            // Naming the unresolved focus is what makes it actionable.
            var ev = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
            Assert.Equal(FeedbackType.Error, ev.Type);
            Assert.Contains("No series in focus", ev.Message!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BarDetail_BullishMarubozu_AnnouncesPatternAndWickPercents()
        {
            // O=100, H=110, L=100, C=110 → range=10, body=10 → bodyPct=100% → marubozu.
            // Bullish (Close >= Open), upper wick 0%, lower wick 0%.
            //
            // The spelling is the shared vocabulary's, not this route's own: CandlePatternSpeech
            // names it "Bullish marubozu" and the live bar-close announcement says exactly the
            // same words about exactly the same bar. It used to be "Bullish Marubozu" here and
            // "Bullish marubozu" there, from two classifiers with two thresholds.
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var bars = MakeBars(new[] { (100d, 110d, 100d, 110d) });
            var state = CandleState(bars, idx: 0);

            svc.AnnounceDetails(state);

            var msg = LastAnnouncement(bus);
            Assert.Contains("Bullish marubozu", msg);
            Assert.Contains("Body 100%", msg);
            Assert.Contains("Upper wick 0%", msg);
            Assert.Contains("Lower wick 0%", msg);
        }

        [Fact]
        public void BarDetail_HammerInADowntrend_IsAHammer()
        {
            // Hammer and hanging man are the SAME SHAPE; only the trend into them decides which.
            // The detail key could not make that distinction at all until it started using the
            // shared analyser — its private classifier saw one bar, so every one of these was
            // announced as a hammer, including the ones that mean the opposite.
            //
            // The shape: O=101, H=101, L=90, C=100 → body 9%, lower wick 91%, upper wick 0%.
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var bars = MakeBars(new[]
            {
                (120d, 121d, 119d, 119d),
                (118d, 119d, 112d, 112d),
                (111d, 112d, 105d, 105d),
                (104d, 105d, 101d, 101d),
                (101d, 101d,  90d, 100d),   // the shape, at the end of a decline
            });
            var state = CandleState(bars, idx: 4);

            svc.AnnounceDetails(state);

            var msg = LastAnnouncement(bus);
            Assert.Contains("Hammer", msg);
            Assert.DoesNotContain("Hanging man", msg);
        }

        [Fact]
        public void BarDetail_TheSameShapeInAnUptrend_IsAHangingMan()
        {
            // The other half, and the reason the first one is not vacuous: identical candle,
            // opposite trend, opposite meaning. Announcing "hammer" here would tell a blind
            // trader a decline is ending when an advance is.
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var bars = MakeBars(new[]
            {
                (90d,  92d,  89d,  91d),
                (91d,  97d,  91d,  96d),
                (96d, 103d,  96d, 102d),
                (102d, 108d, 102d, 107d),
                (101d, 101d,  90d, 100d),   // the same shape, at the end of an advance
            });
            var state = CandleState(bars, idx: 4);

            svc.AnnounceDetails(state);

            var msg = LastAnnouncement(bus);
            Assert.Contains("Hanging man", msg);
            Assert.DoesNotContain("Hammer", msg);
        }

        [Fact]
        public void BarDetail_AMultiBarPattern_IsNamedWithItsSpanAndLean()
        {
            // THE POINT OF THIS PASS. Ctrl+Shift+D on a bar the user was not present for could
            // not name any of the twelve multi-bar patterns, because its classifier looked at one
            // bar. Three white soldiers is three; hearing "Bullish" on the last of them told the
            // user nothing about the two behind the cursor, which is the whole shape.
            //
            // Three white soldiers: each bar large-bodied and green, each opening INSIDE the
            // previous body and closing above its close.
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var bars = MakeBars(new[]
            {
                (100d, 101d,  99d, 100d),
                (100d, 110d,  99d, 109d),   // soldier 1
                (103d, 118d, 102d, 117d),   // soldier 2: opens inside 1's body, closes above it
                (110d, 126d, 109d, 125d),   // soldier 3
            });
            var state = CandleState(bars, idx: 3);

            svc.AnnounceDetails(state);

            var msg = LastAnnouncement(bus);
            Assert.Contains("Three white soldiers", msg);
            // The span is stated because a reader of history cannot otherwise recover it, and the
            // lean because "continuation" and "reversal" are opposite trades.
            Assert.Contains("3-bar continuation", msg);
        }

        [Fact]
        public void BarDetail_AnOrdinaryBar_CarriesNoBiasClause()
        {
            // Vacuity guard for the clause above: appending "reversal" to every bar would satisfy
            // the multi-bar test and destroy the signal. An unremarkable candle gets a direction
            // and the measurements, nothing else.
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            // Not "three ordinary green bars" — that is three white soldiers, and the first
            // draft of this guard named one.
            var bars = MakeBars(new[]
            {
                (100d, 110d,  98d, 107d),
                (107d, 112d, 100d, 102d),
                (103d, 112d, 101d, 108d),
            });
            var state = CandleState(bars, idx: 2);

            svc.AnnounceDetails(state);

            var msg = LastAnnouncement(bus);
            Assert.DoesNotContain("reversal", msg);
            Assert.DoesNotContain("continuation", msg);
            Assert.Contains("Bullish.", msg);
        }

        [Fact]
        public void BarDetail_FlatRange_ClassifiesAsADoji()
        {
            // O=H=L=C=100. The old private classifier called this "Flat"; the shared analyser
            // calls a zero body a doji, and the direction is Neutral so no trend word leads it.
            // Both are defensible readings of a bar that did not move — what is NOT defensible is
            // the terminal using a word here that no other route in the app would use.
            var bus = new SpyEventBus();
            var svc = new BarDetailService(bus);
            var bars = MakeBars(new[] { (100d, 100d, 100d, 100d) });
            var state = CandleState(bars, idx: 0);

            svc.AnnounceDetails(state);
            Assert.Contains("Doji", LastAnnouncement(bus));
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

        /// <summary>
        /// <b>This used to assert the "BB|Upper" definition, and it was a test written to match a
        /// definition rather than the product.</b> Bollinger's components are called "UpperBand"
        /// and "LowerBand" — have been for years — so the registered "BB|Upper" and "BB|Lower"
        /// entries bound to nothing on a real chart, and the only place a component named "Upper"
        /// existed was in this fixture. The test passed for the whole life of the defect.
        ///
        /// <para>
        /// Both definitions were deleted on 2026-09-05 (see <c>IndicatorContextAnalyzer</c>), and
        /// what replaces this assertion is the one that would have caught it: the analyser has
        /// nothing to say about a REAL Bollinger series. Its narration comes from the price-cross
        /// route instead — see <c>OverlayCrossNarrationTests</c> — and
        /// <c>NarrationRouteContractTests.EveryRegisteredOscillatorDefinition_NamesAComponentThatExists</c>
        /// fails if a key that binds to no component comes back.
        /// </para>
        /// </summary>
        [Fact]
        public void Analyzer_HasNothingToSay_AboutARealBollingerSeries()
        {
            var analyzer = new IndicatorContextAnalyzer();
            var series = MakeIndicatorSeries("bb", "Bb",
                component: "UpperBand",
                values: new[] { 110.0, 111.0, 112.0 });
            var state = StateAtIndex(series, 2);

            var ctx = analyzer.Analyze(series, state);

            // The fallback path still returns a context for the first visible component — it is
            // how the detail key reads a value — but it carries no zone and no crossover, which
            // is what "no registered definition" means.
            Assert.Equal(ZoneStatus.Normal, ctx!.Zone);
            Assert.Equal(CrossoverStatus.None, ctx.Crossover);
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
