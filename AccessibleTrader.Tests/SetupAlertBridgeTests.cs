using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Part C — SetupAlertBridge maps composite-strategy Setup* events into
    /// AlertFiredEvent so setups reach the alert delivery pipeline (Discord/email/…),
    /// gated by the default-off "alerts.setups.enabled" setting and routed via the
    /// per-symbol / default webhook target.
    /// </summary>
    public class SetupAlertBridgeTests
    {
        private static (SetupAlertBridge bridge, SpyEventBus bus, MockWorkspaceStore store, ISettingsManager settings)
            Build(bool enabled, string? webhookTarget = null, JObject? webhookMap = null, string symbol = "BTC/USD")
        {
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            store.EmitState(WorkspaceState.Initial with { SymbolDisplayName = symbol });
            var settings = Substitute.For<ISettingsManager>();
            settings.GetSetting("alerts.setups.enabled").Returns(JToken.FromObject(enabled));
            settings.GetSetting("alerts.setups.webhookTarget")
                .Returns(webhookTarget == null ? null : JToken.FromObject(webhookTarget));
            settings.GetSetting("alerts.setups.webhookMap").Returns((JToken?)webhookMap);
            var bridge = new SetupAlertBridge(bus, store, settings);
            return (bridge, bus, store, settings);
        }

        private static SetupDroppedEvent DropEvent() =>
            new("Cipher B", "inst-1", new List<string> { "wave cross" }, SetupStillActive: false);

        [Fact]
        public void Disabled_ByDefault_PublishesNothing()
        {
            var (_, bus, _, _) = Build(enabled: false, webhookTarget: "BTC channel");

            bus.Publish(DropEvent());

            Assert.Empty(bus.Log.OfType<AlertFiredEvent>());
        }

        [Fact]
        public void Enabled_PublishesAlertFired_WithSymbolAndResolvedWebhookTarget()
        {
            var (_, bus, _, _) = Build(enabled: true, webhookTarget: "BTC channel");

            bus.Publish(DropEvent());

            var ev = Assert.Single(bus.Log.OfType<AlertFiredEvent>());
            Assert.Equal("BTC/USD", ev.Alert.Symbol);
            Assert.Equal("BTC channel", ev.Alert.Definition.WebhookTarget);
            Assert.Contains("BTC/USD", ev.Alert.Definition.Name);
        }

        [Fact]
        public void Enabled_PerSymbolMap_WinsOverDefaultTarget()
        {
            var map = new JObject { ["BTC/USD"] = "BTC channel", ["KAS/USD"] = "KAS channel" };
            var (_, bus, _, _) = Build(enabled: true, webhookTarget: "fallback", webhookMap: map, symbol: "KAS/USD");

            bus.Publish(DropEvent());

            var ev = Assert.Single(bus.Log.OfType<AlertFiredEvent>());
            Assert.Equal("KAS channel", ev.Alert.Definition.WebhookTarget);
        }

        [Fact]
        public void Enabled_NoTargetConfigured_StillFires_ButWebhookTargetIsNull()
        {
            var (_, bus, _, _) = Build(enabled: true, webhookTarget: null);

            bus.Publish(DropEvent());

            var ev = Assert.Single(bus.Log.OfType<AlertFiredEvent>());
            Assert.Null(ev.Alert.Definition.WebhookTarget);
        }
    }
}
