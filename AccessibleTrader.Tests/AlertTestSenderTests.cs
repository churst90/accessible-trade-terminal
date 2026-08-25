using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// AlertTestSender (debt item 5 view-model extraction): the Settings dialog's
    /// test-send logic, now testable without rendering a component. Status strings
    /// are pinned verbatim because the existing bUnit tests assert them in the UI.
    /// </summary>
    public class AlertTestSenderTests
    {
        private sealed class StubChannel : IAlertChannel
        {
            public string Id { get; init; } = "email";
            public string DisplayName => Id;
            public bool IsConfigured { get; set; } = true;
            public Exception? ThrowOnSend { get; set; }
            public AlertFired? Sent { get; private set; }
            public Task SendAsync(AlertFired alert, CancellationToken ct = default)
            {
                Sent = alert;
                if (ThrowOnSend != null) throw ThrowOnSend;
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task UnknownChannel_ReportsNotRegistered()
        {
            var (msg, isError) = await AlertTestSender.SendTestAsync(
                new List<IAlertChannel>(), "email");
            Assert.Equal("Channel not registered.", msg);
            Assert.True(isError);
        }

        [Fact]
        public async Task UnconfiguredChannel_ReportsMissingFields()
        {
            var ch = new StubChannel { IsConfigured = false };
            var (msg, isError) = await AlertTestSender.SendTestAsync(new[] { (IAlertChannel)ch }, "email");
            Assert.Equal("Required fields are missing — fill them out and try again.", msg);
            Assert.True(isError);
            Assert.Null(ch.Sent);
        }

        [Fact]
        public async Task Success_ReportsSent_AndFiresProgress()
        {
            var ch = new StubChannel();
            string? progress = null;
            var (msg, isError) = await AlertTestSender.SendTestAsync(
                new[] { (IAlertChannel)ch }, "email", progress: p => progress = p);
            Assert.Equal("Test sent successfully.", msg);
            Assert.False(isError);
            Assert.Equal("Sending…", progress);
            Assert.NotNull(ch.Sent);
        }

        [Fact]
        public async Task ChannelThrow_ReportsFailureWithMessage()
        {
            var ch = new StubChannel { ThrowOnSend = new InvalidOperationException("smtp down") };
            var (msg, isError) = await AlertTestSender.SendTestAsync(new[] { (IAlertChannel)ch }, "email");
            Assert.Equal("Send failed: smtp down", msg);
            Assert.True(isError);
        }

        [Fact]
        public async Task WebhookTarget_IsStampedOnTheSample()
        {
            var ch = new StubChannel { Id = "webhook" };
            await AlertTestSender.SendTestAsync(new[] { (IAlertChannel)ch }, "webhook", webhookTarget: "BTC channel");
            Assert.Equal("BTC channel", ch.Sent!.Definition.WebhookTarget);
        }
    }
}
