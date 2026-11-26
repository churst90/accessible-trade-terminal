using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The dead-man's switch (Cody 2026-11-26): off by default; on, it keeps pushing the venue's
    /// cancel time a minute ahead; switching it off or closing normally clears it, so orders
    /// stay. These drive <see cref="DeadMansSwitchService.TickAsync"/> directly against a fake
    /// venue and a fixed clock; nothing here reaches Hyperliquid.
    /// </summary>
    public class DeadMansSwitchServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 26, 18, 0, 0, TimeSpan.Zero);

        private sealed class FixedClock : TimeProvider
        {
            public DateTimeOffset At = Now;
            public override DateTimeOffset GetUtcNow() => At;
        }

        private sealed class Rig
        {
            public readonly List<DateTimeOffset?> Calls = new();
            public readonly List<AnnouncementEvent> Said = new();
            public readonly Dictionary<string, JToken?> Settings = new();
            public readonly FixedClock Clock = new();
            public string? NextError;
            public bool Throw;
            public bool CanSchedule = true;
            public DeadMansSwitchService Svc = null!;

            public Rig(bool on = true, int? seconds = null, HostMode mode = HostMode.Full,
                bool withdrawalKey = false, bool offersSwitch = true)
            {
                if (on) Settings[SettingsKeys.DeadMansSwitch] = JToken.FromObject(true);
                if (seconds is int s) Settings[SettingsKeys.DeadMansSwitchSeconds] = JToken.FromObject(s);

                var settings = Substitute.For<ISettingsManager>();
                settings.GetSetting(Arg.Any<string>()).Returns(c => Settings.GetValueOrDefault(c.Arg<string>()));

                var keys = Substitute.For<IApiKeyService>();
                keys.GetAllKeysAsync().Returns(Task.FromResult(new List<ApiKeyConfig>
                {
                    new("Hyperliquid", "main", "0xabc", "secret", IsActive: true, AllowsWithdrawal: withdrawalKey),
                }));

                IMarketDataProvider provider;
                if (offersSwitch)
                {
                    provider = Substitute.For<IMarketDataProvider, IDeadMansSwitch>();
                    var venue = (IDeadMansSwitch)provider;
                    venue.CanScheduleCancel.Returns(_ => CanSchedule);
                    venue.ScheduleCancelAsync(Arg.Any<DateTimeOffset?>()).Returns(c =>
                    {
                        Calls.Add(c.Arg<DateTimeOffset?>());
                        if (Throw) throw new HttpRequestException("down");
                        return Task.FromResult(NextError);
                    });
                }
                else provider = Substitute.For<IMarketDataProvider>();

                var data = Substitute.For<IDataService>();
                data.GetProviderAsync("Hyperliquid").Returns(Task.FromResult<IMarketDataProvider?>(provider));

                var bus = new EventBus();
                bus.Subscribe<AnnouncementEvent>(e => Said.Add(e));
                Svc = new DeadMansSwitchService(data, keys, settings, bus, new DemoPolicy(mode), time: Clock);
            }

            public void Set(string key, object value) => Settings[key] = JToken.FromObject(value);
        }

        [Fact]
        public async Task Off_by_default_it_never_asks_the_venue()
        {
            var rig = new Rig(on: false);
            await rig.Svc.TickAsync();
            await rig.Svc.TickAsync();
            Assert.Empty(rig.Calls);
            Assert.Empty(rig.Said);
        }

        [Fact]
        public async Task On_it_sets_the_cancel_a_minute_ahead_and_says_so_once()
        {
            var rig = new Rig();
            await rig.Svc.TickAsync();
            rig.Clock.At = Now.AddSeconds(20);
            await rig.Svc.TickAsync();

            Assert.Equal(new DateTimeOffset?[] { Now.AddSeconds(60), Now.AddSeconds(80) }, rig.Calls);
            var said = Assert.Single(rig.Said);
            Assert.Contains("Dead-man's switch on for Hyperliquid", said.Message);
            Assert.Contains("60 seconds", said.Message);
            Assert.Contains("Closing the terminal normally keeps your orders", said.Message);
            Assert.Equal(new[] { "Hyperliquid" }, rig.Svc.ArmedVenues);
        }

        [Fact]
        public async Task The_wait_is_the_users_and_is_clamped_to_what_the_switch_allows()
        {
            var rig = new Rig(seconds: 300);
            await rig.Svc.TickAsync();
            rig.Set(SettingsKeys.DeadMansSwitchSeconds, 5);       // below the floor
            await rig.Svc.TickAsync();

            Assert.Equal(new DateTimeOffset?[] { Now.AddSeconds(300), Now.AddSeconds(DeadMansSwitchService.MinSeconds) }, rig.Calls);
            // A changed wait is said again, with the wait it now has.
            Assert.Equal(2, rig.Said.Count);
            Assert.Contains($"{DeadMansSwitchService.MinSeconds} seconds", rig.Said[1].Message);
            Assert.Equal(DeadMansSwitchService.MaxSeconds, DeadMansSwitchService.ClampSeconds(99999));
        }

        [Fact]
        public async Task Switching_it_off_clears_it_on_the_venue_and_says_orders_stay()
        {
            var rig = new Rig();
            await rig.Svc.TickAsync();
            rig.Set(SettingsKeys.DeadMansSwitch, false);
            await rig.Svc.TickAsync();
            await rig.Svc.TickAsync();   // nothing armed any more: no further request

            Assert.Equal(new DateTimeOffset?[] { Now.AddSeconds(60), null }, rig.Calls);
            Assert.Contains("Dead-man's switch off for Hyperliquid", rig.Said[^1].Message);
            Assert.Contains("stay until you cancel them", rig.Said[^1].Message);
            Assert.Empty(rig.Svc.ArmedVenues);
        }

        [Fact]
        public async Task A_normal_close_clears_it_and_nothing_renews_it_afterwards()
        {
            var rig = new Rig();
            await rig.Svc.TickAsync();
            await rig.Svc.ClearAsync(TimeSpan.FromSeconds(5));
            await rig.Svc.TickAsync();

            Assert.Equal(new DateTimeOffset?[] { Now.AddSeconds(60), null }, rig.Calls);
            Assert.Empty(rig.Svc.ArmedVenues);
        }

        [Fact]
        public async Task Disposing_the_service_is_a_normal_close()
        {
            // The WebHost's circuit scope ends with DisposeAsync; the desktop calls ClearAsync.
            var rig = new Rig();
            await rig.Svc.TickAsync();
            await rig.Svc.DisposeAsync();
            Assert.Null(rig.Calls[^1]);
            Assert.Equal(2, rig.Calls.Count);
        }

        [Fact]
        public async Task A_close_with_nothing_armed_sends_nothing()
        {
            var rig = new Rig(on: false);
            await rig.Svc.TickAsync();
            await rig.Svc.ClearAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(rig.Calls);
        }

        [Fact]
        public async Task A_failed_renewal_is_said_once_with_the_time_the_old_one_fires_then_the_recovery()
        {
            var rig = new Rig();
            await rig.Svc.TickAsync();                    // armed, fires 18:01:00
            rig.NextError = "rate limited";
            rig.Clock.At = Now.AddSeconds(20);
            await rig.Svc.TickAsync();
            rig.Clock.At = Now.AddSeconds(40);
            await rig.Svc.TickAsync();                    // still failing: silent

            Assert.Equal(2, rig.Said.Count);
            var fail = rig.Said[1];
            Assert.Equal(SpeechChannel.Critical, fail.Channel);
            Assert.Contains("could not be renewed on Hyperliquid: rate limited", fail.Message);
            Assert.Contains("at 18:01:00 UTC", fail.Message);

            rig.NextError = null;
            await rig.Svc.TickAsync();
            Assert.Equal(3, rig.Said.Count);
            Assert.Contains("renewed on Hyperliquid again", rig.Said[2].Message);
        }

        [Fact]
        public async Task A_venue_that_throws_is_a_failure_in_words_not_a_crash()
        {
            var rig = new Rig { };
            rig.Throw = true;
            await rig.Svc.TickAsync();
            var said = Assert.Single(rig.Said);
            Assert.Equal(SpeechChannel.Critical, said.Channel);
            Assert.Contains("could not be set on Hyperliquid: no answer from Hyperliquid", said.Message);
            Assert.Contains("not covered", said.Message);
            Assert.Empty(rig.Svc.ArmedVenues);
        }

        [Fact]
        public async Task The_hosted_terminal_never_arms_it_even_with_the_setting_on()
        {
            var rig = new Rig(mode: HostMode.Hosted);
            await rig.Svc.TickAsync();
            Assert.Empty(rig.Calls);
        }

        [Fact]
        public async Task Only_a_venue_that_offers_it_with_a_signing_key_is_asked()
        {
            var noSwitch = new Rig(offersSwitch: false);
            await noSwitch.Svc.TickAsync();
            Assert.Empty(noSwitch.Said);

            var noKey = new Rig { CanSchedule = false };
            await noKey.Svc.TickAsync();
            Assert.Empty(noKey.Calls);

            var withdrawal = new Rig(withdrawalKey: true);   // a withdrawal profile never trades
            await withdrawal.Svc.TickAsync();
            Assert.Empty(withdrawal.Calls);
        }
    }
}
