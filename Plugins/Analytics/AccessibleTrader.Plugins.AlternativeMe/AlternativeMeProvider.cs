using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.AlternativeMe
{
    /// <summary>
    /// Crypto Fear &amp; Greed Index from alternative.me. Single integer 0-100 per day,
    /// where 0 = extreme fear and 100 = extreme greed. History back to 2018-02-01.
    ///
    /// ── Why this matters for strategies ─────────────────────────────────────────
    /// Sentiment is one of the most genuinely orthogonal signals available to a price-
    /// based strategy because it's computed from off-chain inputs (volatility surveys,
    /// social media volume, search trends, market momentum surveys). Extreme readings
    /// are mean-reverting:
    ///
    ///   • Index &lt; 20 (extreme fear) + Cipher oversold = high-conviction long
    ///   • Index &gt; 80 (extreme greed) + Cipher overbought = high-conviction short
    ///   • Mid-range readings (40-60) carry essentially no information
    ///
    /// The signal-to-noise ratio is highest at the extremes — that's where score-based
    /// strategies should weight it.
    ///
    /// ── Endpoint ────────────────────────────────────────────────────────────────
    /// GET https://api.alternative.me/fng/?limit=0&amp;date_format=us
    ///   • limit=0 returns the FULL historical series (currently ~3000 daily values)
    ///   • No API key, no auth, no rate limit documented (treat as ~60/min to be polite)
    ///   • Free, no signup
    ///
    /// ── Symbol convention ───────────────────────────────────────────────────────
    /// Single symbol: "FNG_INDEX". The provider only serves one series; the symbol
    /// list returns just this one value so the dropdown is unambiguous.
    /// </summary>
    public class AlternativeMeProvider : BaseMarketDataProvider
    {
        // Host-provided HttpClient with 32 MB cap, 60 s timeout, and an
        // outbound-host allow-list. Any future code path that interpolates
        // user input into a URL is blocked from reaching a non-allow-listed
        // host at the handler level — see IPluginHttpClientFactory.
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "AlternativeMe",
            allowedHosts: new[] { "api.alternative.me" });

        private readonly RateLimiter _rateLimiter = new(60, TimeSpan.FromMinutes(1));

        private const string FngUrl = "https://api.alternative.me/fng/?limit=0";
        private const string SymbolFng = "FNG_INDEX";

        public override string Name        => "AlternativeMe";
        public override string Description => "Crypto Fear & Greed Index (daily 0-100 sentiment, history to 2018).";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Sentiment };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 5000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        // Human-readable labels — used as the Price series FriendlyName on analytics
        // tabs so speech reads "Fear and Greed Index, 47" instead of "Price, 47".
        // `override` is required (not just `public`) because DataShape/GetSymbolDisplayName
        // are virtual on BaseMarketDataProvider — a plain shadow would not propagate
        // through the IMarketDataProvider interface vtable.
        public override string GetSymbolDisplayName(string symbol) => symbol switch
        {
            "FNG_INDEX" => "Fear & Greed Index",
            "FNG"       => "Fear & Greed Index",  // legacy alias
            _           => symbol
        };

        // ── Render hints for the Fear & Greed Index ────────────────────────────────
        // FNG is a bounded oscillator: 0 = extreme fear, 100 = extreme greed, 50
        // neutral. The conventional interpretation treats <25 as a buy zone and >75
        // as a sell zone. We declare hard range bounds so the chart always shows the
        // full 0–100 scale (even when current data is 10–60), plus three reference
        // levels at 25 / 50 / 75. Levels named "Oversold" / "Overbought" are picked
        // up by AudioZoneHelper for OB/OS sonification (pink noise in the zone,
        // earcon click on crossing). Speech template formats value to integer since
        // FNG has no sub-unit precision.
        public override SymbolRenderHints? GetSymbolRenderHints(string symbol)
        {
            if (symbol != "FNG_INDEX" && symbol != "FNG") return null;
            return new SymbolRenderHints(
                RangeMin:      0,
                RangeMax:      100,
                DisplayType:   ComponentDisplayType.Oscillator,
                SpeechTemplate:"{name}. {value:F0}.",
                ColorHex:      "#FFD54F",
                ReferenceLevels: new[]
                {
                    new LevelDescriptor(
                        Name: "Oversold (Extreme Fear)", Value: 25,
                        ColorHex: "#26A69A", Dash: DashStyle.Dash,
                        PlayEarcon: true, EarconVolume: 0.6f,
                        ZoneNoiseAmount: 0.25f, ZoneNoiseType: "pink"),
                    new LevelDescriptor(
                        Name: "Neutral", Value: 50,
                        ColorHex: "#888888", Dash: DashStyle.Dot),
                    new LevelDescriptor(
                        Name: "Overbought (Extreme Greed)", Value: 75,
                        ColorHex: "#EF5350", Dash: DashStyle.Dash,
                        PlayEarcon: true, EarconVolume: 0.6f,
                        ZoneNoiseAmount: 0.25f, ZoneNoiseType: "pink"),
                });
        }

        public override List<string> NativelySupportedTimeframes => new()
        {
            // Daily is the only resolution the upstream provides. Anything finer is the
            // same value repeated; anything coarser would aggregate days into weeks.
            StandardTimeframes.OneDay
        };

        public override void Configure(Dictionary<string, string> config) { }

        public override Task EnsureConnectedAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;

        public override Task DisconnectAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
            => Task.FromResult(new List<string> { SymbolFng });

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var json = await _http.GetStringAsync(FngUrl);
                    var root = JObject.Parse(json);
                    var dataArr = root["data"] as JArray;
                    if (dataArr == null) return (new List<Ohlcv>(), new List<(long, double)>());

                    // The API returns newest-first; reverse so OHLCV is in chronological order
                    // (older → newer), which is what every downstream renderer / indicator
                    // expects. Then optionally clip to the requested date range.
                    var bars = new List<Ohlcv>(dataArr.Count);
                    foreach (var entry in dataArr)
                    {
                        long ts = long.TryParse(entry["timestamp"]?.ToString(), out var t) ? t : 0;
                        double val = double.TryParse(entry["value"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
                        if (double.IsNaN(val)) continue;
                        var date = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                        bars.Add(new Ohlcv(date, val, val, val, val, 0));
                    }
                    bars.Reverse();

                    if (request.Since.HasValue)
                    {
                        var sinceDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime;
                        bars = bars.Where(b => b.Date >= sinceDt).ToList();
                    }
                    if (request.Until.HasValue)
                    {
                        var untilDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime;
                        bars = bars.Where(b => b.Date <= untilDt).ToList();
                    }

                    var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                    return (bars, vols);
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"AlternativeMe fetch error: {ex.Message}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }
    }
}
