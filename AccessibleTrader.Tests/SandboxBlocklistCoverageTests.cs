using System.Reflection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Scripting;

namespace AccessibleTrader.Tests;

/// <summary>
/// Every entry on the script sandbox's blocklist, compiled at rather than read.
///
/// <para>
/// A2/F6: of the 25 <c>_blockedMembers</c> entries, 22 were never named by any test, and 2 of
/// the 8 <c>_blockedTypes</c> entries likewise — mutant M10 deleted one and the suite stayed
/// green. A blocklist entry nothing exercises is not a policy, it is a comment: the 2026-08-25
/// audit found that the member check was consulted for METHODS ONLY, so
/// <c>System.Type.Assembly</c> would have read as blocked in this list and been reachable in
/// fact. That is exactly the failure an untested entry hides.
/// </para>
///
/// <para>
/// Two claims, and they need each other. Each entry below is compiled and must be refused
/// <b>by name</b> — the walker's diagnostic quotes the key it matched, so agreeing with it
/// proves this entry did the refusing rather than some neighbouring rule that happens to cover
/// the same snippet. And the coverage test reads the live sets by reflection, so adding an entry
/// without a probe fails the build instead of quietly joining the 22.
/// </para>
///
/// <para>
/// This is the 2026-08-25 sandbox audit's method — compile the escape, do not read the walker —
/// applied to the list the audit did not enumerate.
/// </para>
/// </summary>
[Collection("ScriptWorker")]   // Roslyn compilation; see ScriptWorkerCollection
public class SandboxBlocklistCoverageTests
{
    /// <summary>
    /// One compilable expression per blocklist entry. The value is the body of
    /// <c>Calculate</c>; the key is the exact blocklist key the walker must quote back.
    /// </summary>
    private static readonly Dictionary<string, string> MemberProbes = new(StringComparer.Ordinal)
    {
        ["System.Type.GetType"]          = """var t = System.Type.GetType("System.IO.File");""",
        ["System.Type.InvokeMember"]     = """var r = typeof(string).InvokeMember("Concat", default, null, null, null);""",
        ["System.Type.GetMethod"]        = """var m = typeof(string).GetMethod("Concat");""",
        ["System.Type.GetMethods"]       = """var m = typeof(string).GetMethods();""",
        ["System.Type.GetField"]         = """var f = typeof(string).GetField("Empty");""",
        ["System.Type.GetFields"]        = """var f = typeof(string).GetFields();""",
        ["System.Type.GetProperty"]      = """var p = typeof(string).GetProperty("Length");""",
        ["System.Type.GetProperties"]    = """var p = typeof(string).GetProperties();""",
        ["System.Type.GetConstructor"]   = """var c = typeof(string).GetConstructor(new System.Type[0]);""",
        ["System.Type.GetMembers"]       = """var m = typeof(string).GetMembers();""",
        ["System.Type.GetMember"]        = """var m = typeof(string).GetMember("Length");""",
        ["System.Type.MakeGenericType"]  = """var t = typeof(System.Collections.Generic.List<>).MakeGenericType(typeof(int));""",
        ["System.Type.MakeArrayType"]    = """var t = typeof(int).MakeArrayType();""",
        ["System.Type.Assembly"]         = """var a = typeof(int).Assembly;""",
        ["System.Type.Module"]           = """var m = typeof(int).Module;""",
        ["System.Type.TypeHandle"]       = """var h = typeof(int).TypeHandle;""",
        ["System.Activator.CreateInstance"]     = """var o = System.Activator.CreateInstance(typeof(object));""",
        ["System.Activator.CreateInstanceFrom"] = """var o = System.Activator.CreateInstanceFrom("evil.dll", "Evil");""",
        ["System.Delegate.CreateDelegate"]      = """var d = System.Delegate.CreateDelegate(typeof(System.Action), typeof(string), "Empty");""",
        ["System.AppDomain.Load"]                   = """var a = System.AppDomain.CurrentDomain.Load(new byte[0]);""",
        ["System.AppDomain.CreateInstance"]         = """var o = System.AppDomain.CurrentDomain.CreateInstance("evil", "Evil");""",
        ["System.AppDomain.CreateInstanceAndUnwrap"] = """var o = System.AppDomain.CurrentDomain.CreateInstanceAndUnwrap("evil", "Evil");""",
        ["System.GC.GetTotalMemory"]     = """var n = System.GC.GetTotalMemory(false);""",
        ["System.Environment.Exit"]      = """System.Environment.Exit(0);""",
        ["System.Environment.FailFast"]  = """System.Environment.FailFast("bye");""",
    };

    private static readonly Dictionary<string, string> TypeProbes = new(StringComparer.Ordinal)
    {
        ["System.AppDomain"]     = """var d = System.AppDomain.CurrentDomain;""",
        ["System.Environment"]   = """var s = System.Environment.CurrentDirectory;""",
        ["System.AppContext"]    = """var s = System.AppContext.BaseDirectory;""",
        ["System.Console"]       = """System.Console.WriteLine("hello from inside the pipe");""",
        ["System.Runtime.InteropServices.GCHandle"]      = """var h = default(System.Runtime.InteropServices.GCHandle);""",
        ["System.Runtime.InteropServices.NativeMemory"]  = """var t = typeof(System.Runtime.InteropServices.NativeMemory);""",
        ["System.Runtime.CompilerServices.Unsafe"]       = """var t = typeof(System.Runtime.CompilerServices.Unsafe);""",
        ["System.Runtime.CompilerServices.RuntimeHelpers"] = """var n = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(new object());""",
    };

    private static string Script(string body) => $$"""
        public sealed class Probe : ICustomIndicator
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

    private static RoslynScriptingService NewScripting() =>
        new(workerLauncher: new DefaultProcessLauncher(),
            // Compile-time refusal is the whole point; reaching the worker would be the bug.
            workerPathResolver: () => "/__blocklist_coverage_never_used__");

    private static async Task AssertRefusedByName(string key, string body, string kindWord)
    {
        var result = await NewScripting().CompileIndicatorAsync(Script(body));
        string errors = string.Join("\n  ", result.Errors ?? Array.Empty<string>());

        Assert.False(result.Success,
            $"the sandbox compiled a script that touches '{key}'. Blocklist entry is dead.");
        Assert.True(result.Errors is { Length: > 0 }, $"'{key}' refused with no diagnostic.");

        // The walker quotes the key it matched. Requiring it here is what separates "this entry
        // works" from "something refused this snippet" — the difference the 22 untested entries
        // were sitting inside.
        string expected = $"{kindWord} '{key}' is not allowed in user scripts.";
        Assert.True(result.Errors!.Any(e => e.Contains(expected, StringComparison.Ordinal)),
            $"'{key}' was refused, but not by its own blocklist entry — nothing said "
            + $"\"{expected}\". If this snippet is now refused by a neighbouring rule the entry "
            + $"itself could be deleted unnoticed. Diagnostics were:\n  {errors}");
    }

    public static TheoryData<string> BlockedMemberKeys()
    {
        var d = new TheoryData<string>();
        foreach (var k in MemberProbes.Keys.OrderBy(k => k, StringComparer.Ordinal)) d.Add(k);
        return d;
    }

    public static TheoryData<string> BlockedTypeKeys()
    {
        var d = new TheoryData<string>();
        foreach (var k in TypeProbes.Keys.OrderBy(k => k, StringComparer.Ordinal)) d.Add(k);
        return d;
    }

    [Theory]
    [MemberData(nameof(BlockedMemberKeys))]
    public Task Every_blocked_member_refuses_a_script_that_touches_it(string key) =>
        AssertRefusedByName(key, MemberProbes[key], "member");

    [Theory]
    [MemberData(nameof(BlockedTypeKeys))]
    public Task Every_blocked_type_refuses_a_script_that_touches_it(string key) =>
        AssertRefusedByName(key, TypeProbes[key], "type");

    // ── The list above must stay the list below ─────────────────────────────

    private static IReadOnlyCollection<string> LiveSet(string fieldName)
    {
        // The sets are private statics. Reading them by reflection is deliberate — a copy of the
        // list here would be a fourth place for it to drift, and drift is what this file exists
        // for. Searched on the service and on its nested types, because which of the two holds
        // them is an implementation detail that has already moved once.
        const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
        var candidates = new[] { typeof(RoslynScriptingService) }
            .Concat(typeof(RoslynScriptingService).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public));

        var field = candidates.Select(t => t.GetField(fieldName, Flags)).FirstOrDefault(f => f != null);
        Assert.True(field != null,
            $"neither RoslynScriptingService nor any of its nested types declares '{fieldName}' "
            + "any more — the blocklist moved and this coverage guard is now measuring nothing.");

        var set = (IEnumerable<string>)field!.GetValue(null)!;
        return set.ToList();
    }

    [Fact]
    public void Every_blocklist_entry_has_a_probe()
    {
        var members = LiveSet("_blockedMembers");
        var types   = LiveSet("_blockedTypes");

        Assert.True(members.Count > 0 && types.Count > 0, "the blocklist sets came back empty");

        var unprobedMembers = members.Where(m => !MemberProbes.ContainsKey(m)).OrderBy(m => m).ToList();
        var unprobedTypes   = types.Where(t => !TypeProbes.ContainsKey(t)).OrderBy(t => t).ToList();

        Assert.True(unprobedMembers.Count == 0,
            "blocked members with no probe in this file — add one, or the entry is a comment:\n  "
            + string.Join("\n  ", unprobedMembers));
        Assert.True(unprobedTypes.Count == 0,
            "blocked types with no probe in this file:\n  " + string.Join("\n  ", unprobedTypes));
    }

    [Fact]
    public void No_probe_names_an_entry_that_is_no_longer_blocked()
    {
        // The other direction: a probe for a deleted entry would keep passing off some other
        // rule and read as coverage the list no longer has.
        var members = LiveSet("_blockedMembers").ToHashSet(StringComparer.Ordinal);
        var types   = LiveSet("_blockedTypes").ToHashSet(StringComparer.Ordinal);

        var stale = MemberProbes.Keys.Where(k => !members.Contains(k))
            .Concat(TypeProbes.Keys.Where(k => !types.Contains(k)))
            .OrderBy(k => k)
            .ToList();

        Assert.True(stale.Count == 0,
            "probes for entries that are no longer on the blocklist:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// The vacuity check, and it is not optional here. Every test above asserts a REFUSAL, so a
    /// walker that refused everything — a broken reference set, a wrapper that no longer compiles,
    /// a namespace filter that swallowed the whole surface — would turn the file green while
    /// proving nothing at all. An ordinary indicator must not be refused BY THE SANDBOX.
    ///
    /// <para>
    /// "By the sandbox" is the whole precision, and the first version of this test got it wrong in
    /// a way worth recording. It asserted <c>result.Success</c> against a real worker, which
    /// passes here and fails on CI: the runner has no <c>bwrap</c>, and <c>LinuxBwrapLauncher</c>
    /// correctly REFUSES rather than silently downgrading to an unsandboxed worker. The claim this
    /// file needs is about the compile-time walker, which never reaches the spawn step — so it is
    /// stated that way, and now holds on any machine whether or not it can run a sandbox at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_ordinary_indicator_is_not_refused_by_the_sandbox()
    {
        var result = await NewScripting().CompileIndicatorAsync(Script(
            "double sum = 0; for (int i = 0; i < data.Length; i++) sum += data[i].Close;"));

        var errors = result.Errors ?? Array.Empty<string>();
        var sandboxRefusals = errors.Where(e =>
            e.Contains("is not allowed in user scripts", StringComparison.Ordinal)
            || e.Contains("is in blocked namespace", StringComparison.Ordinal)
            || e.Contains("Blocked:", StringComparison.Ordinal)).ToList();

        Assert.True(sandboxRefusals.Count == 0,
            "a harmless indicator was refused by the sandbox, so every refusal above is "
            + "meaningless:\n  " + string.Join("\n  ", sandboxRefusals));

        // And it must be a real compile, not one that died before the walker ever ran — otherwise
        // "no sandbox diagnostic" is a statement about a compilation that did not happen. The only
        // error permitted is the deliberately bogus worker path these probes use.
        Assert.True(errors.All(e => e.Contains("__blocklist_coverage_never_used__", StringComparison.Ordinal))
                    || result.Success,
            "the fixture failed to compile for a reason unrelated to the sandbox:\n  "
            + string.Join("\n  ", errors));

        if (result.Indicator != null) NewScripting().UnloadScript(result.Indicator.Id);
    }
}
