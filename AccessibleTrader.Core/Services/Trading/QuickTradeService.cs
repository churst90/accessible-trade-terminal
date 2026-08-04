using System;
using System.Threading.Tasks;
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
        private readonly Func<Task>? _equityRefresh;

        /// <summary>Guards against a second fetch while one is already in flight.</summary>
        private int _fetching;

        private readonly Func<QuickTradeSizingMode>? _sizingMode;

        public QuickTradeService(IWorkspaceStore store, IEventBus eventBus,
            Func<double>? equitySource = null, Func<Task>? equityRefresh = null,
            Func<QuickTradeSizingMode>? sizingMode = null)
        {
            _store = store;
            _eventBus = eventBus;
            _equitySource = equitySource;
            _equityRefresh = equityRefresh;
            _sizingMode = sizingMode;
        }

        private QuickTradeSizingMode Mode => _sizingMode?.Invoke() ?? QuickTradeSizingMode.PositionValue;

        /// <summary>The instrument's own name for a unit — "BTC", not "units".</summary>
        private string Units(double qty) =>
            SymbolAssets.WithBaseUnit(qty, _store.State.Identity.Symbol, Qty(qty));

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
                // ── Ask for the balance rather than blaming the user for its absence ──
                //
                // Equity is a cached number, published by whatever last read a balance — which in
                // practice meant the trading dashboard. Someone who ticks paper trading and goes
                // straight to the chart has never opened it, so the cache is empty and arming
                // refused with "connect a trading provider first" while a perfectly good provider
                // was connected. The advice was wrong and the feature was unreachable.
                //
                // Fetching stays off the keystroke path: the read happens in the background and the
                // arm completes when it lands. The alternative — awaiting a broker inside the key
                // handler — would deliver the spoken size after the user had already pressed Enter.
                BeginEquityFetch(riskPercent);
                return;
            }

            State = QuickTradeState.Idle with
            {
                Stage = QuickTradeStage.AwaitingStop,
                RiskPercent = riskPercent,
                AccountEquity = equity,
                SizingMode = Mode,
                // In position-value mode the size is known the moment the budget is — it does not
                // wait on a stop — so the entry is seeded now and the arming line can state the
                // quantity straight away.
                EntryPrice = Mode == QuickTradeSizingMode.PositionValue ? LatestClose() : null,
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

            Say(Summary() + NotionalCaution() + " Shift enter for a limit at the cursor, control enter for market. Escape cancels.");
        }

        /// <summary>
        /// Warns when the position costs more than the account holds, at the moment the size becomes
        /// known rather than after the order is refused.
        ///
        /// <para>
        /// This is not an edge case, it is the normal consequence of risk-based sizing: quantity is
        /// risk divided by stop distance, so halving the stop distance doubles the position and
        /// doubles what it costs to hold. On a cash account a 1% risk with a tight stop routinely
        /// asks for several times the balance in notional. The trade is perfectly sensible; it is
        /// the funding that is not there.
        /// </para>
        ///
        /// <para>
        /// A caution rather than a refusal. Margin and futures accounts hold positions far larger
        /// than their cash, so blocking here would forbid ordinary leveraged trades on the strength
        /// of a spot-account assumption. The user is told and left to decide.
        /// </para>
        /// </summary>
        private string NotionalCaution()
        {
            // Position-value mode spends exactly the budget, so it cannot overspend the account.
            if (State.SizingMode == QuickTradeSizingMode.PositionValue) return "";
            if (State.PositionSize is not double qty || State.EntryPrice is not double entry) return "";
            if (State.AccountEquity <= 0 || entry <= 0) return "";

            double notional = qty * entry;
            if (notional <= State.AccountEquity) return "";

            return $" Caution: this position costs {Money(notional)}, more than the "
                 + $"{Money(State.AccountEquity)} in the account. On a cash account it will be refused — "
                 + "a stop further away makes it smaller.";
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

            // ── Speak BEFORE publishing, and say little ──────────────────────────
            //
            // Two things went wrong here and they were the same mistake.
            //
            // ORDER. Publishing first meant the executor ran synchronously into the paper broker,
            // which fills a market order immediately and announces "Order filled…" with interrupt.
            // Control then returned here and this line interrupted THAT — so the last thing a user
            // heard after committing real size was the word "Sent", and the fill, the price and the
            // quantity were cut off. The outcome must never be talked over by the intent.
            //
            // LENGTH. This used to repeat the whole calculator — side, quantity, entry, stop, cash
            // at risk — every word of which was already spoken when the stop was set, one keypress
            // earlier. Followed by the broker's own fill announcement carrying the same numbers
            // again, one action produced three recitals of the same trade. What is genuinely new at
            // this moment is only that it went, and whether it was a limit or a market.
            Say($"{(market ? "Market" : "Limit")} {(final.IsLong ? "buy" : "sell")} sent.");

            _eventBus.Publish(new QuickTradeRequestedEvent(
                Symbol: _store.State.Identity.Symbol ?? "",
                IsLong: final.IsLong,
                Quantity: final.PositionSize!.Value,
                EntryPrice: market ? null : entry,
                StopPrice: final.StopPrice!.Value,
                RiskCash: final.RiskCash));

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

        /// <summary>
        /// The armed state in one sentence, in the vocabulary of the sizing mode in force.
        ///
        /// <para>
        /// Both modes state the position value AND the loss at the stop, because each mode controls
        /// one of those and lets the other float — and the one it does not control is exactly the
        /// one a user will be surprised by. Quantities carry the instrument's own unit: "0.0078 BTC",
        /// never "0.0078 units", which names a number and withholds the noun.
        /// </para>
        /// </summary>
        private string Summary()
        {
            var s = State;
            string quote = SymbolAssets.QuoteOf(_store.State.Identity.Symbol);
            string inQuote = quote.Length > 0 ? " " + quote : "";

            if (s.Stage == QuickTradeStage.AwaitingStop)
            {
                if (s.SizingMode == QuickTradeSizingMode.PositionValue)
                {
                    string qty = s.PositionSize is double q0
                        ? $" — about {Units(q0)} at {Price(s.EntryPrice ?? 0)}"
                        : "";
                    return $"Armed to buy {Money(s.RiskCash)}{inQuote} worth, "
                         + $"{Trim(s.RiskPercent)} percent of the account{qty}. No stop yet.";
                }

                return $"Armed to risk {Money(s.RiskCash)}{inQuote}, {Trim(s.RiskPercent)} percent of "
                     + "the account, if the stop is hit. No stop yet — the size depends on it.";
            }

            string size = s.PositionSize is double q ? Units(q) : "unknown size";
            string value = s.Notional is double n ? Money(n) + inQuote : "unknown";
            string loss = s.RiskAtStop is double r ? Money(r) + inQuote : "unknown";

            return $"{(s.IsLong ? "Long" : "Short")} {size}, entry {Price(s.EntryPrice ?? 0)}, "
                 + $"stop {Price(s.StopPrice ?? 0)}. "
                 + $"Position value {value}. If the stop is hit you lose {loss}.";
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

        /// <summary>
        /// Reads the account balance in the background, then completes the arm that asked for it.
        ///
        /// <para>
        /// Every exit from here speaks. A key that goes quiet is indistinguishable from one that is
        /// not bound, and this one is quiet for as long as a network round trip — so it says what it
        /// is doing first, and says how it turned out either way.
        /// </para>
        ///
        /// <para>
        /// The re-arm is a normal <see cref="Arm"/> call rather than a special path, so the fetched
        /// case and the cached case cannot drift apart. It only proceeds if nothing else has armed
        /// or cancelled in the meantime — the user is allowed to change their mind while a balance
        /// is being fetched, and their newer intent wins.
        /// </para>
        /// </summary>
        private void BeginEquityFetch(double riskPercent)
        {
            if (_equityRefresh == null)
            {
                Say("No account equity available, so a position size cannot be worked out. "
                  + "Connect a trading provider, or turn on paper trading in settings.");
                return;
            }

            if (System.Threading.Interlocked.Exchange(ref _fetching, 1) == 1)
            {
                Say("Still fetching your balance.");
                return;
            }

            Say("Fetching your account balance.");

            _ = Task.Run(async () =>
            {
                try
                {
                    await _equityRefresh().ConfigureAwait(false);

                    if (ResolveEquity() > 0)
                    {
                        // Only if the user has not moved on. Arm() re-reads equity, which is now warm.
                        if (State.Stage == QuickTradeStage.Idle) Arm(riskPercent);
                    }
                    else
                    {
                        Say("Your account balance came back empty, so there is nothing to size a "
                          + "position from. Check the trading provider is connected and funded.");
                    }
                }
                catch (Exception ex)
                {
                    Say("Could not read your account balance. " + ex.Message);
                }
                finally
                {
                    System.Threading.Volatile.Write(ref _fetching, 0);
                }
            });
        }

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
