using System;
using System.Collections.Generic;

namespace AccessibleTrader.Sdk.Services;

/// <summary>
/// Categories of security-relevant runtime events the host records for
/// post-incident review. Add new values for every new sandbox /
/// credential / trust-policy code path — these are what an operator
/// grep-filters on.
/// </summary>
public enum SecurityEventKind
{
    /// <summary>Windows AppContainer launch fell back to the unsandboxed default launcher (dev-box ACL gap, unsupported OS, etc).</summary>
    AppContainerFallback,
    /// <summary>macOS <c>sandbox-exec</c> path fell back (missing binary, missing profile).</summary>
    SandboxExecFallback,
    /// <summary>Android isolated-process service bind failed or the platform launcher wasn't registered.</summary>
    AndroidServiceFallback,
    /// <summary>Script worker killed for exceeding <c>WorkingSet64</c> quota.</summary>
    MemoryQuotaKill,
    /// <summary>Script worker killed for exceeding wall-clock Calculate budget.</summary>
    CalculateTimeout,
    /// <summary>A provider's sign-time credential checkout returned <c>HasCredentials=false</c> or threw.</summary>
    CredentialCheckoutFailed,
    /// <summary>On-disk credential file could not be removed during disconnect / logout.</summary>
    TokenCleanupFailed,
    /// <summary>A plugin DLL was refused by <see cref="IPluginTrustPolicy"/> (hash mismatch, missing manifest).</summary>
    PluginTrustRejected,
    /// <summary>Outbound HTTP request rejected by the per-provider allow-list handler.</summary>
    HttpClientHostRejected,
    /// <summary>ScriptWorker ran WITHOUT its OS sandbox because the user set the ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS override while the sandbox primitive was unavailable.</summary>
    UnsandboxedScriptOverride,
    /// <summary>Audio engine command ring buffer overflowed; a voice command was dropped. Not security-sensitive, but shares the same telemetry plumbing because it's another "silent drop" event users need to be able to review after the fact.</summary>
    AudioCommandDropped,
    /// <summary>A hosted-account sign-in succeeded.</summary>
    AuthLoginSucceeded,
    /// <summary>A hosted-account sign-in failed (bad credentials or unknown user).</summary>
    AuthLoginFailed,
    /// <summary>A hosted-account sign-in was refused because the account is locked out after too many failed attempts.</summary>
    AuthLockout,
    /// <summary>A new hosted account was created via the registration page.</summary>
    AuthRegistration,
    /// <summary>A hosted-account password-reset link was requested (via the ForgotPassword page or the admin CLI). Records the submitted email so the operator can action it; does NOT confirm the address exists.</summary>
    AuthPasswordResetRequested,
    /// <summary>A hosted-account password was successfully reset via a reset token on the ResetPassword page.</summary>
    AuthPasswordReset,
    /// <summary>Two-factor authentication was enabled on a hosted account (authenticator app enrolled, recovery codes issued).</summary>
    AuthTwoFactorEnabled,
    /// <summary>Two-factor authentication was disabled on a hosted account (password-confirmed).</summary>
    AuthTwoFactorDisabled,
    /// <summary>A two-factor sign-in challenge succeeded (authenticator code).</summary>
    AuthTwoFactorLoginSucceeded,
    /// <summary>A two-factor sign-in challenge failed (wrong/expired code). Repeated failures trip the same lockout as passwords.</summary>
    AuthTwoFactorLoginFailed,
    /// <summary>A single-use recovery code was consumed to complete a two-factor sign-in.</summary>
    AuthRecoveryCodeUsed,
    /// <summary>A fresh set of two-factor recovery codes was generated (invalidates all previous codes).</summary>
    AuthRecoveryCodesGenerated,
    /// <summary>Generic catch-all for other security-relevant incidents — prefer a specific kind.</summary>
    Other,
}

/// <summary>
/// Immutable record of a single security-relevant event. Timestamp is
/// always UTC so events are directly comparable across machines on a
/// desk (traders in different timezones).
/// </summary>
/// <param name="UtcTimestamp">When the event was recorded.</param>
/// <param name="Kind">See <see cref="SecurityEventKind"/>.</param>
/// <param name="Source">Short identifier of the component that raised the event — a provider id, a service name, etc.</param>
/// <param name="Message">Human-readable description. Should not include secrets.</param>
/// <param name="Data">Optional key/value diagnostic details (numeric codes, file paths). Values are rendered as-is in exports, so do not put secrets here either.</param>
public sealed record SecurityEvent(
    DateTime UtcTimestamp,
    SecurityEventKind Kind,
    string Source,
    string Message,
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// Append-only sink for <see cref="SecurityEvent"/> records. The host
/// registers a concrete implementation in DI and sets
/// <see cref="PluginHostServices.SecurityEvents"/> during startup;
/// provider plugins and Core services push to it.
///
/// <para>
/// Default in-process implementation (see Core's
/// <c>SecurityEventLog</c>) keeps a ring buffer of the last N events
/// and optionally mirrors to <see cref="Microsoft.Extensions.Logging"/>
/// for live telemetry. Tests / headless CLI runs may leave
/// <see cref="PluginHostServices.SecurityEvents"/> null — call sites
/// null-check.
/// </para>
/// </summary>
public interface ISecurityEventLog
{
    /// <summary>Record an event. Never throws.</summary>
    void Record(SecurityEvent ev);

    /// <summary>
    /// Snapshot of recent events, newest first. Returns at most
    /// <paramref name="limit"/> entries. <paramref name="since"/>
    /// filters to events recorded after that UTC instant.
    /// </summary>
    IReadOnlyList<SecurityEvent> Recent(int limit = 200, DateTime? since = null);
}
