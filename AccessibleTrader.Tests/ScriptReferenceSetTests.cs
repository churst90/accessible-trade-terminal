using System.Reflection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Scripting;
using Microsoft.CodeAnalysis;

namespace AccessibleTrader.Tests;

/// <summary>
/// The compile-time reference set for user scripts must be the SAME on every host.
///
/// <para>
/// Until 2026-08-25 it was built by scanning <c>AppDomain.CurrentDomain.GetAssemblies()</c> for
/// anything named <c>System.*</c> or <c>Microsoft.*</c>, so what a script could even NAME was a
/// function of what the running process happened to have loaded — different on the desktop head,
/// the WebHost and a test process, and different between two runs of the same head depending on
/// which features the user opened first. The scripting/sandbox audit hit this directly: two of
/// its four escapes were invisible in a bare test process and the probe had to force-load
/// <c>Microsoft.CSharp</c> and <c>System.Console</c> to see the real answer.
/// </para>
///
/// <para>
/// These guards pin the property that replaced it. The load-bearing one is
/// <see cref="A_type_the_host_has_loaded_is_not_reachable_unless_the_list_names_it"/>: it
/// force-loads an assembly the old scan would have swept in and proves the script still cannot
/// see it.
/// </para>
/// </summary>
public class ScriptReferenceSetTests
{
    private static RoslynScriptingService NewScripting() =>
        new RoslynScriptingService(
            workerLauncher: new DefaultProcessLauncher(),
            workerPathResolver: () => "/__reference_set_test_never_used__");

    /// <summary>Wraps a Calculate body in the smallest legal indicator.</summary>
    private static string Wrap(string body) => $$"""
        public sealed class ProbeIndicator : ICustomIndicator
        {
            public string Id => "PROBE";
            public string DisplayName => "probe";
            public string[] ComponentNames => new[] { "x" };
            public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
            public Dictionary<string, double> DefaultParameters => new();

            public double[][] Calculate(System.ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                {{body}}
                return new[] { new double[data.Length] };
            }
        }
        """;

    private static bool IsSandboxOrigin(string error) =>
        error.Contains("Blocked:", StringComparison.Ordinal)
        || error.Contains("is not allowed in user scripts", StringComparison.Ordinal)
        || error.Contains("is in blocked namespace", StringComparison.Ordinal);

    /// <summary>
    /// The one that matters. <c>System.Text.Json</c> is a real, loadable, <c>System.*</c>
    /// assembly in an ALLOWED namespace — under the old AppDomain scan, force-loading it here
    /// would have made <c>JsonDocument</c> nameable from a script, and on a host that had opened
    /// any feature touching JSON it would have been nameable without anyone choosing that. The
    /// declared list does not name it, so it must be unreachable no matter what this process has
    /// loaded — and unreachable with an ordinary "does not exist" compile error rather than a
    /// sandbox refusal, because the sandbox is not what is stopping it.
    /// </summary>
    [Fact]
    public async Task A_type_the_host_has_loaded_is_not_reachable_unless_the_list_names_it()
    {
        var loaded = Assembly.Load("System.Text.Json");
        Assert.NotNull(loaded);                   // vacuity: the premise is that it IS loaded
        Assert.Contains(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "System.Text.Json");

        var result = await NewScripting().CompileIndicatorAsync(
            Wrap("var doc = System.Text.Json.JsonDocument.Parse(\"{}\"); var _ = doc.RootElement.ValueKind;"));

        string errors = string.Join("\n  ", result.Errors ?? Array.Empty<string>());
        Assert.False(result.Success,
            "System.Text.Json was reachable from a user script — the reference set is following "
          + "the host's loaded assemblies again.");
        Assert.DoesNotContain(result.Errors ?? Array.Empty<string>(), IsSandboxOrigin);
        Assert.Contains("Json", errors, StringComparison.Ordinal);
    }

    /// <summary>
    /// The control for the test above, and the reason it is not vacuous. A type the list DOES
    /// name has to compile all the way through — otherwise "refused" above would prove nothing
    /// more than that the probe wrapper is broken. <c>ImmutableList</c> is the realistic case:
    /// <c>WorkspaceState.ActiveSeries</c> is one, so a strategy script that reads the workspace
    /// needs <c>System.Collections.Immutable</c> in the set.
    /// </summary>
    [Fact]
    public async Task A_type_the_list_does_name_compiles_all_the_way_through()
    {
        var result = await NewScripting().CompileIndicatorAsync(
            Wrap("var list = System.Collections.Immutable.ImmutableList<double>.Empty.Add(1.0); var _ = list.Count;"));

        string errors = string.Join("\n  ", result.Errors ?? Array.Empty<string>());
        Assert.DoesNotContain(result.Errors ?? Array.Empty<string>(), IsSandboxOrigin);

        // Compiles; the only thing left to fail on is the deliberately bogus worker path. (Or it
        // succeeds outright, if another test in the run has ACCESSIBLETRADER_SCRIPT_IN_PROCESS
        // set process-wide — either outcome proves the compile went through.)
        if (!result.Success)
            Assert.Contains("__reference_set_test_never_used__", errors);
    }

    /// <summary>
    /// Building the set twice, with an assembly load in between, must produce byte-identical
    /// input. This is the property stated directly, where the test above states it through a
    /// script; both are here because the direct one cannot catch a second scan added somewhere
    /// else in the compile path, and the script one cannot say WHY it failed.
    /// </summary>
    [Fact]
    public void The_set_does_not_change_when_the_host_loads_another_assembly()
    {
        var before = PathsOf(RoslynScriptingService.BuildReferences(includeHostCore: true));

        // An assembly the old scan would have swept in: System.*, on disk, not on the declared
        // list — and, load-bearingly, NOT already loaded. Picked at runtime rather than named,
        // because a hardcoded name that some other test has already pulled in makes this guard
        // pass for the wrong reason (the first draft did exactly that: it stayed green with the
        // AppDomain scan put back).
        var candidate = FirstUnloadedFrameworkAssembly();
        Assert.NotNull(candidate);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == candidate);
        _ = Assembly.Load(candidate!);
        Assert.Contains(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == candidate);

        var after = PathsOf(RoslynScriptingService.BuildReferences(includeHostCore: true));

        Assert.Equal(before, after);
        Assert.NotEmpty(before);
    }

    /// <summary>
    /// Every name on the list has to resolve to a file that exists. A dead entry in a declared
    /// list reads as coverage it does not have — the same failure the audit found in
    /// <c>_blockedTypes</c>, where two entries named types that do not exist.
    /// </summary>
    [Fact]
    public void Every_declared_framework_reference_resolves_to_a_real_file()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        Assert.False(string.IsNullOrEmpty(runtimeDir));

        var missing = RoslynScriptingService.FrameworkReferenceNames
            .Where(n => !File.Exists(Path.Combine(runtimeDir!, n + ".dll")))
            .ToArray();

        Assert.True(missing.Length == 0,
            "Declared framework references that do not exist on disk: " + string.Join(", ", missing));

        // …and the set actually contains them, so the check above is about the real list.
        var built = PathsOf(RoslynScriptingService.BuildReferences(includeHostCore: false));
        foreach (var name in RoslynScriptingService.FrameworkReferenceNames)
            Assert.Contains(built, p => Path.GetFileNameWithoutExtension(p)
                .Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <c>Microsoft.CSharp</c> is the deliberate omission: it is the assembly the C# compiler
    /// needs to EMIT a <c>dynamic</c> call site, and <c>dynamic</c> turned the entire blocklist
    /// off until 2026-08-25. The walker refuses it before Emit either way — this keeps the escape
    /// from even reaching that step if the rule is ever weakened.
    /// </summary>
    [Fact]
    public void The_dynamic_binder_assembly_is_not_in_the_set()
    {
        _ = typeof(Microsoft.CSharp.RuntimeBinder.Binder).Name;   // loaded in THIS process…

        var built = PathsOf(RoslynScriptingService.BuildReferences(includeHostCore: true));

        Assert.DoesNotContain(built, p => Path.GetFileNameWithoutExtension(p)
            .Equals("Microsoft.CSharp", StringComparison.OrdinalIgnoreCase));  // …and still not offered.
    }

    /// <summary>
    /// A blocked namespace whose assembly is not in the reference set must still be refused BY
    /// THE SANDBOX. Roslyn's own answer — "the type or namespace name 'Process' does not exist in
    /// the namespace 'System.Diagnostics'" — reads to a script author like a typo of theirs
    /// rather than a policy of ours, so the walker names it. Pure string matching, and safe only
    /// because it fires on a name that already failed to bind.
    /// </summary>
    [Fact]
    public async Task A_blocked_name_outside_the_reference_set_is_still_refused_by_name()
    {
        var result = await NewScripting().CompileIndicatorAsync(
            Wrap("System.Diagnostics.Process.Start(\"calc.exe\");"));

        string errors = string.Join("\n  ", result.Errors ?? Array.Empty<string>());
        Assert.False(result.Success);
        Assert.True(result.Errors!.Any(e => e.Contains("is in blocked namespace", StringComparison.Ordinal)),
            "Refused, but not with the sandbox's own wording. Diagnostics were:\n  " + errors);
    }

    /// <summary>
    /// A <c>System.*</c> assembly sitting in the runtime directory that this process has not
    /// loaded and the declared list does not name. Loading it is the event the guard above needs
    /// to observe having no effect.
    /// </summary>
    private static string? FirstUnloadedFrameworkAssembly()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (string.IsNullOrEmpty(runtimeDir)) return null;

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declared = RoslynScriptingService.FrameworkReferenceNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(runtimeDir, "System.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null && !loaded.Contains(n) && !declared.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string[] PathsOf(System.Collections.Generic.List<MetadataReference> refs) =>
        refs.Select(r => (r as PortableExecutableReference)?.FilePath ?? "").ToArray();
}
