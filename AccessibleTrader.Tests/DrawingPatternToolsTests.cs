using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Drawing;
using AccessibleTrader.Core.Services.Drawing.Calculators;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-11-01 drawing-tools pass: the pattern tools' readings, the swing snap, the
    /// pullback a fib extension's third point goes on, custom fib levels and how many points each
    /// tool takes. Cody: <i>"incorporate the additional drawing pattern recognition tools as well
    /// that may be useful"</i>; <i>"how would i find the pull back level, like a retest of the
    /// swing low?"</i>
    /// </summary>
    public class DrawingPatternToolsTests
    {
        private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        private static List<(DateTime, double)> Pts(params double[] prices) =>
            prices.Select((p, i) => (T0.AddDays(i * 5), p)).ToList();

        /// <summary>Daily bars, each with the given close and a high/low one above and below.</summary>
        private static List<Ohlcv> Bars(params double[] closes) =>
            closes.Select((c, i) => new Ohlcv(T0.AddDays(i), c, c + 1, c - 1, c, 0)).ToList();

        // ── Harmonics ───────────────────────────────────────────────────────────

        [Fact]
        public void A_textbook_Gartley_is_named_bullish_with_its_ratios()
        {
            // X 100, A 200 (XA 100), B at .618 back = 138.2, C at .618 of AB = 176.4,
            // D at .786 of XA = 121.4 (CD 55 = 1.44 × BC 38.2).
            string said = PatternReading.Xabcd(Pts(100, 200, 138.2, 176.4, 121.4));
            Assert.StartsWith("Bullish Gartley.", said);
            Assert.Contains("B retraced 61.8% of XA", said);
            Assert.Contains("D is at 78.6% of XA", said);
        }

        [Fact]
        public void A_Crab_upside_down_is_a_bearish_Crab()
        {
            // X 200, A 100, B .5 back = 150, C .886 of AB = 105.7, D 1.618 of XA from A = 261.8
            // (CD 156.1 = 3.52 × BC).
            string said = PatternReading.Xabcd(Pts(200, 100, 150, 105.7, 261.8));
            Assert.StartsWith("Bearish Crab.", said);
        }

        [Fact]
        public void Ratios_that_fit_nothing_name_the_nearest_and_what_misses()
        {
            // B .618 like a Gartley but D at .6 of XA — short of every table.
            string said = PatternReading.Xabcd(Pts(100, 200, 138.2, 176.4, 140));
            Assert.StartsWith("No harmonic pattern matches.", said);
            Assert.Contains("The nearest is the Gartley, but its D is 78.6% of XA", said);
        }

        [Fact]
        public void Points_that_do_not_alternate_are_said_so_not_measured()
        {
            Assert.StartsWith("The points do not alternate", PatternReading.Xabcd(Pts(100, 200, 250, 176, 121)));
            Assert.StartsWith("The points do not alternate", PatternReading.Abcd(Pts(100, 200, 150, 140), null));
        }

        [Fact]
        public void Every_bound_has_five_percent_tolerance_and_no_more()
        {
            Assert.True(PatternReading.Within(0.618 * 1.049, 0.618, 0.618));
            Assert.False(PatternReading.Within(0.618 * 1.051, 0.618, 0.618));
            Assert.True(PatternReading.Within(0.618 * 0.951, 0.618, 0.618));
            Assert.False(PatternReading.Within(0.618 * 0.949, 0.618, 0.618));
        }

        // ── ABCD ────────────────────────────────────────────────────────────────

        [Fact]
        public void An_AB_equals_CD_is_named_with_its_bar_counts()
        {
            // A 200 → B 100 (AB 100), C 161.8 (.618), D 61.8 (CD 100 = AB, 1.618 × BC).
            var data = Bars(Enumerable.Range(0, 30).Select(_ => 150.0).ToArray());
            string said = PatternReading.Abcd(Pts(200, 100, 161.8, 61.8), data);
            Assert.StartsWith("Bullish ABCD.", said);
            Assert.Contains("AB equals CD, the textbook form.", said);
            Assert.Contains("AB took 5 bars and CD 5.", said);
        }

        [Fact]
        public void A_shallow_C_is_not_an_ABCD()
        {
            string said = PatternReading.Abcd(Pts(200, 100, 110, 60), null);
            Assert.StartsWith("Not an ABCD: C should retrace 38.2 to 88.6% of AB and retraced 10%", said);
        }

        // ── Head and shoulders ──────────────────────────────────────────────────

        [Fact]
        public void A_top_that_closed_under_its_neckline_gives_the_measured_target()
        {
            // Shoulders 150 (day 0, 20), neckline 100 flat (days 5, 15), head 200 (day 10).
            // Closes after the right shoulder: 120, then 95 on day 22 — the break.
            var closes = Enumerable.Repeat(120.0, 30).ToArray();
            closes[22] = 95;
            var data = Bars(closes);
            var pts = new List<(DateTime, double)>
            {
                (T0, 150), (T0.AddDays(5), 100), (T0.AddDays(10), 200), (T0.AddDays(15), 100), (T0.AddDays(20), 150),
            };
            string said = PatternReading.HeadAndShoulders(pts, data);
            Assert.StartsWith("Head and shoulders top.", said);
            Assert.Contains("The neckline is flat.", said);
            Assert.Contains("Price closed below the neckline", said);
            Assert.Contains("the measured target is " + SpeechPriceFormatter.FormatPrice(0), said);
        }

        [Fact]
        public void A_head_lower_than_a_shoulder_is_refused()
        {
            string said = PatternReading.HeadAndShoulders(Pts(150, 100, 140, 100, 150), null);
            Assert.StartsWith("Not a head and shoulders", said);
        }

        // ── Triangle ────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(200, 100, 200, 150, "Ascending triangle")]
        [InlineData(200, 100, 150, 100, "Descending triangle")]
        [InlineData(200, 100, 160, 140, "Symmetrical triangle")]
        [InlineData(150, 100, 200, 50, "Expanding triangle")]
        [InlineData(150, 100, 200, 170, "Rising wedge")]
        [InlineData(200, 120, 160, 90, "Falling wedge")]
        public void Four_points_name_their_triangle(double a, double b, double c, double d, string kind)
        {
            Assert.StartsWith(kind, PatternReading.Triangle(Pts(a, b, c, d), null));
        }

        [Fact]
        public void A_symmetrical_triangle_says_where_its_sides_meet()
        {
            // Highs 200 (day 0) and 180 (day 10): upper 200 − 2i. Lows 100 (day 5) and 110
            // (day 15): lower 95 + i. They meet at day 35, 20 bars after D; on the last bar, day
            // 24, the sides are 152 and 119 and the close of 150 is between them.
            var data = Bars(Enumerable.Repeat(150.0, 25).ToArray());
            var pts = new List<(DateTime, double)>
            {
                (T0, 200), (T0.AddDays(5), 100), (T0.AddDays(10), 180), (T0.AddDays(15), 110),
            };
            string said = PatternReading.Triangle(pts, data);
            Assert.StartsWith("Symmetrical triangle", said);
            Assert.Contains("The sides meet 20 bars after the last point", said);
            Assert.Contains("Price is still inside", said);
        }

        // ── Elliott ─────────────────────────────────────────────────────────────

        [Fact]
        public void An_impulse_that_keeps_the_three_rules_is_named()
        {
            // 100 → 150 → 120 → 220 → 180 → 230: w1 50, w3 100, w5 50; 4 stays above 150.
            string said = PatternReading.Elliott(Pts(100, 150, 120, 220, 180, 230));
            Assert.StartsWith("Elliott impulse up: all three rules hold.", said);
            Assert.Contains("wave 3 is 2 times wave 1", said);
            Assert.Contains("Wave 3 is the extended wave.", said);
        }

        [Fact]
        public void Each_broken_rule_is_named()
        {
            Assert.Contains("wave 2 went past the start of wave 1", PatternReading.Elliott(Pts(100, 150, 90, 220, 180, 230)));
            Assert.Contains("wave 3 is the shortest", PatternReading.Elliott(Pts(100, 150, 120, 160, 155, 300)));
            Assert.Contains("wave 4 entered wave 1's range", PatternReading.Elliott(Pts(100, 150, 120, 220, 140, 230)));
        }

        // ── Swing snap ──────────────────────────────────────────────────────────

        [Fact]
        public void Swing_snap_takes_the_low_wick_then_the_high_and_alternates()
        {
            var data = Bars(100, 110, 120, 130, 140);
            var points = new List<(DateTime, double)> { (T0, 100), (T0.AddDays(2), 120), (T0.AddDays(4), 140) };
            var snapped = DrawingInteractionManager.SwingSnap(points, data);
            Assert.Equal((99.0, -1), snapped[0]);
            Assert.Equal((121.0, 1), snapped[1]);
            Assert.Equal((139.0, -1), snapped[2]);
        }

        [Fact]
        public void A_point_past_the_last_bar_keeps_its_price()
        {
            var data = Bars(100, 110);
            var points = new List<(DateTime, double)> { (T0, 100), (T0.AddDays(9), 150) };
            var snapped = DrawingInteractionManager.SwingSnap(points, data);
            Assert.Equal((99.0, -1), snapped[0]);
            Assert.Equal((150.0, 0), snapped[1]);
        }

        // ── Pullback ────────────────────────────────────────────────────────────

        [Fact]
        public void The_pullback_is_the_lowest_low_before_a_new_high()
        {
            // Swing 100 (day 0) → 200 (day 4). Then lows 170, 150, 160 and a high of 201 on day 8.
            // Day 9 sinks to 120, but that is after the swing end was passed.
            var data = new List<Ohlcv>();
            double[] lows = { 99, 120, 140, 160, 190, 170, 150, 160, 180, 120 };
            double[] highs = { 101, 130, 150, 170, 200, 185, 165, 175, 201, 130 };
            for (int i = 0; i < lows.Length; i++)
                data.Add(new Ohlcv(T0.AddDays(i), lows[i], highs[i], lows[i], highs[i], 0));
            var pb = DrawingInteractionManager.FindPullback(data, T0, 100, T0.AddDays(4), 200);
            Assert.NotNull(pb);
            Assert.Equal(6, pb!.Value.Bar);
            Assert.Equal(150, pb.Value.Extreme);
            Assert.Equal(0.5, pb.Value.Retrace, 6);
            Assert.True(pb.Value.Ended);
        }

        [Fact]
        public void No_pullback_when_the_swing_ends_on_the_last_bar()
        {
            var data = Bars(100, 150, 200);
            Assert.Null(DrawingInteractionManager.FindPullback(data, T0, 100, T0.AddDays(2), 200));
        }

        // ── Levels and points ───────────────────────────────────────────────────

        [Theory]
        [InlineData("0.618, 0.786", new[] { 0.618, 0.786 })]
        [InlineData("61.8%; 78.6%", new[] { 0.618, 0.786 })]
        [InlineData("0 0.5 1", new[] { 0.0, 0.5, 1.0 })]
        [InlineData("", new double[0])]
        public void Fib_levels_parse_from_what_a_trader_types(string raw, double[] expected)
        {
            var parsed = FibLevels.Parse(raw);
            Assert.NotNull(parsed);
            Assert.Equal(expected.Length, parsed!.Count);
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], parsed[i], 9);
        }

        [Fact]
        public void A_level_that_is_not_a_number_refuses_the_whole_list()
        {
            Assert.Null(FibLevels.Parse("0.618, golden"));
        }

        [Theory]
        [InlineData(DrawingType.HorizontalLine, 1)]
        [InlineData(DrawingType.FibRetracement, 2)]
        [InlineData(DrawingType.FibTimeZone, 2)]
        [InlineData(DrawingType.FibExtension, 3)]
        [InlineData(DrawingType.AbcdPattern, 4)]
        [InlineData(DrawingType.TrianglePattern, 4)]
        [InlineData(DrawingType.XabcdPattern, 5)]
        [InlineData(DrawingType.HeadAndShoulders, 5)]
        [InlineData(DrawingType.ElliottImpulse, 6)]
        public void Each_tool_takes_its_number_of_points_and_names_each(DrawingType type, int points)
        {
            Assert.Equal(points, DrawingInteractionManager.PointCount(type));
            if (points > 1) Assert.Equal(points, DrawingInteractionManager.AnchorNames(type).Length);
        }

        [Fact]
        public void Fib_time_zones_land_on_the_fibonacci_bars()
        {
            var data = Bars(Enumerable.Range(0, 30).Select(i => 100.0 + i).ToArray());
            var d = new DrawingData { Type = DrawingType.FibTimeZone, AnchorDate1 = T0.AddDays(2), AnchorDate2 = T0.AddDays(4) };
            var result = new FibTimeZoneCalculator().Calculate(d, data);
            // Unit 2 bars from bar 2: zones 0,1,2,3,5,8 → bars 2,4,6,8,12,18; 13 → 28.
            int[] bars = { 2, 4, 6, 8, 12, 18, 28 };
            int[] zones = { 0, 1, 2, 3, 5, 8, 13 };
            for (int k = 0; k < bars.Length; k++)
            {
                var line = result[FibTimeZoneCalculator.Key(zones[k])];
                Assert.Equal(1, line.Count(v => !double.IsNaN(v)));
                Assert.False(double.IsNaN(line[bars[k]]));
            }
            Assert.DoesNotContain(FibTimeZoneCalculator.Key(21), result.Keys.Where(k => result[k].Any(v => !double.IsNaN(v))));
        }

        [Fact]
        public void Sync_adds_parts_for_new_keys_keeps_the_old_and_leaves_excluded_out()
        {
            var config = new SeriesConfig();
            var kept = new ComponentConfig { Name = "50.0% Level", ColorHex = "#123456" };
            config.Components.Add(kept);
            config.Components.Add(new ComponentConfig { Name = "Gone" });
            config.ExcludedComponents.Add(new ComponentConfig { Name = "61.8% Level" });

            bool changed = DrawingComponents.Sync(config, DrawingType.FibRetracement,
                new[] { "0.0% Level", "50.0% Level", "61.8% Level" }, StubFactory());

            Assert.True(changed);
            Assert.Equal(new[] { "0.0% Level", "50.0% Level" }, config.Components.Select(c => c.Name));
            Assert.Same(kept, config.Components[1]);
            Assert.False(DrawingComponents.Sync(config, DrawingType.FibRetracement,
                new[] { "0.0% Level", "50.0% Level", "61.8% Level" }, StubFactory()));
        }

        [Fact]
        public void A_vertical_line_and_time_zones_are_drawn_as_full_height_marks()
        {
            var config = new SeriesConfig();
            DrawingComponents.Sync(config, DrawingType.FibTimeZone, new[] { FibTimeZoneCalculator.Key(0) }, StubFactory());
            Assert.True(config.Components[0].MarkLineAlpha > 0);
            var trend = new SeriesConfig();
            DrawingComponents.Sync(trend, DrawingType.TrendLine, new[] { "Trend Line" }, StubFactory());
            Assert.Equal(0, trend.Components[0].MarkLineAlpha);
        }

        private static AccessibleTrader.Core.Services.IIndicatorModelFactory StubFactory()
        {
            var f = NSubstitute.Substitute.For<AccessibleTrader.Core.Services.IIndicatorModelFactory>();
            f.CreateComponentConfig(NSubstitute.Arg.Any<string>(), NSubstitute.Arg.Any<string>())
             .Returns(ci => new ComponentConfig { Name = ci.ArgAt<string>(1) });
            return f;
        }
    }
}
