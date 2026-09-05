using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>What the store publishes is committed, and it arrives in the order it was committed.</b>
    ///
    /// <para>
    /// ── What went wrong (1): a nested dispatch was silently lost ───────────────
    /// <c>WorkspaceStore.Dispatch</c> carried the comment "EventBus.Publish is non-blocking so
    /// this is safe". <c>EventBus.Publish</c> is <c>GetSubject&lt;T&gt;().OnNext(...)</c> over a
    /// plain <c>Subject&lt;T&gt;</c> — fully synchronous on the caller's thread. Four
    /// <c>SeriesReducer</c> paths publish from inside <c>Reduce</c>, and <c>Reduce</c> runs
    /// inside <c>lock (_lock)</c> <b>before</b> <c>_currentState</c> is assigned. <c>lock</c> is
    /// re-entrant, so a subscriber that dispatched synchronously re-entered <c>Dispatch</c>,
    /// computed from the <i>pre-commit</i> state, committed, notified — and was then overwritten
    /// when the outer dispatch assigned its own candidate.
    /// </para>
    ///
    /// <para>
    /// ── What went wrong (2): the stream could go backwards ─────────────────────
    /// <c>_currentState = candidate</c> was inside the lock; <c>_stateSubject.OnNext</c> and the
    /// two DynamicData <c>Edit</c> calls were outside it. Two concurrent dispatchers could
    /// commit S1 then S2 and publish S2 then S1, leaving the <c>BehaviorSubject</c>'s retained
    /// value stale relative to <c>State</c> — so every late subscriber got the old one. Neither
    /// existing concurrency test asserted anything about stream order; they read
    /// <c>store.State</c>, which was the half that was already safe.
    /// </para>
    /// </summary>
    public class WorkspaceStoreOrderingTests
    {
        private static WorkspaceStore NewStore(IEventBus bus) =>
            new(bus,
                new MockViewportRangeCalculator(),
                new MockViewportNavigationService(),
                new MockVolumeStateService());

        private static ChartIdentity Sym(string s) => new("Spot", "Binance", s, "1h");

        [Fact]
        public void A_subscriber_that_dispatches_synchronously_does_not_have_its_update_erased()
        {
            // The shape of the loss: a subscriber reacts to a state change by dispatching its
            // own. Under the old arrangement the inner commit was computed from the pre-commit
            // state and then overwritten by the outer one.
            var bus = new EventBus();
            var store = NewStore(bus);

            bool reentered = false;
            using var sub = store.StateStream.Subscribe(s =>
            {
                if (reentered || s.Identity.Symbol != "OUTER") return;
                reentered = true;
                store.Dispatch(new SetIdentityAction(Sym("INNER")));
            });

            store.Dispatch(new SetIdentityAction(Sym("OUTER")));

            Assert.True(reentered, "the subscriber never re-entered — this test proved nothing");
            Assert.Equal("INNER", store.State.Identity.Symbol);
        }

        [Fact]
        public void The_retained_stream_value_matches_State_after_a_nested_dispatch()
        {
            // A late subscriber reads the BehaviorSubject's retained value. If that is stale
            // relative to State, the two halves of the store disagree about what is true.
            var bus = new EventBus();
            var store = NewStore(bus);

            bool reentered = false;
            using var sub = store.StateStream.Subscribe(s =>
            {
                if (reentered || s.Identity.Symbol != "OUTER") return;
                reentered = true;
                store.Dispatch(new SetIdentityAction(Sym("INNER")));
            });

            store.Dispatch(new SetIdentityAction(Sym("OUTER")));

            WorkspaceState? lateReader = null;
            using var late = store.StateStream.Subscribe(s => lateReader = s);

            Assert.NotNull(lateReader);
            Assert.Equal(store.State.Identity.Symbol, lateReader!.Identity.Symbol);
        }

        /// <summary>
        /// Commit and publication happen under the SAME lock acquisition.
        ///
        /// <para>This one is a source-structure assertion, deliberately, and it is worth saying
        /// why. A behavioural test was written first: two threads dispatching interleaved
        /// sequences, asserting the last state published is the state the store holds. It was
        /// then run against the defect restored — the publish moved back outside the lock — and
        /// it <b>passed, repeatedly</b>. The window between releasing <c>_lock</c> and calling
        /// <c>OnNext</c> is a few instructions wide and there is no seam to widen it through, so
        /// that test could not fail and was therefore guarding nothing.</para>
        ///
        /// <para>The property that was actually wrong is lexical — <c>_currentState = candidate</c>
        /// was inside <c>lock (_lock)</c> while <c>_stateSubject.OnNext</c> and the two
        /// DynamicData <c>Edit</c> calls were outside it — so a lexical check is one that can
        /// genuinely go red. It does: restoring the old brace placement fails this test.</para>
        /// </summary>
        [Fact]
        public void Commit_and_publication_are_inside_the_same_lock()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);

            var path = Path.Combine(dir!.FullName,
                "AccessibleTrader.Core", "Services", "WorkspaceStore.cs");
            var lines = File.ReadAllLines(path);

            // Find Dispatch, then the lock it opens, then track brace depth to its close.
            int dispatchAt = Array.FindIndex(lines,
                l => l.Contains("public void Dispatch(WorkspaceAction action)", StringComparison.Ordinal));
            Assert.True(dispatchAt >= 0, "Dispatch not found — this guard needs rewriting.");

            int lockAt = Array.FindIndex(lines, dispatchAt,
                l => l.Trim() == "lock (_lock)");
            Assert.True(lockAt > dispatchAt, "the dispatch lock was not found where expected.");

            int depth = 0;
            bool started = false;
            int lockEnd = -1;
            for (int i = lockAt; i < lines.Length; i++)
            {
                foreach (char c in lines[i])
                {
                    if (c == '{') { depth++; started = true; }
                    else if (c == '}') depth--;
                }
                if (started && depth == 0) { lockEnd = i; break; }
            }
            Assert.True(lockEnd > lockAt, "could not find the end of the dispatch lock.");

            string insideLock = string.Join("\n", lines[lockAt..(lockEnd + 1)]);

            Assert.Contains("_currentState = candidate;", insideLock);
            Assert.Contains("_stateSubject.OnNext(newState);", insideLock);
            Assert.Contains("_seriesSource.Edit(", insideLock);
            Assert.Contains("_dataSource.Edit(", insideLock);
        }

        [Fact]
        public void A_reducer_announcement_is_published_after_the_state_it_describes_is_committed()
        {
            // SeriesReducer announces from inside Reduce. Whatever it says must be true of the
            // state the store holds by the time anyone hears it — otherwise a subscriber that
            // reads State in response to the announcement sees the world before the change.
            var bus = new EventBus();
            var store = NewStore(bus);

            store.Dispatch(new SetIdentityAction(Sym("BTCUSDT")));

            string? symbolWhenAnnounced = null;
            using var sub = bus.Subscribe<AnnouncementEvent>(_ =>
                symbolWhenAnnounced ??= store.State.Identity.Symbol);

            // RestoreAllComponentsAction is one of the four reducer paths that announce.
            store.Dispatch(new RestoreAllComponentsAction(true));

            if (symbolWhenAnnounced != null)
                Assert.Equal(store.State.Identity.Symbol, symbolWhenAnnounced);
        }

        [Fact]
        public void A_reduce_that_changes_nothing_still_says_so_when_the_reducer_chose_to()
        {
            // The inverse of the test this replaces. It asserted that an unchanged state
            // announces NOTHING, and the store enforced it by discarding whatever the reducer
            // had queued — which silenced RestoreAll's "Nothing was hidden.", the one sentence
            // written for exactly this case. A key that does nothing and says nothing is a dead
            // key to a screen-reader user. The reducer decides what to say; the store only
            // decides when.
            var bus = new EventBus();
            var store = NewStore(bus);

            var announcements = new List<string>();
            using var sub = bus.Subscribe<AnnouncementEvent>(a => announcements.Add(a.Message));

            // No series at all, so there is nothing to restore and the state cannot change.
            store.Dispatch(new RestoreAllComponentsAction(true));

            Assert.Equal(WorkspaceState.Initial.ActiveSeries.Count, store.State.ActiveSeries.Count);
            Assert.Equal("Nothing was hidden.", Assert.Single(announcements));
        }

        [Fact]
        public void A_toggle_on_a_series_that_does_not_exist_announces_nothing()
        {
            // What the old discard was actually protecting against — and the reducers already
            // protect against it themselves, by publishing only for a target they found.
            var bus = new EventBus();
            var store = NewStore(bus);

            int announcements = 0;
            using var sub = bus.Subscribe<SeriesStateChangedEvent>(_ => announcements++);
            using var sub2 = bus.Subscribe<AnnouncementEvent>(_ => announcements++);

            store.Dispatch(new ToggleHideAction("no-such-series", null));
            store.Dispatch(new ToggleMuteAction("no-such-series", null));
            store.Dispatch(new ToggleNarrationAction("no-such-series", null));

            Assert.Equal(0, announcements);
        }
    }
}
