using System.Security.Claims;

namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// Per-circuit holder for "who is this visitor". Scoped, so each browser circuit gets
    /// its own. Populated when the circuit opens (see WebHostBrowserCircuitHandler) from
    /// the authenticated principal, and — for scopes that are NOT a circuit — resolved from
    /// the ambient <see cref="IHttpContextAccessor"/>. Read by
    /// <see cref="Services.UserScopedPathService"/> to route per-user data directories.
    /// </summary>
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }

        /// <summary>The stable Identity user id (a GUID string), or null when anonymous.</summary>
        string? UserId { get; }

        /// <summary>
        /// A filesystem-safe key for this user's data directory: the UserId when
        /// authenticated, or "anon" for anonymous/demo circuits (non-persistent).
        /// </summary>
        string DataKey { get; }
    }

    /// <summary>
    /// <para>
    /// <b>Why the HttpContext fallback exists.</b> <c>Set</c> is called in exactly two
    /// places: <c>WebHostBrowserCircuitHandler</c> (a Blazor circuit) and
    /// <c>HostedAlertMonitor</c> (a background per-user scope). A <b>Razor Page</b>
    /// request is neither — it gets a fresh DI scope in which nothing ever called
    /// <c>Set</c>, so <c>DataKey</c> was <c>"anon"</c>, so
    /// <c>UserScopedPathService.AppDataDirectory</c> resolved to
    /// <c>{dataRoot}/users/anon</c>.
    /// </para>
    /// <para>
    /// Every authentication audit event this app records is written from a Razor Page —
    /// sign-in success and failure, lockout, registration, 2FA enable/disable, recovery
    /// code use, password-reset request and password reset. All of them, for all users,
    /// landed in the one shared <c>users/anon/SecurityEvents/</c> file, complete with
    /// email address and client IP. An operator investigating a single account found
    /// nothing; every user's PII was pooled in a directory whose name asserts it holds
    /// no user's data; and <c>HostedAlertMonitor.EnumerateUserKeys</c> skips <c>anon</c>,
    /// so nothing ever pruned it either.
    /// </para>
    /// <para>
    /// The fallback is deliberately second, not first: an explicit <c>Set</c> — including
    /// <c>Set(null)</c> for an anonymous circuit — always wins, because a circuit's
    /// identity is fixed at circuit-open and must not start tracking whatever HTTP
    /// request happens to be in flight on the same scope.
    /// </para>
    /// </summary>
    public sealed class CurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor? _http;
        private bool _explicitlySet;
        private string? _explicitUserId;

        /// <summary>
        /// The accessor is optional so tests (and any host that does not register one)
        /// can construct this directly; without it the behaviour is exactly the old
        /// "anon until Set is called".
        /// </summary>
        public CurrentUser(IHttpContextAccessor? http = null) => _http = http;

        public bool IsAuthenticated => !string.IsNullOrEmpty(Resolve());
        public string? UserId => Resolve();
        public string DataKey => Resolve() is { Length: > 0 } id ? id : "anon";

        /// <summary>Set once per circuit by the circuit handler. Null = anonymous.</summary>
        public void Set(string? userId)
        {
            _explicitlySet = true;
            _explicitUserId = userId;
        }

        private string? Resolve()
        {
            if (_explicitlySet) return _explicitUserId;
            return _http?.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
