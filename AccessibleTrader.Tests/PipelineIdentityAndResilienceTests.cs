using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Guards for the two 2026-08-21 audit ship-blockers in the data pipeline:
    /// the tab-switch tick merge, and the resilience layer that could not be reached.
    ///
    /// <para>
    /// Both are instances of the same recurrence pattern the audit named: the defect
    /// was fixed once at the site where it was reported and left standing at the
    /// structurally identical sites. So the guards here are deliberately STRUCTURAL —
    /// they enumerate every provider and pin the shape of the channel type — rather
    /// than pinning one symptom at one call site. A behavioural test proves the fix
    /// works today; a structural one is what stops the third instance.
    /// </para>
    /// </summary>
    public class PipelineIdentityAndResilienceTests
    {
        private static Ohlcv Bar(int daysFromEpoch, double close = 100) =>
            new(new DateTime(2026, 1, 1).AddDays(daysFromEpoch), close, close + 1, close - 1, close, 1);

        private static ChartIdentity Id(string symbol) => new("Spot", "TestProv", symbol, "1h");

        private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (!condition())
            {
                Assert.True(Environment.TickCount64 < deadline, "Condition not met within timeout.");
                await Task.Delay(10);
            }
        }

        private static MarketFeedHub Hub(KeyedFeedsTests.FakeOrchestrator orch) =>
            new(orch, Substitute.For<IDataService>(), new DemoPolicy(false), NullLoggerFactory.Instance);

        // ── The tab-switch tick merge ────────────────────────────────────────

        /// <summary>
        /// The audit's ship-blocker, reproduced at the seam where it happened. Focus
        /// moves synchronously; the subscription is retargeted only after an awaited
        /// gap-fill. In that window the still-running pump is holding the OUTGOING
        /// symbol's ticks and the INCOMING symbol's feed.
        ///
        /// <para>
        /// Consequence, if this regresses: <c>Append</c> fabricates a bar at the wrong
        /// symbol's price, raising <c>LiveAppend</c> — which is the trigger
        /// <c>StrategyEngine.OnFocusedFeedUpdated</c> uses to evaluate a closed bar,
        /// and it can place a real order off a bar that never existed.
        /// </para>
        /// </summary>
        [Fact]
        public async Task Focused_pump_drops_a_tick_belonging_to_the_symbol_that_just_lost_focus()
        {
            var orch = new KeyedFeedsTests.FakeOrchestrator();
            using var hub = Hub(orch);

            var btc = hub.SetFocus(Id("BTC/USD"));
            btc.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0, 50_000) }));
            await hub.StartFocusedLiveAsync();

            // The tab switch: focus moves NOW. The subscription is still BTC's.
            var eth = hub.SetFocus(Id("ETH/USD"));
            eth.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new[] { Bar(0, 3_000) }));

            orch.PushTick(Id("BTC/USD"), Bar(1, 50_100));

            // Give the pump every chance to get it wrong.
            await Task.Delay(150);

            Assert.Equal(1, eth.Bars.Count);
            Assert.Equal(3_000, eth.Bars[^1].Close);   // NOT 50,100

            // …and the correctly-addressed tick still lands, so the guard is not
            // simply dropping everything (a check that passes vacuously is worse
            // than no check).
            orch.PushTick(Id("ETH/USD"), Bar(1, 3_050));
            await WaitUntil(() => eth.Bars.Count == 2);
            Assert.Equal(3_050, eth.Bars[^1].Close);

            await hub.StopFocusedLiveAsync();
        }

        /// <summary>
        /// The structural half. A live bar must not be routable without saying which
        /// identity it belongs to — the fix is only durable while the channel carries
        /// <see cref="LiveTick"/> rather than a bare <see cref="Ohlcv"/>. Simplifying
        /// the element type back is exactly how this defect returns, and it would look
        /// like a tidy-up in review.
        /// </summary>
        [Fact]
        public void The_live_stream_channel_carries_an_identity_with_every_bar()
        {
            var prop = typeof(IDataOrchestrator).GetProperty(nameof(IDataOrchestrator.LiveStream));
            Assert.NotNull(prop);
            Assert.Equal(typeof(System.Threading.Channels.ChannelReader<LiveTick>), prop!.PropertyType);

            var managerProp = typeof(LiveStreamManager).GetProperty(nameof(LiveStreamManager.LiveStream));
            Assert.NotNull(managerProp);
            Assert.Equal(typeof(System.Threading.Channels.ChannelReader<LiveTick>), managerProp!.PropertyType);

            // And the identity is a real field on it, not a decoration.
            Assert.NotNull(typeof(LiveTick).GetProperty(nameof(LiveTick.Identity)));
            Assert.NotNull(typeof(LiveTick).GetProperty(nameof(LiveTick.Bar)));
        }

        // ── The twin: a store dispatch that outlived its tab ─────────────────

        /// <summary>
        /// The same class one layer up, found by asking where else "routed by focus at
        /// completion time" lives. <c>DataManager</c> captures the focused feed, awaits
        /// a fetch, then dispatches — and <c>UpdateDataAction</c> carries no identity, so
        /// the store cannot tell that the bars belong to the tab the user just left.
        ///
        /// <para>
        /// This is not cosmetic: <c>PaperTradingProvider.OnState</c> reads exactly the
        /// pair (<c>st.Identity</c>, last bar of <c>st.Data</c>) to price positions and
        /// fill resting orders. The previous symbol's close filed under the new symbol's
        /// name fills orders at a price that symbol never traded at.
        /// </para>
        /// </summary>
        [Fact]
        public async Task Store_update_is_dropped_when_another_tab_took_focus_during_the_fetch()
        {
            var orch = new KeyedFeedsTests.FakeOrchestrator();
            using var hub = Hub(orch);
            var store = new RecordingStore();
            var manager = new DataManager(hub, store.Store, Substitute.For<IEventBus>(),
                NullLogger<DataManager>.Instance, Substitute.For<IServiceProvider>());

            manager.Identity = Id("BTC/USD");
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(0, 50_000), Bar(1, 50_100) });

            // Hold the fetch open, start the refresh, then switch tabs underneath it.
            var gate = new TaskCompletionSource();
            orch.FetchGate = gate;
            var refresh = manager.RefreshDataAsync();
            await Task.Delay(50);
            hub.SetFocus(Id("ETH/USD"));
            gate.SetResult();
            await refresh;

            Assert.Empty(store.Dispatched.OfType<UpdateDataAction>());
        }

        /// <summary>Vacuity check for the test above: with focus left alone, the very
        /// same path MUST dispatch. A guard that drops everything proves nothing.</summary>
        [Fact]
        public async Task Store_update_still_lands_when_focus_does_not_move()
        {
            var orch = new KeyedFeedsTests.FakeOrchestrator();
            using var hub = Hub(orch);
            var store = new RecordingStore();
            var manager = new DataManager(hub, store.Store, Substitute.For<IEventBus>(),
                NullLogger<DataManager>.Instance, Substitute.For<IServiceProvider>());

            manager.Identity = Id("BTC/USD");
            orch.FetchResults.Enqueue(new List<Ohlcv> { Bar(0, 50_000), Bar(1, 50_100) });

            await manager.RefreshDataAsync();

            Assert.Single(store.Dispatched.OfType<UpdateDataAction>());
        }

        // ── The resilience layer that could not be reached ───────────────────

        /// <summary>
        /// The structural sweep, and the load-bearing guard of the two resilience ones.
        ///
        /// <para>
        /// Every data provider's <c>FetchOhlcvAsync</c> ended in
        /// <c>catch (Exception) { report; return empty; }</c>. Fixing one, or five,
        /// leaves the pipeline's retry and circuit breaker unreachable through the other
        /// twenty-six — the exact recurrence pattern the audit named. So this enumerates
        /// the plugin tree rather than naming providers: a NEW provider added tomorrow
        /// with the old shape fails this test on the day it lands.
        /// </para>
        /// <para>
        /// Comments are stripped before scanning. Every one of these catch blocks now
        /// carries a comment mentioning <c>TransportFailure</c> explaining the rethrow,
        /// so a scan that read comments would pass on the prose describing the fix while
        /// the code did the opposite — the false negative that
        /// <c>ProviderCapabilityAudit</c> already documents ("a mention in a comment is
        /// not implementing it").
        /// </para>
        /// </summary>
        [Fact]
        public void Every_provider_rethrows_transport_faults_out_of_FetchOhlcvAsync()
        {
            var offenders = new List<string>();
            int scanned = 0;

            foreach (var file in ProviderSourceFiles())
            {
                var source = StripCommentsAndStrings(File.ReadAllText(file));
                var body = MethodBody(source, "FetchOhlcvAsync(MarketDataRequest");
                if (body == null) continue;
                scanned++;

                // Only providers that swallow at all are in scope; one that lets
                // everything out is already correct.
                if (!Regex.IsMatch(body, @"catch\s*\(")) continue;

                if (!body.Contains("TransportFailure.IsTransient") && !Regex.IsMatch(body, @"\bthrow\s*;"))
                    offenders.Add(Path.GetFileName(file));
            }

            Assert.True(scanned >= 30,
                $"Expected to scan 30+ provider FetchOhlcvAsync bodies; found {scanned}. " +
                "The scan lost its targets — fix the discovery, do not lower the floor.");
            Assert.True(offenders.Count == 0,
                "These providers swallow transport faults, which makes DataOrchestrator's retry and "
                + "circuit breaker unreachable through them (see TransportFailure): "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// A cancellation catch in <c>FetchOhlcvAsync</c> must carry a filter, because
        /// HttpClient's own timeout arrives as a <see cref="TaskCanceledException"/> WRAPPING a
        /// <see cref="TimeoutException"/> — transport, and the retry and breaker's to handle.
        ///
        /// <para>
        /// This exists because the scan above could not see the hole it was written to close.
        /// It asks whether the METHOD BODY mentions <c>TransportFailure.IsTransient</c> anywhere,
        /// so a provider that rethrows correctly in its generic <c>catch (Exception)</c> passes —
        /// even while an EARLIER, narrower clause swallows the timeout before it can ever reach
        /// that guard. PolygonProvider did exactly that and the suite was green. Presence of the
        /// right call somewhere in a method is not the same as no path around it.
        /// </para>
        /// <para>
        /// None of these methods take a <c>CancellationToken</c>, so an unfiltered clause is not
        /// merely imprecise — a timeout is the only thing it can realistically catch.
        /// </para>
        /// </summary>
        [Fact]
        public void No_provider_swallows_a_timeout_through_an_unfiltered_cancellation_catch()
        {
            var offenders = new List<string>();
            int scanned = 0;

            foreach (var file in ProviderSourceFiles())
            {
                var source = StripCommentsAndStrings(File.ReadAllText(file));
                var body = MethodBody(source, "FetchOhlcvAsync(MarketDataRequest");
                if (body == null) continue;
                scanned++;

                // A cancellation clause is fine WITH a `when (…)` filter — that is how the
                // timeout is let through to the transport guard. Without one it catches both.
                foreach (Match m in Regex.Matches(
                             body, @"catch\s*\(\s*(?:System\.Threading\.Tasks\.)?(TaskCanceled|OperationCanceled)Exception\b[^)]*\)\s*(?<filter>when\b)?"))
                {
                    if (!m.Groups["filter"].Success)
                        offenders.Add($"{Path.GetFileName(file)} ({m.Groups[1].Value}Exception)");
                }
            }

            Assert.True(scanned >= 30,
                $"Expected to scan 30+ provider FetchOhlcvAsync bodies; found {scanned}. " +
                "The scan lost its targets — fix the discovery, do not lower the floor.");
            Assert.True(offenders.Count == 0,
                "These providers catch cancellation unfiltered in FetchOhlcvAsync, which swallows "
                + "HttpClient's timeout (TaskCanceledException wrapping TimeoutException) before the "
                + "transport guard can rethrow it — add `when (!TransportFailure.IsTransient(ex))`: "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// The innermost body of the policy must be able to fail. <c>DataService</c>'s
        /// blanket <c>catch (Exception) { return empty; }</c> is the single line that
        /// made three layers of Polly decorative, and it is invisible on review because
        /// it looks like ordinary defensive coding.
        /// </summary>
        [Fact]
        public async Task DataService_lets_a_transport_failure_reach_the_policy()
        {
            var service = NewDataServiceWith(ThrowingProvider(
                new HttpRequestException("connection reset")));

            await Assert.ThrowsAsync<HttpRequestException>(() => service.FetchOhlcvAsync(
                "Throwing", new MarketDataRequest("Crypto", "BTC/USD", "1h", 100)));
        }

        /// <summary>Vacuity check: a NON-transport provider fault must still be
        /// swallowed into an empty result, or this "fix" is just an uncaught crash on
        /// every malformed payload.</summary>
        [Fact]
        public async Task DataService_still_surfaces_a_provider_bug_as_an_empty_result()
        {
            var service = NewDataServiceWith(ThrowingProvider(
                new InvalidOperationException("provider parsed its own response wrong")));

            // The provider swallows its own bugs; DataService adds nothing.
            var result = await service.FetchOhlcvAsync(
                "Throwing", new MarketDataRequest("Crypto", "BTC/USD", "1h", 100));

            Assert.Empty(result.Ohlcv);
        }

        /// <summary>
        /// The retry must actually retry. It never did: nothing could throw underneath
        /// it, so <c>retryCount: 1</c> described a behaviour that had never once run.
        /// </summary>
        [Fact]
        public async Task Transport_failure_is_retried_exactly_once()
        {
            var fetcher = new CountingFetcher(() => throw new HttpRequestException("socket closed"));
            using var orchestrator = new DataOrchestrator(fetcher, new MockLiveStreamManager(),
                new SpyEventBus(), NullLogger<DataOrchestrator>.Instance, new DemoPolicy(isDemo: false));

            var bars = await orchestrator.FetchOhlcvAsync("Crypto", "P1", "BTC/USD", "1h");

            Assert.Empty(bars);
            Assert.Equal(2, fetcher.Calls);   // the attempt, plus the one retry
        }

        /// <summary>
        /// A permanent fault must NOT be retried — otherwise every bad symbol costs the
        /// provider two requests and the user twice the wait. Pins the boundary the
        /// retry set draws, not just that it fires.
        /// </summary>
        [Fact]
        public async Task A_non_transport_failure_is_not_retried()
        {
            var fetcher = new CountingFetcher(() => throw new InvalidOperationException("bad symbol"));
            using var orchestrator = new DataOrchestrator(fetcher, new MockLiveStreamManager(),
                new SpyEventBus(), NullLogger<DataOrchestrator>.Instance, new DemoPolicy(isDemo: false));

            await orchestrator.FetchOhlcvAsync("Crypto", "P2", "BTC/USD", "1h");

            Assert.Equal(1, fetcher.Calls);
        }

        /// <summary>
        /// The breaker must trip, and the user must HEAR it. Before the fix, <c>onBreak</c>
        /// was unreachable, so <c>ConnectionStatusEvent(Error)</c> and
        /// <c>DataTrigger.ErrorOccurred</c> were both dead from the common failure path and
        /// the only symptom of a dead provider was a chart with no bars on it.
        /// </summary>
        [Fact]
        public async Task The_circuit_breaker_trips_and_announces_it()
        {
            var fetcher = new CountingFetcher(() => throw new HttpRequestException("socket closed"));
            var bus = new SpyEventBus();
            using var orchestrator = new DataOrchestrator(fetcher, new MockLiveStreamManager(),
                bus, NullLogger<DataOrchestrator>.Instance, new DemoPolicy(isDemo: false));

            // 10 failures allowed before breaking; each call spends two of them (retry).
            for (int i = 0; i < 6; i++)
                await orchestrator.FetchOhlcvAsync("Crypto", "P3", "BTC/USD", "1h");

            Assert.Contains(bus.Log.OfType<ConnectionStatusEvent>(),
                e => e.State == ConnectionState.Error);
            Assert.Contains(bus.Log.OfType<FeedbackRequestEvent>(),
                e => e.Type == FeedbackType.Error && (e.Message ?? "").Contains("not responding"));
        }

        // ── TransportFailure's own boundary ──────────────────────────────────

        [Theory]
        // Never reached the far end at all — the most transient thing there is.
        [InlineData(null, true)]
        [InlineData(500, true)]
        [InlineData(503, true)]
        [InlineData(408, true)]   // request timeout: waiting IS the fix
        [InlineData(429, true)]   // rate limited: ditto, and the rate-limit breaker's job
        [InlineData(401, false)]  // bad key — retrying cannot help
        [InlineData(403, false)]  // exhausted quota / forbidden
        [InlineData(404, false)]  // symbol does not exist
        public void Http_status_decides_whether_a_failure_is_worth_retrying(int? status, bool expected)
        {
            var ex = status is null
                ? new HttpRequestException("connection refused")
                : new HttpRequestException("boom", null, (System.Net.HttpStatusCode)status.Value);

            Assert.Equal(expected, TransportFailure.IsTransient(ex));
        }

        [Fact]
        public void A_cancelled_request_is_not_a_transport_failure()
        {
            // A tab switch cancelling an in-flight fetch must not be retried, must not
            // count toward a breaker, and must not be announced as a network fault.
            Assert.False(TransportFailure.IsTransient(new OperationCanceledException()));
            Assert.False(TransportFailure.IsTransient(new TaskCanceledException()));

            // …but HttpClient's own timeout arrives as a TaskCanceledException wrapping a
            // TimeoutException, and that one IS transport. This is why the check walks
            // inner exceptions instead of testing the outermost type.
            Assert.True(TransportFailure.IsTransient(
                new TaskCanceledException("timed out", new TimeoutException())));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static IEnumerable<string> ProviderSourceFiles()
        {
            var root = RepoRoot();
            foreach (var dir in new[] { "Plugins/Providers", "Plugins/Analytics" })
            {
                var full = Path.Combine(root, dir);
                if (!Directory.Exists(full)) continue;
                foreach (var f in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
                {
                    if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                    if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                    yield return f;
                }
            }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate the repository root from the test output directory.");
            return dir!.FullName;
        }

        /// <summary>
        /// Removes comments and string/char literals so a scan reads CODE only.
        /// Two of the four guards written on 2026-08-21 were initially incapable of
        /// failing because the prose explaining a bug matched the pattern for the bug.
        /// </summary>
        internal static string StripCommentsAndStrings(string source)
        {
            var sb = new StringBuilder(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];

                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    i++;
                    continue;
                }
                // Raw string literal — """…""" — swallowed whole; JSON fixtures inside
                // provider sources are full of braces that would wreck brace matching.
                if (c == '"' && i + 2 < source.Length && source[i + 1] == '"' && source[i + 2] == '"')
                {
                    int q = 0;
                    while (i < source.Length && source[i] == '"') { i++; q++; }
                    int run = 0;
                    while (i < source.Length && run < q)
                    {
                        run = source[i] == '"' ? run + 1 : 0;
                        i++;
                    }
                    sb.Append("\"\"");
                    i--;
                    continue;
                }
                if (c == '"')
                {
                    i++;
                    while (i < source.Length && source[i] != '"')
                    {
                        if (source[i] == '\\') i++;
                        i++;
                    }
                    sb.Append("\"\"");
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < source.Length && source[i] != '\'')
                    {
                        if (source[i] == '\\') i++;
                        i++;
                    }
                    sb.Append("' '");
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Body of the first method whose signature contains
        /// <paramref name="signature"/>, by brace matching. Null when absent.</summary>
        internal static string? MethodBody(string source, string signature)
        {
            int i = source.IndexOf(signature, StringComparison.Ordinal);
            if (i < 0) return null;
            int open = source.IndexOf('{', i);
            if (open < 0) return null;
            int depth = 0;
            for (int k = open; k < source.Length; k++)
            {
                if (source[k] == '{') depth++;
                else if (source[k] == '}')
                {
                    depth--;
                    if (depth == 0) return source.Substring(open, k - open + 1);
                }
            }
            return null;
        }

        private static DataService NewDataServiceWith(IMarketDataProvider provider)
        {
            var apiKeys = Substitute.For<IApiKeyService>();
            var service = new DataService(
                Substitute.For<IPluginLoaderService>(),
                NullLogger<DataService>.Instance,
                Substitute.For<ICacheService>(),
                apiKeys);

            // No injection point for providers — the real list is built by plugin
            // discovery. Reflection is how the existing suite reaches these internals
            // (see PaginationBoundsTests).
            SetPrivate(service, "_isInitialized", true);
            var providers = (List<IMarketDataProvider>)GetPrivate(service, "_providers")!;
            providers.Add(provider);
            return service;
        }

        private static void SetPrivate(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
                  .SetValue(target, value);

        private static object? GetPrivate(object target, string field) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
                  .GetValue(target);

        /// <summary>
        /// A provider that behaves exactly as the real ones now do: report, then rethrow
        /// only what the pipeline is waiting for. Substituted rather than subclassed —
        /// <see cref="IMarketDataProvider"/> has ~20 members and none of the others
        /// matter here.
        /// </summary>
        private static IMarketDataProvider ThrowingProvider(Exception ex)
        {
            var provider = Substitute.For<IMarketDataProvider>();
            provider.Name.Returns("Throwing");

            // BOTH overloads. DataService calls the cancellable one added 2026-08-27, and a
            // substitute intercepts it rather than falling through to the default interface
            // implementation — so stubbing only the one-arg form made this double return an
            // empty result instead of throwing, and the test read as "the transport failure
            // was swallowed" when nothing had even been asked to fail.
            Task<(List<Ohlcv>, List<(long Timestamp, double Volume)>)> Behaviour()
            {
                if (TransportFailure.IsTransient(ex)) throw ex;
                return Task.FromResult((new List<Ohlcv>(), new List<(long Timestamp, double Volume)>()));
            }

            provider.FetchOhlcvAsync(Arg.Any<MarketDataRequest>()).Returns(_ => Behaviour());
            provider.FetchOhlcvAsync(Arg.Any<MarketDataRequest>(), Arg.Any<CancellationToken>())
                    .Returns(_ => Behaviour());
            return provider;
        }

        /// <summary>Counts how many times the policy actually invoked the fetch.</summary>
        private sealed class CountingFetcher : HistoricalDataFetcher
        {
            private readonly Func<List<Ohlcv>> _behaviour;
            public int Calls;

            public CountingFetcher(Func<List<Ohlcv>> behaviour) : base(null!, null!, null!, null!)
                => _behaviour = behaviour;

            public override Task<List<Ohlcv>> FetchOhlcvAsync(string market, string providerName,
                string symbol, string timeframe, long? since = null, int? limit = null, long? until = null,
                CancellationToken ct = default)
            {
                Interlocked.Increment(ref Calls);
                try { return Task.FromResult(_behaviour()); }
                catch (Exception ex) { return Task.FromException<List<Ohlcv>>(ex); }
            }
        }

        /// <summary>Records what reached the store. Mirrors KeyedFeedsTests.AdapterHarness
        /// so the two suites agree on what a store looks like under test.</summary>
        private sealed class RecordingStore
        {
            public readonly IWorkspaceStore Store = Substitute.For<IWorkspaceStore>();
            public readonly List<WorkspaceAction> Dispatched = new();

            public RecordingStore()
            {
                Store.State.Returns(_ => WorkspaceState.Initial);
                Store.When(s => s.Dispatch(Arg.Any<WorkspaceAction>())).Do(ci =>
                {
                    lock (Dispatched) Dispatched.Add(ci.Arg<WorkspaceAction>());
                });
            }
        }
    }
}
