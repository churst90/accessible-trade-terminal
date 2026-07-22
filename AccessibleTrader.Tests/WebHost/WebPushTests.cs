using System;
using System.IO;
using System.Linq;
using AccessibleTrader.WebHost.Services.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests.WebHost
{
    /// <summary>
    /// Web Push plumbing for hosted alerts: VAPID keypair generation and
    /// persistence, and the per-user subscription store.
    /// </summary>
    public class WebPushTests
    {
        // ── VAPID keys ───────────────────────────────────────────────────────

        [Fact]
        public void Generated_vapid_keys_are_spec_shaped_base64url()
        {
            var (publicKey, privateKey) = VapidKeyService.Generate();

            // Uncompressed P-256 point = 65 bytes → 87 base64url chars; scalar = 32 → 43.
            Assert.Equal(87, publicKey.Length);
            Assert.Equal(43, privateKey.Length);
            Assert.DoesNotContain('+', publicKey);
            Assert.DoesNotContain('/', publicKey);
            Assert.DoesNotContain('=', publicKey);
            // The uncompressed-point marker (0x04) base64urls to a leading 'B'.
            Assert.StartsWith("B", publicKey);
        }

        [Fact]
        public void Keys_persist_across_service_instances()
        {
            var root = Directory.CreateTempSubdirectory("att-vapid-").FullName;
            try
            {
                var first = new VapidKeyService(root, NullLogger<VapidKeyService>.Instance);
                var publicKey = first.PublicKey;

                var second = new VapidKeyService(root, NullLogger<VapidKeyService>.Instance);
                // A regenerated pair would orphan every browser subscription.
                Assert.Equal(publicKey, second.PublicKey);
                Assert.Equal(first.PrivateKey, second.PrivateKey);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        // ── Subscription store ───────────────────────────────────────────────

        private static StoredPushSubscription Sub(string endpoint = "https://push.example/ep1") => new()
        {
            Endpoint = endpoint,
            P256dh = "BPubKey",
            Auth = "authsecret",
        };

        [Fact]
        public void Add_dedupes_by_endpoint_and_isolates_users()
        {
            var root = Directory.CreateTempSubdirectory("att-push-").FullName;
            try
            {
                var store = new PushSubscriptionStore(root, NullLogger<PushSubscriptionStore>.Instance);

                Assert.True(store.Add("user-a", Sub()));
                Assert.True(store.Add("user-a", Sub())); // same endpoint → refreshed, not doubled
                Assert.True(store.Add("user-a", Sub("https://push.example/ep2")));
                Assert.True(store.Add("user-b", Sub("https://push.example/other")));

                Assert.Equal(2, store.List("user-a").Count);
                Assert.Single(store.List("user-b"));
                Assert.Empty(store.List("user-c"));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void Malformed_subscriptions_are_rejected()
        {
            var root = Directory.CreateTempSubdirectory("att-push-").FullName;
            try
            {
                var store = new PushSubscriptionStore(root, NullLogger<PushSubscriptionStore>.Instance);

                Assert.False(store.Add("u", new StoredPushSubscription()));
                Assert.False(store.Add("u", Sub(endpoint: "http://insecure.example/ep"))); // https only
                Assert.False(store.Add("u", new StoredPushSubscription
                {
                    Endpoint = "https://push.example/ep", P256dh = "", Auth = "a",
                }));
                Assert.Empty(store.List("u"));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void Remove_prunes_one_endpoint_and_persists()
        {
            var root = Directory.CreateTempSubdirectory("att-push-").FullName;
            try
            {
                var store = new PushSubscriptionStore(root, NullLogger<PushSubscriptionStore>.Instance);
                store.Add("u", Sub("https://push.example/ep1"));
                store.Add("u", Sub("https://push.example/ep2"));

                store.Remove("u", "https://push.example/ep1");

                var reloaded = new PushSubscriptionStore(root, NullLogger<PushSubscriptionStore>.Instance);
                Assert.Equal("https://push.example/ep2", reloaded.List("u").Single().Endpoint);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void Subscriptions_cap_sheds_the_oldest()
        {
            var root = Directory.CreateTempSubdirectory("att-push-").FullName;
            try
            {
                var store = new PushSubscriptionStore(root, NullLogger<PushSubscriptionStore>.Instance);
                for (int i = 0; i < 12; i++)
                    store.Add("u", Sub($"https://push.example/ep{i}"));

                var subs = store.List("u");
                Assert.Equal(8, subs.Count);
                Assert.Equal("https://push.example/ep4", subs[0].Endpoint); // 0..3 shed
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }
}
