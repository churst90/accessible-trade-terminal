namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Thrown when the caller tries to launch the script worker on a platform the
/// host explicitly refuses. iOS has always refused (no OS sandbox primitive
/// suitable for a child process that runs arbitrary user IL). macCatalyst
/// joined the refusal list 2026-04-24 — the net10.0 ScriptWorker can't be
/// referenced from a self-contained macCatalyst build, and until a dedicated
/// macCatalyst worker packaging ships we explicitly refuse rather than
/// falling through to the in-process path.
/// </summary>
public sealed class ScriptingNotSupportedOnPlatformException : PlatformNotSupportedException
{
    public ScriptingNotSupportedOnPlatformException(string platform)
        : base($"Custom-script compilation and execution is not supported on {platform}. "
              + "The ScriptWorker process cannot be spawned or sandboxed on this platform.")
    {
        Platform = platform;
    }

    public string Platform { get; }
}

/// <summary>
/// Launcher that throws <see cref="ScriptingNotSupportedOnPlatformException"/>
/// on every <see cref="Launch"/> call. Used on iOS and macCatalyst so script
/// compile attempts surface an explicit platform refusal instead of silently
/// falling through to the unsandboxed in-process path.
/// </summary>
public sealed class RefusingScriptWorkerLauncher : IScriptWorkerLauncher
{
    private readonly string _platform;

    public RefusingScriptWorkerLauncher(string platform)
    {
        _platform = platform;
    }

    public IScriptWorkerProcess Launch(string workerExecutablePath)
        => throw new ScriptingNotSupportedOnPlatformException(_platform);
}
