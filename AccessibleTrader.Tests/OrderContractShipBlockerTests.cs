using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Plugins.InteractiveBrokers;
using AccessibleTrader.Plugins.Mexc;
using AccessibleTrader.Plugins.Schwab;
using AccessibleTrader.Plugins.Tradier;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-08-21 audit's remaining order-contract ship-blockers, pinned:
    ///
    /// - IBKR reused the conId of the CHARTED symbol for any order — chart AAPL,
    ///   order MSFT from the panel, buy AAPL, real order id returned.
    /// - MEXC's futures branch turned a protective stop into an IMMEDIATE market
    ///   order (type 5, no trigger anywhere in the plugin), and mapped side as
    ///   Buy?1:3 without reading ReduceOnly — a sell-to-close opened an opposing
    ///   short in hedge mode.
    /// - Tradier truncated quantity with (int): 9.7 shares placed 9, 0.6 placed 0.
    /// - Schwab discarded the Location header that carries the only copy of the
    ///   order id, returned the literal "ORDER_SUBMITTED" — which prefix-matches
    ///   the error sentinel — so the fill poller and the protective-order
    ///   verification never started. Every fill was silent.
    /// - catch {{ return new(); }} on trading reads re-armed the reconciliation
    ///   incident ProviderResult.cs documents: a transient 502 on a positions
    ///   fetch read as "account flat" and overwrote the snapshot.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class OrderContractShipBlockerTests
    {
        // ── IBKR: the cached conId is only valid for the symbol it was resolved for ──

        [Fact]
        public void Ibkr_cached_conId_is_returned_only_for_the_charted_symbol()
        {
            var ib = new InteractiveBrokersProvider();
            ib.SeedConIdCacheForTest("AAPL", "265598");

            Assert.Equal("265598", ib.CachedConIdFor("AAPL"));
            Assert.Equal("265598", ib.CachedConIdFor("aapl")); // case-insensitive venue symbols
            Assert.Null(ib.CachedConIdFor("MSFT"));            // the audit's wrong-instrument order
        }

        [Fact]
        public void Ibkr_empty_or_stale_cache_never_supplies_a_conId()
        {
            var ib = new InteractiveBrokersProvider();
            Assert.Null(ib.CachedConIdFor("AAPL"));

            // Subscription changed but resolution failed: symbol updated, conId nulled.
            ib.SeedConIdCacheForTest("MSFT", null);
            Assert.Null(ib.CachedConIdFor("MSFT"));
        }

        [Fact]
        public void Ibkr_no_call_site_reads_the_raw_conId_field_as_a_fallback()
        {
            // The bug's shape was `_currentConId ?? await Resolve...` — reuse with
            // no symbol check. Every reuse must go through CachedConIdFor(symbol).
            string file = Path.Combine(RepoRoot(),
                "Plugins", "Providers", "AccessibleTrader.Plugins.InteractiveBrokers",
                "InteractiveBrokersProvider.cs");
            Assert.DoesNotContain("_currentConId ??", File.ReadAllText(file));
        }

        // ── MEXC: futures stop must refuse, sides must honor ReduceOnly ──────────

        [Theory]
        [InlineData(OrderType.Market, true)]
        [InlineData(OrderType.Limit, true)]
        // The audit's incident: StopMarket became type 5 — an immediate market
        // order. A stop at 90,000 with the mark at 100,000 flattened the position
        // NOW at 100,000 and reported success.
        [InlineData(OrderType.StopMarket, false)]
        [InlineData(OrderType.StopLimit, false)]
        [InlineData(OrderType.TakeProfitMarket, false)]
        [InlineData(OrderType.TakeProfitLimit, false)]
        public void Mexc_futures_supports_only_market_and_limit_entries(OrderType type, bool supported)
            => Assert.Equal(supported, MexcProvider.IsSupportedFuturesEntryType(type));

        [Theory]
        [InlineData(OrderSide.Buy, false, 1)]  // open long
        [InlineData(OrderSide.Buy, true, 2)]  // close short — was 1 (opened MORE long)
        [InlineData(OrderSide.Sell, false, 3)]  // open short
        [InlineData(OrderSide.Sell, true, 4)]  // close long — was 3 (opened an opposing short)
        public void Mexc_futures_side_reads_ReduceOnly(OrderSide side, bool reduceOnly, int wire)
            => Assert.Equal(wire, MexcProvider.MapFuturesSide(side, reduceOnly));

        [Theory]
        [InlineData(1, OrderSide.Buy)]   // open long
        [InlineData(2, OrderSide.Buy)]   // close short
        [InlineData(3, OrderSide.Sell)]  // open short
        [InlineData(4, OrderSide.Sell)]  // close long — the old read called this a Buy
        public void Mexc_futures_side_reads_back_symmetrically(int wire, OrderSide side)
            => Assert.Equal(side, MexcProvider.MapFuturesSideToOrderSide(wire));

        // ── Tradier: whole shares or refusal, never silent truncation ────────────

        [Theory]
        [InlineData(9.0, "9")]
        [InlineData(1.0, "1")]
        [InlineData(250.0, "250")]
        [InlineData(9.0000000001, "9")]     // float noise on a whole number is fine
        public void Tradier_whole_share_quantities_format_exactly(double qty, string wire)
            => Assert.Equal(wire, TradierProvider.WholeShareQuantityOrNull(qty));

        [Theory]
        [InlineData(9.7)]   // the audit's risk-sizer case: placed 9
        [InlineData(0.6)]   // truncated to 0 — and 0.6 passes IsFinitePositive upstream
        [InlineData(0.0)]
        [InlineData(-3.0)]
        [InlineData(double.NaN)]
        public void Tradier_fractional_quantities_are_refused_not_truncated(double qty)
            => Assert.Null(TradierProvider.WholeShareQuantityOrNull(qty));

        [Fact]
        public async Task Tradier_PlaceOrder_speaks_the_whole_share_refusal()
        {
            // Configured + account id ⇒ IsConnected, so the quantity guard is
            // reached before any HTTP happens.
            var tradier = new TradierProvider();
            tradier.Configure(new Dictionary<string, string>
            {
                ["AccessToken"] = "test-token",
                ["AccountId"] = "TEST123",
            });

            var result = await tradier.PlaceOrderAsync(new TradeSignal(
                "AAPL", OrderSide.Buy, Quantity: 9.7, OrderType.Market));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("whole shares", result);
            Assert.Contains("9.7", result); // the refused number, spoken invariantly
        }

        // ── Schwab: the order id lives in the Location header ────────────────────

        [Theory]
        [InlineData("https://api.schwabapi.com/trader/v1/accounts/HASH/orders/1004055538889", "1004055538889")]
        [InlineData("https://api.schwabapi.com/trader/v1/accounts/HASH/orders/42/", "42")]
        public void Schwab_order_id_is_the_last_Location_segment(string location, string id)
            => Assert.Equal(id, SchwabProvider.OrderIdFromLocation(location));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("https://api.schwabapi.com/trader/v1/accounts/HASH/orders/")]  // no id segment
        [InlineData("https://api.schwabapi.com/trader/v1/accounts/HASH/orders")]   // "orders" is not an id
        public void Schwab_rejects_location_shapes_that_are_not_ids(string? location)
            => Assert.Null(SchwabProvider.OrderIdFromLocation(location));

        // ── The service half: a real id starts the poller; the no-id fallback warns ──

        private static readonly TradeSignal SaneSignal = new(
            "AAPL", OrderSide.Buy, Quantity: 1, OrderType.Market);

        private static (GeneralOrderService svc, ITradingProvider tp, IGlobalErrorCoordinator err) BuildService()
        {
            var data = Substitute.For<IDataService>();
            var tp = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var trading = (ITradingProvider)tp;
            trading.IsConnected.Returns(true);
            trading.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tp));
            var err = Substitute.For<IGlobalErrorCoordinator>();
            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            var settings = Substitute.For<ISettingsManager>();
            var svc = new GeneralOrderService(
                data, err, NullLogger<GeneralOrderService>.Instance, new EventBus(), paper, settings,
                new DemoPolicy(isDemo: false), new AccessibleTrader.Core.Services.Trading.QuickTradeEquity());
            return (svc, trading, err);
        }

        [Fact]
        public async Task A_real_order_id_from_a_non_streaming_provider_starts_the_fill_poller()
        {
            // Schwab's whole silent-fill defect in one assertion: with a real id
            // (now extracted from Location) the poll watch MUST start.
            var (svc, tp, _) = BuildService();
            tp.SupportsOrderEventStreaming.Returns(false);
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns("1004055538889");

            await svc.PlaceOrderAsync("Schwab", SaneSignal);

            Assert.Equal(1, svc.OrderWatchesStarted);
        }

        [Fact]
        public async Task The_no_id_fallback_warns_that_the_fill_will_be_silent()
        {
            // "ORDER_SUBMITTED" means venue-accepted-but-no-id. Nothing can be
            // polled, so the trader must be TOLD the outcome won't announce —
            // nine providers still use this fallback on rare body shapes.
            var (svc, tp, err) = BuildService();
            tp.SupportsOrderEventStreaming.Returns(false);
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns("ORDER_SUBMITTED");

            await svc.PlaceOrderAsync("Schwab", SaneSignal);

            Assert.Equal(0, svc.OrderWatchesStarted);
            err.Received().ReportError(
                Arg.Is<string>(m => m.Contains("did not return an order id")),
                Arg.Any<ErrorSeverity>(), Arg.Any<ErrorCategory>());
        }

        // ── Trading reads must throw, not swallow ────────────────────────────────

        private static readonly Regex TradingReadSignature = new(
            @"public (async )?Task<[^>]*(List<(Position|Balance|OpenOrder|TradeFill)>|OrderStatusSnapshot\??)>?[^(]*\b(GetPositionsAsync|GetBalancesAsync|GetOpenOrdersAsync|GetFillsAsync|GetOrderStatusAsync)\s*\(",
            RegexOptions.Compiled);

        [Fact]
        public void Trading_reads_have_no_catch_that_swallows()
        {
            // GeneralOrderService classifies a THROWN failure into ProviderResult
            // (Ok / NotSupported / NotPermitted / Unavailable / Failed), and the
            // reconciliation coordinator guards on IsOk. A provider-side
            // catch-and-return-empty routes around all of it: the read "succeeds"
            // with an empty account and the incident in ProviderResult.cs re-arms.
            // EMPTY BASELINE: any catch inside these methods must rethrow.
            var offenders = new List<string>();
            string root = Path.Combine(RepoRoot(), "Plugins", "Providers");

            foreach (string file in Directory.GetFiles(root, "*Provider.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!TradingReadSignature.IsMatch(lines[i]) || lines[i].Contains("=>")) continue;
                    int end = i + 1;
                    while (end < lines.Length && lines[end] != "        }") end++;

                    for (int j = i; j < end; j++)
                    {
                        // Anywhere in the line, not just line-start: the sabotage
                        // run proved `try { } catch { ... }` on one line walks
                        // around a StartsWith check.
                        var kw = Regex.Match(lines[j], @"\bcatch\b\s*(\(|\{|$)");
                        if (!kw.Success) continue;
                        // Collect the catch block by brace balance from the keyword on.
                        int depth = 0; bool opened = false;
                        var block = new System.Text.StringBuilder();
                        for (int k = j; k <= end; k++)
                        {
                            string text = k == j ? lines[k][kw.Index..] : lines[k];
                            block.Append(text).Append('\n');
                            depth += text.Count(c => c == '{') - text.Count(c => c == '}');
                            if (text.Contains('{')) opened = true;
                            if (opened && depth <= 0) break;
                        }
                        string body = block.ToString();
                        if (!body.Contains("throw"))
                            offenders.Add($"{Path.GetFileName(file)}:{j + 1} — catch in a trading read that never rethrows");
                    }
                    i = end;
                }
            }

            Assert.True(offenders.Count == 0,
                "Trading reads must let failures propagate (see ProviderResult.cs):\n"
                + string.Join("\n", offenders));
        }

        // ── The scan must actually see the methods it guards ─────────────────────

        [Fact]
        public void Swallower_scan_is_not_vacuous()
        {
            // A regex drift that stops matching any method would turn the guard
            // above green forever. Prove it still finds the read surface.
            int found = 0;
            string root = Path.Combine(RepoRoot(), "Plugins", "Providers");
            foreach (string file in Directory.GetFiles(root, "*Provider.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                found += File.ReadLines(file).Count(l => TradingReadSignature.IsMatch(l) && !l.Contains("=>"));
            }
            Assert.True(found >= 30,
                $"The swallower scan matched only {found} trading-read methods across all providers — "
                + "the signature regex has drifted and the guard is scanning air.");
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }
}
