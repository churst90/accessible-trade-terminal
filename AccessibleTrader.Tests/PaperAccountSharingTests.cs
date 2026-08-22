using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

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
        private readonly string _dir = Directory.CreateTempSubdirectory("att-hub-").FullName;
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
