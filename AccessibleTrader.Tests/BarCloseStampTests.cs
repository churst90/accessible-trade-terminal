using System.Collections.Immutable;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Which bar just closed, said out loud in the unit the chart is in (Cody, 2026-09-05): the
/// time of day on an intraday chart, the DATE on a daily or coarser one — where a clock time
/// would make every bar "00:00".
///
/// <para>
/// A2q (2026-10-01): the existing new-bar tests checked the price and the composition, never the
/// stamp; <c>SpeechTimeFormatter.FormatBarClock</c> had no test. The campaign moved its
/// intraday/daily boundary by one second, so a DAILY chart's bars closed "on 00:00", and it
/// survived.
/// </para>
/// </summary>
public sealed class BarCloseStampTests
{
    private static List<string> CloseOneBar(string timeframe, TimeSpan spacing)
    {
        var t0 = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, 41)
            .Select(i => new Ohlcv(t0 + spacing * i, 100, 101, 99, 100.5, 10)).ToList();
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var spoken = new List<string>();
        var speech = new CounterSpeechManager { OnSpeak = t => spoken.Add(t) };
        var formatter = new SpeechFormatter();
        var router = new SpeechFeedbackRouter(speech, formatter, store);
        _ = new AccessibilityFeedbackCoordinator(
            store, new NavigationFeedbackManager(router, formatter), router,
            new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
            formatter, bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
            new ChartPatternFocus(), new MockAutoNarrationService());

        store.EmitState(WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Harness", "BTCUSD", timeframe),
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = bars.Count - 1,
            InitStatus = InitializationStatus.Ready,
            DataStatus = DataStatus.Ready,
            AnnounceNewBars = true,
            DescribeCandlePatterns = false,
        });
        spoken.Clear();   // "BTCUSD on Harness, 1d. Ready."
        bus.Publish(new NewBarEvent(bars[^2], bars[^1]));
        return spoken;
    }

    private static readonly Regex ClockTime = new(@"\b\d{2}:\d{2}\b");

    [Fact]
    public void A_daily_bar_closes_on_a_date_not_at_a_clock_time()
    {
        string said = Assert.Single(CloseOneBar("1d", TimeSpan.FromDays(1)));

        Assert.Contains("Close 100.50 on ", said, StringComparison.Ordinal);
        Assert.Contains("2026", said, StringComparison.Ordinal);
        Assert.False(ClockTime.IsMatch(said), $"a daily bar was stamped with a time of day: \"{said}\"");
    }

    [Fact]
    public void An_hourly_bar_closes_at_a_clock_time()
    {
        // The partner, one step below the boundary: the hour is the unit, and the date (which does
        // not change for a day) would make every announcement sound the same.
        string said = Assert.Single(CloseOneBar("1h", TimeSpan.FromHours(1)));

        Assert.Contains("Close 100.50 at ", said, StringComparison.Ordinal);
        Assert.True(ClockTime.IsMatch(said), $"an hourly bar was not stamped with a time: \"{said}\"");
    }
}
