namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// Thrown when a platform launcher would have to run the ScriptWorker WITHOUT
/// its OS-level sandbox (bwrap / sandbox-exec / AppContainer unavailable) and
/// the user has not explicitly opted into that risk. Before 2026-07 the
/// launchers silently fell back to the unsandboxed
/// <see cref="DefaultProcessLauncher"/>; a hostile custom indicator could then
/// read the user's files (including API-key storage) and reach the network
/// with no warning that the protection was absent. The message is written to
/// be shown verbatim in the Custom Scripts modal.
/// </summary>
public sealed class ScriptSandboxUnavailableException : InvalidOperationException
{
    public ScriptSandboxUnavailableException(string details, string remedy)
        : base("Custom scripts are disabled because the operating-system script sandbox is unavailable: "
              + details + " "
              + "Scripts normally run with kernel-level isolation (no network, read-only filesystem); "
              + "running them without it would let a malicious script read your files and API keys. "
              + remedy + " "
              + $"To accept the risk on this machine anyway, set {SandboxPolicy.OverrideEnvVar}=1 and restart.")
    {
    }
}

/// <summary>
/// Central policy for what happens when an OS sandbox primitive is missing at
/// script-launch time. Default: refuse to run the worker unsandboxed (throw
/// <see cref="ScriptSandboxUnavailableException"/>). The user can explicitly
/// accept unsandboxed execution by setting
/// <c>ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=1</c> — an intentional,
/// per-machine decision that is also recorded to the security event log every
/// time it is exercised (see the launchers' RecordFallback calls).
/// </summary>
public static class SandboxPolicy
{
    public const string OverrideEnvVar = "ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS";

    /// <summary>
    /// <c>true</c> when the user has explicitly opted into unsandboxed script
    /// execution via the environment variable.
    /// </summary>
    public static bool AllowUnsandboxedFallback
        => IsOverrideValue(Environment.GetEnvironmentVariable(OverrideEnvVar));

    /// <summary>Accepts "1" or "true" (any case); everything else is off.</summary>
    internal static bool IsOverrideValue(string? value)
        => !string.IsNullOrEmpty(value)
           && (value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gate every "sandbox primitive missing" fallback through here: throws
    /// unless <paramref name="overrideAllowed"/> is set. Kept as a pure helper
    /// so the refusal logic is unit-testable on any OS.
    /// </summary>
    internal static void EnforceOrThrow(bool overrideAllowed, string details, string remedy)
    {
        if (!overrideAllowed)
            throw new ScriptSandboxUnavailableException(details, remedy);
    }

    /// <summary>
    /// Records an "unsandboxed fallback exercised" security event so the
    /// override never happens invisibly. Mirrors the Windows launcher's
    /// RecordFallback diagnostics.
    /// </summary>
    internal static void RecordUnsandboxedFallback(string launcher, string details)
    {
        var log = AccessibleTrader.Sdk.Services.PluginHostServices.SecurityEvents;
        log?.Record(new AccessibleTrader.Sdk.Services.SecurityEvent(
            DateTime.UtcNow,
            AccessibleTrader.Sdk.Services.SecurityEventKind.UnsandboxedScriptOverride,
            Source: launcher,
            Message: $"Running ScriptWorker WITHOUT OS sandbox ({details}); user override {OverrideEnvVar} is set."));
    }
}
