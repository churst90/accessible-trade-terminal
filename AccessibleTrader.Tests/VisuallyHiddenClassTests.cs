// A hidden-text class that nothing defines is not hidden text — it is a paragraph in the toolbar.
//
// The component library is an RCL. It has no stylesheet of its own; it borrows whatever the host
// page loads. So a `class="…"` in a .razor file is a PROMISE about a file in a different project,
// and nothing checked it.
//
// What was actually there on 2026-09-03: eight elements across three components used
// `class="sr-only"`, and the only definition of `.sr-only` in the entire tree was an inline
// `<style>` block inside TradingDashboardModal.razor — a component that is instantiated only when
// `Demo.AllowTrading` is true. On the public demo and the hosted build, where trading is gated
// off, that rule was never in the document and all eight rendered as visible text: four table
// captions, a "Timeframe unit" label, and — added the same day, which is how this was found — a
// 96-character sentence explaining the API-key requirement, wedged into the toolbar row next to
// the Load button. The one build where the class DID resolve was also the only build where the
// author would have seen it working.
//
// The rule here is deliberately about the PROPERTY rather than about the word "sr-only": every
// visually-hidden class the library applies must be defined by every host that renders the
// library. Adding a third host, or renaming the class in one app.css and not the other, fails
// this the same way.

using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests;

public class VisuallyHiddenClassTests
{
    /// <summary>
    /// The stylesheets a host page actually loads. Both must define whatever the RCL asks for —
    /// the WebHost and the standalone Blazor client render the same components.
    /// </summary>
    private static readonly string[] HostStylesheets =
    {
        Path.Combine("AccessibleTrader.BlazorClient", "wwwroot", "app.css"),
        Path.Combine("AccessibleTrader.WebHost", "wwwroot", "app.css"),
    };

    /// <summary>
    /// Class names whose whole job is to hide text from sight while leaving it to a screen
    /// reader. A class in this family that resolves to no rule does the opposite of its name.
    /// </summary>
    private static readonly Regex HidingClassName =
        new(@"^(sr-only|visually-hidden|screen-reader-only|visuallyhidden|a11y-hidden)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClassAttribute =
        new(@"class\s*=\s*""([^""@]*)""", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "AccessibleTrader.Core")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static bool DefinesClass(string css, string className) =>
        Regex.IsMatch(css, @"(^|[\s,}])\." + Regex.Escape(className) + @"(?![\w-])",
            RegexOptions.Multiline);

    [Fact]
    public void EveryVisuallyHiddenClassTheLibraryUsesIsDefinedByEveryHost()
    {
        string root = RepoRoot();
        var sheets = HostStylesheets
            .Select(rel => (Path: rel, Css: File.ReadAllText(Path.Combine(root, rel))))
            .ToList();
        Assert.Equal(HostStylesheets.Length, sheets.Count);

        var componentsDir = Path.Combine(root, "AccessibleTrader.BlazorClient.Components");
        var problems = new List<string>();
        int used = 0;

        foreach (var file in Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories)
                                      .OrderBy(f => f, StringComparer.Ordinal))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            string src = File.ReadAllText(file);

            foreach (Match m in ClassAttribute.Matches(src))
            {
                foreach (var name in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!HidingClassName.IsMatch(name)) continue;
                    used++;
                    int line = src[..m.Index].Count(c => c == '\n') + 1;
                    foreach (var (path, css) in sheets)
                    {
                        if (DefinesClass(css, name)) continue;
                        problems.Add($"{Path.GetFileName(file)}:{line} uses class \"{name}\", "
                                     + $"which {path} does not define — that text is VISIBLE there");
                    }
                }
            }
        }

        // Vacuity floor. "No problems" is also what a scan that matched no class attributes
        // returns, and the class-attribute regex skips any value containing '@', so a library
        // that moved to computed class names would quietly sweep nothing.
        Assert.True(used >= 6,
            $"the scan found only {used} visually-hidden class usages in the component library; "
            + "there were 8 when this floor was written, so it has stopped matching.");

        Assert.True(problems.Count == 0, string.Join("\n  ", problems));
    }

    [Fact]
    public void TheHostsAgreeOnWhichClassThatIs()
    {
        // The two app.css files are near-duplicates maintained side by side, and a rule that
        // exists in one is the exact shape of drift this guard cannot otherwise see: the sweep
        // above only looks at classes the library HAPPENS to use today.
        string root = RepoRoot();
        foreach (var rel in HostStylesheets)
        {
            var css = File.ReadAllText(Path.Combine(root, rel));
            Assert.True(DefinesClass(css, "visually-hidden"),
                $"{rel} does not define .visually-hidden, which is the class the component "
                + "library uses to hide text from sight while leaving it to a screen reader.");
        }
    }
}
