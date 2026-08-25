using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The browser chart's re-render must be SAMPLED, not throttled.
    ///
    /// <para>
    /// In Rx.NET <c>Throttle</c> is debounce — it emits only after a quiet gap of the given length.
    /// The render trigger fires on every store emission, and live feeds on Bitstamp, Binance,
    /// Kraken and MEXC all beat 100 ms during volatility, so the timer kept resetting and the chart
    /// image stopped updating for as long as the market kept moving. It came back when things went
    /// quiet. The picture froze at exactly the moment it mattered, and looked fine either side of it.
    /// </para>
    ///
    /// <para>
    /// Two tests: the behaviour, on a virtual clock, so the difference is stated rather than
    /// asserted by name; and a source check, because the behavioural one would still pass against a
    /// component that had been changed back — no C# test can observe what a .razor file does with
    /// its own subscription.
    /// </para>
    /// </summary>
    public class ChartRenderRateLimitTests
    {
        [Fact]
        public void UnderAContinuousStreamOfTicks_SampleEmitsAndThrottleNeverDoes()
        {
            // A VIRTUAL clock, not the wall clock.
            //
            // This test used to run in real time with ticks four times faster than the window and
            // a comment calling the margin "deliberately wide". It was not wide: a GitHub runner
            // stalls past 100 ms without difficulty, and one such gap in a 700 ms window is a
            // quiet period, which is precisely what Throttle waits for. It emitted once, the
            // assertion of exactly zero failed, and the red build said nothing about the code
            // under test. A test whose subject is *timing semantics* must not be scheduled by
            // something it does not control.
            //
            // HistoricalScheduler comes with System.Reactive itself — no Microsoft.Reactive.Testing
            // reference needed, which is what the old comment gave as the reason for real time.
            // Time now advances only when this test says so, so the result is identical on a
            // starved runner and on an idle laptop, forever.
            var scheduler = new HistoricalScheduler(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var window = TimeSpan.FromMilliseconds(100);
            int throttled = 0, sampled = 0;

            var ticks = Observable.Interval(TimeSpan.FromMilliseconds(25), scheduler).Publish();
            using (ticks.Throttle(window, scheduler).Subscribe(_ => throttled++))
            using (ticks.Sample(window, scheduler).Subscribe(_ => sampled++))
            using (ticks.Connect())
            {
                scheduler.AdvanceBy(TimeSpan.FromMilliseconds(700));
            }

            // Throttle is debounce: while ticks keep arriving inside the window it emits NOTHING.
            // That is the frozen chart, and it is why the operator had to change.
            Assert.Equal(0, throttled);
            Assert.True(sampled > 0, "Sample should have emitted repeatedly under a continuous stream");
        }

        [Fact]
        public void TheChartComponent_UsesSampleForItsRenderTrigger()
        {
            string source = ReadComponent("ChartArea.razor");
            // Anchor on the subscription itself, not the first mention of the field — the field is
            // declared far above, and a window taken from there would prove nothing.
            int start = source.IndexOf("var renderSub", StringComparison.Ordinal);
            Assert.True(start >= 0, "could not find the render subscription in ChartArea.razor");

            // The operator applied to the render trigger, not merely somewhere in the file:
            // TactileCanvasCoordinator uses Throttle deliberately and correctly elsewhere.
            string window = source[start..Math.Min(source.Length, start + 400)];
            Assert.Contains(".Sample(", window);
            Assert.DoesNotContain(".Throttle(", window);
        }

        private static string ReadComponent(string fileName)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;

            Assert.NotNull(dir);
            string path = Path.Combine(dir!.FullName, "AccessibleTrader.BlazorClient.Components", fileName);
            Assert.True(File.Exists(path), $"component not found at {path}");
            return File.ReadAllText(path);
        }
    }
}
