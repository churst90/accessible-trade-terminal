using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The one ordered modal stack — the C# half of the 2026-09-02 stacked-dialog fix. Its
    /// JS mirror is exercised by <c>tools/jstests/keyboard-tests.mjs</c> and the two halves
    /// are observed together in <c>StackedModalBrowserTests</c>.
    /// </summary>
    public class ModalStackTests
    {
        [Fact]
        public void Open_order_is_the_order_and_the_last_opened_is_the_top()
        {
            var stack = new ModalStack(new EventBus());
            stack.Apply(new ModalStateChangedEvent(true, "Settings"));
            stack.Apply(new ModalStateChangedEvent(true, "Help"));

            Assert.Equal(new[] { "Settings", "Help" }, stack.Snapshot);
            Assert.Equal("Help", stack.Top);
            Assert.Equal(2, stack.Count);
            Assert.True(stack.IsAnyOpen);
        }

        [Fact]
        public void Closing_the_top_exposes_the_one_beneath()
        {
            var stack = new ModalStack(new EventBus());
            stack.Apply(new ModalStateChangedEvent(true, "Settings"));
            stack.Apply(new ModalStateChangedEvent(true, "Help"));
            stack.Apply(new ModalStateChangedEvent(false, "Help"));

            Assert.Equal(new[] { "Settings" }, stack.Snapshot);
            Assert.Equal("Settings", stack.Top);
        }

        [Fact]
        public void Closing_a_modal_that_is_not_on_top_removes_it_and_leaves_the_top_alone()
        {
            // A parent closing programmatically underneath a child, or a close event that
            // arrives late. The old dispatcher code had a linear-scan branch for this; the
            // rule here is one line, and this is the case that line exists for.
            var stack = new ModalStack(new EventBus());
            stack.Apply(new ModalStateChangedEvent(true, "Strategy manager"));
            stack.Apply(new ModalStateChangedEvent(true, "Help"));
            stack.Apply(new ModalStateChangedEvent(false, "Strategy manager"));

            Assert.Equal(new[] { "Help" }, stack.Snapshot);
            Assert.Equal("Help", stack.Top);
        }

        [Fact]
        public void Closing_a_name_that_is_not_open_changes_nothing_and_raises_nothing()
        {
            var stack = new ModalStack(new EventBus());
            stack.Apply(new ModalStateChangedEvent(true, "Settings"));
            int raised = 0;
            stack.Changed += _ => raised++;

            stack.Apply(new ModalStateChangedEvent(false, "Help"));

            Assert.Equal(new[] { "Settings" }, stack.Snapshot);
            Assert.Equal(0, raised);
        }

        [Fact]
        public void Reopening_an_open_modal_moves_it_to_the_top_and_does_not_duplicate_it()
        {
            // F1 is allowed while a modal is open and HelpModal.ShowAsync has no visibility
            // guard, so F1, F1 publishes two opens for one dialog. The modal-specialist review
            // of 2026-09-02 traced the old stack through it: [Help, Help], one Escape closes the
            // dialog and leaves a phantom Help, and from then on Escape targets a modal that is
            // not visible and every chart command is refused. A second open is a move-to-top.
            var stack = new ModalStack(new EventBus());
            stack.Apply(new ModalStateChangedEvent(true, "Help"));
            stack.Apply(new ModalStateChangedEvent(true, "Settings"));
            stack.Apply(new ModalStateChangedEvent(true, "Help"));

            Assert.Equal(new[] { "Settings", "Help" }, stack.Snapshot);

            stack.Apply(new ModalStateChangedEvent(false, "Help"));
            Assert.Equal(new[] { "Settings" }, stack.Snapshot);
        }

        [Fact]
        public void Opening_the_same_modal_twice_then_closing_it_once_leaves_nothing_open()
        {
            var stack = new ModalStack(new EventBus());
            stack.Apply(new ModalStateChangedEvent(true, "Help"));
            stack.Apply(new ModalStateChangedEvent(true, "Help"));
            stack.Apply(new ModalStateChangedEvent(false, "Help"));

            Assert.Empty(stack.Snapshot);
            Assert.False(stack.IsAnyOpen);
            Assert.Null(stack.Top);
        }

        [Fact]
        public void Changed_carries_the_event_and_the_stack_after_it()
        {
            var stack = new ModalStack(new EventBus());
            var seen = new List<ModalStackChange>();
            stack.Changed += seen.Add;

            stack.Apply(new ModalStateChangedEvent(true, "Settings"));
            stack.Apply(new ModalStateChangedEvent(true, "Help"));
            stack.Apply(new ModalStateChangedEvent(false, "Help"));
            stack.Apply(new ModalStateChangedEvent(false, "Settings"));

            Assert.Equal(4, seen.Count);
            Assert.True(seen[0].IsOpen);  Assert.Equal(new[] { "Settings" }, seen[0].Stack);
            Assert.True(seen[1].IsOpen);  Assert.Equal(new[] { "Settings", "Help" }, seen[1].Stack);
            Assert.False(seen[2].IsOpen); Assert.Equal("Help", seen[2].ModalName); Assert.Equal(new[] { "Settings" }, seen[2].Stack);
            Assert.False(seen[3].IsOpen); Assert.Empty(seen[3].Stack);
            Assert.Null(stack.Top);
            Assert.False(stack.IsAnyOpen);
        }

        [Fact]
        public void A_bus_fed_stack_follows_every_ModalStateChangedEvent_until_disposed()
        {
            var bus = new EventBus();
            var stack = new ModalStack(bus);

            bus.Publish(new ModalStateChangedEvent(true, "Settings"));
            bus.Publish(new ModalStateChangedEvent(true, "Help"));
            Assert.Equal(new[] { "Settings", "Help" }, stack.Snapshot);

            stack.Dispose();
            bus.Publish(new ModalStateChangedEvent(false, "Help"));
            Assert.Equal(new[] { "Settings", "Help" }, stack.Snapshot);
        }

        [Fact]
        public void The_snapshot_is_a_copy()
        {
            var stack = new ModalStack(new EventBus());
            stack.Apply(new ModalStateChangedEvent(true, "Settings"));
            var before = stack.Snapshot;
            stack.Apply(new ModalStateChangedEvent(true, "Help"));

            Assert.Single(before);
            Assert.Equal(2, stack.Snapshot.Count);
        }
    }
}
