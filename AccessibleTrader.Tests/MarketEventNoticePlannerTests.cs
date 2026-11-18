using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// When a live market-event notice is due. Pure planner tests: a fake "now", no clock, no bus,
/// no I/O. The windows are the whole point — a countdown that passed while the app slept is
/// skipped, never said late.
/// </summary>
public class MarketEventNoticePlannerTests
{
    private static readonly DateTime T = new(2026, 3, 11, 12, 30, 0, DateTimeKind.Utc);
    private static readonly int[] Leads = { 15, 1 };
    private static readonly MarketType[] Crypto = { MarketType.Crypto };

    private static MarketEvent Event(
        DateTime? moment = null,
        MarketEventPrecision precision = MarketEventPrecision.Exact,
        MarketEventImportance importance = MarketEventImportance.High,
        MarketEventKind kind = MarketEventKind.Economy,
        string id = "cpi-2026-03-11",
        string spoken = "CPI",
        params MarketType[] scope)
        => MarketEventTestData.Event(
            id: id, kind: kind, spoken: spoken, importance: importance,
            moment: moment ?? T, precision: precision, date: DateOnly.FromDateTime(moment ?? T),
            scope: scope);

    private static IReadOnlyList<MarketEventNotice> Due(
        DateTime now,
        IReadOnlyList<MarketEvent> events,
        IReadOnlyList<int>? leads = null,
        IReadOnlyCollection<MarketType>? open = null,
        IReadOnlyCollection<MarketType>? held = null,
        MarketEventLevel level = MarketEventLevel.All,
        MarketEventKindFilter? kinds = null,
        IReadOnlyCollection<string>? fired = null,
        bool hasKey = false,
        IReadOnlyCollection<string>? watched = null)
        => MarketEventNoticePlanner.Due(
            now, events, leads ?? Leads,
            open ?? Crypto, held ?? Array.Empty<MarketType>(),
            level, kinds ?? MarketEventKindFilter.All,
            fired ?? Array.Empty<string>(), hasKey, watched);

    [Fact]
    public void FifteenAndOneFireInTheirWindowsAndTheReadOutOneMinuteLater()
    {
        var e = Event();
        var fired = new List<string>();

        var at15 = Due(T.AddMinutes(-15), new[] { e }, fired: fired);
        Assert.Single(at15);
        Assert.Equal(MarketEventNoticeKind.Countdown, at15[0].Kind);
        Assert.Equal(15, at15[0].Minutes);
        fired.AddRange(at15.Select(n => n.Key));

        var at29 = Due(T.AddMinutes(-1), new[] { e }, fired: fired);
        Assert.Single(at29);
        Assert.Equal(MarketEventNoticeKind.Countdown, at29[0].Kind);
        Assert.Equal(1, at29[0].Minutes);
        fired.AddRange(at29.Select(n => n.Key));

        var readOut = Due(T.AddSeconds(60), new[] { e }, fired: fired);
        Assert.Single(readOut);
        Assert.Equal(MarketEventNoticeKind.ReadOut, readOut[0].Kind);

        Assert.Empty(Due(T.AddMinutes(-15).AddSeconds(25), new[] { e }, fired: fired));
    }

    [Fact]
    public void StartedLateSaysOnlyTheOneMinuteCountdown()
    {
        var e = Event();

        // T − 10 minutes: the 15-minute window [12:15, 12:15:20) is long past.
        Assert.Empty(Due(T.AddMinutes(-10), new[] { e }));

        var atOne = Due(T.AddMinutes(-1), new[] { e });
        Assert.Single(atOne);
        Assert.Equal(1, atOne[0].Minutes);
    }

    [Fact]
    public void LeadsOffStillReadsOutButTheLevelOffSaysNothing()
    {
        var e = Event();

        Assert.Empty(Due(T.AddMinutes(-15), new[] { e }, leads: Array.Empty<int>()));
        Assert.Empty(Due(T.AddMinutes(-1), new[] { e }, leads: Array.Empty<int>()));

        var readOut = Due(T.AddSeconds(60), new[] { e }, leads: Array.Empty<int>());
        Assert.Single(readOut);
        Assert.Equal(MarketEventNoticeKind.ReadOut, readOut[0].Kind);

        foreach (var now in new[] { T.AddMinutes(-15), T.AddMinutes(-1), T.AddSeconds(60) })
            Assert.Empty(Due(now, new[] { e }, level: MarketEventLevel.Off));
    }

    [Fact]
    public void AnEventTouchesAnOpenOrHeldMarketAndNothingElse()
    {
        var stock = Event(id: "stock-event", scope: MarketType.Stock);

        Assert.Empty(Due(T.AddMinutes(-15), new[] { stock }, open: Array.Empty<MarketType>()));

        // Held with no tab open: still counted. The sabotage — dropping heldMarkets — makes
        // this red.
        var held = Due(T.AddMinutes(-15), new[] { stock },
            open: Array.Empty<MarketType>(), held: new[] { MarketType.Stock });
        Assert.Single(held);
        Assert.Equal(MarketEventNoticeKind.Countdown, held[0].Kind);
    }

    [Fact]
    public void ADayOnlyEventGetsNothing()
    {
        var expiry = Event(
            precision: MarketEventPrecision.DayOnly,
            importance: MarketEventImportance.Medium,
            kind: MarketEventKind.Expiry,
            id: "stock-expiry-2026-03-20",
            spoken: "options expiry",
            scope: new[] { MarketType.Stock, MarketType.Index, MarketType.Options });
        var moment = expiry.MomentUtc;

        foreach (var now in new[] { moment.AddMinutes(-15), moment.AddMinutes(-1), moment.AddSeconds(60) })
            Assert.Empty(Due(now, new[] { expiry }, open: new[] { MarketType.Stock }));
    }

    [Fact]
    public void DaylightSavingMovesTheReleaseAndItsCountdownInUtc()
    {
        // The week after the US spring change: 8:30 New York is 12:30 UTC. After the autumn
        // change it is 13:30 UTC. Both events are built by the real seed/placement code.
        var march = MarketEventSeed.Load().Events.Single(e => e.Id == "cpi-2026-03-11");
        Assert.Equal(T, march.MomentUtc);

        var november = MarketEventSeed.CreateRelease(
            "cpi", new DateOnly(2026, 11, 12), figures: null, fetched: "2026-11-18");
        Assert.Equal(new DateTime(2026, 11, 12, 13, 30, 0, DateTimeKind.Utc), november.MomentUtc);

        // The placement function, not a hand-built record: the event lands on the 12:30 bar
        // and on the 13:30 bar, in UTC.
        static List<Ohlcv> Bars(DateOnly day)
        {
            var bars = new List<Ohlcv>();
            for (int hour = 8; hour < 20; hour++)
                for (int minute = 0; minute < 60; minute += 30)
                    bars.Add(MarketEventTestData.Bar(new DateTime(
                        day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Utc)));
            return bars;
        }

        var marchBars = Bars(new DateOnly(2026, 3, 11));
        var novemberBars = Bars(new DateOnly(2026, 11, 12));
        var marchPlacement = MarketEventPlacement.Place(
            new[] { march }, marchBars, 1800, TimeZoneInfo.Utc,
            new DateTime(2026, 3, 11, 20, 0, 0, DateTimeKind.Utc))[0];
        var novemberPlacement = MarketEventPlacement.Place(
            new[] { november }, novemberBars, 1800, TimeZoneInfo.Utc,
            new DateTime(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc))[0];
        Assert.True(marchPlacement.IsPlaced);
        Assert.Equal(new DateTime(2026, 3, 11, 12, 30, 0, DateTimeKind.Utc),
            marchBars[marchPlacement.BarIndex].Date);
        Assert.True(novemberPlacement.IsPlaced);
        Assert.Equal(new DateTime(2026, 11, 12, 13, 30, 0, DateTimeKind.Utc),
            novemberBars[novemberPlacement.BarIndex].Date);

        var marchDue = Due(T.AddMinutes(-15), new[] { march });
        Assert.Equal("countdown|15|cpi-2026-03-11", Assert.Single(marchDue).Key);

        var novemberDue = Due(november.MomentUtc.AddMinutes(-15), new[] { november });
        Assert.Equal("countdown|15|cpi-2026-11-12", Assert.Single(novemberDue).Key);
    }

    [Fact]
    public void TwoTicksInsideOneWindowGiveOneNotice()
    {
        var e = Event();
        var fired = new HashSet<string>();

        var first = Due(T.AddMinutes(-15), new[] { e }, fired: fired);
        Assert.Single(first);
        foreach (var notice in first) fired.Add(notice.Key);

        var second = Due(T.AddMinutes(-15).AddSeconds(2), new[] { e }, fired: fired);
        Assert.Empty(second);
    }

    [Fact]
    public void AWatchedEventAtLevelOffTouchingNothingGetsItsCountdown()
    {
        var e = Event(scope: MarketType.Stock);

        var due = Due(T.AddMinutes(-15), new[] { e },
            open: Array.Empty<MarketType>(), held: Array.Empty<MarketType>(),
            level: MarketEventLevel.Off, watched: new[] { e.Id });

        var notice = Assert.Single(due);
        Assert.Equal(MarketEventNoticeKind.Countdown, notice.Kind);
        Assert.Equal(15, notice.Minutes);
    }

    [Fact]
    public void TheSameEventUnwatchedAtLevelOffGetsNothing()
    {
        var e = Event(scope: MarketType.Stock);

        Assert.Empty(Due(T.AddMinutes(-15), new[] { e },
            open: Array.Empty<MarketType>(), held: Array.Empty<MarketType>(),
            level: MarketEventLevel.Off));
    }

    [Fact]
    public void AWatchedDayOnlyEventGetsNothing()
    {
        var expiry = Event(
            precision: MarketEventPrecision.DayOnly,
            importance: MarketEventImportance.Medium,
            kind: MarketEventKind.Expiry,
            id: "stock-expiry-2026-03-20",
            spoken: "options expiry",
            scope: new[] { MarketType.Stock });

        foreach (var now in new[] { T.AddMinutes(-15), T.AddMinutes(-1), T.AddSeconds(60) })
            Assert.Empty(Due(now, new[] { expiry },
                open: Array.Empty<MarketType>(), held: Array.Empty<MarketType>(),
                level: MarketEventLevel.Off, watched: new[] { expiry.Id }));
    }

    [Fact]
    public void AWatchedEventWithTheCountdownSettingOffUsesFifteenAndOne()
    {
        var e = Event(scope: MarketType.Stock);

        var at15 = Due(T.AddMinutes(-15), new[] { e }, leads: Array.Empty<int>(),
            open: Array.Empty<MarketType>(), held: Array.Empty<MarketType>(),
            level: MarketEventLevel.Off, watched: new[] { e.Id });
        Assert.Equal(15, Assert.Single(at15).Minutes);

        var at1 = Due(T.AddMinutes(-1), new[] { e }, leads: Array.Empty<int>(),
            open: Array.Empty<MarketType>(), held: Array.Empty<MarketType>(),
            level: MarketEventLevel.Off, watched: new[] { e.Id },
            fired: at15.Select(n => n.Key).ToList());
        Assert.Equal(1, Assert.Single(at1).Minutes);
    }

    [Fact]
    public void TheCountdownSettingRoundTripsAndSaysItselfInWords()
    {
        Assert.Equal(new[] { 15, 1 }, MarketEventCountdowns.Parse("15,1"));
        Assert.Equal(new[] { 30, 15, 1 }, MarketEventCountdowns.Parse("30,15,1"));
        Assert.Empty(MarketEventCountdowns.Parse("off"));
        Assert.Equal(new[] { 15, 1 }, MarketEventCountdowns.Parse("nonsense"));

        Assert.Equal("off", MarketEventCountdowns.ToSetting(Array.Empty<int>()));
        Assert.Equal("30,15,1", MarketEventCountdowns.ToSetting(new[] { 1, 30, 15, 15 }));
        Assert.Equal("Off", MarketEventCountdowns.Label(Array.Empty<int>()));
        Assert.Equal("1 minute", MarketEventCountdowns.Label(new[] { 1 }));
        Assert.Equal("5 and 1 minutes", MarketEventCountdowns.Label(new[] { 5, 1 }));
        Assert.Equal("30, 15 and 1 minutes", MarketEventCountdowns.Label(new[] { 30, 15, 1 }));
    }
}
