using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// Phase B prerequisite: pins the shared pointer↔chart coordinate mapping in
/// ChartMath before mouse features (click-select, crosshair, context-menu bar
/// resolution) are built on top of it. These functions were previously private
/// duplicates inside DrawingInteractionManager; one regression here silently
/// skews every mouse feature at once.
/// </summary>
public class ChartMathPointerMappingTests
{
    // ── MapXToIndex ──────────────────────────────────────────────────────────

    [Fact]
    public void MapXToIndex_LeftEdge_IsViewportStart()
    {
        Assert.Equal(100, ChartMath.MapXToIndex(0, 1280, startIndex: 100, length: 120));
    }

    [Fact]
    public void MapXToIndex_RightEdge_IsStartPlusLengthMinusOne()
    {
        Assert.Equal(100 + 119, ChartMath.MapXToIndex(1280, 1280, startIndex: 100, length: 120));
    }

    [Fact]
    public void MapXToIndex_Centre_IsMidViewport()
    {
        // 50% across a 121-bar viewport = start + 60 exactly (odd length avoids
        // banker's-rounding ambiguity at .5).
        Assert.Equal(100 + 60, ChartMath.MapXToIndex(640, 1280, startIndex: 100, length: 121));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void MapXToIndex_DegenerateWidth_ReturnsStartInsteadOfDividingByZero(double width)
    {
        Assert.Equal(100, ChartMath.MapXToIndex(640, width, startIndex: 100, length: 120));
    }

    // ── MapYToPrice / PriceToScreenY (linear) ───────────────────────────────

    [Fact]
    public void MapYToPrice_TopOfPane_IsMax_BottomIsMin()
    {
        Assert.Equal(200, ChartMath.MapYToPrice(0, 720, min: 100, max: 200, isLog: false), 6);
        Assert.Equal(100, ChartMath.MapYToPrice(720, 720, min: 100, max: 200, isLog: false), 6);
    }

    [Theory]
    [InlineData(125.0)]
    [InlineData(100.0)]
    [InlineData(199.99)]
    public void Linear_PriceToScreenY_RoundTrips_Through_MapYToPrice(double price)
    {
        double y = ChartMath.PriceToScreenY(price, 720, min: 100, max: 200, isLog: false);
        double back = ChartMath.MapYToPrice(y, 720, min: 100, max: 200, isLog: false);
        Assert.Equal(price, back, 6);
    }

    // ── Log scale ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.05)]
    [InlineData(1.0)]
    [InlineData(42_000.0)]
    public void Log_PriceToScreenY_RoundTrips_Through_MapYToPrice(double price)
    {
        double y = ChartMath.PriceToScreenY(price, 720, min: 0.01, max: 100_000, isLog: true);
        double back = ChartMath.MapYToPrice(y, 720, min: 0.01, max: 100_000, isLog: true);
        Assert.Equal(price, back, 4);
    }

    [Fact]
    public void Log_NonPositiveMin_IsForcedPositive_NotNaN()
    {
        // A viewport range dipping to zero (bad data) must not poison the log math.
        double p = ChartMath.MapYToPrice(360, 720, min: 0, max: 100, isLog: true);
        Assert.False(double.IsNaN(p));
        Assert.True(p > 0);
    }

    [Fact]
    public void DegenerateRange_MaxNotAboveMin_DoesNotThrowOrNaN()
    {
        double linear = ChartMath.MapYToPrice(360, 720, min: 50, max: 50, isLog: false);
        Assert.False(double.IsNaN(linear));

        double logY = ChartMath.PriceToScreenY(50, 720, min: 50, max: 50, isLog: true);
        Assert.False(double.IsNaN(logY));

        Assert.Equal(0, ChartMath.PriceToScreenY(50, 720, min: 60, max: 50, isLog: false));
    }

    [Fact]
    public void MapYToPrice_DegenerateHeight_ReturnsMin()
    {
        Assert.Equal(100, ChartMath.MapYToPrice(10, 0, min: 100, max: 200, isLog: false));
    }
}
