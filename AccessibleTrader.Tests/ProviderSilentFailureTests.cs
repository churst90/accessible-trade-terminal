using System.Net;
using System.Reflection;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A read that failed says so. An empty answer and a dead endpoint are not the same
    /// fact.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Three providers wrapped their order-book read in a bare
    /// <c>catch { return (new(), new()); }</c>, and Coinbase did the same for its symbol list
    /// and its cancel. Kraken and Binance already pushed to <c>_errorStream</c> on that path —
    /// so the fleet disagreed with itself about whether a failed read is worth mentioning.
    /// </para>
    ///
    /// <para>
    /// For a sighted user an empty depth ladder is a visible oddity: the panel is obviously
    /// blank. For this product's audience it is indistinguishable from a book with no liquidity,
    /// and a failed cancel is indistinguishable from an order that had already filled. Those are
    /// opposite facts, and one of them means the position is still live.
    /// </para>
    ///
    /// <para>
    /// ── How this is demonstrated ───────────────────────────────────────────────
    /// The fake transport refuses the call, and the test asserts something reached
    /// <c>ErrorStream</c>. Each case fails on its own if its catch goes bare again.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderSilentFailureTests
    {
        /// <summary>Swaps in a transport that fails every request.</summary>
        private static FakeHttpMessageHandler DeadTransport()
        {
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            handler.Add(HttpMethod.Get, ".*", _ => throw new HttpRequestException("connection refused"));
            handler.Add(HttpMethod.Post, ".*", _ => throw new HttpRequestException("connection refused"));
            return handler;
        }

        private static void SwapAllHttpClients(object provider, HttpMessageHandler handler)
        {
            foreach (var field in provider.GetType()
                         .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(f => f.FieldType == typeof(HttpClient)))
            {
                field.SetValue(provider, new HttpClient(handler));
            }
        }

        private static List<string> Recorded(BaseMarketDataProvider provider)
        {
            var messages = new List<string>();
            provider.ErrorStream.Subscribe(messages.Add);
            return messages;
        }

        [Fact]
        public async Task Tradier_says_so_when_the_order_book_read_fails()
        {
            var provider = new AccessibleTrader.Plugins.Tradier.TradierProvider();
            provider.Configure(new Dictionary<string, string>
            {
                ["AccessToken"] = "tok", ["AccountId"] = "acct",
            });
            SwapAllHttpClients(provider, DeadTransport());
            var errors = Recorded(provider);

            var (bids, asks) = await provider.GetOrderBookAsync("AAPL");

            Assert.Empty(bids);
            Assert.Empty(asks);
            Assert.Contains(errors, m => m.Contains("order book", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Alpaca_says_so_when_the_order_book_read_fails()
        {
            var provider = new AccessibleTrader.Plugins.Alpaca.AlpacaProvider();
            provider.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "k", ["ApiSecret"] = "s",
            });
            SwapAllHttpClients(provider, DeadTransport());
            var errors = Recorded(provider);

            var (bids, asks) = await provider.GetOrderBookAsync("AAPL");

            Assert.Empty(bids);
            Assert.Empty(asks);
            Assert.Contains(errors, m => m.Contains("order book", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Coinbase_says_so_when_the_order_book_read_fails()
        {
            var provider = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
            provider.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "k", ["ApiSecret"] = "s",
            });
            SwapAllHttpClients(provider, DeadTransport());
            var errors = Recorded(provider);

            var (bids, asks) = await provider.GetOrderBookAsync("BTC-USD");

            Assert.Empty(bids);
            Assert.Empty(asks);
            Assert.Contains(errors, m => m.Contains("order book", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A cancel that could not be SENT is not a cancel that was refused.
        ///
        /// <para>
        /// The bool stays false — the cancel did not happen — but the reason is now said. Without
        /// it, "we could not reach Coinbase" and "Coinbase says that order is already gone" are
        /// the same event, and only one of them leaves a live order the user still owns.
        /// </para>
        /// </summary>
        [Fact]
        public async Task Coinbase_says_so_when_a_cancel_cannot_be_sent()
        {
            var provider = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
            provider.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "k", ["ApiSecret"] = "s",
            });
            SwapAllHttpClients(provider, DeadTransport());
            var errors = Recorded(provider);

            bool cancelled = await provider.CancelOrderAsync("order-1", "BTC-USD");

            Assert.False(cancelled);
            Assert.Contains(errors, m =>
                m.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                && m.Contains("still", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// MEXC's leverage call reported a throw and a refusal identically, as a silent 1.0.
        ///
        /// <para>
        /// 1.0 stays the return value — it is what <c>ITradingProvider</c> means by "not set", and
        /// <c>GeneralOrderService</c> compares it against the request. What was missing is that
        /// anybody was TOLD, which matters most in the case that looks least like a failure: a
        /// change the exchange applied and then failed to confirm. The user is then sized against
        /// 1x while the account may be on 20x from this very call.
        /// </para>
        /// </summary>
        [Fact]
        public async Task Mexc_says_so_when_a_leverage_change_cannot_be_confirmed()
        {
            var provider = new AccessibleTrader.Plugins.Mexc.MexcProvider();
            provider.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "k", ["ApiSecret"] = "s",
            });

            var type = typeof(AccessibleTrader.Plugins.Mexc.MexcProvider);
            // SetLeverageAsync returns early unless the provider believes it is connected, and
            // the signed calls go through _rest — which captured the ORIGINAL client, so
            // swapping the _http field alone would leave the real transport in place and the
            // test would pass without ever reaching the code it is about.
            type.GetField("_connected", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(provider, true);
            var restField = type.GetField("_rest", BindingFlags.NonPublic | BindingFlags.Instance)!;
            restField.SetValue(provider,
                Activator.CreateInstance(restField.FieldType, new HttpClient(DeadTransport())));
            var errors = Recorded(provider);

            double applied = await provider.SetLeverageAsync("BTC_USDT", 20);

            Assert.Equal(1.0, applied);
            Assert.Contains(errors, m => m.Contains("leverage", StringComparison.OrdinalIgnoreCase));
        }
    }
}
