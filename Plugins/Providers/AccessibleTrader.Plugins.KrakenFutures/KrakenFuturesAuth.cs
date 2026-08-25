using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Plugins.KrakenFutures
{
    /// <summary>
    /// Kraken Futures request signing. Separated from the provider because it is
    /// the one piece here that is either exactly right or silently rejects every
    /// authenticated call, and it is worth being able to test on its own against
    /// the vector in Kraken's own documentation.
    ///
    /// <para>
    /// **It is not the spot algorithm.** Kraken Futures is a different API with
    /// different keys and a different signature: spot signs
    /// <c>HMAC-SHA512(path + SHA256(nonce + postdata))</c> with the nonce inside
    /// the POST body, while futures signs
    /// <c>HMAC-SHA512(SHA256(postData + nonce + endpointPath))</c> and passes the
    /// key and signature as headers. Reusing the spot signer here would fail
    /// authentication on every call with no hint as to why.
    /// </para>
    /// </summary>
    internal static class KrakenFuturesAuth
    {
        /// <summary>
        /// The <c>Authent</c> header value.
        /// </summary>
        /// <param name="postData">
        /// Query parameters as they appear in the request, url-encoded and joined
        /// with <c>&amp;</c>. Kraken changed this on 2024-02-20 to hash the ENCODED
        /// form — a value containing a space must be hashed as <c>hello%20world</c>,
        /// not as <c>hello world</c>, or the signature will not match.
        /// </param>
        /// <param name="endpointPath">
        /// The path WITHOUT the <c>/derivatives</c> prefix — <c>/api/v3/orderbook</c>,
        /// even though the request goes to <c>/derivatives/api/v3/orderbook</c>.
        /// Getting this wrong is the most likely single cause of a 401 here.
        /// </param>
        public static string Sign(string postData, string nonce, string endpointPath, string apiSecretBase64)
        {
            // 1. SHA-256 over the concatenation, in this exact order.
            byte[] sha = RestSigning.Sha256(postData + nonce + endpointPath);

            // 2. HMAC-SHA-512 of that digest, keyed with the base64-DECODED secret.
            byte[] key = Convert.FromBase64String(apiSecretBase64);
            try { return RestSigning.HmacSha512Base64(key, sha); }
            finally { Array.Clear(key, 0, key.Length); }
        }

        /// <summary>
        /// Milliseconds since the epoch, which is what Kraken Futures expects a
        /// nonce to be. Monotonic within the process so two calls in the same
        /// millisecond cannot collide — a repeated nonce is rejected, and the
        /// failure looks like a bad key rather than a timing problem.
        /// </summary>
        public static string Nonce()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (NonceGate)
            {
                if (now <= _lastNonce) now = _lastNonce + 1;
                _lastNonce = now;
            }
            return now.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static readonly object NonceGate = new();
        private static long _lastNonce;
    }
}
