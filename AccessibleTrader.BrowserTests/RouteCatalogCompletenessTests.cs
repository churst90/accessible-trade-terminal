namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Keeps the sweep honest about its own coverage.
///
/// <para>
/// A sweep that exercises 21 of 25 dialogs and reports no failures is reporting 84% as 100%. This
/// runs without a browser, so even on a machine where the whole browser suite skips, a new dialog
/// added to the component library still fails the build until someone decides how it is reached.
/// </para>
/// </summary>
public sealed class RouteCatalogCompletenessTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> ModalComponentNames() =>
        Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components"),
                "*Modal.razor", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal);

    [Fact]
    public void Every_modal_in_the_component_library_is_either_routed_or_explained()
    {
        var routed = ModalRoutes.All.Select(r => r.Modal).ToHashSet(StringComparer.Ordinal);

        var missing = ModalComponentNames()
            .Where(n => !routed.Contains(n) && !ModalRoutes.NoColdStartRoute.ContainsKey(n))
            .ToList();

        Assert.True(missing.Count == 0,
            "These dialogs exist in AccessibleTrader.BlazorClient.Components and the browser sweep " +
            "neither opens them nor records why it cannot:\n  " + string.Join("\n  ", missing) +
            "\n\nAdd a route to ModalRoutes, or an entry to NoColdStartRoute saying what it needs.");
    }

    [Fact]
    public void Nothing_is_excluded_that_the_sweep_can_actually_reach()
    {
        var routed = ModalRoutes.All.Select(r => r.Modal).ToHashSet(StringComparer.Ordinal);

        var contradictions = ModalRoutes.NoColdStartRoute.Keys.Where(routed.Contains).ToList();

        Assert.True(contradictions.Count == 0,
            "These are listed as unreachable AND have a route — one of the two is stale:\n  "
            + string.Join("\n  ", contradictions));
    }

    /// <summary>
    /// The floor. Without it, a rename of the components folder turns the whole catalog into an
    /// empty list that agrees with everything.
    /// </summary>
    [Fact]
    public void The_catalog_is_not_empty()
    {
        Assert.True(ModalComponentNames().Count() >= 20,
            $"Only {ModalComponentNames().Count()} *Modal.razor files found — the component " +
            "library has around 25. Check the path this test scans.");
        Assert.True(ModalRoutes.All.Count() >= 20);
    }
}
