using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Tests;

public class StrategySpecValidatorTests
{
    private static EditableConditionNode Pulse(LeafOperator op = LeafOperator.Fired) =>
        new() { IsGroup = false, Operator = op };

    private static EditableConditionNode Threshold() =>
        new() { IsGroup = false, Operator = LeafOperator.GreaterThan, Value = 50 };

    private static EditableConditionNode Group(LogicOperator logic, params EditableConditionNode[] children) =>
        new() { IsGroup = true, Logic = logic, Children = new(children) };

    [Fact]
    public void IsPurePulseTree_returns_true_for_single_fired_leaf()
    {
        Assert.True(StrategySpecValidator.IsPurePulseTree(Pulse()));
    }

    [Fact]
    public void IsPurePulseTree_returns_false_for_threshold_leaf()
    {
        Assert.False(StrategySpecValidator.IsPurePulseTree(Threshold()));
    }

    [Fact]
    public void IsPurePulseTree_returns_false_when_any_child_is_threshold()
    {
        var root = Group(LogicOperator.And, Pulse(LeafOperator.CrossesAbove), Threshold());
        Assert.False(StrategySpecValidator.IsPurePulseTree(root));
    }

    [Fact]
    public void IsPurePulseTree_returns_true_when_all_children_are_pulses()
    {
        var root = Group(LogicOperator.And,
            Pulse(LeafOperator.Fired),
            Pulse(LeafOperator.CrossesAbove),
            Pulse(LeafOperator.ChangesDirection));
        Assert.True(StrategySpecValidator.IsPurePulseTree(root));
    }

    [Fact]
    public void IsPurePulseTree_returns_false_for_empty_group()
    {
        Assert.False(StrategySpecValidator.IsPurePulseTree(Group(LogicOperator.And)));
    }

    [Fact]
    public void ValidateForSave_allows_pulse_tree_with_Immediate_trigger()
    {
        var spec = new EditableStrategySpec
        {
            Root = Pulse(),
            EntryKind = EntryTriggerKind.Immediate
        };
        Assert.Null(StrategySpecValidator.ValidateForSave(spec));
    }

    [Fact]
    public void ValidateForSave_rejects_pulse_tree_with_deferred_trigger()
    {
        var spec = new EditableStrategySpec
        {
            Root = Pulse(),
            EntryKind = EntryTriggerKind.OnPullbackToLevel
        };
        string? error = StrategySpecValidator.ValidateForSave(spec);
        Assert.NotNull(error);
        Assert.Contains("Cannot save", error);
        Assert.Contains("OnPullbackToLevel", error);
    }

    [Fact]
    public void ValidateForSave_allows_mixed_tree_with_deferred_trigger()
    {
        var spec = new EditableStrategySpec
        {
            Root = Group(LogicOperator.And, Pulse(), Threshold()),
            EntryKind = EntryTriggerKind.OnPullbackToLevel
        };
        Assert.Null(StrategySpecValidator.ValidateForSave(spec));
    }

    [Fact]
    public void BuildPulseOnlyAdvisory_returns_null_for_non_pulse_tree()
    {
        var spec = new EditableStrategySpec { Root = Threshold() };
        Assert.Null(StrategySpecValidator.BuildPulseOnlyAdvisory(spec));
    }

    [Fact]
    public void BuildPulseOnlyAdvisory_returns_note_for_pulse_plus_Immediate()
    {
        var spec = new EditableStrategySpec
        {
            Root = Pulse(),
            EntryKind = EntryTriggerKind.Immediate
        };
        string? advisory = StrategySpecValidator.BuildPulseOnlyAdvisory(spec);
        Assert.NotNull(advisory);
        Assert.StartsWith("Note:", advisory);
    }

    [Fact]
    public void EditableStrategySpec_round_trip_preserves_fields()
    {
        var spec = new EditableStrategySpec
        {
            Name = "Test Setup",
            Description = "Round-trip check",
            Root = Group(LogicOperator.Or, Threshold(), Pulse(LeafOperator.CrossesAbove)),
            StopKind = StopSourceKind.PercentOfPrice,
            StopPercent = 1.5,
            MinRewardRiskRatio = 2.5,
            EntryKind = EntryTriggerKind.OnBreakoutOf,
            EntryLevelPrice = 50_000.0,
        };

        var persisted = spec.ToSpec();
        var roundTripped = new EditableStrategySpec();
        roundTripped.LoadFromSpec(persisted);

        Assert.Equal(spec.Name, roundTripped.Name);
        Assert.Equal(spec.Description, roundTripped.Description);
        Assert.Equal(spec.StopKind, roundTripped.StopKind);
        Assert.Equal(spec.StopPercent, roundTripped.StopPercent);
        Assert.Equal(spec.MinRewardRiskRatio, roundTripped.MinRewardRiskRatio);
        Assert.Equal(spec.EntryKind, roundTripped.EntryKind);
        Assert.Equal(spec.EntryLevelPrice, roundTripped.EntryLevelPrice);
        Assert.NotNull(roundTripped.Root);
        Assert.True(roundTripped.Root!.IsGroup);
        Assert.Equal(LogicOperator.Or, roundTripped.Root.Logic);
        Assert.Equal(2, roundTripped.Root.Children.Count);
    }
}
