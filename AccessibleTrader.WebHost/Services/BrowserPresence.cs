using System.Collections.Concurrent;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>Is a browser CONNECTED to this process right now?</b> One boolean, process-wide, and
    /// since 2026-09-11 it is the only input the browser-closed half needs.
    ///
    /// <para>
    /// ── The policy it implements (Cody, 2026-09-11) ───────────────────────────
    /// "Toast should be browser closed only, that's the entire purpose. When I have background
    /// monitoring on, I close the browser and receive all terminal events as system
    /// notifications. If the browser is open, then I receive them as normal aria events in the
    /// browser, and that includes if the browser is minimized."
    /// </para>
    ///
    /// <para>
    /// That makes the routing decision PER PROCESS, not per symbol and not per venue. The two
    /// registries this replaces as a gate — <c>CircuitAlertCoverage</c> and
    /// <c>CircuitOrderCoverage</c> — each argued in writing for per-symbol ownership, and each
    /// was right about the question it was asked ("who is already SAYING this?"). The question
    /// changed: with a browser connected, everything is said in the browser.
    /// </para>
    ///
    /// <para>
    /// ── Why not <c>WebHostBrowserCircuitHandler.ActiveCircuits</c> ────────────
    /// That counter is incremented in <c>OnCircuitOpenedAsync</c> and decremented only in
    /// <c>OnCircuitClosedAsync</c>, so it counts RETAINED circuits: it reads 1 for the three
    /// minutes Blazor holds a closed tab's circuit open for a possible reconnect, and 2 for
    /// three minutes after a reload. Gating on it would reinstate the exact three-minute
    /// silence the 2026-09-11 hand-off fix removed — Cody's "three tabs, a 1-minute chart,
    /// browser closed, no notification". This one counts CONNECTIONS: up on
    /// <c>OnConnectionUpAsync</c>, down on <c>OnConnectionDownAsync</c>, which is the only pair
    /// that tracks whether anything can actually reach the user.
    /// </para>
    ///
    /// <para>
    /// ── The grace period, and why the gate is not the raw count ───────────────
    /// A connection goes down on a reload, a laptop sleep, a VPN flap and a pulled cable, and
    /// only some of those mean "the user has left". <see cref="HeadlessOwnsDelivery"/> is
    /// therefore false until <see cref="Grace"/> has passed since the last connection dropped,
    /// so a reload round-trip does not hand the process to the notification channel for two
    /// seconds and hand it back. The count itself is exact and immediate; only the OWNERSHIP
    /// decision is debounced. The same edge is where the farewell toast fires.
    /// </para>
    ///
    /// <para>
    /// Static, with a <see cref="ResetForTests"/>, because the four headless test files already
    /// drive circuits through static seams and a fifth shape would be a fifth thing to reset.
    /// </para>
    /// </summary>
    public static class BrowserPresence
    {
        /// <summary>
        /// How long after the last connection drops before the headless side takes over.
        /// Longer than a reload round-trip or a brief blip, shorter than one 60-second poll —
        /// so the hand-off still happens within the same poll cycle the user closed the browser in.
        /// </summary>
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);

        /// <summary>Clock indirection so tests can advance time, as <c>AlertSnooze</c> does.</summary>
        public static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        // Keyed by circuit id rather than counted, so a handler that reports "up" twice — which
        // OnConnectionUpAsync genuinely does, because it also runs for the FIRST connection right
        // after OnCircuitOpenedAsync — cannot inflate the count. A counter would have needed the
        // caller to be idempotent; a set makes the class itself idempotent.
        private static readonly ConcurrentDictionary<string, byte> _connected = new(StringComparer.Ordinal);

        private static long _lastDropUtcTicks;

        /// <summary>Circuits with a live SignalR connection right now.</summary>
        public static int ConnectedCircuits => _connected.Count;

        /// <summary>A browser is on the other end of this process. Minimised counts.</summary>
        public static bool AnyConnected => !_connected.IsEmpty;

        /// <summary>
        /// <b>The gate.</b> True when no browser is connected and the grace period has passed —
        /// i.e. the headless process owns every terminal event and delivers it as a system
        /// notification. False whenever a browser is connected, in which case the browser
        /// announces everything and nothing may toast.
        /// </summary>
        public static bool HeadlessOwnsDelivery
        {
            get
            {
                if (!_connected.IsEmpty) return false;
                long ticks = Interlocked.Read(ref _lastDropUtcTicks);
                if (ticks == 0) return true;   // never had a browser this run — headless from the start
                return UtcNow() - new DateTime(ticks, DateTimeKind.Utc) >= Grace;
            }
        }

        /// <summary>
        /// Raised on the 1→0 and 0→1 edges with the new connected count. The farewell toast and
        /// the circuit deliverers' mute both hang off this rather than off the circuit handler,
        /// so there is one place that knows what an edge is.
        /// </summary>
        public static event Action<int>? ConnectedCountChanged;

        /// <summary>A circuit's connection came up. Idempotent per circuit id.</summary>
        public static void Connected(string circuitId)
        {
            if (string.IsNullOrEmpty(circuitId)) return;
            if (!_connected.TryAdd(circuitId, 0)) return;
            Raise();
        }

        /// <summary>A circuit's connection went down. Idempotent per circuit id.</summary>
        public static void Disconnected(string circuitId)
        {
            if (string.IsNullOrEmpty(circuitId)) return;
            if (!_connected.TryRemove(circuitId, out _)) return;
            if (_connected.IsEmpty)
                Interlocked.Exchange(ref _lastDropUtcTicks, UtcNow().Ticks);
            Raise();
        }

        private static void Raise()
        {
            // A subscriber that throws must not stop the next one, and must never propagate
            // into a Blazor circuit lifecycle method — a throw there tears down the circuit.
            foreach (var handler in ConnectedCountChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                try { ((Action<int>)handler)(_connected.Count); }
                catch { /* a broken listener is not a reason to lose the edge */ }
            }
        }

        /// <summary>Test seam: no circuits connected, no drop recorded, no listeners.</summary>
        internal static void ResetForTests()
        {
            _connected.Clear();
            Interlocked.Exchange(ref _lastDropUtcTicks, 0);
            ConnectedCountChanged = null;
            UtcNow = () => DateTime.UtcNow;
        }
    }
}
