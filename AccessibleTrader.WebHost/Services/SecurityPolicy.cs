using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
/// <c>X-Frame-Options DENY</c> on the hosted accounts terminal and the desktop
/// build; the public <c>--demo</c> build relaxes both to same-origin
/// (<c>frame-ancestors 'self'</c> / <c>SAMEORIGIN</c>) because it is embedded in a
/// same-origin iframe on the marketing homepage.
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

    /// <summary>Same as <see cref="ContentSecurityPolicy"/> but with
    /// <c>frame-ancestors 'self'</c>. The public <c>--demo</c> build is embedded
    /// in a same-origin &lt;iframe&gt; on the marketing homepage, so it must permit
    /// same-origin framing; no other mode does.</summary>
    public const string DemoContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self'; "
        + "style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data:; "
        + "font-src 'self'; "
        + "connect-src 'self' ws: wss:; "
        + "object-src 'none'; "
        + "base-uri 'self'; "
        + "form-action 'self'; "
        + "frame-ancestors 'self'";

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
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        // The public --demo build is DESIGNED to be embedded in a same-origin
        // <iframe> on the marketing homepage, so it allows same-origin framing.
        // Every other mode (hosted accounts terminal, local desktop) refuses all
        // framing. Null-safe: contexts without a service provider (e.g. unit tests
        // using a bare DefaultHttpContext) fall through to the strict default.
        bool isDemo = ctx.RequestServices?
            .GetService<AccessibleTrader.Core.Services.DemoPolicy>()?.IsDemo == true;
        if (isDemo)
        {
            h["X-Frame-Options"] = "SAMEORIGIN";
            h["Content-Security-Policy"] = DemoContentSecurityPolicy;
        }
        else
        {
            h["X-Frame-Options"] = "DENY";
            h["Content-Security-Policy"] = ContentSecurityPolicy;
        }

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
    /// True for requests that spend a credential-guessing attempt or write an
    /// attacker-controlled audit record: POSTs to the login, register, 2FA,
    /// recovery-code and password-reset pages. GETs (rendering the form) stay
    /// in the general tier. Path is relative to any UsePathBase prefix.
    /// ForgotPassword matters even though it guesses no credential: every POST
    /// appends an attacker-supplied email to the security log under a global
    /// file lock, so the general 200-per-10s tier is an unauthenticated
    /// disk-fill. Note StartsWithSegments is segment-boundary aware, so
    /// "/account/login" does NOT cover "/account/loginwith2fa" — each page is
    /// listed explicitly.
    /// </summary>
    public static bool IsAuthMutation(string method, PathString path)
        => HttpMethods.IsPost(method)
           && (path.StartsWithSegments("/account/login")
            || path.StartsWithSegments("/account/register")
            || path.StartsWithSegments("/account/loginwith2fa")
            || path.StartsWithSegments("/account/loginwithrecovery")
            || path.StartsWithSegments("/account/forgotpassword")
            || path.StartsWithSegments("/account/resetpassword"));

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

/// <summary>
/// Refuses to serve <c>HostMode.Full</c> on a non-loopback address. Full mode
/// is the fully-trusted local terminal — live trading, the API-keys modal and
/// server-side Roslyn scripts all on, with no authentication in front of any
/// of them. Before this guard, the difference between that and the hosted
/// product was one absent command-line flag: a WebHost started with neither
/// <c>--accounts</c> nor <c>--demo</c> on a public bind handed every anonymous
/// visitor the whole terminal. Program.cs checks the actually-bound addresses
/// right after Kestrel starts and shuts down if any is non-loopback, unless
/// the operator passed <c>--unsafe-remote-full</c> (for a trusted LAN behind a
/// firewall, where they accept that every visitor is themselves).
/// </summary>
public static class FullModeBindPolicy
{
    /// <summary>
    /// Returns the first bound address that is not loopback, or null when every
    /// address is safe for an unauthenticated Full-mode terminal. Addresses that
    /// cannot be parsed (Kestrel's <c>http://+:80</c> / <c>http://*:80</c>
    /// wildcard forms) bind every interface, so they fail closed.
    /// </summary>
    public static string? FindNonLoopbackAddress(System.Collections.Generic.IEnumerable<string> boundAddresses)
    {
        foreach (var address in boundAddresses)
        {
            if (!IsLoopback(address)) return address;
        }
        return null;
    }

    internal static bool IsLoopback(string address)
    {
        if (!System.Uri.TryCreate(address, System.UriKind.Absolute, out var uri))
            return false; // wildcard binds ("+", "*") and garbage both fail closed
        if (uri.Host.Equals("localhost", System.StringComparison.OrdinalIgnoreCase))
            return true;
        // IdnHost strips the brackets from IPv6 literals; "[::1]" parses either
        // way, but be explicit. "0.0.0.0" and "::" parse fine and are NOT
        // loopback — they bind every interface.
        return System.Net.IPAddress.TryParse(uri.IdnHost, out var ip)
            && System.Net.IPAddress.IsLoopback(ip);
    }
}
