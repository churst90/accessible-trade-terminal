using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// THE TIMESTAMP AN ARROW-KEY READING CARRIES — the time, and the date only when the reading
/// crosses into a new day.
///
/// <para>
/// Cody, 2026-09-05: <i>"if I switch to an hour chart, when I use the arrows to move by bar, I
/// want to hear the date, like the new date like september 5, september 6, when I actually cross
/// into a new day, otherwise, just the timestamp itself."</i>
/// </para>
///
/// <para>
/// Every bar used to carry the full stamp — "September 05, 2026, 14:00" — which is the same
/// eleven syllables of date in front of each of twenty-four consecutive readings. And on a DAILY
/// chart it was the reverse absurdity: "September 05, 2026, 00:00", a time that is identical on
/// every bar a daily chart has.
/// </para>
///
/// <para>
/// <b>Why the comparison is against the last bar READ and not against the bar before this one in
/// the data.</b> Arrowing LEFT across midnight lands on the last bar of the previous day, which
/// is not the first bar of anything: under a data-only rule that crossing would be silent, and it
/// is the crossing the user just made. The formatter remembers the day it last spoke.
/// </para>
/// </summary>
public sealed class NavigationTimestampTests
{
    private static ChartSeries Candles()
    {
        var cfg = new SeriesConfig { Id = "candles", Name = "Candles", FriendlyName = "Candles" };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Close", DisplayName = "Close", DisplayType = ComponentDisplayType.Line,
            IsVisible = true, DataMapping = "close",
        });
        var buf = new SeriesDataBuffer { SeriesId = "candles" };
        buf.ComponentData["Close"] = new[] { 100.0 };
        return new ChartSeries(cfg, buf);
    }

    /// <summary>
    /// Hourly bars from 22:00 on 5 September, so bar 2 is the first of the 6th.
    ///
    /// <para>
    /// LOCAL kind, deliberately. The day a reading falls on is the day the USER sees — every
    /// stamp goes through <c>SpeechTimeFormatter.ToDisplay</c> — so a fixture built in UTC
    /// crosses midnight only for a reader in UTC. Written in UTC first, this file passed on a
    /// UTC box and failed here at −05:00, where 22:00, 23:00 and 00:00 UTC are all the same
    /// local afternoon.
    /// </para>
    /// </summary>
    private static readonly DateTime[] Hourly =
    {
        new(2026, 9, 5, 22, 0, 0, DateTimeKind.Local),
        new(2026, 9, 5, 23, 0, 0, DateTimeKind.Local),
        new(2026, 9, 6,  0, 0, 0, DateTimeKind.Local),
    };

    private static WorkspaceState State(string timeframe, DateTime[] stamps, bool dateEveryBar = false,
                                        string speechOrder = "HeaderValue")
    {
        var series = Candles();
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(stamps.Select(d => new Ohlcv(d, 100, 110, 95, 105, 1000))),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            PrimarySeriesId = series.Id,
            CurrentDataIndex = 0,
            Identity = new ChartIdentity("Spot", "Test", "BTC/USD", timeframe),
            LastInteractionContext = InteractionContext.Component,
            SpeakTimestamps = true,
            TimestampReadLocation = "Always",
            ReadColumnHeaders = false,
            SpeechOrder = speechOrder,
            SpeakDateOnEveryBar = dateEveryBar,
        };
    }

    /// <summary>Reads a run of bars through ONE formatter, the way navigation does.</summary>
    private static List<string> Read(WorkspaceState state, params int[] indices)
    {
        var formatter = new SpeechFormatter();
        var series = state.ActiveSeries[0];
        var said = new List<string>();
        foreach (int i in indices)
            said.Add(formatter.FormatPointFeedback(
                state with { CurrentDataIndex = i }, true, false, series, state.Data![i], ""));
        return said;
    }

    private static string Time(DateTime d) => SpeechTimeFormatter.FormatTime(d);
    private static string Day(DateTime d) => SpeechTimeFormatter.Format(d, SpeechTimeFormatter.DateFormat);

    // ── Intraday ────────────────────────────────────────────────────────────────

    [Fact]
    public void OnAnHourChart_BarsInsideOneDayCarryTheTimeAlone()
    {
        var said = Read(State("1h", Hourly), 0, 1);

        // The first reading names the day — nothing has been read, so every bar is a crossing.
        Assert.StartsWith($"{Day(Hourly[0])}, {Time(Hourly[0])}.", said[0]);
        // The second is inside the same day: the time, and nothing in front of it.
        Assert.StartsWith($"{Time(Hourly[1])}.", said[1]);
        Assert.DoesNotContain(Day(Hourly[1]), said[1]);
    }

    [Fact]
    public void CrossingIntoANewDay_NamesTheDay()
    {
        var said = Read(State("1h", Hourly), 0, 1, 2);

        Assert.StartsWith($"{Day(Hourly[2])}, {Time(Hourly[2])}.", said[2]);
        Assert.Contains("September 06", said[2], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrossingBACKWARDSIntoThePreviousDay_AlsoNamesTheDay()
    {
        // The case a data-only rule cannot see: bar 1 is not the first bar of anything, and it
        // is exactly where arrowing left from midnight lands.
        var said = Read(State("1h", Hourly), 2, 1);

        Assert.StartsWith($"{Day(Hourly[1])}, {Time(Hourly[1])}.", said[1]);
    }

    [Fact]
    public void ReadingTheSameBarTwice_DoesNotRepeatTheDate()
    {
        var said = Read(State("1h", Hourly), 0, 0);

        Assert.Contains(Day(Hourly[0]), said[0]);
        Assert.DoesNotContain(Day(Hourly[0]), said[1]);
    }

    // ── The switch ──────────────────────────────────────────────────────────────

    [Fact]
    public void SpeakDateOnEveryBar_PutsTheDateOnEveryReading()
    {
        var said = Read(State("1h", Hourly, dateEveryBar: true), 0, 1, 2);

        Assert.All(said, m => Assert.Contains(Day(Hourly[0])[..9], m, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith($"{Day(Hourly[1])}, {Time(Hourly[1])}.", said[1]);
    }

    [Fact]
    public void TheSwitchDefaultsToBoundariesOnly()
    {
        // Opt-IN, so a substitute or a fresh install gets the quieter reading. Stated because an
        // opt-OUT setting silently inverts wherever nobody configures it.
        Assert.False(WorkspaceState.Initial.SpeakDateOnEveryBar);
    }

    // ── Daily and coarser ───────────────────────────────────────────────────────

    [Fact]
    public void OnADailyChart_TheStampIsTheDateAlone()
    {
        var daily = new[]
        {
            new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc),
        };

        var said = Read(State("1d", daily), 0, 1);

        Assert.StartsWith(SpeechTimeFormatter.FormatLongDate(daily[0]) + ".", said[0]);
        // "00:00" on every bar of a daily chart is a time that tells the user nothing.
        Assert.DoesNotContain("00:00", said[0]);
        Assert.StartsWith(SpeechTimeFormatter.FormatLongDate(daily[1]) + ".", said[1]);
    }

    // ── The explicit orders still win ───────────────────────────────────────────

    [Fact]
    public void TimeOnlyOrder_IsUnchanged()
    {
        // A user who picked "Name and value, time only" has already answered this question.
        var said = Read(State("1h", Hourly, speechOrder: "HeaderValueTimeOnly"), 0, 2);

        Assert.All(said, m => Assert.DoesNotContain("September", m, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith($"{Time(Hourly[2])}.", said[1]);
    }

    [Fact]
    public void DateOnlyOrder_IsUnchanged()
    {
        var said = Read(State("1h", Hourly, speechOrder: "HeaderValueDateOnly"), 0, 1);

        Assert.All(said, m => Assert.StartsWith(Day(Hourly[0]) + ".", m));
    }
}
