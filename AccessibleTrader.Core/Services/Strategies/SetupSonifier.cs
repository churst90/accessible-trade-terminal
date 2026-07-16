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

        /// <summary>
        /// "KAS/USDT: " prefix so multi-workspace users always know WHICH chart a
        /// setup announcement belongs to — essential once background monitors can
        /// speak for charts that aren't on screen. Empty symbol (legacy publishers,
        /// blank charts) keeps the old un-prefixed speech.
        /// </summary>
        private static string Prefix(string symbol) =>
            string.IsNullOrWhiteSpace(symbol) ? "" : symbol + ": ";

        private void OnArmed(SetupArmedEvent e)
        {
            _earcon.PlaySetupArmed(e.Side);
            // Multi-rung TP ladder warning: live trading currently attaches only a single
            // TakeProfit price to each order, so rungs beyond the first are not placed
            // live on any broker. A trader relying on a 3-rung ladder needs to know they
            // have to place the 2nd and 3rd rungs manually until broker-side bracket
            // plumbing ships per provider. This one-line warning is orders of magnitude
            // cheaper than the multi-day per-broker implementation and prevents the
            // silent-failure path where the user thinks all three rungs are live.
            string rungCount = e.ResolvedPlan.TpPrices.Count > 1
                ? $" Ladder has {e.ResolvedPlan.TpPrices.Count} rungs — only the first target fires live until multi-rung bracket support ships."
                : string.Empty;
            _speech.Speak(
                Prefix(e.Symbol) +
                $"{(e.Side == AccessibleTrader.Sdk.Plugins.OrderSide.Buy ? "Long" : "Short")} setup armed. " +
                $"{e.TriggerDescription} Stop {SpeechPriceFormatter.FormatPrice(e.ResolvedPlan.StopPrice)}, first target {SpeechPriceFormatter.FormatPrice(e.ResolvedPlan.TpPrices[0])}.{rungCount}",
                interrupt: false);
        }

        private void OnEntryReached(SetupEntryReachedEvent e)
        {
            _earcon.PlaySetupEntryReached(e.Side);
            _speech.Speak(
                Prefix(e.Symbol) +
                $"Entry zone reached at {SpeechPriceFormatter.FormatPrice(e.TriggerPrice)}, {e.BarsArmed} bars after arming.",
                interrupt: false);
        }

        private void OnConfirmed(SetupConfirmedEvent e)
        {
            _earcon.PlaySetupBell(e.Side, reconfirmation: false);
            // The rationale carries the side, score, entry, stop, targets, R:R, and stop
            // notes — exactly what the user asked the journal/speech entry to look like.
            _speech.Speak(Prefix(e.Symbol) + e.Rationale, interrupt: false);
        }

        private void OnReconfirmed(SetupReconfirmedEvent e)
        {
            _earcon.PlaySetupBell(e.Side, reconfirmation: true);
            // Keep re-confirmation speech terse — the user already has the full rationale
            // from the initial confirmation; subsequent bars just need the heartbeat.
            _speech.Speak(
                Prefix(e.Symbol) +
                $"{e.StrategyName} still confirmed, bar {e.BarsSinceFirstConfirm}.",
                interrupt: false);
        }

        private void OnDropped(SetupDroppedEvent e)
        {
            if (e.DroppedLeafLabels == null || e.DroppedLeafLabels.Count == 0) return;
            string labels = string.Join(", ", e.DroppedLeafLabels);
            string suffix = e.SetupStillActive ? "Setup still active." : "Setup invalidated.";
            _speech.Speak($"{Prefix(e.Symbol)}{labels} dropped off. {suffix}", interrupt: false);
        }

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
        }
    }
}
