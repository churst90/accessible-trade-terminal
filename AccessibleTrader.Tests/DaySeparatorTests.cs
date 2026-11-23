using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Day separators (2026-11-23).</b> Cody: <i>"if day separaters are a feature of other trading
/// platforms, add them as a visual toggle with a default of off"</i>. A dotted line at the first
/// bar of each day on an intraday chart, in the day the axis and the readouts name the bar in
/// (<see cref="DaySeparatorPainter"/>). Settings > Appearance > Draw day separators.
/// </summary>
public class DaySeparatorTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);   // a Monday, winter time

    private static List<Ohlcv> Hourly(int n, DateTime start) => Enumerable.Range(0, n)
        .Select(i => new Ohlcv(start.AddHours(i), 100, 101, 99, 100, 10)).ToList();

    private static TimeZoneInfo Chicago()
    {
        foreach (var id in new[] { "America/Chicago", "Central Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new InvalidOperationException("No Central time zone on this machine.");
    }

    private static List<int> In(TimeZoneInfo zone, IReadOnlyList<Ohlcv> bars, int barSeconds)
        => DaySeparatorPainter.Boundaries(bars, barSeconds, s => SpeechTimeFormatter.ToBarDisplay(s, barSeconds, zone));

    // ── Where the lines go ──────────────────────────────────────────────────────

    [Fact]
    public void In_UTC_a_line_starts_each_new_UTC_day()
        => Assert.Equal(new[] { 24, 48 }, In(TimeZoneInfo.Utc, Hourly(60, T0), 3600));

    /// <summary>On a Chicago clock the day starts at 06:00 UTC in winter, where the axis prints
    /// the new date: bar 6, then 30. A UTC-midnight line would sit six bars early.</summary>
    [Fact]
    public void On_the_users_clock_the_line_is_at_their_midnight_not_UTCs()
        => Assert.Equal(new[] { 6, 30, 54 }, In(Chicago(), Hourly(60, T0), 3600));

    [Fact]
    public void A_line_is_drawn_once_per_day_however_many_bars_the_day_has()
    {
        var fiveMinute = Enumerable.Range(0, 300)
            .Select(i => new Ohlcv(T0.AddHours(20).AddMinutes(5 * i), 100, 101, 99, 100, 10)).ToList();
        // 20:00 to 20:55 the next day, 300 bars: one midnight crossed, at bar 48.
        Assert.Equal(new[] { 48 }, In(TimeZoneInfo.Utc, fiveMinute, 300));
    }

    /// <summary>A gap (a weekend on a stock chart) still gets one line, at the first bar after it.</summary>
    [Fact]
    public void A_gap_of_several_days_is_one_line_at_the_first_bar_after_it()
    {
        var friday = Hourly(4, T0.AddDays(4).AddHours(18));
        var monday = Hourly(4, T0.AddDays(7).AddHours(14));
        Assert.Equal(new[] { 4 }, In(TimeZoneInfo.Utc, friday.Concat(monday).ToList(), 3600));
    }

    [Fact]
    public void A_daily_or_coarser_chart_has_no_lines()
    {
        var daily = Enumerable.Range(0, 30).Select(i => new Ohlcv(T0.AddDays(i), 100, 101, 99, 100, 10)).ToList();
        Assert.Empty(In(TimeZoneInfo.Utc, daily, 86400));
        // And as the renderer reads the chart: the bar spacing is measured from the bars.
        Assert.Empty(DaySeparatorPainter.Boundaries(daily));
    }

    [Fact]
    public void Fewer_than_two_bars_have_no_lines()
    {
        Assert.Empty(In(TimeZoneInfo.Utc, Hourly(1, T0), 3600));
        Assert.Empty(In(TimeZoneInfo.Utc, Hourly(0, T0), 3600));
    }

    // ── What is drawn ───────────────────────────────────────────────────────────

    private const int W = 900, H = 600, N = 60;

    private static ChartRenderer Renderer()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ChartRenderer(new ThemeService(settings),
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new PaneLayoutService(), NullLogger<ChartRenderer>.Instance, Substitute.For<IAppLogger>());
    }

    private static SKBitmap Draw(IReadOnlyList<Ohlcv> bars, bool separators)
    {
        var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        Renderer().Render(canvas, W, H, bars, Array.Empty<ChartSeries>(), cursorIndex: -1, viewportStart: 0,
            viewportLength: bars.Count, viewportRange: (90, 110),
            paneRanges: new Dictionary<string, (double, double)>(), rightMarginBars: 0, daySeparators: separators);
        return bmp;
    }

    private static List<int> ChangedColumns(SKBitmap a, SKBitmap b)
    {
        var cols = new List<int>();
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) { cols.Add(x); break; }
        return cols;
    }

    /// <summary>
    /// The switch draws exactly the lines <see cref="DaySeparatorPainter.Boundaries(IReadOnlyList{Ohlcv})"/>
    /// names, each at the left edge of the day's first bar, and nothing else changes. The plot
    /// starts at x = 0 and is the canvas less the price axis, split into one slot per bar.
    /// </summary>
    [Fact]
    public void Switched_on_it_draws_a_line_at_each_days_first_bar_and_nothing_else()
    {
        var bars = Hourly(N, T0);
        var starts = DaySeparatorPainter.Boundaries(bars);
        Assert.NotEmpty(starts); // 60 hourly bars cross a midnight in any zone

        using var off = Draw(bars, false);
        using var on = Draw(bars, true);
        var changed = ChangedColumns(off, on);

        var expected = starts.Select(i => (int)(i * Slot)).ToList();
        // Every line is drawn, within the pixel the stroke rounds to, and nothing else moves.
        Assert.All(expected, e => Assert.Contains(changed, x => Math.Abs(x - e) <= 1));
        Assert.All(changed, x => Assert.Contains(expected, e => Math.Abs(x - e) <= 1));
        // Down the plot, not one pixel of it: a dotted line covers about half its rows.
        int rows = Enumerable.Range(0, H).Count(y => off.GetPixel(expected[0], y) != on.GetPixel(expected[0], y));
        Assert.True(rows > H / 5, $"only {rows} rows changed at x={expected[0]}");
    }

    private static float Slot => (W - new ThemeService(Substitute.For<ISettingsManager>()).Current.AxisWidth) / (float)N;

    /// <summary>The middle half of the plot at column <paramref name="x"/> looks like the column
    /// half a slot to its right, where no line can be: nothing vertical is drawn at x.</summary>
    private static bool NoLineAt(SKBitmap bmp, int x)
    {
        int other = x + (int)(Slot / 2);
        for (int y = H / 4; y < H * 3 / 4; y++)
            if (bmp.GetPixel(x, y) != bmp.GetPixel(other, y)) return false;
        return true;
    }

    [Fact]
    public void Switched_off_no_line_is_drawn()
    {
        var bars = Hourly(N, T0);
        using var off = Draw(bars, false);
        using var on = Draw(bars, true);
        foreach (int i in DaySeparatorPainter.Boundaries(bars))
        {
            int x = (int)(i * Slot);
            Assert.True(NoLineAt(off, x), $"a line at bar {i} with the switch off");
            Assert.False(NoLineAt(on, x), $"no line at bar {i} with the switch on — the probe is blind");
        }
    }

    /// <summary>The renderer's own overload reads the bars in the zone the speech uses. On a UTC
    /// build agent the two zones agree; on a desktop in Chicago this is the check that the lines
    /// are not at UTC midnight.</summary>
    [Fact]
    public void The_renderer_reads_the_day_on_the_speech_clock()
    {
        var bars = Hourly(N, T0);
        var zone = SpeechTimeFormatter.UseUtc ? TimeZoneInfo.Utc : TimeZoneInfo.Local;
        Assert.Equal(In(zone, bars, 3600), DaySeparatorPainter.Boundaries(bars));
    }

    [Fact]
    public void On_a_daily_chart_the_switch_changes_nothing()
    {
        var daily = Enumerable.Range(0, N).Select(i => new Ohlcv(T0.AddDays(i), 100, 101, 99, 100, 10)).ToList();
        using var off = Draw(daily, false);
        using var on = Draw(daily, true);
        Assert.Empty(ChangedColumns(off, on));
    }

    // ── The setting ─────────────────────────────────────────────────────────────

    /// <summary>The frame renderer reads Settings > Appearance > Draw day separators each frame.</summary>
    [Fact]
    public void The_frame_draws_them_only_when_the_setting_is_on()
    {
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Hourly(N, T0).ToArray()),
            ViewportStartIndex = 0, ViewportLength = N, RightMarginBars = 0, ViewportRange = (90, 110),
            PaneRanges = System.Collections.Immutable.ImmutableDictionary<string, (double Min, double Max)>.Empty,
        };
        SKBitmap Frame(bool setting)
        {
            var app = Substitute.For<IAppSettings>();
            app.ShowDaySeparators.Returns(setting);
            app.UiScale.Returns(100);
            var bmp = new SKBitmap(W, H);
            using var canvas = new SKCanvas(bmp);
            new ChartFrameRenderer(Renderer(), settings: app).Render(canvas, W, H, state, 1f);
            return bmp;
        }
        using var off = Frame(false);
        using var on = Frame(true);
        int x = (int)(DaySeparatorPainter.Boundaries(state.Data.ToList())[0] * Slot);
        Assert.True(NoLineAt(off, x), "the setting is off and a line is drawn");
        Assert.False(NoLineAt(on, x), "the setting is on and no line is drawn");
    }

    [Fact]
    public void The_setting_is_off_by_default()
    {
        var store = Substitute.For<ISettingsManager>();
        store.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        Assert.False(new AppSettings(store).ShowDaySeparators);
    }
}
