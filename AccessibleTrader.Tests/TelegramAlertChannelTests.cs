using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Telegram Bot API delivery: configuration gate (token AND chat id), the sendMessage
    /// payload, error propagation to the delivery service, and the two message-content
    /// hazards — user-authored alert names containing Markdown entity characters (which
    /// made the Bot API reject the whole message, so the alert never arrived) and the
    /// fixed :F6 value format (which read "0.000000" for sub-penny assets).
    /// </summary>
    public class TelegramAlertChannelTests
    {
        private static AlertFired SampleAlert(string name = "Gold crossed 2500", double value = 2501.5,
            string speech = "Gold crossed above 2500.") => new(
            new AlertDefinition
            {
                Id = "a1",
                Name = name,
                Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove,
                Delivery = AlertDelivery.Both,
            },
            TriggeringValue: value,
            PreviousValue: 2498.0,
            SpeechText: speech,
            Symbol: "XAU/USD");

        private static TelegramAlertChannel Build(FakeHttpMessageHandler handler, TelegramAlertChannelConfig? cfg)
            => new(new HttpClient(handler), () => cfg);

        private static FakeHttpMessageHandler CapturingHandler(Action<HttpRequestMessage, JObject> capture)
            => new FakeHttpMessageHandler().Add(HttpMethod.Post, @"api\.telegram\.org", req =>
            {
                var body = JObject.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                capture(req, body);
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"ok":true}""") };
            });

        [Theory]
        [InlineData(null, null, false)]
        [InlineData("123:token", null, false)]
        [InlineData("123:token", "  ", false)]
        [InlineData(null, "42", false)]
        [InlineData("", "42", false)]
        [InlineData("123:token", "42", true)]
        public void IsConfigured_RequiresBothBotTokenAndChatId(string? token, string? chatId, bool expected)
        {
            var channel = Build(new FakeHttpMessageHandler(),
                new TelegramAlertChannelConfig { BotToken = token, ChatId = chatId });
            Assert.Equal(expected, channel.IsConfigured);
        }

        [Fact]
        public void IsConfigured_NullConfig_IsFalse()
        {
            Assert.False(Build(new FakeHttpMessageHandler(), null).IsConfigured);
        }

        [Fact]
        public async Task SendAsync_Unconfigured_DoesNotPost()
        {
            // Strict handler: any HTTP request would throw.
            var handler = new FakeHttpMessageHandler();
            var channel = Build(handler, new TelegramAlertChannelConfig { BotToken = "123:token" });

            await channel.SendAsync(SampleAlert());

            Assert.Empty(handler.Captured);
        }

        [Fact]
        public async Task SendAsync_PostsToTheBotSendMessageEndpoint_WithChatIdAndMarkdown()
        {
            Uri? uri = null;
            JObject? body = null;
            var handler = CapturingHandler((req, b) => { uri = req.RequestUri; body = b; });
            var channel = Build(handler, new TelegramAlertChannelConfig { BotToken = "123:secret", ChatId = "4242" });

            await channel.SendAsync(SampleAlert());

            Assert.Equal("https://api.telegram.org/bot123:secret/sendMessage", uri!.ToString());
            Assert.Equal("4242", (string?)body!["chat_id"]);
            Assert.Equal("Markdown", (string?)body["parse_mode"]);
            var text = (string?)body["text"];
            Assert.Contains("*Gold crossed 2500*", text);          // name is the bold headline
            Assert.Contains("Gold crossed above 2500.", text);     // speech text travels too
            Assert.Contains("Value: 2501.50", text);
        }

        [Fact]
        public async Task SendAsync_AlertNameWithMarkdownCharacters_IsEscaped_SoDeliveryStillWorks()
        {
            // "BTC_USD breakout" used to go out with a raw underscore — an unbalanced
            // Markdown entity the Bot API rejects with 400, so the alert never arrived.
            JObject? body = null;
            var handler = CapturingHandler((_, b) => body = b);
            var channel = Build(handler, new TelegramAlertChannelConfig { BotToken = "t", ChatId = "1" });

            await channel.SendAsync(SampleAlert(name: "BTC_USD [spot] breakout",
                speech: "crossed *above* the `POC`"));

            var text = (string?)body!["text"];
            Assert.Contains(@"BTC\_USD \[spot] breakout", text);
            Assert.Contains(@"crossed \*above\* the \`POC\`", text);
        }

        [Fact]
        public async Task SendAsync_SubPennyValue_DoesNotCollapseToZeros()
        {
            // :F6 rendered a KAS-scale trigger as "0.000000". SpeechPriceFormatter keeps
            // the magnitude (and is invariant-culture, so no locale decimal comma either).
            JObject? body = null;
            var handler = CapturingHandler((_, b) => body = b);
            var channel = Build(handler, new TelegramAlertChannelConfig { BotToken = "t", ChatId = "1" });

            await channel.SendAsync(SampleAlert(value: 0.00000012));

            var text = (string?)body!["text"];
            Assert.Contains("0.00000012", text);
            Assert.DoesNotContain("0.000000\n", text);
        }

        [Fact]
        public async Task SendAsync_HttpError_Throws_SoDeliveryServiceLogsIt()
        {
            var handler = new FakeHttpMessageHandler()
                .Post(@"api\.telegram\.org", """{"ok":false,"description":"Too Many Requests"}""",
                    HttpStatusCode.TooManyRequests);
            var channel = Build(handler, new TelegramAlertChannelConfig { BotToken = "t", ChatId = "1" });

            await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(SampleAlert()));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("plain text", "plain text")]
        [InlineData("_*`[", @"\_\*\`\[")]
        [InlineData("]()", "]()")] // only entity OPENERS need escaping in legacy Markdown
        public void EscapeMarkdown_EscapesExactlyTheLegacyEntityOpeners(string? input, string expected)
        {
            Assert.Equal(expected, TelegramAlertChannel.EscapeMarkdown(input));
        }
    }
}
