using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Account;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Per-circuit setup that must run once for each browser connection.
    ///
    /// Currently it re-applies the Firefox <c>Ctrl+Shift+letter</c> → <c>Alt+Shift+letter</c>
    /// drawing-tool / detailed-point-summary remap (<see cref="WebHostShortcutRemap"/>).
    /// That used to run app-once in <c>Program.cs</c>, but once the multi-user scoping
    /// change made <see cref="IShortcutManager"/> per-circuit (Scoped), the remap has to
    /// run per circuit too — otherwise each new visitor's shortcut profile keeps the raw
    /// <c>Ctrl+Shift</c> chords that Firefox swallows. A <see cref="CircuitHandler"/> is
    /// the Blazor Server hook for per-circuit work, and (unlike the shared RCL's
    /// <c>MainLayout</c>) it can reference WebHost-only types like
    /// <see cref="WebHostShortcutRemap"/>.
    ///
    /// Registered Scoped, so the <see cref="IShortcutManager"/> injected here is the same
    /// per-circuit instance the components use.
    /// </summary>
    public sealed class WebHostBrowserCircuitHandler : CircuitHandler
    {
        // Ops observability: how many live browser sessions this process is
        // carrying. Logged on every open/close so the hosted operator can see
        // session churn in journalctl. Cleanup itself is Blazor's job — a closed
        // tab's circuit is retained ~3 minutes for reconnects, then disposed,
        // which disposes every per-circuit scoped service (feeds, providers,
        // audio) via standard DI scope disposal.
        private static int _activeCircuits;

        // Per-user circuit counts (keyed by ICurrentUser.DataKey): the hosted
        // alert monitor suppresses server-side evaluation for users whose OWN
        // session is connected — their in-session pipeline owns delivery then,
        // and double-sending every email/Telegram/push is the hosted analogue
        // of the local monitor's double-speech bug.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _circuitsByUser
            = new(StringComparer.Ordinal);
        private string? _userKey;

        /// <summary>Live circuits currently held by this user (0 when offline).</summary>
        internal static int ActiveCircuitsForUser(string userKey) =>
            _circuitsByUser.TryGetValue(userKey, out var n) ? n : 0;

        // ── Which symbols a user actually has on screen ──────────────────────
        //
        // The hosted monitor used to skip a user ENTIRELY while any of their circuits was
        // connected, handing ownership to the in-session pipeline. But the in-session
        // pipeline only evaluates alerts whose Symbol matches the chart on screen, and
        // BackgroundWorkspaceMonitor covers other open TABS only, is opt-in, and is gated
        // to desktop. Net effect on the hosted terminal: an alert on a symbol with no tab
        // open was evaluated by NOBODY while the browser was connected — so **closing your
        // browser made more of your alerts work than leaving it open.**
        //
        // Suppression is now per SYMBOL rather than per user: the in-session pipeline keeps
        // the alerts it can genuinely see, and the server takes the rest.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<
            string, System.Collections.Concurrent.ConcurrentDictionary<string, string>> _symbolsByUser
            = new(StringComparer.Ordinal);

        /// <summary>
        /// Symbols this user's live circuits currently have on screen, upper-cased for the
        /// case-insensitive comparison the alert pipeline uses. Empty when they are offline —
        /// which correctly means "suppress nothing".
        /// </summary>
        internal static IReadOnlySet<string> OnScreenSymbolsForUser(string userKey)
        {
            if (!_symbolsByUser.TryGetValue(userKey, out var perCircuit))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return perCircuit.Values
                .Where(v => !string.IsNullOrEmpty(v))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Test seam: forget every recorded on-screen symbol.</summary>
        internal static void ResetOnScreenSymbolsForTests() => _symbolsByUser.Clear();

        /// <summary>Test seam: record a symbol as on screen for a user's circuit.</summary>
        internal static void RecordOnScreenSymbol(string userKey, string circuitId, string? symbol)
        {
            var perCircuit = _symbolsByUser.GetOrAdd(userKey,
                _ => new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal));
            perCircuit[circuitId] = symbol ?? "";
        }

        private static void ForgetCircuitSymbol(string userKey, string circuitId)
        {
            if (!_symbolsByUser.TryGetValue(userKey, out var perCircuit)) return;
            perCircuit.TryRemove(circuitId, out _);
            if (perCircuit.IsEmpty) _symbolsByUser.TryRemove(userKey, out _);
        }

        /// <summary>Live subscription to this circuit's workspace state, tracking the symbol
        /// on screen. Disposed with the circuit.</summary>
        private IDisposable? _symbolWatch;
        private string? _circuitId;

        /// <summary>Live browser sessions on this process. The local background
        /// monitor pauses while any session is connected (the in-session alert
        /// pipeline owns delivery then — same Orca, would double-speak).</summary>
        internal static int ActiveCircuits => _activeCircuits;

        private readonly ILogger<WebHostBrowserCircuitHandler> _logger;
        private readonly IServiceProvider _scope;

        // NOTHING user-scoped may be a constructor parameter here, and IShortcutManager used to
        // be one. An object exists before its methods run, so a constructor dependency is built
        // before OnCircuitOpenedAsync can call ICurrentUser.Set — the shortcut manager resolved
        // its file path at that moment and every hosted user shared users/anon/shortcuts.json.
        // Resolve per-circuit services from _scope INSIDE OnCircuitOpenedAsync, after Set.
        // PerUserPathPolicyTests.TheCircuitHandlerTakesNothingUserScopedInItsConstructor pins it.
        public WebHostBrowserCircuitHandler(
            ILogger<WebHostBrowserCircuitHandler> logger,
            IServiceProvider scope)
        {
            _logger = logger;
            _scope = scope;   // the circuit's scoped provider — resolve optional account services from it
        }

        public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            int now = System.Threading.Interlocked.Increment(ref _activeCircuits);
            _logger.LogInformation("Browser circuit opened ({Active} active).", now);

            // Hosted accounts: capture "who is this visitor" into the per-circuit ICurrentUser
            // so UserScopedPathService routes their data directory. Resolved optionally — when
            // accounts are off these services aren't registered and this is a no-op.
            try
            {
                if (_scope.GetService<ICurrentUser>() is CurrentUser current)
                {
                    var authProvider = _scope.GetService<AuthenticationStateProvider>();
                    if (authProvider != null)
                    {
                        var state = await authProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
                        current.Set(state.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                        if (current.IsAuthenticated)
                        {
                            _userKey = current.DataKey;
                            _circuitsByUser.AddOrUpdate(_userKey, 1, (_, n) => n + 1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Per-circuit current-user capture failed.");
            }

            try
            {
                // Resolved here, not injected: this is the first moment the shortcut manager can
                // safely learn which user's shortcuts.json it owns. See the constructor note.
                var shortcuts = _scope.GetService<IShortcutManager>();
                if (shortcuts != null)
                    WebHostShortcutRemap.ApplyBrowserHostOverrides(shortcuts, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Per-circuit browser shortcut remap failed.");
            }

            // Force-create the in-session alert recorder so it subscribes to THIS circuit's
            // event bus for its lifetime (local Full mode only — null otherwise). The scope
            // holds it, so its subscription lives until the circuit closes.
            try { _scope.GetService<InSessionAlertRecorder>(); }
            catch (Exception ex) { _logger.LogDebug(ex, "In-session alert recorder init skipped."); }

            // Bind this tab to the user's shared paper account. Force-created for the same reason
            // as the recorder above: the account has to be watching THIS chart from the moment the
            // circuit opens, not from whenever something first asks for the broker — otherwise a
            // resting order stops being evaluated as soon as another tab takes focus.
            try { _scope.GetService<PaperAccountAttachment>(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Paper account attach skipped."); }

            // Track which symbol THIS circuit is showing, so the hosted alert monitor can
            // suppress per symbol instead of per user. See OnScreenSymbolsForUser.
            try
            {
                if (_userKey != null && _scope.GetService<IWorkspaceStore>() is { } store)
                {
                    _circuitId = circuit.Id;
                    var key = _userKey;
                    var id = _circuitId;
                    RecordOnScreenSymbol(key, id, store.State.SymbolDisplayName);
                    _symbolWatch = store.StateStream.Subscribe(
                        st => RecordOnScreenSymbol(key, id, st.SymbolDisplayName));
                }
            }
            catch (Exception ex)
            {
                // Failing to track means the server evaluates an alert the in-session pipeline
                // may also see — a possible duplicate, which is far better than the silence
                // this replaces.
                _logger.LogDebug(ex, "On-screen symbol tracking could not start for this circuit.");
            }
        }
        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            int now = System.Threading.Interlocked.Decrement(ref _activeCircuits);
            _logger.LogInformation("Browser circuit closed ({Active} active).", now);

            _symbolWatch?.Dispose();
            _symbolWatch = null;

            if (_userKey != null)
            {
                var key = _userKey;
                if (_circuitId != null) ForgetCircuitSymbol(key, _circuitId);
                _circuitId = null;
                _userKey = null;
                while (_circuitsByUser.TryGetValue(key, out var n))
                {
                    if (n <= 1)
                    {
                        if (_circuitsByUser.TryRemove(new KeyValuePair<string, int>(key, n))) break;
                    }
                    else if (_circuitsByUser.TryUpdate(key, n - 1, n)) break;
                }
            }

            // Final session snapshot before the circuit's scoped services die —
            // a browser refresh was previously a destructive act that lost every
            // unsaved tab, drawing, and indicator stack.
            try
            {
                (_scope.GetService(typeof(AccessibleTrader.Core.Services.Workspace.ISessionAutosaveService))
                    as AccessibleTrader.Core.Services.Workspace.ISessionAutosaveService)?.SaveNow();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Session autosave on circuit close failed (scope may already be disposed).");
            }
            return Task.CompletedTask;
        }

    }
}
