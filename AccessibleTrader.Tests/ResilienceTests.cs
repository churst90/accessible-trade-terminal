using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests the resilience and error-handling behaviour of <see cref="DataOrchestrator"/>.
    ///
    /// Coverage targets from the 2026-03-28 architectural assessment:
    ///   1. General network failure — any non-retriable exception escaping the resilience
    ///      policy causes the orchestrator to return an empty list and transition to Faulted.
    ///   2. Open-circuit (BrokenCircuit path) — a BrokenCircuitException (thrown by an
    ///      already-open Polly circuit in production) is handled without re-throwing; the
    ///      orchestrator returns an empty list and fires the ErrorOccurred trigger.
    ///   3. Cancellation safety — an OperationCanceledException from the fetcher (e.g., the
    ///      request was cancelled mid-flight) is handled cleanly; the orchestrator returns
    ///      an empty list without leaving zombie tasks or unobserved exceptions.
    /// </summary>
    public class ResilienceTests
    {
        // ── helpers ──────────────────────────────────────────────────────────

        private static DataOrchestrator BuildOrchestrator(MockHistoricalFetcher fetcher, out SpyEventBus eventBus)
        {
            eventBus = new SpyEventBus();
            var logger = NullLogger<DataOrchestrator>.Instance;
            return new DataOrchestrator(fetcher, new MockLiveStreamManager(), eventBus, logger);
        }

        // ── 1. General network failure ────────────────────────────────────────

        /// <summary>
        /// When a non-retriable exception escapes the resilience policy (e.g. an
        /// unrecognised server error), FetchOhlcvAsync must return an empty list
        /// and transition the orchestrator to the Faulted state.
        /// <para>
        /// Using InvalidOperationException because: (a) the retry policy only handles
        /// HttpRequestException, so this reaches the outer catch immediately with no
        /// artificial sleep delay; (b) it represents the realistic scenario of an
        /// unexpected exception that bypasses all configured Polly handlers.
        /// </para>
        /// </summary>
        [Fact]
        public async Task FetchOhlcv_WhenNonRetriableExceptionThrown_ShouldReturnEmptyAndFault()
        {
            // Arrange
            var fetcher = new MockHistoricalFetcher();
            fetcher.FetchTask = Task.FromException<List<Ohlcv>>(
                new InvalidOperationException("Simulated unexpected server failure"));

            using var orchestrator = BuildOrchestrator(fetcher, out _);

            // Act
            var result = await orchestrator.FetchOhlcvAsync("Crypto", "Binance", "BTC/USDT", "1h");

            // Assert
            Assert.Empty(result);
            Assert.Equal(DataState.Faulted, orchestrator.CurrentState);
        }

        /// <summary>
        /// When the fetcher throws HttpRequestException (e.g. a 5xx or connection
        /// refused), the retry policy makes one additional attempt before giving up.
        /// The orchestrator must still return an empty list and reach Faulted — it
        /// must NOT propagate the exception to the caller.
        /// <para>
        /// Note: the retry policy introduces a 1-second sleep on the single retry
        /// attempt, so this test takes ~1 s by design.
        /// </para>
        /// </summary>
        [Fact]
        public async Task FetchOhlcv_WhenHttpExceptionThrown_ShouldReturnEmptyAndFaultAfterRetry()
        {
            // Arrange
            var fetcher = new MockHistoricalFetcher();
            fetcher.FetchTask = Task.FromException<List<Ohlcv>>(
                new HttpRequestException("503 Service Unavailable"));

            using var orchestrator = BuildOrchestrator(fetcher, out _);

            // Act — the retry policy delays 1 s between attempts; this is expected
            var result = await orchestrator.FetchOhlcvAsync("Crypto", "Binance", "BTC/USDT", "1h");

            // Assert
            Assert.Empty(result);
            Assert.Equal(DataState.Faulted, orchestrator.CurrentState);
        }

        // ── 2. Open-circuit (BrokenCircuit path) ─────────────────────────────

        /// <summary>
        /// When Polly's circuit is already open it throws BrokenCircuitException
        /// instead of executing the delegate.  DataOrchestrator has a dedicated catch
        /// for this type; it must return an empty list and transition to Faulted without
        /// logging a full error (it logs only a warning to keep noise low).
        /// </summary>
        [Fact]
        public async Task FetchOhlcv_WhenCircuitAlreadyOpen_ShouldReturnEmptyAndFaultQuickly()
        {
            // Arrange: simulate the state after the circuit has tripped by throwing
            // BrokenCircuitException directly from the fetcher.  In production, Polly
            // itself throws this; here we bypass the circuit's counter for test speed.
            var fetcher = new MockHistoricalFetcher();
            fetcher.FetchTask = Task.FromException<List<Ohlcv>>(
                new Polly.CircuitBreaker.BrokenCircuitException("Circuit is open (test)"));

            using var orchestrator = BuildOrchestrator(fetcher, out _);

            // Act
            var result = await orchestrator.FetchOhlcvAsync("Crypto", "Binance", "BTC/USDT", "1h");

            // Assert: must be empty, must fault, must NOT throw
            Assert.Empty(result);
            Assert.Equal(DataState.Faulted, orchestrator.CurrentState);
        }

        // ── 3. Cancellation safety ────────────────────────────────────────────

        /// <summary>
        /// When the underlying fetch task is cancelled (OperationCanceledException),
        /// FetchOhlcvAsync must handle it without leaking the exception and without
        /// leaving the orchestrator in an undefined state.
        /// </summary>
        [Fact]
        public async Task FetchOhlcv_WhenCancelled_ShouldReturnEmptyCleanly()
        {
            // Arrange
            var fetcher = new MockHistoricalFetcher();
            fetcher.FetchTask = Task.FromException<List<Ohlcv>>(
                new OperationCanceledException("Fetch was cancelled"));

            using var orchestrator = BuildOrchestrator(fetcher, out _);

            // Act — should NOT throw
            var result = await orchestrator.FetchOhlcvAsync("Crypto", "Binance", "BTC/USDT", "1h");

            // Assert: empty list returned, orchestrator in a defined terminal state
            Assert.Empty(result);
            // Cancellation is treated as an error (fires ErrorOccurred trigger → Faulted)
            Assert.Equal(DataState.Faulted, orchestrator.CurrentState);
        }

        // ── 4. ErrorOccurred state change propagates to event bus ────────────

        /// <summary>
        /// When a network error occurs, a FeedbackRequestEvent of type Error must be
        /// published on the event bus so the accessibility layer can announce the failure.
        /// </summary>
        [Fact]
        public async Task FetchOhlcv_OnError_ShouldPublishFeedbackRequestEvent()
        {
            // Arrange
            var fetcher = new MockHistoricalFetcher();
            fetcher.FetchTask = Task.FromException<List<Ohlcv>>(
                new InvalidOperationException("Simulated failure"));

            using var orchestrator = BuildOrchestrator(fetcher, out var eventBus);

            // Act
            await orchestrator.FetchOhlcvAsync("Crypto", "Binance", "BTC/USDT", "1h");

            // Assert: the event bus received at least one FeedbackRequestEvent with Error type
            var feedbackEvents = eventBus.Log.OfType<FeedbackRequestEvent>().ToList();
            Assert.NotEmpty(feedbackEvents);
            Assert.Contains(feedbackEvents, e => e.Type == FeedbackType.Error);
        }

        // ── 5. Silent fetch does not publish events or change state ──────────

        /// <summary>
        /// When <c>silent: true</c> is passed (used for background backfill fetches),
        /// a failure must NOT publish FeedbackRequestEvents and must NOT fire state
        /// transitions — the caller manages state for these fetches itself.
        /// </summary>
        [Fact]
        public async Task FetchOhlcv_WhenSilentAndFails_ShouldNotPublishEventsOrChangeState()
        {
            // Arrange
            var fetcher = new MockHistoricalFetcher();
            fetcher.FetchTask = Task.FromException<List<Ohlcv>>(
                new InvalidOperationException("Simulated failure"));

            using var orchestrator = BuildOrchestrator(fetcher, out var eventBus);

            // Act
            var result = await orchestrator.FetchOhlcvAsync(
                "Crypto", "Binance", "BTC/USDT", "1h", silent: true);

            // Assert: empty result, no feedback events, state unchanged
            Assert.Empty(result);
            Assert.DoesNotContain(eventBus.Log, e => e is FeedbackRequestEvent { Type: FeedbackType.Error });
            Assert.Equal(DataState.Initializing, orchestrator.CurrentState);
        }
    }
}
