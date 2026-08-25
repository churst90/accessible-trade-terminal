using System.Text;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Plugins.Gemini
{
    /// <summary>
    /// Gemini request signing. Separated from the provider because it is the one
    /// piece that is either exactly right or silently rejects every authenticated
    /// call, and it is worth testing on its own against vectors computed by an
    /// independent implementation.
    ///
    /// <para>
    /// The scheme is unusual in two ways worth naming. First, the request carries
    /// **no body**: every parameter, the endpoint path included, travels inside a
    /// base64-encoded JSON payload in the <c>X-GEMINI-PAYLOAD</c> header. Second,
    /// what is signed is the **base64 STRING** — not the raw JSON, and not the
    /// decoded bytes. HMAC-SHA384 keyed with the UTF-8 bytes of the secret (no
    /// base64-decoding of the secret, unlike both Kraken APIs), emitted as
    /// lowercase hex in <c>X-GEMINI-SIGNATURE</c>.
    /// </para>
    /// </summary>
    internal static class GeminiAuth
    {
        /// <summary>The <c>X-GEMINI-PAYLOAD</c> header value for a JSON payload.</summary>
        public static string Payload(string payloadJson) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));

        /// <summary>
        /// The <c>X-GEMINI-SIGNATURE</c> header value: lowercase hex of
        /// HMAC-SHA384 over the base64 payload string, keyed with the UTF-8 bytes
        /// of the API secret.
        /// </summary>
        public static string Sign(string payloadBase64, string apiSecret) =>
            RestSigning.HmacSha384Hex(apiSecret, payloadBase64);

        /// <summary>
        /// **SECONDS** since the epoch, not milliseconds — found live against the
        /// sandbox on the first signed call: keys created with Gemini's
        /// timestamp-nonce mode (the current default) reject anything not within
        /// 30 seconds of server time, and a millisecond value reads as the year
        /// 58,000. Seconds also satisfy the legacy increasing-integer mode, so
        /// this works for both key types. Monotonic within the process — two
        /// calls in the same second get n and n+1, still inside the 30-second
        /// window — because a repeated nonce is rejected as <c>InvalidNonce</c>,
        /// which reads like an auth problem and would be debugged in the wrong
        /// place.
        /// </summary>
        public static long Nonce()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            lock (NonceGate)
            {
                if (now <= _lastNonce) now = _lastNonce + 1;
                _lastNonce = now;
            }
            return now;
        }

        private static readonly object NonceGate = new();
        private static long _lastNonce;
    }
}
