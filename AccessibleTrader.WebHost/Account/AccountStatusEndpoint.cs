using Microsoft.AspNetCore.Identity;

namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// <c>GET /account/status</c> — whether this browser is signed in, for the public website.
    /// The site (accessibletrader.com) is PHP on the same host as the terminal, which nginx
    /// serves under <c>/terminal/</c>, so a same-origin fetch from the front page carries the
    /// terminal's <c>__Host-</c> cookie. Before this, the site could not tell a signed-in
    /// visitor from a stranger and kept offering "Create your free account" to someone who
    /// already had one (Cody, 2026-11-20: "that was confusing to me").
    ///
    /// Anonymous by design: the answer for a stranger is <c>signedIn: false</c>, not a
    /// redirect to the login page. The user is looked up, not just the cookie trusted, so a
    /// deleted account reads as signed out. Only the display name goes out — never the email —
    /// and nothing is sent to another origin (no CORS headers). <c>no-store</c> so signing out
    /// shows on the very next page load instead of a cached "signed in".
    /// </summary>
    public static class AccountStatusEndpoint
    {
        public const string Path = "/account/status";

        public sealed record Status(bool SignedIn, string? DisplayName);

        public static async Task<IResult> HandleAsync(HttpContext ctx, UserManager<AppUser> users)
        {
            ctx.Response.Headers.CacheControl = "no-store";
            AppUser? user = ctx.User.Identity?.IsAuthenticated == true
                ? await users.GetUserAsync(ctx.User)
                : null;
            return Results.Json(new Status(user != null, user?.DisplayName));
        }
    }
}
