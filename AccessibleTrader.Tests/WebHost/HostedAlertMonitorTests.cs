using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests.WebHost
{
    /// <summary>
    /// Hosted server-side alerts (Tier 2 item 2): user enumeration off the data
    /// root, and the channel fan-out that replaces local speech delivery.
    /// </summary>
    public class HostedAlertMonitorTests
    {
        // ── User enumeration ─────────────────────────────────────────────────

        [Fact]
        public void Enumerates_only_users_with_saved_alerts_and_skips_anon()
        {
            var root = TestTemp.NewDir("att-hosted-");
            try
            {
                void MakeUser(string key, bool withAlerts)
                {
                    var dir = Path.Combine(root, key, "Workspaces");
                    Directory.CreateDirectory(dir);
                    if (withAlerts) File.WriteAllText(Path.Combine(dir, "alerts.json"), "[]");
                }
                MakeUser("user-a", withAlerts: true);
                MakeUser("user-b", withAlerts: false); // registered, never made an alert
                MakeUser("anon", withAlerts: true);    // transient demo slot — never a user

                var keys = HostedAlertMonitor.EnumerateUserKeys(root);

                Assert.Equal(new[] { "user-a" }, keys);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void Missing_users_root_yields_no_users_not_an_exception()
        {
            Assert.Empty(HostedAlertMonitor.EnumerateUserKeys("/nonexistent/users/root"));
        }

        // ── Channel fan-out ──────────────────────────────────────────────────

        private static AlertFired Fired() => new(
            new AlertDefinition
            {
                Id = Guid.NewGuid().ToString(),
                Name = "BTC/USD above 50,000",
                Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove,
                Threshold = 50_000,
                Delivery = AlertDelivery.Both,
                Symbol = "BTC/USD",
                Provider = "Bitstamp",
            },
            TriggeringValue: 50000, PreviousValue: 49000,
            SpeechText: "BTC/USD crossed above 50,000", Symbol: "BTC/USD");

        [Fact]
        public async Task Fanout_sends_to_configured_channels_and_skips_unconfigured()
        {
            var configured = Substitute.For<IAlertChannel>();
            configured.IsConfigured.Returns(true);
            var unconfigured = Substitute.For<IAlertChannel>();
            unconfigured.IsConfigured.Returns(false);

            await HostedAlertMonitor.DeliverToChannelsAsync(
                new[] { configured, unconfigured }, Fired(), NullLogger.Instance, CancellationToken.None);

            await configured.Received(1).SendAsync(Arg.Any<AlertFired>(), Arg.Any<CancellationToken>());
            await unconfigured.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        }

        [Fact]
        public async Task One_failing_channel_never_starves_the_rest()
        {
            var failing = Substitute.For<IAlertChannel>();
            failing.IsConfigured.Returns(true);
            failing.SendAsync(Arg.Any<AlertFired>(), Arg.Any<CancellationToken>())
                .Returns<Task>(_ => throw new TimeoutException("SMTP down"));
            var healthy = Substitute.For<IAlertChannel>();
            healthy.IsConfigured.Returns(true);

            await HostedAlertMonitor.DeliverToChannelsAsync(
                new[] { failing, healthy }, Fired(), NullLogger.Instance, CancellationToken.None);

            await healthy.Received(1).SendAsync(Arg.Any<AlertFired>(), Arg.Any<CancellationToken>());
        }

        // ── The evaluation loop (survivors N23 and N24, 2026-08-29) ──────────
        //
        // Everything above this line tests a pure static. The loop that actually decides
        // whether a user's alerts keep working had no test at all, which is why both of the
        // mutants aimed at it survived a green 5,798-test suite. Both defects are silent by
        // construction: the user's only signal is an alert that never arrives.

        private static HostedAlertMonitor Monitor(out RecordingLogger<HostedAlertMonitor> log)
        {
            log = new RecordingLogger<HostedAlertMonitor>();
            // The scope factory and users root are never touched by the dead-feed path; push
            // is null so the report's observable is the warning it logs first, unconditionally.
            return new HostedAlertMonitor(
                Substitute.For<IServiceScopeFactory>(),
                new DemoPolicy(isDemo: false),
                usersRoot: "/nonexistent/users/root",
                logger: log,
                push: null);
        }

        private static LocalBackgroundMonitor.Watch Watch(string symbol = "BTC/USD") =>
            new("Bitstamp", symbol, "1h", new[] { Fired().Definition });

        private static IReadOnlyList<string> DeadFeedWarnings(RecordingLogger<HostedAlertMonitor> log) =>
            log.Entries
               .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("feed dead"))
               .Select(e => e.Message)
               .ToList();

        /// <summary>
        /// N23. The threshold is a real escalation, not decoration: two consecutive failures
        /// are transient and stay quiet, the third is reported, and it is reported ONCE.
        ///
        /// <para>
        /// The mutant made <c>if (n &lt; FeedFailuresBeforeReporting)</c> unreachable, so a
        /// dead feed was never reported at all and the user's alerts stopped evaluating in
        /// silence — the provider's key expires at 02:00 and the stop-loss alert watches
        /// nothing until they happen to notice.
        /// </para>
        ///
        /// <para>
        /// The counts are asserted as behaviour and the constant is deliberately NOT
        /// referenced. Reading <c>FeedFailuresBeforeReporting</c> back into the test would
        /// make it agree with any value the constant took, which is the shape that let the
        /// bound go untested in the first place.
        /// </para>
        /// </summary>
        [Fact]
        public async Task A_dead_feed_is_reported_on_the_third_consecutive_failure_and_only_once()
        {
            var monitor = Monitor(out var log);
            var watch = Watch();

            await monitor.ReportFeedFailureAsync("user-a", watch, CancellationToken.None);
            Assert.Empty(DeadFeedWarnings(log));

            await monitor.ReportFeedFailureAsync("user-a", watch, CancellationToken.None);
            Assert.Empty(DeadFeedWarnings(log));

            await monitor.ReportFeedFailureAsync("user-a", watch, CancellationToken.None);
            var reported = Assert.Single(DeadFeedWarnings(log));
            Assert.Contains("BTC/USD", reported);

            // Still once, five polls later — a warning repeated every minute trains the
            // reader to ignore it, which is the same outcome as never sending it.
            for (int i = 0; i < 5; i++)
                await monitor.ReportFeedFailureAsync("user-a", watch, CancellationToken.None);
            Assert.Single(DeadFeedWarnings(log));
        }

        /// <summary>
        /// N23, second half. The counter is CONSECUTIVE, so a recovery resets it — two
        /// failures either side of a good poll must not add up to a report.
        /// </summary>
        [Fact]
        public async Task Recovery_resets_the_counter_so_scattered_failures_never_accumulate()
        {
            var monitor = Monitor(out var log);
            var watch = Watch();

            await monitor.ReportFeedFailureAsync("user-a", watch, CancellationToken.None);
            await monitor.ReportFeedFailureAsync("user-a", watch, CancellationToken.None);
            monitor.NoteFeedRecovered("user-a", "BTC/USD");
            await monitor.ReportFeedFailureAsync("user-a", watch, CancellationToken.None);
            await monitor.ReportFeedFailureAsync("user-a", watch, CancellationToken.None);

            Assert.Empty(DeadFeedWarnings(log));

            // And the third after the reset does report — proving the silence above is the
            // reset working, not the escalation being broken outright.
            await monitor.ReportFeedFailureAsync("user-a", watch, CancellationToken.None);
            Assert.Single(DeadFeedWarnings(log));
        }

        /// <summary>
        /// N23, third half. The counter is keyed on (user, symbol): one user's expired
        /// credential is not another user's dead feed, and telling them both would be a
        /// false alarm for one of them.
        /// </summary>
        [Fact]
        public async Task Failures_do_not_pool_across_users_or_across_symbols()
        {
            var monitor = Monitor(out var log);

            // Three failures in total, but spread over three different (user, symbol) keys.
            await monitor.ReportFeedFailureAsync("user-a", Watch("BTC/USD"), CancellationToken.None);
            await monitor.ReportFeedFailureAsync("user-b", Watch("BTC/USD"), CancellationToken.None);
            await monitor.ReportFeedFailureAsync("user-a", Watch("ETH/USD"), CancellationToken.None);

            Assert.Empty(DeadFeedWarnings(log));
        }

        /// <summary>
        /// N24. A one-bar answer is not evaluable, and the bound is exactly two.
        ///
        /// <para>
        /// The second assertion is the one that makes this a contract rather than a restatement
        /// of <c>Count &gt;= 2</c>: it shows what the guard is protecting against by doing the
        /// indexing the loop does. One bar throws; two do not. So "2" is the answer the code
        /// requires, not a number someone picked.
        /// </para>
        ///
        /// <para>
        /// And the consequence is worse than a skipped watch. The throw happens inside the
        /// per-user try in <c>PollOnceAsync</c>, so it abandons EVERY REMAINING WATCH for that
        /// user on that poll — one short-answering provider silently switches off the rest of
        /// their alerts.
        /// </para>
        /// </summary>
        [Fact]
        public void A_feed_that_answers_with_fewer_than_two_bars_is_not_evaluated()
        {
            var t0 = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
            Ohlcv Bar(int hour) => new(t0.AddHours(hour), 100, 100, 100, 100, 0);

            Assert.False(HostedAlertMonitor.HasComparableBars(null));
            Assert.False(HostedAlertMonitor.HasComparableBars(Array.Empty<Ohlcv>()));
            Assert.False(HostedAlertMonitor.HasComparableBars(new[] { Bar(0) }));
            Assert.True(HostedAlertMonitor.HasComparableBars(new[] { Bar(0), Bar(1) }));
            Assert.True(HostedAlertMonitor.HasComparableBars(new[] { Bar(0), Bar(1), Bar(2) }));

            // Why two: the loop evaluates bars[^1] against bars[^2].
            var one = new List<Ohlcv> { Bar(0) };
            Assert.Throws<ArgumentOutOfRangeException>(() => one[^2]);
            var two = new List<Ohlcv> { Bar(0), Bar(1) };
            Assert.Equal(t0, two[^2].Date);
        }

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public readonly List<(LogLevel Level, string Message)> Entries = new();

            IDisposable? ILogger.BeginScope<TState>(TState state) => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
                => Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
