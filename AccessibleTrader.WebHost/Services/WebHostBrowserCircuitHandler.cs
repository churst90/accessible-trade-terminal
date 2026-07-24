using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        /// <summary>Live browser sessions on this process. The local background
        /// monitor pauses while any session is connected (the in-session alert
        /// pipeline owns delivery then — same Orca, would double-speak).</summary>
        internal static int ActiveCircuits => _activeCircuits;

        private readonly IShortcutManager _shortcuts;
        private readonly ILogger<WebHostBrowserCircuitHandler> _logger;
        private readonly IServiceProvider _scope;

        public WebHostBrowserCircuitHandler(
            IShortcutManager shortcuts,
            ILogger<WebHostBrowserCircuitHandler> logger,
            IServiceProvider scope)
        {
            _shortcuts = shortcuts;
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
                WebHostShortcutRemap.ApplyBrowserHostOverrides(_shortcuts, _logger);
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
        }
        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            int now = System.Threading.Interlocked.Decrement(ref _activeCircuits);
            _logger.LogInformation("Browser circuit closed ({Active} active).", now);

            if (_userKey != null)
            {
                var key = _userKey;
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
