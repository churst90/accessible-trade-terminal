using System;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Strategies;

/// <summary>
/// Top-level serializable strategy specification. This is what gets persisted in
/// <c>strategies.json</c> and what the future builder UI produces. A spec is purely data —
/// the runtime <c>ConfigurableStrategy</c> wraps it with evaluation state.
/// </summary>
/// <param name="Id">Stable spec identifier (GUID for new specs).</param>
/// <param name="Name">User-facing name shown in the strategy modal.</param>
/// <param name="Description">Free-form description, narrated by TTS in the journal entry.</param>
/// <param name="Side">Long or short bias. Determines stop/target arithmetic mirroring.</param>
/// <param name="Conditions">Root of the condition tree (group or single leaf).</param>
/// <param name="Risk">Risk plan: stop, TP ladder, sizing, entry trigger, R:R gate.</param>
/// <param name="ExecutionMode">Suggestion (alert only) or Auto (place orders).</param>
/// <param name="CreatedUtc">Audit timestamp.</param>
/// <param name="UpdatedUtc">Last edit timestamp.</param>
/// <param name="IsAutoActivate">
/// When true, the <c>StrategyAutoLoader</c> instantiates this spec via
/// <c>IConfigurableStrategyFactory</c> and registers it with <c>IStrategyEngine</c>
/// at app startup. The builder UI's "Add to Engine" button flips this to true and persists;
/// the Active tab's Remove button flips it back to false. Saved specs that aren't auto-activated
/// remain available in the library as templates the user can load and edit later.
/// </param>
public record StrategySpec(
    string Id,
    string Name,
    string Description,
    OrderSide Side,
    ConditionNode Conditions,
    RiskPlan Risk,
    StrategyExecutionMode ExecutionMode = StrategyExecutionMode.Suggestion,
    DateTime CreatedUtc = default,
    DateTime UpdatedUtc = default,
    bool IsAutoActivate = false
);
