using System.Text.Json.Serialization;

namespace AccessibleTrader.Sdk.Strategies;

/// <summary>
/// How a single leaf compares its signal source against the configured value(s).
/// Different <see cref="SignalKind"/> values accept different operators — the builder UI
/// gates the dropdown so the user can only pick valid combinations.
/// </summary>
public enum LeafOperator
{
    /// <summary>Marker fired on this bar (value is non-NaN).</summary>
    Fired,
    /// <summary>Marker fired any time within the last N bars (debounce window).</summary>
    FiredWithin,
    /// <summary>Oscillator/line value is above the configured threshold.</summary>
    GreaterThan,
    /// <summary>Oscillator/line value is below the configured threshold.</summary>
    LessThan,
    /// <summary>Value is between Value (low) and Value2 (high), inclusive.</summary>
    Between,
    /// <summary>Value crossed above the threshold this bar (was below previously).</summary>
    CrossesAbove,
    /// <summary>Value crossed below the threshold this bar (was above previously).</summary>
    CrossesBelow,
    /// <summary>Value crossed above another component (Value2 = "{indicator}.{component}" reference).</summary>
    CrossesAboveLine,
    /// <summary>Value crossed below another component (Value2 = "{indicator}.{component}" reference).</summary>
    CrossesBelowLine,
    /// <summary>Direction flipped this bar (sign of (value - prevValue) reversed).</summary>
    ChangesDirection,
    /// <summary>Cloud condition: price is inside the cloud region.</summary>
    InsideCloud,
    /// <summary>Cloud condition: price is above the cloud region.</summary>
    AboveCloud,
    /// <summary>Cloud condition: price is below the cloud region.</summary>
    BelowCloud,
    /// <summary>Phase-4: price touched a support/resistance level and reversed away from it within
    /// the last <c>WithinNBars</c> bars (long: bounced off support; short: rejected from resistance).
    /// Tolerance is provided as a fraction of price in <c>Value</c> (e.g. 0.001 = 0.1%).</summary>
    PriceRejectsLevel,
    /// <summary>Phase-4: price closed on the opposite side of a support/resistance level on this bar
    /// (long-relevant: close above a resistance — breakout; short-relevant: close below a support).
    /// Tolerance fraction in <c>Value</c>.</summary>
    PriceBreaksLevel,
    /// <summary>Phase-4: bar closed above the volume profile POC.</summary>
    BarClosesAbovePoc,
    /// <summary>Phase-4: bar closed below the volume profile POC.</summary>
    BarClosesBelowPoc,
    /// <summary>Phase-4: current close is between any value-area's VAL and VAH.</summary>
    PriceInsideValueArea,
    /// <summary>Phase-4: current close is outside every visible value area (above VAH or below VAL).</summary>
    PriceOutsideValueArea,
    /// <summary>Phase-4: this bar's wick (low or high) crossed an LVN even if its close didn't.
    /// Long-relevant: low wicked into a support LVN. Short-relevant: high wicked into a resistance LVN.</summary>
    WickIntoLvn,
    /// <summary>Phase-11 v7: value strictly greater than <c>Value</c> on ANY of the last <c>WithinNBars</c>
    /// closed bars. Persistent-condition analogue to <see cref="FiredWithin"/> for rolling-window
    /// temporal confluence across non-pulse leaves.</summary>
    GreaterThanWithin,
    /// <summary>Phase-11 v7: value strictly less than <c>Value</c> on ANY of the last <c>WithinNBars</c>
    /// closed bars.</summary>
    LessThanWithin,
    /// <summary>Phase-11 v7: value within [<c>Value</c>, <c>Value2</c>] on ANY of the last
    /// <c>WithinNBars</c> closed bars.</summary>
    BetweenWithin,
    /// <summary>v9 fix: current value is at or below the Pth percentile of the trailing
    /// <c>WithinNBars</c> closed bars, where P = <c>Value</c> (0..100). Regime-relative
    /// gate — replaces fixed thresholds for non-stationary inputs (funding rate, sentiment)
    /// where an absolute number is meaningless across different market regimes.</summary>
    PercentileBelow,
    /// <summary>v9 fix: current value is at or above the Pth percentile of the trailing
    /// <c>WithinNBars</c> closed bars, where P = <c>Value</c> (0..100).</summary>
    PercentileAbove
}

/// <summary>
/// How a <see cref="ConditionGroup"/> combines its children.
/// </summary>
public enum LogicOperator
{
    /// <summary>All children must evaluate true.</summary>
    And,
    /// <summary>At least one child must evaluate true.</summary>
    Or,
    /// <summary>Inverts the result of a single child (group must contain exactly one child).</summary>
    Not,
    /// <summary>Phase-11 v7: evaluate every child, sum the Score weight of each leaf that
    /// resolved true, and return true if the sum meets <see cref="ConditionGroup.ScoreThreshold"/>.
    /// When ScoreThreshold is null a group with Score logic degrades to OR. Unlike And/Or this
    /// operator gates setups by weighted confluence rather than boolean unanimity, letting a
    /// v7-style strategy fire when enough orthogonal sources agree.</summary>
    Score,
    /// <summary>v10: evaluate children as a strict chronological sequence — child 0 must
    /// fire on some bar t0, child 1 must fire on some bar t1 strictly after t0, child 2 on
    /// t2 > t1, etc. Each child's <c>WithinNBars</c> is interpreted as the max gap between
    /// that child and the NEXT child (or, for the last child, max gap from current bar).
    /// Walks backward from current bar: finds last fire of last child within its budget,
    /// then last fire of previous child in the budget BEFORE that, etc. All children must
    /// be leaves (groups inside a Sequence are not supported). Children do NOT contribute
    /// to score or maxScore — Sequence is a structural gate, not a confluence sum.</summary>
    Sequence
}

/// <summary>
/// Discriminator base for condition tree nodes — either a <see cref="ConditionLeaf"/>
/// or a <see cref="ConditionGroup"/>. JsonPolymorphic attributes enable
/// System.Text.Json round-trip serialization through the abstract base type, which is
/// what <c>JsonStrategyLibrary</c> uses to load <c>strategies.json</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(ConditionLeaf),  "leaf")]
[JsonDerivedType(typeof(ConditionGroup), "group")]
public abstract record ConditionNode(string Id);

/// <summary>
/// One leaf in the condition tree — references a single signal source and applies an operator.
/// </summary>
/// <param name="Id">Stable identifier within the strategy spec, used by the evaluator to key per-leaf state.</param>
/// <param name="SignalDescriptorId">Foreign key into <see cref="SignalDescriptor.Id"/>.</param>
/// <param name="Operator">How to compare the signal value(s).</param>
/// <param name="Value">Primary comparison value (threshold for GreaterThan, low end for Between, etc.).</param>
/// <param name="Value2">Secondary value — high end for Between, line reference for CrossesAboveLine.</param>
/// <param name="WithinNBars">Debounce window in bars for FiredWithin and similar window operators.</param>
/// <param name="Score">Weight contribution to setup score when this leaf is true (0..1).</param>
/// <param name="Timeframe">Optional higher-timeframe context for multi-timeframe strategies.</param>
/// <param name="SecondSignalDescriptorId">
/// Optional second signal descriptor used by line-cross operators
/// (<see cref="LeafOperator.CrossesAboveLine"/> and <see cref="LeafOperator.CrossesBelowLine"/>).
/// When set, the leaf compares the primary descriptor's value series against this second
/// descriptor's value series — e.g. "Cipher A WT Momentum crosses above WT Signal" pairs two
/// components of the same indicator. When null, the cross-line operators degrade to false.
/// </param>
public record ConditionLeaf(
    string Id,
    string SignalDescriptorId,
    LeafOperator Operator,
    double Value = 0.0,
    double? Value2 = null,
    int WithinNBars = 1,
    double Score = 1.0,
    string? Timeframe = null,
    string? SecondSignalDescriptorId = null,
    double MinLevelStrength = 0.0
) : ConditionNode(Id);

/// <summary>
/// A group of child nodes combined by a single <see cref="LogicOperator"/>.
/// Children may themselves be groups, allowing arbitrarily deep boolean trees.
/// </summary>
public record ConditionGroup(
    string Id,
    LogicOperator Logic,
    IReadOnlyList<ConditionNode> Children,
    double? ScoreThreshold = null
) : ConditionNode(Id);

/// <summary>
/// Result of evaluating a condition tree against current market state.
/// </summary>
/// <param name="OverallTrue">True if the root group/leaf evaluated true.</param>
/// <param name="LeafResults">Per-leaf bool map keyed by <see cref="ConditionLeaf.Id"/> — used by
/// <c>ConfigurableStrategy</c> to detect dropouts (leaves that flipped true→false since last bar).</param>
/// <param name="Score">Sum of Score values from leaves that evaluated true (0..N).</param>
/// <param name="MaxScore">Theoretical maximum if every leaf were true — divide for normalised score.</param>
public record ConditionEvaluation(
    bool OverallTrue,
    IReadOnlyDictionary<string, bool> LeafResults,
    double Score,
    double MaxScore
);
