namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Whether a Playwright Chromium is on this machine, decided cheaply enough to run at test
/// DISCOVERY time so <see cref="BrowserFactAttribute"/> can turn the whole browser sweep into
/// skips rather than a wall of identical launch failures.
///
/// <para>
/// A skip is a lie the moment nobody notices it, which is why
/// <c>BrowserHarnessSelfTests.The_browser_suite_is_not_silently_skipping</c> prints the reason
/// and why <see cref="SkipReason"/> names the exact command that fixes it. The rule this repo
/// runs on is that a green suite has to mean something; "0 browser tests ran" must be visible.
/// </para>
/// </summary>
internal static class BrowserAvailability
{
    /// <summary>Null when a browser is present; otherwise why it is not, and how to get one.</summary>
    public static string? SkipReason { get; } = Probe();

    /// <summary>The browsers root Playwright will read (env override, else the platform default).</summary>
    public static string BrowsersRoot { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        var overridden = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

        // Playwright's defaults, node and .NET alike.
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "ms-playwright");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "ms-playwright");
    }

    private static string? Probe()
    {
        var root = ResolveRoot();
        var install =
            "Install one with either of:\n" +
            "  npx playwright@1.55.0 install chromium\n" +
            "  pwsh AccessibleTrader.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium\n" +
            $"(browsers root: {root})";

        if (!Directory.Exists(root))
            return $"No Playwright browsers directory. {install}";

        bool anyChromium = Directory.EnumerateDirectories(root, "chromium*")
            .Any(d => Directory.EnumerateFiles(d, "headless_shell", SearchOption.AllDirectories).Any()
                   || Directory.EnumerateFiles(d, "chrome", SearchOption.AllDirectories).Any());

        return anyChromium ? null : $"No Chromium build under {root}. {install}";
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips when this machine has no Chromium, so the browser
/// sweep degrades to "did not run" instead of "failed 40 times for one reason".
/// </summary>
internal sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (BrowserAvailability.SkipReason is { } reason) Skip = reason;
    }
}

/// <summary>Theory counterpart of <see cref="BrowserFactAttribute"/>.</summary>
internal sealed class BrowserTheoryAttribute : TheoryAttribute
{
    public BrowserTheoryAttribute()
    {
        if (BrowserAvailability.SkipReason is { } reason) Skip = reason;
    }
}
