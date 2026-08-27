using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>One signal per instance per bar, and never off a bar that is still forming.</b>
    ///
    /// <para>
    /// ── What went wrong (1): the dedup was a wall clock ────────────────────────
    /// <c>StrategyEngine.EvaluateBar</c> suppressed a repeat signal with
    /// <c>(DateTime.UtcNow - last).TotalSeconds &lt; 30</c>. Two independent drivers can land
    /// on the same closed bar — <c>OnDataUpdated</c> fires on load, tab switch and prepend;
    /// <c>OnFocusedFeedUpdated</c> fires on live append. A prepend or a tab switch 31 seconds
    /// after a bar closed therefore re-signalled that same bar, and in Auto mode placed a
    /// <b>second real order</b> for it. <c>GeneralOrderService</c>'s ClientOid dedup cannot
    /// help, because the engine mints a fresh signal object each time. In the other direction,
    /// on a sub-30-second timeframe the window suppressed consecutive-bar signals that were
    /// entirely legitimate.
    /// </para>
    ///
    /// <para>
    /// ── What went wrong (2): the forming bar was tradeable ─────────────────────
    /// No provider in the fleet drops the partial trailing candle, and the contract never said
    /// whether the last bar is closed. The live-append driver is safe by construction — it
    /// takes <c>bars[Count-2]</c> — but <c>OnDataUpdated</c> evaluated
    /// <c>Data[CurrentDataIndex]</c>, which on load is the partial candle straight from the
    /// fetch, with its high, low and close all still moving.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// The closure gate is tested against the clock rather than against a canned flag, because
    /// deriving closure from the clock is the fix. The dedup is tested by evaluating the SAME
    /// bar twice with no delay at all — under the old rule that was already suppressed, so the
    /// interesting case is the one that follows: a repeat on the same bar stays suppressed
    /// <i>however long you wait</i>, and a NEW bar is never suppressed however fast it arrives.
    /// </para>
    /// </summary>
    public class StrategyBarIdentityAndClosureTests
    {
        // ── The closure gate ─────────────────────────────────────────────────

        [Theory]
        [InlineData("1h", -2.0, true)]    // closed two hours ago
        [InlineData("1h", -1.5, true)]    // opened 90 min ago on an hourly bar → closed
        [InlineData("1h", -0.5, false)]   // opened 30 min ago on an hourly bar → still forming
        [InlineData("1d", -0.5, false)]
        [InlineData("1m", -0.5, true)]    // 30 min old on a 1-minute bar → long closed
        public void A_bar_is_closed_only_once_its_interval_has_elapsed(
            string timeframe, double hoursAgo, bool expectedClosed)
        {
            var bar = new Ohlcv(DateTime.UtcNow.AddHours(hoursAgo), 100, 101, 99, 100, 10);

            Assert.Equal(expectedClosed, StrategyEngine.IsBarClosed(bar, timeframe));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-timeframe")]
        public void An_unparseable_timeframe_does_not_silently_stop_every_strategy(string? timeframe)
        {
            // Deliberate: the pre-existing behaviour was to evaluate unconditionally, and a
            // gate that quietly stopped all evaluation on an unrecognised timeframe would be a
            // worse failure than the one it prevents. A strategy that never fires looks exactly
            // like a market with no signals, and nothing would tell the user which it was.
            var forming = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 10);

            Assert.True(StrategyEngine.IsBarClosed(forming, timeframe));
        }

        // ── The dedup ────────────────────────────────────────────────────────

        private sealed class Harness
        {
            public readonly IEventBus EventBus = Substitute.For<IEventBus>();
            public readonly ITradingStrategy Strategy = Substitute.For<ITradingStrategy>();
            public readonly IWorkspaceStore Store = Substitute.For<IWorkspaceStore>();
            public readonly StrategyEngine Engine;

            public Harness()
            {
                Strategy.Name.Returns("TestStrategy");
                Strategy.OnBar(Arg.Any<Ohlcv>(), Arg.Any<IReadOnlyList<Ohlcv>>(), Arg.Any<WorkspaceState>())
                        .Returns(new StrategySignal(
                            OrderSide.Buy, OrderType.Market, null, 1.0, null, null, "always", 0.9));
                Store.State.Returns(WorkspaceState.Initial);

                Engine = new StrategyEngine(EventBus, Substitute.For<IOrderExecutionService>(),
                    Substitute.For<IAppLogger>(), NullLogger<StrategyEngine>.Instance,
                    Substitute.For<IDataManager>(), Store,
                    Substitute.For<IStrategyIndicatorCache>());
                Engine.AddStrategy(Strategy);
            }

            public int SignalCount => EventBus.ReceivedCalls()
                .Count(c => c.GetArguments().FirstOrDefault() is StrategySignalEvent);

            /// <summary>Drives the private evaluator directly — the two public drivers both
            /// funnel here, and the dedup is what is under test, not the plumbing above it.</summary>
            public void Evaluate(Ohlcv bar, IReadOnlyList<Ohlcv> history)
            {
                typeof(StrategyEngine)
                    .GetMethod("EvaluateBar", System.Reflection.BindingFlags.NonPublic
                                            | System.Reflection.BindingFlags.Instance)!
                    .Invoke(Engine, new object[] { bar, history, WorkspaceState.Initial });
            }
        }

        private static Ohlcv At(int hours, double close = 100) =>
            new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc).AddHours(hours),
                close, close + 1, close - 1, close, 10);

        [Fact]
        public void The_same_closed_bar_reached_twice_produces_exactly_one_signal()
        {
            // The load / tab-switch / prepend driver and the live-append driver can both land
            // on the same bar. That must be one signal, not two orders.
            var h = new Harness();
            var history = new List<Ohlcv> { At(0), At(1) };

            h.Evaluate(At(1), history);
            h.Evaluate(At(1), history);

            Assert.Equal(1, h.SignalCount);
        }

        [Fact]
        public void A_repeat_on_the_same_bar_stays_suppressed_however_much_time_passes()
        {
            // This is the case the 30-second window got wrong. A prepend 31 seconds after the
            // bar closed used to re-signal it; bar identity does not expire.
            var h = new Harness();
            var history = new List<Ohlcv> { At(0), At(1) };

            h.Evaluate(At(1), history);
            SetLastSignalBarClockBackwards(h);   // as if a long time had passed
            h.Evaluate(At(1), history);

            Assert.Equal(1, h.SignalCount);
        }

        [Fact]
        public void Two_consecutive_bars_each_signal_even_when_they_arrive_back_to_back()
        {
            // The other direction: on a sub-30-second timeframe the wall-clock window
            // suppressed legitimate consecutive-bar signals. Nothing here waits.
            var h = new Harness();

            h.Evaluate(At(1), new List<Ohlcv> { At(0), At(1) });
            h.Evaluate(At(2), new List<Ohlcv> { At(0), At(1), At(2) });

            Assert.Equal(2, h.SignalCount);
        }

        [Fact]
        public void The_harness_signals_at_all()
        {
            // Vacuity check: every count above would be satisfied by a strategy that never
            // signals, or an event bus that never sees a publish.
            var h = new Harness();
            h.Evaluate(At(1), new List<Ohlcv> { At(0), At(1) });
            Assert.Equal(1, h.SignalCount);
        }

        /// <summary>
        /// The dedup key is a bar date, so there is no clock to advance. This exists to make
        /// the intent of the test above explicit: nothing is being waited on, and the
        /// suppression must hold anyway.
        /// </summary>
        private static void SetLastSignalBarClockBackwards(Harness h)
        {
            var field = typeof(StrategyEngine).GetField("_lastSignalBars",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var map = (Dictionary<string, DateTime>)field.GetValue(h.Engine)!;
            Assert.NotEmpty(map); // the first evaluation really did record a bar
        }
    }
}
