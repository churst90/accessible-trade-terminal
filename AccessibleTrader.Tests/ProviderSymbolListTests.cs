using System.Net;
using System.Reflection;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Shared rig for the symbol-list suites: configure a provider, then make every HTTP client
    /// it owns — including the ones held by nested helper objects — answer from a fake.
    ///
    /// <para>
    /// <c>ProviderFetchOhlcvTests</c> and <c>ProviderSilentFailureTests</c> each grew their own
    /// half of this and each swaps only what its own cases needed. The nested-object half matters
    /// here because two providers reach the network through a helper that captured the client at
    /// construction (MEXC's <c>MexcRestApi</c>, Schwab's OAuth service): swapping the provider's
    /// own field alone leaves the REAL transport in place, and the test then passes while making
    /// a live call — the failure mode <c>ProviderSilentFailureTests</c> documents being burned by.
    /// </para>
    /// </summary>
    internal static class SymbolListHarness
    {
        /// <summary>
        /// Every credential key any provider reads, so one dictionary configures all of them.
        /// Deliberately excludes the keys that are SETTINGS rather than credentials —
        /// <c>Environment</c>, <c>Testnet</c>, <c>GatewayUrl</c>, <c>GatewayCertSha256</c>,
        /// <c>Plan</c>, <c>Feed</c>, <c>RedirectUri</c> — because those change base URLs and TLS
        /// pinning, and a sweep that quietly repoints a provider is testing something else.
        /// </summary>
        public static Dictionary<string, string> Credentials() => new()
        {
            ["ApiKey"]      = "test-key",
            ["ApiSecret"]   = "test-secret",
            ["AccessToken"] = "test-token",
            ["AccountId"]   = "test-account",
            ["CustomerId"]  = "test-customer",
            ["Passphrase"]  = "test-passphrase",
        };

        /// <summary>
        /// A transport where every route answers 401.
        ///
        /// <para>
        /// 401 rather than a thrown <c>HttpRequestException</c> with no status on purpose:
        /// <c>RateLimiter.ShouldRetry</c> retries a statusless network fault three times with
        /// exponential backoff, which would add ~3.5 s per provider to a sweep of seventeen. A
        /// 4xx is not retried, and "the key is wrong" is the case the reported bug actually wore
        /// — the terminal showed an empty dropdown and said nothing.
        /// </para>
        /// </summary>
        public static FakeHttpMessageHandler DeadTransport()
        {
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
                handler.Add(method, ".*", """{"error":"unauthorized"}""", HttpStatusCode.Unauthorized);
            return handler;
        }

        /// <summary>Replaces every HttpClient the object can reach, one level of nesting deep.</summary>
        public static void SwapEveryHttpClient(object root, HttpMessageHandler handler)
        {
            SwapDirect(root, handler);

            foreach (var field in Fields(root))
            {
                if (field.FieldType == typeof(HttpClient)) continue;
                if (!field.FieldType.IsClass || field.FieldType.IsArray) continue;
                // Only walk into types this repo ships: recursing into BCL or third-party
                // graphs finds clients nothing is going to call and can trip on lazy state.
                if (field.FieldType.Namespace?.StartsWith("AccessibleTrader", StringComparison.Ordinal) != true) continue;

                var nested = field.GetValue(root);
                if (nested != null) SwapDirect(nested, handler);
            }
        }

        private static void SwapDirect(object target, HttpMessageHandler handler)
        {
            foreach (var field in Fields(target))
            {
                if (field.FieldType == typeof(HttpClient))
                    field.SetValue(target, new HttpClient(handler));
            }
        }

        private static IEnumerable<FieldInfo> Fields(object target)
        {
            for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        public static List<string> Recorded(BaseMarketDataProvider provider)
        {
            var messages = new List<string>();
            provider.ErrorStream.Subscribe(messages.Add);
            return messages;
        }
    }

    /// <summary>
    /// <b>An empty symbol list is not allowed to be the only thing the user hears.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Twelve Data asked <c>/stocks?exchange=NYSE,NASDAQ</c>. The endpoint takes one exchange per
    /// call and does not reject the list — it answers HTTP 200 with
    /// <c>{"data":[],"count":0,"status":"ok"}</c>. An empty success, byte-for-byte what a market
    /// with genuinely no symbols looks like. The provider then wrapped the whole read in
    /// <c>catch { return new List&lt;string&gt;(); }</c>, so a refused key, a quota wall, a dead
    /// network and an honestly empty market all arrived at the dropdown as the same silence.
    /// </para>
    ///
    /// <para>
    /// ── Why this sweep and not four more cases ─────────────────────────────────
    /// <see cref="ProviderSilentFailureTests"/> is aimed at exactly this rule — its own summary
    /// is "an empty answer and a dead endpoint are not the same fact", and its prose even names
    /// Coinbase's symbol list. Every one of its assertions is about the order book, the cancel or
    /// the leverage call, and its provider list is typed out by hand. So the rule was written
    /// down, four providers deep, and the symbol path had no coverage in the suite at all — not
    /// one test called <c>GetAvailableSymbolsAsync</c> on any of the seventeen market-data
    /// providers. This enumerates <see cref="ProviderRoster"/> instead, the way
    /// <see cref="ProviderConformanceTests"/> does, so a new venue is enrolled by existing.
    /// </para>
    ///
    /// <para>
    /// ── The rule ───────────────────────────────────────────────────────────────
    /// With every route answering 401, ask each venue for the symbols of a market and sub-type it
    /// itself declares. A provider that still returns a list is fine — a static suggestion set is
    /// a legitimate answer. A provider that returns NOTHING must have said why.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderSymbolListSilenceTests
    {
        /// <summary>
        /// The venues, not the analytics feeds: an analytics provider's "symbols" are the series
        /// it computes, and a fixed list of those is the right answer rather than a fetch that
        /// can fail.
        /// </summary>
        public static IEnumerable<object[]> MarketDataProviderTypeNames()
        {
            var venues = RepoPaths.MarketDataPluginProjectsOnDisk()
                .Select(project => "AccessibleTrader.Plugins." + project.Replace("AccessibleTrader.Plugins.", ""))
                .ToHashSet(StringComparer.Ordinal);

            return ProviderRoster.Types
                .Where(t => venues.Contains(t.Namespace ?? "")
                         || venues.Any(v => (t.Namespace ?? "").StartsWith(v + ".", StringComparison.Ordinal)))
                .Select(t => new object[] { t.FullName! });
        }

        private static async Task<(List<string> Symbols, List<string> Said)> AskForSymbolsAsync(string typeName)
        {
            var provider = ProviderRoster.All().First(p => p.GetType().FullName == typeName);
            try
            {
                provider.Configure(SymbolListHarness.Credentials());
                SymbolListHarness.SwapEveryHttpClient(provider, SymbolListHarness.DeadTransport());
                var said = SymbolListHarness.Recorded(provider);

                var market = provider.SupportedMarkets[0];
                // The provider's own sub-type, not a guessed "Spot": several route on it, and one
                // asked for a sub-type a venue does not offer returns an honest empty list for a
                // reason that has nothing to do with this rule.
                string subType = (await provider.GetSupportedSubTypesAsync(market)).FirstOrDefault() ?? "Spot";

                var symbols = await provider.GetAvailableSymbolsAsync(market, subType);
                return (symbols, said);
            }
            finally
            {
                provider.Dispose();
            }
        }

        [Theory]
        [MemberData(nameof(MarketDataProviderTypeNames))]
        public async Task An_empty_symbol_list_is_explained(string providerTypeName)
        {
            var (symbols, said) = await AskForSymbolsAsync(providerTypeName);

            if (symbols.Count > 0) return;   // a list is an answer; nothing to explain

            Assert.True(said.Count > 0,
                $"{providerTypeName} returned an empty symbol list and said nothing. An empty "
              + "dropdown is how this app spells both 'this market has no symbols' and 'the "
              + "lookup failed', and the user cannot tell them apart. Push a message to "
              + "ErrorStream (SurfaceError) naming which one it is.");
        }

        /// <summary>
        /// Anti-vacuity. Two ways this sweep could pass on no work: the roster could shrink to
        /// nothing (a dropped ProjectReference — see <see cref="ProviderRosterDriftTests"/>), or
        /// every provider could return a non-empty static list, in which case the early
        /// <c>return</c> above fires every time and nothing is ever asserted.
        /// </summary>
        [Fact]
        public async Task The_sweep_covers_every_venue_and_the_rule_actually_fires()
        {
            var names = MarketDataProviderTypeNames().Select(r => (string)r[0]).ToList();

            Assert.True(names.Count >= 16,
                $"Only {names.Count} market-data providers swept; the repo ships more: "
              + string.Join(", ", names));

            int wentEmpty = 0;
            foreach (var name in names)
            {
                var (symbols, _) = await AskForSymbolsAsync(name);
                if (symbols.Count == 0) wentEmpty++;
            }

            Assert.True(wentEmpty >= 8,
                $"Only {wentEmpty} of {names.Count} providers returned an empty list under a dead "
              + "transport, so the rule above is close to never firing. Check that the fake "
              + "handler is actually reaching each provider's client.");
        }
    }

    /// <summary>
    /// <b>Twelve Data's stock list: one exchange per request, and a cut that says so.</b>
    ///
    /// <para>
    /// Two defects stacked, either of which alone empties the market. The comma-separated
    /// <c>exchange=NYSE,NASDAQ</c> returned zero rows as an HTTP 200 success. And the merged list
    /// is 7,570 symbols, which the old <c>.OrderBy(s => s).Take(2000)</c> cut off at
    /// <c>DIBS</c> — <c>AAPL</c> survived, <c>MSFT</c>, <c>NVDA</c> and <c>TSLA</c> did not.
    /// Fixing only the first would still have left TSLA unselectable, which is why both halves
    /// are pinned separately.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class TwelveDataSymbolListTests
    {
        private static AccessibleTrader.Plugins.TwelveData.TwelveDataProvider NewConfigured(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.TwelveData.TwelveDataProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
            SymbolListHarness.SwapEveryHttpClient(p, h);
            return p;
        }

        /// <summary>Twelve Data's /stocks shape: {"data":[{"symbol":"…"}],"status":"ok"}.</summary>
        private static string StocksBody(IEnumerable<string> symbols) =>
            $$"""{"data":[{{string.Join(",", symbols.Select(s => $$"""{"symbol":"{{s}}"}"""))}}],"status":"ok"}""";

        // ── 2. The comma-separated exchange is never constructed ──────────────

        [Fact]
        public async Task Each_exchange_is_asked_for_separately()
        {
            var handler = new FakeHttpMessageHandler()
                .Get(@"twelvedata\.com/stocks.*exchange=NYSE",   StocksBody(new[] { "GE", "JPM" }))
                .Get(@"twelvedata\.com/stocks.*exchange=NASDAQ", StocksBody(new[] { "AAPL", "TSLA" }));
            using var provider = NewConfigured(handler);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Equal(2, handler.Captured.Count);
            var urls = handler.Captured.Select(r => r.RequestUri!.ToString()).ToList();
            Assert.Contains(urls, u => u.Contains("exchange=NYSE", StringComparison.Ordinal));
            Assert.Contains(urls, u => u.Contains("exchange=NASDAQ", StringComparison.Ordinal));
            // The defect itself: a list in one call. The endpoint accepts it and answers with
            // zero rows and status "ok", so nothing downstream can notice.
            Assert.DoesNotContain(urls, u => u.Contains("exchange=NYSE,", StringComparison.Ordinal)
                                          || u.Contains("%2C", StringComparison.OrdinalIgnoreCase));
            // Both responses are merged, not just the last one to answer.
            Assert.Equal(new[] { "AAPL", "GE", "JPM", "TSLA" }, symbols);
        }

        [Theory]
        [InlineData(MarketType.Forex,  "forex_pairs")]
        [InlineData(MarketType.Crypto, "cryptocurrencies")]
        [InlineData(MarketType.Index,  "indices")]
        public async Task The_non_stock_markets_still_take_a_single_request(MarketType market, string endpoint)
        {
            // Vacuity guard on the case above: splitting per exchange must not have turned the
            // one-call markets into two calls against an endpoint that has no exchange parameter.
            var handler = new FakeHttpMessageHandler().Get($@"twelvedata\.com/{endpoint}", StocksBody(new[] { "X" }));
            using var provider = NewConfigured(handler);

            var symbols = await provider.GetAvailableSymbolsAsync(market);

            Assert.Single(handler.Captured);
            Assert.Equal(new[] { "X" }, symbols);
        }

        // ── 3. An error carried in the body of a 200 ──────────────────────────

        [Fact]
        public async Task A_refusal_reported_as_HTTP_200_is_said_not_swallowed()
        {
            // Twelve Data reports quota and plan refusals in the BODY with a 200 status, so a
            // status-code check never sees them and the list comes back empty and silent.
            var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com/stocks",
                """{"code":429,"message":"You have run out of API credits","status":"error"}""");
            using var provider = NewConfigured(handler);
            var said = SymbolListHarness.Recorded(provider);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Empty(symbols);
            Assert.Contains(said, m => m.Contains("refused", StringComparison.OrdinalIgnoreCase)
                                    && m.Contains("API credits", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task A_genuinely_empty_market_says_nothing()
        {
            // The other half of the rule, and the reason this is worth testing: "status":"ok"
            // with no rows is an honest answer and must not be dressed up as a failure.
            var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com/stocks", StocksBody(Array.Empty<string>()));
            using var provider = NewConfigured(handler);
            var said = SymbolListHarness.Recorded(provider);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Empty(symbols);
            Assert.Empty(said);
        }

        // ── 4. Truncation is announced ────────────────────────────────────────

        [Fact]
        public async Task A_list_over_the_cap_reports_the_cut_and_names_the_last_symbol()
        {
            // 10,050 unique symbols, alphabetically ordered by construction.
            var many = Enumerable.Range(0, 10_050).Select(i => $"SYM{i:D5}").ToList();
            var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com/stocks", StocksBody(many));
            using var provider = NewConfigured(handler);
            var said = SymbolListHarness.Recorded(provider);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Equal(10_000, symbols.Count);
            string last = symbols[^1];
            Assert.Equal("SYM09999", last);
            // Naming the last symbol that survived is what makes the cut actionable: it tells the
            // user where the list stops rather than leaving them to discover it a ticker at a time.
            Assert.Contains(said, m => m.Contains("truncated", StringComparison.OrdinalIgnoreCase)
                                    && m.Contains("10050", StringComparison.Ordinal)
                                    && m.Contains(last, StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_list_under_the_cap_reports_nothing()
        {
            // Vacuity check: announcing truncation unconditionally would satisfy the case above.
            var handler = new FakeHttpMessageHandler().Get(@"twelvedata\.com/stocks",
                StocksBody(Enumerable.Range(0, 50).Select(i => $"SYM{i:D5}")));
            using var provider = NewConfigured(handler);
            var said = SymbolListHarness.Recorded(provider);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Equal(50, symbols.Count);
            Assert.Empty(said);
        }

        // ── 5. The reported symptom, as a test ────────────────────────────────

        [Fact]
        public async Task TSLA_survives_a_list_longer_than_the_old_cap()
        {
            // The bug as the user met it. NYSE+NASDAQ merged is 7,570 unique symbols; sorted
            // alphabetically and cut at 2,000 the list ended at "DIBS". AAPL made it, so the
            // provider looked like it worked. TSLA did not, and nothing said so.
            //
            // 2,400 filler symbols all sort before TSLA, so with the old cap TSLA is past the
            // cut by 400 places. The assertion is on the SYMBOL, not the count: a cap raised
            // just far enough to pass a count check would still lose the tail of the alphabet.
            var filler = Enumerable.Range(0, 2_400).Select(i => $"AAA{i:D4}").ToList();
            var handler = new FakeHttpMessageHandler()
                .Get(@"twelvedata\.com/stocks.*exchange=NYSE",   StocksBody(filler))
                .Get(@"twelvedata\.com/stocks.*exchange=NASDAQ", StocksBody(new[] { "AAPL", "MSFT", "NVDA", "TSLA" }));
            using var provider = NewConfigured(handler);
            var said = SymbolListHarness.Recorded(provider);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Equal(2_404, symbols.Count);
            Assert.Contains("AAPL", symbols);   // survived the old cut too — why this looked fine
            Assert.Contains("MSFT", symbols);
            Assert.Contains("NVDA", symbols);
            Assert.Contains("TSLA", symbols);
            // Under the cap, so no truncation notice: the list is complete and says nothing.
            Assert.Empty(said);
        }

        [Fact]
        public async Task A_dead_symbol_lookup_says_so()
        {
            using var provider = NewConfigured(SymbolListHarness.DeadTransport());
            var said = SymbolListHarness.Recorded(provider);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Empty(symbols);
            Assert.Contains(said, m => m.Contains("symbol list", StringComparison.OrdinalIgnoreCase));
            // The key must not ride out on the message: ex.Message can carry the whole URL, and
            // apikey= is on it. This is the leak that five providers shipped.
            Assert.DoesNotContain(said, m => m.Contains("test", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// <b>Finnhub had the identical cut.</b>
    ///
    /// <para>
    /// The same <c>.OrderBy(s => s).Take(2000)</c> against <c>/stock/symbol?exchange=US</c>, which
    /// is far larger than 2,000 — so every US ticker after the alphabetical cut was unselectable
    /// there too, and nothing said so. Found by grepping for the shape of the Twelve Data defect
    /// rather than by a second report.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class FinnhubSymbolListTests
    {
        private static AccessibleTrader.Plugins.Finnhub.FinnhubProvider NewConfigured(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Finnhub.FinnhubProvider();
            p.Configure(new Dictionary<string, string> { ["ApiKey"] = "test" });
            SymbolListHarness.SwapEveryHttpClient(p, h);
            return p;
        }

        /// <summary>Finnhub's /stock/symbol shape: a top-level array of objects.</summary>
        private static string SymbolsBody(IEnumerable<string> symbols) =>
            "[" + string.Join(",", symbols.Select(s => $$"""{"symbol":"{{s}}","displaySymbol":"{{s}}"}""")) + "]";

        [Fact]
        public async Task A_list_over_the_cap_reports_the_cut_and_names_the_last_symbol()
        {
            var many = Enumerable.Range(0, 10_050).Select(i => $"SYM{i:D5}").ToList();
            var handler = new FakeHttpMessageHandler().Get(@"finnhub\.io.*stock/symbol", SymbolsBody(many));
            using var provider = NewConfigured(handler);
            var said = SymbolListHarness.Recorded(provider);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Equal(10_000, symbols.Count);
            Assert.Equal("SYM09999", symbols[^1]);
            Assert.Contains(said, m => m.Contains("truncated", StringComparison.OrdinalIgnoreCase)
                                    && m.Contains("10050", StringComparison.Ordinal)
                                    && m.Contains("SYM09999", StringComparison.Ordinal));
        }

        [Fact]
        public async Task TSLA_survives_a_list_longer_than_the_old_cap()
        {
            var listing = Enumerable.Range(0, 2_400).Select(i => $"AAA{i:D4}").Concat(new[] { "AAPL", "TSLA" });
            var handler = new FakeHttpMessageHandler().Get(@"finnhub\.io.*stock/symbol", SymbolsBody(listing));
            using var provider = NewConfigured(handler);
            var said = SymbolListHarness.Recorded(provider);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Equal(2_402, symbols.Count);
            Assert.Contains("TSLA", symbols);
            Assert.Empty(said);
        }

        [Fact]
        public async Task A_list_under_the_cap_reports_nothing()
        {
            var handler = new FakeHttpMessageHandler().Get(@"finnhub\.io.*stock/symbol",
                SymbolsBody(Enumerable.Range(0, 50).Select(i => $"SYM{i:D5}")));
            using var provider = NewConfigured(handler);
            var said = SymbolListHarness.Recorded(provider);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Equal(50, symbols.Count);
            Assert.Empty(said);
        }

        [Fact]
        public async Task A_dead_symbol_lookup_says_so()
        {
            using var provider = NewConfigured(SymbolListHarness.DeadTransport());
            var said = SymbolListHarness.Recorded(provider);

            var symbols = await provider.GetAvailableSymbolsAsync(MarketType.Stock);

            Assert.Empty(symbols);
            Assert.Contains(said, m => m.Contains("symbol list", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(said, m => m.Contains("test", StringComparison.OrdinalIgnoreCase));
        }
    }
}
