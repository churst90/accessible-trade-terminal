using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests that <see cref="ViewportRangeCalculator"/> correctly expands pane ranges
    /// to include visible reference levels (OB/OS/zero-line), so those lines are always
    /// on-screen regardless of where the indicator data currently sits.
    /// </summary>
    public class ViewportRangeCalculatorTests
    {
        private static readonly ViewportRangeCalculator Calc = new ViewportRangeCalculator();

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="WorkspaceState"/> with <paramref name="barCount"/> synthetic
        /// OHLCV bars and optionally an indicator series in a separate pane.
        /// </summary>
        private static WorkspaceState BuildState(
            int barCount,
            double priceBase,
            ImmutableList<ChartSeries>? activeSeries = null,
            int viewportLength = 0)
        {
            var bars = new List<Ohlcv>(barCount);
            for (int i = 0; i < barCount; i++)
            {
                bars.Add(new Ohlcv(
                    Date:   new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                    Open:   priceBase,
                    High:   priceBase + 10,
                    Low:    priceBase - 10,
                    Close:  priceBase,
                    Volume: 100
                ));
            }

            return WorkspaceState.Initial with
            {
                Data            = new TimeSeriesBuffer<Ohlcv>(bars),
                ActiveSeries    = activeSeries ?? ImmutableList<ChartSeries>.Empty,
                ViewportStartIndex = 0,
                ViewportLength  = viewportLength > 0 ? viewportLength : barCount,
            };
        }

        /// <summary>
        /// Builds a synthetic RSI series in pane "RSI" with data that stays in the 40–60
        /// band (never reaching OB=70 or OS=30) but with Overbought/Oversold/Midpoint levels.
        /// </summary>
        private static ChartSeries BuildRsiSeries(int barCount, double rsiValue = 50.0)
        {
            var config = new SeriesConfig
            {
                IndicatorCode = "RSI",
                Pane          = "RSI",
            };
            config.Components.Add(new ComponentConfig
            {
                Name        = "RSI",
                DisplayType = ComponentDisplayType.Line,
                ColorHex    = "#FFFFFF",
            });
            config.Levels.Add(new LevelConfig { Name = "Overbought", Value = 70, IsVisible = true });
            config.Levels.Add(new LevelConfig { Name = "Midpoint",   Value = 50, IsVisible = true });
            config.Levels.Add(new LevelConfig { Name = "Oversold",   Value = 30, IsVisible = true });

            var rsiData = new double[barCount];
            Array.Fill(rsiData, rsiValue); // data sits in a narrow 40–60 band

            var dataBuffer = new SeriesDataBuffer { SeriesId = config.Id };
            dataBuffer.ComponentData["RSI"] = rsiData;

            return new ChartSeries(config, dataBuffer);
        }

        // ── 1. Guard cases ────────────────────────────────────────────────────

        [Fact]
        public void Calculate_NullData_ReturnsDefaultRange()
        {
            var state = WorkspaceState.Initial; // Data is empty, ViewportLength = 100
            var result = Calc.Calculate(state);

            Assert.Equal(0,   result.MainRange.Min);
            Assert.Equal(100, result.MainRange.Max);
        }

        [Fact]
        public void Calculate_ZeroViewportLength_ReturnsDefaultRange()
        {
            var state = BuildState(10, 100) with { ViewportLength = 0 };
            var result = Calc.Calculate(state);

            Assert.Equal(0,   result.MainRange.Min);
            Assert.Equal(100, result.MainRange.Max);
        }

        // ── 2. Main pane price range ──────────────────────────────────────────

        [Fact]
        public void Calculate_MainPane_RangeEncompassesPriceData()
        {
            // Bars with High=110 and Low=90 → raw range 90–110 → with 5% buffer ~87.0–113.0
            var state = BuildState(10, 100);
            var result = Calc.Calculate(state);

            Assert.True(result.MainRange.Min <= 90,  $"Min {result.MainRange.Min} should be ≤ 90");
            Assert.True(result.MainRange.Max >= 110, $"Max {result.MainRange.Max} should be ≥ 110");
        }

        // ── 3. Levels expand pane range when data is narrow ───────────────────

        /// <summary>
        /// When RSI data sits entirely in 40–60, the pane range must still include 70 (OB) and 30 (OS)
        /// because those levels are injected with IsVisible=true.
        /// This is the core regression test for the ViewportRangeCalculator levels-expansion fix.
        /// </summary>
        [Fact]
        public void Calculate_RsiPaneWithNarrowData_LevelsExpandRangeToIncludeObOs()
        {
            const int bars = 20;
            var rsiSeries = BuildRsiSeries(bars, rsiValue: 50.0);
            var state = BuildState(bars, 100, ImmutableList.Create(rsiSeries));

            var result = Calc.Calculate(state);

            Assert.True(result.PaneRanges.ContainsKey("RSI"), "PaneRanges must contain RSI pane.");

            var (min, max) = result.PaneRanges["RSI"];
            Assert.True(min <= 30, $"Pane min {min} should be ≤ 30 (Oversold level)");
            Assert.True(max >= 70, $"Pane max {max} should be ≥ 70 (Overbought level)");
        }

        [Fact]
        public void Calculate_MacdPaneWithPositiveData_LevelsExpandRangeToIncludeZero()
        {
            const int bars = 15;
            var config = new SeriesConfig { IndicatorCode = "MACD", Pane = "MACD" };
            config.Components.Add(new ComponentConfig { Name = "MACD", DisplayType = ComponentDisplayType.Line, ColorHex = "#FFFFFF" });
            config.Levels.Add(new LevelConfig { Name = "Zero", Value = 0, IsVisible = true });

            // All MACD values positive (data never crosses zero)
            var macdData = new double[bars];
            Array.Fill(macdData, 50.0);

            var dataBuffer = new SeriesDataBuffer { SeriesId = config.Id };
            dataBuffer.ComponentData["MACD"] = macdData;
            var macdSeries = new ChartSeries(config, dataBuffer);

            var state = BuildState(bars, 100, ImmutableList.Create(macdSeries));
            var result = Calc.Calculate(state);

            Assert.True(result.PaneRanges.ContainsKey("MACD"), "PaneRanges must contain MACD pane.");
            var (min, max) = result.PaneRanges["MACD"];
            Assert.True(min <= 0, $"Pane min {min} should be ≤ 0 (zero-line level)");
        }

        // ── 4. Hidden levels do NOT expand the range ──────────────────────────

        [Fact]
        public void Calculate_HiddenLevel_DoesNotExpandRange()
        {
            const int bars = 10;
            var config = new SeriesConfig { IndicatorCode = "RSI", Pane = "RSI" };
            config.Components.Add(new ComponentConfig { Name = "RSI", DisplayType = ComponentDisplayType.Line, ColorHex = "#FFF" });
            // Only add a hidden Overbought level — should not force range up to 70
            config.Levels.Add(new LevelConfig { Name = "Overbought", Value = 70, IsVisible = false });

            var rsiData = new double[bars];
            Array.Fill(rsiData, 50.0); // data sits entirely at 50

            var dataBuffer = new SeriesDataBuffer { SeriesId = config.Id };
            dataBuffer.ComponentData["RSI"] = rsiData;
            var rsiSeries = new ChartSeries(config, dataBuffer);

            var state = BuildState(bars, 100, ImmutableList.Create(rsiSeries));
            var result = Calc.Calculate(state);

            Assert.True(result.PaneRanges.ContainsKey("RSI"));
            var (_, max) = result.PaneRanges["RSI"];
            // max should be driven only by the data (≈50 + 10% buffer ≈ 55), well below 70
            Assert.True(max < 70, $"Hidden level should not pull pane max {max} up to 70");
        }

        // ── 5. Levels alone (no component data) establish a valid range ───────

        [Fact]
        public void Calculate_SeriesWithOnlyLevelsAndNoData_StillGetsPaneRange()
        {
            const int bars = 10;
            var config = new SeriesConfig { IndicatorCode = "RSI", Pane = "RSI" };
            // No components — only levels
            config.Levels.Add(new LevelConfig { Name = "Overbought", Value = 70, IsVisible = true });
            config.Levels.Add(new LevelConfig { Name = "Oversold",   Value = 30, IsVisible = true });

            var dataBuffer = new SeriesDataBuffer { SeriesId = config.Id };
            var rsiSeries = new ChartSeries(config, dataBuffer);

            var state = BuildState(bars, 100, ImmutableList.Create(rsiSeries));
            var result = Calc.Calculate(state);

            Assert.True(result.PaneRanges.ContainsKey("RSI"), "Pane established by levels alone must appear in PaneRanges.");
            var (min, max) = result.PaneRanges["RSI"];
            Assert.True(min <= 30, $"Min {min} should be ≤ 30");
            Assert.True(max >= 70, $"Max {max} should be ≥ 70");
        }

        // ── 6. Multiple separate panes each get their own range ───────────────

        [Fact]
        public void Calculate_TwoSeparatePanes_EachGetCorrectRange()
        {
            const int bars = 10;

            // RSI pane: data at 50, levels at 30 and 70
            var rsiSeries = BuildRsiSeries(bars, 50.0);

            // Custom oscillator pane: data at -0.5, zero-line level
            var oscConfig = new SeriesConfig { IndicatorCode = "MACD", Pane = "OSC" };
            oscConfig.Components.Add(new ComponentConfig { Name = "OSC", DisplayType = ComponentDisplayType.Line, ColorHex = "#FFF" });
            oscConfig.Levels.Add(new LevelConfig { Name = "Zero", Value = 0, IsVisible = true });

            var oscData = new double[bars];
            Array.Fill(oscData, -0.5);
            var oscDataBuffer = new SeriesDataBuffer { SeriesId = oscConfig.Id };
            oscDataBuffer.ComponentData["OSC"] = oscData;
            var oscSeries = new ChartSeries(oscConfig, oscDataBuffer);

            var state = BuildState(bars, 100, ImmutableList.Create(rsiSeries, oscSeries));
            var result = Calc.Calculate(state);

            // RSI pane must include 30 and 70
            Assert.True(result.PaneRanges["RSI"].Min <= 30);
            Assert.True(result.PaneRanges["RSI"].Max >= 70);

            // OSC pane must include 0
            Assert.True(result.PaneRanges["OSC"].Min <= 0,
                $"OSC pane min {result.PaneRanges["OSC"].Min} must be ≤ 0");
        }

        // ── 7. Two same-type oscillators share a pane with a unified range ────────

        [Fact]
        public void Calculate_TwoRsiSeriesSamePane_UnifiedRangeCoversBothAndLevels()
        {
            const int bars = 20;

            // RSI-A: data in 40–50 range
            var configA = new SeriesConfig { IndicatorCode = "RSI", Pane = "Pane_RSI" };
            configA.Components.Add(new ComponentConfig { Name = "RSI", DisplayType = ComponentDisplayType.Line, ColorHex = "#FFF" });
            configA.Levels.Add(new LevelConfig { Name = "Overbought", Value = 70, IsVisible = true });
            configA.Levels.Add(new LevelConfig { Name = "Oversold",   Value = 30, IsVisible = true });
            var dataA = new double[bars]; Array.Fill(dataA, 45.0);
            var bufA = new SeriesDataBuffer { SeriesId = configA.Id };
            bufA.ComponentData["RSI"] = dataA;
            var rsiA = new ChartSeries(configA, bufA);

            // RSI-B: data in 55–65 range (different period, different GUID)
            var configB = new SeriesConfig { IndicatorCode = "RSI", Pane = "Pane_RSI" };
            configB.Components.Add(new ComponentConfig { Name = "RSI", DisplayType = ComponentDisplayType.Line, ColorHex = "#FF0" });
            configB.Levels.Add(new LevelConfig { Name = "Overbought", Value = 70, IsVisible = true });
            configB.Levels.Add(new LevelConfig { Name = "Oversold",   Value = 30, IsVisible = true });
            var dataB = new double[bars]; Array.Fill(dataB, 60.0);
            var bufB = new SeriesDataBuffer { SeriesId = configB.Id };
            bufB.ComponentData["RSI"] = dataB;
            var rsiB = new ChartSeries(configB, bufB);

            var state = BuildState(bars, 100, ImmutableList.Create(rsiA, rsiB));
            var result = Calc.Calculate(state);

            Assert.True(result.PaneRanges.ContainsKey("Pane_RSI"));
            var (min, max) = result.PaneRanges["Pane_RSI"];
            Assert.True(min <= 30, $"Shared pane min {min} should include OS=30");
            Assert.True(max >= 70, $"Shared pane max {max} should include OB=70");
        }
    }
}
