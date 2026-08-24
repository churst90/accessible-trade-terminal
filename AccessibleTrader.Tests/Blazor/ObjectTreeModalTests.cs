using System.Linq;
using AccessibleTrader.Core.Models;
using Bunit;
using Xunit;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The Object Tree's per-series action buttons. These have no aria-label, so the
/// visible text IS the accessible name — it must state the action the button
/// performs, not the state the user is already in.
/// </summary>
public class ObjectTreeModalTests
{
    [Fact]
    public void Buttons_name_the_action_they_perform_not_the_state_they_are_leaving()
    {
        // Regression: a VISIBLE series showed a button reading "Show" (and an audible
        // one "Sound"), with a separately inverted aria-pressed contradicting it —
        // so a user cross-checking got two wrong signals. The labels also carried
        // orphan U+FE0F variation selectors ("Show️", "Delete️").
        using var h = new BlazorTestHarness();
        ModalCatalog.SeedChartState(h);   // one visible, audible series

        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.ObjectTreeModal>(
            bus => bus.Publish(new OpenObjectTreeEvent()));

        var texts = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Hide", texts);
        Assert.Contains("Mute", texts);
        Assert.DoesNotContain("Show", texts);
        Assert.DoesNotContain("Sound", texts);
        Assert.DoesNotContain(texts, t => t.Contains('️'));
    }
}
