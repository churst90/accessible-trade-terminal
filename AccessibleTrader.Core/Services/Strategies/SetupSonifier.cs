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

        /// <summary>
        /// How a multi-rung ladder actually executes, said out loud.
        ///
        /// <para>
        /// This used to read "only the first target fires live until multi-rung bracket support
        /// ships", which was true and is not any more: <c>IStrategyPositionManager</c> holds
        /// every rung and closes its portion as price reaches it. But the replacement is not
        /// "all targets fire live" either. The rungs are run by the TERMINAL, on bar close, with
        /// reduce-only market orders — not by a resting order at the exchange. So the exit
        /// happens at the close of the bar that reached the level, the app has to be running,
        /// and a gap through a rung fills past it. The user is entitled to know which of those
        /// two things is protecting their money.
        /// </para>
        /// </summary>
        private static string LadderNote(AccessibleTrader.Sdk.Strategies.ResolvedRiskPlan plan) =>
            plan.TpPrices.Count > 1
                ? $" Ladder has {plan.TpPrices.Count} rungs; the terminal closes each one at the "
                  + "close of the bar that reaches it, so the app has to be running."
                : string.Empty;

        private void OnArmed(SetupArmedEvent e)
        {
            _earcon.PlaySetupArmed(e.Side);
            string rungCount = LadderNote(e.ResolvedPlan);
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
            //
            // The ladder note belongs here as well as on Armed, and this is the path that
            // needed it more: an Immediate-trigger setup — and every pure-pulse tree, which is
            // auto-promoted to Immediate — goes Inactive→Active through THIS event and never
            // publishes SetupArmedEvent at all. Those are the setups most likely to be running
            // in Auto mode, so they were precisely the ones whose user never heard how their
            // targets execute.
            _speech.Speak(Prefix(e.Symbol) + e.Rationale + LadderNote(e.ResolvedPlan), interrupt: false);
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
