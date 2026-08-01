namespace AccessibleTrader.Sdk.Strategies;

/// <summary>
/// How much a strategy has actually been tested. Deliberately blunt: there is no "good" or
/// "recommended" level, only how far the evidence goes. A spec with no recorded testing reads
/// <see cref="Untested"/>, which is the honest answer for most strategies anyone will ever write.
/// </summary>
public enum StrategyEvidenceLevel
{
    /// <summary>Never run through the research harness. Assume nothing about it.</summary>
    Untested = 0,

    /// <summary>
    /// Has backtest numbers, but only on the data the strategy was designed or selected on.
    /// This includes a cell promoted out of a large battery: picking the best of N cells is a
    /// choice made in-sample even when each individual cell was walked forward.
    /// </summary>
    InSampleOnly = 1,

    /// <summary>
    /// Survived out-of-sample evaluation — a split-half or rolling-window walk-forward on data
    /// not used to build it — but was never compared against a null (random entries, surrogate
    /// series, exposure-matched benchmark).
    /// </summary>
    WalkForward = 2,

    /// <summary>
    /// Beat an explicit control arm, not just a positive number: random-entry, surrogate-series,
    /// exposure-matched or era-sliced. The only level that means "measured against a null".
    /// </summary>
    ControlTested = 3,

    /// <summary>
    /// Real under its original test but does not survive perturbation — noise injection,
    /// parameter jitter, a different era or a nearby asset. Treat as a default, not an edge.
    /// </summary>
    Fragile = 4,

    /// <summary>Tested and failed. Kept because a recorded negative is worth more than a gap.</summary>
    Falsified = 5,
}

/// <summary>
/// The evidence attached to a <see cref="StrategySpec"/>: what it was tested on, which controls
/// were applied, and the resulting verdict in one sentence. Travels with the spec through export
/// and import so a strategy can never arrive in a user's library as an anonymous recommendation.
/// </summary>
/// <param name="Evidence">How far the testing went.</param>
/// <param name="TestedOn">Assets, timeframes and windows — or "never run".</param>
/// <param name="Controls">Which control arms were applied, or "none".</param>
/// <param name="Verdict">One honest sentence. Negative verdicts are recorded, not softened.</param>
/// <param name="Source">Optional pointer to the run, doc or note the verdict came from.</param>
public record StrategyProvenance(
    StrategyEvidenceLevel Evidence,
    string TestedOn,
    string Controls,
    string Verdict,
    string? Source = null
)
{
    /// <summary>A short, screen-reader-friendly one-liner for lists and announcements.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => $"{Evidence}: {Verdict}";
}
