namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The desktop and web heads share one stylesheet, kept as two files.</b>
///
/// <para>
/// <c>AccessibleTrader.BlazorClient/wwwroot/app.css</c> (the MAUI head) and
/// <c>AccessibleTrader.WebHost/wwwroot/app.css</c> style the same shared components. They had
/// drifted: the 2026-07-26 phone fix that lets the touch-nav button row wrap reached only the
/// desktop copy, so the web head, which is where phones actually visit, still overflowed. The
/// web copy legitimately has more: the first-visit speech chooser and the circuit reconnect
/// overlay only exist where there is a browser and a SignalR circuit.
/// </para>
///
/// <para>
/// So the rule is a prefix rule. Everything in the web copy ABOVE its "WEB-ONLY BELOW THIS LINE"
/// marker must equal the whole desktop copy. A shared rule added to one file and not the other
/// turns this red, and says which.
/// </para>
/// </summary>
public sealed class SharedStylesheetParityTests
{
    private const string WebOnlyMarker = "/* ══ WEB-ONLY BELOW THIS LINE";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Normalise(string css) => css.Replace("\r\n", "\n").TrimEnd();

    [Fact]
    public void The_web_stylesheet_is_the_desktop_one_plus_a_marked_web_only_tail()
    {
        string root = RepoRoot();
        string desktop = Normalise(File.ReadAllText(Path.Combine(root, "AccessibleTrader.BlazorClient", "wwwroot", "app.css")));
        string web = File.ReadAllText(Path.Combine(root, "AccessibleTrader.WebHost", "wwwroot", "app.css")).Replace("\r\n", "\n");

        int marker = web.IndexOf(WebOnlyMarker, StringComparison.Ordinal);
        Assert.True(marker > 0, "the web app.css has lost its WEB-ONLY marker; restore it above the web-only rules");
        string shared = Normalise(web[..marker]);

        if (shared == desktop) return;

        // Name the first line that differs, so the failure says where to look.
        var d = desktop.Split('\n');
        var w = shared.Split('\n');
        int i = 0;
        while (i < d.Length && i < w.Length && d[i] == w[i]) i++;
        Assert.Fail(
            $"app.css has drifted between the heads at shared line {i + 1}.\n" +
            $"  desktop (BlazorClient): {(i < d.Length ? d[i].Trim() : "<end of file>")}\n" +
            $"  web     (WebHost):      {(i < w.Length ? w[i].Trim() : "<web-only marker>")}\n" +
            "A shared rule belongs in BOTH files; a web-only rule belongs below the marker.");
    }
}
