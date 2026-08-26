namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Thrown when the terminal never reached the state a test can drive — the heading never
/// appeared, or the input pipeline never armed.
///
/// <para>
/// This type exists because of what a bare Playwright timeout costs. On 2026-08-26 every one of
/// the 45 browser tests that got to run on CI failed with the same four lines:
/// <c>Timeout 30000ms exceeded. Call log: waiting for Locator("#main-heading") to be visible</c>.
/// That message says the page did not render and nothing else — not the HTTP status, not whether
/// <c>blazor.web.js</c> was fetched, not the JavaScript exception on the console, and above all
/// not the server-side exception, even though <see cref="TerminalServerFactory"/> captures every
/// log line the host writes and its own documentation says that log "is usually the only place
/// that says why". The evidence was collected and then thrown away at the moment of failure.
/// </para>
///
/// <para>
/// So the rule this type enforces: a page that does not load reports the state of BOTH ends of
/// the connection — what the browser saw and what the server logged — in the failure message
/// itself, because on CI there is no second chance to look.
/// </para>
///
/// <para>
/// Note especially that <c>#main-heading</c> is not server-rendered. <c>App.razor</c> mounts the
/// app tree with <c>InteractiveServerRenderMode(prerender: false)</c>, so the first response is a
/// near-empty document and every element this harness looks for arrives only after the Blazor
/// circuit is established over its WebSocket. "The heading is not there" therefore means "the
/// circuit never rendered", which is a far larger set of causes than a slow page: a 404 on the
/// framework script, a rejected WebSocket, or an exception thrown in the first render of
/// <c>MainLayout</c> all look identical from the locator's point of view.
/// </para>
/// </summary>
internal sealed class AppNeverLoadedException : Exception
{
    public AppNeverLoadedException(string message) : base(message) { }

    /// <summary>
    /// Builds the report. Every section is included even when empty, because "no console errors"
    /// and "no failed requests" are themselves findings — they rule out the whole class of
    /// missing-asset causes and point at the server instead.
    /// </summary>
    public static AppNeverLoadedException Build(
        string stage,
        string rootUrl,
        int? httpStatus,
        string? html,
        IReadOnlyList<string> consoleErrors,
        IReadOnlyList<string> pageErrors,
        IReadOnlyList<string> failedRequests,
        IReadOnlyList<string> serverLog,
        Exception inner)
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine($"The terminal never loaded: {stage}");
        report.AppendLine($"  url:         {rootUrl}");
        report.AppendLine($"  http status: {(httpStatus is null ? "(no navigation response)" : httpStatus.ToString())}");
        report.AppendLine($"  underlying:  {inner.GetType().Name}: {FirstLine(inner.Message)}");
        report.AppendLine();

        Section(report, "browser console errors", consoleErrors, 15);
        Section(report, "unhandled JS exceptions", pageErrors, 10);
        Section(report, "failed requests", failedRequests, 20);
        Section(report, "server log (tail)", Tail(serverLog, 40), 40);

        report.AppendLine("── document at the moment of failure ──");
        report.AppendLine(html is null ? "  (could not read)" : Clip(html, 4_000));

        return new AppNeverLoadedException(report.ToString());
    }

    private static void Section(System.Text.StringBuilder sb, string title, IReadOnlyList<string> lines, int max)
    {
        sb.AppendLine($"── {title} ({lines.Count}) ──");
        if (lines.Count == 0) sb.AppendLine("  (none)");
        else
        {
            foreach (var line in lines.Take(max)) sb.AppendLine("  " + Clip(FirstLine(line), 300));
            if (lines.Count > max) sb.AppendLine($"  … {lines.Count - max} more");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// The tail, not the head: host startup writes hundreds of routine lines before anything
    /// interesting happens, and the exception that killed the circuit is always the last thing in.
    /// </summary>
    private static IReadOnlyList<string> Tail(IReadOnlyList<string> lines, int count) =>
        lines.Count <= count ? lines : lines.Skip(lines.Count - count).ToList();

    private static string FirstLine(string s)
    {
        var i = s.IndexOf('\n');
        return i < 0 ? s : s[..i].TrimEnd('\r');
    }

    private static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"… (+{s.Length - max} chars)";
}
