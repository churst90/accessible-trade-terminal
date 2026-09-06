using System.Collections.Concurrent;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>What the open browser sessions are already watching</b> — the rule that replaced
    /// "stand down entirely while a circuit is open".
    ///
    /// <para>
    /// ── The defect this closes ────────────────────────────────────────────────
    /// <see cref="LocalBackgroundMonitor"/> used to return immediately whenever
    /// <c>WebHostBrowserCircuitHandler.ActiveCircuits &gt; 0</c>. The reasoning was sound as far
    /// as it went — the monitor and the circuit both speak through the same Orca, and doubling
    /// every announcement is the bug the speech work killed. But the in-session pipeline only
    /// evaluates alerts for the symbol ON SCREEN (<c>AlertOrchestrator</c>'s Part A symbol
    /// gating), plus whatever <c>BackgroundMonitoringService</c> covers for other open tabs —
    /// which is opt-in and off by default. So an alert on a symbol with no tab open was
    /// evaluated by NOBODY while the browser was connected: <b>closing your browser made more of
    /// your alerts work than leaving it open.</b>
    /// </para>
    ///
    /// <para>
    /// This is the same defect the HOSTED monitor already fixed, in the same words —
    /// see the "Which symbols a user actually has on screen" note in
    /// <see cref="WebHostBrowserCircuitHandler"/>. Hosted suppresses per symbol keyed by user;
    /// the local desktop has exactly one user, so it suppresses per symbol across every circuit
    /// in the process.
    /// </para>
    ///
    /// <para>
    /// ── Why a pull, not a push ────────────────────────────────────────────────
    /// The circuit handler could push a symbol on every workspace-state change, and for the
    /// FOCUSED chart it already does. But coverage also includes the background workspace
    /// monitors, which start and stop on tab switches without a state change of their own, and a
    /// pushed snapshot of those would be stale exactly when it mattered. Each circuit therefore
    /// registers a callback that computes its own coverage at the moment the background monitor
    /// asks — once every 60 seconds, off the circuit's threads. Every call is wrapped: a circuit
    /// whose scope is mid-disposal contributes nothing rather than throwing, and contributing
    /// nothing means the headless side takes the symbol, which is the safe direction to fail.
    /// </para>
    /// </summary>
    public static class CircuitAlertCoverage
    {
        private static readonly ConcurrentDictionary<string, Func<IEnumerable<string>>> _sources
            = new(StringComparer.Ordinal);

        /// <summary>How many circuits are currently contributing coverage.</summary>
        public static int SourceCount => _sources.Count;

        /// <summary>
        /// Register one circuit's coverage callback. Dispose the returned handle when the
        /// circuit closes — an un-disposed handle would keep a closed circuit's symbols
        /// "covered" forever, which is silent non-coverage, the failure mode this whole
        /// feature exists to avoid.
        /// </summary>
        public static IDisposable Register(string circuitId, Func<IEnumerable<string>> symbols)
        {
            _sources[circuitId] = symbols;
            return new Registration(circuitId);
        }

        /// <summary>
        /// The union of every open circuit's coverage, upper-cased for the case-insensitive
        /// comparison the alert pipeline uses. Empty when the browser is closed — which
        /// correctly means "suppress nothing".
        /// </summary>
        public static IReadOnlySet<string> CoveredSymbols()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in _sources.Values)
            {
                IEnumerable<string> symbols;
                try { symbols = source() ?? Enumerable.Empty<string>(); }
                catch { continue; }   // a disposing circuit covers nothing; the headless side takes it

                foreach (var s in symbols)
                    if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim());
            }
            return set;
        }

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
