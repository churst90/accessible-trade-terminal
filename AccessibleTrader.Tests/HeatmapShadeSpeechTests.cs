using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The heatmap says how strong a cell is, not a colour name it does not have.</b> The heatmap
/// is one colour at varying opacity, so the words are "Dark cell.", "Medium cell." and
/// "Light cell." from the alpha <see cref="HeatmapRenderer"/> draws — top, middle and bottom third
/// of the range. An empty cell says nothing. The arrival/change rule is the line rule, in both
/// directions: between prices in one column and between bars.
/// </summary>
public sealed class HeatmapShadeSpeechTests
{
    private static List<Ohlcv> Bars(int n)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddDays(i), 100, 101, 99, 100, 1))
            .ToList();
    }

    private static ProfileBin Bin(double price, double volume) => new()
    {
        PriceLow = price, PriceHigh = price + 5, TotalVolume = volume,
        TpoPeriodCount = 0, IsPOC = false, IsValueArea = false,
    };

    private static ChartSeries Heatmap()
    {
        var cfg = new SeriesConfig { Id = "hm", Name = "hm", Pane = "Main", IsVisible = true, Volume = 1f };
        cfg.Components.Add(new ComponentConfig { Name = "hm", DisplayType = ComponentDisplayType.Heatmap, IsVisible = true });
        var buf = new SeriesDataBuffer
        {
            SeriesId = "hm",
            HeatmapData = new List<List<ProfileBin>>
            {
                new() { Bin(100, 10), Bin(105, 50), Bin(110, 90), Bin(115, 0) },  // light, medium, dark, empty
                new() { Bin(100, 10), Bin(105, 50), Bin(110, 90), Bin(115, 0) },
            },
        };
        return new ChartSeries(cfg, buf);
    }

    private static WorkspaceState State(ChartSeries series, bool describe, int dataIndex, int binIndex) =>
        WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Bars(2)),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedBinIndex = binIndex,
            CurrentDataIndex = dataIndex,
            SpeakTimestamps = false,
            DescribeSignalLooks = describe,
        };

    private static string Say(SpeechFormatter formatter, ChartSeries series, bool describe,
        int dataIndex, int binIndex, bool isXMove)
    {
        var state = State(series, describe, dataIndex, binIndex);
        return formatter.FormatHeatmapFeedback(state, isXMove, !isXMove, series,
            dataIndex, binIndex, "", cursorDataIndex: dataIndex);
    }

    [Fact]
    public void The_three_thirds_are_named_and_an_empty_cell_is_silent()
    {
        var series = Heatmap();
        var formatter = new SpeechFormatter();

        Assert.Contains("Light cell.", Say(formatter, series, true, 0, 0, isXMove: false));
        Assert.Contains("Medium cell.", Say(formatter, series, true, 0, 1, isXMove: false));
        Assert.Contains("Dark cell.", Say(formatter, series, true, 0, 2, isXMove: false));
        string empty = Say(formatter, series, true, 0, 3, isXMove: false);
        Assert.DoesNotContain("cell.", empty);
        Assert.Contains("percent.", empty);   // the rest of the reading is unchanged
    }

    [Fact]
    public void A_shade_is_said_on_arrival_and_only_when_the_word_changes_going_down()
    {
        var series = Heatmap();
        var formatter = new SpeechFormatter();
        string first = Say(formatter, series, true, 0, 0, isXMove: false);
        string again = Say(formatter, series, true, 0, 0, isXMove: false);
        string medium = Say(formatter, series, true, 0, 1, isXMove: false);
        string dark = Say(formatter, series, true, 0, 2, isXMove: false);

        Assert.Contains("Light cell.", first);
        Assert.DoesNotContain("cell.", again);
        Assert.Contains("Medium cell.", medium);
        Assert.Contains("Dark cell.", dark);
    }

    [Fact]
    public void A_shade_is_said_going_back_up_the_column_too()
    {
        var series = Heatmap();
        var formatter = new SpeechFormatter();
        Say(formatter, series, true, 0, 2, isXMove: false);   // arrive dark
        string medium = Say(formatter, series, true, 0, 1, isXMove: false);
        string light = Say(formatter, series, true, 0, 0, isXMove: false);

        Assert.Contains("Medium cell.", medium);
        Assert.Contains("Light cell.", light);
    }

    [Fact]
    public void Moving_between_bars_says_a_new_shade_even_when_the_bin_is_the_same()
    {
        var series = Heatmap();
        var formatter = new SpeechFormatter();
        Say(formatter, series, true, 0, 2, isXMove: false);   // dark in column 0
        string otherBar = Say(formatter, series, true, 1, 0, isXMove: true);

        Assert.Contains("Light cell.", otherBar);
    }

    [Fact]
    public void An_empty_cell_breaks_the_run_so_the_next_shade_is_said()
    {
        var series = Heatmap();
        var formatter = new SpeechFormatter();
        Say(formatter, series, true, 0, 0, isXMove: false);   // light
        string empty = Say(formatter, series, true, 0, 3, isXMove: false);
        string lightAgain = Say(formatter, series, true, 0, 0, isXMove: false);

        Assert.DoesNotContain("cell.", empty);
        Assert.Contains("Light cell.", lightAgain);
    }

    [Fact]
    public void With_looks_off_the_heatmap_says_no_shade()
    {
        var series = Heatmap();
        var formatter = new SpeechFormatter();
        Assert.DoesNotContain("cell.", Say(formatter, series, false, 0, 0, isXMove: false));
        Assert.DoesNotContain("cell.", Say(formatter, series, false, 0, 1, isXMove: false));
    }

    [Fact]
    public void The_shade_word_is_the_alpha_the_renderer_paints()
    {
        var series = Heatmap();
        Assert.Equal("light", HeatmapRenderer.ShadeWord(25));
        Assert.Equal("medium", HeatmapRenderer.ShadeWord(127));
        Assert.Equal("dark", HeatmapRenderer.ShadeWord(229));
        Assert.Equal("", HeatmapRenderer.ShadeWord(0));
    }
}
