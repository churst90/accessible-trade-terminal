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
    /// <b>Every runtime payload the desktop head loads by PATH must reach the publish output.</b>
    ///
    /// <para>
    /// These two were recorded here as known, undemonstrated exceptions when this file was
    /// written, and demonstrated a few hours later by unzipping the CI artifact: neither the Dot
    /// Pad SDK nor <c>AccessibleTrader.ScriptWorker.exe</c> was in it. So every released Windows
    /// build has had tactile support disabled (the driver's own log line is "NativeLibrary.TryLoad
    /// failed ... Tactile DISABLED") and no way to run a user-compiled indicator or strategy,
    /// because Release refuses the in-process path by design.
    /// </para>
    ///
    /// <para>
    /// <b>The Braille tab is why this needed a test rather than a note.</b> It is ordinary DOM,
    /// so it renders perfectly on a build that cannot load the SDK — the feature looks present
    /// and is inert, which is harder to notice than a feature that is plainly missing.
    /// </para>
    /// </summary>
    [Theory]
    // The Dot Pad SDK is not one file. Its modules expect to find each other in the same
    // directory, so staging the entry DLL alone leaves it just as unable to load — and the
    // driver binds DOT_PAD_BRAILLE_DISPLAY among its REQUIRED exports, which is why the
    // LibLouis tables are on this list and not treated as optional extras.
    [InlineData("DotPadSDK-3.0.0.dll", "the Dot Pad tactile SDK — the device this application exists for")]
    [InlineData("TTBEngine.dll", "the Dot Pad SDK's text-to-braille engine")]
    [InlineData("Mecab.dll", "a Dot Pad SDK module its siblings expect beside them")]
    [InlineData("jsoncpp.dll", "a Dot Pad SDK module its siblings expect beside them")]
    [InlineData("liblouis.dll", "the braille translator behind DOT_PAD_BRAILLE_DISPLAY")]
    [InlineData("mecabrc", "the MeCab config the SDK reads at init")]
    [InlineData("tables", "the LibLouis translation tables — without them there is no text-to-braille")]
    [InlineData("AccessibleTrader.ScriptWorker", "the out-of-process Roslyn script worker")]
    public void EveryNativePayloadTheDesktopHeadLoadsByPath_ReachesThePublishOutput(string payload, string what)
    {
        var doc = XDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot(), "AccessibleTrader.BlazorClient", "AccessibleTrader.BlazorClient.csproj")));

        // PARSED, not pattern-matched. The first draft of this asked whether the payload's name
        // appeared within 400 characters of the string "CopyToPublishDirectory", and a sabotage
        // that deleted the real one passed — because the name also occurs in the OutDir-only
        // target, in the missing-SDK warning text and in the comments, and one of those landed
        // near a DIFFERENT item's metadata. A guard over a file format should read the format.
        bool publishItem = doc.Descendants()
            .Where(e => e.Name.LocalName == "None")
            .Where(e => ((string?)e.Attribute("Include") ?? "").Contains(payload, StringComparison.OrdinalIgnoreCase))
            .Any(e => e.Elements().Any(c => c.Name.LocalName == "CopyToPublishDirectory")
                   || e.Attribute("CopyToPublishDirectory") != null);

        // …or a target that runs after Publish and copies the payload INTO $(PublishDir).
        bool publishTarget = doc.Descendants()
            .Where(e => e.Name.LocalName == "Target")
            .Where(e => Regex.IsMatch((string?)e.Attribute("AfterTargets") ?? "", @"\bPublish\b"))
            .Any(e =>
            {
                var body = e.ToString();
                return body.Contains(payload, StringComparison.OrdinalIgnoreCase)
                    && body.Contains("$(PublishDir)", StringComparison.Ordinal);
            });

        Assert.True(publishItem || publishTarget,
            $"{payload} ({what}) is staged into $(OutDir) only, so it reaches a `dotnet build` and "
          + "NOT a `dotnet publish` — the released zip ships without it and the feature is inert. "
          + "Give it a <None> item with a CopyToPublishDirectory child, or an "
          + "AfterTargets=\"Publish\" target that copies it into $(PublishDir).");
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
