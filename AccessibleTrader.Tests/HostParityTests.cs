using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// The two heads must register the same services.
///
/// <para>
/// <b>Why this test exists and why it is a source scan.</b> The MAUI desktop head cannot be built on
/// the development machine — no MAUI workloads, and none of its target frameworks target Linux — so
/// it is compiled only by CI and has never been <i>run</i> during two release cycles. That makes one
/// class of defect completely invisible: a service registered in
/// <c>WebHost/ServiceCollectionExtensions.cs</c> and forgotten in
/// <c>BlazorClient/ServiceCollectionExtensions.cs</c>. It compiles on both heads, passes every unit
/// test, and then throws at startup on the head nobody launches.
/// </para>
///
/// <para>
/// Reflection cannot help here — the MAUI assembly is not loadable on this platform, which is the
/// whole problem. So the check reads the two registration files as text. That is cruder than a
/// container probe and it is the only thing that works from a machine that cannot build one of the
/// two subjects.
/// </para>
///
/// <para>
/// Divergence is legitimate and expected: browser speech and a WASAPI sink belong to one host each,
/// and secure storage has a genuinely different backend per platform. Those are listed by name
/// below, so an <i>intended</i> difference is a one-line edit and an <i>accidental</i> one fails the
/// build.
/// </para>
/// </summary>
public class HostParityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static HashSet<string> Registrations(string relativePath)
    {
        string path = Path.Combine(RepoRoot(), relativePath);
        Assert.True(File.Exists(path), $"{relativePath} not found — has the file moved?");

        // services.AddSingleton<IFoo, Foo>() / AddScoped<IFoo>(sp => …) / AddTransient<Foo>()
        return Regex.Matches(File.ReadAllText(path),
                @"services\.Add(?:Singleton|Scoped|Transient)<\s*([A-Za-z0-9_.]+)")
            .Select(m => m.Groups[1].Value.Split('.').Last())
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Services the two heads are ALLOWED to differ on, with the reason. Anything else appearing in
    /// one file and not the other is a defect.
    /// </summary>
    private static readonly Dictionary<string, string> IntendedDifferences = new(StringComparer.Ordinal)
    {
        ["BlazorSpeechManager"]        = "WebHost speaks through the browser / ARIA live region; MAUI uses its own manager.",
        ["IBrowserSpeechOutput"]       = "Browser-voice output has no meaning on a native head.",
        ["WebHostBrowserAudioSink"]    = "The web head routes audio through WebAudio; MAUI uses a native driver.",
        ["WebHostSecureStorageService"] = "Secure storage backend is genuinely per-platform.",
        ["MauiSecureStorageService"]   = "Secure storage backend is genuinely per-platform.",
        ["PaperAccountHub"] =
            "Hosted-only. A Blazor scope is a browser TAB, so the WebHost needs a per-user account "
          + "registry to stop two tabs keeping two account objects over one file. The desktop head "
          + "is single-user and registers PaperTradingProvider as a Singleton, which already gives "
          + "exactly one account.",
        ["PaperAccountAttachment"] =
            "Hosted-only, and paired with PaperAccountHub: it binds one circuit's chart to the "
          + "shared account and unbinds when the tab closes. A desktop head has one chart set and "
          + "no circuits to bind.",
        ["QuickTradeEquityHub"] =
            "Hosted-only, same shape as PaperAccountHub: one equity cache per USER so quick-trade "
          + "sizing never reads another user's balance, while tabs of one user still share. The "
          + "desktop head is single-user and registers QuickTradeEquity itself as a Singleton, "
          + "which both heads consume identically.",
    };

    private const string WebHostFile = "AccessibleTrader.WebHost/ServiceCollectionExtensions.cs";
    private const string MauiFile = "AccessibleTrader.BlazorClient/ServiceCollectionExtensions.cs";

    [Fact]
    public void BothHeadsRegisterTheSameServicesExceptWhereTheyMust()
    {
        var web = Registrations(WebHostFile);
        var maui = Registrations(MauiFile);

        var webOnly = web.Except(maui).Where(s => !IntendedDifferences.ContainsKey(s)).OrderBy(s => s).ToList();
        var mauiOnly = maui.Except(web).Where(s => !IntendedDifferences.ContainsKey(s)).OrderBy(s => s).ToList();

        Assert.True(webOnly.Count == 0,
            "Registered on the WebHost but NOT on the MAUI head, which is only built by CI and has "
          + "never been launched — so this would throw at startup there and nowhere else: "
          + string.Join(", ", webOnly)
          + ". Add it to BlazorClient/ServiceCollectionExtensions.cs, or list it in "
          + nameof(IntendedDifferences) + " with the reason.");

        Assert.True(mauiOnly.Count == 0,
            "Registered on the MAUI head but NOT on the WebHost: " + string.Join(", ", mauiOnly)
          + ". Add it to WebHost/ServiceCollectionExtensions.cs, or list it in "
          + nameof(IntendedDifferences) + " with the reason.");
    }

    /// <summary>
    /// Guards the guard. If the regex stopped matching — a refactor to an extension method, say —
    /// both sets would be empty and the parity test would pass by checking nothing at all.
    /// </summary>
    [Fact]
    public void TheScanActuallyFindsRegistrations()
    {
        Assert.True(Registrations(WebHostFile).Count > 50, "The WebHost scan found almost nothing — has the registration style changed?");
        Assert.True(Registrations(MauiFile).Count > 50, "The MAUI scan found almost nothing — has the registration style changed?");
    }

    /// <summary>
    /// Every allowance must still be real. An entry left behind after a service is deleted turns
    /// into a permanent hole in the check.
    /// </summary>
    [Fact]
    public void EveryIntendedDifferenceIsStillPresentSomewhere()
    {
        var all = Registrations(WebHostFile).Union(Registrations(MauiFile)).ToHashSet(StringComparer.Ordinal);

        var stale = IntendedDifferences.Keys.Where(k => !all.Contains(k)).OrderBy(k => k).ToList();

        Assert.True(stale.Count == 0,
            "These are listed as intended host differences but are no longer registered anywhere, so "
          + "the allowance is now a blind spot: " + string.Join(", ", stale));
    }

    /// <summary>
    /// The MAUI head keeps its own copy of <c>app.css</c>, and CSS is not compiled — so no build on
    /// any machine validates it.
    ///
    /// <para>
    /// The 2.1.0 verification document recorded the specific fear: 121 theming edits went into the
    /// WebHost's copy while the MAUI copy was never seen, which would leave dialogs dark on dark.
    /// Comparing the theme custom properties is the cheapest possible check of that, and it can be
    /// done from a machine that cannot build the head at all.
    /// </para>
    /// </summary>
    [Fact]
    public void BothHeadsDefineTheSameThemeVariables()
    {
        string root = RepoRoot();
        var web = ThemeVars(Path.Combine(root, "AccessibleTrader.WebHost/wwwroot/app.css"));
        var maui = ThemeVars(Path.Combine(root, "AccessibleTrader.BlazorClient/wwwroot/app.css"));

        var missingFromMaui = web.Except(maui).OrderBy(v => v).ToList();
        var missingFromWeb = maui.Except(web).OrderBy(v => v).ToList();

        Assert.True(missingFromMaui.Count == 0,
            "Theme variables defined for the WebHost but not the MAUI head. CSS is not compiled, so "
          + "nothing else catches this and the result is unreadable text on the head nobody runs: "
          + string.Join(", ", missingFromMaui));

        Assert.True(missingFromWeb.Count == 0,
            "Theme variables defined for the MAUI head but not the WebHost: " + string.Join(", ", missingFromWeb));
    }

    private static HashSet<string> ThemeVars(string path)
    {
        Assert.True(File.Exists(path), $"{path} not found.");
        return Regex.Matches(File.ReadAllText(path), @"(--[a-z0-9-]+)\s*:")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }
}
