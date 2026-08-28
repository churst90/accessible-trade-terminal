using System.Text.Encodings.Web;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// The page <c>app.UseExceptionHandler("/Error")</c> re-executes into.
    ///
    /// <para>
    /// <b>It did not exist.</b> Grepping the tree found no <c>@page "/Error"</c>, no
    /// <c>Error.cshtml</c> and no <c>Error.razor</c>, so every unhandled exception in
    /// a production run re-executed at a path that only matched the Blazor fallback.
    /// On the accounts head that fallback carries <c>RequireAuthorization()</c>, so an
    /// unauthenticated failure became a redirect to the login page and an
    /// authenticated one booted a whole fresh Blazor circuit *inside* a 500 response
    /// and rendered the Router's &lt;NotFound&gt; — "Page not found." The user who just
    /// lost their work was told the page does not exist.
    /// </para>
    ///
    /// <para>
    /// Plain server-rendered HTML on purpose, for the same reason the recent-alerts
    /// page is: booting an interactive circuit is exactly the machinery that may have
    /// just failed, and a page that needs a working SignalR connection to tell you the
    /// server broke is not an error page. It carries no exception text — the trace
    /// identifier is the correlation handle, and the detail stays in the log.
    /// </para>
    ///
    /// <para>
    /// Accessibility: the heading is the first thing in the document, the message sits
    /// in a <c>role="alert"</c> region so it is announced on arrival even though the
    /// navigation was not user-initiated, and the way out is a real link rather than a
    /// history-dependent "go back" instruction.
    /// </para>
    /// </summary>
    public static class ErrorPage
    {
        /// <summary>
        /// Renders the page. <paramref name="traceId"/> is shown verbatim so a user can
        /// read it to an operator; it is HTML-encoded like everything else here because
        /// <c>TraceIdentifier</c> is not guaranteed to be markup-safe.
        /// </summary>
        public static string Render(string traceId, string homePath)
        {
            var enc = HtmlEncoder.Default;
            return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">"
                 + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
                 + "<title>Something went wrong — Accessible Trade Terminal</title></head><body>"
                 + "<h1>Something went wrong</h1>"
                 + "<div role=\"alert\">"
                 + "<p>The server hit an unexpected error and could not finish that request. "
                 + "Nothing you had already saved has been lost.</p>"
                 + "<p>Try the action again. If it keeps failing, quote this reference: <strong>"
                 + enc.Encode(traceId) + "</strong></p>"
                 + "</div>"
                 + "<p><a href=\"" + enc.Encode(homePath) + "\">Return to the terminal</a></p>"
                 + "</body></html>";
        }
    }
}
