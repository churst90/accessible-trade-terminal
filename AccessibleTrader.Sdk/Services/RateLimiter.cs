namespace AccessibleTrader.Sdk.Services
{
    /// <summary>
    /// Token-bucket rate limiter with exponential backoff for API calls.
    /// Each provider creates its own instance with exchange-specific limits.
    /// </summary>
    public sealed class RateLimiter
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly int _maxRequestsPerWindow;
        private readonly TimeSpan _window;
        private int _requestCount;
        private DateTime _windowStart;
        private int _consecutiveFailures;

        /// <summary>
        /// Creates a rate limiter.
        /// </summary>
        /// <param name="maxRequestsPerWindow">Maximum requests allowed in <paramref name="window"/>.</param>
        /// <param name="window">Sliding window duration.</param>
        public RateLimiter(int maxRequestsPerWindow, TimeSpan window)
        {
            _maxRequestsPerWindow = maxRequestsPerWindow;
            _window = window;
            _windowStart = DateTime.UtcNow;
        }

        /// <summary>
        /// Waits until a request slot is available, then returns.
        /// Throws <see cref="OperationCanceledException"/> if <paramref name="ct"/> fires.
        /// </summary>
        public async Task WaitAsync(CancellationToken ct = default)
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                if (now - _windowStart >= _window)
                {
                    _requestCount = 0;
                    _windowStart = now;
                }

                if (_requestCount >= _maxRequestsPerWindow)
                {
                    var waitTime = _window - (now - _windowStart);
                    if (waitTime > TimeSpan.Zero)
                        await Task.Delay(waitTime, ct).ConfigureAwait(false);
                    _requestCount = 0;
                    _windowStart = DateTime.UtcNow;
                }

                _requestCount++;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Call after a successful request to reset the backoff counter.
        /// </summary>
        public void ReportSuccess()
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }

        /// <summary>
        /// Call after a failed request. Returns the recommended backoff delay
        /// using exponential backoff with jitter (base 500ms, max 30s).
        /// </summary>
        public TimeSpan ReportFailure()
        {
            int failures = Interlocked.Increment(ref _consecutiveFailures);
            double baseMs = 500 * Math.Pow(2, Math.Min(failures - 1, 6)); // max ~32s
            double jitter = baseMs * 0.2 * Random.Shared.NextDouble();
            return TimeSpan.FromMilliseconds(Math.Min(baseMs + jitter, 30_000));
        }

        /// <summary>
        /// Executes <paramref name="action"/> with rate limiting and automatic retry
        /// on failure with exponential backoff (up to <paramref name="maxRetries"/> attempts).
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, int maxRetries = 3, CancellationToken ct = default)
        {
            for (int attempt = 0; ; attempt++)
            {
                await WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var result = await action().ConfigureAwait(false);
                    ReportSuccess();
                    return result;
                }
                catch (Exception ex) when (attempt < maxRetries && ShouldRetry(ex, ct))
                {
                    var backoff = ReportFailure();
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Takes a rate-limit slot and runs <paramref name="action"/> <b>exactly once</b>. Never
        /// retries, for any exception, ever.
        ///
        /// <para>
        /// ── Why this exists as its own method ──────────────────────────────────
        /// <see cref="ExecuteAsync{T}"/> retries on network faults and — deliberately, see
        /// <c>ShouldRetry</c> — on an <see cref="OperationCanceledException"/> whose token was not
        /// cancelled, which is exactly the shape of an <see cref="System.Net.Http.HttpClient"/>
        /// timeout. For a GET that is right. For a POST that <i>creates</i> something — an order, a
        /// withdrawal — it is the worst possible behaviour: the venue booked the request, the
        /// response was lost to the timeout, and the retry books it again. The caller hears "Order
        /// placed" once and holds twice the position. <c>GeneralOrderService</c>'s dedup gate sits
        /// <i>above</i> <c>PlaceOrderAsync</c> and cannot see a retry that happens inside one call.
        /// </para>
        ///
        /// <para>
        /// Passing <c>maxRetries: 0</c> to <see cref="ExecuteAsync{T}"/> is equivalent in behaviour
        /// and was rejected on purpose: it is a magic argument that reads as a tuning knob, it is
        /// silently lost by any refactor that re-wraps the lambda, and it is not greppable as an
        /// intent. This name states the rule at the call site, and
        /// <c>OrderPostRetrySafetyTests</c> enforces that every order- and withdrawal-creating
        /// method uses it.
        /// </para>
        ///
        /// <para>
        /// A lost response is still ambiguous — this method makes the ambiguity <i>singular</i>
        /// rather than multiplying it. Recovering from it is the caller's job
        /// (<c>GeneralOrderService</c> scans open orders and returns <c>ORDER_UNCERTAIN</c>).
        /// </para>
        /// </summary>
        public async Task<T> ExecuteOnceAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
        {
            await WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var result = await action().ConfigureAwait(false);
                ReportSuccess();
                return result;
            }
            catch
            {
                // Still feed the backoff counter: a failing venue should slow the NEXT
                // call down even though this one will not be repeated.
                ReportFailure();
                throw;
            }
        }

        /// <summary>
        /// Whether a failed attempt is worth retrying. Client errors (4xx) are NOT
        /// retried — a 400/401/403/404 just hammers the endpoint and delays the real
        /// error reaching the user — EXCEPT 429 (Too Many Requests) and 408 (Request
        /// Timeout), which are transient. Caller cancellation propagates immediately;
        /// an HttpClient timeout (OperationCanceledException with no cancellation
        /// requested) is retried. Network errors and 5xx are retried.
        /// (Retry-After can't be honoured here — the limiter runs an opaque action
        /// with no access to the response headers; providers that need it must read
        /// it at the call site.)
        /// </summary>
        private static bool ShouldRetry(Exception ex, CancellationToken ct)
        {
            if (ex is OperationCanceledException)
                return !ct.IsCancellationRequested;

            if (ex is System.Net.Http.HttpRequestException hre && hre.StatusCode is { } sc)
            {
                int code = (int)sc;
                if (code >= 400 && code < 500)
                    return sc is System.Net.HttpStatusCode.TooManyRequests
                              or System.Net.HttpStatusCode.RequestTimeout;
            }
            return true;
        }

        /// <summary>
        /// Executes a void action with rate limiting and automatic retry.
        /// </summary>
        public async Task ExecuteAsync(Func<Task> action, int maxRetries = 3, CancellationToken ct = default)
        {
            await ExecuteAsync(async () => { await action().ConfigureAwait(false); return true; }, maxRetries, ct).ConfigureAwait(false);
        }
    }
}
