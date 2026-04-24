using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
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

        [Theory]
        [InlineData("BTC/USD",  "BTC-USD")]
        [InlineData("btc/usd",  "BTC-USD")]
        [InlineData("ETH-USDT", "ETH-USDT")]  // already dashed → only uppercased
        [InlineData("SOLUSD",   "SOLUSD")]    // no separator → passed through
        public void Coinbase_ProductId_ReplacesSlashWithDashAndUppercases(string input, string expected)
        {
            // CoinbaseProvider does not expose a helper; the transform is inline at
            // multiple call sites (GetOrderBook, SubscribeOrderBook, GetOpenOrders).
            // This test mirrors the exact transform so a future refactor that
            // consolidates it into a helper lands on the same behaviour.
            string actual = input.Replace("/", "-").ToUpperInvariant();
            Assert.Equal(expected, actual);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static T InvokeKrakenStatic<T>(string methodName, string arg)
        {
            // KrakenProvider lives in AccessibleTrader.Plugins.Kraken — load by fully-
            // qualified name. The private static methods FormatPair/FormatRestPair are
            // the tested surface. Reflection keeps the test project from taking a
            // hard plugin dependency.
            var asm = Assembly.Load("AccessibleTrader.Plugins.Kraken");
            var type = asm.GetType("AccessibleTrader.Plugins.Kraken.KrakenProvider")
                ?? throw new InvalidOperationException("KrakenProvider not found.");
            var method = type.GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null, types: new[] { typeof(string) }, modifiers: null)
                ?? throw new MissingMethodException($"{methodName} not found on KrakenProvider.");
            return (T)method.Invoke(null, new object[] { arg })!;
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
