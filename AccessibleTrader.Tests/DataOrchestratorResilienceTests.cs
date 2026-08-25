using System.Collections.Concurrent;
using System.Net;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies the two resilience invariants <see cref="DataOrchestrator"/> relies on:
    ///
    ///   • Polly circuit breakers are keyed PER PROVIDER. One dead source (e.g. Polygon
    ///     throwing timeouts) must NOT trip the breaker for any other provider — otherwise
    ///     the 2026-04-22 audit regression ("one bad provider blocks all 25 for 5 s") returns.
    ///
    ///   • <see cref="DataState"/> transitions obey a bounded state machine, so an accidental
    ///     case reorder cannot silently let <c>Initializing → LiveStreaming</c> slip through.
    ///
    /// <para>
    /// **This file used to test a copy of the orchestrator rather than the orchestrator.** It
    /// declared its own <c>ConcurrentDictionary</c> of breakers and its own <c>Transition</c>
    /// switch, and asserted against those; <c>DataOrchestrator</c> was never constructed. So the
    /// breaker tests proved that Polly keys a dictionary — which Polly does — and the named
    /// regression this file exists for would NOT have been caught if someone had refactored the
    /// orchestrator to a single shared breaker. The comment even said "if the production switch
    /// changes, these tests must change with it", and if the production switch had changed,
    /// nothing here would have moved.
    /// </para>
    /// <para>
    /// Everything below now goes through the real orchestrator's public surface. The mock farm
    /// the old comment said this was gated on already existed in <see cref="StateMachineTests"/>.
    /// </para>
    /// </summary>
    public class DataOrchestratorResilienceTests
    {
        // ── Harness ─────────────────────────────────────────────────────────────

        /// <summary>A fetcher whose behaviour can be swapped mid-test and which records who
        /// asked. The record is what proves fail-fast: an open breaker means the orchestrator
        /// never reaches the fetcher at all.</summary>
        private sealed class ScriptedFetcher : HistoricalDataFetcher
        {
            public Func<string, Task<List<Ohlcv>>> Next = _ => Task.FromResult(new List<Ohlcv>());
            public readonly ConcurrentQueue<string> Calls = new();

            public ScriptedFetcher() : base(null!, null!, null!, null!, null!) { }

            public override Task<List<Ohlcv>> FetchOhlcvAsync(
                string market, string provider, string symbol, string timeframe,
                long? since = null, int? limit = null, long? until = null)
            {
                Calls.Enqueue(provider);
                return Next(provider);
            }

            public int CallsFor(string provider) =>
                Calls.Count(c => string.Equals(c, provider, StringComparison.OrdinalIgnoreCase));
        }

        private static DataOrchestrator Build(ScriptedFetcher fetcher, out SpyEventBus bus)
        {
            bus = new SpyEventBus();
            return new DataOrchestrator(fetcher, new MockLiveStreamManager(), bus,
                NullLogger<DataOrchestrator>.Instance, new DemoPolicy(isDemo: false));
        }

        private static Task<List<Ohlcv>> Fetch(DataOrchestrator o, string provider) =>
            o.FetchOhlcvAsync("Crypto", provider, "BTC/USD", "1h");

        private static List<Ohlcv> OneBar() =>
            new() { new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, 2, 0.5, 1.5, 10) };

        // ── Per-provider circuit breaker isolation ──────────────────────────────

        /// <summary>
        /// The named regression, driven end to end: trip one provider's transport breaker and
        /// show that (a) that provider then fails fast without touching the network, (b) the
        /// key is case-insensitive so the same provider under different casing is still shut,
        /// and (c) a DIFFERENT provider is served normally.
        ///
        /// <para>
        /// Takes about five seconds by design. The retry policy sleeps one second between its
        /// two attempts, each attempt counts once against the breaker, and the breaker opens on
        /// the tenth — so five failing fetches is the cheapest honest way to reach an open
        /// circuit through the real policy stack. Faking it by throwing
        /// <c>BrokenCircuitException</c> from the fetcher is what
        /// <see cref="ResilienceTests"/> does, and that tests the catch block, not the keying.
        /// </para>
        /// </summary>
        [Fact]
        public async Task A_tripped_provider_fails_fast_and_leaves_every_other_provider_alone()
        {
            var fetcher = new ScriptedFetcher();
            using var o = Build(fetcher, out _);

            // Only Polygon is broken; anyone else gets a good bar.
            fetcher.Next = provider => string.Equals(provider, "Polygon", StringComparison.OrdinalIgnoreCase)
                ? Task.FromException<List<Ohlcv>>(new HttpRequestException("connection reset"))
                : Task.FromResult(OneBar());

            // 5 fetches × (1 attempt + 1 retry) = 10 counted transport failures → breaker opens.
            for (int i = 0; i < 5; i++)
                Assert.Empty(await Fetch(o, "Polygon"));

            int callsBeforeFailFast = fetcher.CallsFor("Polygon");
            Assert.Equal(10, callsBeforeFailFast);

            // (a) Polygon now fails fast: the fetcher is not reached at all.
            Assert.Empty(await Fetch(o, "Polygon"));
            Assert.Equal(callsBeforeFailFast, fetcher.CallsFor("Polygon"));

            // (b) The dictionary is case-insensitive, so "polygon" is the same circuit and not
            //     a second, fresh one that would let the dead provider straight back in.
            Assert.Empty(await Fetch(o, "polygon"));
            Assert.Equal(callsBeforeFailFast, fetcher.CallsFor("Polygon"));

            // (c) …and Binance is untouched. This is the whole point of the per-provider key.
            var binance = await Fetch(o, "Binance");
            Assert.Single(binance);
            Assert.Equal(1, fetcher.CallsFor("Binance"));
        }

        /// <summary>
        /// The rate-limit breaker is a second, twitchier circuit living in the same per-provider
        /// entry (2 failures, 30 s open). It is not handled by the retry, so this reaches an open
        /// circuit in two fetches with no sleeping — and it independently proves the keying.
        /// </summary>
        [Fact]
        public async Task The_rate_limit_breaker_is_also_per_provider()
        {
            var fetcher = new ScriptedFetcher();
            using var o = Build(fetcher, out _);

            fetcher.Next = provider => string.Equals(provider, "Mexc", StringComparison.OrdinalIgnoreCase)
                // Deliberately NOT a transport failure: this must reach the rate-limit breaker
                // without the retry policy handling (and therefore doubling) it.
                ? Task.FromException<List<Ohlcv>>(new InvalidOperationException("429 Too Many Requests"))
                : Task.FromResult(OneBar());

            Assert.Empty(await Fetch(o, "Mexc"));
            Assert.Empty(await Fetch(o, "Mexc"));
            Assert.Equal(2, fetcher.CallsFor("Mexc"));

            // Open now — third attempt never reaches the fetcher.
            Assert.Empty(await Fetch(o, "Mexc"));
            Assert.Equal(2, fetcher.CallsFor("Mexc"));

            // Another provider is unaffected.
            Assert.Single(await Fetch(o, "Kraken"));
        }

        /// <summary>
        /// A 4xx that is not 408/429 is the provider's problem, not the network's: retrying
        /// cannot help and counting it toward a breaker labelled "network issue" would suspend a
        /// provider that is working. Ten of them must leave the circuit closed.
        /// </summary>
        [Fact]
        public async Task A_permanent_4xx_never_trips_the_transport_breaker()
        {
            var fetcher = new ScriptedFetcher();
            using var o = Build(fetcher, out _);

            fetcher.Next = provider => string.Equals(provider, "Tradier", StringComparison.OrdinalIgnoreCase)
                ? Task.FromException<List<Ohlcv>>(new HttpRequestException("not found", null, HttpStatusCode.NotFound))
                : Task.FromResult(OneBar());

            for (int i = 0; i < 12; i++)
                Assert.Empty(await Fetch(o, "Tradier"));

            // No retry (not transient) and no breaker count: one fetcher call each, every time.
            Assert.Equal(12, fetcher.CallsFor("Tradier"));

            // And the circuit is still closed — the 13th call reaches the fetcher too.
            fetcher.Next = _ => Task.FromResult(OneBar());
            Assert.Single(await Fetch(o, "Tradier"));
            Assert.Equal(13, fetcher.CallsFor("Tradier"));
        }

        // ── DataState transitions, driven through the orchestrator ──────────────

        /// <summary>
        /// Puts a real orchestrator into <paramref name="target"/> using only its public
        /// surface, and asserts it got there. The transition table is private, so the states
        /// this can reach ARE the states the app can reach.
        /// </summary>
        private static async Task<(DataOrchestrator O, ScriptedFetcher F)> InState(DataState target)
        {
            var f = new ScriptedFetcher();
            var o = Build(f, out _);

            switch (target)
            {
                case DataState.Initializing:
                    break;

                case DataState.HistoricalFilling:
                    // FetchHistoricalStarted fires synchronously, before the first await, so the
                    // orchestrator is parked mid-fetch while this task stays pending.
                    var pending = new TaskCompletionSource<List<Ohlcv>>();
                    f.Next = _ => pending.Task;
                    _ = Fetch(o, "Binance");
                    break;

                case DataState.GapFilling:
                    f.Next = _ => Task.FromResult(OneBar());
                    await Fetch(o, "Binance");
                    break;

                case DataState.LiveStreaming:
                    f.Next = _ => Task.FromResult(OneBar());
                    await Fetch(o, "Binance");
                    await o.StartLiveStreamAsync("Crypto", "Binance", "BTC/USD", "1h");
                    break;

                case DataState.Faulted:
                    f.Next = _ => Task.FromException<List<Ohlcv>>(new InvalidOperationException("boom"));
                    await Fetch(o, "Binance");
                    break;

                default:
                    throw new InvalidOperationException(
                        $"{target} is not reachable through the orchestrator's public surface — see "
                      + nameof(Stalled_is_unreachable_because_nothing_fires_NetworkLagged));
            }

            Assert.Equal(target, o.CurrentState);
            return (o, f);
        }

        /// <summary>Every state <see cref="InState"/> claims to reach, so the sweeps below cannot
        /// pass by iterating a list that quietly lost an entry.</summary>
        public static readonly DataState[] ReachableStates =
        {
            DataState.Initializing,
            DataState.HistoricalFilling,
            DataState.GapFilling,
            DataState.LiveStreaming,
            DataState.Faulted,
        };

        public static IEnumerable<object[]> ReachableStateRows() =>
            ReachableStates.Select(s => new object[] { s });

        [Theory]
        [MemberData(nameof(ReachableStateRows))]
        public async Task Every_reachable_state_is_actually_reachable(DataState target)
        {
            var (o, _) = await InState(target);
            using (o) Assert.Equal(target, o.CurrentState);
        }

        [Fact]
        public async Task Lifecycle_reaches_LiveStreaming_only_through_the_historical_path()
        {
            var f = new ScriptedFetcher { Next = _ => Task.FromResult(OneBar()) };
            using var o = Build(f, out _);

            Assert.Equal(DataState.Initializing, o.CurrentState);
            await Fetch(o, "Binance");
            Assert.Equal(DataState.GapFilling, o.CurrentState);
            await o.StartLiveStreamAsync("Crypto", "Binance", "BTC/USD", "1h");
            Assert.Equal(DataState.LiveStreaming, o.CurrentState);
        }

        [Fact]
        public async Task Starting_a_live_stream_from_Initializing_is_ignored()
        {
            // You cannot skip straight from Initializing to LiveStreaming; the historical fetch
            // must have fired first. This is the case-reorder the file exists to catch, and it
            // is now asserted against the real transition table rather than a copy of it.
            var f = new ScriptedFetcher();
            using var o = Build(f, out _);

            await o.StartLiveStreamAsync("Crypto", "Binance", "BTC/USD", "1h");

            Assert.Equal(DataState.Initializing, o.CurrentState);
        }

        [Theory]
        [MemberData(nameof(ReachableStateRows))]
        public async Task An_error_from_any_state_lands_in_Faulted(DataState start)
        {
            var (o, f) = await InState(start);
            using (o)
            {
                f.Next = _ => Task.FromException<List<Ohlcv>>(new InvalidOperationException("boom"));
                await Fetch(o, "Kraken");

                Assert.Equal(DataState.Faulted, o.CurrentState);
            }
        }

        [Theory]
        [MemberData(nameof(ReachableStateRows))]
        public async Task A_reset_from_any_state_returns_to_Initializing(DataState start)
        {
            var (o, _) = await InState(start);
            using (o)
            {
                await o.StopLiveStreamAsync();

                Assert.Equal(DataState.Initializing, o.CurrentState);
            }
        }

        [Fact]
        public async Task A_tick_while_Initializing_does_not_start_the_stream()
        {
            var f = new ScriptedFetcher();
            var bus = new SpyEventBus();
            var live = new MockLiveStreamManager();
            using var o = new DataOrchestrator(f, live, bus,
                NullLogger<DataOrchestrator>.Instance, new DemoPolicy(isDemo: false));

            live.EmitTick(new Ohlcv());
            await Task.Delay(50);

            Assert.Equal(DataState.Initializing, o.CurrentState);
        }

        /// <summary>
        /// <see cref="DataState.Stalled"/> is in the enum and in the transition table, and the
        /// old version of this file "covered" it by driving its own copy of the switch. In the
        /// shipping app it is unreachable: <see cref="DataTrigger.NetworkLagged"/> is the only
        /// way in and nothing fires it. So the honest guard is on the premise — if someone adds
        /// a producer, the Stalled ↔ LiveStreaming edge becomes live code with no behavioural
        /// test behind it, and this says so instead of a green test implying otherwise.
        /// </summary>
        [Fact]
        public void Stalled_is_unreachable_because_nothing_fires_NetworkLagged()
        {
            var core = Path.Combine(RepoPaths.RepoRoot(), "AccessibleTrader.Core");
            var producers = Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
                .Select(path => (path, text: File.ReadAllText(path)))
                .Where(f => f.text.Contains("Fire(DataTrigger.NetworkLagged"))
                .Select(f => Path.GetRelativePath(core, f.path))
                .ToList();

            Assert.True(producers.Count == 0,
                "Something now fires DataTrigger.NetworkLagged, so DataState.Stalled is reachable "
              + "in the shipping app for the first time. Add a behavioural test for the "
              + "LiveStreaming → Stalled → LiveStreaming edge and update this guard. Found in: "
              + string.Join(", ", producers));

            // Anti-vacuity: the scan must be looking at real source, not an empty directory.
            Assert.Contains(Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories),
                p => Path.GetFileName(p) == "DataOrchestrator.cs");
        }
    }
}
