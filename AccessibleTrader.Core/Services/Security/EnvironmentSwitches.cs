namespace AccessibleTrader.Core.Services.Security;

/// <summary>
/// The environment switches both heads read for the security-event file and the plugin-trust
/// override. Until 2026-11-20 each was parsed by copy and paste in three places (the desktop
/// head, the hosted head and its plugin bridge), so one head could come to read "off" or "yes"
/// differently from another and its audit sink would quietly differ (code review).
/// </summary>
public static class EnvironmentSwitches
{
    /// <summary>
    /// Whether security events are written to disk. On unless
    /// <c>ACCESSIBLETRADER_SECURITY_EVENT_PERSIST</c> is <c>0</c> or <c>false</c> (any case).
    /// </summary>
    public static bool SecurityEventsPersisted() =>
        SecurityEventsPersisted(Environment.GetEnvironmentVariable("ACCESSIBLETRADER_SECURITY_EVENT_PERSIST"));

    internal static bool SecurityEventsPersisted(string? value) =>
        string.IsNullOrEmpty(value)
        || !(value.Equals("0", StringComparison.Ordinal) || value.Equals("false", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>ACCESSIBLETRADER_SECURITY_EVENT_DIR</c>: one directory for every security event file,
    /// for operators shipping them to a log collector; null when unset.
    /// </summary>
    public static string? SecurityEventDirectory() =>
        Environment.GetEnvironmentVariable("ACCESSIBLETRADER_SECURITY_EVENT_DIR");

    /// <summary>
    /// Whether <c>ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS</c> lets plugin DLLs outside the
    /// trusted manifest load: only <c>1</c> or <c>true</c> (any case). Development only.
    /// </summary>
    public static bool UnverifiedPluginsAllowed() =>
        UnverifiedPluginsAllowed(Environment.GetEnvironmentVariable("ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS"));

    internal static bool UnverifiedPluginsAllowed(string? value) =>
        !string.IsNullOrEmpty(value)
        && (value.Equals("1", StringComparison.Ordinal) || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
