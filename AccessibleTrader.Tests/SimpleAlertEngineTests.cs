using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The simple alert engine, against the report Cody made from live use (2026-10-09):
    /// <i>"if I select SMA on the chart under indicator, this doesn't have overbought/oversold
    /// zones … why not have an option for 'touches'? … what if I want to know if price touches
    /// the 50 week …"</i>.
    ///
    /// <para>Each fixture puts the navigation cursor somewhere OTHER than the live bar unless a
    /// test says otherwise: an alert that reads the cursor and one that reads the market agree
    /// when the two coincide, and every test below would then pass for the wrong reason.</para>
    /// </summary>
    public class SimpleAlertEngineTests
    {
        private static AlertEvaluator NewEvaluator() =>
            new(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());

        private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        private static Ohlcv Bar(int i, double open, double high, double low, double close) =>
            new(T0.AddHours(i), open, high, low, close, 1000);

        /// <summary>Flat bars at 100 (High 101, Low 99), all the same colour.</summary>
        private static Ohlcv[] FlatBars(int count) =>
            Enumerable.Range(0, count).Select(i => Bar(i, 100, 101, 99, 100.5)).ToArray();

        private static ChartSeries Series(string id, string code, string pane,
            IEnumerable<LevelConfig>? levels = null, params (string Name, double[] Data)[] comps)
        {
            var buffer = new SeriesDataBuffer { SeriesId = id };
            var config = new SeriesConfig { Id = id, Name = code, FriendlyName = code, IndicatorCode = code, Pane = pane };
            foreach (var (name, data) in comps)
            {
                buffer.ComponentData[name] = data;
                config.Components.Add(new ComponentConfig { Name = name, DisplayName = name });
            }
            foreach (var l in levels ?? Array.Empty<LevelConfig>()) config.Levels.Add(l);
            return new ChartSeries(config, buffer);
        }

        private static LevelConfig[] RsiLevels() => new[]
        {
            new LevelConfig { Name = "Overbought", Value = 70 },
            new LevelConfig { Name = "Midpoint",   Value = 50 },
            new LevelConfig { Name = "Oversold",   Value = 30 },
        };

        private static WorkspaceState State(Ohlcv[] bars, int cursorAt, params ChartSeries[] series) =>
            WorkspaceState.Initial with
            {
                SymbolDisplayName = "BTC/USD",
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                ActiveSeries = ImmutableList.Create(series),
                CurrentDataIndex = cursorAt,
            };

        private static readonly IReadOnlyDictionary<string, double> NoPrev = new Dictionary<string, double>();

        private static List<AlertFired> Eval(AlertEvaluator ev, AlertDefinition a, WorkspaceState s,
            IReadOnlyDictionary<string, double>? prev = null) =>
            ev.EvaluateAlerts(new[] { a }, s, s.Data[^1], s.Data[^2], prev ?? NoPrev).ToList();

        // ── Zone alerts read the LIVE bar, not the reading cursor ────────────────

        private static AlertDefinition Zone(string code, string? comp, AlertCondition c, AlertZone z) => new()
        {
            Id = "zone-" + Guid.NewGuid().ToString("N"),
            Name = "zone",
            Delivery = AlertDelivery.Speech,
            Target = AlertTarget.Indicator,
            Condition = c,
            IndicatorCode = code,
            ComponentName = comp,
            Zone = z,
        };

        [Fact]
        public void A_zone_alert_fires_when_the_live_bar_enters_the_zone_while_the_cursor_is_parked_in_history()
        {
            // RSI 50 everywhere, then the live bar jumps to 80. The cursor is parked on bar 10
            // (RSI 50). Reading the cursor, the zone never changes and the alert never fires.
            var ev = NewEvaluator();
            var alert = Zone("Rsi", "Rsi", AlertCondition.EntersZone, AlertZone.Overbought);
            var bars = FlatBars(40);

            var calm = Enumerable.Repeat(50.0, 40).ToArray();
            Eval(ev, alert, State(bars, 10, Series("rsi-1", "Rsi", "Pane_Rsi", RsiLevels(), ("Rsi", calm))));

            var hot = Enumerable.Repeat(50.0, 40).ToArray();
            hot[^1] = 80;
            // The cursor bar must not itself be in the zone, or the old reading agrees.
            Assert.True(hot[10] < 70);
            var fired = Eval(ev, alert, State(bars, 10, Series("rsi-1", "Rsi", "Pane_Rsi", RsiLevels(), ("Rsi", hot))));

            Assert.Single(fired);
        }

        [Fact]
        public void A_zone_alert_reads_the_component_it_names_not_the_first_one_the_indicator_has()
        {
            // Cipher B: the Wave Trend sits at 0; the Anchor Wave climbs to 58, past the 53
            // Overbought line. An Anchor Wave alert that reads the Wave Trend never fires.
            var ev = NewEvaluator();
            var alert = Zone("CIPHER_B", "Anchor Wave", AlertCondition.EntersZone, AlertZone.Overbought);
            var bars = FlatBars(40);
            var levels = new[]
            {
                new LevelConfig { Name = "Extreme OB", Value = 60 },
                new LevelConfig { Name = "Overbought", Value = 53 },
                new LevelConfig { Name = "Oversold",   Value = -53 },
                new LevelConfig { Name = "Extreme OS", Value = -60 },
            };
            var wave = Enumerable.Repeat(0.0, 40).ToArray();
            var anchorCalm = Enumerable.Repeat(10.0, 40).ToArray();
            var anchorHot = Enumerable.Repeat(10.0, 40).ToArray();
            anchorHot[^1] = 58;

            Eval(ev, alert, State(bars, 39, Series("cb", "CIPHER_B", "Pane_CIPHER_B", levels, ("Wave Trend", wave), ("Anchor Wave", anchorCalm))));
            var fired = Eval(ev, alert, State(bars, 39, Series("cb", "CIPHER_B", "Pane_CIPHER_B", levels, ("Wave Trend", wave), ("Anchor Wave", anchorHot))));

            Assert.Single(fired);
        }

        // ── "Changes direction" on an indicator is the INDICATOR turning ─────────

        private static AlertDefinition Direction(string code, string comp) => new()
        {
            Id = "dir-" + Guid.NewGuid().ToString("N"),
            Name = "turn",
            Delivery = AlertDelivery.Speech,
            Target = AlertTarget.Indicator,
            Condition = AlertCondition.ChangesDirection,
            IndicatorCode = code,
            ComponentName = comp,
        };

        [Fact]
        public void An_indicator_direction_alert_fires_when_the_component_turns_even_though_the_candles_keep_their_colour()
        {
            // SMA rising 1 per bar, then falling on the live bar. Every candle is green.
            var ev = NewEvaluator();
            var sma = Enumerable.Range(0, 30).Select(i => 100.0 + i).ToArray();
            sma[^1] = sma[^2] - 0.5;
            var state = State(FlatBars(30), 5, Series("sma-50", "Sma", "Main", null, ("Sma", sma)));

            Assert.Single(Eval(ev, Direction("Sma", "Sma"), state));
        }

        [Fact]
        public void An_indicator_direction_alert_does_not_fire_on_a_candle_colour_flip_while_the_indicator_keeps_rising()
        {
            var ev = NewEvaluator();
            var bars = FlatBars(30);
            bars[^2] = Bar(28, 100, 101, 99, 100.5);   // green
            bars[^1] = Bar(29, 100.5, 101, 99, 99.5);  // red
            var sma = Enumerable.Range(0, 30).Select(i => 100.0 + i).ToArray();
            var state = State(bars, 5, Series("sma-50", "Sma", "Main", null, ("Sma", sma)));

            Assert.Empty(Eval(ev, Direction("Sma", "Sma"), state));
        }

        [Fact]
        public void A_candle_direction_alert_is_still_the_candle_colour_flip()
        {
            // The other target keeps its meaning: Candle + ChangesDirection = colour flip.
            var ev = NewEvaluator();
            var bars = FlatBars(30);
            bars[^1] = Bar(29, 100.5, 101, 99, 99.5);   // red after green
            var alert = new AlertDefinition
            {
                Id = "c", Name = "flip", Delivery = AlertDelivery.Speech,
                Target = AlertTarget.Candle, Condition = AlertCondition.ChangesDirection,
            };
            Assert.Single(Eval(ev, alert, State(bars, 5)));
        }

        // ── Touches ──────────────────────────────────────────────────────────────

        private static AlertDefinition PriceTouch(double level, AlertTarget target = AlertTarget.Price) => new()
        {
            Id = "touch-" + Guid.NewGuid().ToString("N"),
            Name = "touch",
            Delivery = AlertDelivery.Speech,
            Target = target,
            Condition = AlertCondition.Touches,
            Threshold = level,
        };

        private static List<AlertFired> EvalBars(AlertEvaluator ev, AlertDefinition a, Ohlcv prev, Ohlcv now) =>
            ev.EvaluateAlerts(new[] { a }, State(new[] { prev, now }, 0), now, prev, NoPrev).ToList();

        [Fact]
        public void Price_touches_when_a_wick_reaches_the_level_from_below_though_the_close_never_does()
        {
            var fired = EvalBars(NewEvaluator(), PriceTouch(105),
                prev: Bar(0, 102, 103.5, 101.5, 103),
                now: Bar(1, 103, 105.2, 102.5, 104));
            var f = Assert.Single(fired);
            Assert.Contains("touched 105", f.SpeechText);
        }

        [Fact]
        public void Price_touches_when_a_wick_reaches_the_level_from_above()
        {
            Assert.Single(EvalBars(NewEvaluator(), PriceTouch(105, AlertTarget.Candle),
                prev: Bar(0, 108, 109, 107, 108),
                now: Bar(1, 108, 108.5, 104.9, 107)));
        }

        [Fact]
        public void Price_touches_on_a_bar_with_no_range_sitting_exactly_on_the_level()
        {
            Assert.Single(EvalBars(NewEvaluator(), PriceTouch(105),
                prev: Bar(0, 104, 104, 104, 104),
                now: Bar(1, 105, 105, 105, 105)));
        }

        [Fact]
        public void Price_does_not_touch_when_the_range_misses_the_level()
        {
            Assert.Empty(EvalBars(NewEvaluator(), PriceTouch(105),
                prev: Bar(0, 101, 102, 100, 101),
                now: Bar(1, 101, 104.99, 100, 104.5)));
        }

        [Fact]
        public void A_touch_fires_once_for_its_bar_however_often_the_bar_is_polled()
        {
            var ev = NewEvaluator();
            var alert = PriceTouch(105);
            var prev = Bar(0, 102, 103, 101, 103);
            var now = Bar(1, 103, 106, 102, 104);
            int fires = 0;
            for (int i = 0; i < 5; i++) fires += EvalBars(ev, alert, prev, now).Count;
            Assert.Equal(1, fires);
            // ...and the next bar that reaches it is a new touch.
            Assert.Single(EvalBars(ev, alert, now, Bar(2, 104, 105.5, 103, 104)));
        }

        private static WorkspaceState RsiAt(double[] values) =>
            State(FlatBars(values.Length), 0, Series("rsi-1", "Rsi", "Pane_Rsi", RsiLevels(), ("Rsi", values)));

        private static AlertDefinition RsiTouch(double level) => new()
        {
            Id = "rt-" + Guid.NewGuid().ToString("N"), Name = "rsi touch", Delivery = AlertDelivery.Speech,
            Target = AlertTarget.Indicator, Condition = AlertCondition.Touches,
            IndicatorCode = "Rsi", ComponentName = "Rsi", Threshold = level,
        };

        [Theory]
        [InlineData(65, 70)]   // up to it
        [InlineData(65, 72)]   // up through it
        [InlineData(75, 70)]   // down to it
        [InlineData(75, 68)]   // down through it
        public void An_indicator_touches_by_reaching_the_level_from_either_side(double before, double now)
        {
            var s = RsiAt(new[] { 50.0, 50, now });
            var prev = new Dictionary<string, double> { ["Rsi.Rsi"] = before };
            Assert.Single(Eval(NewEvaluator(), RsiTouch(70), s, prev));
        }

        [Fact]
        public void An_indicator_that_does_not_reach_the_level_does_not_touch_it()
        {
            var s = RsiAt(new[] { 50.0, 50, 69.9 });
            var prev = new Dictionary<string, double> { ["Rsi.Rsi"] = 65 };
            Assert.Empty(Eval(NewEvaluator(), RsiTouch(70), s, prev));
        }

        // ── A line instead of a number ───────────────────────────────────────────

        /// <summary>Price and two SMAs. SMA 20 is FIRST on the chart, so an alert that ignores
        /// the instance it named reads the wrong line.</summary>
        private static WorkspaceState PriceAndTwoSmas(Ohlcv prevBar, Ohlcv nowBar, double[] sma20, double[] sma50)
        {
            var bars = new[] { Bar(-1, 100, 100, 100, 100), prevBar, nowBar };
            return State(bars, 0,
                Series("sma-20", "Sma", "Main", null, ("Sma", sma20)),
                Series("sma-50", "Sma", "Main", null, ("Sma", sma50)));
        }

        private static AlertDefinition PriceVsLine(AlertCondition c, string lineSeriesId) => new()
        {
            Id = "line-" + Guid.NewGuid().ToString("N"),
            Name = "line",
            Delivery = AlertDelivery.Speech,
            Target = AlertTarget.Price,
            Condition = c,
            LineIndicatorCode = "Sma",
            LineComponentName = "Sma",
            LineSeriesId = lineSeriesId,
        };

        [Fact]
        public void Price_crossing_above_the_line_it_names_fires_and_the_other_SMA_is_not_consulted()
        {
            // Close 99 → 101. SMA 50: 100 → 100.5, crossed. SMA 20: 102 → 102, not crossed.
            var s = PriceAndTwoSmas(Bar(0, 99, 99, 99, 99), Bar(1, 101, 101, 101, 101),
                sma20: new[] { 102.0, 102, 102 }, sma50: new[] { 100.0, 100, 100.5 });

            var f = Assert.Single(Eval(NewEvaluator(), PriceVsLine(AlertCondition.CrossesAbove, "sma-50"), s));
            Assert.Contains("crossed above Sma at 100.5", f.SpeechText);
            Assert.Empty(Eval(NewEvaluator(), PriceVsLine(AlertCondition.CrossesAbove, "sma-20"), s));
        }

        [Fact]
        public void A_crossing_of_a_moving_line_compares_the_previous_close_with_the_lines_previous_value()
        {
            // The line rose THROUGH a flat price: 99 < 100 before, 101 > 100 now. Price never
            // went above it — not a CrossesAbove, however the current close compares with any
            // single fixed number.
            var s = PriceAndTwoSmas(Bar(0, 100, 100, 100, 100), Bar(1, 100, 100, 100, 100),
                sma20: new[] { 0.0, 0, 0 }, sma50: new[] { 99.0, 99, 101 });
            Assert.Empty(Eval(NewEvaluator(), PriceVsLine(AlertCondition.CrossesAbove, "sma-50"), s));
            Assert.Single(Eval(NewEvaluator(), PriceVsLine(AlertCondition.CrossesBelow, "sma-50"), s));
        }

        [Fact]
        public void Price_touches_the_line_when_the_bar_reaches_its_current_value()
        {
            var s = PriceAndTwoSmas(Bar(0, 103, 104, 102, 103), Bar(1, 103, 103.5, 99.8, 102),
                sma20: new[] { 0.0, 0, 0 }, sma50: new[] { 100.0, 100, 100 });
            var f = Assert.Single(Eval(NewEvaluator(), PriceVsLine(AlertCondition.Touches, "sma-50"), s));
            Assert.Contains("touched Sma at 100", f.SpeechText);

            var miss = PriceAndTwoSmas(Bar(0, 103, 104, 102, 103), Bar(1, 103, 103.5, 100.2, 102),
                sma20: new[] { 0.0, 0, 0 }, sma50: new[] { 100.0, 100, 100 });
            Assert.Empty(Eval(NewEvaluator(), PriceVsLine(AlertCondition.Touches, "sma-50"), miss));
        }

        [Fact]
        public void A_line_that_has_left_the_chart_is_said_once_not_silently_never_fired()
        {
            var ev = NewEvaluator();
            var said = new List<string>();
            ev.EvaluationDegraded += (_, why) => said.Add(why);
            var alert = PriceVsLine(AlertCondition.Touches, "sma-200") with { LineIndicatorCode = "Ema", LineComponentName = "Ema" };
            var s = PriceAndTwoSmas(Bar(0, 103, 104, 102, 103), Bar(1, 103, 103.5, 99.8, 102),
                sma20: new[] { 0.0, 0, 0 }, sma50: new[] { 100.0, 100, 100 });

            Eval(ev, alert, s);
            Eval(ev, alert, s);

            var why = Assert.Single(said);
            Assert.Contains("not on this chart", why);
        }

        [Fact]
        public void An_indicator_line_alert_uses_its_own_instances_crossover_memory_not_the_other_SMAs()
        {
            // Indicator target SMA 20 vs a fixed 100. The code key "Sma.Sma" holds the SMA 50's
            // previous value (written last); the SMA 20's own was 99. Read by code, 101 → 101
            // is no crossing; read by instance, 99 → 101 is.
            var s = PriceAndTwoSmas(Bar(0, 1, 1, 1, 1), Bar(1, 1, 1, 1, 1),
                sma20: new[] { 0.0, 0, 101 }, sma50: new[] { 0.0, 0, 101 });
            var prev = new Dictionary<string, double>
            {
                ["Sma.Sma"] = 101,
                [AlertEvaluator.InstanceKey(s.ActiveSeries[0], "Sma")] = 99,
                [AlertEvaluator.InstanceKey(s.ActiveSeries[1], "Sma")] = 101,
            };
            var alert = new AlertDefinition
            {
                Id = "i", Name = "sma20", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
                Condition = AlertCondition.CrossesAbove, Threshold = 100,
                IndicatorCode = "Sma", ComponentName = "Sma", SeriesId = "sma-20",
            };
            Assert.Single(Eval(NewEvaluator(), alert, s, prev));
        }

        // ── Zones come from the indicator's own lines ────────────────────────────

        [Fact]
        public void A_zone_alert_on_an_indicator_with_no_zone_lines_says_so_once()
        {
            var ev = NewEvaluator();
            var said = new List<string>();
            ev.EvaluationDegraded += (_, why) => said.Add(why);
            var alert = Zone("Sma", "Sma", AlertCondition.EntersZone, AlertZone.Overbought);
            var s = State(FlatBars(5), 0, Series("sma-50", "Sma", "Main", null, ("Sma", new[] { 1.0, 2, 3, 4, 5 })));

            Eval(ev, alert, s);
            Eval(ev, alert, s);

            Assert.Contains("no overbought line", Assert.Single(said));
        }

        [Fact]
        public void A_zone_follows_a_level_the_user_moved_not_a_table()
        {
            // The user moved RSI's overbought line to 80. 75 is no longer overbought.
            var ev = NewEvaluator();
            var levels = new[] { new LevelConfig { Name = "Overbought", Value = 80 }, new LevelConfig { Name = "Oversold", Value = 20 } };
            var alert = Zone("Rsi", "Rsi", AlertCondition.EntersZone, AlertZone.Overbought);
            Eval(ev, alert, State(FlatBars(3), 0, Series("rsi-1", "Rsi", "Pane_Rsi", levels, ("Rsi", new[] { 50.0, 50, 50 }))));
            Assert.Empty(Eval(ev, alert, State(FlatBars(3), 0, Series("rsi-1", "Rsi", "Pane_Rsi", levels, ("Rsi", new[] { 50.0, 50, 75 })))));
            Assert.Single(Eval(ev, alert, State(FlatBars(3), 0, Series("rsi-1", "Rsi", "Pane_Rsi", levels, ("Rsi", new[] { 50.0, 50, 81 })))));
        }
    }
}
