using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Walking or panning left past the start of what is loaded asks for older history
/// (<see cref="RequestHistoryEvent"/>); <see cref="HistoryBufferCoordinator"/> fetches it, says
/// when there is no more, and stops asking the provider once it has been told so — until a new
/// chart loads.
///
/// <para>
/// A2q (2026-10-01): the coordinator had no direct test. Every construction of it in the suite was
/// through DI with a substituted data manager, so nothing observed what it SAID or whether it
/// recovered after a "no more history".
/// </para>
/// </summary>
public sealed class HistoryBackfillCoordinatorTests
{
    /// <summary>A data manager whose backfill adds however many bars the test says.</summary>
    private sealed class BackfillingDataManager : IDataManager
    {
        public TimeSeriesBuffer<Ohlcv> Data { get; private set; }
        public int BarsPerBackfill { get; set; }
        public int BackfillCalls { get; private set; }

        public BackfillingDataManager(int bars)
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, bars)
                .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 1, 1, 1, 1, 1)));
        }

        public Task PrependOlderDataAsync()
        {
            BackfillCalls++;
            if (BarsPerBackfill > 0)
            {
                var first = Data[0].Date;
                var older = Enumerable.Range(1, BarsPerBackfill).Reverse()
                    .Select(i => new Ohlcv(first.AddDays(-i), 1, 1, 1, 1, 1));
                Data = new TimeSeriesBuffer<Ohlcv>(older.Concat(Data));
            }
            return Task.CompletedTask;
        }

        public ChartIdentity Identity { get; set; } = ChartIdentity.Empty;
        public IObservable<TimeSeriesBuffer<Ohlcv>> DataStream => System.Reactive.Linq.Observable.Never<TimeSeriesBuffer<Ohlcv>>();
        public IObservable<TimeSeriesBuffer<Ohlcv>> InitialLoadStream => System.Reactive.Linq.Observable.Never<TimeSeriesBuffer<Ohlcv>>();
        public Task RefreshDataAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StartLiveUpdates() => Task.CompletedTask;
        public Task StopLiveUpdatesAsync() => Task.CompletedTask;
        public Task CatchUpFromSnapshotAsync(TimeSeriesBuffer<Ohlcv> snapshotData, CancellationToken ct = default) => Task.CompletedTask;
        public Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync()
            => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
#pragma warning disable CS0067
        public event Action? DataUpdated;
        public event Action<string>? ErrorOccurred;
#pragma warning restore CS0067
    }

    private sealed class Harness
    {
        public SpyEventBus Bus { get; } = new();
        public MockWorkspaceStore Store { get; } = new();
        public BackfillingDataManager Data { get; } = new(300);
        public List<string> Spoken { get; } = new();

        public Harness()
        {
            Store.EmitState(WorkspaceState.Initial with { InitStatus = InitializationStatus.Ready });
            var speech = new CounterSpeechManager { OnSpeak = t => Spoken.Add(t) };
            var formatter = new SpeechFormatter();
            var router = new SpeechFeedbackRouter(speech, formatter, Store);
            _ = new AccessibilityFeedbackCoordinator(
                Store, new NavigationFeedbackManager(router, formatter), router,
                new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
                formatter, Bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
                new Core.Services.Analysis.ChartPatternCache(new Core.Services.Analysis.ChartPatternDetector(
                    new Core.Services.Analysis.SwingStructureAnalyzer())),
                new Core.Services.Analysis.ChartPatternFocus(), new MockAutoNarrationService());
            _ = new HistoryBufferCoordinator(Bus, Data, Store,
                new GlobalErrorCoordinator(Bus, Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalErrorCoordinator>.Instance,
                    new MockAudioRouter()));
        }

        public void WalkLeftPastTheStart() => Bus.Publish(new RequestHistoryEvent());

        public void LoadAnotherChart()
        {
            Store.EmitState(Store.State with { InitStatus = InitializationStatus.Loading });
            Store.EmitState(Store.State with { InitStatus = InitializationStatus.Ready });
        }
    }

    [Fact]
    public void When_the_provider_has_nothing_older_the_user_is_told_so()
    {
        var h = new Harness { Data = { BarsPerBackfill = 0 } };

        h.WalkLeftPastTheStart();

        Assert.Contains("No more history available.", h.Spoken);
    }

    [Fact]
    public void When_older_bars_arrive_nothing_is_said_about_running_out()
    {
        // The partner: a coordinator that always said "no more" would pass the test above.
        var h = new Harness { Data = { BarsPerBackfill = 100 } };

        h.WalkLeftPastTheStart();

        Assert.Equal(400, h.Data.Data.Count);
        Assert.DoesNotContain("No more history available.", h.Spoken);
    }

    [Fact]
    public void After_no_more_history_the_provider_is_not_asked_again_on_every_key()
    {
        var h = new Harness { Data = { BarsPerBackfill = 0 } };
        h.WalkLeftPastTheStart();
        h.WalkLeftPastTheStart();
        h.WalkLeftPastTheStart();

        Assert.Equal(1, h.Data.BackfillCalls);
    }

    [Fact]
    public void Loading_another_chart_lets_backfill_work_again_after_no_more_history()
    {
        // "No more history" is a fact about ONE chart. Without the reset, the first chart that
        // ran out disabled walking-left-into-history for every chart for the rest of the session.
        var h = new Harness { Data = { BarsPerBackfill = 0 } };
        h.WalkLeftPastTheStart();
        Assert.Equal(1, h.Data.BackfillCalls);

        h.LoadAnotherChart();
        h.Data.BarsPerBackfill = 50;
        h.WalkLeftPastTheStart();

        Assert.Equal(2, h.Data.BackfillCalls);
        Assert.Equal(350, h.Data.Data.Count);
    }
}
