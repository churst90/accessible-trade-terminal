using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A volume-profile row is split into buying and selling as TradingView does without
/// lower-timeframe data</b> (Cody, 2026-11-18). A bar's volume is buying when it closed above its
/// open, selling when it closed below; a doji is compared with the previous bar's close, and an
/// equal close is half each. The split only divides the volume the profile already counted in the
/// close's bin: TotalVolume, the POC, the value area, the levels and the backtests stay identical.
/// TPO/market-profile rows keep no split.
/// </summary>
public sealed class ProfileSplitTests
{
    private static Ohlcv Bar(DateTime start, int day, double open, double close, double volume) =>
        new(start.AddDays(day), open, Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, volume);

    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ProfileBin OneBin(params Ohlcv[] bars)
        => new ProfileService().CalculateVolumeProfile(bars, binCount: 1).Single();

    [Fact]
    public void An_up_bar_is_all_buying()
    {
        var bin = OneBin(Bar(Start, 0, 100, 101, 10), Bar(Start, 1, 101, 102, 20));
        Assert.Equal(30, bin.BuyVolume);
        Assert.Equal(0, bin.SellVolume);
        Assert.Equal(bin.TotalVolume, bin.BuyVolume + bin.SellVolume);
    }

    [Fact]
    public void A_down_bar_is_all_selling()
    {
        var bin = OneBin(Bar(Start, 0, 101, 100, 10), Bar(Start, 1, 100, 99, 20));
        Assert.Equal(0, bin.BuyVolume);
        Assert.Equal(30, bin.SellVolume);
        Assert.Equal(bin.TotalVolume, bin.BuyVolume + bin.SellVolume);
    }

    [Fact]
    public void A_doji_above_the_previous_close_is_buying()
    {
        var bin = OneBin(Bar(Start, 0, 99, 100, 0), Bar(Start, 1, 105, 105, 10));
        Assert.Equal(10, bin.BuyVolume);
        Assert.Equal(0, bin.SellVolume);
    }

    [Fact]
    public void A_doji_below_the_previous_close_is_selling()
    {
        var bin = OneBin(Bar(Start, 0, 109, 110, 0), Bar(Start, 1, 105, 105, 10));
        Assert.Equal(0, bin.BuyVolume);
        Assert.Equal(10, bin.SellVolume);
    }

    [Fact]
    public void A_doji_equal_to_the_previous_close_is_half_each()
    {
        // First bar closes at 100 (buy 4); the doji also closes at 100, so its 10 is half each.
        var bin = OneBin(Bar(Start, 0, 99, 100, 4), Bar(Start, 1, 100, 100, 10));
        Assert.Equal(9, bin.BuyVolume);
        Assert.Equal(5, bin.SellVolume);
        Assert.Equal(bin.TotalVolume, bin.BuyVolume + bin.SellVolume);
    }

    [Fact]
    public void A_first_bar_doji_has_no_previous_close_and_is_half_each()
    {
        var bin = OneBin(Bar(Start, 0, 100, 100, 10));
        Assert.Equal(5, bin.BuyVolume);
        Assert.Equal(5, bin.SellVolume);
        Assert.Equal(bin.TotalVolume, bin.BuyVolume + bin.SellVolume);
    }

    // ── The guard for the fields that must not move ────────────────────────────

    /// <summary>The fixed data set, and the exact answers today's profile code gives for it
    /// (captured before the split was added). Do not recompute these from the new code.</summary>
    private static List<Ohlcv> BaselineBars() => new()
    {
        new(Start, 100, 101, 99, 100.5, 1000),
        new(Start.AddDays(1), 100.5, 103, 100, 102, 2000),
        new(Start.AddDays(2), 102, 102, 98, 99, 1500),
        new(Start.AddDays(3), 99, 100, 97, 98, 1200),
        new(Start.AddDays(4), 98, 99, 96, 98, 800),
        new(Start.AddDays(5), 98, 101, 97, 100, 2500),
        new(Start.AddDays(6), 100, 100.5, 95, 96, 3000),
        new(Start.AddDays(7), 96, 98, 94, 97, 700),
        new(Start.AddDays(8), 97, 97, 93, 93, 1800),
        new(Start.AddDays(9), 93, 96, 92, 95, 1100),
        new(Start.AddDays(10), 95, 95, 91, 91, 900),
        new(Start.AddDays(11), 91, 94, 90, 94, 2100),
    };

    private static readonly double[] VolumeTotals = { 900, 3900, 4100, 2700, 5000, 2000 };
    private static readonly double[] VolumeTpoCounts = { 1, 2, 2, 3, 3, 1 };
    private static readonly int[] VolumeValueArea = { 1, 2, 3, 4 };
    private const int VolumePoc = 4;

    [Fact]
    public void Volume_TotalVolume_Poc_and_value_area_are_unchanged()
    {
        var bins = new ProfileService().CalculateVolumeProfile(BaselineBars(), 6);
        Assert.Equal(VolumeTotals, bins.Select(b => b.TotalVolume));
        Assert.Equal(VolumePoc, bins.FindIndex(b => b.IsPOC));
        Assert.Equal(VolumeValueArea, bins.Select((b, i) => (b, i)).Where(t => t.b.IsValueArea).Select(t => t.i));
        Assert.Equal(VolumeTpoCounts, bins.Select(b => b.TpoPeriodCount));
    }

    [Fact]
    public void Every_volume_profile_bin_splits_exactly_its_TotalVolume()
    {
        var bins = new ProfileService().CalculateVolumeProfile(BaselineBars(), 6);
        Assert.All(bins, b => Assert.Equal(b.TotalVolume, b.BuyVolume + b.SellVolume));
        Assert.Equal(bins.Sum(b => b.TotalVolume), bins.Sum(b => b.BuyVolume) + bins.Sum(b => b.SellVolume));
    }

    [Fact]
    public void The_split_does_not_move_a_bar_to_a_different_bin()
    {
        // All the volume of an up bar still belongs to the close's bin, even though its open was
        // much lower: the split divides that bin's volume, it does not spread it between bins.
        var bars = new[]
        {
            new Ohlcv(Start, 90, 101, 89, 100, 1000),       // closes at 100, opened at 90
            new Ohlcv(Start.AddDays(1), 90, 91, 89, 90, 1),
        };
        var bins = new ProfileService().CalculateVolumeProfile(bars, 2);
        Assert.Equal(1000, bins.Single(b => b.PriceHigh > 99.5).BuyVolume);
        Assert.Equal(0, bins.Single(b => b.PriceHigh < 99.5).BuyVolume);
    }

    // ── TPO is untouched ───────────────────────────────────────────────────────

    private static readonly double[] TpoTotals = { 4100, 6600, 8300, 11500, 12000, 7000 };
    private static readonly double[] TpoPeriodCounts = { 3, 5, 6, 7, 7, 4 };
    private static readonly int[] TpoValueArea = { 1, 2, 3, 4 };
    private const int TpoPoc = 3;

    [Fact]
    public void Market_profile_is_unchanged_and_has_no_split()
    {
        var bins = new ProfileService().CalculateMarketProfile(BaselineBars(), 6);
        Assert.Equal(TpoTotals, bins.Select(b => b.TotalVolume));
        Assert.Equal(TpoPoc, bins.FindIndex(b => b.IsPOC));
        Assert.Equal(TpoValueArea, bins.Select((b, i) => (b, i)).Where(t => t.b.IsValueArea).Select(t => t.i));
        Assert.Equal(TpoPeriodCounts, bins.Select(b => b.TpoPeriodCount));
        Assert.All(bins, b =>
        {
            Assert.Equal(0, b.BuyVolume);
            Assert.Equal(0, b.SellVolume);
        });
    }
}
