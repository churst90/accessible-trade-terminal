using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace AccessibleTrader.WebHost.Services.Push
{
    /// <summary>
    /// Delivers hosted alert notifications over Web Push (VAPID). Fire-and-log:
    /// a dead push service must never stall alert evaluation. Subscriptions the
    /// push service reports permanently gone (404/410 — browser reinstalled,
    /// permission revoked) are pruned from the store so they aren't retried
    /// every poll forever.
    /// </summary>
    /// <summary>
    /// Web Push to every subscription one user holds. Seam for the callers that fan out
    /// through it — the alert monitor and the password-reset notifier — so a test can watch
    /// what would have been pushed without VAPID keys or a push service.
    /// </summary>
    public interface IWebPushSender
    {
        Task SendToUserAsync(string userKey, string title, string body, CancellationToken ct);
    }

    public sealed class HostedWebPushSender : IWebPushSender
    {
        private readonly PushServiceClient _client;
        private readonly PushSubscriptionStore _store;
        private readonly ILogger<HostedWebPushSender> _logger;

        public HostedWebPushSender(
            VapidKeyService vapid,
            PushSubscriptionStore store,
            ILogger<HostedWebPushSender> logger,
            AccessibleTrader.Core.Services.DemoPolicy? policy = null)
        {
            _store = store;
            _logger = logger;
            // The endpoint is a URL the USER's browser handed us, which makes it the same
            // shape of target as an alert webhook — so it gets the same client. Before this
            // it was a bare `new HttpClient()`: no outbound guard, no redirect refusal, no
            // timeout, and a signed-in user could aim it at loopback, the private network
            // or the cloud metadata service. AlertChannelHttpClient resolves and validates
            // inside ConnectCallback, so the address the socket reaches is the address that
            // was checked — a DNS record that flips after validation has nothing to rebind.
            _client = new PushServiceClient(
                AccessibleTrader.Core.Services.Alerts.AlertChannelHttpClient.Create(
                    blockPrivateNetworks: policy?.BlockPrivateNetworkTargets ?? true))
            {
                DefaultAuthentication = new VapidAuthentication(vapid.PublicKey, vapid.PrivateKey)
                {
                    Subject = VapidKeyService.Subject,
                },
            };
        }

        public async Task SendToUserAsync(string userKey, string title, string body, CancellationToken ct)
        {
            var subscriptions = _store.List(userKey);
            if (subscriptions.Count == 0) return;

            string payload = JsonSerializer.Serialize(new { title, body });

            foreach (var stored in subscriptions)
            {
                ct.ThrowIfCancellationRequested();
                var subscription = new PushSubscription { Endpoint = stored.Endpoint };
                subscription.SetKey(PushEncryptionKeyName.P256DH, stored.P256dh);
                subscription.SetKey(PushEncryptionKeyName.Auth, stored.Auth);

                try
                {
                    var message = new PushMessage(payload)
                    {
                        Urgency = PushMessageUrgency.High, // an alert IS the urgent case
                        TimeToLive = (int)TimeSpan.FromHours(4).TotalSeconds,
                    };
                    await _client.RequestPushMessageDeliveryAsync(subscription, message, ct);
                }
                catch (PushServiceClientException ex) when (
                    ex.StatusCode == System.Net.HttpStatusCode.NotFound
                    || ex.StatusCode == System.Net.HttpStatusCode.Gone)
                {
                    _logger.LogInformation("Push endpoint gone for {User}; pruning.", userKey);
                    _store.Remove(userKey, stored.Endpoint);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Web Push delivery failed for {User}.", userKey);
                }
            }
        }
    }
}
