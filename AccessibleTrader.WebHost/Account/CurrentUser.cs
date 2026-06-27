namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// Per-circuit holder for "who is this visitor". Scoped, so each browser circuit gets
    /// its own. Populated once when the circuit opens (see WebHostBrowserCircuitHandler)
    /// from the authenticated principal. Read by <see cref="Services.UserScopedPathService"/>
    /// to route per-user data directories.
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

    public sealed class CurrentUser : ICurrentUser
    {
        public bool IsAuthenticated { get; private set; }
        public string? UserId { get; private set; }
        public string DataKey { get; private set; } = "anon";

        /// <summary>Set once per circuit by the circuit handler. Null = anonymous.</summary>
        public void Set(string? userId)
        {
            UserId = userId;
            IsAuthenticated = !string.IsNullOrEmpty(userId);
            DataKey = IsAuthenticated ? userId! : "anon";
        }
    }
}
