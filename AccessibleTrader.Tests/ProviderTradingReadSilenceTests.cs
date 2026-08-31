using System.Net;
using System.Reflection;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Shared rig for the dead-transport roster sweeps below.
    ///
    /// <para>
    /// The transport is the 401-answering <see cref="SymbolListHarness.DeadTransport"/>
    /// (a 4xx is not retried by the rate limiter, and "the key is wrong" is the shape
    /// the reported bugs actually wore). The bodies deliberately carry NO venue error
    /// shape — no Kraken <c>error</c> array, no MEXC <c>code</c>+<c>msg</c>, no
    /// Bitstamp <c>status:"error"</c> — because that is the case the status-blind
    /// reads shipped: a body that parses fine and simply lacks the expected fields.
    /// </para>
    /// </summary>
    internal static class TradingReadHarness
    {
        /// <summary>
        /// Configure, swap every reachable HttpClient for the dead transport, record
        /// the error stream, then run <paramref name="read"/> and report what happened.
        /// "Attempted" is measured at the wire — a provider whose guard returned
        /// before any HTTP call is a legitimate skip, not a pass.
        /// </summary>
        public static async Task<(Exception? Threw, int Count, List<string> Said, bool Attempted)>
            AskAsync(string typeName, Func<BaseMarketDataProvider, Task<int>> read)
        {
            var provider = ProviderRoster.All().First(p => p.GetType().FullName == typeName);
            try
            {
                provider.Configure(SymbolListHarness.Credentials());
                var handler = SymbolListHarness.DeadTransport();
                SymbolListHarness.SwapEveryHttpClient(provider, handler);
                var said = SymbolListHarness.Recorded(provider);

                try
                {
                    int count = await read(provider);
                    return (null, count, said, handler.Captured.Count > 0);
                }
                catch (Exception ex)
                {
                    return (ex, 0, said, handler.Captured.Count > 0);
                }
            }
            finally
            {
                provider.Dispose();
            }
        }

        public static IEnumerable<object[]> MarketDataProviderTypeNames()
            => ProviderSymbolListSilenceTests.MarketDataProviderTypeNames();

        public static IEnumerable<object[]> TradingProviderTypeNames() =>
            ProviderRoster.Types
                .Where(t => typeof(ITradingProvider).IsAssignableFrom(t))
                .Select(t => new object[] { t.FullName! });

        /// <summary>A symbol the venue's own pre-HTTP formatting will accept.</summary>
        public static string SymbolFor(BaseMarketDataProvider provider)
            => provider.SupportedMarkets[0].ToString().Contains("Crypto", StringComparison.OrdinalIgnoreCase)
                ? "BTC/USD" : "AAPL";
    }

    /// <summary>
    /// <b>A balance read that failed is not an account that is flat.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Four venues read the private-endpoint body without ever consulting the HTTP
    /// status (Kraken, Bitstamp, Coinbase, and the Kraken Futures pair fixed one
    /// session earlier for the symbol path), and Kraken additionally never looked at
    /// the <c>error</c> array its API reports refusals in. In every case the failure
    /// body parses as JSON, lacks the expected fields, and the read returned an EMPTY
    /// balance list as success — "the account is flat" — which is the reconciliation
    /// overwrite <c>ProviderResult.cs</c> documents. Several of those readers carry
    /// the comment "No catch: a failed read must throw so the order service can
    /// classify it" directly above the code path that could not throw.
    /// </para>
    ///
    /// <para>
    /// ── The rule ───────────────────────────────────────────────────────────────
    /// With every route refusing, a trading provider asked for balances must not
    /// hand back an empty list in silence: it throws (the order service classifies),
    /// or it says why on <c>ErrorStream</c>. A provider whose guard returns before
    /// any HTTP call (MEXC needs a connect step, Schwab an account hash) is skipped
    /// at the wire, and the anti-vacuity floor below keeps that honest.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderBalanceReadSilenceTests
    {
        public static IEnumerable<object[]> TradingProviderTypeNames()
            => TradingReadHarness.TradingProviderTypeNames();

        private static Task<(Exception? Threw, int Count, List<string> Said, bool Attempted)> Ask(string typeName)
            => TradingReadHarness.AskAsync(typeName, async p =>
                (await ((ITradingProvider)p).GetBalancesAsync()).Count);

        [Theory]
        [MemberData(nameof(TradingProviderTypeNames))]
        public async Task A_failed_balance_read_throws_or_says_so(string providerTypeName)
        {
            var r = await Ask(providerTypeName);

            if (!r.Attempted) return;      // guard refused pre-network; counted by the floor below
            if (r.Threw != null) return;   // loud — the order service classifies exceptions
            if (r.Count > 0) return;       // an answer is an answer

            Assert.True(r.Said.Count > 0,
                $"{providerTypeName} reached the wire, was refused, and returned an empty balance "
              + "list with no exception and nothing on ErrorStream. An empty balance list is how "
              + "this app spells 'the account is flat', and a refused read must never spell that.");
        }

        [Fact]
        public async Task The_sweep_reaches_the_wire_on_most_venues()
        {
            var names = TradingProviderTypeNames().Select(r => (string)r[0]).ToList();
            Assert.True(names.Count >= 10,
                $"Only {names.Count} trading providers enumerated: {string.Join(", ", names)}");

            int attempted = 0;
            foreach (var name in names)
                if ((await Ask(name)).Attempted) attempted++;

            Assert.True(attempted >= 8,
                $"Only {attempted} of {names.Count} trading providers made an HTTP call under the "
              + "sweep, so the rule above is close to never firing. Either the credential "
              + "dictionary no longer satisfies their guards or the client swap is missing them.");
        }
    }

    /// <summary>
    /// <b>An order book that could not be read is not a book with no liquidity.</b>
    ///
    /// <para>
    /// The rule <see cref="ProviderSilentFailureTests"/> states, converted from a
    /// four-venue spot check into a roster sweep — that file's own history is the
    /// argument: its summary named the symbol-list rule while covering three venues'
    /// order books, and the venues it did not name (OANDA, Polygon, Interactive
    /// Brokers) all carried a bare <c>catch { return (new(), new()); }</c> on this
    /// exact path. A venue that does not offer a book (a stub returning empty with no
    /// HTTP call) is a legitimate answer and is skipped at the wire.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderOrderBookSilenceTests
    {
        public static IEnumerable<object[]> MarketDataProviderTypeNames()
            => TradingReadHarness.MarketDataProviderTypeNames();

        private static Task<(Exception? Threw, int Count, List<string> Said, bool Attempted)> Ask(string typeName)
            => TradingReadHarness.AskAsync(typeName, async p =>
            {
                var (bids, asks) = await p.GetOrderBookAsync(TradingReadHarness.SymbolFor(p));
                return bids.Count + asks.Count;
            });

        [Theory]
        [MemberData(nameof(MarketDataProviderTypeNames))]
        public async Task A_failed_order_book_read_throws_or_says_so(string providerTypeName)
        {
            var r = await Ask(providerTypeName);

            if (!r.Attempted) return;
            if (r.Threw != null) return;
            if (r.Count > 0) return;

            Assert.True(r.Said.Count > 0,
                $"{providerTypeName} reached the wire, was refused, and returned an empty order "
              + "book with no exception and nothing on ErrorStream. For this product's audience "
              + "an empty ladder is indistinguishable from a book with no liquidity, and those "
              + "are opposite facts.");
        }

        [Fact]
        public async Task The_sweep_reaches_the_wire_on_most_venues()
        {
            var names = MarketDataProviderTypeNames().Select(r => (string)r[0]).ToList();

            int attempted = 0;
            foreach (var name in names)
                if ((await Ask(name)).Attempted) attempted++;

            Assert.True(attempted >= 9,
                $"Only {attempted} of {names.Count} venues made an HTTP call for the order book "
              + "under the sweep. The stubs (Finnhub, FMP, Twelve Data) are expected skips; this "
              + "floor firing means real book-reading venues stopped reaching the wire.");
        }
    }

    /// <summary>
    /// <b>A chart that could not load is not a market with no bars.</b>
    ///
    /// <para>
    /// The OHLCV half of the same rule. Every venue already had SOME failure handling
    /// here — this sweep is what keeps it true for the next venue and the next
    /// refactor, because the symbol-list session proved the pattern: the rule was
    /// written down, four providers deep, aimed one method to the left of where it
    /// was needed. Empty with nothing said and nothing thrown is the one outcome a
    /// refused fetch is not allowed to produce.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderOhlcvFailureSilenceTests
    {
        public static IEnumerable<object[]> MarketDataProviderTypeNames()
            => TradingReadHarness.MarketDataProviderTypeNames();

        private static Task<(Exception? Threw, int Count, List<string> Said, bool Attempted)> Ask(string typeName)
            => TradingReadHarness.AskAsync(typeName, async p =>
            {
                var request = new MarketDataRequest(
                    p.SupportedMarkets[0].ToString(), TradingReadHarness.SymbolFor(p), "1h", 50);
                var (ohlcv, _) = await p.FetchOhlcvAsync(request);
                return ohlcv.Count;
            });

        [Theory]
        [MemberData(nameof(MarketDataProviderTypeNames))]
        public async Task A_failed_fetch_throws_or_says_so(string providerTypeName)
        {
            var r = await Ask(providerTypeName);

            if (!r.Attempted) return;
            if (r.Threw != null) return;
            if (r.Count > 0) return;

            Assert.True(r.Said.Count > 0,
                $"{providerTypeName} reached the wire, was refused, and returned an empty candle "
              + "list with no exception and nothing on ErrorStream — a dead feed dressed as a "
              + "quiet market.");
        }

        [Fact]
        public async Task The_sweep_reaches_the_wire_on_most_venues()
        {
            var names = MarketDataProviderTypeNames().Select(r => (string)r[0]).ToList();

            int attempted = 0;
            foreach (var name in names)
                if ((await Ask(name)).Attempted) attempted++;

            Assert.True(attempted >= 12,
                $"Only {attempted} of {names.Count} venues made an HTTP call for candles under "
              + "the sweep (Schwab, which needs a seeded refresh token, is the expected skip).");
        }
    }

    /// <summary>
    /// <b>The nine status-blind reads, pinned one by one.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Nine call sites across seven providers read the HTTP response body without
    /// ever consulting the status code — on trading and streaming paths the roster
    /// sweeps above cannot all reach (signed sends, session mints, conid lookups).
    /// The venue-specific hazard is worse than an empty read: MEXC's spot order
    /// placement treated ANY parseable body without a <c>code</c> field as success,
    /// so a proxy 502 answering <c>{"message":"bad gateway"}</c> announced "order
    /// placed" for an order the venue never booked. Bitstamp's placement did the
    /// same through <c>json["id"] ?? "ORDER_SUBMITTED"</c>.
    /// </para>
    ///
    /// <para>
    /// ── The contract each fix keeps ────────────────────────────────────────────
    /// Body first, then status (the Gemini <c>ReadJson</c> order): a refusal the
    /// venue explains in its own error shape keeps its classification whatever the
    /// status code, and any other non-2xx throws an <see cref="HttpRequestException"/>
    /// naming the status and the PATH — never the URL, because private URLs are the
    /// credential-leak channel five providers once shipped.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderStatusBlindReadTests
    {
        private static FakeHttpMessageHandler AllRoutes(string body, HttpStatusCode status)
        {
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
                handler.Add(method, ".*", body, status);
            return handler;
        }

        private static void SwapField(object target, string name, object? value)
        {
            FieldInfo? field = null;
            for (var t = target.GetType(); t != null && field == null; t = t.BaseType)
                field = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            (field ?? throw new InvalidOperationException($"{target.GetType().Name} has no field {name}"))
                .SetValue(target, value);
        }

        // ── Kraken Futures: the signed pair (/accounts, /sendorder) ───────────

        [Fact]
        public async Task KrakenFutures_an_unexplained_gateway_refusal_throws_with_the_status()
        {
            using var p = new AccessibleTrader.Plugins.KrakenFutures.KrakenFuturesProvider();
            p.Configure(SymbolListHarness.Credentials());
            SymbolListHarness.SwapEveryHttpClient(p, AllRoutes("""{"message":"bad gateway"}""", HttpStatusCode.BadGateway));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => p.GetBalancesAsync());

            Assert.Contains("502", ex.Message);
            // The path may travel; the host and query string never do.
            Assert.DoesNotContain("futures.kraken.com", ex.Message);
        }

        [Fact]
        public async Task KrakenFutures_a_refusal_the_venue_explains_keeps_its_classification()
        {
            // The same 401 status — but the body carries Kraken Futures' own error
            // shape, so the classified UnauthorizedAccessException (which the order
            // service maps to "fix your key", not "venue down") must survive the
            // status check that was added in front of it.
            using var p = new AccessibleTrader.Plugins.KrakenFutures.KrakenFuturesProvider();
            p.Configure(SymbolListHarness.Credentials());
            SymbolListHarness.SwapEveryHttpClient(p,
                AllRoutes("""{"result":"error","error":"authenticationError"}""", HttpStatusCode.Unauthorized));

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => p.GetBalancesAsync());

            Assert.Contains("authenticationError", ex.Message);
        }

        // ── Kraken spot: the signed send + the error array ────────────────────

        [Fact]
        public async Task Kraken_an_unexplained_gateway_refusal_throws_with_the_status()
        {
            using var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            p.Configure(SymbolListHarness.Credentials());
            SymbolListHarness.SwapEveryHttpClient(p, AllRoutes("""{"message":"bad gateway"}""", HttpStatusCode.BadGateway));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => p.GetBalancesAsync());

            Assert.Contains("502", ex.Message);
            Assert.DoesNotContain("nonce", ex.Message);
        }

        [Fact]
        public async Task Kraken_an_error_array_no_longer_reads_as_an_account_that_is_flat()
        {
            // HTTP 200 — Kraken's normal way to refuse. The old read looked only for
            // json["result"], found none, and reported the account FLAT.
            using var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            p.Configure(SymbolListHarness.Credentials());
            SymbolListHarness.SwapEveryHttpClient(p,
                AllRoutes("""{"error":["EAPI:Invalid key"]}""", HttpStatusCode.OK));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => p.GetBalancesAsync());

            Assert.Contains("EAPI:Invalid key", ex.Message);
        }

        [Fact]
        public async Task Kraken_open_positions_refused_by_the_venue_throw_too()
        {
            // The same blindness existed on all four private READS while AddOrder
            // checked the array — one venue disagreeing with itself about whether a
            // refusal is worth mentioning.
            using var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            p.Configure(SymbolListHarness.Credentials());
            SymbolListHarness.SwapEveryHttpClient(p,
                AllRoutes("""{"error":["EAPI:Invalid key"]}""", HttpStatusCode.OK));

            await Assert.ThrowsAsync<InvalidOperationException>(() => p.GetPositionsAsync());
        }

        // ── Bitstamp: the signed POST ─────────────────────────────────────────

        [Fact]
        public async Task Bitstamp_an_unexplained_gateway_refusal_throws_with_the_status()
        {
            using var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
            p.Configure(SymbolListHarness.Credentials());
            SymbolListHarness.SwapEveryHttpClient(p, AllRoutes("""{"message":"bad gateway"}""", HttpStatusCode.BadGateway));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => p.GetBalancesAsync());

            Assert.Contains("502", ex.Message);
        }

        [Fact]
        public async Task Bitstamp_an_explained_refusal_is_classified_not_read_as_flat()
        {
            // Bitstamp explains refusals as status:"error" + reason. That body parses
            // fine, has no *_available properties, and used to come back as an empty
            // balance list — an account that is FLAT.
            using var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
            p.Configure(SymbolListHarness.Credentials());
            SymbolListHarness.SwapEveryHttpClient(p,
                AllRoutes("""{"status":"error","reason":"API key not found","code":"API0001"}""", HttpStatusCode.Forbidden));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => p.GetBalancesAsync());

            Assert.Contains("API key not found", ex.Message);
        }

        [Fact]
        public async Task Bitstamp_a_refused_order_placement_does_not_announce_success()
        {
            // json["id"] ?? "ORDER_SUBMITTED" — so any refusal body without an "id"
            // used to read as an order placed. The double-guard: an explained refusal
            // becomes ORDER_FAILED with the venue's reason, and (tested above) an
            // unexplained one throws into the catch, which also reports ORDER_FAILED.
            using var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
            p.Configure(SymbolListHarness.Credentials());
            SymbolListHarness.SwapEveryHttpClient(p,
                AllRoutes("""{"status":"error","reason":"Minimum order size is 10 USD","code":"API0005"}""", HttpStatusCode.Forbidden));

            string result = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Buy, 0.001, OrderType.Limit, Price: 100));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("Minimum order size", result);
        }

        // ── Coinbase: the signed GET helper every account read shares ─────────

        [Fact]
        public async Task Coinbase_a_refused_balance_read_throws_with_the_status()
        {
            using var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "k", ["ApiSecret"] = "s" });
            SymbolListHarness.SwapEveryHttpClient(p, AllRoutes("""{"error":"Unauthorized"}""", HttpStatusCode.Unauthorized));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => p.GetBalancesAsync());

            Assert.Contains("401", ex.Message);
            Assert.Contains("/api/v3/brokerage/accounts", ex.Message);
        }

        // ── MEXC: the signed spot/futures REST pair ───────────────────────────

        private static AccessibleTrader.Plugins.Mexc.MexcProvider Mexc(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Mexc.MexcProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "mk", ["ApiSecret"] = "ms" });
            // _rest captured the original HttpClient at construction, so the client
            // swap must rebuild it — swapping _http alone changes nothing.
            SwapField(p, "_rest", new AccessibleTrader.Plugins.Mexc.MexcRestApi(new HttpClient(h)));
            SwapField(p, "_connected", true); // IsConnected gates the trading calls
            return p;
        }

        [Fact]
        public async Task Mexc_a_gateway_502_on_order_placement_does_not_announce_success()
        {
            // THE case this whole batch exists for. The placement read
            // json["code"] != null ? classify : (json["orderId"] ?? "ORDER_SUBMITTED")
            // — so a proxy 502 whose JSON carries neither field announced "order
            // placed" for an order MEXC never booked. A false "Order placed" is the
            // exact defect class B1 existed to kill.
            var h = AllRoutes("""{"message":"bad gateway"}""", HttpStatusCode.BadGateway);
            using var p = Mexc(h);
            var said = SymbolListHarness.Recorded(p);

            string result = await p.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 0.5, OrderType.Limit, Price: 90000));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.DoesNotContain("ORDER_SUBMITTED", result);
            Assert.Contains(said, m => m.Contains("order", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Mexc_a_refusal_the_venue_explains_keeps_its_message()
        {
            // Same 4xx band — but MEXC's own code+msg shape must keep flowing to the
            // caller's classifier, or every venue refusal flattens into a bare
            // exception type name.
            var h = AllRoutes("""{"code":30004,"msg":"Insufficient balance"}""", HttpStatusCode.BadRequest);
            using var p = Mexc(h);

            string result = await p.PlaceOrderAsync(new TradeSignal("BTCUSDT", OrderSide.Buy, 0.5, OrderType.Limit, Price: 90000));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("Insufficient balance", result);
        }

        [Fact]
        public async Task Mexc_an_explained_refusal_of_the_balance_read_throws_not_flat()
        {
            // BodyOrThrow hands explained bodies through — so the balance reader
            // itself must refuse to mine them for balances.
            var h = AllRoutes("""{"code":700002,"msg":"Signature for this request is not valid."}""", HttpStatusCode.BadRequest);
            using var p = Mexc(h);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => p.GetBalancesAsync());

            Assert.Contains("Signature", ex.Message);
        }

        // ── Interactive Brokers: conid resolution, the root of every path ─────

        [Fact]
        public async Task IBKR_a_failed_book_read_after_a_resolved_conid_is_said()
        {
            // The roster sweep cannot prove this one: on a dead transport the conid
            // lookup fails FIRST and its own message satisfies the sweep's rule, so
            // the book read's former bare catch was unreachable there. Resolve the
            // conid for real, then kill only the rest of the transport.
            using var p = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
            p.Configure(new Dictionary<string, string> { ["AccountId"] = "DU123" });
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            handler.Post(@"iserver/secdef/search", """[{"conid":"265598"}]""");
            handler.Add(HttpMethod.Get, ".*", """{"message":"bad gateway"}""", HttpStatusCode.BadGateway);
            handler.Add(HttpMethod.Post, ".*", """{"message":"bad gateway"}""", HttpStatusCode.BadGateway);
            SymbolListHarness.SwapEveryHttpClient(p, handler);
            var said = SymbolListHarness.Recorded(p);

            var (bids, asks) = await p.GetOrderBookAsync("AAPL");

            Assert.Empty(bids);
            Assert.Empty(asks);
            Assert.Contains(said, m => m.Contains("order book", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task IBKR_a_refused_conid_resolution_is_said_not_a_silent_unknown_symbol()
        {
            using var p = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
            p.Configure(new Dictionary<string, string> { ["AccountId"] = "DU123" });
            SymbolListHarness.SwapEveryHttpClient(p, AllRoutes("""{"error":"not authenticated"}""", HttpStatusCode.Unauthorized));
            var said = SymbolListHarness.Recorded(p);

            string result = await p.PlaceOrderAsync(new TradeSignal("AAPL", OrderSide.Buy, 1, OrderType.Limit, Price: 100));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains(said, m => m.Contains("resolve", StringComparison.OrdinalIgnoreCase)
                                    && m.Contains("401", StringComparison.Ordinal));
        }

        // ── Tradier: the streaming session mint ───────────────────────────────

        [Fact]
        public async Task Tradier_a_refused_stream_session_names_the_refusal_not_a_parse_error()
        {
            // Tradier answers a refused session mint with a PLAIN-TEXT body, so the
            // old blind JObject.Parse turned a 401 into a JsonReaderException and the
            // retry loop spoke a parse error every five seconds.
            using var p = new AccessibleTrader.Plugins.Tradier.TradierProvider();
            p.Configure(new Dictionary<string, string> { ["AccessToken"] = "tok", ["AccountId"] = "acct" });
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            handler.Add(HttpMethod.Post, ".*", "Invalid Access Token", HttpStatusCode.Unauthorized, "text/plain");
            SymbolListHarness.SwapEveryHttpClient(p, handler);
            var said = SymbolListHarness.Recorded(p);

            using var cts = new CancellationTokenSource();
            var method = typeof(AccessibleTrader.Plugins.Tradier.TradierProvider)
                .GetMethod("StreamEventsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var loop = (Task)method.Invoke(p, new object[] { "AAPL", cts.Token })!;

            // Wait for the first loop iteration to report, then stop the loop. Not a
            // stopwatch: the deadline only bounds how long we are willing to wait for
            // an event that the code under test emits before its first back-off delay.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (said.Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(25);
            cts.Cancel();
            try { await loop; } catch (OperationCanceledException) { }

            Assert.Contains(said, m => m.Contains("HTTP 401", StringComparison.Ordinal));
            Assert.DoesNotContain(said, m => m.Contains("JsonReader", StringComparison.OrdinalIgnoreCase));
        }
    }
}
