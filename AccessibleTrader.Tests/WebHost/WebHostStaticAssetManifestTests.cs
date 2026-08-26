using System.Text.Json;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Asserts that the WebHost's built static-asset manifest actually names the two assets a Blazor
/// Server page cannot start without.
///
/// <para>
/// The failure this guards is silent and total. <c>OutputType=WinExe</c> makes the SDK's
/// static-web-assets targets drop <c>_framework/blazor.web.js</c> and the RCL scoped-CSS bundle
/// from <c>AccessibleTrader.WebHost.staticwebassets.endpoints.json</c>. Kestrel still starts, the
/// shell document is still served with HTTP 200, and every server-side test that drives the host
/// through <c>HttpClient</c> still passes — because none of them ask for the framework script. Only
/// a real browser notices, and what it reports is "the page is blank".
/// </para>
///
/// <para>
/// It is not hypothetical. On 2026-08-26 it took down the entire Chromium harness job: all 45
/// tests that got to run failed on <c>waiting for Locator("#main-heading")</c>, 30 seconds apiece,
/// until GitHub killed the job at its 25-minute cap. The heading is not server-rendered
/// (<c>App.razor</c> mounts the app tree with <c>prerender: false</c>), so a missing
/// <c>blazor.web.js</c> means no circuit, and no circuit means the document never gains a single
/// element the harness looks for.
/// </para>
///
/// <para>
/// <c>docs/TODO.md</c> proposed two guards for this: scan the csproj for the <c>OutputType</c>
/// condition, or publish to a temp directory and inspect the result. This does neither. Scanning
/// the csproj asserts the incantation rather than the artifact — it would have stayed green
/// through the very regression above, because the condition it looks for was present and correct
/// the whole time and simply did not cover the configuration the harness built. Publishing is
/// honest but costs minutes. Reading the manifest that the ordinary build already copied next to
/// this assembly is both: it asserts the real artifact, and it costs a file read.
/// </para>
/// </summary>
public sealed class WebHostStaticAssetManifestTests
{
    private const string ManifestName = "AccessibleTrader.WebHost.staticwebassets.endpoints.json";

    /// <summary>
    /// The framework script. Its absence is what a blank Blazor page actually is.
    /// </summary>
    [Fact]
    public void The_manifest_serves_blazor_web_js()
        => AssertRouteExists(
            "_framework/blazor.web.js",
            "The Blazor circuit cannot start: the browser fetches this script from the shell "
            + "document and gets a 404, so no interactive component ever renders.");

    /// <summary>
    /// The scoped-CSS bundle, which the csproj comment and <c>docs/SERVER_SETUP.md</c> both list
    /// alongside the framework script as something <c>WinExe</c> drops.
    ///
    /// <para>
    /// UNPROVEN, deliberately recorded as such. Reintroducing the <c>WinExe</c> condition on Linux
    /// and rebuilding dropped all 12 <c>_framework</c> routes but left this bundle present, so this
    /// assertion stayed green through the sabotage that turned its sibling red. Either the bundle
    /// is dropped only by <c>publish</c> and not by <c>build</c>, or the received wisdom is wrong
    /// about it. Until someone demonstrates it failing, treat this as an invariant worth holding
    /// rather than a guard known to catch anything — the sibling test above is the one carrying
    /// the weight.
    /// </para>
    /// </summary>
    [Fact]
    public void The_manifest_serves_the_component_scoped_css_bundle()
        => AssertRouteExists(
            "_content/AccessibleTrader.BlazorClient.Components/"
            + "AccessibleTrader.BlazorClient.Components.bundle.scp.css",
            "Every *.razor.css scoped style is missing, so the app renders unstyled.");

    private static void AssertRouteExists(string route, string consequence)
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, ManifestName);

        Assert.True(File.Exists(manifestPath),
            $"No {ManifestName} beside the test assembly ({AppContext.BaseDirectory}). "
            + "The WebHost's static-asset manifest is supposed to be copied here by the project "
            + "reference; if it is not, this guard is vacuous and needs fixing rather than skipping.");

        var routes = ReadRoutes(manifestPath);

        // A manifest that parsed to nothing would make every assertion below trivially true.
        Assert.True(routes.Count > 0,
            $"{ManifestName} parsed to zero routes — the guard cannot conclude anything. "
            + "Check the manifest schema rather than trusting a green result.");

        Assert.True(routes.Contains(route),
            $"""
             {ManifestName} does not serve "{route}".
             {consequence}

             The cause is almost always OutputType: Release sets WinExe unless
             ServerPublish=true, and WinExe makes the SDK drop these assets from the manifest
             on every platform, not just Windows. See the OutputType comment in
             AccessibleTrader.WebHost.csproj for the measured route counts.

             Manifest: {manifestPath}
             It currently serves {routes.Count} routes, {routes.Count(r => r.StartsWith("_framework/", StringComparison.Ordinal))} of them under _framework/.
             """);
    }

    private static HashSet<string> ReadRoutes(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));

        if (!doc.RootElement.TryGetProperty("Endpoints", out var endpoints)
            || endpoints.ValueKind != JsonValueKind.Array)
            return new HashSet<string>(StringComparer.Ordinal);

        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints.EnumerateArray())
            if (endpoint.TryGetProperty("Route", out var route) && route.GetString() is { } value)
                routes.Add(value);

        return routes;
    }
}
