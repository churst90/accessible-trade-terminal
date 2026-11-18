using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A volume-profile row says its split</b>: "…, 62 percent buying.", "…, 58 percent selling.",
/// "…, even." within half a point, and nothing at all when the row has no split (TPO, zero
/// volume). Under the look setting the row's drawn colour follows the line rule.
/// </summary>
public sealed class ProfileSplitSpeechTests
{
    private static IThemeService FixedTheme()
    {
        var theme = BaseThemeResolver.Resolve(ThemeType.Classic) with
        {
            CandleBullishBody = new SKColor(0x00, 0xE6, 0x76),
            CandleBearishBody = new SKColor(0xFF, 0x17, 0x44),
        };
        var service = Substitute.For<IThemeService>();
        service.Current.Returns(theme);
        return service;
    }

    private static List<Ohlcv> Bars(int n)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddDays(i), 100, 101, 99, 100, 1))
            .ToList();
    }

    private static ProfileBin Bin(double low, double total, double buy, double sell,
        bool poc = false, bool valueArea = false, bool tpo = false)
        => new()
        {
            PriceLow = low, PriceHigh = low + 1, TotalVolume = total,
            BuyVolume = buy, SellVolume = sell,
            TpoPeriodCount = tpo ? 1 : 0, IsPOC = poc, IsValueArea = valueArea,
            TpoLetters = tpo ? ImmutableList.Create('A') : ImmutableList<char>.Empty,
        };

    private static ChartSeries Profile(params ProfileBin[] bins)
    {
        var cfg = new SeriesConfig { Id = "vp", Name = "vp", Pane = "Main", IsVisible = true };
        var series = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "vp" }) { IsProfile = true };
        series.ProfileBins = bins.ToList();
        return series;
    }

    // 100 volume in the row under test: 62 buying / 38 selling.
    private static ChartSeries Buying() => Profile(
        Bin(100, 100, 60, 40, valueArea: true),
        Bin(101, 100, 62, 38, poc: true),
        Bin(102, 100, 40, 60, valueArea: true));

    private static WorkspaceState State(ChartSeries series, bool describe) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Bars(3)),
        ActiveSeries = ImmutableList.Create(series),
        FocusedSeriesId = series.Id,
        CurrentDataIndex = 1,
        SpeakTimestamps = false,
        TimestampReadLocation = "None",
        ReadColumnHeaders = false,
        SpeechOrder = "ValueOnly",
        DescribeSignalLooks = describe,
    };

    private static string Say(SpeechFormatter formatter, ChartSeries series, bool describe, int bin)
        => formatter.FormatProfileFeedback(State(series, describe), isXMove: false, isYMove: true,
            series, bin, "");

    [Fact]
    public void A_row_says_percent_buying()
    {
        var series = Buying();
        string said = new SpeechFormatter().FormatProfileFeedback(State(series, false), false, true, series, 1, "");
        Assert.Contains(", 62 percent buying.", said);
        Assert.DoesNotContain("percent selling", said);
    }

    [Fact]
    public void A_row_says_percent_selling()
    {
        var series = Profile(Bin(100, 100, 42, 58, poc: true));
        string said = new SpeechFormatter().FormatProfileFeedback(State(series, false), false, true, series, 0, "");
        Assert.Contains(", 58 percent selling.", said);
        Assert.DoesNotContain("percent buying", said);
    }

    [Theory]
    [InlineData(50, 50)]     // exact
    [InlineData(49.6, 50.4)] // within half a point
    [InlineData(50.4, 49.6)]
    public void A_split_within_half_a_point_says_even(double buy, double sell)
    {
        var series = Profile(Bin(100, 100, buy, sell, poc: true));
        string said = new SpeechFormatter().FormatProfileFeedback(State(series, false), false, true, series, 0, "");
        Assert.EndsWith(", even.", said);
    }

    [Fact]
    public void A_tpo_row_says_no_split()
    {
        var series = Profile(Bin(100, 100, 0, 0, poc: true, valueArea: true, tpo: true));
        string said = new SpeechFormatter().FormatProfileFeedback(State(series, false), false, true, series, 0, "");
        Assert.Contains("letters A", said);
        Assert.DoesNotContain("percent buying", said);
        Assert.DoesNotContain("percent selling", said);
        Assert.DoesNotContain("even", said);
    }

    [Fact]
    public void A_row_with_no_volume_says_no_split()
    {
        var series = Profile(Bin(100, 0, 0, 0, poc: true));
        string said = new SpeechFormatter().FormatProfileFeedback(State(series, false), false, true, series, 0, "");
        Assert.DoesNotContain("percent buying", said);
        Assert.DoesNotContain("percent selling", said);
        Assert.DoesNotContain("even", said);
    }

    [Fact]
    public void The_split_is_always_on_even_with_looks_off()
    {
        var series = Buying();
        string said = Say(new SpeechFormatter(), series, describe: false, bin: 1);
        Assert.Contains(", 62 percent buying.", said);
        Assert.DoesNotContain("row.", said);
    }

    [Fact]
    public void With_looks_on_a_split_row_says_its_two_colours_and_only_when_they_change()
    {
        var series = Buying();
        var themeService = FixedTheme();
        var formatter = new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, themeService: themeService);
        var (buy, sell) = ProfileRenderLayer.SplitColors(themeService.Current);
        string splitLook = MarkerLook.Capitalise(
            $"{MarkerLook.ColorName(buy)} and {MarkerLook.ColorName(sell)} row") + ".";
        string pocLook = MarkerLook.Capitalise(
            MarkerLook.ColorName(ProfileRenderLayer.PocColor(themeService.Current)) + " row") + ".";

        string first = Say(formatter, series, describe: true, bin: 0);   // split
        string second = Say(formatter, series, describe: true, bin: 2);  // split: same colours
        string poc = Say(formatter, series, describe: true, bin: 1);     // the POC row

        Assert.Contains(splitLook, first);
        Assert.DoesNotContain("row.", second);
        Assert.Contains(pocLook, poc);
    }

    [Fact]
    public void With_looks_off_a_row_says_no_colour()
    {
        var series = Buying();
        string said = Say(new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, themeService: FixedTheme()),
            series, describe: false, bin: 1);
        Assert.DoesNotContain("row.", said);
        Assert.DoesNotContain(" and ", said.Split("contracts")[^1]);
    }
}
