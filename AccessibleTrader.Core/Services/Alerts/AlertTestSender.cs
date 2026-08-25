using AccessibleTrader.Sdk.Alerts;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// The Settings dialog's "Send test" logic, extracted from the component
    /// (debt item 5: view-models — UI components render and report; logic lives
    /// in plain classes that unit-test without bUnit). Builds the sample alert
    /// and runs the channel send, returning the exact user-facing status strings
    /// the modal displays.
    /// </summary>
    public static class AlertTestSender
    {
        public static AlertFired BuildSampleAlert(string? webhookTarget = null) => new(
            Definition: new AlertDefinition
            {
                Id = "test-alert",
                Name = "Accessible Trader test alert",
                Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove,
                Delivery = AlertDelivery.Both,
                WebhookTarget = webhookTarget,
            },
            TriggeringValue: 0.0,
            PreviousValue: null,
            SpeechText: "This is a test alert from the Settings modal.");

        /// <summary>
        /// Sends a sample alert through the named channel. Returns the status line
        /// to display; <paramref name="progress"/> fires with "Sending…" just before
        /// the network call so the UI can show intermediate state.
        /// </summary>
        public static async Task<(string Message, bool IsError)> SendTestAsync(
            IEnumerable<IAlertChannel> channels, string channelId,
            string? webhookTarget = null, Action<string>? progress = null)
        {
            var channel = channels.FirstOrDefault(c => c.Id == channelId);
            if (channel == null)
                return ("Channel not registered.", true);
            if (!channel.IsConfigured)
                return ("Required fields are missing — fill them out and try again.", true);

            progress?.Invoke("Sending…");
            try
            {
                await channel.SendAsync(BuildSampleAlert(webhookTarget));
                return ("Test sent successfully.", false);
            }
            catch (Exception ex)
            {
                return ($"Send failed: {ex.Message}", true);
            }
        }
    }
}
