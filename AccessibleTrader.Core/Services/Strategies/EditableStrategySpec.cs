using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Mutable working copy of a <see cref="StrategySpec"/>, shaped for two-way binding in
    /// the Build-Setup UI. The SDK <see cref="StrategySpec"/> record and its nested records
    /// (<see cref="ConditionNode"/>, <see cref="RiskPlan"/>, etc.) are immutable; this class
    /// mirrors them with mutable fields so Blazor can bind inputs without the round-trip of
    /// rebuilding the whole record on every keystroke.
    ///
    /// <see cref="ToSpec"/> converts back to the immutable record at save/preview time;
    /// <see cref="LoadFromSpec"/> walks the inverse path when loading an existing strategy.
    /// </summary>
    public sealed class EditableStrategySpec
    {
        // ── Identity ─────────────────────────────────────────────────────────
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public OrderSide Side { get; set; } = OrderSide.Buy;

        // ── Conditions ───────────────────────────────────────────────────────
        public EditableConditionNode? Root { get; set; }
        public EditableConditionNode? SelectedNode { get; set; }

        // ── Risk plan: stop ──────────────────────────────────────────────────
        public StopSourceKind StopKind { get; set; } = StopSourceKind.AtrMultiple;
        public double StopPercent { get; set; } = 0.5;
        public int    StopAtrPeriod { get; set; } = 14;
        public double StopAtrMultiple { get; set; } = 1.5;
        public int    StopLookback { get; set; } = 20;
        public double StopFixed { get; set; }
        public double StopBuffer { get; set; }

        // ── Risk plan: TP ladder ─────────────────────────────────────────────
        public List<EditableTpRung> TpRungs { get; set; } = new()
        {
            new() { Kind = TargetSourceKind.RiskRewardMultiple, Multiple = 1.0, ClosePortion = 0.34 },
            new() { Kind = TargetSourceKind.RiskRewardMultiple, Multiple = 2.0, ClosePortion = 0.33 },
            new() { Kind = TargetSourceKind.RiskRewardMultiple, Multiple = 3.0, ClosePortion = 0.33 }
        };

        // ── Risk plan: sizing / equity / R:R ─────────────────────────────────
        public SizingMode SizingMode { get; set; } = SizingMode.FixedRiskPercent;
        public double RiskPercent { get; set; } = 0.005;
        public double RiskCash { get; set; } = 100.0;
        public double FixedQuantity { get; set; } = 1.0;
        public double Equity { get; set; } = 10_000.0;
        public double MinRewardRiskRatio { get; set; } = 1.5;

        // ── Risk plan: entry trigger ─────────────────────────────────────────
        public EntryTriggerKind EntryKind { get; set; } = EntryTriggerKind.Immediate;
        public double EntryLevelPrice { get; set; }
        public int    EntryNCandles { get; set; } = 1;

        // ── Save/load state ──────────────────────────────────────────────────
        public string LoadedId { get; set; } = string.Empty;

        /// <summary>
        /// Converts the current editable values to an immutable <see cref="StrategySpec"/>.
        /// Throws if the spec isn't ready to be saved (missing root, missing name).
        /// Callers should run <see cref="StrategySpecValidator.ValidateForSave"/> first to
        /// get a human-readable error.
        /// </summary>
        public StrategySpec ToSpec()
        {
            if (Root == null)
                throw new InvalidOperationException("Add at least one condition before saving.");
            if (string.IsNullOrWhiteSpace(Name))
                throw new InvalidOperationException("Strategy name is required.");

            var conditions = ToConditionNode(Root);

            var stop = new StopSource(
                Kind: StopKind,
                PercentValue: StopPercent,
                AtrPeriod: StopAtrPeriod,
                AtrMultiple: StopAtrMultiple,
                LookbackBars: StopLookback,
                FixedPrice: StopFixed,
                BufferTicks: StopBuffer);

            var ladder = TpRungs.Select(r => new TpLadderRung(
                Kind: r.Kind,
                Multiple: r.Multiple,
                PercentValue: r.PercentValue,
                FixedPrice: r.FixedPrice,
                FibLevel: r.FibLevel,
                ClosePortion: r.ClosePortion)).ToList();

            var sizing = new PositionSizing(SizingMode, RiskPercent, FixedQuantity, RiskCash);
            var entry  = new EntryTrigger(EntryKind, EntryLevelPrice, EntryNCandles);

            var risk = new RiskPlan(
                Stop: stop,
                TpLadder: ladder,
                Sizing: sizing,
                Entry: entry,
                MinRewardRiskRatio: MinRewardRiskRatio,
                StopAdjust: StopAdjustOnTp1.MoveToBreakeven,
                NotionalEquity: Equity);

            string id = string.IsNullOrEmpty(LoadedId) ? Guid.NewGuid().ToString("N") : LoadedId;

            return new StrategySpec(
                Id: id,
                Name: Name,
                Description: Description,
                Side: Side,
                Conditions: conditions,
                Risk: risk,
                ExecutionMode: StrategyExecutionMode.Suggestion,
                CreatedUtc: DateTime.UtcNow,
                UpdatedUtc: DateTime.UtcNow);
        }

        /// <summary>
        /// Replaces all fields with values from the supplied immutable spec so the UI can edit it.
        /// </summary>
        public void LoadFromSpec(StrategySpec spec)
        {
            Name = spec.Name;
            Description = spec.Description;
            Side = spec.Side;
            Root = FromConditionNode(spec.Conditions);
            SelectedNode = null;

            StopKind = spec.Risk.Stop.Kind;
            StopPercent = spec.Risk.Stop.PercentValue;
            StopAtrPeriod = spec.Risk.Stop.AtrPeriod;
            StopAtrMultiple = spec.Risk.Stop.AtrMultiple;
            StopLookback = spec.Risk.Stop.LookbackBars;
            StopFixed = spec.Risk.Stop.FixedPrice;
            StopBuffer = spec.Risk.Stop.BufferTicks;

            TpRungs = spec.Risk.TpLadder.Select(r => new EditableTpRung
            {
                Kind = r.Kind, Multiple = r.Multiple, PercentValue = r.PercentValue,
                FixedPrice = r.FixedPrice, FibLevel = r.FibLevel, ClosePortion = r.ClosePortion
            }).ToList();

            SizingMode = spec.Risk.Sizing.Mode;
            RiskPercent = spec.Risk.Sizing.RiskPercent;
            FixedQuantity = spec.Risk.Sizing.FixedQuantity;
            RiskCash = spec.Risk.Sizing.RiskCash;

            MinRewardRiskRatio = spec.Risk.MinRewardRiskRatio;
            Equity = spec.Risk.NotionalEquity;

            EntryKind = spec.Risk.Entry.Kind;
            EntryLevelPrice = spec.Risk.Entry.LevelPrice;
            EntryNCandles = spec.Risk.Entry.NCandles;

            LoadedId = spec.Id;
        }

        /// <summary>Resets every field to the starting-point defaults.</summary>
        public void Reset()
        {
            Name = string.Empty;
            Description = string.Empty;
            Side = OrderSide.Buy;
            Root = null;
            SelectedNode = null;
            LoadedId = string.Empty;
            // Risk-plan defaults remain at their initialised values — a fresh spec inherits
            // the engine-level defaults so the user doesn't start from all-zeros.
        }

        private static ConditionNode ToConditionNode(EditableConditionNode n) => n.IsGroup
            ? new ConditionGroup(n.Id, n.Logic, n.Children.Select(ToConditionNode).ToList())
            : new ConditionLeaf(n.Id, n.SignalDescriptorId, n.Operator, n.Value, n.Value2,
                                n.WithinNBars, n.Score, n.Timeframe, n.SecondSignalDescriptorId);

        private static EditableConditionNode FromConditionNode(ConditionNode n) => n switch
        {
            ConditionGroup g => new EditableConditionNode
            {
                Id = g.Id,
                IsGroup = true,
                Logic = g.Logic,
                Children = g.Children.Select(FromConditionNode).ToList()
            },
            ConditionLeaf l => new EditableConditionNode
            {
                Id = l.Id,
                IsGroup = false,
                SignalDescriptorId = l.SignalDescriptorId,
                Operator = l.Operator,
                Value = l.Value,
                Value2 = l.Value2,
                WithinNBars = l.WithinNBars,
                Score = l.Score,
                Timeframe = l.Timeframe,
                SecondSignalDescriptorId = l.SecondSignalDescriptorId
            },
            _ => new EditableConditionNode()
        };
    }

    /// <summary>Mutable mirror of <see cref="ConditionNode"/>. Records are immutable so we use
    /// a class for two-way Blazor binding; converted to/from the SDK record on Save/Load.</summary>
    public sealed class EditableConditionNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public bool IsGroup { get; set; }

        // Group state
        public LogicOperator Logic { get; set; } = LogicOperator.And;
        public List<EditableConditionNode> Children { get; set; } = new();

        // Leaf state
        public string SignalDescriptorId = string.Empty;
        public LeafOperator Operator = LeafOperator.Fired;
        public double Value;
        public double? Value2;
        public int WithinNBars = 1;
        public double Score = 1.0;
        public string? Timeframe;
        public string? SecondSignalDescriptorId; // for CrossesAboveLine / CrossesBelowLine
    }

    /// <summary>Mutable mirror of <see cref="TpLadderRung"/>.</summary>
    public sealed class EditableTpRung
    {
        public TargetSourceKind Kind = TargetSourceKind.RiskRewardMultiple;
        public double Multiple = 2.0;
        public double PercentValue = 1.0;
        public double FixedPrice;
        public double FibLevel = 1.618;
        public double ClosePortion = 0.34;
    }
}
