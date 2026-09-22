using System.Diagnostics;
using System.Security.Cryptography;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The plugin trust manifest task, RUN, against a directory layout shaped like the one that
/// broke.</b>
///
/// <para>
/// There was already a guard asserting that <c>packaging/PluginTrustManifest.targets</c> contains
/// the string <c>WriteBesidePlugins="true"</c>. It passed on a version of the task that did not
/// work, and the release caught what it could not: on Mac Catalyst the same plugin exists loose
/// in <c>bin/&lt;rid&gt;/</c> AND inside <c>YourApp.app/Contents/MonoBundle/</c>, and the task
/// deduplicated its file list BY NAME before deriving destinations from it — so whichever copy
/// the directory walk reached first was the only directory that ever got a manifest, and the
/// bundle (the one that matters, because it is what ships and what
/// <c>AppContext.BaseDirectory</c> resolves to) was never seen.
/// </para>
///
/// <para>
/// <b>A guard over a build step should RUN the build step.</b> These tests invoke MSBuild on a
/// throwaway project that imports the real targets file, over a temporary tree with the same
/// duplicate-name shape, and read what lands on disk. The layout is the load-bearing part: two
/// directories, the same plugin file name in both, DIFFERENT BYTES in each.
/// </para>
/// </summary>
public sealed class PluginTrustManifestTaskTests : IDisposable
{
    // TestTemp, so the directory is under the one run-scoped root that is removed at process
    // exit. The suite once left roughly eight thousand directories behind on this machine's
    // tmpfs; TestTempScanTests is the guard that stops it coming back, and it caught this file
    // twice — once for the call and once for a comment naming the call, because the guard reads
    // the source text. That is the right trade for a leak nothing else fails on.
    private readonly string _root = TestTemp.NewDir("att-manifest-");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    public void Dispose()
    {
        // Eager cleanup on top of TestTemp's process-exit sweep: this class writes four trees
        // per run and there is no reason to keep them around for the rest of the suite.
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* a temp directory that will not delete is not a test failure */ }
    }

    private static string Sha256Of(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    /// <summary>
    /// Writes a project that imports the real targets and invokes the real task, then runs it.
    /// Returns MSBuild's combined output so a failure can say what the build said.
    /// </summary>
    private string RunTask(string pluginsDir, string manifestPath, bool writeBesidePlugins)
    {
        string project = Path.Combine(_root, "probe.proj");
        File.WriteAllText(project, $"""
            <Project>
              <Import Project="{Path.Combine(RepoRoot(), "packaging", "PluginTrustManifest.targets")}" />
              <Target Name="Probe">
                <HashPluginDlls PluginsDir="{pluginsDir}"
                                ManifestPath="{manifestPath}"
                                WriteBesidePlugins="{writeBesidePlugins.ToString().ToLowerInvariant()}" />
              </Target>
            </Project>
            """);

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _root,
        };
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add(project);
        psi.ArgumentList.Add("-t:Probe");
        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-v:normal");

        using var proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit(milliseconds: 180_000);

        Assert.True(proc.ExitCode == 0, $"MSBuild failed running the real task:\n{output}");
        return output;
    }

    /// <summary>
    /// The layout that matters: the same plugin FILE NAME in two directories, with different
    /// bytes — a per-RID output directory and an app bundle's MonoBundle.
    /// </summary>
    private (string Loose, string Bundle) BuildDuplicateNameLayout()
    {
        string loose = Path.Combine(_root, "maccatalyst-x64");
        string bundle = Path.Combine(_root, "Probe.app", "Contents", "MonoBundle");
        Directory.CreateDirectory(loose);
        Directory.CreateDirectory(bundle);

        foreach (var name in new[] { "AccessibleTrader.Plugins.Alpha.dll", "AccessibleTrader.Plugins.Beta.dll" })
        {
            File.WriteAllText(Path.Combine(loose, name), $"loose bytes for {name}");
            File.WriteAllText(Path.Combine(bundle, name), $"DIFFERENT bundle bytes for {name}");
        }

        return (loose, bundle);
    }

    /// <summary>
    /// <b>Every directory holding plugins gets a manifest</b> — including one nested inside an
    /// app bundle, whose copies share their names with the loose ones.
    /// </summary>
    [Fact]
    public void AManifestLandsInEveryDirectoryThatHoldsPlugins_IncludingInsideAnAppBundle()
    {
        var (loose, bundle) = BuildDuplicateNameLayout();
        string manifestPath = Path.Combine(_root, "plugins_trusted.manifest");

        string output = RunTask(_root, manifestPath, writeBesidePlugins: true);

        Assert.True(File.Exists(manifestPath), $"no manifest at the declared path.\n{output}");
        Assert.True(File.Exists(Path.Combine(loose, "plugins_trusted.manifest")),
            $"no manifest beside the loose per-RID plugins.\n{output}");
        Assert.True(File.Exists(Path.Combine(bundle, "plugins_trusted.manifest")),
            "NO MANIFEST INSIDE THE APP BUNDLE. That bundle is what ships and what "
          + "AppContext.BaseDirectory resolves to on Mac Catalyst, so PluginTrustPolicy."
          + "RequireTrusted would enforce an empty allow-list and refuse every plugin in it — "
          + $"the market dropdown offering only the built-in providers.\n{output}");
    }

    /// <summary>
    /// <b>And each manifest describes ITS OWN directory's bytes.</b>
    ///
    /// <para>
    /// This is the half that is worse to get wrong than to omit. A manifest carrying digests of
    /// a different copy of the same file names is present, well-formed, and refuses every plugin
    /// beside it — which looks to a user exactly like a missing manifest and to a developer
    /// exactly like a working one.
    /// </para>
    /// </summary>
    [Fact]
    public void EachManifestHashesTheFilesInItsOwnDirectory()
    {
        var (loose, bundle) = BuildDuplicateNameLayout();
        RunTask(_root, Path.Combine(_root, "plugins_trusted.manifest"), writeBesidePlugins: true);

        foreach (var dir in new[] { loose, bundle })
        {
            string text = File.ReadAllText(Path.Combine(dir, "plugins_trusted.manifest"));
            foreach (var dll in Directory.GetFiles(dir, "AccessibleTrader.Plugins.*.dll"))
            {
                Assert.True(text.Contains(Sha256Of(dll), StringComparison.OrdinalIgnoreCase),
                    $"the manifest in {Path.GetFileName(dir)} does not carry the digest of its own "
                  + $"{Path.GetFileName(dll)}. It describes a different copy of that file, so the "
                  + "trust policy will refuse the one sitting next to it.");
            }
        }
    }

    /// <summary>
    /// With the flag off, behaviour is exactly what it always was: one manifest, at the declared
    /// path, and nothing scattered. The flag has to be a real switch or the parity tests that
    /// assert it is set are asserting nothing.
    /// </summary>
    [Fact]
    public void WithoutTheFlag_OnlyTheDeclaredPathIsWritten()
    {
        var (loose, bundle) = BuildDuplicateNameLayout();
        string manifestPath = Path.Combine(_root, "plugins_trusted.manifest");

        RunTask(_root, manifestPath, writeBesidePlugins: false);

        Assert.True(File.Exists(manifestPath));
        Assert.False(File.Exists(Path.Combine(loose, "plugins_trusted.manifest")));
        Assert.False(File.Exists(Path.Combine(bundle, "plugins_trusted.manifest")));
    }

    /// <summary>A directory with no plugins produces no manifest and no failure.</summary>
    [Fact]
    public void AnEmptyTreeIsNotAnError()
    {
        Directory.CreateDirectory(_root);
        string manifestPath = Path.Combine(_root, "plugins_trusted.manifest");

        RunTask(_root, manifestPath, writeBesidePlugins: true);

        Assert.False(File.Exists(manifestPath));
    }

    /// <summary>
    /// Runs one of the REAL targets (not just the task) so the conditions on it are exercised,
    /// with the RID properties a given publish shape would carry.
    /// </summary>
    private string RunPublishTarget(string publishDir, string? runtimeIdentifier, string? runtimeIdentifiers)
    {
        string project = Path.Combine(_root, "target-probe.proj");
        File.WriteAllText(project, $"""
            <Project>
              <Import Project="{Path.Combine(RepoRoot(), "packaging", "PluginTrustManifest.targets")}" />
            </Project>
            """);

        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _root };
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add(project);
        psi.ArgumentList.Add("-t:GeneratePluginTrustManifestOnPublish");
        psi.ArgumentList.Add($"-p:PublishDir={publishDir}{Path.DirectorySeparatorChar}");
        if (runtimeIdentifier is not null) psi.ArgumentList.Add($"-p:RuntimeIdentifier={runtimeIdentifier}");
        // %3B, not ';'. A semicolon on an MSBuild command line separates PROPERTIES, so
        // -p:RuntimeIdentifiers=a;b parses as "-p:RuntimeIdentifiers=a" plus a stray "b" and
        // fails with MSB1006. The escape is how a list value reaches a single property.
        if (runtimeIdentifiers is not null)
            psi.ArgumentList.Add($"-p:RuntimeIdentifiers={runtimeIdentifiers.Replace(";", "%3B")}");
        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-v:normal");

        using var proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit(milliseconds: 180_000);
        Assert.True(proc.ExitCode == 0, $"MSBuild failed running the real target:\n{output}");
        return output;
    }

    /// <summary>
    /// <b>An INNER per-RID build of a universal publish must not write into its bundle.</b>
    ///
    /// <para>
    /// A universal Mac Catalyst publish builds once per RID and LIPO-merges the two bundles, and
    /// that merge demands every non-binary file be byte-identical between them. Two per-RID
    /// manifests never are — they carry a generation timestamp, and they hash different builds
    /// of the same assemblies. Writing one into each inner bundle turned "the macOS app has no
    /// manifest" into "the macOS app does not build":
    /// </para>
    ///
    /// <code>
    /// error : Unable to merge the file 'Contents/MonoBundle/plugins_trusted.manifest',
    ///         it's different between the input app bundles.
    /// </code>
    ///
    /// <para>
    /// Caught by the release job on the second re-cut attempt, which is one layer later than it
    /// should have been — hence this test.
    /// </para>
    /// </summary>
    [Fact]
    public void AnInnerRidBuildOfAUniversalPublishWritesNothingIntoTheBundle()
    {
        var (_, bundle) = BuildDuplicateNameLayout();

        // ONLY RuntimeIdentifier. The first version of this test also set RuntimeIdentifiers,
        // and so did the condition it was checking — which is why both agreed and both were
        // wrong: the SDK drives RuntimeIdentifiers as a GLOBAL property in the inner build, so
        // it is not visible there at all, and the macOS job failed a second time with the same
        // merge error. A test that mirrors the production condition's assumptions cannot
        // falsify them.
        RunPublishTarget(_root, runtimeIdentifier: "maccatalyst-x64", runtimeIdentifiers: null);

        Assert.False(File.Exists(Path.Combine(bundle, "plugins_trusted.manifest")),
            "an inner per-RID build wrote a manifest into its app bundle. That bundle is an INPUT "
          + "to the universal merge, which requires byte-identical files, so this does not produce "
          + "a wrong manifest — it produces a macOS head that does not build at all.");
    }

    /// <summary>
    /// And the outer universal build — the one that runs after the merge, and the only one whose
    /// bundle ships — does write it. Without this case the fix above is indistinguishable from
    /// switching the feature off.
    /// </summary>
    [Fact]
    public void TheOuterUniversalBuildDoesWriteIntoTheBundle()
    {
        var (_, bundle) = BuildDuplicateNameLayout();

        string output = RunPublishTarget(_root, runtimeIdentifier: null, runtimeIdentifiers: null);

        Assert.True(File.Exists(Path.Combine(bundle, "plugins_trusted.manifest")),
            $"the outer universal build did not write into the bundle, so the shipped .app has no "
          + $"manifest and refuses every plugin in it.\n{output}");
    }

    /// <summary>
    /// <b>The load-bearing case for how blunt the rule is.</b> Windows and all four WebHosts are
    /// RID-specific too, so the guard above applies to them — and it must cost them nothing,
    /// because they have no app bundles. If this ever goes red, the rule has stopped being
    /// "do not write inside a .app" and become "do not write", which switches the whole feature
    /// off for every head that is not macOS.
    /// </summary>
    [Fact]
    public void ASingleRidHeadIsNotMistakenForAnInnerBuild()
    {
        var (loose, _) = BuildDuplicateNameLayout();

        RunPublishTarget(_root, runtimeIdentifier: "win-x64", runtimeIdentifiers: null);

        Assert.True(File.Exists(Path.Combine(loose, "plugins_trusted.manifest")),
            "a single-RID publish stopped writing beside its plugins — the inner-build guard is "
          + "too broad and has switched the feature off for every head that is not macOS.");
    }
}
