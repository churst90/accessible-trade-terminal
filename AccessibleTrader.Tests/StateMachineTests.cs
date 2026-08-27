using System.Threading.Channels;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests
{
    public class StateMachineTests
    {
        [Fact]
        public async Task DataOrchestrator_Lifecycle_ShouldFollowDeterministicPath()
        {
            // Arrange
            var eventBus = new SpyEventBus();
            var logger = NullLogger<DataOrchestrator>.Instance;
            var fetcher = new MockHistoricalFetcher();
            var liveManager = new MockLiveStreamManager();
            
            using var orchestrator = new DataOrchestrator(fetcher, liveManager, eventBus, logger, new DemoPolicy(isDemo: false));

            // Assert: Initial State
            Assert.Equal(DataState.Initializing, orchestrator.CurrentState);

            // Act: Start Fetch
            var tcs = new TaskCompletionSource<List<Ohlcv>>();
            fetcher.FetchTask = tcs.Task;

            var fetchTask = orchestrator.FetchOhlcvAsync("Crypto", "Binance", "BTC/USDT", "1h");
            
            // Should be in HistoricalFilling while task is pending
            Assert.Equal(DataState.HistoricalFilling, orchestrator.CurrentState);
            
            tcs.SetResult(new List<Ohlcv>());
            await fetchTask;
            
            // Assert: After Fetch, it moves to GapFilling
            Assert.Equal(DataState.GapFilling, orchestrator.CurrentState);

            // Act: Start Live Stream
            await orchestrator.StartLiveStreamAsync("Crypto", "Binance", "BTC/USDT", "1h");
            
            // Assert: Live State
            Assert.Equal(DataState.LiveStreaming, orchestrator.CurrentState);

            // Act: Stop
            await orchestrator.StopLiveStreamAsync();
            
            // Assert: Reset to Initializing
            Assert.Equal(DataState.Initializing, orchestrator.CurrentState);
        }

        [Fact]
        public async Task DataOrchestrator_IllegalTriggers_ShouldBeIgnored()
        {
            // Arrange
            var eventBus = new SpyEventBus();
            var logger = NullLogger<DataOrchestrator>.Instance;
            var fetcher = new MockHistoricalFetcher();
            var liveManager = new MockLiveStreamManager();

            using var orchestrator = new DataOrchestrator(fetcher, liveManager, eventBus, logger, new DemoPolicy(isDemo: false));

            // Act: Fire TickReceived while Initializing (Illegal)

            Assert.Equal(DataState.Initializing, orchestrator.CurrentState);

            // Simulation of a tick from live stream manager
            liveManager.EmitTick(new Ohlcv());

            // The tick is drained by a background reader that fires TickReceived
            // BEFORE forwarding the tick into the orchestrator's own LiveStream —
            // so once the forwarded tick is readable, the state machine has
            // provably seen the trigger. Asserting without this sync point went
            // green merely because the reader hadn't been scheduled yet, and
            // would have gone green identically if the illegal trigger WERE
            // honoured.
            await orchestrator.LiveStream.ReadAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10));

            // Assert: State should still be Initializing
            Assert.Equal(DataState.Initializing, orchestrator.CurrentState);
        }
    }

    // Minimal mocks for testing
    public class MockHistoricalFetcher : HistoricalDataFetcher
    {
        public Task<List<Ohlcv>>? FetchTask { get; set; }

        public MockHistoricalFetcher() : base(null!, null!, null!, null!) { }
        public override Task<List<Ohlcv>> FetchOhlcvAsync(string market, string provider, string symbol, string timeframe, long? since = null, int? limit = null, long? until = null, CancellationToken ct = default)
        {
            return FetchTask ?? Task.FromResult(new List<Ohlcv>());
        }
    }

    public class MockLiveStreamManager : LiveStreamManager
    {
        private readonly Channel<LiveTick> _channel = Channel.CreateUnbounded<LiveTick>();
        public MockLiveStreamManager() : base(null!, null!, null!) { }
        public override ChannelReader<LiveTick> LiveStream => _channel.Reader;
        public override Task StartLiveStreamAsync(string market, string providerName, string symbol, string timeframe) => Task.CompletedTask;
        public override Task StopLiveStreamAsync() => Task.CompletedTask;
        public void EmitTick(Ohlcv tick)
            => _channel.Writer.TryWrite(new LiveTick(new ChartIdentity("Spot", "Test", "BTC/USD", "1h"), tick));
    }
}



