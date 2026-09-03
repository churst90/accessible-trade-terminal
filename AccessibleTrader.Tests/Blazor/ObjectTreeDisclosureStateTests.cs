// Does the Object Tree tell the user whether a node is open or closed?
//
// The dialog declares role="tree" over a <details>/<summary> structure. Putting
// role="treeitem" on a <summary> REPLACES the native disclosure mapping — the browser
// stops exposing the summary as an expandable disclosure triangle and exposes it as a
// treeitem instead, and a treeitem's expanded state comes from one place only:
// aria-expanded. The 2026-09-01 audit's finding was that the attribute appears nowhere
// in the file, so `treeKeyboard.js` knew the state (it reads `details.open`) and the
// user never heard it. Collapsing a pane and expanding it again produced the same
// announcement both times.
//
// The rule asserted here is the PROPERTY, not a spelling: for every treeitem in the
// tree, if it owns a <details> then its aria-expanded must agree with that details'
// `open`, and if it owns nothing it must carry no aria-expanded at all (an
// aria-expanded on a leaf promises children that do not exist). Both halves matter —
// hard-coding "the pane says true" would stay green if the panes stopped opening.
//
// The fixture is deliberately richer than ModalCatalog's one-series seed: two panes,
// three series and components underneath, so all three treeitem LEVELS are swept and
// both the expanded and the collapsed case are present in one render. A vacuity floor
// guards the fixture — a seed that quietly stopped rendering the tree would otherwise
// sweep zero treeitems and report a clean pass.

using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AngleSharp.Dom;
using NSubstitute;
using Bunit;

namespace AccessibleTrader.Tests.Blazor;

public class ObjectTreeDisclosureStateTests
{
    // ── fixture ──────────────────────────────────────────────────────────────

    private static ChartSeries Series(string id, string name, string pane, params string[] components)
    {
        var config = new SeriesConfig { Id = id, Name = name, FriendlyName = name, Pane = pane };
        foreach (var c in components)
        {
            config.Components.Add(new ComponentConfig
            {
                Name = c, DisplayName = c, DisplayType = ComponentDisplayType.Line, IsVisible = true,
            });
        }
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = id });
    }

    private static void SeedTwoPanes(BlazorTestHarness h)
    {
        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            ActiveSeries = ImmutableList.Create(
                Series(CoreSeriesIds.Candles, "Candles", "Main", "Open", "High", "Low", "Close"),
                Series("sma20", "SMA 20", "Main", "SMA"),
                Series("rsi14", "RSI 14", "Pane_RSI", "RSI", "Signal")),
            FocusedSeriesId = "sma20",
        };
        h.WorkspaceStore.State.Returns(_ => state);
    }

    private static IRenderedFragment Open(BlazorTestHarness h)
    {
        SeedTwoPanes(h);
        return h.OpenModal<AccessibleTrader.BlazorClient.Components.ObjectTreeModal>(
            bus => bus.Publish(new OpenObjectTreeEvent()));
    }

    // ── the rule ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The &lt;details&gt; a treeitem owns, mirroring treeKeyboard.js's findOwnedDetails:
    /// either the treeitem IS the summary of a details, or it has one as a direct child.
    /// Anything else is a leaf.
    /// </summary>
    private static IElement? OwnedDetails(IElement treeitem)
    {
        if (treeitem.TagName == "SUMMARY" && treeitem.ParentElement?.TagName == "DETAILS")
            return treeitem.ParentElement;
        return treeitem.Children.FirstOrDefault(c => c.TagName == "DETAILS");
    }

    private static string Describe(IElement el) =>
        $"<{el.TagName.ToLowerInvariant()} aria-level=\"{el.GetAttribute("aria-level")}\" " +
        $"aria-label=\"{el.GetAttribute("aria-label")}\">";

    [Fact]
    public void EveryExpandableTreeitem_ExposesAnAriaExpandedThatMatchesItsDetails()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h);

        var items = cut.FindAll("[role='tree'] [role='treeitem']").ToList();
        // Vacuity floor: two panes + three series + seven components.
        Assert.True(items.Count >= 12,
            $"fixture rendered only {items.Count} treeitems — the tree stopped rendering, " +
            "so this sweep proves nothing. Expected at least 12.");

        var wrong = new List<string>();
        int expandable = 0, leaves = 0;
        foreach (var item in items)
        {
            var details = OwnedDetails(item);
            var declared = item.GetAttribute("aria-expanded");
            if (details is null)
            {
                leaves++;
                if (declared is not null)
                    wrong.Add($"leaf {Describe(item)} declares aria-expanded=\"{declared}\" " +
                              "but owns no disclosure");
                continue;
            }

            expandable++;
            var actual = details.HasAttribute("open") ? "true" : "false";
            if (declared is null)
                wrong.Add($"{Describe(item)} owns a <details {(actual == "true" ? "open" : "closed")}> " +
                          "and declares no aria-expanded — the state is invisible to a screen reader");
            else if (declared != actual)
                wrong.Add($"{Describe(item)} declares aria-expanded=\"{declared}\" " +
                          $"but its <details> is {(actual == "true" ? "open" : "closed")}");
        }

        // Both branches must actually be exercised, or half the rule is unasserted.
        Assert.True(expandable >= 5, $"only {expandable} expandable treeitems swept");
        Assert.True(leaves >= 7, $"only {leaves} leaf treeitems swept");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void PanesRenderExpanded_AndSeriesRenderCollapsed()
    {
        // The initial values are the ones a user meets, and they are the half a
        // property assertion alone cannot pin: a tree that rendered every node
        // collapsed would satisfy "aria-expanded matches the details" perfectly.
        using var h = new BlazorTestHarness();
        var cut = Open(h);

        var panes = cut.FindAll("summary.pane-header[role='treeitem']").ToList();
        Assert.Equal(2, panes.Count);
        Assert.All(panes, p => Assert.Equal("true", p.GetAttribute("aria-expanded")));

        var series = cut.FindAll("div.series-node[role='treeitem']").ToList();
        Assert.Equal(3, series.Count);
        Assert.All(series, s => Assert.Equal("false", s.GetAttribute("aria-expanded")));
    }

    [Fact]
    public void TogglingASeriesDisclosure_FlipsItsAriaExpanded()
    {
        // <details> is toggled by the browser itself (a click on the summary) and
        // directly by treeKeyboard.js (`d.open = !d.open`), neither of which runs C#.
        // The component keeps up by handling the `toggle` event the browser fires on
        // every state change — Blazor registers `toggle` as a non-bubbling event and
        // attaches the listener to the element, so it arrives. Without that handler
        // the attribute is a snapshot of the first render and goes stale on the first
        // keystroke, which is worse than not having it.
        using var h = new BlazorTestHarness();
        var cut = Open(h);

        var first = cut.FindAll("div.series-node[role='treeitem']")[0];
        Assert.Equal("false", first.GetAttribute("aria-expanded"));

        // WaitForAssertion, not a bare read: TriggerEvent queues the handler on the
        // renderer's dispatcher and returns, so under a full-suite load the re-render has
        // not happened yet when the next line runs. Both assertions here are POSITIVE —
        // "it became true", "it became false again" — which is the only shape a wait can
        // strengthen; a wait around a DoesNotContain passes on the first poll and proves
        // nothing.
        cut.FindAll("details.series-details")[0].TriggerEvent("ontoggle", EventArgs.Empty);
        cut.WaitForAssertion(() => Assert.Equal("true",
            cut.FindAll("div.series-node[role='treeitem']")[0].GetAttribute("aria-expanded")));

        cut.FindAll("details.series-details")[0].TriggerEvent("ontoggle", EventArgs.Empty);
        cut.WaitForAssertion(() => Assert.Equal("false",
            cut.FindAll("div.series-node[role='treeitem']")[0].GetAttribute("aria-expanded")));
    }

    [Fact]
    public void TogglingAPaneDisclosure_FlipsItsAriaExpanded()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h);

        Assert.Equal("true", cut.FindAll("summary.pane-header")[0].GetAttribute("aria-expanded"));

        cut.FindAll("details.pane-node")[0].TriggerEvent("ontoggle", EventArgs.Empty);
        cut.WaitForAssertion(() => Assert.Equal(
            "false", cut.FindAll("summary.pane-header")[0].GetAttribute("aria-expanded")));
    }

    [Fact]
    public void TheFooterDoesNotTellTheUserToTabThroughATree()
    {
        // The dialog declares role="tree", which means arrow keys — and only the first
        // treeitem is in the tab order, so following the old "Use Tab to navigate"
        // instruction walked the user straight out of the tree on the second press.
        using var h = new BlazorTestHarness();
        var cut = Open(h);

        var footer = cut.Find("div.modal-footer").TextContent;
        Assert.DoesNotContain("Use Tab to navigate", footer);
        Assert.Contains("arrow", footer, StringComparison.OrdinalIgnoreCase);
    }
}
