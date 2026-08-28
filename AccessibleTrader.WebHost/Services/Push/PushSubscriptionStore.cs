using System.Text.Json;

namespace AccessibleTrader.WebHost.Services.Push
{
    /// <summary>One browser's push subscription as the client hands it to us.</summary>
    public sealed class StoredPushSubscription
    {
        public string Endpoint { get; set; } = "";
        public string P256dh { get; set; } = "";
        public string Auth { get; set; } = "";
    }

    /// <summary>
    /// Per-user Web Push subscriptions, one JSON file per user beside their
    /// other data ({usersRoot}/{userKey}/push_subscriptions.json). A user can
    /// hold several (phone + desktop browser); deduped by endpoint. Endpoints
    /// that the push service reports gone (404/410) are pruned by the sender.
    /// </summary>
    public sealed class PushSubscriptionStore
    {
        private const int MaxSubscriptionsPerUser = 8;

        private readonly string _usersRoot;
        private readonly ILogger<PushSubscriptionStore> _logger;
        private readonly object _gate = new();

        public PushSubscriptionStore(string usersRoot, ILogger<PushSubscriptionStore> logger)
        {
            _usersRoot = usersRoot;
            _logger = logger;
        }

        /// <summary>
        /// Same sanitisation <c>UserScopedPathService.Sanitize</c> applies to the same
        /// value. Not exploitable today — the key is the Identity GUID read off the auth
        /// cookie, never anything from a request body — but this class concatenated it
        /// into a path raw while its sibling stripped it, and an invariant that holds in
        /// one place and not the other is a bug waiting for its first caller.
        /// </summary>
        internal static string SanitizeUserKey(string userKey)
        {
            var clean = new string((userKey ?? "anon").Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
            return string.IsNullOrEmpty(clean) ? "anon" : clean;
        }

        private string PathFor(string userKey) =>
            Path.Combine(_usersRoot, SanitizeUserKey(userKey), "push_subscriptions.json");

        public IReadOnlyList<StoredPushSubscription> List(string userKey)
        {
            lock (_gate)
            {
                return Load(userKey);
            }
        }

        /// <summary>Adds (or refreshes) a subscription; endpoint is the identity.
        /// Returns false when the payload is unusable.</summary>
        public bool Add(string userKey, StoredPushSubscription subscription)
        {
            if (string.IsNullOrWhiteSpace(subscription.Endpoint)
                || !subscription.Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(subscription.P256dh)
                || string.IsNullOrWhiteSpace(subscription.Auth))
                return false;

            if (!IsPlausiblePushEndpoint(subscription.Endpoint)) return false;

            lock (_gate)
            {
                var subs = Load(userKey);
                var next = subs.Where(s => !string.Equals(s.Endpoint, subscription.Endpoint, StringComparison.Ordinal)).ToList();
                next.Add(subscription);
                // Oldest-out past the cap: a user re-subscribing across many
                // browser reinstalls must not accumulate dead endpoints forever.
                while (next.Count > MaxSubscriptionsPerUser) next.RemoveAt(0);
                Save(userKey, next);
                return true;
            }
        }

        /// <summary>
        /// Refuses an endpoint whose host is an IP literal outside the public internet.
        ///
        /// <para>
        /// <c>/push/subscribe</c> validated only that the string began with <c>https://</c>,
        /// so any signed-in user could aim the server's push sender at
        /// <c>https://10.0.0.5:6379/</c> or <c>https://169.254.169.254/</c> — user-controlled
        /// outbound traffic from a public server, once per fired alert. It is a weak
        /// primitive (blind, and the body is push-encrypted), but the codebase's own
        /// established answer for a user-chosen target is
        /// <see cref="AccessibleTrader.Core.Services.Alerts.OutboundNetworkGuard"/>, and the
        /// alert channels reason about exactly this case.
        /// </para>
        ///
        /// <para>
        /// Only IP literals are decided here, deliberately: a name needs DNS, and a
        /// resolve-now/connect-later check is both a TOCTOU window and a network call on a
        /// request path. The real control is the connect-time guard inside
        /// <c>HostedWebPushSender</c>'s HttpClient, which resolves and validates the address
        /// it is about to connect to and so has nothing to rebind. This is the cheap,
        /// deterministic half that rejects the obvious attempt at the door.
        /// </para>
        /// </summary>
        internal static bool IsPlausiblePushEndpoint(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;

            if (System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal))
                return AccessibleTrader.Core.Services.Alerts.OutboundNetworkGuard.IsPublic(literal);

            return true;   // a name — settled at connect time by the sender's guarded client
        }

        public void Remove(string userKey, string endpoint)
        {
            lock (_gate)
            {
                var subs = Load(userKey);
                var next = subs.Where(s => !string.Equals(s.Endpoint, endpoint, StringComparison.Ordinal)).ToList();
                if (next.Count != subs.Count) Save(userKey, next);
            }
        }

        private List<StoredPushSubscription> Load(string userKey)
        {
            try
            {
                var path = PathFor(userKey);
                if (!File.Exists(path)) return new List<StoredPushSubscription>();
                return JsonSerializer.Deserialize<List<StoredPushSubscription>>(File.ReadAllText(path))
                       ?? new List<StoredPushSubscription>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push subscriptions unreadable for {User}; treating as none.", userKey);
                return new List<StoredPushSubscription>();
            }
        }

        private void Save(string userKey, List<StoredPushSubscription> subs)
        {
            try
            {
                var path = PathFor(userKey);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                AccessibleTrader.Core.Services.AtomicFile.WriteAllText(path,
                    JsonSerializer.Serialize(subs, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push subscription save failed for {User}.", userKey);
            }
        }
    }
}
