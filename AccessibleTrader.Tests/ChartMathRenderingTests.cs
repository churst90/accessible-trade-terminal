using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// Phase F test-debt: covers the FORWARD / remaining ChartMath surface that the
/// pointer-mapping suite (ChartMathPointerMappingTests) deliberately leaves out —
/// MapY (linear + log, and its round-trip against MapYToPrice), GetSeriesRange for
/// non-Main panes (min/max + 10% buffer + degenerate/empty fallbacks), GetPointValue
/// component + candle-mapping fallbacks, and the Heikin-Ashi transform formula.
///
/// These are the shared math primitives the renderers and sonifiers both call, so a
/// regression here skews the visual chart AND the audio chart at once.
/// </summary>
public class ChartMathRenderingTests
{
    // ── MapY (linear) ────────────────────────────────────────────────────────

    [Fact]
    public void MapY_Linear_MaxMapsToTop_MinMapsToBottom()
    {
        // Pane spans top=100 .. bottom=400 (height 300). MapY inverts the axis:
        // the max value sits at the top, the min at the bottom.
        Assert.Equal(100f, ChartMath.MapY(200, 100, 400, min: 100, max: 200, isLogScale: false), 3);
        Assert.Equal(400f, ChartMath.MapY(100, 100, 400, min: 100, max: 200, isLogScale: false), 3);
    }

    [Fact]
    public void MapY_Linear_Midpoint_IsPaneCentre()
    {
        // value 150 is the midpoint of [100,200] → centre of the pane (250).
        Assert.Equal(250f, ChartMath.MapY(150, 100, 400, min: 100, max: 200, isLogScale: false), 3);
    }

    [Fact]
    public void MapY_Linear_DegenerateRange_ReturnsPaneCentre()
    {
        // range <= epsilon → renderer can't distribute values, so everything
        // collapses to the vertical centre rather than dividing by ~0.
        float centre = 100 + (400 - 100) / 2f;
        Assert.Equal(centre, ChartMath.MapY(123, 100, 400, min: 50, max: 50, isLogScale: false), 3);
    }

    [Fact]
    public void MapY_DegenerateHeight_ReturnsTop()
    {
        // bottom <= top → zero (or inverted) pane height returns the top edge.
        Assert.Equal(400f, ChartMath.MapY(150, 400, 400, min: 100, max: 200, isLogScale: false), 3);
    }

    [Fact]
    public void MapY_Linear_RoundTrips_Through_MapYToPrice()
    {
        // MapY maps into a pane [top,bottom]; MapYToPrice expects a [0,height]
        // pane. Feed MapY a top=0 pane so the two share the same coordinate frame,
        // then confirm the inverse recovers the original value.
        const float height = 720f;
        const double min = 100, max = 200;
        foreach (double value in new[] { 100.0, 125.5, 175.0, 200.0 })
        {
            float y = ChartMath.MapY(value, 0, height, min, max, isLogScale: false);
            double back = ChartMath.MapYToPrice(y, height, min, max, isLog: false);
            Assert.Equal(value, back, 4);
        }
    }

    // ── MapY (log) ───────────────────────────────────────────────────────────

    [Fact]
    public void MapY_Log_MaxMapsToTop_MinMapsToBottom()
    {
        Assert.Equal(0f, ChartMath.MapY(1000, 0, 600, min: 1, max: 1000, isLogScale: true), 2);
        Assert.Equal(600f, ChartMath.MapY(1, 0, 600, min: 1, max: 1000, isLogScale: true), 2);
    }

    [Fact]
    public void MapY_Log_GeometricMidpoint_IsPaneCentre()
    {
        // On a log axis the geometric mean sqrt(min*max) sits at the centre.
        // sqrt(1 * 1000) ≈ 31.62 → centre of a 0..600 pane = 300.
        double geoMid = Math.Sqrt(1.0 * 1000.0);
        Assert.Equal(300f, ChartMath.MapY(geoMid, 0, 600, min: 1, max: 1000, isLogScale: true), 1);
    }

    [Fact]
    public void MapY_Log_RoundTrips_Through_MapYToPrice()
    {
        const float height = 720f;
        const double min = 0.01, max = 100_000;
        foreach (double value in new[] { 0.05, 1.0, 500.0, 42_000.0 })
        {
            float y = ChartMath.MapY(value, 0, height, min, max, isLogScale: true);
            double back = ChartMath.MapYToPrice(y, height, min, max, isLog: true);
            // MapY returns float, so large magnitudes lose absolute precision on
            // the round-trip; assert relative closeness (<0.01%) instead.
            Assert.True(Math.Abs(back - value) <= Math.Abs(value) * 1e-4,
                $"expected {value}, got {back}");
        }
    }

    [Fact]
    public void MapY_Log_NonPositiveValueAndBounds_DoNotProduceNaN()
    {
        // Zero/negative price or bounds are clamped to a tiny positive so the log
        // math never yields NaN (bad-data resilience).
        float y = ChartMath.MapY(0, 0, 600, min: 0, max: 0, isLogScale: true);
        Assert.False(float.IsNaN(y));

        float y2 = ChartMath.MapY(-5, 0, 600, min: -10, max: 100, isLogScale: true);
        Assert.False(float.IsNaN(y2));
    }

    // ── GetSeriesRange ───────────────────────────────────────────────────────

    private static ChartSeries MakeSeries(string id, string pane, params (string name, double[] data)[] comps)
    {
        var config = new SeriesConfig { Id = id, Name = id, Pane = pane };
        var buffer = new SeriesDataBuffer { SeriesId = id };
        foreach (var (name, data) in comps)
        {
            config.Components.Add(new ComponentConfig { Name = name });
            buffer.ComponentData[name] = data;
        }
        return new ChartSeries(config, buffer);
    }

    [Fact]
    public void GetSeriesRange_MainPane_AlwaysReturnsSharedGlobalRange()
    {
        // Primary price/candle series ignore their own component data and use the
        // shared viewport range so every overlay in Main shares one Y axis.
        var series = MakeSeries("price", "Main", ("Line", new[] { 1.0, 2.0, 3.0 }));
        var range = ChartMath.GetSeriesRange(series, 0, 3, (500.0, 900.0));
        Assert.Equal((500.0, 900.0), range);
    }

    [Fact]
    public void GetSeriesRange_SubPane_ComputesMinMax_WithTenPercentBuffer()
    {
        // Non-Main pane derives its own range from component data within the
        // viewport, plus a 10% margin top and bottom for legibility.
        var series = MakeSeries("rsi", "sub", ("Line", new[] { 30.0, 50.0, 70.0 }));
        var (min, max) = ChartMath.GetSeriesRange(series, 0, 3, (0.0, 100.0));

        double buffer = (70.0 - 30.0) * 0.1; // = 4
        Assert.Equal(30.0 - buffer, min, 6);
        Assert.Equal(70.0 + buffer, max, 6);
    }

    [Fact]
    public void GetSeriesRange_SubPane_SkipsNaN_AndSpansMultipleComponents()
    {
        var series = MakeSeries("multi", "sub",
            ("A", new[] { 10.0, double.NaN, 20.0 }),
            ("B", new[] { 5.0, 15.0, double.NaN }));
        var (min, max) = ChartMath.GetSeriesRange(series, 0, 3, (0.0, 1.0));

        // Real min = 5 (B[0]), real max = 20 (A[2]); NaNs ignored.
        double buffer = (20.0 - 5.0) * 0.1;
        Assert.Equal(5.0 - buffer, min, 6);
        Assert.Equal(20.0 + buffer, max, 6);
    }

    [Fact]
    public void GetSeriesRange_SubPane_NoData_FallsBackTo0To100()
    {
        var series = MakeSeries("empty", "sub", ("Line", Array.Empty<double>()));
        Assert.Equal((0.0, 100.0), ChartMath.GetSeriesRange(series, 0, 5, (10.0, 20.0)));
    }

    [Fact]
    public void GetSeriesRange_SubPane_AllNaN_FallsBackTo0To100()
    {
        var series = MakeSeries("nan", "sub", ("Line", new[] { double.NaN, double.NaN }));
        Assert.Equal((0.0, 100.0), ChartMath.GetSeriesRange(series, 0, 2, (10.0, 20.0)));
    }

    [Fact]
    public void GetSeriesRange_SubPane_FlatValue_ExpandsByOne()
    {
        // When every value is identical the range would be zero-height; the code
        // widens it by ±1 (before the 10% buffer) so the line is visible.
        var series = MakeSeries("flat", "sub", ("Line", new[] { 42.0, 42.0, 42.0 }));
        var (min, max) = ChartMath.GetSeriesRange(series, 0, 3, (0.0, 1.0));

        // min/max become 41/43, then buffer = (43-41)*0.1 = 0.2.
        Assert.Equal(41.0 - 0.2, min, 6);
        Assert.Equal(43.0 + 0.2, max, 6);
    }

    [Fact]
    public void GetSeriesRange_SubPane_HonoursViewportWindow()
    {
        // Only the [start, start+length) slice contributes to the range.
        var series = MakeSeries("win", "sub", ("Line", new[] { 100.0, 1.0, 2.0, 3.0, 999.0 }));
        var (min, max) = ChartMath.GetSeriesRange(series, 1, 3, (0.0, 1.0)); // sees 1,2,3

        double buffer = (3.0 - 1.0) * 0.1;
        Assert.Equal(1.0 - buffer, min, 6);
        Assert.Equal(3.0 + buffer, max, 6);
    }

    // ── GetPointValue ─────────────────────────────────────────────────────────

    [Fact]
    public void GetPointValue_UsesSnapshot_WhenProvided()
    {
        var series = MakeSeries("s", "sub", ("Line", new[] { 1.0, 2.0, 3.0 }));
        var snapshot = new[] { 10.0, 20.0, 30.0 };
        double v = ChartMath.GetPointValue(series, default, componentIndex: 0, dataIndex: 1, snapshot);
        Assert.Equal(20.0, v);
    }

    [Fact]
    public void GetPointValue_FallsBackToLiveComponentData_WhenNoSnapshot()
    {
        var series = MakeSeries("s", "sub", ("Line", new[] { 1.0, 2.0, 3.0 }));
        double v = ChartMath.GetPointValue(series, default, componentIndex: 0, dataIndex: 2);
        Assert.Equal(3.0, v);
    }

    [Fact]
    public void GetPointValue_ComponentIndexOutOfRange_ReturnsNaN()
    {
        var series = MakeSeries("s", "sub", ("Line", new[] { 1.0 }));
        Assert.True(double.IsNaN(ChartMath.GetPointValue(series, default, componentIndex: 5, dataIndex: 0)));
        Assert.True(double.IsNaN(ChartMath.GetPointValue(series, default, componentIndex: -1, dataIndex: 0)));
    }

    [Theory]
    [InlineData("Open", 100.0)]
    [InlineData("High", 110.0)]
    [InlineData("upper_wick", 110.0)]
    [InlineData("Upper Wick", 110.0)]
    [InlineData("Low", 90.0)]
    [InlineData("lower_wick", 90.0)]
    [InlineData("Lower Wick", 90.0)]
    [InlineData("Close", 105.0)]
    [InlineData("body", 105.0)]
    [InlineData("line", 105.0)]
    [InlineData("Candle Body", 105.0)]
    [InlineData("Volume", 5000.0)]
    [InlineData("SomethingUnknown", 105.0)] // default → Close
    public void GetPointValue_PriceSeries_MapsVirtualComponents_ToOhlcv(string compName, double expected)
    {
        // Primary "price" series has virtual components resolved from the OHLCV bar
        // rather than a backing array — covering both new snake_case and legacy names.
        var config = new SeriesConfig { Id = "price", Name = "price", Pane = "Main" };
        config.Components.Add(new ComponentConfig { Name = compName });
        var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "price" });

        var bar = new Ohlcv(DateTime.UtcNow, 100, 110, 90, 105, 5000);
        double v = ChartMath.GetPointValue(series, bar, componentIndex: 0, dataIndex: 0);
        Assert.Equal(expected, v);
    }

    [Fact]
    public void GetPointValue_NonPriceSeries_WithNoBackingData_ReturnsNaN()
    {
        // A component that has no snapshot, no live data, and is NOT a price/candle
        // series has nothing to resolve → NaN (rather than silently returning Close).
        var config = new SeriesConfig { Id = "rsi", Name = "rsi", Pane = "sub" };
        config.Components.Add(new ComponentConfig { Name = "Ghost" });
        var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "rsi" });

        double v = ChartMath.GetPointValue(series, new Ohlcv(DateTime.UtcNow, 1, 2, 3, 4, 5),
            componentIndex: 0, dataIndex: 0);
        Assert.True(double.IsNaN(v));
    }

    // ── CalculateHeikinAshi ──────────────────────────────────────────────────

    [Fact]
    public void CalculateHeikinAshi_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ChartMath.CalculateHeikinAshi(new List<Ohlcv>()));
    }

    [Fact]
    public void CalculateHeikinAshi_FirstBar_UsesSeededOpen_AndAveragedClose()
    {
        var input = new List<Ohlcv> { new(DateTime.UtcNow, 100, 120, 80, 110, 1) };
        var ha = ChartMath.CalculateHeikinAshi(input);

        var bar = ha.Single();
        // Close = (O+H+L+C)/4 = (100+120+80+110)/4 = 102.5
        Assert.Equal(102.5, bar.Close, 6);
        // First Open seeds prevOpen=100, prevClose=110 → (100+110)/2 = 105
        Assert.Equal(105.0, bar.Open, 6);
        // High = Max(H, open, close) = Max(120, 105, 102.5) = 120
        Assert.Equal(120.0, bar.High, 6);
        // Low = Min(L, open, close) = Min(80, 105, 102.5) = 80
        Assert.Equal(80.0, bar.Low, 6);
    }

    [Fact]
    public void CalculateHeikinAshi_SecondBar_DerivesOpenFromFirstHaBar()
    {
        var d0 = new Ohlcv(DateTime.UtcNow, 100, 120, 80, 110, 1);
        var d1 = new Ohlcv(DateTime.UtcNow.AddMinutes(1), 111, 130, 108, 128, 1);
        var ha = ChartMath.CalculateHeikinAshi(new List<Ohlcv> { d0, d1 });

        // From bar 0: haOpen0 = 105, haClose0 = 102.5.
        // Bar 1 open = (haOpen0 + haClose0)/2 = (105 + 102.5)/2 = 103.75
        Assert.Equal(103.75, ha[1].Open, 6);
        // Bar 1 close = (111+130+108+128)/4 = 119.25
        Assert.Equal(119.25, ha[1].Close, 6);
        // High = Max(130, 103.75, 119.25) = 130
        Assert.Equal(130.0, ha[1].High, 6);
        // Low = Min(108, 103.75, 119.25) = 103.75
        Assert.Equal(103.75, ha[1].Low, 6);
    }

    [Fact]
    public void CalculateHeikinAshi_PreservesDateAndVolume()
    {
        var date = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var input = new List<Ohlcv> { new(date, 10, 12, 9, 11, 777) };
        var ha = ChartMath.CalculateHeikinAshi(input).Single();
        Assert.Equal(date, ha.Date);
        Assert.Equal(777, ha.Volume, 6);
    }
}
