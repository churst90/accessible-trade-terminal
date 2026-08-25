using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Coverage for <see cref="ReplayService"/>.
    ///
    /// The invariants that matter: replay must never reveal a bar the user has not stepped to
    /// (that would defeat the entire exercise), the mode flag must be set before the buffer is
    /// truncated and cleared after it is restored (so a live tick can never slip through the
    /// gap), and stopping must put the full history back.
    /// </summary>
    public class ReplayServiceTests
    {
        private static List<Ohlcv> Bars(int n)
        {
            var start = new DateTime(2026, 1, 1);
            var bars = new List<Ohlcv>(n);
            for (int i = 0; i < n; i++)
                bars.Add(new Ohlcv(start.AddDays(i), i, i + 1, i - 1, i, 10));
            return bars;
        }

        /// <summary>Minimal store double that records the dispatch ORDER, which is what the tests assert on.</summary>
        private sealed class RecordingStore : IWorkspaceStore
        {
            public WorkspaceState State { get; private set; } = WorkspaceState.Initial;
            public readonly List<WorkspaceAction> Actions = new();

            public RecordingStore(IEnumerable<Ohlcv> seed)
            {
                State = State with { Data = new TimeSeriesBuffer<Ohlcv>(seed) };
            }

            public IObservable<WorkspaceState> StateStream =>
                System.Reactive.Linq.Observable.Return(State);

            public IObservable<DynamicData.IChangeSet<Ohlcv, DateTime>> DataStream =>
                System.Reactive.Linq.Observable.Empty<DynamicData.IChangeSet<Ohlcv, DateTime>>();

            public IObservable<DynamicData.IChangeSet<ChartSeries, string>> SeriesStream =>
                System.Reactive.Linq.Observable.Empty<DynamicData.IChangeSet<ChartSeries, string>>();

            public void Dispatch(WorkspaceAction action)
            {
                Actions.Add(action);
                State = action switch
                {
                    UpdateDataAction u => State with { Data = u.NewData },
                    SetReplayModeAction r => State with { IsReplaying = r.Active },
                    NavigateAction n => State with { CurrentDataIndex = n.NewIndex },
                    _ => State,
                };
            }
        }

        private sealed class NullBus : IEventBus
        {
            public readonly List<object> Published = new();
            public void Publish<T>(T eventData) => Published.Add(eventData!);
            public IDisposable Subscribe<T>(Action<T> handler) =>
                System.Reactive.Disposables.Disposable.Empty;
            public IObservable<T> AsObservable<T>() => System.Reactive.Linq.Observable.Empty<T>();
            public IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan window) =>
                System.Reactive.Disposables.Disposable.Empty;
            public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan interval) =>
                System.Reactive.Disposables.Disposable.Empty;
        }

        private static (ReplayService Service, RecordingStore Store, NullBus Bus) Build(int barCount = 100)
        {
            var store = new RecordingStore(Bars(barCount));
            var bus = new NullBus();
            return (new ReplayService(store, bus), store, bus);
        }

        [Fact]
        public void Start_RevealsOnlyUpToTheChosenBar()
        {
            var (service, store, _) = Build(100);

            Assert.True(service.Start(29));

            Assert.True(service.IsActive);
            Assert.Equal(30, service.RevealedCount);   // index 29 inclusive
            Assert.Equal(100, service.TotalCount);
            Assert.Equal(30, store.State.Data.Count);
        }

        [Fact]
        public void Start_SetsTheModeFlagBEFORETruncatingTheBuffer()
        {
            // If the order were reversed, a live tick landing between the two dispatches would be
            // treated as a normal update and would blow away the replay prefix.
            var (service, store, _) = Build(100);
            service.Start(29);

            int modeIdx = store.Actions.FindIndex(a => a is SetReplayModeAction { Active: true });
            int dataIdx = store.Actions.FindIndex(a => a is UpdateDataAction);

            Assert.True(modeIdx >= 0 && dataIdx >= 0);
            Assert.True(modeIdx < dataIdx, "replay mode must be set before the buffer is truncated");
        }

        [Fact]
        public void Stop_RestoresFullHistoryBEFOREClearingTheModeFlag()
        {
            var (service, store, _) = Build(100);
            service.Start(29);
            store.Actions.Clear();

            service.Stop();

            int dataIdx = store.Actions.FindIndex(a => a is UpdateDataAction u && u.NewData.Count == 100);
            int modeIdx = store.Actions.FindIndex(a => a is SetReplayModeAction { Active: false });

            Assert.True(dataIdx >= 0, "full history was not restored");
            Assert.True(modeIdx >= 0);
            Assert.True(dataIdx < modeIdx, "the mode flag must not clear until the full buffer is back");
            Assert.False(service.IsActive);
            Assert.Equal(100, store.State.Data.Count);
        }

        [Fact]
        public void Step_RevealsOneBarAtATime()
        {
            var (service, store, _) = Build(100);
            service.Start(29);

            Assert.Equal(31, service.Step());
            Assert.Equal(31, store.State.Data.Count);
            Assert.Equal(33, service.Step(2));
            Assert.Equal(33, store.State.Data.Count);
        }

        [Fact]
        public void Step_ClampsAtBothEnds()
        {
            var (service, _, _) = Build(50);
            service.Start(10);

            service.Step(-999);
            Assert.Equal(ReplayService.MinRevealed, service.RevealedCount);

            service.Step(9999);
            Assert.Equal(50, service.RevealedCount);
        }

        [Fact]
        public void Step_WhenInactive_ReturnsMinusOneAndDispatchesNothing()
        {
            var (service, store, _) = Build(50);
            store.Actions.Clear();

            Assert.Equal(-1, service.Step());
            Assert.Empty(store.Actions);
        }

        [Fact]
        public void Start_RefusesWhenThereIsNotEnoughHistory()
        {
            var (service, _, _) = Build(2);
            Assert.False(service.Start(1));
            Assert.False(service.IsActive);
        }

        [Fact]
        public void Start_IsIgnoredWhileAlreadyRunning()
        {
            var (service, _, _) = Build(100);
            Assert.True(service.Start(29));
            Assert.False(service.Start(60));
            Assert.Equal(30, service.RevealedCount);
        }

        [Fact]
        public void Stop_WhenInactive_IsANoOp()
        {
            var (service, store, _) = Build(50);
            store.Actions.Clear();
            service.Stop();
            Assert.Empty(store.Actions);
        }

        [Fact]
        public void CursorFollowsTheNewestRevealedBar()
        {
            var (service, store, _) = Build(100);
            service.Start(29);
            Assert.Equal(29, store.State.CurrentDataIndex);

            service.Step();
            Assert.Equal(30, store.State.CurrentDataIndex);
        }

        [Fact]
        public void RevealedBars_AreThePrefixOfRealHistory_NeverReordered()
        {
            var (service, store, _) = Build(100);
            service.Start(19);

            var revealed = store.State.Data.ToList();
            Assert.Equal(20, revealed.Count);
            for (int i = 0; i < revealed.Count; i++)
                Assert.Equal(i, revealed[i].Close); // Bars() encodes the index in Close
        }

        [Fact]
        public void Handle_ToggleStartsThenStops()
        {
            var (service, store, _) = Build(100);
            store.Dispatch(new NavigateAction(40));

            service.Handle(ReplayCommand.Toggle);
            Assert.True(service.IsActive);

            service.Handle(ReplayCommand.Toggle);
            Assert.False(service.IsActive);
            Assert.Equal(100, store.State.Data.Count);
        }

        [Fact]
        public void Handle_StepWhenNotRunning_AnnouncesInsteadOfSilentlyDoingNothing()
        {
            var (service, _, bus) = Build(100);

            service.Handle(ReplayCommand.StepForward);

            // A dead key is the accessibility failure mode that matters most: with no visual
            // feedback, silence is indistinguishable from a broken build.
            Assert.NotEmpty(bus.Published.OfType<AnnouncementEvent>());
        }

        [Fact]
        public void InactiveService_LeavesReplayFlagFalse()
        {
            var (_, store, _) = Build(100);
            Assert.False(store.State.IsReplaying);
        }
    }
}
