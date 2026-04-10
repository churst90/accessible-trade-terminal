using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Audio + speech router for the composite-strategy setup state machine. Subscribes to
    /// <c>SetupConfirmedEvent</c> / <c>SetupReconfirmedEvent</c> / <c>SetupDroppedEvent</c>
    /// (published by <c>ConfigurableStrategy</c>) and:
    ///
    ///   - On a new confirmation: plays the long/short setup bell at full volume and
    ///     speaks the rationale (which already contains stop, first target, and R:R).
    ///   - On a re-confirmation: replays the same bell at lower volume and speaks a brief
    ///     "still confirmed, bar N" — fulfils the user's directive that ongoing matches
    ///     produce ongoing audio confirmation without fatigue.
    ///   - On a dropout: speaks the dropped leaf labels ("Cipher A wave cross dropped off").
    ///
    /// Construction-time DI subscription is intentional: the service is a singleton and the
    /// <c>JournalService</c> already mirrors the strategy signals into the journal via the
    /// existing <c>StrategySignalEvent</c> path, so we don't need to double-log here.
    /// </summary>
    public sealed class SetupSonifier : IDisposable
    {
        private readonly IEarconService _earcon;
        private readonly ISpeechManager _speech;
        private readonly List<IDisposable> _subs = new();

        public SetupSonifier(IEventBus bus, IEarconService earcon, ISpeechManager speech)
        {
            _earcon = earcon;
            _speech = speech;

            _subs.Add(bus.Subscribe<SetupConfirmedEvent>(OnConfirmed));
            _subs.Add(bus.Subscribe<SetupReconfirmedEvent>(OnReconfirmed));
            _subs.Add(bus.Subscribe<SetupDroppedEvent>(OnDropped));
            _subs.Add(bus.Subscribe<SetupArmedEvent>(OnArmed));
            _subs.Add(bus.Subscribe<SetupEntryReachedEvent>(OnEntryReached));
        }

        private void OnArmed(SetupArmedEvent e)
        {
            _earcon.PlaySetupArmed(e.Side);
            _speech.Speak(
                $"{(e.Side == AccessibleTrader.Sdk.Plugins.OrderSide.Buy ? "Long" : "Short")} setup armed. " +
                $"{e.TriggerDescription} Stop {e.ResolvedPlan.StopPrice:F4}, first target {e.ResolvedPlan.TpPrices[0]:F4}.",
                interrupt: false);
        }

        private void OnEntryReached(SetupEntryReachedEvent e)
        {
            _earcon.PlaySetupEntryReached(e.Side);
            _speech.Speak(
                $"Entry zone reached at {e.TriggerPrice:F4}, {e.BarsArmed} bars after arming.",
                interrupt: false);
        }

        private void OnConfirmed(SetupConfirmedEvent e)
        {
            _earcon.PlaySetupBell(e.Side, reconfirmation: false);
            // The rationale carries the side, score, stop, first target, R:R, and stop notes —
            // exactly what the user asked the journal/speech entry to look like.
            _speech.Speak(e.Rationale, interrupt: false);
        }

        private void OnReconfirmed(SetupReconfirmedEvent e)
        {
            _earcon.PlaySetupBell(e.Side, reconfirmation: true);
            // Keep re-confirmation speech terse — the user already has the full rationale
            // from the initial confirmation; subsequent bars just need the heartbeat.
            _speech.Speak(
                $"{e.StrategyName} still confirmed, bar {e.BarsSinceFirstConfirm}.",
                interrupt: false);
        }

        private void OnDropped(SetupDroppedEvent e)
        {
            if (e.DroppedLeafLabels == null || e.DroppedLeafLabels.Count == 0) return;
            string labels = string.Join(", ", e.DroppedLeafLabels);
            string suffix = e.SetupStillActive ? "Setup still active." : "Setup invalidated.";
            _speech.Speak($"{labels} dropped off. {suffix}", interrupt: false);
        }

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
        }
    }
}
