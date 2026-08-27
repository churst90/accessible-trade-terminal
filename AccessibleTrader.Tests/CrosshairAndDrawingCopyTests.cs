using System.Reflection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Two chart defects that both come down to copying the wrong thing.
    ///
    /// <para><b>The crosshair read a viewport-local index into an absolute array.</b>
    /// <c>RenderCrosshair</c> is called with <c>cursorIndex - viewportStart</c> and then did
    /// <c>data[localIndex]</c> on component arrays, which are absolute — indexed
    /// <c>ViewportStart + i</c> in every other renderer path. Pan back to
    /// <c>ViewportStartIndex = 500</c> and put the cursor on bar 560: the RSI pane's crosshair
    /// line and its numeric label were drawn from <c>rsi[60]</c>, a reading from five hundred
    /// bars ago, and <c>FormatAxisValue</c> rendered it as the current one. An earlier fix in
    /// the same method corrected the local index's upper BOUND, not the local-versus-absolute
    /// indexing.</para>
    ///
    /// <para><b>Duplicating a drawing hand-copied seven fields and dropped eight.</b>
    /// <c>DrawingContextMenu.OnDuplicate</c> wrote out <c>Type</c> and the anchors and omitted
    /// <c>Text</c>, <c>ChannelWidth</c>, <c>IsLocked</c>, <c>ExtendLeft</c>, <c>StopLoss</c>,
    /// <c>TakeProfit</c>, <c>RiskRewardRatio</c> and <c>MeasureResult</c> — while
    /// <c>DrawingData.Clone()</c> existed the whole time and copies all of them. Duplicating a
    /// Text Label gave a copy with empty <c>Text</c>, which <c>RenderTextLabels</c> skips
    /// entirely AND which never gets the "Label: …" treatment in its series name, so the
    /// duplicate was invisible <i>and</i> inaudible. The user was told "created" either way.</para>
    /// </summary>
    public class CrosshairAndDrawingCopyTests
    {
        // ── Crosshair index space ────────────────────────────────────────────

        private static ChartSeries SeriesWith(string component, double[] values)
        {
            var buffer = new SeriesDataBuffer { SeriesId = "rsi-1" };
            buffer.ComponentData[component] = values;
            var config = new SeriesConfig { Id = "rsi-1", Name = "RSI", IndicatorCode = "RSI" };
            config.Components.Add(new ComponentConfig { Name = component, DisplayName = component });
            return new ChartSeries(config, buffer);
        }

        [Fact]
        public void The_crosshair_reads_the_cursor_bar_not_the_viewport_offset()
        {
            // 700 bars where the value at index i is simply i, so a wrong index is unmistakable.
            var values = Enumerable.Range(0, 700).Select(i => (double)i).ToArray();
            var series = new[] { SeriesWith("Rsi", values) };

            // Panned back to bar 500, cursor on bar 560 → localIndex 60.
            double? read = ChartRenderer.CrosshairValueAt(series, localIndex: 60, viewportStart: 500);

            Assert.Equal(560, read);   // NOT 60
        }

        [Fact]
        public void With_no_pan_the_local_and_absolute_indices_coincide()
        {
            // The reason this survived: at ViewportStart 0 the buggy and correct readings are
            // identical, and that is the state a freshly loaded chart is in.
            var values = Enumerable.Range(0, 700).Select(i => (double)i).ToArray();
            var series = new[] { SeriesWith("Rsi", values) };

            Assert.Equal(60, ChartRenderer.CrosshairValueAt(series, localIndex: 60, viewportStart: 0));
        }

        [Fact]
        public void A_cursor_past_the_end_of_a_component_array_reports_nothing()
        {
            var series = new[] { SeriesWith("Rsi", new double[] { 1, 2, 3 }) };

            Assert.Null(ChartRenderer.CrosshairValueAt(series, localIndex: 10, viewportStart: 500));
            Assert.Null(ChartRenderer.CrosshairValueAt(series, localIndex: -600, viewportStart: 500));
        }

        [Fact]
        public void A_NaN_at_the_cursor_falls_through_to_the_next_component()
        {
            var buffer = new SeriesDataBuffer { SeriesId = "s" };
            buffer.ComponentData["A"] = new[] { 1.0, double.NaN, 3.0 };
            buffer.ComponentData["B"] = new[] { 9.0, 8.0, 7.0 };
            var config = new SeriesConfig { Id = "s", Name = "S", IndicatorCode = "S" };
            config.Components.Add(new ComponentConfig { Name = "A", DisplayName = "A" });
            config.Components.Add(new ComponentConfig { Name = "B", DisplayName = "B" });
            var series = new[] { new ChartSeries(config, buffer) };

            Assert.Equal(8.0, ChartRenderer.CrosshairValueAt(series, localIndex: 1, viewportStart: 0));
        }

        // ── DrawingData.Clone ────────────────────────────────────────────────

        [Fact]
        public void Clone_copies_every_property_of_a_drawing()
        {
            // Reflection rather than a hand-written field list, because a hand-written field
            // list is exactly what OnDuplicate was and exactly how eight fields went missing.
            // Adding a property to DrawingData without adding it to Clone fails here.
            var src = new DrawingData
            {
                Type = DrawingType.TextLabel,
                AnchorDate1 = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                AnchorPrice1 = 101.5,
                AnchorDate2 = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc),
                AnchorPrice2 = 202.5,
                AnchorDate3 = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                AnchorPrice3 = 303.5,
                Text = "resistance retest",
                ChannelWidth = 12.25,
                IsLocked = true,
                ExtendLeft = true,
                ExtendRight = true,
                StopLoss = 95.0,
                TakeProfit = 130.0,
                RiskRewardRatio = 3.5,
                MeasureResult = "+12.4% over 9 bars",
            };

            var copy = src.Clone();

            Assert.NotSame(src, copy);
            foreach (var p in typeof(DrawingData).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite) continue;
                Assert.True(Equals(p.GetValue(src), p.GetValue(copy)),
                    $"DrawingData.Clone() does not copy {p.Name} — the duplicate loses it silently.");
            }
        }

        [Fact]
        public void The_clone_fixture_sets_every_property_to_a_non_default_value()
        {
            // Vacuity check for the test above: a fixture that left a property at its default
            // would pass whether or not Clone copied it, which is how the omissions in
            // OnDuplicate would have gone unnoticed by a less careful test.
            var src = new DrawingData
            {
                Type = DrawingType.TextLabel,
                AnchorDate1 = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                AnchorPrice1 = 101.5,
                AnchorDate2 = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc),
                AnchorPrice2 = 202.5,
                AnchorDate3 = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                AnchorPrice3 = 303.5,
                Text = "resistance retest",
                ChannelWidth = 12.25,
                IsLocked = true,
                ExtendLeft = true,
                ExtendRight = true,
                StopLoss = 95.0,
                TakeProfit = 130.0,
                RiskRewardRatio = 3.5,
                MeasureResult = "+12.4% over 9 bars",
            };
            var blank = new DrawingData();

            var same = typeof(DrawingData)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .Where(p => Equals(p.GetValue(src), p.GetValue(blank)))
                .Select(p => p.Name)
                .ToList();

            Assert.True(same.Count == 0,
                "These properties are still at their default in the Clone fixture, so the test "
                + "above cannot tell whether Clone copies them: " + string.Join(", ", same));
        }
    }
}
