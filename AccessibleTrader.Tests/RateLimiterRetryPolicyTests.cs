using System.Net;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Which failures <see cref="RateLimiter"/> is willing to repeat.</b>
    ///
    /// <para>
    /// ── Why this file exists ───────────────────────────────────────────────────
    /// A2d/D20: making <c>ShouldRetry</c>'s 4xx branch unreachable — so every client error fell
    /// through to "retry" — left the full suite green. <c>ProviderStatusCodeClassificationTests</c>
    /// covers the other half of that same defect (<c>TransportFailure.IsTransient</c>) and its own
    /// summary says <i>"RateLimiter.ShouldRetry was defeated the same way and retried a known-bad
    /// key three times"</i> — but the limiter's own decision was never asserted anywhere. The
    /// fixed classifier had one caller under test and one not.
    /// </para>
    ///
    /// <para>
    /// The consequence is the one that documentation calls out: a 401 from an expired key is sent
    /// four times with exponential backoff, so the real error reaches the user seconds late,
    /// wearing the wrong description, after the venue has counted four failed authentications
    /// against the key. 429 and 408 are pinned in the same theory, because a fix that stopped
    /// retrying everything would be exactly as wrong in the other direction — waiting IS the
    /// remedy for a rate limit.
    /// </para>
    /// </summary>
    public class RateLimiterRetryPolicyTests
    {
        private static RateLimiter Unthrottled() => new(1000, TimeSpan.FromMilliseconds(1));

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, 1)]      // bad key — repeating cannot help
        [InlineData(HttpStatusCode.Forbidden, 1)]
        [InlineData(HttpStatusCode.NotFound, 1)]          // no such symbol — same
        [InlineData(HttpStatusCode.BadRequest, 1)]
        [InlineData((HttpStatusCode)429, 2)]              // rate limited — waiting IS the fix
        [InlineData(HttpStatusCode.RequestTimeout, 2)]
        [InlineData(HttpStatusCode.BadGateway, 2)]        // 5xx — genuinely transient
        [InlineData(HttpStatusCode.ServiceUnavailable, 2)]
        public async Task Only_a_failure_repeating_could_fix_is_repeated(HttpStatusCode status, int expectedAttempts)
        {
            var limiter = Unthrottled();
            int attempts = 0;

            var ex = await Record.ExceptionAsync(() => limiter.ExecuteAsync<int>(() =>
            {
                attempts++;
                throw new HttpRequestException($"venue said {(int)status}", null, status);
            }, maxRetries: 1));

            Assert.IsType<HttpRequestException>(ex);
            Assert.Equal(expectedAttempts, attempts);
        }

        [Fact]
        public async Task A_transport_fault_with_no_status_never_reached_the_venue_so_it_is_retried()
        {
            var limiter = Unthrottled();
            int attempts = 0;

            await Record.ExceptionAsync(() => limiter.ExecuteAsync<int>(() =>
            {
                attempts++;
                throw new HttpRequestException("connection reset");
            }, maxRetries: 1));

            Assert.Equal(2, attempts);
        }

        [Fact]
        public async Task Caller_cancellation_stops_immediately_but_a_client_timeout_is_retried()
        {
            var limiter = Unthrottled();

            // An HttpClient timeout surfaces as OperationCanceledException with nothing cancelled.
            int timeoutAttempts = 0;
            await Record.ExceptionAsync(() => limiter.ExecuteAsync<int>(() =>
            {
                timeoutAttempts++;
                throw new OperationCanceledException();
            }, maxRetries: 1));
            Assert.Equal(2, timeoutAttempts);

            // The caller's own token is the opposite case — it means stop, not try again.
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            int cancelledAttempts = 0;
            await Record.ExceptionAsync(() => limiter.ExecuteAsync<int>(() =>
            {
                cancelledAttempts++;
                throw new OperationCanceledException();
            }, maxRetries: 3, ct: cts.Token));
            Assert.True(cancelledAttempts <= 1,
                $"a cancelled caller must not be retried, ran {cancelledAttempts} times");
        }

        [Fact]
        public async Task A_success_after_a_retryable_failure_returns_the_value()
        {
            var limiter = Unthrottled();
            int attempts = 0;

            int value = await limiter.ExecuteAsync(() =>
            {
                attempts++;
                if (attempts == 1) throw new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway);
                return Task.FromResult(42);
            }, maxRetries: 2);

            Assert.Equal(42, value);
            Assert.Equal(2, attempts);
        }
    }
}
