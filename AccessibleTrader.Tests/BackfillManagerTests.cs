using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Persistence;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests the queue, persistence, error-handling and cancellation behaviours of
    /// <see cref="BackfillManager"/>.
    ///
    /// Each test spins up a unique temp SQLite file to prevent cross-test contamination.
    /// The file is deleted in the test fixture teardown.
    /// </summary>
    public class BackfillManagerTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly DbContextOptions<AppDbContext> _dbOptions;

        public BackfillManagerTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"backfill_test_{Guid.NewGuid():N}.db");
            _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;

            // Ensure the schema exists in the temp file before any test runs.
            using var ctx = new AppDbContext(_dbOptions);
            ctx.Database.EnsureCreated();
        }

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private BackfillManager Build(IDataService dataService, out SpyEventBus eventBus)
        {
            eventBus = new SpyEventBus();
            var factory = new StaticDbContextFactory(_dbOptions);
            return new BackfillManager(dataService, factory, eventBus, NullLogger<BackfillManager>.Instance);
        }

        // ── 1. Queue acceptance ───────────────────────────────────────────────

        /// <summary>
        /// QueueBackfill must accept a task without throwing — the queue is unbounded
        /// and the processing loop runs on a background thread.
        /// </summary>
        [Fact]
        public void QueueBackfill_DoesNotThrow()
        {
            using var mgr = Build(new SucceedingDataService(bars: new List<Ohlcv>()), out _);
            var ex = Record.Exception(() =>
                mgr.QueueBackfill("Crypto", "Binance", "BTC/USDT",
                    DateTime.UtcNow.AddDays(-1), DateTime.UtcNow));
            Assert.Null(ex);
        }

        // ── 2. Successful fetch persists bars ────────────────────────────────

        /// <summary>
        /// When the data service returns bars, they must be written to SQLite.
        /// The BackfillComplete ChartEvent must be published on the event bus.
        /// </summary>
        [Fact]
        public async Task QueueBackfill_WhenFetchSucceeds_PersistsBarsAndPublishesEvent()
        {
            var bars = new List<Ohlcv>
            {
                new Ohlcv { Date = new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), Open = 40000, High = 41000, Low = 39000, Close = 40500, Volume = 100 },
                new Ohlcv { Date = new DateTime(2024, 1, 1, 0, 1, 0, 0, DateTimeKind.Utc), Open = 40500, High = 41500, Low = 40000, Close = 41000, Volume = 120 },
            };

            using var mgr = Build(new SucceedingDataService(bars), out var eventBus);
            mgr.QueueBackfill("Crypto", "Binance", "BTC/USDT",
                new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2024, 1, 2, 0, 0, 0, 0, DateTimeKind.Utc));

            // Wait for both the DB rows AND the event to be published.
            // The two operations happen sequentially on the background thread (save → publish),
            // so we must wait for the event explicitly to avoid a timing race when tests run
            // in parallel and the scheduler preempts the worker between the two steps.
            await WaitForConditionAsync(() =>
            {
                using var ctx = new AppDbContext(_dbOptions);
                bool rowsReady = ctx.OhlcvData.CountAsync().Result >= 2;
                bool eventFired = eventBus.Log.Exists(e => e is ChartEvent ce && ce.Type == "BACKFILL_COMPLETE");
                return rowsReady && eventFired;
            }, timeoutMs: 5000);

            using var verify = new AppDbContext(_dbOptions);
            int saved = await verify.OhlcvData.CountAsync();
            Assert.Equal(2, saved);

            // BackfillComplete event must have been published.
            Assert.Contains(eventBus.Log, e => e is ChartEvent ce && ce.Type == "BACKFILL_COMPLETE");
        }

        // ── 3. Empty fetch writes nothing ────────────────────────────────────

        /// <summary>
        /// When the data service returns an empty list, no database rows are written
        /// and no BackfillComplete event is published (there is nothing to confirm).
        /// </summary>
        [Fact]
        public async Task QueueBackfill_WhenFetchReturnsEmpty_WritesNothingToDb()
        {
            using var mgr = Build(new SucceedingDataService(new List<Ohlcv>()), out var eventBus);
            mgr.QueueBackfill("Crypto", "Binance", "BTC/USDT",
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

            // Short wait to let the processor run at least once.
            await Task.Delay(1200);

            using var verify = new AppDbContext(_dbOptions);
            Assert.Equal(0, await verify.OhlcvData.CountAsync());
            Assert.DoesNotContain(eventBus.Log, e => e is ChartEvent { Type: "BACKFILL_COMPLETE" });
        }

        // ── 4. Fetch failure does not kill the processing loop ───────────────

        /// <summary>
        /// When the data service throws on the first task, the processing loop must
        /// swallow the exception and remain alive to process subsequent tasks.
        /// </summary>
        [Fact]
        public async Task QueueBackfill_WhenFetchThrows_ContinuesProcessingNextTask()
        {
            // First call throws; second call succeeds with one bar.
            var firstCallThrows = true;
            var successBars = new List<Ohlcv>
            {
                new Ohlcv { Date = new DateTime(2024, 2, 1, 0, 0, 0, 0, DateTimeKind.Utc), Open = 1, High = 2, Low = 0, Close = 1, Volume = 1 }
            };
            var service = new ConditionalDataService(() =>
            {
                if (firstCallThrows) { firstCallThrows = false; throw new InvalidOperationException("Simulated provider error"); }
                return successBars;
            });

            using var mgr = Build(service, out _);
            mgr.QueueBackfill("Crypto", "Binance", "BTC/USDT", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1));
            mgr.QueueBackfill("Crypto", "Binance", "BTC/USDT", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

            await WaitForConditionAsync(() =>
            {
                using var ctx = new AppDbContext(_dbOptions);
                return ctx.OhlcvData.CountAsync().Result >= 1;
            }, timeoutMs: 8000);

            using var verify = new AppDbContext(_dbOptions);
            // The second task must have succeeded even though the first one threw.
            Assert.Equal(1, await verify.OhlcvData.CountAsync());
        }

        // ── 5. Dispose cancels gracefully ────────────────────────────────────

        /// <summary>
        /// Dispose must cancel the background processing loop without throwing
        /// (OperationCanceledException must be caught internally).
        /// </summary>
        [Fact]
        public void Dispose_CancelsWithoutThrowing()
        {
            var mgr = Build(new SucceedingDataService(new List<Ohlcv>()), out _);
            var ex = Record.Exception(() => mgr.Dispose());
            Assert.Null(ex);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                await Task.Delay(100);
            }
        }

        // ── mock services ─────────────────────────────────────────────────────

        private sealed class SucceedingDataService : IDataService
        {
            private readonly List<Ohlcv> _bars;
            public SucceedingDataService(List<Ohlcv> bars) => _bars = bars;
            public Task<(List<Ohlcv>, List<(long, double)>)> FetchOhlcvAsync(string p, MarketDataRequest r)
                => Task.FromResult((_bars, new List<(long, double)>()));
            public Task InitializeAsync(IPluginLoaderService _) => Task.CompletedTask;
            public Task<List<string>> LoadAvailableMarketsAsync() => Task.FromResult(new List<string>());
            public Task<List<string>> LoadProvidersAsync() => Task.FromResult(new List<string>());
            public Task<List<string>> LoadProvidersByMarketTypeAsync(string _) => Task.FromResult(new List<string>());
            public Task<List<string>> GetSupportedSubTypesAsync(string a, string b) => Task.FromResult(new List<string>());
            public Task<List<string>> LoadSymbolsAsync(string a, string b) => Task.FromResult(new List<string>());
            public Task<List<string>> GetSupportedTimeframesAsync(string _) => Task.FromResult(new List<string>());
            public Task<bool> IsProviderConfiguredAsync(string _) => Task.FromResult(true);
            public Task<bool> ProviderRequiresApiKeyAsync(string _) => Task.FromResult(false);
            public Task<(List<OrderBookEntry>, List<OrderBookEntry>)> GetOrderBookAsync(string p, string s, int l = 10)
                => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
            public Task<List<MarketType>> GetSupportedMarketsForProviderAsync(string _) => Task.FromResult(new List<MarketType>());
            public Task<IMarketDataProvider?> GetProviderAsync(string _) => Task.FromResult<IMarketDataProvider?>(null);
            public Task<IProviderPlugin?> GetPluginAsync(string _) => Task.FromResult<IProviderPlugin?>(null);
        }

        private sealed class ConditionalDataService : IDataService
        {
            private readonly Func<List<Ohlcv>> _factory;
            public ConditionalDataService(Func<List<Ohlcv>> factory) => _factory = factory;
            public Task<(List<Ohlcv>, List<(long, double)>)> FetchOhlcvAsync(string p, MarketDataRequest r)
                => Task.FromResult((_factory(), new List<(long, double)>()));
            public Task InitializeAsync(IPluginLoaderService _) => Task.CompletedTask;
            public Task<List<string>> LoadAvailableMarketsAsync() => Task.FromResult(new List<string>());
            public Task<List<string>> LoadProvidersAsync() => Task.FromResult(new List<string>());
            public Task<List<string>> LoadProvidersByMarketTypeAsync(string _) => Task.FromResult(new List<string>());
            public Task<List<string>> GetSupportedSubTypesAsync(string a, string b) => Task.FromResult(new List<string>());
            public Task<List<string>> LoadSymbolsAsync(string a, string b) => Task.FromResult(new List<string>());
            public Task<List<string>> GetSupportedTimeframesAsync(string _) => Task.FromResult(new List<string>());
            public Task<bool> IsProviderConfiguredAsync(string _) => Task.FromResult(true);
            public Task<bool> ProviderRequiresApiKeyAsync(string _) => Task.FromResult(false);
            public Task<(List<OrderBookEntry>, List<OrderBookEntry>)> GetOrderBookAsync(string p, string s, int l = 10)
                => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
            public Task<List<MarketType>> GetSupportedMarketsForProviderAsync(string _) => Task.FromResult(new List<MarketType>());
            public Task<IMarketDataProvider?> GetProviderAsync(string _) => Task.FromResult<IMarketDataProvider?>(null);
            public Task<IProviderPlugin?> GetPluginAsync(string _) => Task.FromResult<IProviderPlugin?>(null);
        }

        private sealed class StaticDbContextFactory : IDbContextFactory<AppDbContext>
        {
            private readonly DbContextOptions<AppDbContext> _options;
            public StaticDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
            public AppDbContext CreateDbContext() => new AppDbContext(_options);
            public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
                => Task.FromResult(new AppDbContext(_options));
        }
    }
}
