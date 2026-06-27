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
        }
    }
}
