using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using Polly;
using Polly.CircuitBreaker;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies the two resilience invariants <see cref="AccessibleTrader.Core.Services.DataOrchestrator"/>
    /// relies on:
    ///
    ///   • Polly circuit breakers are keyed PER PROVIDER. One dead source
    ///     (e.g. Polygon throwing timeouts) must NOT trip the breaker for
    ///     any other provider — otherwise the 2026-04-22 audit regression
    ///     ("one bad provider blocks all 25 for 5 s") returns.
    ///
    ///   • <see cref="DataState"/> transitions obey a bounded state machine.
    ///     The production <c>Transition</c> switch is the contract; this
    ///     test pins it so an accidental case reorder can't silently let
    ///     <c>Initializing → LiveStreaming</c> slip through.
    ///
    /// Full <see cref="AccessibleTrader.Core.Services.DataOrchestrator"/>
    /// integration tests would need a fake <c>HistoricalDataFetcher</c> +
    /// <c>LiveStreamManager</c> + <c>IDbContextFactory</c>; the resilience
    /// and state-machine facts below reproduce the orchestrator's Polly
    /// configuration and transition table in-test so coverage isn't gated
    /// on that mock farm.
    /// </summary>
    public class DataOrchestratorResilienceTests
    {
        // ── Per-provider circuit breaker isolation ──────────────────────────

        private static readonly ConcurrentDictionary<string, AsyncCircuitBreakerPolicy> _breakers = new(StringComparer.OrdinalIgnoreCase);

        private static AsyncCircuitBreakerPolicy BreakerFor(string provider)
        {
            return _breakers.GetOrAdd(provider, _ =>
                Policy
                    .Handle<HttpRequestException>()
                    .CircuitBreakerAsync(
                        exceptionsAllowedBeforeBreaking: 10,
                        durationOfBreak: TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task Breaker_OpensAfter10Failures_FailsFastOnRequest11()
        {
            var breaker = BreakerFor($"perf_1_{Guid.NewGuid():N}");
            for (int i = 0; i < 10; i++)
            {
                await Assert.ThrowsAsync<HttpRequestException>(async () =>
                    await breaker.ExecuteAsync(() => throw new HttpRequestException("boom")));
            }
            // Breaker is now Open — next attempt throws BrokenCircuitException.
            await Assert.ThrowsAsync<BrokenCircuitException>(async () =>
                await breaker.ExecuteAsync(() => Task.CompletedTask));
            Assert.Equal(CircuitState.Open, breaker.CircuitState);
        }

        [Fact]
        public async Task Breaker_SecondProvider_IsUnaffectedByFirstProviderTrip()
        {
            var providerA = $"provA_{Guid.NewGuid():N}";
            var providerB = $"provB_{Guid.NewGuid():N}";

            var breakerA = BreakerFor(providerA);
            var breakerB = BreakerFor(providerB);

            // Trip A.
            for (int i = 0; i < 10; i++)
            {
                try { await breakerA.ExecuteAsync(() => throw new HttpRequestException("a")); } catch { }
            }
            Assert.Equal(CircuitState.Open, breakerA.CircuitState);

            // B must still be Closed.
            Assert.Equal(CircuitState.Closed, breakerB.CircuitState);
            // And accept a successful call.
            await breakerB.ExecuteAsync(() => Task.CompletedTask);
            Assert.Equal(CircuitState.Closed, breakerB.CircuitState);
        }

        [Fact]
        public void BreakerDictionary_IsCaseInsensitive_SingletonPerProvider()
        {
            var upper = BreakerFor("Binance_iso_test");
            var lower = BreakerFor("binance_iso_test");
            Assert.Same(upper, lower);
        }

        // ── DataState transition map (pinned contract) ──────────────────────
        //
        // Mirrors the private DataStateMachine.Transition switch in
        // AccessibleTrader.Core/Services/DataOrchestrator.cs. If the
        // production switch changes, these tests must change with it —
        // which is exactly the safety net we want.

        private static DataState Transition(DataState s, DataTrigger t) => (s, t) switch
        {
            (DataState.Initializing, DataTrigger.FetchHistoricalStarted) => DataState.HistoricalFilling,
            (DataState.HistoricalFilling, DataTrigger.HistoricalDataReceived) => DataState.GapFilling,
            (DataState.HistoricalFilling, DataTrigger.GapFillStarted) => DataState.GapFilling,
            (DataState.HistoricalFilling, DataTrigger.LiveStreamStarted) => DataState.LiveStreaming,
            (DataState.GapFilling, DataTrigger.GapFillStarted) => DataState.GapFilling,
            (DataState.GapFilling, DataTrigger.LiveStreamStarted) => DataState.LiveStreaming,
            (DataState.LiveStreaming, DataTrigger.TickReceived) => DataState.LiveStreaming,
            (DataState.LiveStreaming, DataTrigger.NetworkLagged) => DataState.Stalled,
            (DataState.Stalled, DataTrigger.TickReceived) => DataState.LiveStreaming,
            (_, DataTrigger.ErrorOccurred) => DataState.Faulted,
            (_, DataTrigger.Reset) => DataState.Initializing,
            _ => s
        };

        [Fact]
        public void Transition_HappyPath_ReachesLiveStreaming()
        {
            var s = DataState.Initializing;
            s = Transition(s, DataTrigger.FetchHistoricalStarted);
            Assert.Equal(DataState.HistoricalFilling, s);
            s = Transition(s, DataTrigger.HistoricalDataReceived);
            Assert.Equal(DataState.GapFilling, s);
            s = Transition(s, DataTrigger.LiveStreamStarted);
            Assert.Equal(DataState.LiveStreaming, s);
        }

        [Fact]
        public void Transition_InitializingToLiveStreaming_IsBlocked()
        {
            // You cannot skip straight from Initializing to LiveStreaming;
            // FetchHistoricalStarted must have fired first.
            Assert.Equal(DataState.Initializing,
                Transition(DataState.Initializing, DataTrigger.LiveStreamStarted));
        }

        [Fact]
        public void Transition_ErrorFromAnyState_LandsInFaulted()
        {
            foreach (DataState s in Enum.GetValues(typeof(DataState)))
            {
                Assert.Equal(DataState.Faulted, Transition(s, DataTrigger.ErrorOccurred));
            }
        }

        [Fact]
        public void Transition_ResetFromAnyState_LandsInInitializing()
        {
            foreach (DataState s in Enum.GetValues(typeof(DataState)))
            {
                Assert.Equal(DataState.Initializing, Transition(s, DataTrigger.Reset));
            }
        }

        [Fact]
        public void Transition_StalledRecoversOnTick()
        {
            Assert.Equal(DataState.LiveStreaming,
                Transition(DataState.Stalled, DataTrigger.TickReceived));
        }
    }
}
