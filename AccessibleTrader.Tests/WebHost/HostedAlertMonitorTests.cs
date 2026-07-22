using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

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
            var root = Directory.CreateTempSubdirectory("att-hosted-").FullName;
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
    }
}
