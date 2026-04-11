using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    /// Persistence: refresh tokens are written to a plain-text file inside the
    /// user's <see cref="Environment.SpecialFolder.ApplicationData"/> folder.
    /// This file is intended to be the sole location on disk that holds the
    /// refresh token. The host application is responsible for protecting that
    /// directory (the app's data folder already contains API-key secrets).
    /// </remarks>
    public sealed class SchwabOAuthService : IDisposable
    {
        private const string AuthorizeUrl  = "https://api.schwabapi.com/v1/oauth/authorize";
        private const string TokenUrl      = "https://api.schwabapi.com/v1/oauth/token";
        private const string AppSubfolder  = "AccessibleTrader";
        private const string TokenFileName = "schwab_refresh_token.json";

        private readonly HttpClient _http;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly string _tokenFilePath;

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

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir     = Path.Combine(appData, AppSubfolder);
            Directory.CreateDirectory(dir);
            _tokenFilePath = Path.Combine(dir, TokenFileName);
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

        /// <summary>
        /// Builds the full Schwab authorization URL that the user should open
        /// in a browser to start the auth-code flow.
        /// </summary>
        public string BuildAuthorizationUrl()
        {
            var query = $"?client_id={Uri.EscapeDataString(ClientId)}"
                      + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
                      + "&response_type=code";
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

            (openBrowser ?? LaunchDefaultBrowser)(BuildAuthorizationUrl());

            using var reg = ct.Register(() => { try { listener.Stop(); } catch { } });

            string? code;
            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                code = context.Request.QueryString["code"];
                var error = context.Request.QueryString["error"];

                var body = error != null
                    ? $"<html><body><h2>Schwab authorization failed: {WebUtility.HtmlEncode(error)}</h2></body></html>"
                    : "<html><body><h2>Schwab authorization complete. You can close this window.</h2></body></html>";

                var buffer = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = "text/html";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                context.Response.Close();

                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException($"Schwab returned authorization error: {error}");
                if (string.IsNullOrEmpty(code))
                    throw new InvalidOperationException("Schwab callback did not contain an authorization code.");
            }
            finally
            {
                try { listener.Stop(); } catch { }
                listener.Close();
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
                        "No Schwab refresh token on disk. User must run the authorization flow.");

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

        private void LoadPersistedRefreshToken()
        {
            try
            {
                if (!File.Exists(_tokenFilePath)) return;
                var json = File.ReadAllText(_tokenFilePath);
                var stored = JsonConvert.DeserializeObject<PersistedRefreshToken>(json);
                if (stored != null && stored.ClientId == ClientId && !string.IsNullOrEmpty(stored.RefreshToken))
                {
                    RefreshToken = stored.RefreshToken;
                }
            }
            catch
            {
                // Corrupt file — ignore and force re-auth.
            }
        }

        private void PersistRefreshToken()
        {
            try
            {
                var payload = new PersistedRefreshToken
                {
                    ClientId     = ClientId,
                    RefreshToken = RefreshToken ?? "",
                    StoredAtUtc  = DateTime.UtcNow,
                };
                File.WriteAllText(_tokenFilePath, JsonConvert.SerializeObject(payload));
            }
            catch
            {
                // Non-fatal: next session will simply require re-auth.
            }
        }

        private void DeletePersistedRefreshToken()
        {
            try { if (File.Exists(_tokenFilePath)) File.Delete(_tokenFilePath); }
            catch { }
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
