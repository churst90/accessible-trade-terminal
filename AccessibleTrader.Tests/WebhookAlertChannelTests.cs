using System.Net;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Webhook alert channel: HTTPS-only gate, per-asset routing (post only to the
    /// webhook whose Name == the alert's WebhookTarget), Discord ("content") + Slack
    /// ("text") dual-compatibility payload, optional per-webhook auth header, and error
    /// propagation to the delivery service's catch. Plus the legacy-URL migration.
    /// </summary>
    public class WebhookAlertChannelTests
    {
        private static AlertFired SampleAlert(string? webhookTarget = "Default", string? symbol = "XAU/USD") => new(
            new AlertDefinition
            {
                Id = "a1",
                Name = "Gold crossed 2500",
                Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove,
                Delivery = AlertDelivery.Both,
                WebhookTarget = webhookTarget,
            },
            TriggeringValue: 2501.5,
            PreviousValue: 2498.0,
            SpeechText: "Gold crossed above 2500.",
            Symbol: symbol);

        private static WebhookAlertChannel Build(FakeHttpMessageHandler handler, WebhookAlertChannelConfig? cfg)
            => new(new HttpClient(handler), () => cfg);

        private static WebhookAlertChannel Build(FakeHttpMessageHandler handler, WebhookAlertChannelConfig? cfg,
            AccessibleTrader.Tests.Mocks.SpyEventBus bus)
            => new(new HttpClient(handler), () => cfg, logger: null, eventBus: bus);

        private static List<AccessibleTrader.Core.Models.FeedbackRequestEvent> Warnings(
            AccessibleTrader.Tests.Mocks.SpyEventBus bus)
        {
            var list = new List<AccessibleTrader.Core.Models.FeedbackRequestEvent>();
            foreach (var e in bus.Log)
                if (e is AccessibleTrader.Core.Models.FeedbackRequestEvent f
                    && f.Type == AccessibleTrader.Core.Models.FeedbackType.Error)
                    list.Add(f);
            return list;
        }

        private static WebhookAlertChannelConfig Cfg(params NamedWebhook[] hooks)
            => new() { Webhooks = hooks };

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("not a url", false)]
        [InlineData("http://insecure.example.com/hook", false)] // alerts may carry position info
        [InlineData("https://discord.com/api/webhooks/1/abc", true)]
        public void IsConfigured_RequiresAtLeastOneValidHttpsWebhook(string? url, bool expected)
        {
            var cfg = url == null
                ? Cfg() // no webhooks at all
                : Cfg(new NamedWebhook { Name = "Default", Url = url });
            var channel = Build(new FakeHttpMessageHandler(), cfg);
            Assert.Equal(expected, channel.IsConfigured);
        }

        [Fact]
        public async Task SendAsync_PostsDiscordAndSlackCompatibleJson_WithSymbolAndCondition()
        {
            string? body = null;
            string? authHeader = null;
            var handler = new FakeHttpMessageHandler()
                .Add(HttpMethod.Post, @"discord\.com/api/webhooks", req =>
                {
                    body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    authHeader = req.Headers.Authorization?.ToString();
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                });
            var channel = Build(handler, Cfg(new NamedWebhook
            {
                Name = "Default",
                Url = "https://discord.com/api/webhooks/1/abc",
                AuthHeader = "Bearer tok123",
            }));

            await channel.SendAsync(SampleAlert());

            Assert.Equal("Bearer tok123", authHeader);
            Assert.NotNull(body);
            Assert.Contains("\"content\":", body);                 // Discord reads this
            Assert.Contains("\"text\":", body);                    // Slack reads this
            Assert.Contains("Gold crossed 2500", body);
            Assert.Contains("\"triggering_value\":2501.5", body);  // custom endpoints get structure
            Assert.Contains("XAU/USD", body);                      // symbol travels in the payload
            Assert.Contains("\"condition\":\"CrossesAbove\"", body);
        }

        [Fact]
        public async Task SendAsync_RoutesToTheWebhookNamedByWebhookTarget()
        {
            // Two webhooks configured; the alert targets "KAS channel" so only the KAS
            // endpoint must receive the POST — the BTC endpoint stays untouched.
            bool btcHit = false, kasHit = false;
            var handler = new FakeHttpMessageHandler()
                .Add(HttpMethod.Post, @"hooks\.example\.com/btc", _ => { btcHit = true; return new HttpResponseMessage(HttpStatusCode.OK); })
                .Add(HttpMethod.Post, @"hooks\.example\.com/kas", _ => { kasHit = true; return new HttpResponseMessage(HttpStatusCode.OK); });
            var channel = Build(handler, Cfg(
                new NamedWebhook { Name = "BTC channel", Url = "https://hooks.example.com/btc" },
                new NamedWebhook { Name = "KAS channel", Url = "https://hooks.example.com/kas" }));

            await channel.SendAsync(SampleAlert(webhookTarget: "KAS channel"));

            Assert.False(btcHit);
            Assert.True(kasHit);
        }

        [Fact]
        public async Task SendAsync_NullWebhookTarget_DoesNotPost()
        {
            bool hit = false;
            var handler = new FakeHttpMessageHandler()
                .Add(HttpMethod.Post, @".*", _ => { hit = true; return new HttpResponseMessage(HttpStatusCode.OK); });
            var channel = Build(handler, Cfg(new NamedWebhook { Name = "Default", Url = "https://hooks.example.com/x" }));

            await channel.SendAsync(SampleAlert(webhookTarget: null));

            Assert.False(hit);
        }

        [Fact]
        public async Task SendAsync_UnknownTarget_DoesNotPost()
        {
            bool hit = false;
            var handler = new FakeHttpMessageHandler()
                .Add(HttpMethod.Post, @".*", _ => { hit = true; return new HttpResponseMessage(HttpStatusCode.OK); });
            var channel = Build(handler, Cfg(new NamedWebhook { Name = "BTC channel", Url = "https://hooks.example.com/btc" }));

            await channel.SendAsync(SampleAlert(webhookTarget: "does-not-exist"));

            Assert.False(hit);
        }

        [Fact]
        public async Task SendAsync_HttpError_Throws_SoDeliveryServiceLogsIt()
        {
            var handler = new FakeHttpMessageHandler()
                .Post(@"discord\.com", "rate limited", HttpStatusCode.TooManyRequests);
            var channel = Build(handler, Cfg(new NamedWebhook
            {
                Name = "Default",
                Url = "https://discord.com/api/webhooks/1/abc",
            }));

            await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(SampleAlert()));
        }

        [Fact]
        public async Task SendAsync_UnknownTarget_SpeaksAWarning_OncePerTarget()
        {
            // Regression fence for the dead-diagnostics bug: the missing-target warning
            // was unreachable because the event bus was never injected. With a bus wired,
            // a renamed/deleted target must speak exactly one Error feedback — and only
            // once even across repeated fires — so a blind user learns their alert broke.
            var bus = new AccessibleTrader.Tests.Mocks.SpyEventBus();
            var handler = new FakeHttpMessageHandler().Add(HttpMethod.Post, @".*",
                _ => new HttpResponseMessage(HttpStatusCode.OK));
            var channel = Build(handler,
                Cfg(new NamedWebhook { Name = "BTC channel", Url = "https://hooks.example.com/btc" }), bus);

            await channel.SendAsync(SampleAlert(webhookTarget: "was-renamed"));
            await channel.SendAsync(SampleAlert(webhookTarget: "was-renamed"));

            var warn = Assert.Single(Warnings(bus));
            Assert.Contains("was-renamed", warn.Message);
        }

        [Fact]
        public async Task SendAsync_HttpFailure_SpeaksAWarning_AndStillThrows()
        {
            // The delivery failure must both (a) reach the user's speech as an Error and
            // (b) still throw, so AlertDeliveryService's existing log + SecurityEvent path
            // runs unchanged.
            var bus = new AccessibleTrader.Tests.Mocks.SpyEventBus();
            var handler = new FakeHttpMessageHandler()
                .Post(@"discord\.com", "rate limited", HttpStatusCode.TooManyRequests);
            var channel = Build(handler, Cfg(new NamedWebhook
            {
                Name = "Default",
                Url = "https://discord.com/api/webhooks/1/abc",
            }), bus);

            await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(SampleAlert()));

            var warn = Assert.Single(Warnings(bus));
            Assert.Contains("Default", warn.Message);
            Assert.Contains("failing to deliver", warn.Message);
        }

        // ── Migration: legacy single alerts.webhook.url → {Name:"Default"} ──────────

        [Fact]
        public void ParseWebhooks_MigratesLegacySingleUrl_ToDefaultNamedWebhook()
        {
            var list = WebhookAlertConfigLoader.ParseWebhooks(
                webhooksToken: null,
                legacyUrl: "https://discord.com/api/webhooks/1/legacy",
                legacyAuth: "Bearer old");

            var only = Assert.Single(list);
            Assert.Equal("Default", only.Name);
            Assert.Equal("https://discord.com/api/webhooks/1/legacy", only.Url);
            Assert.Equal("Bearer old", only.AuthHeader);
        }

        [Fact]
        public void ParseWebhooks_PrefersNamedList_OverLegacyUrl()
        {
            var arr = new JArray
            {
                new JObject { ["name"] = "BTC channel", ["url"] = "https://hooks.example.com/btc" },
                new JObject { ["name"] = "KAS channel", ["url"] = "https://hooks.example.com/kas", ["authHeader"] = "Bearer k" },
            };

            var list = WebhookAlertConfigLoader.ParseWebhooks(arr, legacyUrl: "https://old/legacy", legacyAuth: null);

            Assert.Equal(2, list.Count);
            Assert.Equal("BTC channel", list[0].Name);
            Assert.Equal("KAS channel", list[1].Name);
            Assert.Equal("Bearer k", list[1].AuthHeader);
            // Legacy URL is ignored once a named list exists.
            Assert.DoesNotContain(list, w => w.Url == "https://old/legacy");
        }

        [Fact]
        public void ParseWebhooks_NoNamedList_NoLegacy_ReturnsEmpty()
        {
            var list = WebhookAlertConfigLoader.ParseWebhooks(null, null, null);
            Assert.Empty(list);
        }

        [Fact]
        public void ToJson_RoundTripsThroughParseWebhooks()
        {
            var original = new List<NamedWebhook>
            {
                new() { Name = "BTC channel", Url = "https://hooks.example.com/btc" },
                new() { Name = "KAS channel", Url = "https://hooks.example.com/kas", AuthHeader = "Bearer k" },
            };

            var json = WebhookAlertConfigLoader.ToJson(original);
            var back = WebhookAlertConfigLoader.ParseWebhooks(json, null, null);

            Assert.Equal(2, back.Count);
            Assert.Equal("BTC channel", back[0].Name);
            Assert.Equal("Bearer k", back[1].AuthHeader);
        }
    }
}
