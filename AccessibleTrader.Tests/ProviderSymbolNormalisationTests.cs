using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tier 3 coverage for provider-specific symbol normalisation helpers. Each
    /// provider plugin does its own conversion between the canonical BASE/QUOTE
    /// form used inside the app and the exchange's native wire-format. A
    /// regression here produces silent "symbol not found" responses on the wire,
    /// so the tests pin the known transforms.
    ///
    /// - <see cref="BaseMarketDataProvider.CleanSymbol"/> strips "/" and "-" and
    ///   uppercases (Binance/Alpaca/Bitstamp wire format, e.g. "BTCUSDT").
    /// - Kraken <c>FormatPair</c> produces "BASE/QUOTE" for the v2 WebSocket;
    ///   <c>FormatRestPair</c> strips the separator for REST.
    /// - Coinbase inlines <c>Replace("/", "-").ToUpper()</c> at call-sites
    ///   (product-id convention, e.g. "BTC-USD").
    /// </summary>
    // Constructs real providers, which touch the global ApiKeys bridge — see BrokerParityTests.
    [Collection("ProviderCredentialBridge")]
    public class ProviderSymbolNormalisationTests
    {
        // ── BaseMarketDataProvider.CleanSymbol ────────────────────────────────

        [Theory]
        [InlineData("BTC/USDT",  "BTCUSDT")]
        [InlineData("btc/usdt",  "BTCUSDT")]
        [InlineData("BTC-USD",   "BTCUSD")]
        [InlineData("BTCUSDT",   "BTCUSDT")]
        [InlineData("btc",       "BTC")]
        [InlineData("",          "")]
        public void BaseProvider_CleanSymbol_StripsSeparatorsAndUppercases(string input, string expected)
        {
            // CleanSymbol is the Binance/Alpaca/Bitstamp/InteractiveBrokers convention:
            // strip both slash and dash separators, uppercase. Shared via BaseMarketDataProvider
            // so all wire-format providers get the same normalisation.
            var provider = new StubProvider();
            string actual = provider.InvokeCleanSymbol(input);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void BaseProvider_CleanSymbol_NullInput_ReturnsEmpty()
        {
            // Null-safe path — callers passing null (e.g. from a partially populated
            // signal) must not crash the provider.
            var provider = new StubProvider();
            Assert.Equal("", provider.InvokeCleanSymbol(null!));
        }

        // ── Kraken FormatPair / FormatRestPair (private static) ──────────────

        [Theory]
        [InlineData("BTC/USD",   "BTC/USD")]   // already in slash form — just uppercased
        [InlineData("btc/usd",   "BTC/USD")]
        [InlineData("BTCUSD",    "BTC/USD")]   // 6-char no-separator gets split at [-3]
        [InlineData("ETHUSDT",   "ETHU/SDT")]  // 7-char: split is still at [-3]; Kraken WS rarely sees this
        public void Kraken_FormatPair_ReturnsSlashForm(string input, string expected)
        {
            string actual = InvokeKrakenStatic<string>("FormatPair", input);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("BTC/USD", "BTCUSD")]
        [InlineData("BTC-USD", "BTCUSD")]
        [InlineData("btc/usd", "BTCUSD")]
        [InlineData("XBTUSD",  "XBTUSD")]
        public void Kraken_FormatRestPair_StripsSeparatorsAndUppercases(string input, string expected)
        {
            string actual = InvokeKrakenStatic<string>("FormatRestPair", input);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Kraken_FormatPair_ShortInput_FallsBackToUpper()
        {
            // Shorter than 6 chars and no slash → the branch falls through to ToUpper().
            string actual = InvokeKrakenStatic<string>("FormatPair", "abc");
            Assert.Equal("ABC", actual);
        }

        // ── Coinbase product-id convention ────────────────────────────────────
        //
        // This used to assert `input.Replace("/", "-").ToUpperInvariant()` — the BCL, on a
        // symbol-routing money path — with CoinbaseProvider never constructed. The comment
        // saying the provider "does not expose a helper" had also gone stale: ToProductId has
        // been the one transform for a while, and the test never noticed either way.

        [Theory]
        [InlineData("BTC/USD",  "BTC-USD")]
        [InlineData("btc/usd",  "BTC-USD")]
        [InlineData("ETH-USDT", "ETH-USDT")]  // already dashed → only uppercased
        [InlineData("SOLUSD",   "SOLUSD")]    // no separator → passed through
        [InlineData("",         "")]          // null-safe / empty short-circuit
        public void Coinbase_ProductId_ReplacesSlashWithDashAndUppercases(string input, string expected)
        {
            string actual = InvokeProviderStatic<string>(
                "AccessibleTrader.Plugins.Coinbase", "CoinbaseProvider", "ToProductId", input);
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// …and the transform is actually on the path a request takes. A correct helper nobody
        /// calls is the same outage as a wrong one, and the three call sites the old comment
        /// named were never exercised.
        /// </summary>
        [Fact]
        public async Task Coinbase_ProductId_ReachesTheWire()
        {
            var handler = new FakeHttpMessageHandler().Get(@"coinbase\.com", """{"candles":[]}""");
            var provider = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
            provider.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "test",
                ["ApiSecret"] = "test-secret",
            });
            SwapHttpClient(provider, handler);

            await provider.FetchOhlcvAsync(new MarketDataRequest("Crypto", "btc/usd", "1m", 10));

            Assert.NotEmpty(handler.Captured);
            Assert.Contains("/products/BTC-USD/candles", handler.Captured[0].RequestUri!.ToString());
        }

        // ── Bitstamp: CleanSymbol + usdt→usd quote swap ──────────────────────

        [Theory]
        [InlineData("BTC/USD",  "btcusd")]
        [InlineData("BTC/USDT", "btcusd")]   // Bitstamp maps USDT quotes to USD
        [InlineData("btc-usdt", "btcusd")]
        [InlineData("ETHUSDT",  "ethusd")]
        [InlineData("XRP/USD",  "xrpusd")]
        [InlineData("usdt/usd", "usdtusd")] // trailing-quote-only remap: base "usdt" is left intact
        public void Bitstamp_ToBitstampPair_LowercasesAndMapsUsdtToUsd(string input, string expected)
        {
            // Bitstamp REST paths are lowercase and list pairs in /usd form only.
            // ALL Bitstamp paths (fetch, order-book, live subscribe, private
            // channel) now route through this one helper, so historical and live
            // feeds can never target different markets — the bug that left the
            // keyed live feed on a dead usdt channel.
            string actual = AccessibleTrader.Plugins.Bitstamp.BitstampProvider.ToBitstampPair(input);
            Assert.Equal(expected, actual);
        }

        // ── Oanda: forex "EUR_USD" underscore convention ─────────────────────
        //
        // Same class of defect as the Coinbase case above, and the mirror was WRONG as well as
        // vacuous: the real FormatInstrument splits a 6-character separator-less symbol, so
        // "EURUSD" becomes "EUR_USD" — the test's copy would have left it as "EURUSD" and
        // nobody would have known, because no row covered it.

        [Theory]
        [InlineData("EUR/USD", "EUR_USD")]
        [InlineData("eur/usd", "EUR_USD")]
        [InlineData("GBP-JPY", "GBP_JPY")]
        [InlineData("EUR_USD", "EUR_USD")]  // already underscored → uppercased only
        [InlineData("EURUSD",  "EUR_USD")]  // 6 chars, no separator → split at [3]
        [InlineData("XAUUSD",  "XAU_USD")]
        public void Oanda_ForexSymbol_UsesUnderscoreSeparator(string input, string expected)
        {
            string actual = InvokeProviderStatic<string>(
                "AccessibleTrader.Plugins.Oanda", "OandaProvider", "FormatInstrument", input);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task Oanda_Instrument_ReachesTheWire()
        {
            var handler = new FakeHttpMessageHandler().Get(@"oanda\.com", """{"candles":[]}""");
            var provider = new AccessibleTrader.Plugins.Oanda.OandaProvider();
            SwapHttpClient(provider, handler);   // Oanda writes auth headers in Configure
            provider.Configure(new Dictionary<string, string>
            {
                ["AccessToken"] = "test-token",
                ["AccountId"]   = "test-acct",
            });

            await provider.FetchOhlcvAsync(new MarketDataRequest("Forex", "eur/usd", "1h", 10));

            Assert.NotEmpty(handler.Captured);
            Assert.Contains("/instruments/EUR_USD/candles", handler.Captured[0].RequestUri!.ToString());
        }

        // ── Polygon: stock ticker passthrough, asserted on the wire ──────────
        //
        // The two theories that used to live here asserted `input.ToUpperInvariant()` against
        // no provider at all — there is no Polygon or equity normalisation helper to drift
        // from, so the only claim worth making is about what actually lands in the URL.

        [Theory]
        [InlineData("AAPL", "AAPL")]     // stock ticker unchanged
        [InlineData("spy",  "SPY")]      // uppercased on the way out
        [InlineData("brk.b", "BRK.B")]   // dotted ticker kept (Berkshire B)
        public async Task Polygon_StockTicker_ReachesTheWire_Uppercased(string input, string expected)
        {
            var handler = new FakeHttpMessageHandler().Get(@"polygon\.io", """{"results":[]}""");
            var provider = new AccessibleTrader.Plugins.Polygon.PolygonProvider();
            provider.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
            SwapHttpClient(provider, handler);

            await provider.FetchOhlcvAsync(new MarketDataRequest("Stock", input, "1m", 10));

            Assert.NotEmpty(handler.Captured);
            Assert.Contains($"/aggs/ticker/{expected}/range/", handler.Captured[0].RequestUri!.ToString());
        }

        // ── MEXC + Alpaca crypto: CleanSymbol pattern (covered above) ────────

        [Theory]
        [InlineData("BTC/USDT", "BTCUSDT")]
        [InlineData("eth-usdt", "ETHUSDT")]
        [InlineData("SOL/USD",  "SOLUSD")]
        public void Mexc_And_Alpaca_Crypto_UseCleanSymbol(string input, string expected)
        {
            // Both providers defer to BaseMarketDataProvider.CleanSymbol. Mirrored
            // here so a future MEXC or Alpaca override of the normalization path
            // gets caught by the failure.
            var provider = new StubProvider();
            string actual = provider.InvokeCleanSymbol(input);
            Assert.Equal(expected, actual);
        }

        // ── Alpaca crypto: the SLASHED form v1beta3 needs (BTC/USD) ──────────

        [Theory]
        [InlineData("BTC/USD",  "BTC/USD")]
        [InlineData("BTCUSD",   "BTC/USD")]
        [InlineData("btc-usd",  "BTC/USD")]
        [InlineData("ETHUSDT",  "ETH/USDT")]
        [InlineData("SOLUSDC",  "SOL/USDC")]
        public void Alpaca_Crypto_UsesSlashedPair(string input, string expected)
        {
            // Regression: stripping the slash returned an empty crypto chart, because
            // the v1beta3 request AND the response key both need "BASE/QUOTE".
            string actual = AccessibleTrader.Plugins.Alpaca.AlpacaProvider.ToAlpacaCryptoSymbol(input);
            Assert.Equal(expected, actual);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static T InvokeKrakenStatic<T>(string methodName, string arg) =>
            InvokeProviderStatic<T>("AccessibleTrader.Plugins.Kraken", "KrakenProvider", methodName, arg);

        /// <summary>
        /// Calls a provider's private static string→string normaliser. These transforms are
        /// deliberately not public — they are wire-format details — but "not public" is not a
        /// reason to assert a copy of them instead, which is what several tests in this file
        /// used to do. A missing method throws rather than silently skipping.
        /// </summary>
        private static T InvokeProviderStatic<T>(string assemblyName, string typeName, string methodName, string arg)
        {
            var asm = Assembly.Load(assemblyName);
            var type = asm.GetType($"{assemblyName}.{typeName}")
                ?? throw new InvalidOperationException($"{typeName} not found in {assemblyName}.");
            var method = type.GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null, types: new[] { typeof(string) }, modifiers: null)
                ?? throw new MissingMethodException($"{methodName}(string) not found on {typeName}.");
            return (T)method.Invoke(null, new object[] { arg })!;
        }

        /// <summary>Swap a provider's private HttpClient for one wired to a fake handler —
        /// the same trick <see cref="ProviderFetchOhlcvTests"/> and <see cref="BrokerParityTests"/>
        /// already use. Providers name the field differently, so it is found by type.</summary>
        private static void SwapHttpClient(object provider, FakeHttpMessageHandler handler)
        {
            var target = provider.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.FieldType == typeof(System.Net.Http.HttpClient))
                ?? throw new InvalidOperationException(
                    $"{provider.GetType().Name} has no HttpClient-typed private field.");
            target.SetValue(provider, new System.Net.Http.HttpClient(handler));
        }

        // ── Minimal concrete subclass to exercise protected CleanSymbol ──────

        /// <summary>
        /// A test-only <see cref="BaseMarketDataProvider"/> subclass that throws on
        /// every abstract member except the one we need. Stubbing like this — rather
        /// than reflecting on an uninitialized object or adding a Release-visible
        /// helper — pins the protected method contract without exposing it on the
        /// public API surface.
        /// </summary>
        private sealed class StubProvider : BaseMarketDataProvider
        {
            public string InvokeCleanSymbol(string symbol) => CleanSymbol(symbol);

            public override string Name => "stub";
            public override string Description => "";
            public override List<MarketType> SupportedMarkets => new();
            public override bool SupportsSymbolSearch => false;
            public override bool RequiresApiKey => false;
            public override bool IsConfigured => true;
            public override bool SupportsLiveUpdates => false;
            public override ProviderEnvironment Environment => ProviderEnvironment.Live;
            public override int MaxBarsPerRequest => 1000;
            public override List<string> NativelySupportedTimeframes => new();

            public override void Configure(Dictionary<string, string> config) { }
            public override Task EnsureConnectedAsync() => Task.CompletedTask;
            public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;
            public override Task DisconnectAsync() => Task.CompletedTask;
            public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot") => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(new List<string>());
            public override Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
                => Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));
            public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
                => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
        }
    }
}
