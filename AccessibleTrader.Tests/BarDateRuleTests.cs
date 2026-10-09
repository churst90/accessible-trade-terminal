using System.Collections.Immutable;
using System.Globalization;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A daily bar is named by its own date.</b>
///
/// <para>Reported by Cody, 2026-10-09, in America/Chicago: on the 9th, the daily chart's newest
/// bar read "October 8". The bar is stamped 2026-10-09T00:00Z; every readout converted that to
/// the user's zone (19:00 on the 8th in Chicago) and spoke the local date. Weekly bars, which
/// open on Monday 00:00Z, read as the Sunday before. A day-or-longer bar is a calendar date, not
/// an instant, so it is labelled by its own (UTC) date in every zone, the way exchanges and
/// TradingView label it; intraday bars keep the user's clock.</para>
///
/// <para>Two tiers, as in <see cref="SpeechTimeZoneConsistencyTests"/>. The rule itself, against
/// America/Chicago named explicitly, holds on any build agent. The readout paths run in the
/// machine's zone: they go red against the old code anywhere west of Greenwich (Cody's box, and
/// this one), and are honestly vacuous on a UTC agent — the rule tier carries the weight there.
/// Every expected date below is a literal or comes from the BCL, never from the formatter under
/// test.</para>
/// </summary>
public sealed class BarDateRuleTests
{
    private const int Day = 86400;
    private const int Week = 7 * Day;

    private static readonly DateTime DailyBar = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);   // a Friday
    private static readonly DateTime WeeklyBar = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);  // a Monday

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

    // ── The rule, in a zone named explicitly ────────────────────────────────────

    [Fact]
    public void A_daily_bar_is_its_own_date_even_where_local_time_is_the_day_before()
    {
        var chicago = Chicago();
        // Vacuity first: in Chicago the instant really is the evening of the 8th.
        Assert.Equal(8, TimeZoneInfo.ConvertTimeFromUtc(DailyBar, chicago).Day);

        var shown = SpeechTimeFormatter.ToBarDisplay(DailyBar, Day, chicago);
        Assert.Equal(new DateTime(2026, 10, 9), shown.Date);
    }

    [Fact]
    public void A_weekly_bar_is_its_monday_not_the_sunday_before()
    {
        var shown = SpeechTimeFormatter.ToBarDisplay(WeeklyBar, Week, Chicago());
        Assert.Equal(DayOfWeek.Monday, shown.DayOfWeek);
        Assert.Equal(5, shown.Day);
    }

    [Fact]
    public void An_intraday_bar_keeps_the_users_clock()
    {
        var bar = new DateTime(2026, 10, 9, 14, 30, 0, DateTimeKind.Utc);
        var shown = SpeechTimeFormatter.ToBarDisplay(bar, 3600, Chicago());
        Assert.Equal(new DateTime(2026, 10, 9, 9, 30, 0), shown);  // CDT, UTC-5
    }

    [Theory]
    [InlineData(0)]   // crypto venues, Tradier, Twelve Data, FMP, CSV dates
    [InlineData(4)]   // Alpaca and Polygon stocks: midnight New York, summer
    [InlineData(5)]   // midnight New York in winter; Schwab's midnight Chicago in summer
    [InlineData(6)]   // Schwab, winter
    public void Every_providers_daily_stamp_names_the_session_it_is_for(int hourUtc)
    {
        var stamp = new DateTime(2026, 10, 9, hourUtc, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 10, 9), SpeechTimeFormatter.BarDate(stamp));
    }

    // ── Every readout path ───────────────────────────────────────────────────────

    private static WorkspaceState DailyState(string timeframe = "1d", params DateTime[] stamps)
    {
        if (stamps.Length == 0) stamps = new[] { DailyBar.AddDays(-1), DailyBar };
        var cfg = new SeriesConfig { Id = "candles", Name = "Candles", FriendlyName = "Candles", Pane = "Main" };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Close", DisplayName = "Close", DisplayType = ComponentDisplayType.Line,
            IsVisible = true, DataMapping = "close",
        });
        var buf = new SeriesDataBuffer { SeriesId = "candles" };
        buf.ComponentData["Close"] = stamps.Select(_ => 100.0).ToArray();
        var series = new ChartSeries(cfg, buf);
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(stamps.Select(d => new Ohlcv(d, 100, 110, 95, 105, 1000))),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            PrimarySeriesId = series.Id,
            CurrentDataIndex = stamps.Length - 1,
            ViewportStartIndex = 0,
            ViewportLength = stamps.Length,
            Identity = new ChartIdentity("Spot", "Test", "BTC/USD", timeframe),
            LastInteractionContext = InteractionContext.Component,
            SpeakTimestamps = true,
            TimestampReadLocation = "Always",
            ReadColumnHeaders = false,
            SpeechOrder = "HeaderValue",
        };
    }

    [Fact]
    public void The_arrow_keys_say_the_bars_own_date()
    {
        var state = DailyState();
        string said = new SpeechFormatter().FormatPointFeedback(
            state, isXMove: true, isYMove: false, state.ActiveSeries[0], state.Data![^1], "");

        Assert.StartsWith("October 9 2026.", said);
    }

    [Fact]
    public void The_date_only_speech_order_says_the_bars_own_date()
    {
        var state = DailyState() with { SpeechOrder = "DateOnly" };
        string said = new SpeechFormatter().FormatPointFeedback(
            state, isXMove: true, isYMove: false, state.ActiveSeries[0], state.Data![^1], "");

        Assert.Contains("October 09", said);
    }

    [Fact]
    public void A_bar_close_announcement_names_the_bars_own_date()
    {
        // FormatBarClock is the stamp the focused bar-close line, the background-tab line and
        // the desktop notification all carry.
        Assert.Equal("October 9 2026", SpeechTimeFormatter.FormatBarClock(DailyBar, Day));
        Assert.Equal("October 5 2026", SpeechTimeFormatter.FormatBarClock(WeeklyBar, Week));
    }

    [Fact]
    public void Playback_and_the_viewport_range_name_the_bars_own_dates()
    {
        Assert.Equal("October 9 2026", PlaybackNarration.DateText(DailyBar, Day));
        Assert.Equal("October 8 2026 to October 9 2026",
            SpeechTimeFormatter.FormatBarRange(DailyBar.AddDays(-1), DailyBar, Day));
    }

    [Fact]
    public void Playback_landmarks_cross_into_the_month_the_weekly_bar_is_in()
    {
        // Weekly bars on Monday 25 May and Monday 1 June 2026. In Chicago 1 June 00:00Z is the
        // evening of 31 May, so the old local comparison never crossed into June at all.
        var may25 = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc);
        var june1 = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("June 2026",
            PlaybackNarration.Landmark(may25, june1, PlaybackNarration.LandmarkUnit.Month, Week));
    }

    [Fact]
    public void The_layout_description_names_the_bars_own_dates()
    {
        string text = ChartLayoutDescriber.Describe(DailyState(), "BTC/USD", "1d");
        Assert.Contains("October 8 2026 to October 9 2026", text);
    }

    [Fact]
    public void Bar_detail_names_the_day_not_a_clock_time_on_a_daily_chart()
    {
        // Ctrl+Shift+D led with FormatTime(bar.Date): "19:00" in Chicago for every daily bar.
        Assert.Equal("October 9 2026",
            SpeechTimeFormatter.FormatBarClock(DailyBar, PlaybackNarration.BarSeconds(DailyState())));
    }

    [Fact]
    public void The_hover_readout_and_the_bar_slider_say_the_same_date_as_the_arrow_keys()
    {
        // ChartHoverTracker.FormatBarDate is the hover readout AND the bar slider's spoken value
        // (ChartArea.GetBarSliderValueText). It formatted the raw UTC stamp itself, skipping the
        // formatter — the one readout that did.
        Assert.Equal("Oct 9, 2026", ChartHoverTracker.FormatBarDate(DailyBar, Day));
        Assert.Equal("Oct 5, 2026", ChartHoverTracker.FormatBarDate(WeeklyBar, Week));

        // Intraday: the user's clock, which the arrow keys also read.
        var intraday = new DateTime(2026, 10, 9, 14, 30, 0, DateTimeKind.Utc);
        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(intraday, TimeZoneInfo.Local)
                         .ToString("MMM d, yyyy HH:mm", CultureInfo.InvariantCulture),
                     ChartHoverTracker.FormatBarDate(intraday, 3600));
    }

    [Fact]
    public void The_axis_infers_bar_spacing_from_the_smallest_gap_so_a_weekend_does_not_make_hourly_bars_daily()
    {
        var fri = new DateTime(2026, 10, 9, 19, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>
        {
            new(fri, 1, 1, 1, 1, 0),
            new(fri.AddHours(1), 1, 1, 1, 1, 0),
            new(fri.AddDays(3), 1, 1, 1, 1, 0),   // Monday
        };
        Assert.Equal(3600, AccessibleTrader.Core.Services.ChartMath.BarSecondsOf(bars));
    }
}
