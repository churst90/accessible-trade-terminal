namespace AccessibleTrader.Tests;

/// <summary>
/// Where the ScriptWorker executable is, for the tests that need a real one.
///
/// <para>
/// The test assembly lives at <c>…/AccessibleTrader.Tests/bin/&lt;Config&gt;/net10.0</c> and the
/// worker at <c>…/AccessibleTrader.ScriptWorker/bin/&lt;Config&gt;/net10.0</c>, so the walk is up
/// four and back down. <c>AccessibleTrader.Tests.csproj</c> carries a ProjectReference to the
/// worker purely so a plain <c>dotnet test</c> builds it first.
/// </para>
///
/// <para>
/// This was copied verbatim into three test classes before a fourth needed it. Four copies of a
/// path that has to track a project layout is three chances for one of them to keep passing
/// against a worker that no longer exists.
/// </para>
/// </summary>
internal static class ScriptWorkerPath
{
    public static string Resolve()
    {
        var baseDir      = AppContext.BaseDirectory;
        var solutionRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        var config       = Path.GetFileName(Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar))) ?? "Debug";
        var exeName      = OperatingSystem.IsWindows()
            ? "AccessibleTrader.ScriptWorker.exe"
            : "AccessibleTrader.ScriptWorker";
        return Path.Combine(solutionRoot, "AccessibleTrader.ScriptWorker", "bin", config, "net10.0", exeName);
    }

    /// <summary>The resolved path, or null when the worker has not been built.</summary>
    public static string? ResolveIfBuilt()
    {
        var path = Resolve();
        return File.Exists(path) ? path : null;
    }
}
