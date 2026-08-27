using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A saved workspace comes back on the tab it was saved on, in the order it was saved in.</b>
    ///
    /// <para>
    /// ── What went wrong (save) ─────────────────────────────────────────────────
    /// <c>WorkspaceLibraryService</c> built <c>config.Tabs</c> from
    /// <c>state.TabSnapshots.OrderBy(s =&gt; s.TabIndex)</c> and then mapped indices by reading
    /// <c>state.TabSnapshots![i - 1]</c> — the <b>raw, unsorted</b> list. The snapshot list is
    /// not sorted after any <c>SwitchTab</c>, because <c>SwitchTab</c> appends the outgoing tab
    /// to the end. So each tab config was filed under another tab's index, and symbols,
    /// indicator stacks and drawings went to disk against the wrong slots. A <c>sortedTabs</c>
    /// local was allocated and never used — the leftover of an earlier attempt at this fix.
    /// </para>
    ///
    /// <para>
    /// ── What went wrong (restore) ──────────────────────────────────────────────
    /// <c>WorkspaceInitializer</c> restored the SAVED ACTIVE tab into the store's existing slot
    /// (store 0), appended the others in config order at store 1..n, and then dispatched
    /// <c>SwitchTabAction</c> with the <b>config</b> index. With <c>Tabs = [A, B, C]</c> and
    /// <c>ActiveTabIndex = 1</c>: B landed at store 0, A at store 1, C at store 2, and the
    /// switch activated store 1 — which is A. The user resumed on the wrong chart with the tab
    /// bar in the wrong order, every time they saved while any tab but the first was focused.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// A real save through a real store into a real temp directory, and the file read back.
    /// The fixture always performs a <c>SwitchTab</c> before saving, because that is what
    /// unsorts the snapshot list — a fixture that saves straight after <c>AddTab</c> cannot
    /// tell the sorted path from the raw one, which is exactly why this survived.
    /// </para>
    /// </summary>
    public class WorkspaceTabIndexRoundTripTests
    {
        private static ChartIdentity Sym(string s) => new("Spot", "Binance", s, "1h");

        private static WorkspaceStore NewStore()
        {
            var bus = Substitute.For<IEventBus>();
            return new WorkspaceStore(bus,
                new MockViewportRangeCalculator(),
                new MockViewportNavigationService(),
                new MockVolumeStateService());
        }

        /// <summary>
        /// Four tabs carrying four distinct symbols, then a switch so the snapshot list is no
        /// longer in TabIndex order. Returns the store and the symbol expected at each index.
        /// </summary>
        private static (WorkspaceStore Store, string[] ByIndex) FourTabsSwitchedTo(int activeIndex)
        {
            var symbols = new[] { "AAAUSDT", "BBBUSDT", "CCCUSDT", "DDDUSDT" };

            var store = NewStore();
            store.Dispatch(new SetIdentityAction(Sym(symbols[0])));
            for (int i = 1; i < symbols.Length; i++)
            {
                store.Dispatch(new AddTabAction());
                store.Dispatch(new SetIdentityAction(Sym(symbols[i])));
            }

            // The switches are the point: each one appends the OUTGOING tab to the end of the
            // snapshot list, so list position stops matching TabIndex.
            //
            // It takes more than one. After AddTab x3 the list is [0,1,2] with tab 3 active,
            // and a single switch removes one entry and appends the highest index — which
            // leaves it sorted. Switching to 0 and then to 2 gives [1,3,0], and every
            // subsequent switch keeps it unsorted. The vacuity check below is what discovered
            // that a single switch was not enough.
            store.Dispatch(new SwitchTabAction(0));
            store.Dispatch(new SwitchTabAction(2));
            store.Dispatch(new SwitchTabAction(activeIndex));

            return (store, symbols);
        }

        private static string SymbolAt(WorkspaceState s, int index) =>
            index == s.ActiveTabIndex
                ? s.Identity.Symbol
                : (s.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty)
                      .First(t => t.TabIndex == index).Identity.Symbol;

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Each_tab_is_saved_under_its_own_index(int activeIndex)
        {
            var (store, expected) = FourTabsSwitchedTo(activeIndex);

            // Sanity: the store really does hold symbol N at index N before saving.
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], SymbolAt(store.State, i));

            var paths = new TempWorkspacePaths();
            var library = new WorkspaceLibraryService(
                NullLogger<WorkspaceLibraryService>.Instance, paths);

            library.SaveWorkspaceProfile("round-trip", store);
            var config = library.LoadProfile("round-trip");

            Assert.NotNull(config);
            Assert.Equal(4, config!.Tabs.Count);

            // Saved position N holds the tab that was at index N.
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], config.Tabs[i].Symbol);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void The_saved_active_index_points_at_the_tab_that_was_active(int activeIndex)
        {
            var (store, expected) = FourTabsSwitchedTo(activeIndex);

            var library = new WorkspaceLibraryService(
                NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths());
            library.SaveWorkspaceProfile("round-trip", store);
            var config = library.LoadProfile("round-trip")!;

            // This is the half the restore path indexes with. If it points at the wrong entry,
            // the user comes back on the wrong chart however correct the tab list is.
            Assert.InRange(config.ActiveTabIndex, 0, config.Tabs.Count - 1);
            Assert.Equal(expected[activeIndex], config.Tabs[config.ActiveTabIndex].Symbol);
        }

        [Fact]
        public void The_fixture_really_does_unsort_the_snapshot_list()
        {
            // Vacuity check. Everything above is only a test of the defect while the snapshot
            // list is NOT in TabIndex order — sorted, the old buggy mapping and the correct one
            // agree. If SwitchTab ever stops reordering, these tests quietly stop guarding and
            // this is what will say so.
            var (store, _) = FourTabsSwitchedTo(1);

            var raw = (store.State.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty)
                      .Select(t => t.TabIndex).ToList();

            Assert.NotEqual(raw.OrderBy(i => i).ToList(), raw);
        }
    }
}
