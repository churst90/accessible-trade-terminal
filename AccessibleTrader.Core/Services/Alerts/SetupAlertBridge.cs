using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Plugins;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// Part C — bridges composite-strategy setup/signal events into the alert delivery
    /// pipeline so buy/sell setups can reach Discord (and email/Telegram), not just
    /// in-app audio. Subscribes to <see cref="SetupConfirmedEvent"/>,
    /// <see cref="SetupEntryReachedEvent"/> and <see cref="SetupDroppedEvent"/> and, when
    /// enabled, republishes each as an <see cref="AlertFiredEvent"/> carrying a synthetic
    /// <see cref="AlertDefinition"/> whose <c>WebhookTarget</c> routes to the per-asset
    /// webhook (Part B routing, keyed by the on-screen symbol).
    ///
    /// Gated by the default-off setting <c>alerts.setups.enabled</c>. The webhook target is
    /// resolved from the optional per-symbol map <c>alerts.setups.webhookMap</c>
    /// (<c>{ "BTC/USD": "BTC channel" }</c>), falling back to <c>alerts.setups.webhookTarget</c>.
    /// When neither is set the alert still flows to email/Telegram (WebhookTarget = null =
    /// no webhook post), matching the standard alert semantics.
    /// </summary>
    public sealed class SetupAlertBridge : IDisposable
    {
        private readonly IEventBus _bus;
        private readonly IWorkspaceStore _store;
        private readonly ISettingsManager _settings;
        private readonly List<IDisposable> _subs = new();

        public SetupAlertBridge(IEventBus bus, IWorkspaceStore store, ISettingsManager settings)
        {
            _bus = bus;
            _store = store;
            _settings = settings;

            _subs.Add(bus.Subscribe<SetupConfirmedEvent>(OnConfirmed));
            _subs.Add(bus.Subscribe<SetupEntryReachedEvent>(OnEntryReached));
            _subs.Add(bus.Subscribe<SetupDroppedEvent>(OnDropped));
        }

        private bool Enabled =>
            _settings.GetSetting("alerts.setups.enabled")?.ToObject<bool>() ?? false;

        private void OnConfirmed(SetupConfirmedEvent e)
        {
            string side = e.Side == OrderSide.Buy ? "Long" : "Short";
            Emit($"setup-{e.InstanceId}",
                 $"{side} setup armed — {SymbolOr(e.Symbol)}",
                 e.Rationale, symbolOverride: e.Symbol);
        }

        private void OnEntryReached(SetupEntryReachedEvent e)
        {
            string side = e.Side == OrderSide.Buy ? "Long" : "Short";
            Emit($"setup-entry-{e.InstanceId}",
                 $"{side} entry reached — {SymbolOr(e.Symbol)}",
                 $"Entry zone reached at {SpeechPriceFormatter.FormatPrice(e.TriggerPrice)}, {e.BarsArmed} bars after arming.",
                 e.TriggerPrice, symbolOverride: e.Symbol);
        }

        private void OnDropped(SetupDroppedEvent e)
        {
            if (e.DroppedLeafLabels == null || e.DroppedLeafLabels.Count == 0) return;
            string labels = string.Join(", ", e.DroppedLeafLabels);
            string suffix = e.SetupStillActive ? "Setup still active." : "Setup invalidated.";
            Emit($"setup-dropped-{e.InstanceId}",
                 $"Setup dropped — {SymbolOr(e.Symbol)}",
                 $"{labels} dropped off. {suffix}", symbolOverride: e.Symbol);
        }

        /// <summary>Prefer the symbol carried on the setup event (correct for background
        /// workspace monitors) and fall back to the focused chart for legacy publishers.</summary>
        private string SymbolOr(string eventSymbol)
        {
            if (!string.IsNullOrWhiteSpace(eventSymbol)) return eventSymbol;
            var s = _store.State.SymbolDisplayName;
            return string.IsNullOrWhiteSpace(s) ? "current chart" : s;
        }

        private void Emit(string id, string title, string speech, double triggeringValue = 0.0,
            string? symbolOverride = null)
        {
            if (!Enabled) return;

            // Event-carried symbol wins (background monitors publish for charts that
            // are NOT focused); the focused chart is only a legacy fallback.
            string? symbol = !string.IsNullOrWhiteSpace(symbolOverride)
                ? symbolOverride
                : _store.State.SymbolDisplayName;
            symbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol;

            var def = new AlertDefinition
            {
                Id = id,
                Name = title,
                Target = AlertTarget.Price,
                Condition = AlertCondition.PatternDetected, // synthetic; setups have no simple condition
                Delivery = AlertDelivery.Both,
                Symbol = symbol,
                WebhookTarget = ResolveWebhookTarget(symbol),
            };

            var fired = new AlertFired(def, triggeringValue, PreviousValue: null, SpeechText: speech, Symbol: symbol);
            _bus.Publish(new AlertFiredEvent(fired));
        }

        /// <summary>Per-symbol webhook map lookup, falling back to a single default target.</summary>
        private string? ResolveWebhookTarget(string? symbol)
        {
            if (!string.IsNullOrWhiteSpace(symbol) &&
                _settings.GetSetting("alerts.setups.webhookMap") is JObject map)
            {
                foreach (var prop in map.Properties())
                {
                    if (string.Equals(prop.Name, symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        var mapped = prop.Value?.ToString();
                        if (!string.IsNullOrWhiteSpace(mapped)) return mapped;
                    }
                }
            }

            var fallback = _settings.GetSetting("alerts.setups.webhookTarget")?.ToString();
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
        }

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
        }
    }
}
