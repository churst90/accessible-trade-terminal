using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Cluster ticks must fire from NAVIGATION, not just when a test calls the method.
    ///
    /// <c>NavigationSonifierClusterTests</c> has twelve tests and every one of them invokes
    /// <c>FireClusterTicksAsync</c> directly. That is why the feature could regress to zero
    /// production callers — documented as shipped in CHANGES.md:13057, then silently
    /// caller-less — without a single test going red. The user-visible consequence: a bar
    /// carrying several simultaneous indicator markers sounded identical to a bar carrying
    /// one, because only the focused component is voiced by SyncNavigationSlots.
    ///
    /// So this file asserts the wiring instead of the algorithm: move the cursor through
    /// the real <see cref="SonificationManager"/> and require that the sonifier was asked.
    /// </summary>
    public class ClusterTicksFireOnNavigationTests
    {
        private static Ohlcv Bar(int i) => new(
            new DateTime(2026, 1, 1, 0, i, 0, DateTimeKind.Utc), 100, 101, 99, 100, 1000);

        private static WorkspaceState StateAt(int index) => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 6).Select(Bar).ToList()),
            CurrentDataIndex = index,
            FocusedSeriesId = "series-1",
            FocusedComponentIndex = 2,
            InitStatus = InitializationStatus.Ready,
        };

        private static (SonificationManager mgr, MockWorkspaceStore store, INavigationSonifier nav)
            Build()
        {
            var store = new MockWorkspaceStore();
            var nav = Substitute.For<INavigationSonifier>();
            nav.FireClusterTicksAsync(
                    Arg.Any<WorkspaceState>(), Arg.Any<int>(), Arg.Any<string>(),
                    Arg.Any<int>(), Arg.Any<bool>())
                .Returns(Task.CompletedTask);

            // MockMainThreadService QUEUES rather than runs, which would swallow the
            // subscription body this test is about — the manager marshals its state
            // handling through IMainThreadService. Run inline instead.
            var mainThread = Substitute.For<IMainThreadService>();
            mainThread.When(m => m.InvokeOnMainThread(Arg.Any<Action>()))
                      .Do(ci => ci.Arg<Action>().Invoke());

            var mgr = new SonificationManager(
                Substitute.For<IPlaybackOrchestrator>(),
                nav,
                store,
                mainThread,
                new SpyEventBus());

            return (mgr, store, nav);
        }

        private static int ClusterCalls(INavigationSonifier nav) =>
            nav.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "FireClusterTicksAsync");

        private static bool WaitFor(Func<bool> cond, int timeoutMs = 1000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until)
            {
                if (cond()) return true;
                Thread.Sleep(5);
            }
            return cond();
        }

        [Fact]
        public void MovingTheCursor_AsksForClusterTicks_OnTheFocusedSeries()
        {
            var (mgr, store, nav) = Build();
            using (mgr)
            {
                store.EmitState(StateAt(1));
                store.EmitState(StateAt(2)); // index changed — this is the navigation event

                Assert.True(
                    WaitFor(() => nav.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "FireClusterTicksAsync")),
                    "Navigating to a new bar never asked for cluster ticks. Every marker on that " +
                    "bar except the focused one is then inaudible, and no existing test would " +
                    "notice because they all call FireClusterTicksAsync themselves.");

                nav.Received().FireClusterTicksAsync(
                    Arg.Any<WorkspaceState>(),
                    2,                                   // the bar just navigated to
                    "series-1",                          // the focused series
                    2,                                   // the focused component, excluded
                    false);                              // navigation, not playback
            }
        }

        [Fact]
        public void ChangingFocusWithoutMovingTheCursor_DoesNotFireClusterTicks()
        {
            // Cluster ticks describe the BAR. Re-firing them when only the focused
            // component changed would add a burst of noise to every up/down keypress,
            // which is the behaviour that makes users switch sonification off.
            var (mgr, store, nav) = Build();
            using (mgr)
            {
                // The first emit is a genuine navigation (Initial sits at a different bar),
                // so it fires. Baseline on that, then assert the focus-only change adds none.
                store.EmitState(StateAt(3));
                Assert.True(WaitFor(() => ClusterCalls(nav) == 1));

                store.EmitState(StateAt(3) with { FocusedComponentIndex = 4 });

                Thread.Sleep(120);
                Assert.Equal(1, ClusterCalls(nav));
            }
        }
    }
}
