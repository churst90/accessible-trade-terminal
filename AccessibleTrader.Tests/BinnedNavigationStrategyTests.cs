using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Moving through a volume profile and a liquidity heatmap.</b>
///
/// <para>
/// <see cref="BinnedNavigationStrategy"/> had no tests of its own. The A2j sabotage set
/// (2026-09-19) inverted its Y direction and disabled its X refusal, one at a time, and the
/// whole 7,880-test suite stayed green both times — on the one series in the terminal where the
/// Y axis IS the navigation axis, and where the only thing telling the user which way they moved
/// is the price they hear next.
/// </para>
///
/// <para>
/// The assertions are written in PRICE, not in bin index. The index is an implementation detail
/// of <c>ProfileService</c> (bin 0 is the lowest price, so UP is <c>index + 1</c>, which is why
/// the strategy subtracts a delta that is <c>-1</c> for up); the user's claim is "Up reads a
/// higher price", and that is what must not invert.
/// </para>
/// </summary>
public sealed class BinnedNavigationStrategyTests
{
    /// <summary>Up and Down as <c>NavigationEngine</c> sends them: NAV_COMP_UP is -1.</summary>
    private const int Up = -1;
    private const int Down = 1;

    private static List<Ohlcv> Bars() => Enumerable.Range(0, 40).Select(i => new Ohlcv(
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
        100 + (i % 7), 106 + (i % 7), 94 + (i % 7), 101 + (i % 7), 1000 + (i * 10))).ToList();

    private static ChartSeries Profile(IReadOnlyList<Ohlcv> bars)
    {
        var cfg = new SeriesConfig
        {
            Id = "vp", Name = "Volume Profile", FriendlyName = "Volume Profile",
            IndicatorCode = "VPVR", Pane = "Main", IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Profile", DisplayName = "Profile", DisplayType = ComponentDisplayType.Bar, IsVisible = true,
        });
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id, ProfileBins = new ProfileService().CalculateVolumeProfile(bars) };
        return new ChartSeries(cfg, buf) { IsProfile = true };
    }

    private static ChartSeries Heatmap(IReadOnlyList<Ohlcv> bars)
    {
        var cfg = new SeriesConfig
        {
            Id = "heat", Name = "Liquidity", FriendlyName = "Liquidity heatmap",
            IndicatorCode = "HEATMAP", Pane = "Main", IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Book", DisplayName = "Book", DisplayType = ComponentDisplayType.Heatmap, IsVisible = true,
        });
        var column = Enumerable.Range(0, 5).Select(i => new ProfileBin
        {
            PriceLow = 100 + i, PriceHigh = 101 + i, TotalVolume = 10 + i,
            TpoPeriodCount = 0, IsPOC = false, IsValueArea = false,
        }).ToList();
        var buf = new SeriesDataBuffer
        {
            SeriesId = cfg.Id,
            HeatmapData = bars.Select(_ => column.ToList()).ToList(),
        };
        return new ChartSeries(cfg, buf);
    }

    private static WorkspaceState State(ChartSeries series, IReadOnlyList<Ohlcv> bars, int bin, int dataIndex = 20) =>
        WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            CurrentDataIndex = dataIndex,
            FocusedBinIndex = bin,
        };

    private static double MidOf(ChartSeries profile, int bin)
    {
        var b = profile.ProfileBins![bin];
        return (b.PriceLow + b.PriceHigh) / 2.0;
    }

    // ── Y: the price axis is the navigation axis ──────────────────────────────

    [Fact]
    public void Up_MovesToAHigherPriceBin()
    {
        var bars = Bars();
        var profile = Profile(bars);
        int start = profile.ProfileBins!.Count / 2;

        var result = new BinnedNavigationStrategy().NavigateY(State(profile, bars, start), Up);

        Assert.True(result.Success);
        Assert.True(MidOf(profile, result.NewBinIndex) > MidOf(profile, start),
            $"Up landed on {MidOf(profile, result.NewBinIndex)} from {MidOf(profile, start)}");
    }

    [Fact]
    public void Down_MovesToALowerPriceBin()
    {
        var bars = Bars();
        var profile = Profile(bars);
        int start = profile.ProfileBins!.Count / 2;

        var result = new BinnedNavigationStrategy().NavigateY(State(profile, bars, start), Down);

        Assert.True(result.Success);
        Assert.True(MidOf(profile, result.NewBinIndex) < MidOf(profile, start),
            $"Down landed on {MidOf(profile, result.NewBinIndex)} from {MidOf(profile, start)}");
    }

    /// <summary>
    /// The ends are a refusal, not a wrap: walking off the top of a profile must not reappear at
    /// the bottom, which on an audio-only readout is indistinguishable from a huge price move.
    /// </summary>
    [Fact]
    public void AtTheTopBin_UpRefusesRatherThanWrapping()
    {
        var bars = Bars();
        var profile = Profile(bars);
        int top = profile.ProfileBins!.Count - 1;

        var result = new BinnedNavigationStrategy().NavigateY(State(profile, bars, top), Up);

        Assert.False(result.Success);
    }

    /// <summary>With no bin focused yet, Y starts from the middle of the profile.</summary>
    [Fact]
    public void WithNoBinFocused_TheFirstMoveStartsFromTheMiddle()
    {
        var bars = Bars();
        var profile = Profile(bars);
        int middle = profile.ProfileBins!.Count / 2;

        var result = new BinnedNavigationStrategy().NavigateY(State(profile, bars, bin: -1), Up);

        Assert.True(result.Success);
        Assert.Equal(middle + 1, result.NewBinIndex);
    }

    // ── X: a profile has no time axis, a heatmap does ─────────────────────────

    /// <summary>
    /// A volume profile aggregates across ALL the time on the chart, so left and right have
    /// nothing to move to. The refusal is silent on purpose — the audio engine plays nothing —
    /// but it has to BE a refusal: a cursor that moves while the reading cannot change is a key
    /// that appears to work and does not.
    /// </summary>
    [Fact]
    public void LeftAndRight_DoNotMoveInsideAProfile()
    {
        var bars = Bars();
        var profile = Profile(bars);
        var strategy = new BinnedNavigationStrategy();

        Assert.False(strategy.NavigateX(State(profile, bars, bin: 3), 1).Success);
        Assert.False(strategy.NavigateX(State(profile, bars, bin: 3), -1).Success);
    }

    /// <summary>
    /// The partner, and the reason the refusal is conditional: a heatmap is a profile PER BAR,
    /// so it does have a time axis and right moves along it.
    /// </summary>
    [Fact]
    public void AHeatmapDoesHaveATimeAxis_AndRightMovesAlongIt()
    {
        var bars = Bars();
        var heat = Heatmap(bars);

        var result = new BinnedNavigationStrategy().NavigateX(State(heat, bars, bin: 2, dataIndex: 20), 1);

        Assert.True(result.Success);
        Assert.Equal(21, result.NewIndex);
    }

    /// <summary>A binned series with no bins says so rather than moving silently.</summary>
    [Fact]
    public void WithNoBinsAtAll_YSaysSoRatherThanMovingSilently()
    {
        var bars = Bars();
        var cfg = new SeriesConfig { Id = "vp", Name = "Volume Profile", IndicatorCode = "VPVR", Pane = "Main" };
        cfg.Components.Add(new ComponentConfig { Name = "Profile", DisplayType = ComponentDisplayType.Bar, IsVisible = true });
        var empty = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = cfg.Id }) { IsProfile = true };

        var result = new BinnedNavigationStrategy().NavigateY(State(empty, bars, bin: -1), Up);

        Assert.False(result.Success);
        Assert.Equal(FeedbackType.Error, result.FeedbackType);
    }
}
