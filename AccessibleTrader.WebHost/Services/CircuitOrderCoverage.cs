using System.Collections.Concurrent;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>Which venues' fills the open browser sessions are already announcing</b> — the
    /// routing rule for Phase 2, and the exact shape of <see cref="CircuitAlertCoverage"/>
    /// one domain over.
    ///
    /// <para>
    /// ── Why it is needed ──────────────────────────────────────────────────────
    /// Provider plugins are SINGLETONS (<c>IPluginLoaderService</c> is registered
    /// <c>AddSingleton</c>), so a venue's <c>OrderUpdateStream</c> is one object shared by
    /// every scope in the process. Once the headless session subscribes to it, the same fill
    /// is delivered to the headless <see cref="IEventBus"/> AND to the bus of every circuit
    /// whose own order service is subscribed. Both would announce it — the headless side
    /// through <c>spd-say</c>, the circuit through the browser — and the user hears the fill
    /// twice. That is the narration bug of 2026-09-05 inverted, and it is the hazard the
    /// whole headless-session design has to answer for every event type it adds.
    /// </para>
    ///
    /// <para>
    /// ── Why PER PROVIDER and not "is a browser open" ──────────────────────────
    /// Because that is precisely the mistake Phase 1 had to undo. The alert monitor stood
    /// down whenever <c>ActiveCircuits &gt; 0</c>, on the reasoning that the circuit had it —
    /// and the circuit had only the symbol on screen, so everything else was watched by
    /// nobody. The same trap is here in a different costume: a circuit announces fills only
    /// for the venues its own order service actually hooked, and hooking can fail (no stored
    /// key for that venue, a provider that is not a trading provider, a socket that dropped
    /// at 03:00 and took itself out of the set). "A browser is open" is not the same claim as
    /// "that fill will be announced", so the question asked here is the second one.
    /// </para>
    ///
    /// <para>
    /// ── A pull, not a push ────────────────────────────────────────────────────
    /// Each circuit registers a callback rather than a snapshot, because what it covers
    /// changes underneath it: a stream that fails removes itself from
    /// <c>IOrderExecutionService.LiveOrderStreamProviders</c> with no event of its own, and a
    /// snapshot taken at circuit start would then claim coverage that had since evaporated —
    /// silent non-coverage, which is the failure mode this feature exists to prevent. Every
    /// call is wrapped: a circuit whose scope is mid-disposal contributes nothing rather than
    /// throwing, and contributing nothing routes the fill to the headless side. Of the two
    /// ways to be wrong, a possible duplicate is recoverable and silence is not.
    /// </para>
    /// </summary>
    public static class CircuitOrderCoverage
    {
        private static readonly ConcurrentDictionary<string, Func<IEnumerable<string>>> _sources
            = new(StringComparer.Ordinal);

        /// <summary>How many circuits are currently contributing coverage.</summary>
        public static int SourceCount => _sources.Count;

        /// <summary>
        /// Register one circuit's covered-provider callback. Dispose the returned handle when
        /// the circuit closes — an un-disposed handle keeps a closed browser's venues
        /// "covered" forever, and the headless session would then stay silent about fills
        /// nobody is announcing.
        /// </summary>
        public static IDisposable Register(string circuitId, Func<IEnumerable<string>> providers)
        {
            _sources[circuitId] = providers;
            return new Registration(circuitId);
        }

        /// <summary>
        /// The union of every open circuit's covered venues. Empty when the browser is
        /// closed — which correctly means "the headless session owns every fill".
        /// </summary>
        public static IReadOnlySet<string> CoveredProviders()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in _sources.Values)
            {
                IEnumerable<string> providers;
                try { providers = source() ?? Enumerable.Empty<string>(); }
                catch { continue; }   // a disposing circuit covers nothing; the headless side takes it

                foreach (var p in providers)
                    if (!string.IsNullOrWhiteSpace(p)) set.Add(p.Trim());
            }
            return set;
        }

        /// <summary>
        /// Whether an open browser session is already announcing fills from
        /// <paramref name="provider"/>.
        ///
        /// <para>
        /// A null or blank provider is NOT covered. An event that cannot say where it came
        /// from is routed to the headless side deliberately: the alternative is to guess that
        /// somebody else has it, and that guess is how a fill goes unannounced.
        /// </para>
        /// </summary>
        public static bool IsCovered(string? provider) =>
            !string.IsNullOrWhiteSpace(provider) && CoveredProviders().Contains(provider.Trim());

        /// <summary>Test seam: forget every registered circuit.</summary>
        internal static void ResetForTests() => _sources.Clear();

        private sealed class Registration : IDisposable
        {
            private readonly string _circuitId;
            public Registration(string circuitId) => _circuitId = circuitId;
            public void Dispose() => _sources.TryRemove(_circuitId, out _);
        }
    }
}
