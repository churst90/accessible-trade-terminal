using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Shift+F1 asks for the context summary on an event of its own, and puts nothing on the
    /// channel that is spoken and printed verbatim.
    ///
    /// <para><b>What went wrong.</b> The command published
    /// <c>FeedbackRequestEvent(FeedbackType.Info, "CONTEXT_SUMMARY")</c> — a machine token in the
    /// field every other publisher fills with a sentence for a human.
    /// <c>AccessibilityFeedbackCoordinator</c> recognised the token and spoke the real summary
    /// instead, so from the coordinator's side it looked contained. It was not: the coordinator
    /// is not the only subscriber. <c>StatusBar</c> mirrors <c>ev.Message</c> into the visible
    /// strip, so Shift+F1 printed "CONTEXT_SUMMARY" there and, while that strip was still a live
    /// region, a screen reader read it out. Cody heard it as "feedback, context summary".</para>
    ///
    /// <para>The lesson is about the bus, not the word: a sentinel in a shared field is safe only
    /// while exactly one subscriber exists, and nothing stops the next one being added. So the
    /// assertion below is not "the message is no longer CONTEXT_SUMMARY" — it is that the command
    /// publishes NO <c>FeedbackRequestEvent</c> at all, which is the only shape that cannot leak
    /// again through a subscriber written next year.</para>
    /// </summary>
    public class ContextSummaryDispatchTests
    {
        private static (CommandDispatcher dispatcher, EventBus bus) Build()
        {
            var bus = new EventBus();
            var store = new WorkspaceStore(
                bus,
                new MockViewportRangeCalculator(),
                new MockViewportNavigationService(),
                new MockVolumeStateService());
            var dispatcher = new CommandDispatcher(
                bus,
                Substitute.For<INavigationEngine>(),
                store,
                Substitute.For<IBarDetailService>(),
                new IndicatorCrossingEngine(store, bus));
            return (dispatcher, bus);
        }

        [Fact]
        public void Shift_F1_publishes_its_own_event()
        {
            var (dispatcher, bus) = Build();

            var summaries = new List<ContextSummaryRequestEvent>();
            bus.Subscribe<ContextSummaryRequestEvent>(summaries.Add);

            dispatcher.Dispatch(SystemCommand.ContextSummary);

            Assert.Single(summaries);
        }

        [Fact]
        public void Shift_F1_puts_nothing_on_the_spoken_and_printed_feedback_channel()
        {
            var (dispatcher, bus) = Build();

            var feedback = new List<FeedbackRequestEvent>();
            bus.Subscribe<FeedbackRequestEvent>(feedback.Add);

            dispatcher.Dispatch(SystemCommand.ContextSummary);

            Assert.True(feedback.Count == 0,
                "Shift+F1 published a FeedbackRequestEvent. Every subscriber of that event takes "
                + "its message at face value — the status strip prints it and the screen reader "
                + "speaks it — so a request-for-a-summary travelling on it will be read aloud as "
                + "whatever token it carries. Messages: "
                + string.Join(", ", feedback.Select(f => $"\"{f.Message}\"")));
        }

        /// <summary>
        /// Vacuity check: the bus and the dispatcher are wired, so a command that DOES publish
        /// feedback is seen by the same subscription. Without this, a dispatcher that silently
        /// did nothing at all would pass the test above.
        /// </summary>
        [Fact]
        public void The_feedback_subscription_would_have_seen_one()
        {
            var (dispatcher, bus) = Build();

            var feedback = new List<FeedbackRequestEvent>();
            bus.Subscribe<FeedbackRequestEvent>(feedback.Add);

            // ChartFocus publishes a FeedbackRequestEvent on the same bus, from the same switch.
            dispatcher.Dispatch(SystemCommand.ChartFocus);

            Assert.NotEmpty(feedback);
        }
    }
}
