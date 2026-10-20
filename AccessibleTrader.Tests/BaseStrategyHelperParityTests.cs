using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Strategies.BuiltIn;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Tests;

/// <summary>
/// Strategy scripts that derive from <see cref="BaseStrategy"/> must get the same
/// SMA, RSI and Bollinger Bands values as the shared strategy indicator cache.
/// </summary>
public sealed class BaseStrategyHelperParityTests
{
    private static readonly DateTime Start = new(2021, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<Ohlcv> Series(int count, int seed = 7)
    {
        var rng = new Random(seed);
        var bars = new List<Ohlcv>(count);
        double price = 100;
        for (int i = 0; i < count; i++)
        {
            price += (rng.NextDouble() - 0.48) * 3.0;
            bars.Add(new Ohlcv(Start.AddHours(i), price, price + 1, price - 1, price, 1000));
        }
        return bars;
    }

    private static double[] Closes(IReadOnlyList<Ohlcv> bars) => bars.Select(bar => bar.Close).ToArray();

    [Theory]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(21)]
    public void RsiMatchesTheLibraryTheChartDrawsFrom(int period)
    {
        var bars = Series(300);
        double expected = IndicatorMath.Rsi(Closes(bars), period)[^1];

        Assert.Equal(expected, HelperProbe.RsiValue(bars, period), precision: 9);
    }

    [Fact]
    public void TheRsiTestCanTellWilderFromCutler()
    {
        // Vacuity check: prove that this random-walk fixture separates the old simple-mean
        // formula from Wilder's RSI by enough to make the parity assertion meaningful.
        var bars = Series(300);
        double wilder = IndicatorMath.Rsi(Closes(bars), 14)[^1];
        double cutler = CutlerRsi(bars, 14);

        Assert.True(Math.Abs(wilder - cutler) > 0.5,
            $"fixture cannot separate the two RSIs (Wilder {wilder:F3} vs Cutler {cutler:F3})");
    }

    /// <summary>The old helper's plain-mean RSI, kept here to prove the fixture distinguishes it.</summary>
    private static double CutlerRsi(IReadOnlyList<Ohlcv> data, int period)
    {
        int count = data.Count;
        double gain = 0, loss = 0;
        for (int i = count - period; i < count; i++)
        {
            double change = data[i].Close - data[i - 1].Close;
            if (change > 0) gain += change;
            else loss -= change;
        }

        double avgGain = gain / period, avgLoss = loss / period;
        return avgLoss == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + avgGain / avgLoss);
    }

    [Fact]
    public void SmaMatchesTheStrategyIndicatorCache()
    {
        // This is expected to pass before the fix: the helper and cache bodies already match.
        var bars = Series(300);
        var cache = new StrategyIndicatorCache();

        Assert.Equal(cache.GetSma(bars, 20), HelperProbe.SmaValue(bars, 20), precision: 9);
    }

    [Fact]
    public void BollingerBandsMatchTheStrategyIndicatorCache()
    {
        // This is expected to pass before the fix: both helpers use population standard deviation
        // (variance divided by period) over the same closes.
        var bars = Series(300);
        var cache = new StrategyIndicatorCache();
        var expected = cache.GetBollingerBands(bars, 20, 2.0);
        var actual = HelperProbe.BollingerBandsValue(bars, 20, 2.0);

        Assert.Equal(expected.Middle, actual.Middle, precision: 9);
        Assert.Equal(expected.Upper, actual.Upper, precision: 9);
        Assert.Equal(expected.Lower, actual.Lower, precision: 9);
    }

    [Fact]
    public void RsiIsNaNBeforePeriodPlusOneBarsAreAvailable()
    {
        Assert.True(double.IsNaN(HelperProbe.RsiValue(Series(14), 14)));
    }

    private sealed class HelperProbe : BaseStrategy
    {
        public static double SmaValue(IReadOnlyList<Ohlcv> history, int period) => Sma(history, period);

        public static double RsiValue(IReadOnlyList<Ohlcv> history, int period) => Rsi(history, period);

        public static (double Middle, double Upper, double Lower) BollingerBandsValue(
            IReadOnlyList<Ohlcv> history, int period, double deviations) =>
            BollingerBands(history, period, deviations);

        public override string Id => "helper-probe";
        public override string Name => "Helper probe";
        public override string Description => "Test probe";
        public override StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
        public override IReadOnlyList<StrategyParameter> Parameters { get; } = Array.Empty<StrategyParameter>();

        protected override StrategySignal? ComputeSignal(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state) => null;
    }
}
