// Reading a gated button the way a screen reader reads it.
//
// GatedButton and ToolbarIconButton do not say why they are refusing in their own
// text — they say it through aria-describedby, which is what NVDA and JAWS read after
// the name and the "unavailable" state. A test that asserts on the button's TextContent
// is therefore asserting on the wrong string, and a test that asserts only
// aria-disabled="true" cannot tell a real refusal from a button that has stopped
// refusing but kept the attribute. This resolves the description the way the browser
// does, so a test can assert on the sentence the user actually hears.

using AngleSharp.Dom;
using Bunit;

namespace AccessibleTrader.Tests.Blazor;

internal static class GatedButtonAssert
{
    /// <summary>
    /// The accessible description of <paramref name="el"/>: every id in its
    /// aria-describedby, resolved against the rendered fragment and joined in the order
    /// they are listed — which is the order a screen reader reads them in.
    /// Empty string when there is no description, which is what an available button has.
    /// </summary>
    internal static string ReasonOf(IRenderedFragment cut, IElement el)
    {
        var ids = (el.GetAttribute("aria-describedby") ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        foreach (var id in ids)
        {
            // Matched on the attribute rather than through a CSS #id selector so an id
            // needing escaping still resolves the way a browser resolves it.
            var target = cut.Nodes.OfType<IElement>()
                .SelectMany(r => new[] { r }.Concat(r.QuerySelectorAll("*")))
                .FirstOrDefault(e => e.GetAttribute("id") == id);
            var text = target?.TextContent?.Trim();
            if (!string.IsNullOrEmpty(text)) parts.Add(text);
        }
        return string.Join(" ", parts);
    }

    /// <summary>
    /// The whole contract for a refused button, asserted in one place: it is NOT natively
    /// disabled (so it is still in the tab order and in the screen reader's button list),
    /// it announces as unavailable, and it carries a reason containing
    /// <paramref name="expectedFragment"/>.
    /// </summary>
    internal static void IsRefusedBecause(IRenderedFragment cut, IElement el, string expectedFragment)
    {
        Assert.False(el.HasAttribute("disabled"),
            "The button is natively disabled, so it is gone from the tab order and from the "
            + "screen reader's button list — the defect GatedButton exists to remove.");
        Assert.Equal("true", el.GetAttribute("aria-disabled"));
        var reason = ReasonOf(cut, el);
        Assert.True(reason.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase),
            $"Expected the refusal to mention \"{expectedFragment}\"; it said: \"{reason}\"");
    }

    /// <summary>The button is available: no unavailable state, and no reason text left over.</summary>
    internal static void IsAvailable(IRenderedFragment cut, IElement el)
    {
        Assert.False(el.HasAttribute("disabled"));
        Assert.Null(el.GetAttribute("aria-disabled"));
        Assert.Equal("", ReasonOf(cut, el));
    }
}
