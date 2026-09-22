using System.Text.Json;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A payload the build knows how to stage, and nothing supplies, is not shipped — and the
/// build says nothing about it.</b>
///
/// <para>
/// v2.12.0 is the release that found four runtime payloads staged into <c>$(OutDir)</c> and
/// never into the publish, and fixed the MSBuild correctly. Two of the four are third-party
/// binaries that cannot live in this repository, every csproj item is <c>Exists()</c>-guarded so
/// a build without them succeeds in silence, and the release is built by GitHub Actions, which
/// had no copy of either. So the fix landed on one developer's machine and not on the artifact:
/// six published assets, a green run, and two headline claims in WHATSNEW that were untrue of
/// every one of them. An NVDA user downloading it still got a silent chart.
/// </para>
///
/// <para>
/// <see cref="PublishStagingParityTests"/> verifies the RULES by parsing the csproj, and it is
/// not at fault: whether a file will exist on the machine that runs the release is a different
/// question, and no test that reads this repository can answer it. That question is answered on
/// the artifact, by <c>scripts/verify_release_payloads.py</c>, inside the release job. These
/// tests cover the seam BETWEEN the two — that the workflow supplies what the csproj can only
/// conditionally stage, and that the artifact check runs before anything is published.
/// </para>
/// </summary>
public sealed class ReleasePayloadManifestTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    internal static JsonElement Manifest() => JsonDocument
        .Parse(File.ReadAllText(Path.Combine(RepoRoot(), "packaging", "release-payloads.json")))
        .RootElement;

    private static string ReleaseWorkflow() =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "release.yml"));

    /// <summary>
    /// Every rule, flattened, as (assetId, name, stagedBy, vendorDir).
    ///
    /// <para>
    /// <c>Name</c> is the name the BUILD stages under, which is not always the filename that
    /// has to be in the zip: the script worker is staged by a directory wildcard, so no
    /// individual file of it is ever named in a csproj. <c>stagedAs</c> in the manifest carries
    /// that difference explicitly rather than letting a parity test quietly match on a prefix.
    /// </para>
    /// </summary>
    internal static IEnumerable<(string Asset, string Name, string StagedBy, string? VendorDir)> Rules()
    {
        foreach (var asset in Manifest().GetProperty("assets").EnumerateArray())
        {
            string id = asset.GetProperty("id").GetString()!;
            foreach (var rule in asset.GetProperty("required").EnumerateArray())
            {
                string name =
                    rule.TryGetProperty("stagedAs", out var sa) ? sa.GetString()! :
                    rule.TryGetProperty("file", out var f) ? f.GetString()! :
                    rule.TryGetProperty("glob", out var g) ? g.GetString()! :
                    rule.GetProperty("anyOf").EnumerateArray().First().GetString()!;

                yield return (
                    id,
                    name,
                    rule.GetProperty("stagedBy").GetString()!,
                    rule.TryGetProperty("vendorDir", out var v) ? v.GetString() : null);
            }
        }
    }

    /// <summary>
    /// Every rule says what it is for and how it gets there. The <c>why</c> is not decoration:
    /// it is what the release job prints when the check fails, and a payload nobody can explain
    /// is a payload someone will delete to make a red build green.
    /// </summary>
    [Fact]
    public void EveryPayloadRuleSaysWhyItMattersAndHowItIsStaged()
    {
        var known = new[] { "csproj-item", "msbuild-target", "project-reference" };

        foreach (var asset in Manifest().GetProperty("assets").EnumerateArray())
        {
            string id = asset.GetProperty("id").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(asset.GetProperty("match").GetString()),
                $"asset {id} has no filename pattern, so it matches nothing and checks nothing");

            foreach (var rule in asset.GetProperty("required").EnumerateArray())
            {
                Assert.True(rule.TryGetProperty("why", out var why)
                            && !string.IsNullOrWhiteSpace(why.GetString()),
                    $"a rule in {id} does not say why it matters");
                Assert.True(rule.TryGetProperty("stagedBy", out var by)
                            && known.Contains(by.GetString()),
                    $"a rule in {id} has no recognised stagedBy (one of {string.Join(", ", known)})");
            }
        }
    }

    /// <summary>
    /// <b>The workflow must SUPPLY every payload that is not in the repository.</b>
    ///
    /// <para>
    /// This is the test that would have caught v2.12.0 without waiting for a release. Each of
    /// these payloads is gitignored, so the csproj item that stages it is <c>Exists()</c>-guarded
    /// and a build that lacks it succeeds quietly. Somebody has to put the file there before the
    /// publish runs, and on a CI runner that somebody is <c>release.yml</c>.
    /// </para>
    ///
    /// <para>
    /// The assertion is deliberately about the DIRECTORY the csproj reads from, not about a step
    /// name or a URL: rename the step, change the vendor, switch from a zip to a NuGet package —
    /// all fine. Stop writing the file the build looks for, and this goes red.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("vendor/nvda", "the NVDA controller client — without it the desktop chart is SILENT, because the canvas is a native control and the ARIA live region cannot reach a reader focused on it")]
    [InlineData("dotpad-sdk/Windows/dotpad-3.0.0", "the Dot Pad tactile SDK — without it the Braille tab renders and does nothing, which is harder to notice than a missing tab")]
    [InlineData("vendor/vcruntime", "the VC++ runtime the Dot Pad SDK statically imports — without it the SDK does not load at all on a machine that has no redistributable installed")]
    public void TheReleaseWorkflowSuppliesEveryPayloadThatIsNotInTheRepository(string vendorDir, string what)
    {
        // Named by the manifest as a vendored payload, so the two halves agree on the list.
        Assert.Contains(Rules(), r => r.VendorDir == vendorDir);

        var workflow = ReleaseWorkflow();

        Assert.Contains(vendorDir, workflow, StringComparison.Ordinal);
        Assert.True(
            workflow.Contains($"'{vendorDir}'", StringComparison.Ordinal)
            || workflow.Contains($"Path {vendorDir}", StringComparison.Ordinal)
            || workflow.Contains($"-Path {vendorDir}", StringComparison.Ordinal)
            || workflow.Contains($"$dest = '{vendorDir}'", StringComparison.Ordinal)
            || workflow.Contains($"{vendorDir}/", StringComparison.Ordinal),
            $"release.yml never writes into {vendorDir}. That directory holds {what}, it is "
          + "gitignored, and the csproj item that stages it is Exists()-guarded — so the release "
          + "runner will publish a build without it and NOTHING will say so. That is exactly how "
          + "v2.12.0 shipped claiming payloads it did not contain.");
    }

    /// <summary>
    /// Every vendored payload is pinned by checksum. A vendor who re-cuts a file under the same
    /// URL should produce a failed release, never a silently different binary in the speech or
    /// tactile path.
    /// </summary>
    [Fact]
    public void EveryFetchedPayloadIsPinnedByChecksum()
    {
        var workflow = ReleaseWorkflow();

        Assert.Contains("SHA256", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mismatch", workflow, StringComparison.OrdinalIgnoreCase);

        // One digest per Dot Pad file plus the NVDA zip. Counting them is crude and it is the
        // property that matters: a payload added to the fetch without a digest drops the count.
        int digests = System.Text.RegularExpressions.Regex
            .Matches(workflow, @"'[0-9A-F]{64}'").Count;
        Assert.True(digests >= 8,
            $"only {digests} SHA-256 digests in release.yml; every third-party binary fetched "
          + "into the build must be pinned, and there are at least eight of them (the NVDA "
          + "controller-client zip and seven Dot Pad SDK files).");
    }

    /// <summary>
    /// <b>The artifact check runs BEFORE the release is published, or it is decoration.</b>
    ///
    /// <para>
    /// Ordering is the whole value. A payload check that runs after
    /// <c>softprops/action-gh-release</c> reports a red job on a release the world can already
    /// download, which is strictly worse than no check at all: it looks like coverage.
    /// </para>
    /// </summary>
    [Fact]
    public void TheArtifactPayloadCheckRunsBeforeThePublishStep()
    {
        var workflow = ReleaseWorkflow();

        int verify = workflow.IndexOf("verify_release_payloads.py", StringComparison.Ordinal);
        Assert.True(verify > 0,
            "release.yml never runs scripts/verify_release_payloads.py, so nothing reads the "
          + "built artifacts before they are published. PublishStagingParityTests checks the "
          + "csproj rules and structurally cannot tell you whether the file existed on the "
          + "runner — only the artifact can.");

        int publish = workflow.IndexOf("softprops/action-gh-release", StringComparison.Ordinal);
        Assert.True(publish > 0, "release.yml no longer publishes a GitHub Release");

        Assert.True(verify < publish,
            "the payload check runs AFTER the release is published. A red job on a release "
          + "people are already downloading is not a gate, it is a notification.");
    }

    /// <summary>
    /// The verifier and its manifest both exist where the workflow says they are. A workflow
    /// step that silently cannot find its script is a step that passes.
    /// </summary>
    [Fact]
    public void TheVerifierAndItsManifestExist()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "scripts", "verify_release_payloads.py")));
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "packaging", "release-payloads.json")));
    }

    /// <summary>
    /// Both heads take the plugin-trust manifest from the SAME file.
    ///
    /// <para>
    /// It used to be a copy in each csproj, and both bugs that produced came from the copying:
    /// the publish-time target existed on the WebHost for its whole life and was never applied
    /// to the desktop head, which therefore shipped 33 plugin DLLs and no manifest; and when it
    /// was finally copied across, the copy inherited the assumption that the manifest belongs at
    /// <c>$(PublishDir)</c> — false on Mac Catalyst, where the plugins and
    /// <c>AppContext.BaseDirectory</c> are both inside <c>Contents/MonoBundle</c>.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("AccessibleTrader.BlazorClient/AccessibleTrader.BlazorClient.csproj")]
    [InlineData("AccessibleTrader.WebHost/AccessibleTrader.WebHost.csproj")]
    public void BothHeadsImportTheSharedPluginTrustTargets(string project)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), project.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("packaging", text, StringComparison.Ordinal);
        Assert.Contains("PluginTrustManifest.targets", text, StringComparison.Ordinal);
        Assert.False(text.Contains("<UsingTask TaskName=\"HashPluginDlls\"", StringComparison.Ordinal),
            $"{project} defines its own copy of HashPluginDlls again. Two copies is how the "
          + "desktop head went its entire life without the publish-time manifest the WebHost had "
          + "had all along. Import packaging/PluginTrustManifest.targets instead.");
    }

    /// <summary>
    /// The manifest lands where the PLUGINS are, which is not <c>$(PublishDir)</c> on every head.
    /// <c>PluginTrustPolicy</c> reads it from <c>AppContext.BaseDirectory</c>, and on Mac Catalyst
    /// that is <c>YourApp.app/Contents/MonoBundle</c> — one directory INSIDE the thing the
    /// workflow zips, while <c>$(PublishDir)</c> is the directory it sits in.
    /// </summary>
    [Fact]
    public void ThePluginManifestIsWrittenBesideThePluginsAndNotOnlyAtPublishDir()
    {
        var targets = File.ReadAllText(Path.Combine(RepoRoot(), "packaging", "PluginTrustManifest.targets"));

        Assert.Contains("WriteBesidePlugins", targets, StringComparison.Ordinal);
        Assert.Contains("WriteBesidePlugins=\"true\"", targets, StringComparison.Ordinal);
    }
}
