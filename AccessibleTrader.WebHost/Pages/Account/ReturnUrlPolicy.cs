using Microsoft.AspNetCore.Mvc;

namespace AccessibleTrader.WebHost.Pages.Account
{
    /// <summary>
    /// One place that decides where a successful sign-in / registration lands.
    ///
    /// <para>
    /// Every auth page takes <c>returnUrl</c> from the query string and fed it
    /// straight to <c>LocalRedirect</c>. <c>LocalRedirectResult</c> validates the
    /// URL — but it does so at <b>result-execution</b> time, by throwing
    /// <see cref="InvalidOperationException"/>. So
    /// <c>/account/login?returnUrl=https://example.com</c> produced a *successful*
    /// sign-in (the auth cookie is issued before the redirect is built) followed by
    /// an unhandled exception and a 500. The user's account is now signed in on a
    /// browser that was shown a server error, which is the worst of both outcomes:
    /// it is not a security hole (the redirect never happens) but it is a
    /// one-query-string denial of the login page, and it is trivially reachable by
    /// anyone who can get a user to click a link.
    /// </para>
    ///
    /// <para>
    /// <see cref="IUrlHelper.IsLocalUrl"/> is the framework's own answer and it is
    /// the strict one — it rejects absolute URLs, protocol-relative <c>//host</c>,
    /// and the backslash variants (<c>/\host</c>) that some parsers treat as
    /// authority separators. A non-local value is dropped rather than rejected:
    /// the visitor asked to sign in and they are signed in, so send them to the
    /// app root instead of failing the whole request over a bad hint.
    /// </para>
    /// </summary>
    public static class ReturnUrlPolicy
    {
        /// <summary>
        /// The supplied <paramref name="returnUrl"/> when it is a local path, else
        /// the application root. Never returns a value <c>LocalRedirect</c> would
        /// throw on.
        /// </summary>
        public static string Sanitize(IUrlHelper url, string? returnUrl)
            => !string.IsNullOrEmpty(returnUrl) && url.IsLocalUrl(returnUrl)
                ? returnUrl!
                : url.Content("~/");
    }
}
