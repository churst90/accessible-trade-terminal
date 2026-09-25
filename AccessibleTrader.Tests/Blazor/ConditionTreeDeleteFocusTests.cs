using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Strategies;
using Bunit;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// <b>Deleting a condition keeps the keyboard user in the tree.</b> Delete (×) is a button
/// inside the row it removes, so the focused element leaves the DOM with the row and the browser
/// drops focus on &lt;body&gt;. Until 2026-09-24 that was unreachable from the keyboard
/// (treeKeyboard.js took Enter from any button inside a treeitem); once Enter reached the button,
/// an accessibility review found focus lost and nothing announced. The rule is the WAI tree
/// pattern's: next row, else previous, else the parent, and that row becomes the Tab stop.
/// </summary>
public class ConditionTreeDeleteFocusTests
{
    private static (EditableStrategySpec spec, EditableConditionNode group, EditableConditionNode a,
                    EditableConditionNode b) Spec()
    {
        var a = new EditableConditionNode();
        var b = new EditableConditionNode();
        var group = new EditableConditionNode { IsGroup = true, Children = { a, b } };
        return (new EditableStrategySpec { Root = group, SelectedNode = a }, group, a, b);
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.ConditionTreeEditor>
        Render(BlazorTestHarness h, EditableStrategySpec spec) =>
        h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.ConditionTreeEditor>(
            p => p.Add(c => c.Spec, spec));

    private static void ClickDelete(IRenderedComponent<AccessibleTrader.BlazorClient.Components.ConditionTreeEditor> cut,
                                    EditableConditionNode node) =>
        cut.Find($"#cond-node-{node.Id} button[aria-label^='Delete']").Click();

    [Fact]
    public void Deleting_a_row_lands_on_the_next_row_and_makes_it_the_tab_stop()
    {
        using var h = new BlazorTestHarness();
        var (spec, _, a, b) = Spec();
        var cut = Render(h, spec);

        ClickDelete(cut, a);

        cut.WaitForAssertion(() => Assert.Equal("cond-node-" + b.Id, h.FocusedElementIds.LastOrDefault()));
        Assert.Same(b, spec.SelectedNode);
        Assert.Equal("0", cut.Find($"#cond-node-{b.Id}").GetAttribute("tabindex"));
    }

    [Fact]
    public void Deleting_the_last_row_lands_on_the_previous_one()
    {
        using var h = new BlazorTestHarness();
        var (spec, _, a, b) = Spec();
        var cut = Render(h, spec);

        ClickDelete(cut, b);

        cut.WaitForAssertion(() => Assert.Equal("cond-node-" + a.Id, h.FocusedElementIds.LastOrDefault()));
    }

    [Fact]
    public void Deleting_an_only_child_lands_on_its_group()
    {
        using var h = new BlazorTestHarness();
        var (spec, group, a, b) = Spec();
        group.Children.Remove(b);
        var cut = Render(h, spec);

        ClickDelete(cut, a);

        cut.WaitForAssertion(() => Assert.Equal("cond-node-" + group.Id, h.FocusedElementIds.LastOrDefault()));
    }

    [Fact]
    public void Deleting_the_root_lands_on_the_add_group_button()
    {
        using var h = new BlazorTestHarness();
        var (spec, group, _, _) = Spec();
        var cut = Render(h, spec);

        ClickDelete(cut, group);

        cut.WaitForAssertion(() => Assert.Equal("cond-add-root-group", h.FocusedElementIds.LastOrDefault()));
        Assert.Null(spec.Root);
    }

    [Fact]
    public void A_delete_is_announced()
    {
        using var h = new BlazorTestHarness();
        var said = new List<string>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) said.Add(e.Message); });
        var (spec, _, a, _) = Spec();
        var cut = Render(h, spec);

        ClickDelete(cut, a);

        cut.WaitForAssertion(() => Assert.Contains(said, m => m.StartsWith("Deleted ", StringComparison.Ordinal)));
    }
}
