using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Tests.Fakes;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Webhook alert channel: HTTPS-only gate, Discord ("content") + Slack ("text")
    /// dual-compatibility payload, optional auth header, and error propagation to
    /// the delivery service's catch (throw, don't swallow).
    /// </summary>
    public class WebhookAlertChannelTests
    {
        private static AlertFired SampleAlert() => new(
            new AlertDefinition
            {
                Id = "a1",
                Name = "Gold crossed 2500",
                Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove,
                Delivery = AlertDelivery.Both,
            },
            TriggeringValue: 2501.5,
            PreviousValue: 2498.0,
            SpeechText: "Gold crossed above 2500.");

        private static WebhookAlertChannel Build(FakeHttpMessageHandler handler, WebhookAlertChannelConfig? cfg)
            => new(new HttpClient(handler), () => cfg);

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("not a url", false)]
        [InlineData("http://insecure.example.com/hook", false)] // alerts may carry position info
        [InlineData("https://discord.com/api/webhooks/1/abc", true)]
        public void IsConfigured_RequiresValidHttpsUrl(string? url, bool expected)
        {
            var channel = Build(new FakeHttpMessageHandler(),
                url == null ? null : new WebhookAlertChannelConfig { WebhookUrl = url });
            Assert.Equal(expected, channel.IsConfigured);
        }

        [Fact]
        public async Task SendAsync_PostsDiscordAndSlackCompatibleJson()
        {
            // The channel disposes the request after sending, so the body must be
            // captured inside the responder, at send time.
            string? body = null;
            string? authHeader = null;
            var handler = new FakeHttpMessageHandler()
                .Add(HttpMethod.Post, @"discord\.com/api/webhooks", req =>
                {
                    body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    authHeader = req.Headers.Authorization?.ToString();
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("{}") };
                });
            var channel = Build(handler, new WebhookAlertChannelConfig
            {
                WebhookUrl = "https://discord.com/api/webhooks/1/abc",
                AuthHeader = "Bearer tok123",
            });

            await channel.SendAsync(SampleAlert());

            Assert.Equal("Bearer tok123", authHeader);
            Assert.NotNull(body);
            Assert.Contains("\"content\":", body);                 // Discord reads this
            Assert.Contains("\"text\":", body);                    // Slack reads this
            Assert.Contains("Gold crossed 2500", body);
            Assert.Contains("\"triggering_value\":2501.5", body);  // custom endpoints get structure
        }

        [Fact]
        public async Task SendAsync_HttpError_Throws_SoDeliveryServiceLogsIt()
        {
            var handler = new FakeHttpMessageHandler()
                .Post(@"discord\.com", "rate limited", HttpStatusCode.TooManyRequests);
            var channel = Build(handler, new WebhookAlertChannelConfig
            {
                WebhookUrl = "https://discord.com/api/webhooks/1/abc",
            });

            await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(SampleAlert()));
        }
    }
}
