using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The live notice service: a fake clock, a fake feed, a fake paper account and a recording bus.
/// No speech, no sound, no network and no toast.
/// </summary>
public class MarketEventLiveNoticesTests
{
    private static readonly DateTime T = new(2026, 3, 11, 12, 30, 0, DateTimeKind.Utc);

    private static MarketEvent Cpi(bool withFigure = false) => MarketEventTestData.Event(
        id: "cpi-2026-03-11", spoken: "CPI", longName: "Consumer Price Index", moment: T)
        with
        {
            Source = "FRED release 10, fetched 2026-11-18",
            Figures = withFigure
                ? new MarketEventFigures(new[]
                {
                    new MarketEventFigure("CPI", 3.1, 2.9,
                        MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
                })
                : null,
        };

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeClock(DateTime now) =>
            _now = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _now;

        public void Set(DateTime now) =>
            _now = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new DummyTimer();

        private sealed class DummyTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeFeed : IMarketEventFigureFeed
    {
        public readonly List<MarketEvent> Fetched = new();
        public bool HasKey = true;
        public int Requests;
        public Func<string, DateOnly, bool>? OnRequest;

        public IReadOnlyList<MarketEvent> Events => Fetched;
        public int Version { get; private set; }

        public Task<bool> HasKeyAsync() => Task.FromResult(HasKey);

        public Task<bool> RequestReleaseFigureAsync(string source, DateOnly date)
        {
            Requests++;
            bool arrived = OnRequest?.Invoke(source, date) ?? false;
            if (arrived) Version++;
            return Task.FromResult(arrived);
        }
    }

    private sealed class FakePresence : IUserPresence
    {
        public bool CanReachUser { get; init; } = true;
        public bool IsAppVisible { get; init; } = true;
    }

    private sealed class Rig : IDisposable
    {
        public readonly FakeClock Clock = new(T.AddMinutes(-20));
        public readonly FakeFeed Feed = new();
        public readonly SpyEventBus Bus = new();
        public readonly MockWorkspaceStore Store = new();
        public readonly IPaperTradingProvider Paper = Substitute.For<IPaperTradingProvider>();
        public readonly IOrderExecutionService Orders = Substitute.For<IOrderExecutionService>();
        public readonly IApiKeyService Keys = Substitute.For<IApiKeyService>();
        public readonly ISettingsManager Settings = Substitute.For<ISettingsManager>();
        public readonly MarketEventLiveNotices Service;
        public readonly List<MarketEventNoticeEvent> Notices = new();

        public Rig(
            MarketEvent? e = null,
            bool key = true,
            bool present = true,
            IReadOnlyList<Position>? positions = null,
            IReadOnlyList<OpenOrder>? orders = null,
            ChartIdentity? identity = null,
            IMarketEventExpectedFeed? expected = null)
        {
            Settings.GetSetting(SettingsKeys.PaperTradingMode).Returns(JToken.FromObject(true));
            Paper.GetPositionsAsync().Returns(Task.FromResult(
                (positions ?? new List<Position>()).ToList()));
            Paper.GetOpenOrdersAsync(Arg.Any<string?>()).Returns(Task.FromResult(
                (orders ?? new List<OpenOrder>()).ToList()));
            Paper.ExposedIdentities().Returns(new[]
            {
                new ChartIdentity("Crypto|Spot", "MEXC", "BTC", "1h"),
            });

            Feed.HasKey = key;
            if (e != null) Feed.Fetched.Add(e);

            Store.EmitState(WorkspaceState.Initial with
            {
                Identity = identity ?? new ChartIdentity("Crypto|Spot", "MEXC", "BTC", "1h"),
                SelectedMarketType = MarketType.Crypto,
                MarketEventsLevel = "all",
                MarketEventsCountdowns = "15,1",
            });
            Bus.Subscribe<MarketEventNoticeEvent>(Notices.Add);

            Service = new MarketEventLiveNotices(
                Store, Bus, Feed, Orders, Paper, Keys, Settings,
                new DemoPolicy(isDemo: false),
                timeProvider: Clock,
                calendar: new MarketEventCalendar(Array.Empty<MarketEvent>(), Feed, expected),
                presence: new FakePresence { CanReachUser = present });
        }

        public async Task TickAt(DateTime now)
        {
            Clock.Set(now);
            await Service.TickAsync();
        }

        public void Dispose() => Service.Dispose();
    }

    [Fact]
    public async Task TheCountdownAndReadOutGoOutAtTheirFakeTimes()
    {
        using var rig = new Rig(Cpi(), key: false, positions: new[]
        {
            new Position("BTC", 0.05, 90_000, 4_500, 0),
        });

        await rig.TickAt(T.AddMinutes(-15));
        var countdown = Assert.Single(rig.Notices);
        Assert.Equal("CPI in 15 minutes. You're long 0.05 BTC on Paper.",
            countdown.Text);
        Assert.Equal(MarketEventImportance.High, countdown.Importance);
        Assert.Equal("cpi-2026-03-11", countdown.EventId);

        await rig.TickAt(T.AddMinutes(-1));
        Assert.Equal("CPI in 1 minute. You're long 0.05 BTC on Paper.",
            rig.Notices[^1].Text);

        // The focused chart moves twice in the first minute: one price at T, one at T + 60 s.
        rig.Clock.Set(T.AddSeconds(1));
        rig.Bus.Publish(new IntraBarUpdateEvent(MarketEventTestData.Bar(T, open: 100, close: 100)));
        rig.Clock.Set(T.AddSeconds(60));
        rig.Bus.Publish(new IntraBarUpdateEvent(MarketEventTestData.Bar(T, open: 100, close: 99.2)));

        await rig.TickAt(T.AddSeconds(61));
        Assert.Equal("CPI out. BTC down 0.8% in the first minute.", rig.Notices[^1].Text);
    }

    [Fact]
    public async Task AHeldPositionsMarketAloneIsEnough()
    {
        // No chart: a blank tab has no market of its own. The paper position's own chart
        // identity is what makes the event relevant, and the countdown still fires.
        using var rig = new Rig(Cpi(), key: false, positions: new[]
        {
            new Position("BTC", 0.05, 90_000, 4_500, 0),
        }, identity: ChartIdentity.Empty);

        await rig.TickAt(T.AddMinutes(-15));

        Assert.Equal("CPI in 15 minutes. You're long 0.05 BTC on Paper.",
            Assert.Single(rig.Notices).Text);
    }

    [Fact]
    public async Task ACircuitThatCannotReachItsUserPublishesNothing()
    {
        using var rig = new Rig(Cpi(), present: false, positions: new[]
        {
            new Position("BTC", 0.05, 90_000, 4_500, 0),
        });

        await rig.TickAt(T.AddMinutes(-15));

        Assert.Empty(rig.Notices);
        await rig.Paper.DidNotReceive().GetPositionsAsync();
    }

    [Fact]
    public async Task TheFigureIsAskedForAtTwoAndFiveMinutesThenSaidOnce()
    {
        var eventWithoutFigure = Cpi();
        var eventWithFigure = Cpi(withFigure: true);
        using var rig = new Rig(eventWithoutFigure);
        rig.Feed.OnRequest = (source, date) =>
        {
            Assert.Equal("cpi", source);
            Assert.Equal(new DateOnly(2026, 3, 11), date);
            if (rig.Feed.Requests < 2) return false;
            rig.Feed.Fetched.Clear();
            rig.Feed.Fetched.Add(eventWithFigure);
            return true;
        };

        await rig.TickAt(T.AddSeconds(60));
        Assert.Equal("CPI out. Waiting for the figure.", rig.Notices[^1].Text);

        await rig.TickAt(T.AddMinutes(2));
        Assert.Single(rig.Notices); // the try was made, and said nothing
        Assert.Equal(1, rig.Feed.Requests);

        await rig.TickAt(T.AddMinutes(5));
        Assert.Equal(2, rig.Feed.Requests);
        Assert.Equal(MarketEventSpeech.FigureClause(eventWithFigure), rig.Notices[^1].Text);

        await rig.TickAt(T.AddMinutes(15));
        Assert.Equal(2, rig.Feed.Requests);
        Assert.Equal(2, rig.Notices.Count);
    }

    private sealed class CpiExpected : IMarketEventExpectedFeed
    {
        public int Version => 1;

        public double? ExpectedFor(string source, DateOnly releaseDate, string figureName)
            => source == "cpi" && releaseDate == new DateOnly(2026, 3, 11) && figureName == "CPI"
                ? 3.0
                : null;

        public void RequestRefresh(WorkspaceState state) { }
    }

    [Fact]
    public async Task AKalshiExpectedFigureDoesNotPassForThePublishedOne()
    {
        // Review fix (Claude): the merge gives the upcoming release an expected-only row, and
        // every "has a figures object" check took that for the published figure — no waiting,
        // no FRED tries, and so never the surprise at the very moment it matters.
        using var rig = new Rig(Cpi(), expected: new CpiExpected());
        rig.Feed.OnRequest = (_, _) =>
        {
            rig.Feed.Fetched.Clear();
            rig.Feed.Fetched.Add(Cpi(withFigure: true));
            return true;
        };

        await rig.TickAt(T.AddSeconds(60));
        Assert.Equal("CPI out. Waiting for the figure.", rig.Notices[^1].Text);

        await rig.TickAt(T.AddMinutes(2));
        Assert.Equal(1, rig.Feed.Requests);
        Assert.Equal("CPI 3.1, expected 3.0, hotter.", rig.Notices[^1].Text);
    }

    [Fact]
    public async Task WithNoKeyThereIsNoWaitingAndNoFetch()
    {
        using var rig = new Rig(Cpi(), key: false);

        await rig.TickAt(T.AddSeconds(60));
        Assert.Equal("CPI out.", Assert.Single(rig.Notices).Text);

        await rig.TickAt(T.AddMinutes(2));
        Assert.Equal(0, rig.Feed.Requests);
        Assert.Single(rig.Notices);
    }
}
