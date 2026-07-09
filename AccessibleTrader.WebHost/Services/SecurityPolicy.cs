using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace AccessibleTrader.WebHost.Services;

/// <summary>
/// Response security headers for every WebHost mode (Phase A hardening,
/// 2026-07). Values live here — not inline in Program.cs — so tests can pin
/// them and the CSP has one authoritative definition.
///
/// <para>
/// CSP notes: the app ships no inline <c>&lt;script&gt;</c> (App.razor loads
/// everything from files), so <c>script-src 'self'</c> is safe.
/// <c>style-src 'unsafe-inline'</c> is required — Blazor components position
/// elements with style attributes (context menu, pane layout) and Blazor's
/// reconnect overlay injects inline styles. <c>connect-src</c> lists ws/wss
/// explicitly for older browsers where 'self' does not cover scheme upgrades
/// to the SignalR circuit. <c>frame-ancestors 'none'</c> +
/// <c>X-Frame-Options DENY</c>: the public builds are reverse-proxied under a
/// subpath, never iframed — if that ever changes, relax both together.
/// </para>
/// </summary>
public static class SecurityHeadersPolicy
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self'; "
        + "style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data:; "
        + "font-src 'self'; "
        + "connect-src 'self' ws: wss:; "
        + "object-src 'none'; "
        + "base-uri 'self'; "
        + "form-action 'self'; "
        + "frame-ancestors 'none'";

    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    /// <summary>
    /// Applies the standard header set to a response. HSTS is only sent on
    /// HTTPS requests (the hosted/demo deployments terminate TLS at nginx and
    /// forward X-Forwarded-Proto, so IsHttps is accurate there; plain local
    /// HTTP runs must not send HSTS at all).
    /// </summary>
    public static void Apply(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        h["Content-Security-Policy"] = ContentSecurityPolicy;
        if (ctx.Request.IsHttps)
            h["Strict-Transport-Security"] = StrictTransportSecurity;
    }
}

/// <summary>
/// Rate-limit partitioning for the hosted endpoint. Two tiers per client IP:
/// a generous general window for page loads / SignalR negotiates, and a much
/// stricter window for credential-guessing surfaces (POST to the login and
/// register pages). Before 2026-07 a single 200-per-10s window applied to
/// everything — ~72k password attempts per hour per IP.
/// </summary>
public static class AuthRateLimitPolicy
{
    public const int GeneralPermitLimit = 200;
    public static readonly System.TimeSpan GeneralWindow = System.TimeSpan.FromSeconds(10);

    // 10 credential attempts per 5 minutes per IP. Legitimate users retry a
    // handful of times; Identity's per-account lockout (10 failures) is the
    // second layer. Screen-reader users are slower typists than average —
    // the limit must never be so tight that honest retries hit 429.
    public const int AuthPermitLimit = 10;
    public static readonly System.TimeSpan AuthWindow = System.TimeSpan.FromMinutes(5);

    /// <summary>
    /// True for requests that spend a credential-guessing attempt: POSTs to
    /// the login/register pages. GETs (rendering the form) stay in the
    /// general tier. Path is relative to any UsePathBase prefix.
    /// </summary>
    public static bool IsAuthMutation(string method, PathString path)
        => HttpMethods.IsPost(method)
           && (path.StartsWithSegments("/account/login")
            || path.StartsWithSegments("/account/register"));

    public static RateLimitPartition<string> GetPartition(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (IsAuthMutation(http.Request.Method, http.Request.Path))
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                $"auth:{ip}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = AuthPermitLimit,
                    Window = AuthWindow,
                    QueueLimit = 0,
                });
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            $"general:{ip}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = GeneralPermitLimit,
                Window = GeneralWindow,
                QueueLimit = 0,
            });
    }
}
