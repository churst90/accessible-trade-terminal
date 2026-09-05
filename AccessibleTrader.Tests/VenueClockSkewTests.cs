using System.Net;
using System.Reflection;
using System.Web;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A signed request is timestamped against the VENUE's clock, not the desktop's.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Binance and MEXC both sign a hardcoded <c>recvWindow=5000</c> against
    /// <c>DateTimeOffset.UtcNow</c>, and the venue rejects anything landing outside that
    /// five-second window with <c>-1021 Timestamp for this request is outside of the
    /// recvWindow</c>. Grepping <c>serverTime</c> / <c>/api/v3/time</c> / <c>timeOffset</c>
    /// across the whole plugin fleet returned <b>zero hits</b> — no provider ever synced
    /// against venue time.
    /// </para>
    ///
    /// <para>
    /// A desktop whose clock has drifted more than five seconds is not exotic: a laptop
    /// resuming from sleep or a VM with lazy NTP both do it routinely. The symptom is that
    /// balances, positions <i>and</i> orders all start failing at the same moment, and — because
    /// the exception carried no status code, the sibling finding in this pass — the failure was
    /// announced as a network problem. A blind user is told their connection is bad when their
    /// clock is wrong, and nothing they can do about the network will help.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// The fake venue reports a server time deliberately far from the local clock, and the test
    /// reads the <c>timestamp</c> that actually went out on the signed request. The offset is
    /// large (an hour) so it cannot be confused with round-trip latency; the tolerance is
    /// generous for the same reason. There is also a <c>-1021</c> case, because syncing at
    /// connect and never again would still strand a session whose clock drifts mid-run.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class VenueClockSkewTests
    {
        /// <summary>An hour ahead — unmistakably not latency.</summary>
        private const long SkewMs = 3_600_000;

        private static void Swap(object target, FakeHttpMessageHandler handler)
        {
            HttpClientSwap.ReplaceAll(target, handler);
        }

        private static long TimestampOf(HttpRequestMessage req)
        {
            var q = HttpUtility.ParseQueryString(req.RequestUri!.Query);
            var ts = q["timestamp"];
            Assert.False(string.IsNullOrEmpty(ts), $"No timestamp on {req.RequestUri}");
            return long.Parse(ts!);
        }

        private static AccessibleTrader.Plugins.Binance.BinanceProvider Binance(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "k", ["ApiSecret"] = "s" });
            Swap(p, h);
            return p;
        }

        [Fact]
        public async Task Binance_signs_against_venue_server_time_when_the_local_clock_has_drifted()
        {
            long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + SkewMs;

            var h = new FakeHttpMessageHandler()
                .Get(@"/api/v3/time", $$"""{"serverTime":{{serverNow}}}""")
                .Get(@"/api/v3/account", """{"balances":[]}""");

            await Binance(h).GetBalancesAsync();

            var signed = h.Captured.FirstOrDefault(r => r.RequestUri!.AbsolutePath.Contains("/account"));
            Assert.NotNull(signed);

            long sent = TimestampOf(signed!);
            // Must be near the VENUE's clock, not the machine's.
            Assert.InRange(sent, serverNow - 30_000, serverNow + 30_000);
        }

        [Fact]
        public async Task Binance_resyncs_and_retries_once_when_the_venue_answers_minus_1021()
        {
            // The venue's clock moves mid-session: the first probe is honest, the account call
            // is refused with -1021 anyway, and the second probe reports the new truth. Syncing
            // only at connect would leave the session permanently unable to sign.
            long firstProbe  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long secondProbe = firstProbe + SkewMs;

            int probeCalls = 0;
            int accountCalls = 0;

            var h = new FakeHttpMessageHandler()
                .Add(HttpMethod.Get, @"/api/v3/time", _ =>
                {
                    long t = probeCalls++ == 0 ? firstProbe : secondProbe;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($$"""{"serverTime":{{t}}}""",
                                                    System.Text.Encoding.UTF8, "application/json"),
                    };
                })
                .Add(HttpMethod.Get, @"/api/v3/account", _ =>
                {
                    if (accountCalls++ == 0)
                        return new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent(
                                """{"code":-1021,"msg":"Timestamp for this request is outside of the recvWindow."}""",
                                System.Text.Encoding.UTF8, "application/json"),
                        };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"balances":[]}""",
                                                    System.Text.Encoding.UTF8, "application/json"),
                    };
                });

            await Binance(h).GetBalancesAsync();

            Assert.Equal(2, accountCalls);  // refused once, retried once
            Assert.Equal(2, probeCalls);    // and the retry was preceded by a forced re-sync

            var retried = h.Captured.Last(r => r.RequestUri!.AbsolutePath.Contains("/account"));
            Assert.InRange(TimestampOf(retried), secondProbe - 30_000, secondProbe + 30_000);
        }

        [Fact]
        public async Task Binance_a_failed_clock_probe_does_not_break_the_signed_call()
        {
            // Best effort by design: if the probe cannot be answered the call still goes out on
            // the local clock, which is the pre-fix behaviour and no worse. A clock probe that
            // could block trading would be a worse bug than the one it fixes.
            var h = new FakeHttpMessageHandler()
                .Get(@"/api/v3/time", "not json at all", HttpStatusCode.ServiceUnavailable)
                .Get(@"/api/v3/account", """{"balances":[]}""");

            var ex = await Record.ExceptionAsync(() => Binance(h).GetBalancesAsync());

            Assert.Null(ex);
            Assert.Contains(h.Captured, r => r.RequestUri!.AbsolutePath.Contains("/account"));
        }

        // ── MEXC ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Mexc_signs_against_venue_server_time_when_the_local_clock_has_drifted()
        {
            long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + SkewMs;

            var h = new FakeHttpMessageHandler()
                .Get(@"/api/v3/time", $$"""{"serverTime":{{serverNow}}}""")
                .Get(@"/api/v3/account", """{"balances":[]}""");

            var api = new AccessibleTrader.Plugins.Mexc.MexcRestApi(new HttpClient(h));

            await api.SpotSignedAsync(HttpMethod.Get, "/api/v3/account", "k", "s");

            var signed = h.Captured.First(r => r.RequestUri!.AbsolutePath.Contains("/account"));
            Assert.InRange(TimestampOf(signed), serverNow - 30_000, serverNow + 30_000);
        }
    }
}
