using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json;

namespace AccessibleTrader.Plugins.Schwab
{
    /// <summary>
    /// Handles the Schwab Trader API OAuth2 authorization-code flow and keeps
    /// access tokens fresh. Refresh tokens live for 7 days — after that, the
    /// user must complete a full browser-based re-authorization.
    /// </summary>
    /// <remarks>
    /// Schwab allows a <c>127.0.0.1</c> loopback redirect URI, which is the
    /// cleanest way to capture the auth code from a native desktop app without
    /// requiring a hosted callback server. Registered redirect URIs must be
    /// HTTPS in production; the Schwab developer portal does allow a localhost
    /// HTTPS entry like <c>https://127.0.0.1:8443/callback</c> if the user
    /// registers one. For apps that register an HTTP loopback for development,
    /// the <see cref="RedirectUri"/> can be set to that scheme and port.
    ///
    /// Persistence:
    ///   1. <see cref="PluginHostServices.SecureStorage"/> — host-provided
    ///      keychain / KeyStore / DPAPI-backed store. This is the ONLY write
    ///      path. MAUI and the Full-mode WebHost set the bridge at startup;
    ///      the hosted multi-user WebHost deliberately leaves it null (its
    ///      secret store is process-wide, so a persisted token would be
    ///      shared by every signed-in user).
    ///   2. Non-persist — with no host bridge the token lives only in memory
    ///      and the user re-runs OAuth next session. Deliberately not
    ///      plaintext, deliberately not a hand-built file path.
    ///
    /// Earlier releases also kept a DPAPI-encrypted file at
    /// %APPDATA%\AccessibleTrader\schwab_refresh_token.json on Windows. That
    /// file is still READ once for migration (then moved into the bridge and
    /// deleted) but is never written again — see LoadPersistedRefreshToken.
    /// </remarks>
    public sealed class SchwabOAuthService : IDisposable
    {
        private const string AuthorizeUrl  = "https://api.schwabapi.com/v1/oauth/authorize";
        private const string TokenUrl      = "https://api.schwabapi.com/v1/oauth/token";
        private const string AppSubfolder  = "AccessibleTrader";
        private const string TokenFileName = "schwab_refresh_token.json";

        private readonly HttpClient _http;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);

        public string  ClientId     { get; private set; } = "";
        public string  ClientSecret { get; private set; } = "";
        public string  RedirectUri  { get; private set; } = "https://127.0.0.1:8443/callback";

        public string? AccessToken  { get; private set; }
        public string? RefreshToken { get; private set; }

        /// <summary>
        /// UTC timestamp after which <see cref="AccessToken"/> is assumed to be
        /// stale. We refresh 60 seconds early to avoid edge-case 401s.
        /// </summary>
        public DateTime AccessTokenExpiresAtUtc { get; private set; } = DateTime.MinValue;

        public bool HasRefreshToken => !string.IsNullOrEmpty(RefreshToken);
        public bool HasAccessToken  => !string.IsNullOrEmpty(AccessToken)
                                     && DateTime.UtcNow < AccessTokenExpiresAtUtc;

        public SchwabOAuthService(HttpClient httpClient)
        {
            _http = httpClient;
        }

        /// <summary>
        /// Pushes credentials into the service and loads any persisted refresh
        /// token for this client id. Must be called before any other method.
        /// </summary>
        public void Configure(string clientId, string clientSecret, string? redirectUri = null)
        {
            ClientId     = clientId     ?? "";
            ClientSecret = clientSecret ?? "";
            if (!string.IsNullOrWhiteSpace(redirectUri))
                RedirectUri = redirectUri!;

            LoadPersistedRefreshToken();
        }

        // Random state captured at the start of each authorization-code flow and
        // validated against the callback's `state` query parameter. Without this
        // an attacker can trick the user into opening a crafted authorize URL on
        // a shared/untrusted device and silently bind their own Schwab app to the
        // user's account. Generated fresh per `RunAuthorizationCodeFlowAsync` call.
        private string? _expectedState;

        /// <summary>
        /// Builds the full Schwab authorization URL that the user should open
        /// in a browser to start the auth-code flow. Pass the same <paramref
        /// name="state"/> value that the callback handler will validate.
        /// </summary>
        public string BuildAuthorizationUrl(string? state = null)
        {
            var query = $"?client_id={Uri.EscapeDataString(ClientId)}"
                      + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
                      + "&response_type=code";
            if (!string.IsNullOrEmpty(state))
                query += $"&state={Uri.EscapeDataString(state)}";
            return AuthorizeUrl + query;
        }

        /// <summary>
        /// Runs a one-shot local HTTPS listener on the redirect URI, opens the
        /// user's default browser to the Schwab authorization page, waits for
        /// the callback, and exchanges the code for tokens. The refresh token
        /// is persisted before this method returns.
        /// </summary>
        /// <param name="openBrowser">
        /// Callback invoked to open the URL. Defaults to
        /// <see cref="LaunchDefaultBrowser"/>. A host application that embeds
        /// a WebView or wants to delegate to the OS can supply a custom opener.
        /// </param>
        public async Task<SchwabTokenResponse> RunAuthorizationCodeFlowAsync(
            Action<string>? openBrowser = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
                throw new InvalidOperationException("Schwab client id/secret are not configured.");

            // HttpListener requires the prefix to end in '/'.
            var prefix = RedirectUri.EndsWith("/", StringComparison.Ordinal) ? RedirectUri : RedirectUri + "/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);

            try { listener.Start(); }
            catch (HttpListenerException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to bind local OAuth listener to {prefix}. " +
                    "On Windows, an HTTPS loopback may require a URL reservation (netsh http add urlacl) " +
                    "and a trusted certificate bound with 'netsh http add sslcert'. " +
                    "Alternatively, configure a plain http://127.0.0.1:<port>/callback redirect URI in the Schwab developer portal.",
                    ex);
            }

            // Generate a fresh CSRF-style state token per flow and stash it; the
            // callback handler below will reject any callback whose `state` query
            // parameter doesn't match. Mitigates the audit's CSRF gap on shared/
            // untrusted devices (2026-04-27 e19).
            _expectedState = Guid.NewGuid().ToString("N");
            (openBrowser ?? LaunchDefaultBrowser)(BuildAuthorizationUrl(_expectedState));

            // Stop() races with the finally block below and with listener.Close() --
            // ObjectDisposedException / InvalidOperationException are expected when we
            // already closed it. Swallowing is correct; the outer flow propagates the
            // real cancellation.
            using var reg = ct.Register(() => { try { listener.Stop(); } catch { } });

            string? code;
            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                code = context.Request.QueryString["code"];
                var error = context.Request.QueryString["error"];
                var returnedState = context.Request.QueryString["state"];

                // CSRF: the callback's state MUST match the value we put on the
                // outgoing authorize URL. Mismatch means either an unsolicited
                // callback (attacker-crafted URL) or a stale flow — refuse either.
                bool stateMatches = !string.IsNullOrEmpty(_expectedState)
                                 && string.Equals(returnedState, _expectedState, StringComparison.Ordinal);

                var body = error != null
                    ? $"<html><body><h2>Schwab authorization failed: {WebUtility.HtmlEncode(error)}</h2></body></html>"
                    : !stateMatches
                        ? "<html><body><h2>Schwab callback rejected: state mismatch (possible CSRF). Re-run the sign-in flow.</h2></body></html>"
                        : "<html><body><h2>Schwab authorization complete. You can close this window.</h2></body></html>";

                var buffer = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = "text/html";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                context.Response.Close();

                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException($"Schwab returned authorization error: {error}");
                if (!stateMatches)
                    throw new InvalidOperationException(
                        "Schwab callback rejected: state token mismatch. The callback may have been triggered by an unsolicited URL (CSRF) or by a stale authorization flow. Re-run sign-in.");
                if (string.IsNullOrEmpty(code))
                    throw new InvalidOperationException("Schwab callback did not contain an authorization code.");
            }
            finally
            {
                // Best-effort cleanup -- Stop() may race with the ct.Register callback
                // above; Close() is the authoritative release.
                try { listener.Stop(); } catch { }
                listener.Close();
                _expectedState = null;
            }

            return await ExchangeAuthorizationCodeAsync(code!, ct).ConfigureAwait(false);
        }

        /// <summary>Exchanges an auth code for access+refresh tokens.</summary>
        public async Task<SchwabTokenResponse> ExchangeAuthorizationCodeAsync(string authCode, CancellationToken ct = default)
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type",   "authorization_code"),
                new KeyValuePair<string, string>("code",         authCode),
                new KeyValuePair<string, string>("redirect_uri", RedirectUri),
            });

            return await PostTokenRequestAsync(form, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Refreshes the access token using the stored refresh token. Throws
        /// <see cref="SchwabReauthRequiredException"/> if the refresh token is
        /// missing, expired, or rejected by Schwab.
        /// </summary>
        public async Task<SchwabTokenResponse> RefreshAccessTokenAsync(CancellationToken ct = default)
        {
            await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (string.IsNullOrEmpty(RefreshToken))
                    throw new SchwabReauthRequiredException(
                        "No stored Schwab refresh token. User must run the authorization flow.");

                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type",    "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", RefreshToken!),
                });

                try
                {
                    return await PostTokenRequestAsync(form, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    // A 400/401 here almost always means the refresh token is
                    // past its 7-day life and the user must re-authorize.
                    RefreshToken = null;
                    DeletePersistedRefreshToken();
                    throw new SchwabReauthRequiredException(
                        "Schwab refresh token was rejected. Refresh tokens expire after 7 days — please re-authorize.", ex);
                }
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <summary>
        /// Returns a valid access token, refreshing it if necessary. Callers
        /// that receive a 401 despite this method succeeding should call
        /// <see cref="InvalidateAccessToken"/> and retry once.
        /// </summary>
        public async Task<string> GetValidAccessTokenAsync(CancellationToken ct = default)
        {
            if (HasAccessToken) return AccessToken!;
            await RefreshAccessTokenAsync(ct).ConfigureAwait(false);
            if (!HasAccessToken)
                throw new SchwabReauthRequiredException("Schwab access token is still missing after refresh.");
            return AccessToken!;
        }

        /// <summary>Marks the current access token as stale so the next call refreshes.</summary>
        public void InvalidateAccessToken()
        {
            AccessTokenExpiresAtUtc = DateTime.MinValue;
        }

        /// <summary>
        /// Seeds a refresh token from an external source (e.g. a user who pasted
        /// one they obtained out-of-band). Only applied when no persisted token
        /// is already loaded, so the auth-code flow remains the source of truth.
        /// </summary>
        public void SeedRefreshTokenIfMissing(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) return;
            if (!string.IsNullOrEmpty(RefreshToken)) return;
            RefreshToken = refreshToken;
            PersistRefreshToken();
        }

        // ── persistence ─────────────────────────────────────────────────────

        // DPAPI entropy for the LEGACY on-disk token file (read-only migration
        // path below). Must stay byte-identical to what old builds wrote with,
        // or existing desktop tokens become undecryptable.
        private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("AccessibleTrader/Schwab/RT/v1");

        // Host-bridge key. Scoped by ClientId so multiple Schwab app registrations
        // in the same install don't overwrite each other.
        private string HostStorageKey => $"schwab_refresh_{ClientId}";

        /// <summary>
        /// Location of the legacy DPAPI-encrypted token file that pre-bridge
        /// builds wrote, or null where it cannot exist. Windows-only by
        /// construction: %APPDATA% is the same directory old builds resolved,
        /// and reading the environment variable avoids composing a per-user
        /// path from Environment.GetFolderPath — the exact defect
        /// PerUserPathPolicyTests guards against (empty-string → relative path
        /// on Unix; no single per-user directory in the hosted head). This
        /// path is only ever read and deleted, never written.
        /// </summary>
        private static string? LegacyTokenFilePath()
        {
            if (!OperatingSystem.IsWindows()) return null;
            var appData = Environment.GetEnvironmentVariable("APPDATA");
            if (string.IsNullOrWhiteSpace(appData)) return null;
            return Path.Combine(appData, AppSubfolder, TokenFileName);
        }

        private void LoadPersistedRefreshToken()
        {
            // Canonical store: host-provided keychain / KeyStore / SecureStorage.
            var host = PluginHostServices.SecureStorage;
            if (host != null)
            {
                try
                {
                    // Host API is async but the legacy sync flow (Configure → use)
                    // can't await cleanly. Block on the task; SecureStorage reads
                    // are cheap and local. If it ever becomes a hot path we can
                    // expose a LoadPersistedAsync() that host code can await.
                    var stored = host.GetAsync(HostStorageKey).GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(stored)) { RefreshToken = stored; return; }
                }
                catch
                {
                    // Fall through to the legacy-file migration — don't let a
                    // host-bridge hiccup strand a user whose token is still in
                    // the old on-disk location.
                }
            }

            // Migration: legacy DPAPI-encrypted file written by pre-bridge
            // Windows builds. Loaded into memory, then moved into the bridge
            // (which deletes the file) when one is available. If no bridge is
            // wired the file is left in place so a later, bridged run can
            // still migrate it.
            var legacyPath = LegacyTokenFilePath();
            if (legacyPath == null) return;
            try
            {
                if (!File.Exists(legacyPath)) return;

                var raw = File.ReadAllBytes(legacyPath);
                byte[] plaintextBytes;
                try
                {
                    plaintextBytes = ProtectedData.Unprotect(raw, DpapiEntropy, DataProtectionScope.CurrentUser);
                }
                catch (CryptographicException)
                {
                    // Token was encrypted under a different user / machine key.
                    // Drop the unreadable file so the next launch re-authenticates cleanly.
                    try { File.Delete(legacyPath); } catch (IOException) { }
                    return;
                }

                var json = Encoding.UTF8.GetString(plaintextBytes);
                Array.Clear(plaintextBytes, 0, plaintextBytes.Length);

                var stored = JsonConvert.DeserializeObject<PersistedRefreshToken>(json);
                if (stored != null && stored.ClientId == ClientId && !string.IsNullOrEmpty(stored.RefreshToken))
                {
                    RefreshToken = stored.RefreshToken;
                    if (host != null) PersistRefreshToken();
                }
            }
            catch
            {
                // Corrupt file — ignore and force re-auth.
            }
        }

        private void PersistRefreshToken()
        {
            // Host-provided SecureStorage is the only write path. No bridge →
            // non-persist: the token lives in memory for this session and the
            // user re-authorizes next launch. The old DPAPI-file fallback is
            // gone on purpose — it was a hand-built per-user path, and on a
            // multi-user host one file would be shared by every signed-in user.
            var host = PluginHostServices.SecureStorage;
            if (host == null) return;
            try
            {
                host.SetAsync(HostStorageKey, RefreshToken ?? "").GetAwaiter().GetResult();
            }
            catch
            {
                // Non-fatal: next session will simply require re-auth.
                return;
            }

            // Clean up the legacy DPAPI artifact so the token doesn't exist in
            // two places. Failure isn't fatal (the file is keyed to the same
            // Windows user and readable only by this app) but is worth surfacing.
            var legacyPath = LegacyTokenFilePath();
            if (legacyPath == null) return;
            try { if (File.Exists(legacyPath)) File.Delete(legacyPath); }
            catch (Exception ex) { RecordTokenCleanupFailure("File.Delete (legacy DPAPI after host-write)", legacyPath, ex); }
        }

        private void DeletePersistedRefreshToken()
        {
            // Drop from host storage first — that's the canonical location.
            // Failures here are security-relevant: this is the explicit
            // scrub path called on disconnect / logout, and a silent failure
            // means the refresh token persists on disk past the point the
            // user thinks it's gone. Push each failure into the
            // ISecurityEventLog so operators can review.
            var host = PluginHostServices.SecureStorage;
            if (host != null)
            {
                try { host.Remove(HostStorageKey); }
                catch (Exception ex)
                {
                    RecordTokenCleanupFailure("SecureStorage.Remove", HostStorageKey, ex);
                }
            }

            var legacyPath = LegacyTokenFilePath();
            if (legacyPath == null) return;
            try
            {
                if (File.Exists(legacyPath)) File.Delete(legacyPath);
            }
            catch (Exception ex)
            {
                RecordTokenCleanupFailure("File.Delete", legacyPath, ex);
            }
        }

        private static void RecordTokenCleanupFailure(string operation, string target, Exception ex)
        {
            var log = PluginHostServices.SecurityEvents;
            if (log is null) return;
            log.Record(new SecurityEvent(
                DateTime.UtcNow,
                SecurityEventKind.TokenCleanupFailed,
                Source: "SchwabOAuthService",
                Message: $"{operation} failed: {ex.Message}",
                Data: new Dictionary<string, string>
                {
                    ["operation"] = operation,
                    ["target"]    = target,
                    ["exception"] = ex.GetType().Name,
                }));
        }

        // ── internals ───────────────────────────────────────────────────────

        private async Task<SchwabTokenResponse> PostTokenRequestAsync(FormUrlEncodedContent form, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = form };

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Schwab token endpoint returned {(int)resp.StatusCode}: {body}");

            var token = JsonConvert.DeserializeObject<SchwabTokenResponse>(body)
                        ?? throw new InvalidOperationException("Schwab token endpoint returned an empty body.");

            AccessToken  = token.AccessToken;
            if (!string.IsNullOrEmpty(token.RefreshToken))
                RefreshToken = token.RefreshToken;

            // Refresh 60s early to dodge clock skew / TLS latency.
            var ttl = Math.Max(60, token.ExpiresIn - 60);
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(ttl);

            if (!string.IsNullOrEmpty(RefreshToken))
                PersistRefreshToken();

            return token;
        }

        private static void LaunchDefaultBrowser(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = url,
                    UseShellExecute = true,
                });
            }
            catch { /* host should have supplied a custom opener if this fails */ }
        }

        public void Dispose()
        {
            _refreshGate.Dispose();
        }

        private sealed class PersistedRefreshToken
        {
            public string   ClientId     { get; set; } = "";
            public string   RefreshToken { get; set; } = "";
            public DateTime StoredAtUtc  { get; set; }
        }
    }
}
