using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The hosted Web Push minimal-API endpoints over HTTP: authentication is
/// enforced, bodies are validated, and — the one the TODO called out — user A
/// cannot touch user B's subscriptions, because the store keys strictly off
/// the caller's own identity claim, never off anything in the request body.
/// </summary>
public sealed class WebHostPushEndpointsIntegrationTests : IClassFixture<HostedWebHostFixture>
{
    private readonly HostedWebHostFixture _host;

    public WebHostPushEndpointsIntegrationTests(HostedWebHostFixture host) => _host = host;

    private const string Password = "Correct-h0rse-battery";

    private static object Subscription(string endpoint) => new
    {
        endpoint,
        p256dh = "test-p256dh-key",
        auth = "test-auth-secret",
    };

    private string SubscriptionFileFor(string userId)
        => Path.Combine(_host.DataRoot, "users", userId, "push_subscriptions.json");

    private async Task<(HttpClient Client, string UserId)> SignedInClientAsync(string email)
    {
        var user = await WebHostIntegration.SeedUserAsync(_host.Factory, email, Password);
        var client = WebHostIntegration.NewClient(_host.Factory);
        var login = await WebHostIntegration.LoginAsync(client, email, Password);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        return (client, user.Id);
    }

    [Fact]
    public async Task Anonymous_callers_are_refused_everywhere()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);

        var key = await client.GetAsync("/terminal/push/vapid-public-key");
        Assert.Equal(HttpStatusCode.Found, key.StatusCode);
        Assert.StartsWith("/terminal/account/login", key.Headers.Location!.AbsolutePath);

        var subscribe = await client.PostAsJsonAsync("/terminal/push/subscribe",
            Subscription("https://push.example/anon"));
        Assert.NotEqual(HttpStatusCode.OK, subscribe.StatusCode);
    }

    [Fact]
    public async Task Subscribe_requires_a_plausible_subscription_body()
    {
        var (client, userId) = await SignedInClientAsync("push-validate@example.test");
        using (client)
        {
            // http:// endpoint → the store refuses it.
            var insecure = await client.PostAsJsonAsync("/terminal/push/subscribe",
                Subscription("http://push.example/insecure"));
            Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
            Assert.False(File.Exists(SubscriptionFileFor(userId)),
                "a rejected subscription must not create the user's subscription file");
        }
    }

    [Fact]
    public async Task Subscribe_stores_per_user_and_unsubscribe_cannot_cross_users()
    {
        var (alice, aliceId) = await SignedInClientAsync("push-alice@example.test");
        var (bob, bobId) = await SignedInClientAsync("push-bob@example.test");
        using (alice)
        using (bob)
        {
            const string aliceEndpoint = "https://push.example/alice-device";

            var subscribed = await alice.PostAsJsonAsync("/terminal/push/subscribe",
                Subscription(aliceEndpoint));
            Assert.Equal(HttpStatusCode.OK, subscribed.StatusCode);

            var aliceFile = SubscriptionFileFor(aliceId);
            Assert.True(File.Exists(aliceFile), "subscription must persist under the OWNER's user directory");
            Assert.Contains(aliceEndpoint, await File.ReadAllTextAsync(aliceFile));

            // Bob "unsubscribes" Alice's endpoint. The endpoint answers 200
            // (it keys the removal off Bob's own identity and stays quiet
            // about other users), but Alice's subscription must survive.
            var crossUser = await bob.PostAsJsonAsync("/terminal/push/unsubscribe",
                Subscription(aliceEndpoint));
            Assert.Equal(HttpStatusCode.OK, crossUser.StatusCode);
            Assert.Contains(aliceEndpoint, await File.ReadAllTextAsync(aliceFile));
            Assert.False(File.Exists(SubscriptionFileFor(bobId)));

            // Alice unsubscribing herself is what actually removes it.
            var selfRemove = await alice.PostAsJsonAsync("/terminal/push/unsubscribe",
                Subscription(aliceEndpoint));
            Assert.Equal(HttpStatusCode.OK, selfRemove.StatusCode);
            Assert.DoesNotContain(aliceEndpoint, await File.ReadAllTextAsync(aliceFile));
        }
    }

    [Fact]
    public async Task Vapid_public_key_is_stable_per_instance()
    {
        var (client, _) = await SignedInClientAsync("push-vapid@example.test");
        using (client)
        {
            string first = await client.GetStringAsync("/terminal/push/vapid-public-key");
            string second = await client.GetStringAsync("/terminal/push/vapid-public-key");
            Assert.False(string.IsNullOrWhiteSpace(first));
            Assert.Equal(first, second); // persisted instance key, not re-minted per request
            Assert.True(File.Exists(Path.Combine(_host.DataRoot, "vapid-keys.json")));
        }
    }
}
