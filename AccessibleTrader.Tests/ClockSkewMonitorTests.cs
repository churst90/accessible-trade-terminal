using System.Net;
using System.Net.Http;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.WebHost.Services;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A computer clock that is off is said once, read from the Date header of venue answers
    /// (code review, 2026-11-20); <c>/healthz/deep</c> reports it to the server's operator.
    /// </summary>
    public class ClockSkewMonitorTests
    {
        private static readonly DateTime T0 = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        // An answer whose Date header is `ahead` seconds behind this computer's clock.
        private static double? Answer(ClockSkewMonitor m, double ahead, int i = 0, bool cached = false, string source = "Kraken")
        {
            var local = T0.AddMinutes(i);
            var server = new DateTimeOffset(local.AddSeconds(-ahead - 0.5), TimeSpan.Zero);
            return m.Observe(source, server, cached, local.AddMilliseconds(-100), local.AddMilliseconds(100));
        }

        [Fact]
        public void Three_answers_a_minute_off_are_said_once()
        {
            var errors = Substitute.For<IGlobalErrorCoordinator>();
            var m = new ClockSkewMonitor(errors);

            for (int i = 0; i < 6; i++) Answer(m, 60, i);

            errors.Received(1).ReportError(
                "This computer's clock is about 60 seconds fast next to Kraken's. "
                + "Venues refuse signed orders when the clock is off, and the newest bar's time will be wrong. "
                + "Set the clock to sync automatically.", ErrorSeverity.High);
            Assert.True(m.Warned);
            Assert.Equal(60, m.Last!.SecondsAhead, 3);
        }

        [Fact]
        public void A_slow_clock_says_slow_and_long_gaps_in_minutes()
        {
            Assert.Contains("about 5 minutes slow next to Coinbase's",
                ClockSkewMonitor.Sentence("Coinbase", -300));
        }

        [Fact]
        public void An_answer_in_step_breaks_the_streak()
        {
            var errors = Substitute.For<IGlobalErrorCoordinator>();
            var m = new ClockSkewMonitor(errors);

            Answer(m, 60, 0); Answer(m, 60, 1); Answer(m, 0.4, 2); Answer(m, 60, 3); Answer(m, 60, 4);

            errors.DidNotReceiveWithAnyArgs().ReportError(default!);
        }

        [Fact]
        public void Cached_answers_and_mixed_directions_do_not_count()
        {
            var errors = Substitute.For<IGlobalErrorCoordinator>();
            var m = new ClockSkewMonitor(errors);

            for (int i = 0; i < 5; i++) Answer(m, 600, i, cached: true);
            Answer(m, 60, 6); Answer(m, -60, 7); Answer(m, 60, 8); Answer(m, -60, 9);

            errors.DidNotReceiveWithAnyArgs().ReportError(default!);
        }

        [Fact]
        public void A_few_seconds_off_is_never_said()
        {
            var errors = Substitute.For<IGlobalErrorCoordinator>();
            var m = new ClockSkewMonitor(errors);

            for (int i = 0; i < 20; i++) Answer(m, ClockSkewMonitor.WarnSeconds - 1, i);

            errors.DidNotReceiveWithAnyArgs().ReportError(default!);
            Assert.False(m.Warned);
        }

        [Fact]
        public async Task The_handler_reads_the_Date_header_and_passes_the_answer_through()
        {
            var m = new ClockSkewMonitor();
            var answer = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            answer.Headers.Date = DateTimeOffset.UtcNow.AddMinutes(-10);
            using var http = new HttpClient(new ClockSkewHandler(m, "Kalshi", new Fixed(answer)));

            var got = await http.GetAsync("https://example.invalid/");

            Assert.Same(answer, got);
            Assert.Equal("Kalshi", m.Last!.Source);
            Assert.InRange(m.Last.SecondsAhead, 590, 610);
        }

        [Theory]
        [InlineData("AccessibleTrader.BlazorClient/Services/MauiPluginHttpClientFactory.cs")]
        [InlineData("AccessibleTrader.WebHost/Services/WebHostPluginHttpClientFactory.cs")]
        public void Both_plugin_web_client_factories_read_the_clock(string rel)
        {
            string text = File.ReadAllText(Path.Combine(RepoPaths.RepoRoot(), rel));
            Assert.Matches(@"new\s+(AccessibleTrader\.Core\.Services\.)?ClockSkewHandler\(", text);
        }

        [Fact]
        public void Deep_health_answers_only_on_the_server_itself()
        {
            static HttpContext Ctx(string ip, string? fwd = null)
            {
                var c = new DefaultHttpContext();
                c.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                if (fwd != null) c.Request.Headers["X-Forwarded-For"] = fwd;
                return c;
            }

            Assert.True(DeepHealth.IsLocalOperator(Ctx("127.0.0.1")));
            Assert.True(DeepHealth.IsLocalOperator(Ctx("::1")));
            Assert.False(DeepHealth.IsLocalOperator(Ctx("127.0.0.1", "203.0.113.9")));   // through nginx
            Assert.False(DeepHealth.IsLocalOperator(Ctx("203.0.113.9")));
        }

        [Fact]
        public void Deep_health_names_a_warned_clock_and_a_stale_leaderboard()
        {
            var m = new ClockSkewMonitor();
            for (int i = 0; i < 3; i++) Answer(m, -45, i, source: "Coinbase");
            string dir = TestTemp.NewDir("deephealth");
            string board = Path.Combine(dir, "leaderboard.json");
            File.WriteAllText(board, "{}");
            File.SetLastWriteTimeUtc(board, T0.AddMinutes(-40));

            string text = DeepHealth.Report(m, dir, board, T0.AddMinutes(4));

            Assert.Contains("version: ", text);
            Assert.Contains("clock: 45.0 s slow next to Coinbase, read 1 min ago; WARNED", text);
            Assert.Contains("disk: ", text);
            Assert.Contains("leaderboard: written 44 min ago; STALE", text);
        }

        private sealed class Fixed(HttpResponseMessage answer) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
                => Task.FromResult(answer);
        }
    }
}
