using System.Security.Cryptography;
using System.Text;

namespace AccessibleTrader.Sdk.Services
{
    /// <summary>
    /// Shared REST signing/query primitives. Each exchange still owns its own
    /// signature RECIPE (what gets hashed, which header carries it), but the
    /// HMAC + hex/base64 + query-string mechanics were hand-rolled in every
    /// provider — this is the one place they live now.
    /// </summary>
    public static class RestSigning
    {
        public static byte[] HmacSha256(byte[] key, string message)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        }

        public static byte[] HmacSha512(byte[] key, byte[] message)
        {
            using var hmac = new HMACSHA512(key);
            return hmac.ComputeHash(message);
        }

        /// <summary>HMAC-SHA512(key, message) as base64 — the output shape both
        /// Kraken APIs put in their auth header (spot: API-Sign, futures: Authent).</summary>
        public static string HmacSha512Base64(byte[] key, byte[] message) =>
            Convert.ToBase64String(HmacSha512(key, message));

        /// <summary>SHA-256 over the UTF-8 bytes of <paramref name="message"/>.
        /// Both Kraken signature recipes digest their message with this before
        /// the HMAC step.</summary>
        public static byte[] Sha256(string message) =>
            SHA256.HashData(Encoding.UTF8.GetBytes(message));

        /// <summary>HMAC-SHA384(secret, message) as lowercase hex — Gemini's
        /// X-GEMINI-SIGNATURE shape. The secret is keyed as its UTF-8 bytes
        /// (NOT base64-decoded, unlike both Kraken APIs).</summary>
        public static string HmacSha384Hex(string secret, string message)
        {
            var key = Encoding.UTF8.GetBytes(secret);
            try
            {
                using var hmac = new HMACSHA384(key);
                return ToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
            }
            finally { Array.Clear(key, 0, key.Length); }
        }

        /// <summary>HMAC-SHA256(secret, message) as hex. Binance/MEXC use lowercase;
        /// Bitstamp uses uppercase.</summary>
        public static string HmacSha256Hex(string secret, string message, bool upperCase = false)
        {
            var key = Encoding.UTF8.GetBytes(secret);
            try
            {
                var hash = HmacSha256(key, message);
                return ToHex(hash, upperCase);
            }
            finally { Array.Clear(key, 0, key.Length); }
        }

        public static string ToHex(ReadOnlySpan<byte> bytes, bool upperCase = false)
        {
            var fmt = upperCase ? "X2" : "x2";
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>URL-encoded "k=v&amp;k2=v2" (no leading '?'). Insertion order preserved.</summary>
        public static string BuildQuery(IEnumerable<KeyValuePair<string, string>> parameters) =>
            string.Join("&", parameters.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));

        /// <summary>URL-encoded query prefixed with '?', or "" when empty.</summary>
        public static string QueryPrefixed(IReadOnlyDictionary<string, string>? parameters) =>
            parameters == null || parameters.Count == 0 ? string.Empty : "?" + BuildQuery(parameters);
    }
}
