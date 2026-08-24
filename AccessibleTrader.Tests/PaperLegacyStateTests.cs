using System;
using System.IO;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Migration of paper_account.json files written before a feature was withdrawn.
    /// The live reporting account carried <c>Leverage: BTC/USD = 3.0</c> from before
    /// leverage was pulled (MaxLeverage is 1.0 now); recorded state must not go on
    /// describing — or reporting on positions — a feature that no longer exists.
    /// </summary>
    public sealed class PaperLegacyStateTests : IDisposable
    {
        private const string Btc = "BTC/USD";
        private readonly string _tempDir;

        public PaperLegacyStateTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "atc-legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private PaperTradingProvider Make(out MockWorkspaceStore store)
        {
            store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(_tempDir);
            return new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance);
        }

        [Fact]
        public async Task A_pre_withdrawal_leverage_entry_is_clamped_on_load_and_dropped_on_persist()
        {
            string path = Path.Combine(_tempDir, "paper_account.json");
            File.WriteAllText(path,
                @"{""Cash"":100000.0,
                   ""Positions"":[{""Symbol"":""BTC/USD"",""Qty"":1.0,""Avg"":60391.87}],
                   ""Leverage"":[{""Symbol"":""BTC/USD"",""Value"":3.0}]}");

            var paper = Make(out var store);

            // Loaded positions report 1x, not the withdrawn 3x.
            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(1.0, pos.Leverage, 6);

            // Any persisting mutation rewrites the file without the stale entry.
            store.EmitState(WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Test", Btc, "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, 60000, 60100, 59900, 60000, 1000)),
            });
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 0.001));

            var saved = JObject.Parse(File.ReadAllText(path));
            Assert.Empty(saved["Leverage"]!);
        }
    }
}
