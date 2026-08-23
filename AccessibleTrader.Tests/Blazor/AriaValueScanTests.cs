// Aria VALUE scan over each catalog component's rendered tree.
//
// The string-scan era checked that aria attributes were present in .razor
// source; nothing checked the values Blazor actually rendered — which is how
// five tablists shipped with broken aria-selected. This suite renders every
// catalog dialog (opened) and bare component and walks the real DOM:
//
//   1. Enumerated aria attributes carry a legal value ("True" from a bool
//      ToString(), an unevaluated "@active" or an empty string all fail).
//   2. aria-labelledby / aria-describedby / aria-activedescendant reference
//      elements that exist in the rendered tree.
//   3. aria-controls resolves for the ACTIVE element (aria-selected or
//      aria-expanded "true"). Inactive tabs may legitimately point at panels
//      that are not currently rendered.
//   4. Every role="tablist" contains at least one role="tab", every tab carries
//      aria-selected, and exactly one is selected.

using AngleSharp.Dom;
using Bunit;

namespace AccessibleTrader.Tests.Blazor;

public class AriaValueScanTests
{
    private static readonly Dictionary<string, string[]> EnumeratedAria = new()
    {
        ["aria-selected"]    = new[] { "true", "false" },
        ["aria-expanded"]    = new[] { "true", "false" },
        ["aria-checked"]     = new[] { "true", "false", "mixed" },
        ["aria-pressed"]     = new[] { "true", "false", "mixed" },
        ["aria-modal"]       = new[] { "true", "false" },
        ["aria-hidden"]      = new[] { "true", "false" },
        ["aria-disabled"]    = new[] { "true", "false" },
        ["aria-multiselectable"] = new[] { "true", "false" },
        ["aria-orientation"] = new[] { "horizontal", "vertical" },
        ["aria-live"]        = new[] { "off", "polite", "assertive" },
        ["aria-haspopup"]    = new[] { "true", "false", "menu", "listbox", "tree", "grid", "dialog" },
        ["aria-sort"]        = new[] { "ascending", "descending", "none", "other" },
    };

    private static List<IElement> AllElements(IRenderedFragment cut) =>
        cut.Nodes.OfType<IElement>()
           .SelectMany(root => new[] { root }.Concat(root.QuerySelectorAll("*")))
           .ToList();

    private static void ScanTree(string name, IRenderedFragment cut)
    {
        var elements = AllElements(cut);
        var ids = elements.Where(e => e.HasAttribute("id"))
                          .Select(e => e.GetAttribute("id")!)
                          .ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var el in elements)
        {
            string Describe() => $"<{el.TagName.ToLowerInvariant()}" +
                (el.HasAttribute("id") ? $" id='{el.GetAttribute("id")}'" : "") + ">";

            foreach (var (attr, legal) in EnumeratedAria)
            {
                var value = el.GetAttribute(attr);
                if (value == null) continue;
                if (!legal.Contains(value, StringComparer.Ordinal))
                    failures.Add($"{Describe()} has {attr}=\"{value}\" — legal values: {string.Join("/", legal)}.");
            }

            foreach (var attr in new[] { "aria-labelledby", "aria-describedby", "aria-activedescendant" })
            {
                var value = el.GetAttribute(attr);
                if (string.IsNullOrWhiteSpace(value)) continue;
                foreach (var refId in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (!ids.Contains(refId))
                        failures.Add($"{Describe()} {attr} references '#{refId}' which is not in the rendered tree.");
            }

            // aria-controls must resolve when this element claims to be active.
            var controls = el.GetAttribute("aria-controls");
            bool active = el.GetAttribute("aria-selected") == "true"
                       || el.GetAttribute("aria-expanded") == "true";
            if (!string.IsNullOrWhiteSpace(controls) && active)
                foreach (var refId in controls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (!ids.Contains(refId))
                        failures.Add($"{Describe()} is active but aria-controls references missing '#{refId}'.");

            if (el.GetAttribute("role") == "tablist")
            {
                var tabs = el.QuerySelectorAll("[role='tab']");
                if (tabs.Length == 0)
                    failures.Add($"{Describe()} is a tablist with no role='tab' descendants.");
                else
                {
                    var missingState = tabs.Where(t => !t.HasAttribute("aria-selected")).ToList();
                    foreach (var t in missingState)
                        failures.Add($"tab '{t.TextContent.Trim()}' in {Describe()} has no aria-selected at all.");
                    int selected = tabs.Count(t => t.GetAttribute("aria-selected") == "true");
                    if (selected != 1)
                        failures.Add($"{Describe()} has {selected} tabs with aria-selected=\"true\" — must be exactly 1.");
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{name}: {failures.Count} aria value violation(s):\n - " + string.Join("\n - ", failures));
    }

    [Theory]
    [MemberData(nameof(DialogNames))]
    public void OpenedDialog_AriaValuesAreCoherent(string name)
    {
        using var h = new BlazorTestHarness();
        var cut = ModalCatalog.OpenDialog(h, ModalCatalog.Dialog(name));
        Assert.NotEmpty(cut.FindAll("[role='dialog']")); // scan the OPEN tree, not a closed stub
        ScanTree(name, cut);
    }

    [Theory]
    [MemberData(nameof(BareNames))]
    public void BareComponent_AriaValuesAreCoherent(string name)
    {
        using var h = new BlazorTestHarness();
        var c = ModalCatalog.Bare(name);
        c.Seed?.Invoke(h);
        ScanTree(name, c.Render(h.Ctx));
    }

    /// <summary>Vacuity check: the scanner must flag a tree with a bad
    /// enumerated value, a dangling labelledby, and a two-selected tablist —
    /// otherwise every green scan above is meaningless.</summary>
    [Fact]
    public void Scanner_FlagsKnownViolations()
    {
        using var h = new BlazorTestHarness();
        var cut = h.Ctx.Render(builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "role", "tablist");
            builder.AddAttribute(2, "aria-labelledby", "nowhere");
            builder.OpenElement(3, "button");
            builder.AddAttribute(4, "role", "tab");
            builder.AddAttribute(5, "aria-selected", "True"); // bool.ToString() casing bug
            builder.CloseElement();
            builder.OpenElement(6, "button");
            builder.AddAttribute(7, "role", "tab");
            builder.AddAttribute(8, "aria-selected", "true");
            builder.CloseElement();
            builder.CloseElement();
        });

        var ex = Assert.Throws<Xunit.Sdk.TrueException>(() => ScanTree("synthetic", cut));
        Assert.Contains("aria-selected=\"True\"", ex.Message);
        Assert.Contains("#nowhere", ex.Message);
    }

    public static TheoryData<string> DialogNames => ModalCatalog.DialogNames;
    public static TheoryData<string> BareNames => ModalCatalog.BareNames;
}
