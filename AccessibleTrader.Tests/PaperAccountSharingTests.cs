using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// One paper account per USER, not per browser tab.
    ///
    /// <para>
    /// <c>IPaperTradingProvider</c> was <c>AddScoped</c> on the WebHost, and a Blazor scope is a
    /// tab. Two tabs each built their own account object, each loaded <c>paper_account.json</c>
    /// when it happened to be created, and each wrote the whole file back on every change with no
    /// re-read and no file watch. Last writer won, so a trade made in one tab could be erased by a
    /// trailing-stop update in another — silently, which is the worst way for money state to fail.
    /// </para>
    /// </summary>
    public sealed class PaperAccountSharingTests : IDisposable
    {
        private readonly string _dir = TestTemp.NewDir("att-hub-");
        private readonly PaperAccountHub _hub = new();

        public void Dispose()
        {
            _hub.Dispose();
            try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
        }

        [Fact]
        public async Task TwoTabsOfOneUser_ShareOneAccount()
        {
            var tabA = Account("user-1", out var storeA);
            var tabB = Account("user-1", out _);

            Assert.Same(tabA, tabB);

            storeA.EmitState(ChartOf("Venue", "BTCUSDT", 60_000));
            await tabA.PlaceOrderAsync(Buy("BTCUSDT", 0.1));

            // The second tab sees it immediately — same object, not a second copy of the file.
            var seen = await tabB.GetPositionsAsync();
            Assert.Contains(seen, p => p.Symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase)
                                    && Math.Abs(p.Quantity - 0.1) < 1e-9);
        }

        [Fact]
        public void TwoUsers_NeverShare()
        {
            // The other half of "one account per user": separate people must not meet.
            Assert.NotSame(Account("user-1", out _), Account("user-2", out _));
        }

        [Fact]
        public async Task ClosingOneTab_DoesNotDisposeTheAccountTheOtherIsUsing()
        {
            // The DI container disposes whatever a scoped factory hands it. Obeying that would tear
            // down a shared account when any ONE tab closed, leaving the others trading on a dead
            // object — worse than the bug sharing exists to fix.
            var account = Account("user-1", out var storeA);
            var attachment = account.Attach(new MockWorkspaceStore());

            attachment.Dispose();       // tab B goes away
            account.Dispose();          // …and its scope disposes what the factory returned

            storeA.EmitState(ChartOf("Venue", "BTCUSDT", 60_000));
            string result = await account.PlaceOrderAsync(Buy("BTCUSDT", 0.1));

            Assert.DoesNotContain("ORDER_FAILED", result);
        }

        [Fact]
        public async Task AnOrderFillsFromWhicheverTabIsLive()
        {
            // A resting order must keep being evaluated when another tab takes focus. The account
            // watches every attached chart, not only the one that placed the order.
            var account = Account("user-1", out var storeA);
            var storeB = new MockWorkspaceStore();
            account.Attach(storeB);

            storeA.EmitState(ChartOf("Venue", "BTCUSDT", 60_000));
            await account.PlaceOrderAsync(Buy("BTCUSDT", 0.1));

            // Tab A goes quiet; tab B is the one now carrying prices.
            storeB.EmitState(ChartOf("Venue", "BTCUSDT", 70_000));

            var pos = (await account.GetPositionsAsync())
                .Single(p => p.Symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase));
            Assert.True(pos.UnrealizedPnL > 0,
                "the account priced the position from the tab that is live, so P&L should have moved");
        }

        /// <summary>
        /// Two tabs opening at the same instant get ONE account.
        ///
        /// <para>
        /// <c>ConcurrentDictionary.GetOrAdd</c> does not serialise its factory, so both
        /// ran and the loser was thrown away WITHOUT <c>DisposeAccount()</c> — while its
        /// constructor had already attached to a store and subscribed to
        /// <c>MonitoredBarEvent</c>. It went on processing bars and writing the whole of
        /// <c>paper_account.json</c> on every change, which is exactly the last-writer-wins
        /// corruption this hub exists to prevent, reproduced by the fix for it.
        /// </para>
        /// </summary>
        [Fact]
        public void ConcurrentFirstUse_BuildsExactlyOneAccount()
        {
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(Path.Combine(_dir, "racer"));
            Directory.CreateDirectory(Path.Combine(_dir, "racer"));

            int built = 0;
            var built_accounts = new System.Collections.Concurrent.ConcurrentBag<PaperTradingProvider>();
            using var gate = new System.Threading.Barrier(8);

            var results = new PaperTradingProvider[8];
            System.Threading.Tasks.Parallel.For(0, 8, i =>
            {
                gate.SignalAndWait();
                results[i] = _hub.ForUser("racer", () =>
                {
                    System.Threading.Interlocked.Increment(ref built);
                    var a = new PaperTradingProvider(new MockWorkspaceStore(), paths,
                                                     NullLogger<PaperTradingProvider>.Instance);
                    built_accounts.Add(a);
                    return a;
                });
            });

            Assert.Equal(1, built);
            Assert.Single(built_accounts);
            Assert.All(results, r => Assert.Same(results[0], r));
        }

        /// <summary>
        /// The tab that CREATED the account can leave like any other.
        ///
        /// <para>
        /// It used to be handed a no-op token, so nothing ever detached: the dead store
        /// kept its live subscription and the account went on resolving prices and chart
        /// identities against a workspace nobody was looking at.
        /// </para>
        /// </summary>
        [Fact]
        public async Task TheCreatingTab_ActuallyDetachesWhenItCloses()
        {
            var account = Account("user-1", out var storeA);
            var creating = account.TakePrimaryAttachment();

            storeA.EmitState(ChartOf("Venue", "BTCUSDT", 60_000));
            await account.PlaceOrderAsync(Buy("BTCUSDT", 0.1));

            creating.Dispose();   // the first tab closes

            // Its store is no longer watched, so its prices stop reaching the account.
            storeA.EmitState(ChartOf("Venue", "BTCUSDT", 90_000));

            var pos = (await account.GetPositionsAsync())
                .Single(p => p.Symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, pos.UnrealizedPnL, 6);
        }

        /// <summary>
        /// Claimable once. A second claimant must get a no-op rather than a token that
        /// detaches a subscription somebody else owns.
        /// </summary>
        [Fact]
        public async Task ThePrimaryAttachment_IsHandedOverExactlyOnce()
        {
            var account = Account("user-1", out var storeA);
            account.TakePrimaryAttachment();

            account.TakePrimaryAttachment().Dispose();   // a second claimant

            storeA.EmitState(ChartOf("Venue", "BTCUSDT", 60_000));
            string result = await account.PlaceOrderAsync(Buy("BTCUSDT", 0.1));

            Assert.DoesNotContain("ORDER_FAILED", result);
        }

        /// <summary>
        /// Each tab brings its own event bus, and takes it away again.
        ///
        /// <para>
        /// <c>IEventBus</c> is <c>AddScoped</c> on the WebHost — a scope is a tab — so an
        /// account that kept only its creator's bus never heard a background monitor in
        /// any other tab, which is the exact case background fills exist for.
        /// </para>
        /// </summary>
        [Fact]
        public async Task AnAttachedTabsBus_IsHeardAndThenReleased()
        {
            var account = Account("user-1", out var storeA);
            var busB = new SpyEventBus();
            var attachment = account.Attach(new MockWorkspaceStore(), busB);

            storeA.EmitState(ChartOf("Venue", "BTCUSDT", 60_000));
            await account.PlaceOrderAsync(Buy("BTCUSDT", 0.1));
            storeA.EmitState(ChartOf("Venue", "ETHUSDT", 3_000));   // tab A navigates away

            // Tab B's monitor is now the only thing pricing the BTC position.
            busB.Publish(new MonitoredBarEvent(new ChartIdentity("Spot", "Venue", "BTCUSDT", "1h"),
                                               new Ohlcv(DateTime.UtcNow, 70_000, 70_000, 70_000, 70_000, 1)));
            var moved = (await account.GetPositionsAsync())
                .Single(p => p.Symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase));
            Assert.True(moved.UnrealizedPnL > 0);

            attachment.Dispose();

            // …and stops being heard once that tab is gone.
            busB.Publish(new MonitoredBarEvent(new ChartIdentity("Spot", "Venue", "BTCUSDT", "1h"),
                                               new Ohlcv(DateTime.UtcNow, 90_000, 90_000, 90_000, 90_000, 1)));
            var after = (await account.GetPositionsAsync())
                .Single(p => p.Symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(moved.UnrealizedPnL, after.UnrealizedPnL, 6);
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        private PaperTradingProvider Account(string userKey, out MockWorkspaceStore store)
        {
            var s = new MockWorkspaceStore();
            store = s;
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(Path.Combine(_dir, userKey));
            Directory.CreateDirectory(Path.Combine(_dir, userKey));

            return _hub.ForUser(userKey, () => new PaperTradingProvider(
                s, paths, NullLogger<PaperTradingProvider>.Instance));
        }

        private static WorkspaceState ChartOf(string provider, string symbol, double price) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", provider, symbol, "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, price, price, price, price, 1)),
            };

        private static TradeSignal Buy(string symbol, double qty) =>
            new(Symbol: symbol, Side: OrderSide.Buy, Quantity: qty, Type: OrderType.Market);
    }
}
