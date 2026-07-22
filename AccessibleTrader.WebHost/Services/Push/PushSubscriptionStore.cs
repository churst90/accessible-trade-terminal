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

        private string PathFor(string userKey) =>
            Path.Combine(_usersRoot, userKey, "push_subscriptions.json");

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
