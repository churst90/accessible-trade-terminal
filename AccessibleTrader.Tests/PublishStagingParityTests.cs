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
/// <item>The Dot Pad SDK and the ScriptWorker — recorded here as undemonstrated exceptions when
/// this file was written, and demonstrated a few hours later by unzipping the CI artifact:
/// neither was in it. Tactile support has been disabled in every released Windows build, and no
/// user-compiled indicator or strategy could run at all.</item>
/// </list>
///
/// <para>
/// The lesson the test encodes is the second one: <b>the heads have separate project files, so a
/// fix landed in one of them is not a fix — it is a fix in one place.</b>
/// </para>
///
/// <para>
/// <b>What this file learned on 2026-09-22, from reading the published v2.12.0 assets.</b> Two
/// things, and both are about the limits of reading a csproj:
/// </para>
///
/// <list type="bullet">
/// <item>Verifying the RULES is not verifying the ARTIFACT. Every rule below passed while the
/// release shipped without the NVDA client and without the Dot Pad SDK, because both are
/// gitignored vendor binaries, every item that stages them is <c>Exists()</c>-guarded, and the
/// release runner had neither. No test that reads this repository can answer "will the file be
/// there?". <c>scripts/verify_release_payloads.py</c> answers it against the zip, in the release
/// job; <see cref="ReleasePayloadManifestTests"/> covers the seam.</item>
/// <item>The payload list is no longer written here. It is
/// <c>packaging/release-payloads.json</c>, which the artifact check reads too — because a list
/// of required payloads kept in two places is the same shape as a build target kept in two
/// places, and that is the defect this whole file is about.</item>
/// </list>
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
    /// The project AND the repo-local files it imports, because the shared logic now lives in
    /// one of those.
    ///
    /// <para>
    /// These tests used to read the csproj as a single string, which was correct while every
    /// head carried its own copy of the plugin-trust task and targets. Those copies were the
    /// defect — so they were unified into <c>packaging/PluginTrustManifest.targets</c>, and a
    /// test that still read only the csproj would have gone red on the FIX. Following the
    /// import is what lets the guard survive the code being made right.
    /// </para>
    /// </summary>
    private static List<XDocument> ProjectAndImports(string relativePath)
    {
        var root = RepoRoot();
        var docs = new List<XDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Load(string fullPath)
        {
            fullPath = Path.GetFullPath(fullPath);
            if (!seen.Add(fullPath) || !File.Exists(fullPath)) return;

            var doc = XDocument.Parse(File.ReadAllText(fullPath));
            docs.Add(doc);

            string dir = Path.GetDirectoryName(fullPath)!;
            foreach (var import in doc.Descendants().Where(e => e.Name.LocalName == "Import"))
            {
                string? spec = (string?)import.Attribute("Project");
                if (string.IsNullOrWhiteSpace(spec)) continue;

                // Only follow imports into this repository. $(MSBuildThisFileDirectory) is the
                // one property these files use; anything else (SDK imports, NuGet props) is not
                // ours and not readable as a path.
                spec = spec.Replace("$(MSBuildThisFileDirectory)", dir + Path.DirectorySeparatorChar)
                           .Replace('\\', Path.DirectorySeparatorChar);
                if (spec.Contains('$')) continue;

                Load(Path.IsPathRooted(spec) ? spec : Path.Combine(dir, spec));
            }
        }

        Load(Path.Combine(root, relativePath));
        Assert.NotEmpty(docs);
        return docs;
    }

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
        var docs = ProjectAndImports(relativePath);

        Assert.True(
            docs.Any(d => d.ToString().Contains("plugins_trusted.manifest", StringComparison.Ordinal)),
            $"{relativePath} does not generate a plugin trust manifest at all.");

        var publishTargets = TargetsWith(docs, afterTargets: "Publish");
        Assert.True(
            publishTargets.Any(t => t.Contains("$(PublishDir)plugins_trusted.manifest", StringComparison.Ordinal)),
            $"{relativePath} writes plugins_trusted.manifest but not into $(PublishDir) from an "
          + "AfterTargets=\"Publish\" target. A publish does NOT carry a file a custom target dropped "
          + "into $(OutDir), so the released zip will ship the plugin DLLs with no manifest and "
          + "RequireTrusted will refuse every one of them — the market dropdown showing only the "
          + "built-in providers, with nothing anywhere saying why. See "
          + "GeneratePluginTrustManifestOnPublish in packaging/PluginTrustManifest.targets.");
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
        var publishTargets = TargetsWith(ProjectAndImports(relativePath), afterTargets: "Publish")
            .Where(t => t.Contains("plugins_trusted.manifest", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(publishTargets);
        Assert.All(publishTargets, t => Assert.True(
            t.Contains("PluginsDir=\"$(PublishDir)\"", StringComparison.Ordinal),
            $"{relativePath}'s publish-time manifest target hashes a directory other than "
          + "$(PublishDir), so the digests describe files that are not the ones shipping."));
    }

    /// <summary>
    /// <b>Every runtime payload the desktop head loads by PATH must reach the publish output.</b>
    ///
    /// <para>
    /// These were recorded as known, undemonstrated exceptions when this file was written, and
    /// demonstrated a few hours later by unzipping the CI artifact: neither the Dot Pad SDK nor
    /// <c>AccessibleTrader.ScriptWorker.exe</c> was in it. So every released Windows build has
    /// had tactile support disabled (the driver's own log line is "NativeLibrary.TryLoad failed
    /// ... Tactile DISABLED") and no way to run a user-compiled indicator or strategy, because
    /// Release refuses the in-process path by design.
    /// </para>
    ///
    /// <para>
    /// <b>The Braille tab is why this needed a test rather than a note.</b> It is ordinary DOM,
    /// so it renders perfectly on a build that cannot load the SDK — the feature looks present
    /// and is inert, which is harder to notice than a feature that is plainly missing.
    /// </para>
    ///
    /// <para>
    /// The cases come from <c>packaging/release-payloads.json</c> rather than from InlineData
    /// here, so this test and the artifact check in the release job are driven by ONE list. A
    /// payload added to the build and not to the check, or to the check and not to the build, is
    /// now a failure in this file instead of a discovery made by reading a published zip.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(WindowsPayloadsFromTheManifest))]
    public void EveryNativePayloadTheDesktopHeadLoadsByPath_ReachesThePublishOutput(string payload)
    {
        var docs = ProjectAndImports(Path.Combine(
            "AccessibleTrader.BlazorClient", "AccessibleTrader.BlazorClient.csproj"));

        // PARSED, not pattern-matched. The first draft of this asked whether the payload's name
        // appeared within 400 characters of the string "CopyToPublishDirectory", and a sabotage
        // that deleted the real one passed — because the name also occurs in the OutDir-only
        // target, in the missing-SDK warning text and in the comments, and one of those landed
        // near a DIFFERENT item's metadata. A guard over a file format should read the format.
        bool publishItem = docs.SelectMany(d => d.Descendants())
            .Where(e => e.Name.LocalName == "None")
            .Where(e => ((string?)e.Attribute("Include") ?? "").Contains(payload, StringComparison.OrdinalIgnoreCase))
            .Any(e => e.Elements().Any(c => c.Name.LocalName == "CopyToPublishDirectory")
                   || e.Attribute("CopyToPublishDirectory") != null);

        // …or a target that runs after Publish and copies the payload INTO $(PublishDir).
        bool publishTarget = docs.SelectMany(d => d.Descendants())
            .Where(e => e.Name.LocalName == "Target")
            .Where(e => Regex.IsMatch((string?)e.Attribute("AfterTargets") ?? "", @"\bPublish\b"))
            .Any(e =>
            {
                var body = e.ToString();
                return body.Contains(payload, StringComparison.OrdinalIgnoreCase)
                    && body.Contains("$(PublishDir)", StringComparison.Ordinal);
            });

        Assert.True(publishItem || publishTarget,
            $"{payload} is staged into $(OutDir) only, so it reaches a `dotnet build` and NOT a "
          + "`dotnet publish` — the released zip ships without it and the feature is inert. "
          + "Give it a <None> item with a CopyToPublishDirectory child, or an "
          + "AfterTargets=\"Publish\" target that copies it into $(PublishDir). "
          + "(This case comes from packaging/release-payloads.json.)");
    }

    /// <summary>
    /// The Windows payloads the manifest names, minus the ones a ProjectReference brings in on
    /// its own — those are not staged by a rule anyone can get wrong in the way this test looks
    /// for. Globs lose their wildcard; the item is matched on the directory name.
    /// </summary>
    public static TheoryData<string> WindowsPayloadsFromTheManifest()
    {
        var data = new TheoryData<string>();

        // DISTINCT, and the first draft was not. Several manifest rules legitimately resolve to
        // one staged name — the script worker's apphost and its runtimeconfig.json are separate
        // requirements of the ARTIFACT and a single wildcard in the build. Two theory cases with
        // the same argument produce the same xUnit test ID, and xUnit's response is to SKIP the
        // duplicate with a console line nobody reads. A case that silently stops running is the
        // failure mode this entire file exists to prevent, so it is not allowed to happen here.
        foreach (var name in ReleasePayloadManifestTests.Rules()
                     .Where(r => r.Asset == "maui-windows" && r.StagedBy != "project-reference")
                     .Select(r => r.Name.TrimEnd('*', '/'))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            data.Add(name);
        }

        Assert.NotEmpty(data);
        return data;
    }

    /// <summary>
    /// <b>The WebHost stages the script worker, and forwards the RID and self-contained flag
    /// when it does.</b>
    ///
    /// <para>
    /// Until 2026-09-22 <c>grep -n ScriptWorker AccessibleTrader.WebHost.csproj</c> found
    /// nothing, while <c>RoslynScriptingService.DefaultWorkerPathResolver</c> resolves the worker
    /// from <c>AppContext.BaseDirectory</c> on every head and Release refuses the in-process
    /// path by design. User-compiled indicators and strategies therefore could not run at all on
    /// the package the site recommends first.
    /// </para>
    ///
    /// <para>
    /// The forwarding is not a detail. This head publishes SELF-CONTAINED per RID, so its
    /// directory carries a private .NET runtime and the user is promised they need no .NET
    /// install. A framework-dependent worker apphost dropped beside it resolves its runtime
    /// through hostfxr's INSTALL search and never through the sibling private runtime, so it
    /// would fail to start on exactly the machines this head exists for — and it would do so
    /// while passing every "is the file present?" check ever written.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWebHostStagesTheScriptWorkerForItsOwnRuntimeIdentifier()
    {
        string relativePath = Path.Combine("AccessibleTrader.WebHost", "AccessibleTrader.WebHost.csproj");
        var docs = ProjectAndImports(relativePath);
        var all = docs.SelectMany(d => d.Descendants()).ToList();

        var reference = all
            .Where(e => e.Name.LocalName == "ProjectReference")
            .FirstOrDefault(e => ((string?)e.Attribute("Include") ?? "")
                .Contains("AccessibleTrader.ScriptWorker.csproj", StringComparison.OrdinalIgnoreCase));

        Assert.True(reference != null,
            "AccessibleTrader.WebHost.csproj does not reference the ScriptWorker at all, so no "
          + "user-compiled indicator or strategy can run on this head — on any platform. It is "
          + "the package the download page recommends first.");

        string additional = reference!.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "AdditionalProperties")?.Value
            ?? (string?)reference.Attribute("AdditionalProperties") ?? "";

        // AdditionalProperties is usually a property reference rather than a literal, because
        // what to forward depends on whether this publish carries a RID at all. Follow it: a
        // guard that only understood the literal form would have to be loosened the moment the
        // build was written the readable way, and a loosened guard checks nothing.
        additional = ResolveProperties(additional, docs);

        Assert.Contains("RuntimeIdentifier", additional, StringComparison.Ordinal);
        Assert.Contains("SelfContained", additional, StringComparison.Ordinal);

        bool stagedOnPublish = all
            .Where(e => e.Name.LocalName == "Target")
            .Where(e => Regex.IsMatch((string?)e.Attribute("AfterTargets") ?? "", @"\bPublish\b"))
            .Any(e =>
            {
                var body = e.ToString();
                return body.Contains("ScriptWorker", StringComparison.OrdinalIgnoreCase)
                    && body.Contains("$(PublishDir)", StringComparison.Ordinal);
            });

        Assert.True(stagedOnPublish,
            "the WebHost references the ScriptWorker but never copies it into $(PublishDir). A "
          + "ProjectReference carries the worker's ASSEMBLIES; the standalone apphost and its "
          + "runtimeconfig.json — which is what the resolver actually launches — do not travel "
          + "with it.");
    }

    /// <summary>
    /// A release build must FAIL rather than ship a payload-less head.
    ///
    /// <para>
    /// Both of these were high-importance build messages, and v2.12.0 was published with both
    /// of them printed in a green log that nobody read. A warning is the right level for a
    /// developer's build, where the answer is "fine, I am not testing braille today"; it is the
    /// wrong level for the artifact people download, where the answer is a silent chart. The
    /// gate is <c>ReleasePublish=true</c>, which only the release workflow passes.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("WarnIfNvdaControllerMissing", "a release that ships mute is worse than a release that fails")]
    [InlineData("WarnIfDotPadSdkMissing", "a release that ships an inert Braille tab is worse than a release that fails")]
    public void AReleaseBuildWithoutItsVendoredPayloadsIsAnError(string targetName, string why)
    {
        var docs = ProjectAndImports(Path.Combine(
            "AccessibleTrader.BlazorClient", "AccessibleTrader.BlazorClient.csproj"));

        var target = docs.SelectMany(d => d.Descendants())
            .Where(e => e.Name.LocalName == "Target")
            .FirstOrDefault(e => (string?)e.Attribute("Name") == targetName);

        Assert.True(target != null, $"{targetName} no longer exists");

        var error = target!.Elements().FirstOrDefault(e => e.Name.LocalName == "Error");
        Assert.True(error != null,
            $"{targetName} only warns. On a release publish it must be an error: {why}.");

        Assert.Contains("ReleasePublish", (string?)error!.Attribute("Condition") ?? "",
            StringComparison.Ordinal);
    }


    /// <summary>
    /// Substitutes <c>$(Name)</c> for the value of any <c>&lt;Name&gt;</c> defined in a
    /// PropertyGroup of these documents. One level deep and deliberately not an MSBuild
    /// evaluator — enough to read a value through the indirection the build uses for
    /// readability, and no more.
    /// </summary>
    private static string ResolveProperties(string value, List<XDocument> docs)
    {
        var defined = docs
            .SelectMany(d => d.Descendants())
            .Where(e => e.Parent?.Name.LocalName == "PropertyGroup")
            .GroupBy(e => e.Name.LocalName)
            .ToDictionary(g => g.Key, g => string.Join(";", g.Select(e => e.Value)));

        return Regex.Replace(value, @"\$\(([A-Za-z_][A-Za-z0-9_]*)\)",
            m => defined.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }
    /// <summary>The bodies of every <c>&lt;Target&gt;</c> whose AfterTargets names the given target.</summary>
    private static List<string> TargetsWith(List<XDocument> docs, string afterTargets)
        => docs.SelectMany(d => d.Descendants())
            .Where(e => e.Name.LocalName == "Target")
            .Where(e => Regex.IsMatch((string?)e.Attribute("AfterTargets") ?? "",
                                      $@"\b{Regex.Escape(afterTargets)}\b"))
            .Select(e => e.ToString())
            .ToList();
}
