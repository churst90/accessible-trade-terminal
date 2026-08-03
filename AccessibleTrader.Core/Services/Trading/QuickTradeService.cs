using System;
using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Trading
{
    public interface IQuickTradeService
    {
        QuickTradeState State { get; }

        /// <summary>Arm a risk budget. Announces, and asks for a stop.</summary>
        void Arm(double riskPercent);

        /// <summary>Use the bar under the cursor as the stop.</summary>
        void SetStopAtCursor();

        /// <summary>Place the armed trade. Limit at the cursor bar's close, or market at the live price.</summary>
        void Place(bool market);

        /// <summary>Cancel. Always safe, always answers.</summary>
        void Disarm(bool announce = true);

        /// <summary>Speak the armed state without changing it.</summary>
        void Announce();
    }

    /// <summary>
    /// Quick trades from the chart, without opening the trading dashboard.
    ///
    /// <para>
    /// ── The workflow ────────────────────────────────────────────────────────────
    /// Arm a risk budget (<c>Ctrl+Alt+Shift+1/2/3</c> → 0.5% / 1% / 2%), arrow to the bar whose low
    /// or high should be the stop and press <c>Ctrl+Alt+Shift+X</c>, then arrow to the entry and
    /// press <c>Shift+Enter</c> for a limit or <c>Ctrl+Enter</c> for market. <c>Escape</c> disarms
    /// at any point.
    /// </para>
    ///
    /// <para>
    /// ── Why the stop comes before the size ──────────────────────────────────────
    /// <b>A risk percentage does not define a position size.</b> "Risk 1%" is a cash budget; turning
    /// it into a quantity requires the distance to the stop, because that distance is what one unit
    /// can lose. Arming a percentage and immediately allowing a market order would mean either
    /// guessing the stop or sizing from equity alone — and a screen-reader user cannot glance at the
    /// ticket to catch the difference. So the state machine refuses to reach
    /// <see cref="QuickTradeStage.Ready"/> without one.
    /// </para>
    ///
    /// <para>
    /// This is also the feature's real accessibility win, and it is worth naming: the arithmetic a
    /// sighted trader does in a position-size calculator — equity, risk, stop distance, quantity —
    /// is spoken in one sentence, at the moment of the decision.
    /// </para>
    ///
    /// <para>
    /// ── Borrowed from the drawing state machine ─────────────────────────────────
    /// The shape is <c>DrawingInteractionManager</c>'s: arm a tool, collect anchors from the cursor,
    /// commit, and let <c>Escape</c> abandon a half-finished placement at any stage. That machinery
    /// earned three properties this needs more than drawings do — a partial placement is always
    /// cancellable, every stage transition is announced, and no invisible state survives a cancel.
    /// </para>
    ///
    /// <para>
    /// ── The failure mode this is designed against ───────────────────────────────
    /// Forgetting you are armed. A silent armed state that fires on a later <c>Enter</c> is the
    /// worst outcome available, so the armed state is re-announced on every bar the cursor moves to
    /// — see <see cref="ArmedSuffix"/>, which the navigation utterance appends — and the arming
    /// announcement always states what is still missing.
    /// </para>
    /// </summary>
    public sealed class QuickTradeService : IQuickTradeService
    {
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;

        public QuickTradeState State { get; private set; } = QuickTradeState.Idle;

        private readonly Func<double>? _equitySource;

        public QuickTradeService(IWorkspaceStore store, IEventBus eventBus, Func<double>? equitySource = null)
        {
            _store = store;
            _eventBus = eventBus;
            _equitySource = equitySource;
        }

        // ── Arming ──────────────────────────────────────────────────────────────

        public void Arm(double riskPercent)
        {
            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0)
            {
                Say("No chart loaded.");
                return;
            }
            if (riskPercent <= 0 || riskPercent > MaxRiskPercent)
            {
                // A hotkey typo must not be able to arm a position that can take the account out.
                Say($"Risk must be between 0 and {MaxRiskPercent} percent.");
                return;
            }

            double equity = ResolveEquity();
            if (equity <= 0)
            {
                Say("No account equity available, so a position size cannot be worked out. "
                  + "Connect a trading provider first.");
                return;
            }

            State = QuickTradeState.Idle with
            {
                Stage = QuickTradeStage.AwaitingStop,
                RiskPercent = riskPercent,
                AccountEquity = equity,
            };

            Say($"Armed {Trim(riskPercent)} percent, {Money(State.RiskCash)} at risk. "
              + "Move to the bar for your stop and press control alt shift X. Escape cancels.");
        }

        public void SetStopAtCursor()
        {
            if (State.Stage == QuickTradeStage.Idle) { Say("Nothing armed."); return; }

            var (bar, ok) = CursorBar();
            if (!ok) { Say("No bar under the cursor."); return; }

            double entry = LatestClose();
            if (entry <= 0) { Say("No price available."); return; }

            // Direction is INFERRED from where the stop sits, not asked for. A stop below the
            // current price can only be protecting a long; above it, a short. Asking would be a
            // second question with exactly one correct answer.
            bool isLong = (double)bar.Low < entry;
            double stop = isLong ? (double)bar.Low : (double)bar.High;

            if (Math.Abs(entry - stop) <= 0)
            {
                Say("That bar's stop is at the current price, so the position size would be "
                  + "unbounded. Choose a bar further away.");
                return;
            }

            State = State with
            {
                Stage = QuickTradeStage.Ready,
                StopPrice = stop,
                EntryPrice = entry,
                IsLong = isLong,
            };

            Say(Summary() + " Shift enter for a limit at the cursor, control enter for market. Escape cancels.");
        }

        // ── Placing ─────────────────────────────────────────────────────────────

        public void Place(bool market)
        {
            if (State.Stage == QuickTradeStage.Idle) { Say("Nothing armed."); return; }
            if (State.Stage == QuickTradeStage.AwaitingStop)
            {
                Say("No stop set yet. Move to the bar for your stop and press control alt shift X.");
                return;
            }

            // The limit price is the bar under the cursor; a market order takes the live price. This
            // is the one place the two paths differ, and re-deriving the size for a limit matters:
            // the cursor may have moved a long way since the stop was set, which changes the stop
            // distance and therefore the quantity.
            double entry = State.EntryPrice ?? 0;
            if (!market)
            {
                var (bar, ok) = CursorBar();
                if (!ok) { Say("No bar under the cursor to price the limit at."); return; }
                entry = (double)bar.Close;
            }

            var final = State with { EntryPrice = entry };
            if (!final.CanPlace)
            {
                Say("The stop and entry are too close to size a position. Nothing placed.");
                return;
            }

            _eventBus.Publish(new QuickTradeRequestedEvent(
                Symbol: _store.State.Identity.Symbol ?? "",
                IsLong: final.IsLong,
                Quantity: final.PositionSize!.Value,
                EntryPrice: market ? null : entry,
                StopPrice: final.StopPrice!.Value,
                RiskCash: final.RiskCash));

            Say($"{(market ? "Market" : "Limit")} {(final.IsLong ? "buy" : "sell")} "
              + $"{Qty(final.PositionSize!.Value)} at {Price(entry)}, stop {Price(final.StopPrice!.Value)}, "
              + $"risking {Money(final.RiskCash)}. Sent.");

            Disarm(announce: false);
        }

        public void Disarm(bool announce = true)
        {
            bool wasArmed = State.Stage != QuickTradeStage.Idle;
            State = QuickTradeState.Idle;
            if (announce) Say(wasArmed ? "Quick trade cancelled." : "Nothing was armed.");
        }

        public void Announce() => Say(State.Stage == QuickTradeStage.Idle ? "Nothing armed." : Summary());

        // ── The always-on reminder ──────────────────────────────────────────────

        /// <summary>
        /// The clause appended to every bar reading while a trade is armed.
        ///
        /// <para>
        /// Deliberately short — it is heard on every arrow key — and deliberately unconditional. An
        /// armed state that goes quiet is the failure this feature has to be designed against: the
        /// user forgets, presses Enter for something else, and money moves. Two words are cheap; a
        /// forgotten armed order is not.
        /// </para>
        /// </summary>
        public string ArmedSuffix() => State.Stage switch
        {
            QuickTradeStage.AwaitingStop => $"Armed {Trim(State.RiskPercent)} percent, stop needed.",
            QuickTradeStage.Ready => $"Armed {Trim(State.RiskPercent)} percent, ready.",
            _ => "",
        };

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Hard ceiling on a single quick trade. These are hotkeys pressed without a confirmation
        /// dialog, so a mis-typed risk must not be able to arm something account-ending.
        /// </summary>
        internal const double MaxRiskPercent = 10.0;

        private string Summary()
        {
            var s = State;
            if (s.Stage == QuickTradeStage.AwaitingStop)
                return $"Armed {Trim(s.RiskPercent)} percent, {Money(s.RiskCash)} at risk. No stop yet.";

            string size = s.PositionSize.HasValue ? Qty(s.PositionSize.Value) : "unknown";
            return $"Armed {Trim(s.RiskPercent)} percent. {Money(s.RiskCash)} at risk, "
                 + $"stop {Price(s.StopPrice ?? 0)}, {(s.IsLong ? "long" : "short")} "
                 + $"{size} units, entry {Price(s.EntryPrice ?? 0)}.";
        }

        private (Ohlcv Bar, bool Ok) CursorBar()
        {
            var st = _store.State;
            var d = st.Data;
            if (d == null || d.Count == 0) return (default, false);
            int i = Math.Clamp(st.CurrentDataIndex, 0, d.Count - 1);
            return (d[i], true);
        }

        private double LatestClose()
        {
            var d = _store.State.Data;
            return d == null || d.Count == 0 ? 0 : (double)d[^1].Close;
        }

        /// <summary>
        /// Account equity for sizing, supplied by the host.
        ///
        /// <para>
        /// A delegate rather than a direct call to the order service, for one reason that matters:
        /// this class must never be able to reach a broker. Sizing is arithmetic and belongs in a
        /// unit test; a service that could fetch a balance could also, one refactor later, place an
        /// order as a side effect of a test run. The host supplies the number, this decides what to
        /// do with it.
        /// </para>
        ///
        /// <para>
        /// Returning zero is a legitimate answer — no provider connected — and the arming path
        /// refuses rather than guessing, because a sized position built on a made-up balance is
        /// worse than no feature at all.
        /// </para>
        /// </summary>
        private double ResolveEquity() => _equitySource?.Invoke() ?? 0;

        private void Say(string message) =>
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, message, true));

        private static string Trim(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Money(double v) => "$" + v.ToString("N2", CultureInfo.InvariantCulture);
        private static string Price(double v) => SpeechPriceFormatter.FormatPrice(v);
        private static string Qty(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A quick trade the user has committed to. The host wires this to the order service; the
    /// service itself never touches a provider, so it stays testable and cannot place an order as a
    /// side effect of a unit test.
    /// </summary>
    public record QuickTradeRequestedEvent(
        string Symbol,
        bool IsLong,
        double Quantity,
        double? EntryPrice,
        double StopPrice,
        double RiskCash);
}
