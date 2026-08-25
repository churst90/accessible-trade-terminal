using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-08-21 audit's provider-tier batch two, pinned:
    ///
    /// - Binance parsed Testnet with a case-sensitive compare ("True" traded the
    ///   real book), Oanda's Environment config had no else (once live, a later
    ///   practice config kept the LIVE urls).
    /// - Kraken signed one string and sent another (FormUrlEncodedContent
    ///   re-encoded brackets and spaces), so bracketed orders died with
    ///   EAPI:Invalid signature — and the parity test unescaped the body before
    ///   asserting, normalizing the mismatch away.
    /// - Kraken split pairs at a hardcoded 3 characters ("BTCUSDT" → "BTCU/SDT",
    ///   silently empty chart); SymbolFormat's 8-entry quote list was why MEXC
    ///   produced "BTCTUSD" and Oanda couldn't use the helper at all.
    /// - CryptoAddressValidator matched networks by raw Contains: "TETHER USD
    ///   (TRC20)" contains "ETH" (valid Tron address refused), "BTC Lightning"
    ///   contains "BTC" (lnbc1… invoices refused).
    /// - Coinbase built its WS JWT through the REST path builder
    ///   (uri = "GET api.coinbase.com/advanced-trade-ws.coinbase.com", no nonce),
    ///   so the user channel was rejected — and with the streaming flag
    ///   defaulting true, fills were announced by no path at all.
    /// - Six providers left SupportsOrderEventStreaming at its true default
    ///   while their push channel could be dead.
    /// - Six providers reported Position.Quantity through Math.Abs while every
    ///   consumer derives long/short from the sign.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderStreamAndSigningTests
    {
        // ── Environment configs must be case-insensitive and reversible ─────

        [Theory]
        [InlineData("true", ProviderEnvironment.Paper)]
        [InlineData("True", ProviderEnvironment.Paper)]   // bool.ToString() default — used to trade the real book
        [InlineData("TRUE", ProviderEnvironment.Paper)]
        [InlineData("false", ProviderEnvironment.Live)]
        [InlineData("False", ProviderEnvironment.Live)]
        public void Binance_testnet_config_is_case_insensitive(string value, ProviderEnvironment expected)
        {
            var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            p.Configure(new Dictionary<string, string> { ["Testnet"] = value });
            Assert.Equal(expected, p.Environment);
        }

        [Fact]
        public void Oanda_environment_switch_is_reversible()
        {
            var p = new AccessibleTrader.Plugins.Oanda.OandaProvider();
            Assert.Equal(ProviderEnvironment.Paper, p.Environment); // practice by default

            p.Configure(new Dictionary<string, string> { ["Environment"] = "live" });
            Assert.Equal(ProviderEnvironment.Live, p.Environment);

            // The missing else: this used to keep the LIVE urls and environment.
            p.Configure(new Dictionary<string, string> { ["Environment"] = "practice" });
            Assert.Equal(ProviderEnvironment.Paper, p.Environment);
        }

        // ── Kraken: the signed string IS the sent string ─────────────────────

        private static readonly string KrakenSecret = Convert.ToBase64String(new byte[32]);

        private static AccessibleTrader.Plugins.Kraken.KrakenProvider Kraken(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "k", ["ApiSecret"] = KrakenSecret });
            SwapHttp(p, h);
            return p;
        }

        [Fact]
        public async Task Kraken_signature_verifies_over_the_exact_bytes_sent()
        {
            var h = new FakeHttpMessageHandler().Post(@"/0/private/AddOrder",
                """{"error":[],"result":{"txid":["TX1"]}}""");
            var p = Kraken(h);

            // A bracketed order — close[ordertype] has the brackets that
            // FormUrlEncodedContent used to re-encode out from under the signature.
            await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.5,
                OrderType.Market, StopLoss: 90000));

            var req = h.Captured.Single(r => r.RequestUri!.ToString().Contains("AddOrder"));
            string body = await req.Content!.ReadAsStringAsync();

            // Raw body still carries the documented bracket syntax…
            Assert.Contains("close[ordertype]=", body);

            // …and the API-Sign header verifies over exactly these bytes:
            // HMAC-SHA512(secret, path + SHA256(nonce + body)).
            string nonce = Regex.Match(body, @"(?:^|&)nonce=(\d+)").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(nonce), "nonce missing from body");
            byte[] shaHash = SHA256.HashData(Encoding.UTF8.GetBytes(nonce + body));
            byte[] pathBytes = Encoding.UTF8.GetBytes(req.RequestUri!.AbsolutePath);
            using var hmac = new HMACSHA512(Convert.FromBase64String(KrakenSecret));
            string expected = Convert.ToBase64String(hmac.ComputeHash(
                pathBytes.Concat(shaHash).ToArray()));

            Assert.Equal(expected, req.Headers.GetValues("API-Sign").Single());
        }

        [Theory]
        [InlineData("BTCUSDT", "BTC/USDT")]   // the audit's case: used to become BTCU/SDT
        [InlineData("BTC/USD", "BTC/USD")]
        [InlineData("XBTUSD", "XBT/USD")]
        [InlineData("ETHBTC", "ETH/BTC")]
        [InlineData("ADAJPY", "ADA/JPY")]
        [InlineData("SOLFDUSD", "SOL/FDUSD")]
        public void Kraken_ws_pairs_split_on_the_real_quote(string symbol, string pair)
            => Assert.Equal(pair, AccessibleTrader.Plugins.Kraken.KrakenProvider.FormatPair(symbol));

        [Theory]
        [InlineData("BTCTUSD", "BTC", "TUSD")]   // why MEXC produced BTC_TUSD wrong
        [InlineData("EURJPY", "EUR", "JPY")]     // why Oanda couldn't use the helper
        [InlineData("AUDCAD", "AUD", "CAD")]
        [InlineData("BTCUSDT", "BTC", "USDT")]
        public void SymbolFormat_knows_the_widened_quote_list(string symbol, string b, string q)
            => Assert.Equal((b, q), SymbolFormat.SplitBaseQuote(symbol));

        // ── Address validation routes by token, not substring ────────────────

        [Fact]
        public void A_tron_address_on_a_tether_trc20_network_is_verified_not_sent_to_the_evm_rules()
        {
            // "TETHER USD (TRC20)" contains the letters "ETH" — the raw-Contains
            // dispatch sent this to the EVM hex validator, which refused it, and
            // the wallet declined to display a CORRECT deposit address.
            var r = CryptoAddressValidator.Validate("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "Tether USD (TRC20)");
            Assert.Equal(AddressCheck.Verified, r.Result);
            Assert.Contains("Tron", r.Detail);
        }

        [Fact]
        public void A_lightning_invoice_on_a_btc_lightning_network_verifies_its_bech32_checksum()
        {
            // "BTC Lightning" contains "BTC" — the invoice used to hit the
            // 1/3/bc1 shape check and fail. BOLT11 spec test vector.
            var r = CryptoAddressValidator.Validate(
                "lnbc1pvjluezpp5qqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqqqsyqcyq5rqwzqfqypqdpl2pkx2ctnv5sxxmmwwd5kgetjypeh2ursdae8g6twvus8g6rfwvs8qun0dfjkxaq8rkx3yf5tcsyz3d73gafnh3cax9rn449d9p5uxz9ezhhypd0elx87sjle52x86fux2ypatgddc6k63n7erqz25le42c4u4ecky03ylcqca784w",
                "BTC Lightning");
            Assert.Equal(AddressCheck.Verified, r.Result);
            Assert.Contains("Lightning", r.Detail);
        }

        [Fact]
        public void A_tether_erc20_network_still_reaches_the_evm_rules()
        {
            var r = CryptoAddressValidator.Validate(
                "0x52908400098527886E0F7030069857D2E4169EE7", "Tether USD (ERC20)");
            Assert.Equal(AddressCheck.StructureOnly, r.Result);
        }

        [Fact]
        public void Bitcoin_cash_is_unknown_not_misjudged_by_bitcoin_rules()
        {
            var r = CryptoAddressValidator.Validate(
                "qzm47qz5ue99y9yl4aca7jnz7dwgdenl85jkfx3znl", "Bitcoin Cash (BCH)");
            Assert.Equal(AddressCheck.Unknown, r.Result);
        }

        // ── Coinbase JWTs: WS has no uri claim and carries a nonce ───────────

        private static (JObject Header, JObject Payload) DecodeJwt(string token)
        {
            static JObject Part(string p)
            {
                p = p.Replace('-', '+').Replace('_', '/');
                p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
                return JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(p)));
            }
            var parts = token.Split('.');
            return (Part(parts[0]), Part(parts[1]));
        }

        private static string FreshEcPem()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return ecdsa.ExportECPrivateKeyPem();
        }

        [Fact]
        public void Coinbase_ws_jwt_has_no_uri_claim_and_a_nonce_header()
        {
            var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
            string token = p.GenerateWsJwt("organizations/org/apiKeys/key", FreshEcPem());
            Assert.NotEqual("AUTH_ERROR", token);

            var (header, payload) = DecodeJwt(token);
            // The old builder produced "GET api.coinbase.com/advanced-trade-ws.coinbase.com"
            // here, and Coinbase rejected the user-channel subscription server-side.
            Assert.Null(payload["uri"]);
            Assert.NotNull(header["nonce"]);
            Assert.Equal("organizations/org/apiKeys/key", header["kid"]?.ToString());
        }

        [Fact]
        public void Coinbase_rest_jwt_still_binds_method_and_path()
        {
            var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
            string token = p.GenerateJwt("organizations/org/apiKeys/key", FreshEcPem(),
                "GET", "/api/v3/brokerage/accounts");
            var (header, payload) = DecodeJwt(token);
            Assert.Equal("GET api.coinbase.com/api/v3/brokerage/accounts", payload["uri"]?.ToString());
            Assert.Null(header["nonce"]); // REST bytes deliberately unchanged
        }

        // ── Binance futures user-data: ORDER_TRADE_UPDATE parses ─────────────

        [Fact]
        public void Binance_futures_order_update_reaches_the_order_stream()
        {
            var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            OrderUpdate? got = null;
            using var sub = p.OrderUpdateStream.Subscribe(u => got = u);

            using var doc = JsonDocument.Parse(
                """{"e":"ORDER_TRADE_UPDATE","o":{"X":"FILLED","s":"BTCUSDT","S":"SELL","q":"0.5","z":"0.5","L":"95000.5","p":"0","i":123456,"ot":"STOP_MARKET"}}""");
            p.OnFuturesUserDataMessage(doc.RootElement);

            Assert.NotNull(got);
            Assert.Equal(OrderStatus.Filled, got!.Status);
            Assert.Equal(OrderSide.Sell, got.Side);
            Assert.Equal("BTCUSDT", got.Symbol);
            Assert.Equal(0.5, got.FilledQuantity);
            Assert.Equal(95000.5, got.FilledPrice);
            Assert.True(got.StopTriggered); // ot STOP_MARKET — this fill IS the stop firing
        }

        [Fact]
        public void Binance_futures_variant_frame_missing_fields_does_not_throw()
        {
            var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            using var doc = JsonDocument.Parse("""{"e":"ORDER_TRADE_UPDATE","o":{"i":1}}""");
            p.OnFuturesUserDataMessage(doc.RootElement); // must not throw — a throw is a LOST frame
        }

        // ── Streaming honesty: at rest, no provider claims a live push channel ─

        // Bitstamp is deliberately absent: it has no GetFillsAsync, so the
        // poller would resolve a filled order as "cancelled" — its flag stays
        // statically true until that surface exists (see the provider comment).
        [Theory]
        [InlineData(typeof(AccessibleTrader.Plugins.Coinbase.CoinbaseProvider))]
        [InlineData(typeof(AccessibleTrader.Plugins.Kraken.KrakenProvider))]
        [InlineData(typeof(AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider))]
        [InlineData(typeof(AccessibleTrader.Plugins.Oanda.OandaProvider))]
        [InlineData(typeof(AccessibleTrader.Plugins.Alpaca.AlpacaProvider))]
        [InlineData(typeof(AccessibleTrader.Plugins.Binance.BinanceProvider))]
        public void No_provider_claims_order_event_streaming_before_its_push_channel_is_up(Type providerType)
        {
            // The default-true flag on a dead channel is the states-of-silence
            // bug: no stream events AND no polling. A freshly constructed
            // provider has no socket, so the flag must be false — the order
            // service will then poll, and fills announce either way.
            var provider = Activator.CreateInstance(providerType)!;
            var tp = Assert.IsAssignableFrom<ITradingProvider>(provider);
            Assert.False(tp.SupportsOrderEventStreaming,
                $"{providerType.Name} claims a live order-event stream with no connection at all.");
        }

        // ── Position.Quantity is signed ──────────────────────────────────────

        [Fact]
        public async Task Kraken_short_positions_carry_a_negative_quantity()
        {
            var h = new FakeHttpMessageHandler().Post(@"/0/private/OpenPositions",
                """{"error":[],"result":{"P1":{"pair":"XBTUSD","type":"sell","vol":"0.5","cost":"45000","value":"44000","net":"-100","margin":"9000"}}}""");
            var p = Kraken(h);

            var positions = await p.GetPositionsAsync();

            var pos = Assert.Single(positions);
            Assert.True(pos.Quantity < 0,
                "a Kraken short (type=sell) must be negative — consumers derive long/short from the sign");
            Assert.Equal(-0.5, pos.Quantity, 10);
        }

        [Fact]
        public async Task Tradier_short_positions_carry_a_negative_quantity()
        {
            var h = new FakeHttpMessageHandler().Get(@"/accounts/ACC1/positions",
                """{"positions":{"position":{"symbol":"AAPL","quantity":-10,"cost_basis":2300,"last_price":230}}}""");
            var p = new AccessibleTrader.Plugins.Tradier.TradierProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "tok", ["AccountId"] = "ACC1" });
            SwapHttp(p, h);

            var positions = await p.GetPositionsAsync();

            var pos = Assert.Single(positions);
            Assert.Equal(-10, pos.Quantity);
        }

        [Fact]
        public void No_provider_reports_position_quantity_through_Abs()
        {
            // The class guard for the sweep: the QUANTITY argument of a Position
            // constructed in a provider must never be wrapped in Math.Abs — the
            // sign IS the long/short fact. (Math.Abs is fine for MarketValue.)
            var offenders = new List<string>();
            string root = System.IO.Path.Combine(RepoRoot(), "Plugins", "Providers");
            foreach (string file in System.IO.Directory.GetFiles(root, "*Provider.cs", System.IO.SearchOption.AllDirectories))
            {
                if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}")) continue;
                string text = System.IO.File.ReadAllText(file);
                // The quantity is the ARGUMENT AFTER the symbol: match
                // "new Position(<first-arg-line(s)>, Math.Abs(" allowing the
                // comment lines the fixed sites carry.
                foreach (Match m in Regex.Matches(text, @"new Position\((?:[^;]*?)\)", RegexOptions.Singleline))
                {
                    // Strip comments, then take the second top-level argument.
                    string body = Regex.Replace(m.Value, @"//[^\n]*", "");
                    var args = SplitTopLevelArgs(body[13..^1]);
                    if (args.Count >= 2 && args[1].TrimStart().StartsWith("Math.Abs(", StringComparison.Ordinal))
                        offenders.Add($"{System.IO.Path.GetFileName(file)}: {args[1].Trim()[..Math.Min(40, args[1].Trim().Length)]}");
                }
            }
            Assert.True(offenders.Count == 0,
                "Position.Quantity must be SIGNED (consumers derive long/short from it):\n" + string.Join("\n", offenders));
        }

        private static List<string> SplitTopLevelArgs(string s)
        {
            var args = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == ',' && depth == 0) { args.Add(s[start..i]); start = i + 1; }
            }
            args.Add(s[start..]);
            return args;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static void SwapHttp(object provider, FakeHttpMessageHandler handler)
        {
            var target = Assert.Single(provider.GetType()
                .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(HttpClient) && f.Name is "_httpClient" or "_http"));
            target.SetValue(provider, new HttpClient(handler));
        }

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }
}
