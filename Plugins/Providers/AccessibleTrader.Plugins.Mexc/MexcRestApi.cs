using System.Globalization;
using System.Text;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        // ── Venue clock ──────────────────────────────────────────────────────

        /// <summary>
        /// Milliseconds to add to the local clock to get MEXC's. Zero until synced.
        ///
        /// <para>Both signing schemes here are clock-bound: spot sends
        /// <c>recvWindow=5000</c> with a local timestamp, and futures signs
        /// <c>apiKey + reqTime + params</c> where <c>reqTime</c> is likewise local. Nothing in
        /// the plugin tier ever synced against venue time, so a desktop whose clock had drifted
        /// more than five seconds — a laptop resuming from sleep, a VM with lazy NTP — had
        /// every signed call rejected at once: balances, positions and orders together.</para>
        /// </summary>
        private long _clockOffsetMs;
        private DateTime _clockSyncedAtUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _clockGate = new(1, 1);

        private static readonly TimeSpan ClockSyncInterval = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Syncs the offset against <c>/api/v3/time</c>, at most once per
        /// <see cref="ClockSyncInterval"/> unless <paramref name="force"/> is set. Best effort:
        /// a failed probe leaves the offset alone and signing proceeds on the local clock,
        /// which is the old behaviour and no worse.
        /// </summary>
        private async Task EnsureClockSyncedAsync(bool force = false)
        {
            if (!force && DateTime.UtcNow - _clockSyncedAtUtc < ClockSyncInterval) return;

            await _clockGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!force && DateTime.UtcNow - _clockSyncedAtUtc < ClockSyncInterval) return;

                long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string body = await _http.GetStringAsync(SpotBase + "/api/v3/time").ConfigureAwait(false);
                long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("serverTime", out var st)) return;

                // Midpoint of the round trip, so half the network latency does not land in
                // the offset.
                _clockOffsetMs = st.GetInt64() - (before + (after - before) / 2);
                _clockSyncedAtUtc = DateTime.UtcNow;
            }
            catch
            {
                // Best effort by design — never let a clock probe break a signed call.
            }
            finally
            {
                _clockGate.Release();
            }
        }

        private long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _clockOffsetMs;

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
            await EnsureClockSyncedAsync().ConfigureAwait(false);

            for (int attempt = 0; ; attempt++)
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
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // -700003 / -1021 is MEXC saying our timestamp is outside recvWindow. Re-sync
                // against server time and sign once more; retrying without re-syncing would
                // reproduce the same bad timestamp.
                if (attempt == 0
                    && (body.Contains("-1021", StringComparison.Ordinal)
                     || body.Contains("700003", StringComparison.Ordinal)))
                {
                    await EnsureClockSyncedAsync(force: true).ConfigureAwait(false);
                    continue;
                }

                return BodyOrThrow(response, body, path);
            }
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
            await EnsureClockSyncedAsync().ConfigureAwait(false);

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
            return BodyOrThrow(response, await response.Content.ReadAsStringAsync().ConfigureAwait(false), path);
        }

        /// <summary>
        /// A non-2xx whose body MEXC itself explains (spot's <c>code</c>+<c>msg</c>,
        /// futures' <c>success</c>) is returned so the caller classifies it — that is
        /// how a spot-only key's futures refusal stays a quiet no-access rather than a
        /// fault. Any OTHER non-2xx used to come back as an ordinary body: a proxy 502
        /// answering <c>{"message":"bad gateway"}</c> parsed in PlaceSpotOrderAsync as
        /// code-absent, which reads as success — an order announced as placed that the
        /// venue never booked. Only the path travels in the message; the signed query
        /// string never does.
        /// </summary>
        private static string BodyOrThrow(HttpResponseMessage response, string body, string path)
        {
            if (response.IsSuccessStatusCode) return body;

            try
            {
                var json = JObject.Parse(body);
                bool venueExplained = json["success"] != null
                    || (json["code"] != null && (json["msg"] ?? json["message"]) != null);
                if (venueExplained) return body;
            }
            catch (JsonReaderException) { }   // an HTML gateway page is not JSON

            throw new HttpRequestException(
                $"MEXC refused {path}: HTTP {(int)response.StatusCode}.", null, response.StatusCode);
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
