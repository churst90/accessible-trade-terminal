using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Anything a head needs at runtime must reach its PUBLISH output, not merely its build
/// output — and the two are different directories.</b>
///
/// <para>
/// This became a test on 2026-09-21 after the same defect was found three times in one day, in
/// three unrelated files, on the same head. A `dotnet publish` does not carry loose files that a
/// custom target dropped into <c>$(OutDir)</c>, so anything staged that way works on a
/// developer's machine and is absent from every release:
/// </para>
///
/// <list type="number">
/// <item><c>nvdaControllerClient64.dll</c>, which was staged by nothing at all — and when that
/// was fixed, it was fixed with an <c>AfterTargets="Build"</c> copy that had this very
/// bug.</item>
/// <item><c>plugins_trusted.manifest</c>, absent from every released desktop zip, so the trust
/// policy loaded an empty allow-list and refused all 33 plugin DLLs — the market dropdown
/// offering only the built-in providers. <b>The WebHost had had the publish-time target for this
/// since its own publish shipped without one, comment and all; the desktop head never got
/// it.</b></item>
/// <item>The Dot Pad SDK and the ScriptWorker, still staged into <c>$(OutDir)</c> only, and
/// pinned below as known exceptions rather than left to be rediscovered.</item>
/// </list>
///
/// <para>
/// The lesson the test encodes is the second one: <b>the heads have separate project files, so a
/// fix landed in one of them is not a fix — it is a fix in one place.</b>
/// </para>
/// </summary>
public sealed class PublishStagingParityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Every project file that ships a runnable head.</summary>
    public static TheoryData<string> HeadProjects() => new()
    {
        Path.Combine("AccessibleTrader.BlazorClient", "AccessibleTrader.BlazorClient.csproj"),
        Path.Combine("AccessibleTrader.WebHost", "AccessibleTrader.WebHost.csproj"),
    };

    /// <summary>
    /// <b>The plugin trust manifest must be generated at PUBLISH time on every head.</b>
    ///
    /// <para>
    /// A build-time manifest alone is worse than none: the published zip carries the plugin DLLs
    /// and no manifest, so <c>PluginTrustPolicy.RequireTrusted</c> enforces an empty allow-list
    /// and refuses every one of them. The application then behaves exactly as though no plugins
    /// were installed — which is a symptom nobody reads as "a manifest is missing".
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(HeadProjects))]
    public void EveryHeadGeneratesThePluginTrustManifestAtPublishTime(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        Assert.True(
            text.Contains("plugins_trusted.manifest", StringComparison.Ordinal),
            $"{relativePath} does not generate a plugin trust manifest at all.");

        var publishTargets = TargetsWith(text, afterTargets: "Publish");
        Assert.True(
            publishTargets.Any(t => t.Contains("$(PublishDir)plugins_trusted.manifest", StringComparison.Ordinal)),
            $"{relativePath} writes plugins_trusted.manifest but not into $(PublishDir) from an "
          + "AfterTargets=\"Publish\" target. A publish does NOT carry a file a custom target dropped "
          + "into $(OutDir), so the released zip will ship the plugin DLLs with no manifest and "
          + "RequireTrusted will refuse every one of them — the market dropdown showing only the "
          + "built-in providers, with nothing anywhere saying why. See "
          + "GeneratePluginTrustManifestOnPublish in AccessibleTrader.WebHost.csproj.");
    }

    /// <summary>
    /// The manifest must hash the PUBLISHED assemblies, not the built ones. A digest taken over
    /// <c>$(OutDir)</c> and shipped beside <c>$(PublishDir)</c>'s files is a claim about the
    /// wrong bytes, and the refusal it produces looks identical to a missing manifest.
    /// </summary>
    [Theory]
    [MemberData(nameof(HeadProjects))]
    public void ThePublishManifestHashesThePublishedAssemblies(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        var publishTargets = TargetsWith(text, afterTargets: "Publish")
            .Where(t => t.Contains("plugins_trusted.manifest", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(publishTargets);
        Assert.All(publishTargets, t => Assert.True(
            t.Contains("PluginsDir=\"$(PublishDir)\"", StringComparison.Ordinal),
            $"{relativePath}'s publish-time manifest target hashes a directory other than "
          + "$(PublishDir), so the digests describe files that are not the ones shipping."));
    }

    /// <summary>
    /// <b>The known exceptions, pinned rather than left to be rediscovered.</b> Both of these
    /// stage native payloads into <c>$(OutDir)</c> only and therefore do not reach a published
    /// build. Neither has been demonstrated broken — showing it needs the vendor SDK present and
    /// a Windows publish to observe — so they are recorded here as OPEN rather than asserted
    /// either way. When one is fixed, delete its line; when this list is empty, delete the test.
    /// </summary>
    [Fact]
    public void TheRemainingOutDirOnlyStagingIsRecorded()
    {
        var text = File.ReadAllText(Path.Combine(
            RepoRoot(), "AccessibleTrader.BlazorClient", "AccessibleTrader.BlazorClient.csproj"));

        var known = new[]
        {
            "CopyDotPadSdkWindows", // the Dot Pad tactile SDK — the device this app is built for
            "CopyScriptWorker",     // the out-of-process Roslyn script worker
        };

        foreach (var name in known)
        {
            Assert.True(text.Contains($"Name=\"{name}\"", StringComparison.Ordinal),
                $"{name} is gone from the csproj. If it was fixed or removed, drop it from this list; "
              + "it is recorded here because it stages a runtime payload into $(OutDir) only and so "
              + "does not reach a published build.");
        }

        // If one of these ever gains a publish-time counterpart, this test should stop claiming it
        // is outstanding.
        var publishTargets = TargetsWith(text, afterTargets: "Publish");
        Assert.DoesNotContain(publishTargets,
            t => t.Contains("DotPadSdk", StringComparison.Ordinal) || t.Contains("ScriptWorker", StringComparison.Ordinal));
    }

    /// <summary>The bodies of every <c>&lt;Target&gt;</c> whose AfterTargets names the given target.</summary>
    private static List<string> TargetsWith(string projectXml, string afterTargets)
    {
        var doc = XDocument.Parse(projectXml);
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "Target")
            .Where(e => Regex.IsMatch((string?)e.Attribute("AfterTargets") ?? "",
                                      $@"\b{Regex.Escape(afterTargets)}\b"))
            .Select(e => e.ToString())
            .ToList();
    }
}
