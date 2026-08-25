using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>What a strategy's live position looks like between bars.</summary>
    public sealed class ManagedStrategyPosition
    {
        /// <summary>The engine instance currently driving this position. Reassigned on every
        /// restart — instance ids are fresh GUIDs — which is why <see cref="SpecId"/> and not
        /// this is what the record is persisted under.</summary>
        public string InstanceId { get; set; } = "";

        /// <summary>The library spec this position belongs to. The only identity that survives
        /// a restart; a position opened by an ad-hoc or Roslyn strategy has none and therefore
        /// cannot be re-adopted (see <see cref="IStrategyPositionManager.ReconcileAsync"/>).</summary>
        public string SpecId { get; set; } = "";

        public string StrategyName { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Symbol { get; set; } = "";
        public OrderSide Side { get; set; }

        /// <summary>The price the breakeven move and the ATR trail anchor to. Seeded from the
        /// bar the entry was decided on and corrected to the real fill price when the entry
        /// order's <c>OrderFilledEvent</c> arrives — a breakeven stop at a price the user did
        /// not get is not breakeven.</summary>
        public double EntryPrice { get; set; }

        public double InitialQuantity { get; set; }
        public double RemainingQuantity { get; set; }
        public double? StopPrice { get; set; }

        /// <summary>Ladder rungs not yet reached, in order. Popped as each fires.</summary>
        public List<double> TargetPrices { get; set; } = new();

        /// <summary>Close fractions of <see cref="InitialQuantity"/>, aligned with
        /// <see cref="TargetPrices"/>.</summary>
        public List<double> TargetPortions { get; set; } = new();

        /// <summary>
        /// How many rungs the ladder had when it was armed. Kept separately from
        /// <see cref="TargetPrices"/>, which shrinks as rungs fire — deriving the count from the
        /// remaining list makes the second rung of three announce itself as "target 1 of 2".
        /// </summary>
        public int LadderSize { get; set; }

        public StopAdjustOnTp1 StopAdjust { get; set; } = StopAdjustOnTp1.MoveToBreakeven;
        public int TrailAtrPeriod { get; set; } = 14;
        public double TrailAtrMultiple { get; set; } = 1.5;

        /// <summary>True once the first rung has fired and the stop has been adjusted. The
        /// ATR trail only runs after this, exactly as in the replay.</summary>
        public bool FirstTargetFilled { get; set; }

        /// <summary>The venue's id for the entry order, when placement returned one.</summary>
        public string? EntryOrderId { get; set; }

        public DateTime OpenedUtc { get; set; }

        /// <summary>False until a restart's reconciliation pass has confirmed this position
        /// against the broker (or established that the broker cannot say). Managed exits still
        /// run for an unverified position — refusing to exit is not the conservative choice.</summary>
        public bool Verified { get; set; } = true;

        public ManagedStrategyPosition Clone() => new()
        {
            InstanceId = InstanceId, SpecId = SpecId, StrategyName = StrategyName,
            Provider = Provider, Symbol = Symbol, Side = Side,
            EntryPrice = EntryPrice, InitialQuantity = InitialQuantity,
            RemainingQuantity = RemainingQuantity, StopPrice = StopPrice,
            TargetPrices = new List<double>(TargetPrices),
            TargetPortions = new List<double>(TargetPortions), LadderSize = LadderSize,
            StopAdjust = StopAdjust, TrailAtrPeriod = TrailAtrPeriod,
            TrailAtrMultiple = TrailAtrMultiple, FirstTargetFilled = FirstTargetFilled,
            EntryOrderId = EntryOrderId, OpenedUtc = OpenedUtc, Verified = Verified,
        };
    }

    /// <summary>
    /// One reduce-only order the manager wants placed, with everything the caller needs to
    /// place it and to say what happened. The manager has already applied the bookkeeping
    /// optimistically; <see cref="IStrategyPositionManager.ExitRejected"/> puts it back if the
    /// order does not go.
    /// </summary>
    public sealed record StrategyExitOrder(
        string ExitId,
        string InstanceId,
        string StrategyName,
        string Provider,
        string Symbol,
        /// <summary>The side of the EXIT order — the opposite of the position's.</summary>
        OrderSide Side,
        double Quantity,
        /// <summary>Spoken-ready reason: "stop", "target 2 of 3", "reversed".</summary>
        string Reason);

    /// <summary>What the manager wants done with a fresh entry signal.</summary>
    public enum StrategyEntryDisposition
    {
        /// <summary>Nothing is open for this instance — place the entry.</summary>
        Open,
        /// <summary>An opposite-side position is open — close it, then place the entry.</summary>
        Reverse,
        /// <summary>A same-side position is already open. The protective plan has been
        /// re-armed from the new signal and NO order is placed: adding to a live position is
        /// something the replay never modelled and the user never asked for.</summary>
        AlreadyOpen,
    }

    /// <summary>The manager's answer to an entry signal.</summary>
    public sealed record StrategyEntryPlan(
        StrategyEntryDisposition Disposition,
        StrategyExitOrder? CloseFirst,
        string? Message);

    /// <summary>
    /// Runs the exit plan the backtester simulates — stop, take-profit ladder, move to
    /// breakeven, ratcheting ATR trail — against live bars, and remembers the resulting
    /// position across a restart.
    ///
    /// <para>
    /// This is a TERMINAL-SIDE emulation, not a broker-side bracket. Every level is evaluated
    /// on bar close and exited with a reduce-only market order, so an exit happens at the close
    /// of the bar that reached the level rather than at the level itself. Broker-native
    /// multi-leg brackets remain the better answer where a venue has them and remain filed;
    /// this is the answer that works on all sixteen trading providers today, and it is the same
    /// granularity the replay runs at.
    /// </para>
    /// </summary>
    public interface IStrategyPositionManager
    {
        /// <summary>Every position the manager currently believes is open.</summary>
        IReadOnlyList<ManagedStrategyPosition> Open { get; }

        /// <summary>The open position for one engine instance, or null.</summary>
        ManagedStrategyPosition? Get(string instanceId);

        /// <summary>
        /// Re-attaches a persisted position to a freshly created engine instance carrying the
        /// same spec id. Called from <c>StrategyEngine.AddStrategy</c>; a no-op when the spec
        /// has no remembered position.
        /// </summary>
        void Adopt(string instanceId, string? specId);

        /// <summary>
        /// Decides what a new entry signal may do given what is already open. Applies the
        /// bookkeeping for <see cref="StrategyEntryDisposition.AlreadyOpen"/> (re-arming) and
        /// for <see cref="StrategyEntryDisposition.Reverse"/> (removing the position) itself;
        /// the caller places any returned order and then calls <see cref="OpenPosition"/>.
        /// </summary>
        StrategyEntryPlan PlanEntry(ActiveStrategy active, StrategySignal signal, double quantity,
            string provider, string symbol);

        /// <summary>Records a position after its entry order was accepted.</summary>
        void OpenPosition(ActiveStrategy active, StrategySignal signal, double quantity,
            string provider, string symbol, double referencePrice, string? entryOrderId);

        /// <summary>
        /// Walks one closed bar against the open position: stop first (it closes the whole
        /// remainder), then every ladder rung the bar reached, then the ATR trail. Returns the
        /// reduce-only orders the caller must place, in order. Mutates the position; call
        /// <see cref="ExitRejected"/> for any order that fails to place.
        /// </summary>
        IReadOnlyList<StrategyExitOrder> OnBarClosed(string instanceId, Ohlcv bar,
            IReadOnlyList<Ohlcv> history);

        /// <summary>
        /// Places the given exit orders as reduce-only market orders, IN ORDER — rung two must
        /// not race rung one onto the wire — announcing each refusal and rolling its bookkeeping
        /// back. Returns true only if every order was accepted.
        /// </summary>
        Task<bool> PlaceExitsAsync(IReadOnlyList<StrategyExitOrder> orders);

        /// <summary>Confirms an exit order went to the venue; drops the rollback snapshot.</summary>
        void ExitAccepted(string exitId);

        /// <summary>
        /// Puts back everything an exit order's bookkeeping had already taken off, because the
        /// order was refused. The level is left armed, so the next bar that is still beyond it
        /// tries again — believing you are flat when the broker still holds the position is the
        /// one outcome worse than a retry.
        /// </summary>
        void ExitRejected(string exitId);

        /// <summary>Drops a position's record entirely (strategy removed by the user).</summary>
        void Forget(string instanceId);

        /// <summary>
        /// Checks every remembered position against the broker after a restart and says what it
        /// found. A position the broker no longer holds is dropped; one whose side disagrees is
        /// dropped and announced; one the broker cannot speak about (a spot venue has no
        /// positions concept, a failed read has no answer at all) is kept, announced, and marked
        /// unverified.
        /// </summary>
        Task ReconcileAsync();
    }
}
