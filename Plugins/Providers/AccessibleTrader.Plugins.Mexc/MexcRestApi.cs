using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Plugins.Mexc
{
    /// <summary>
    /// Direct MEXC REST client — replaces the JK.Mexc.Net/CryptoExchange.Net SDK so
    /// the plugin carries no shared third-party exchange library that could clash in
    /// the flattened plugin output directory. Spot (api.mexc.com) is Binance-style:
    /// HMAC-SHA256 over the full query string, key in the X-MEXC-APIKEY header. Futures
    /// (contract.mexc.com) signs differently: HMAC-SHA256 of (apiKey + timestamp +
    /// paramString) in a Signature header.
    ///
    /// Credentials are supplied per call (sign-time checkout) and never stored here.
    /// </summary>
    internal sealed class MexcRestApi
    {
        internal const string SpotBase    = "https://api.mexc.com";
        internal const string FuturesBase = "https://contract.mexc.com";

        private readonly HttpClient _http;

        public MexcRestApi(HttpClient http) => _http = http;

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // ── Spot: public ─────────────────────────────────────────────────────

        public Task<string> SpotGetAsync(string path, IReadOnlyDictionary<string, string>? query = null)
            => _http.GetStringAsync(SpotBase + path + RestSigning.QueryPrefixed(query));

        // ── Spot: signed ─────────────────────────────────────────────────────

        /// <summary>
        /// Signs and sends a spot request. All params + a timestamp are formed into a
        /// query string, HMAC-SHA256'd with the secret, and the signature appended.
        /// Body is sent as query params (MEXC accepts params in the query for POST/DELETE).
        /// </summary>
        public async Task<string> SpotSignedAsync(HttpMethod method, string path, string apiKey, string apiSecret,
            IReadOnlyDictionary<string, string>? parameters = null)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            if (parameters != null) pairs.AddRange(parameters);
            pairs.Add(new("timestamp", NowMs().ToString(CultureInfo.InvariantCulture)));
            pairs.Add(new("recvWindow", "5000"));

            string queryString = RestSigning.BuildQuery(pairs);
            string signature = RestSigning.HmacSha256Hex(apiSecret, queryString);
            string url = $"{SpotBase}{path}?{queryString}&signature={signature}";

            using var request = new HttpRequestMessage(method, url);
            request.Headers.Add("X-MEXC-APIKEY", apiKey);
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        // ── Futures ──────────────────────────────────────────────────────────

        public Task<string> FuturesGetAsync(string path, IReadOnlyDictionary<string, string>? query = null)
            => _http.GetStringAsync(FuturesBase + path + RestSigning.QueryPrefixed(query));

        /// <summary>
        /// Signs and sends a futures request. Signature = HMAC-SHA256(apiKey + reqTime +
        /// paramString); paramString is the sorted query for GET or the raw JSON body for
        /// POST. Headers carry ApiKey / Request-Time / Signature / Content-Type.
        /// </summary>
        public async Task<string> FuturesSignedAsync(HttpMethod method, string path, string apiKey, string apiSecret,
            IReadOnlyDictionary<string, string>? query = null, string? jsonBody = null)
        {
            string reqTime = NowMs().ToString(CultureInfo.InvariantCulture);
            string paramString = jsonBody
                ?? (query != null && query.Count > 0
                    ? string.Join("&", query.OrderBy(k => k.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"))
                    : string.Empty);
            string signature = RestSigning.HmacSha256Hex(apiSecret, apiKey + reqTime + paramString);

            string url = FuturesBase + path + (jsonBody == null ? RestSigning.QueryPrefixed(query) : string.Empty);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Add("ApiKey", apiKey);
            request.Headers.Add("Request-Time", reqTime);
            request.Headers.Add("Signature", signature);
            if (jsonBody != null)
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        // ── Interval tokens ──────────────────────────────────────────────────

        /// <summary>Spot REST kline interval token (1m, 5m, 15m, 30m, 60m, 4h, 1d, 1W, 1M).</summary>
        internal static string SpotRestInterval(string timeframe) => timeframe switch
        {
            "1m"  => "1m",  "5m"  => "5m",  "15m" => "15m", "30m" => "30m",
            "1h"  => "60m", "4h"  => "4h",  "1d"  => "1d",  "1w"  => "1W", "1M" => "1M",
            _     => "60m",
        };

        /// <summary>Spot WS kline interval token used in the channel name
        /// (spot@public.kline.v3.api.pb@SYMBOL@Min1). Per the official proto:
        /// Min1/Min5/Min15/Min30/Min60/Hour4/Hour8/Day1/Week1/Month1.</summary>
        internal static string SpotWsInterval(string timeframe) => timeframe switch
        {
            "1m"  => "Min1",  "5m" => "Min5", "15m" => "Min15", "30m" => "Min30",
            "1h"  => "Min60", "4h" => "Hour4", "8h" => "Hour8",
            "1d"  => "Day1",  "1w" => "Week1", "1M" => "Month1",
            _     => "Min60",
        };

        /// <summary>Futures kline/WS interval token (Min1..Month1).</summary>
        internal static string FuturesInterval(string timeframe) => timeframe switch
        {
            "1m"  => "Min1",  "5m" => "Min5", "15m" => "Min15", "30m" => "Min30",
            "1h"  => "Min60", "4h" => "Hour4", "8h" => "Hour8",
            "1d"  => "Day1",  "1w" => "Week1", "1M" => "Month1",
            _     => "Min60",
        };
    }
}
