using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Bunit;
using NSubstitute;
using Editor = AccessibleTrader.BlazorClient.Components.ConditionTreeEditor;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// <b>The condition editor can say "close crosses above the SMA 50" and "SMA 20 crosses SMA 50".</b>
/// Until 2026-10 it could say neither: the operator list never offered CrossesAboveLine /
/// CrossesBelowLine, so the "Crosses against" picker those operators reveal was unreachable;
/// when reached, it listed only other components of the SAME indicator — never price, never
/// another indicator; and two SMAs on one chart were one indicator code, so the second could not
/// be chosen. Cody asked for an alert on price touching the 50-week SMA and there was no way to
/// build it. The evaluator half is <see cref="ConditionTreeLineCrossTests"/>.
/// </summary>
public class ConditionTreeLineCrossEditorTests
{
    private static readonly ConditionTreeLineCrossTests.Catalog Cat = new();

    private static (BlazorTestHarness h, EditableStrategySpec spec, EditableConditionNode leaf, ChartSeries sma20, ChartSeries sma50)
        Setup(string descriptorId = "Sma.Sma")
    {
        var h = new BlazorTestHarness();
        h.SignalCatalog.All.Returns(Cat.All);
        h.SignalCatalog.GetById(Arg.Any<string>()).Returns(ci => Cat.GetById(ci.Arg<string>()));
        h.SignalCatalog.GetForIndicator(Arg.Any<string>()).Returns(ci => Cat.GetForIndicator(ci.Arg<string>()));

        var bars = new List<Ohlcv>
        {
            new(new DateTime(2026, 1, 5), 10, 11, 9, 10, 1),
            new(new DateTime(2026, 1, 6), 10, 11, 9, 10, 1),
        };
        var sma50 = ConditionTreeLineCrossTests.Series("Sma", 50, new[] { 10.0, 11.0 });
        var sma20 = ConditionTreeLineCrossTests.Series("Sma", 20, new[] { 9.0, 12.0 });
        var ema = ConditionTreeLineCrossTests.Series("Ema", 9, new[] { 10.0, 10.0 }, component: "Ema");
        var state = ConditionTreeLineCrossTests.State(bars, sma50, sma20, ema);
        h.WorkspaceStore.State.Returns(_ => state);

        var leaf = new EditableConditionNode { SignalDescriptorId = descriptorId, Operator = LeafOperator.GreaterThan };
        var spec = new EditableStrategySpec { Root = leaf, SelectedNode = leaf };
        return (h, spec, leaf, sma20, sma50);
    }

    private static IRenderedComponent<Editor> Render(BlazorTestHarness h, EditableStrategySpec spec) =>
        h.Ctx.RenderComponent<Editor>(p => p.Add(c => c.Spec, spec));

    private static List<string> OptionValues(IRenderedComponent<Editor> cut, string selectId) =>
        cut.FindAll($"#{selectId} option").Select(o => o.GetAttribute("value") ?? "").ToList();

    [Fact]
    public void A_line_signal_offers_the_line_cross_operators()
    {
        var (h, spec, _, _, _) = Setup();
        using var _h = h;
        var cut = Render(h, spec);

        var ops = OptionValues(cut, "leaf-operator");
        Assert.Contains(nameof(LeafOperator.CrossesAboveLine), ops);
        Assert.Contains(nameof(LeafOperator.CrossesBelowLine), ops);
    }

    [Fact]
    public void Choosing_a_line_cross_reveals_a_labelled_picker_offering_price_and_other_indicators_and_says_so()
    {
        var (h, spec, _, sma20, sma50) = Setup("CANDLES.body");
        using var _h = h;
        var said = new List<string>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) said.Add(e.Message); });
        var cut = Render(h, spec);
        Assert.Empty(cut.FindAll("#leaf-second-indicator"));

        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesAboveLine));

        Assert.Single(cut.FindAll("label[for='leaf-second-indicator']"));
        var sources = OptionValues(cut, "leaf-second-indicator");
        Assert.Contains("CANDLES", sources);                // price
        Assert.Contains("Ema", sources);                    // another indicator
        Assert.Contains("@" + sma20.Id, sources);           // each SMA on the chart, by instance
        Assert.Contains("@" + sma50.Id, sources);
        Assert.Contains(said, m => m.Contains("Crosses against", StringComparison.Ordinal));

        // The crossed line appears AFTER the operator in the DOM, so Tab from the operator reaches it.
        var ids = cut.FindAll("select").Select(s => s.Id).ToList();
        Assert.True(ids.IndexOf("leaf-second-indicator") > ids.IndexOf("leaf-operator"),
            $"select order: {string.Join(", ", ids)}");
    }

    [Fact]
    public void Close_crosses_above_the_sma_50_is_buildable_and_evaluates()
    {
        var (h, spec, leaf, _, sma50) = Setup("CANDLES.body");
        using var _h = h;
        var cut = Render(h, spec);

        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesAboveLine));
        cut.Find("#leaf-second-indicator").Change("@" + sma50.Id);

        Assert.Equal("Sma.Sma", leaf.SecondSignalDescriptorId);
        Assert.Equal(50, leaf.SecondParameters!["lookbackPeriods"]);
        Assert.Single(cut.FindAll("label[for='leaf-second-component']"));
        Assert.Single(cut.FindAll("label[for='leaf-second-tf']"));

        cut.Find("#leaf-second-tf").Change("1w");
        Assert.Equal("1w", leaf.SecondTimeframe);

        var built = Assert.IsType<ConditionLeaf>(spec.BuildConditionTree());
        Assert.Equal(LeafOperator.CrossesAboveLine, built.Operator);
        Assert.Equal("CANDLES.body", built.SignalDescriptorId);
        Assert.Equal("Sma.Sma", built.SecondSignalDescriptorId);
        Assert.Equal(50, built.SecondParameters!["lookbackPeriods"]);
        Assert.Equal("1w", built.SecondTimeframe);
    }

    [Fact]
    public void Sma_20_crosses_sma_50_is_buildable_and_reads_the_two_instances()
    {
        var (h, spec, leaf, sma20, sma50) = Setup();
        using var _h = h;
        var cut = Render(h, spec);

        cut.Find("#leaf-indicator").Change("@" + sma20.Id);
        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesAboveLine));
        cut.Find("#leaf-second-indicator").Change("@" + sma50.Id);

        Assert.Equal(20, leaf.Parameters!["lookbackPeriods"]);
        Assert.Equal(50, leaf.SecondParameters!["lookbackPeriods"]);

        // End to end through the evaluator: SMA 20 9 -> 12 over SMA 50 10 -> 11 is a cross.
        var tree = spec.BuildConditionTree()!;
        var state = h.WorkspaceStore.State;
        var eval = new ConditionEvaluator(Cat);
        Assert.True(eval.Evaluate(tree, state.Data.ToList(), state).OverallTrue,
            $"the built leaf did not cross; degradation: {eval.LastDegradation}");

        // And the tree row says both lines.
        var row = cut.Find($"#cond-node-{leaf.Id}").GetAttribute("aria-label") ?? "";
        Assert.Contains("Sma 20", row);
        Assert.Contains("Sma 50", row);
    }

    // ── accessibility review, 2026-10-09 (T1–T8) ─────────────────────────────

    private static List<FeedbackRequestEvent> Listen(BlazorTestHarness h)
    {
        var said = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) said.Add(e); });
        return said;
    }

    private static string? DescribedBy(IRenderedComponent<Editor> cut, string id) =>
        cut.Find($"#{id}").GetAttribute("aria-describedby");

    [Fact]
    public void T1_the_cross_announcement_names_above_or_below_and_does_not_interrupt()
    {
        var (h, spec, _, _, _) = Setup("CANDLES.body");
        using var _h = h;
        var said = Listen(h);
        var cut = Render(h, spec);

        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesBelowLine));

        var e = Assert.Single(said);
        Assert.StartsWith("Crosses below another line.", e.Message);
        Assert.False(e.Interrupt);
    }

    [Fact]
    public void T2_operators_are_words_not_enum_names()
    {
        var (h, spec, _, _, _) = Setup();
        using var _h = h;
        var cut = Render(h, spec);

        string Text(LeafOperator op) => cut.Find($"#leaf-operator option[value='{op}']").TextContent.Trim();
        Assert.Equal("Crosses above another line", Text(LeafOperator.CrossesAboveLine));
        Assert.Equal("Crosses above a value", Text(LeafOperator.CrossesAbove));
        Assert.Equal("Is greater than", Text(LeafOperator.GreaterThan));
        Assert.DoesNotContain(cut.FindAll("#leaf-operator option"), o => o.TextContent.Trim() == o.GetAttribute("value"));
    }

    [Fact]
    public void T3_crossing_its_own_line_is_kept_and_explained_and_a_timeframe_makes_it_valid()
    {
        var (h, spec, leaf, _, sma50) = Setup();
        using var _h = h;
        var said = Listen(h);
        var cut = Render(h, spec);

        cut.Find("#leaf-indicator").Change("@" + sma50.Id);
        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesAboveLine));
        said.Clear();
        cut.Find("#leaf-second-indicator").Change("@" + sma50.Id);

        // Kept, not snapped back to "— Select line —".
        Assert.Equal("Sma.Sma", leaf.SecondSignalDescriptorId);
        Assert.Equal("@" + sma50.Id, cut.Find("#leaf-second-indicator").GetAttribute("value"));
        Assert.Single(cut.FindAll("#leaf-second-own-line"));
        Assert.Contains("leaf-second-own-line", DescribedBy(cut, "leaf-second-indicator") ?? "");
        Assert.Contains(said, e => e.Message!.Contains("own line", StringComparison.Ordinal));
        Assert.NotNull(ConditionTreeChecks.WhyUnfireable(spec.BuildConditionTree()));

        cut.Find("#leaf-second-tf").Change("1w");

        Assert.Empty(cut.FindAll("#leaf-second-own-line"));
        Assert.Contains(said, e => e.Message!.StartsWith("Now crosses Sma 50, weekly", StringComparison.Ordinal));
        Assert.Null(ConditionTreeChecks.WhyUnfireable(spec.BuildConditionTree()));
    }

    [Fact]
    public void T4_the_default_settings_hints_are_linked_to_their_timeframe_pickers()
    {
        var (h, spec, leaf, _, _) = Setup();   // Sma.Sma, bound to no instance
        using var _h = h;
        var cut = Render(h, spec);
        Assert.Null(DescribedBy(cut, "leaf-tf"));

        cut.Find("#leaf-tf").Change("1w");
        Assert.Equal("leaf-tf-defaults", DescribedBy(cut, "leaf-tf"));
        Assert.Single(cut.FindAll("#leaf-tf-defaults"));

        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesAboveLine));
        cut.Find("#leaf-second-indicator").Change("Cipher");
        Assert.Contains("leaf-second-tf-defaults", DescribedBy(cut, "leaf-second-tf") ?? "");
        Assert.Single(cut.FindAll("#leaf-second-tf-defaults"));
        Assert.Null(leaf.SecondParameters);
    }

    [Fact]
    public void T5_the_operator_is_kept_while_offered_and_a_replacement_is_said()
    {
        var (h, spec, leaf, _, _) = Setup();
        using var _h = h;
        var said = Listen(h);
        var cut = Render(h, spec);

        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesAbove));
        said.Clear();
        cut.Find("#leaf-indicator").Change("Cipher");            // first component: Wave, a line
        Assert.Equal(LeafOperator.CrossesAbove, leaf.Operator);
        Assert.Empty(said);

        cut.Find("#leaf-component").Change("Cipher.Buy");          // a marker: CrossesAbove is not offered
        Assert.Equal(LeafOperator.Fired, leaf.Operator);
        Assert.Equal("Operator changed to Fires.", Assert.Single(said).Message);
        Assert.Equal(nameof(LeafOperator.Fired), cut.Find("#leaf-operator").GetAttribute("value"));
    }

    [Fact]
    public void T6_tree_rows_are_spoken_sentences()
    {
        var (h, spec, leaf, _, sma50) = Setup("CANDLES.body");
        using var _h = h;
        var cut = Render(h, spec);
        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesAboveLine));
        cut.Find("#leaf-second-indicator").Change("@" + sma50.Id);
        cut.Find("#leaf-second-tf").Change("1w");

        Assert.Equal("Price close crosses above Sma 50, weekly", cut.Find($"#cond-node-{leaf.Id}").GetAttribute("aria-label"));

        var (h2, spec2, leaf2, _, _) = Setup();
        using var _h2 = h2;
        leaf2.Parameters = new Dictionary<string, double> { ["lookbackPeriods"] = 50 };
        leaf2.Value = 70;
        var cut2 = Render(h2, spec2);
        Assert.Equal("Sma 50 is greater than 70", cut2.Find($"#cond-node-{leaf2.Id}").GetAttribute("aria-label"));
    }

    [Fact]
    public void T7_a_crossed_line_that_is_not_on_the_chart_is_warned_and_linked()
    {
        var (h, spec, _, _, _) = Setup("CANDLES.body");
        using var _h = h;
        var cut = Render(h, spec);
        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesAboveLine));
        cut.Find("#leaf-second-indicator").Change("Cipher");

        Assert.Single(cut.FindAll("#leaf-second-not-on-chart"));
        Assert.Contains("leaf-second-not-on-chart", DescribedBy(cut, "leaf-second-indicator") ?? "");

        // On a higher timeframe it is computed from that timeframe's bars: no warning.
        cut.Find("#leaf-second-tf").Change("1w");
        Assert.Empty(cut.FindAll("#leaf-second-not-on-chart"));
    }

    [Fact]
    public void T8_the_line_picker_has_a_placeholder()
    {
        var (h, spec, _, _, sma50) = Setup("CANDLES.body");
        using var _h = h;
        var cut = Render(h, spec);
        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesAboveLine));
        cut.Find("#leaf-second-indicator").Change("@" + sma50.Id);

        var first = cut.FindAll("#leaf-second-component option")[0];
        Assert.Equal("", first.GetAttribute("value"));
        Assert.Equal("— Select line —", first.TextContent.Trim());
    }

    [Fact]
    public void Every_picker_in_the_leaf_editor_is_labelled()
    {
        var (h, spec, _, _, sma50) = Setup("CANDLES.body");
        using var _h = h;
        var cut = Render(h, spec);
        cut.Find("#leaf-operator").Change(nameof(LeafOperator.CrossesAboveLine));
        cut.Find("#leaf-second-indicator").Change("@" + sma50.Id);

        foreach (var control in cut.FindAll("select, input"))
        {
            Assert.False(string.IsNullOrEmpty(control.Id), "a control with no id cannot be labelled by <label for>");
            Assert.True(cut.FindAll($"label[for='{control.Id}']").Count == 1, $"#{control.Id} has no visible label");
        }
    }
}
