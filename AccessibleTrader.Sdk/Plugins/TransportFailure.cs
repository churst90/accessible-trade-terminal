namespace AccessibleTrader.Sdk.Plugins
{
    /// <summary>
    /// The one definition of "this failed because of the network, not because of us".
    ///
    /// <para>
    /// It exists because the pipeline's retry and circuit breaker are only as real as
    /// the exceptions that reach them, and for a long time none did: every provider's
    /// <c>FetchOhlcvAsync</c> ended in <c>catch (Exception) { report; return empty; }</c>,
    /// and so did <c>DataService.FetchOhlcvAsync</c> above them. Three layers of
    /// carefully configured Polly policy sat on top of a call that could not fail. The
    /// only symptom a user ever got from a dead network was a chart with no bars on it.
    /// </para>
    /// <para>
    /// A provider may still report and swallow anything that is genuinely ITS problem —
    /// a malformed payload, an unknown symbol, an auth refusal. What it must not do is
    /// eat the transport faults, because those are the ones a retry can fix and a
    /// breaker needs to count. <c>DataOrchestrator</c> tests the same predicate for its
    /// retry and breaker, so the definition cannot drift between the layer that throws
    /// and the layer that handles.
    /// </para>
    /// <para>
    /// Cancellation is deliberately NOT here. A tab switch cancelling an in-flight fetch
    /// is not a network fault: retrying it is wrong, counting it toward a breaker is
    /// wrong, and announcing "failed to load" for it is wrong. It has its own path.
    /// </para>
    /// </summary>
    public static class TransportFailure
    {
        /// <summary>
        /// True when <paramref name="ex"/> means the request never got a usable answer
        /// from the far end: a broken HTTP exchange, a dead socket, a stream fault, or a
        /// timeout. Inner exceptions count — <see cref="System.Net.Http.HttpRequestException"/>
        /// routinely wraps the socket error that actually happened, and a provider that
        /// re-wraps its own failures would otherwise hide one.
        ///
        /// <para>
        /// A 4xx is NOT transient, with two exceptions. 401/403 is a bad key, 404 is a
        /// symbol that does not exist: retrying cannot help, and counting them toward a
        /// breaker labelled "network issue" would announce the wrong thing and suspend a
        /// provider that is working perfectly. 408 (request timeout) and 429 (rate
        /// limited) are the two where waiting IS the fix, so they stay transient — 429
        /// is also what the rate-limit breaker is for.
        /// </para>
        /// </summary>
        public static bool IsTransient(Exception? ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is System.Net.Http.HttpRequestException http)
                {
                    // A null StatusCode means the exchange never completed — DNS, TLS,
                    // connection refused, or a provider constructing its own. Those are
                    // the most transient failures there are.
                    if (http.StatusCode is not { } status) return true;
                    int code = (int)status;
                    if (code is 408 or 429) return true;
                    if (code >= 400 && code < 500) continue; // permanent; keep unwrapping
                    return true;
                }
                if (e is System.Net.Sockets.SocketException
                    or System.IO.IOException
                    or TimeoutException)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
