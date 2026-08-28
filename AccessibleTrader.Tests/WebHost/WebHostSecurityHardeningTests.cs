using System.Net;
using AccessibleTrader.WebHost.Account;
using AccessibleTrader.WebHost.Services;
using AccessibleTrader.WebHost.Services.Push;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The MEDIUM/LOW half of the 2026-08-24 hosted-WebHost security audit — the items the
/// HIGH pass left behind. Each test names the defect it was proven red against.
///
/// <para>
/// Grouped by unit vs. integration rather than by finding: the cheap deterministic
/// assertions live here, and the ones that need a real request through the real pipeline
/// live in <see cref="WebHostSecurityHardeningIntegrationTests"/>.
/// </para>
/// </summary>
public class WebHostSecurityHardeningTests
{
    // ── TODO:5462 — /account/security was an unthrottled password oracle ─────────

    /// <summary>
    /// Both signed-in verification surfaces must sit in the strict credential tier.
    /// <c>/account/security</c> POSTs verify the current password (the gate in front of
    /// "disable two-factor") and <c>/account/enable2fa</c> POSTs verify a TOTP code; both
    /// sat in the general 200-per-10s tier, i.e. 72,000 guesses per hour per IP — the exact
    /// number <c>SecurityPolicy</c>'s own summary records as the bug that was fixed for the
    /// login page. Being behind <c>[Authorize]</c> is not a rate limit: an attacker holding
    /// a stolen session is already past it.
    /// </summary>
    [Theory]
    [InlineData("/account/security")]
    [InlineData("/account/enable2fa")]
    public void SignedIn_verification_posts_are_in_the_strict_auth_tier(string path)
    {
        Assert.True(AuthRateLimitPolicy.IsAuthMutation("POST", path),
            $"POST {path} verifies a credential and must be rate-limited in the auth tier, "
            + "not the 200-per-10-seconds general tier.");
    }

    /// <summary>
    /// The vacuity control for the theory above. If <c>IsAuthMutation</c> ever degenerated
    /// into "true for every POST", the assertions there would pass while the general tier
    /// stopped existing — and page loads, static assets and SignalR negotiates would all be
    /// charged ten-per-five-minutes, which locks honest users out of the app.
    /// </summary>
    [Theory]
    [InlineData("POST", "/account/logout")]
    [InlineData("GET", "/account/security")]
    [InlineData("GET", "/account/login")]
    [InlineData("POST", "/_blazor/negotiate")]
    public void Ordinary_traffic_stays_in_the_general_tier(string method, string path)
    {
        Assert.False(AuthRateLimitPolicy.IsAuthMutation(method, path));
    }

    // ── TODO:5506 — the rejection said nothing ──────────────────────────────────

    /// <summary>
    /// The 429 body must be announceable. It was a zero-length response with no
    /// <c>Retry-After</c>: on a product whose premise is that every refusal is spoken, the
    /// one refusal honest users actually hit — ten auth POSTs per five minutes, with the
    /// policy's own comment noting screen-reader users type slower than average — said
    /// nothing at all.
    /// </summary>
    [Fact]
    public void The_rate_limit_body_names_the_wait_in_an_alert_region()
    {
        string body = RateLimitRejection.Render(isAuthTier: true, retryAfterSeconds: 300);

        Assert.Contains("role=\"alert\"", body, StringComparison.Ordinal);
        Assert.Contains("5 minutes", body, StringComparison.Ordinal);
        Assert.Contains("sign-in attempts", body, StringComparison.Ordinal);
        // It must not read as an account problem — nothing has been locked.
        Assert.Contains("nothing has been locked", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_general_tier_rejection_does_not_talk_about_sign_in()
    {
        // A shared-NAT visitor tripping the general tier on static assets must not be told
        // their sign-in attempts are the problem.
        string body = RateLimitRejection.Render(isAuthTier: false, retryAfterSeconds: 10);
        Assert.DoesNotContain("sign-in", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10 seconds", body, StringComparison.Ordinal);
    }

    // ── TODO:5492 — /Error did not exist ────────────────────────────────────────

    [Fact]
    public void The_error_page_announces_and_offers_a_way_back()
    {
        string body = ErrorPage.Render("00-abc-123", "/terminal/");

        Assert.Contains("role=\"alert\"", body, StringComparison.Ordinal);
        Assert.Contains("00-abc-123", body, StringComparison.Ordinal);
        Assert.Contains("href=\"/terminal/\"", body, StringComparison.Ordinal);
        Assert.StartsWith("<!doctype html>", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_error_page_encodes_a_hostile_trace_identifier()
    {
        // TraceIdentifier is not guaranteed markup-safe and is echoed verbatim.
        string body = ErrorPage.Render("<script>alert(1)</script>", "/");
        Assert.DoesNotContain("<script>", body, StringComparison.Ordinal);
    }

    // ── TODO:5476 — every audit event landed in users/anon ──────────────────────

    /// <summary>
    /// <c>ICurrentUser.Set</c> is called in exactly two places, and neither is a Razor Page
    /// request — which is where every authentication audit event is written from. So
    /// <c>DataKey</c> was "anon" for all of them and every user's sign-ins, lockouts, email
    /// addresses and client IPs pooled into one shared directory.
    /// </summary>
    [Fact]
    public void CurrentUser_resolves_a_razor_page_request_from_its_principal()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = Principal("11111111-2222-3333-4444-555555555555"),
            },
        };

        var user = new CurrentUser(accessor);

        Assert.True(user.IsAuthenticated);
        Assert.Equal("11111111-2222-3333-4444-555555555555", user.UserId);
        Assert.Equal("11111111-2222-3333-4444-555555555555", user.DataKey);
    }

    /// <summary>
    /// An explicit <c>Set</c> — including <c>Set(null)</c> for an anonymous circuit — must
    /// beat the fallback. A circuit's identity is fixed when the circuit opens and must not
    /// start tracking whatever HTTP request happens to touch the same scope afterwards.
    /// </summary>
    [Fact]
    public void An_explicit_Set_beats_the_HttpContext_fallback()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = Principal("http-principal-id") },
        };

        var circuit = new CurrentUser(accessor);
        circuit.Set("circuit-id");
        Assert.Equal("circuit-id", circuit.DataKey);

        var anonymousCircuit = new CurrentUser(accessor);
        anonymousCircuit.Set(null);
        Assert.False(anonymousCircuit.IsAuthenticated);
        Assert.Equal("anon", anonymousCircuit.DataKey);
    }

    [Fact]
    public void CurrentUser_without_an_accessor_is_anonymous_as_before()
    {
        // The MAUI head and the non-accounts WebHost modes register no accessor; the old
        // behaviour must be exactly preserved there.
        var user = new CurrentUser();
        Assert.False(user.IsAuthenticated);
        Assert.Equal("anon", user.DataKey);
    }

    private static System.Security.Claims.ClaimsPrincipal Principal(string userId)
        => new(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier, userId) },
            "TestAuth"));

    // ── TODO:5516 — /diag/journal could only ever return [] ─────────────────────

    [Fact]
    public void The_journal_mirror_keeps_owners_apart()
    {
        var mirror = new JournalMirror();
        mirror.Record("alice", Entry("alice speech"));
        mirror.Record("bob", Entry("bob speech"));

        Assert.Single(mirror.Snapshot("alice"));
        Assert.Equal("alice speech", mirror.Snapshot("alice")[0].Text);
        Assert.Single(mirror.Snapshot("bob"));
        Assert.Empty(mirror.Snapshot("carol"));
    }

    [Fact]
    public void The_journal_mirror_caps_entries_per_owner_and_owners_overall()
    {
        var mirror = new JournalMirror();

        for (int i = 0; i < JournalMirror.PerOwnerCapacity + 50; i++)
            mirror.Record("alice", Entry($"entry {i}"));

        var snapshot = mirror.Snapshot("alice");
        Assert.Equal(JournalMirror.PerOwnerCapacity, snapshot.Count);
        // Oldest evicted, newest kept — a diagnostic tail, not a head.
        Assert.Equal($"entry {JournalMirror.PerOwnerCapacity + 49}", snapshot[^1].Text);

        for (int i = 0; i < JournalMirror.MaxOwners + 10; i++)
            mirror.Record($"user-{i}", Entry("x"));

        Assert.True(mirror.OwnerCount <= JournalMirror.MaxOwners,
            $"the mirror holds {mirror.OwnerCount} owners; a long-lived hosted process must not "
            + "accumulate a ring buffer per user forever.");
    }

    private static AccessibleTrader.Core.Services.JournalEntry Entry(string text)
        => new(DateTime.UtcNow, AccessibleTrader.Core.Services.JournalEntryKind.Speech, "TTS", "BTCUSD", text);

    // ── TODO:5553 — /push/subscribe aimed the server anywhere https ─────────────

    /// <summary>
    /// <c>Add</c> validated only that the endpoint began with <c>https://</c>. Any
    /// signed-in user could point the server's push sender at loopback, the private
    /// network, or the cloud metadata service — once per fired alert.
    /// </summary>
    [Theory]
    [InlineData("https://169.254.169.254/latest/meta-data/")]   // every cloud's metadata service
    [InlineData("https://10.0.0.5:6379/")]                      // private network
    [InlineData("https://127.0.0.1/")]                          // loopback
    [InlineData("https://[::1]/")]                              // loopback, v6
    [InlineData("https://[::ffff:10.0.0.1]/")]                  // private v4 smuggled through v6
    [InlineData("http://push.example/insecure")]                // not https at all
    public void A_push_endpoint_outside_the_public_internet_is_refused(string endpoint)
    {
        Assert.False(PushSubscriptionStore.IsPlausiblePushEndpoint(endpoint));
    }

    /// <summary>
    /// The vacuity control: the real push services must still be accepted, or the feature
    /// is simply off and every assertion above passes for the wrong reason.
    /// </summary>
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc123")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/gAAA")]
    [InlineData("https://wns2-by3p.notify.windows.com/w/?token=x")]
    [InlineData("https://8.8.8.8/")]                            // a public IP literal is fine
    public void Real_push_service_endpoints_are_accepted(string endpoint)
    {
        Assert.True(PushSubscriptionStore.IsPlausiblePushEndpoint(endpoint));
    }

    [Fact]
    public void The_store_refuses_a_private_endpoint_end_to_end()
    {
        string root = TestTemp.NewDir("att-push-guard-");
        try
        {
            var store = new PushSubscriptionStore(root,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PushSubscriptionStore>.Instance);

            bool added = store.Add("user-1", new StoredPushSubscription
            {
                Endpoint = "https://169.254.169.254/latest/meta-data/",
                P256dh = "key",
                Auth = "secret",
            });

            Assert.False(added);
            Assert.Empty(store.List("user-1"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>
    /// <c>PathFor</c> concatenated the user key into a path raw while
    /// <c>UserScopedPathService.Sanitize</c> stripped the same value. Not exploitable today
    /// (the key is the Identity GUID off the auth cookie) but an invariant that holds in one
    /// place and not its sibling is a bug waiting for its first caller.
    /// </summary>
    [Fact]
    public void The_store_sanitises_the_user_key_before_it_becomes_a_path()
    {
        string root = TestTemp.NewDir("att-push-path-");
        try
        {
            var store = new PushSubscriptionStore(root,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PushSubscriptionStore>.Instance);

            Assert.True(store.Add("../../escape", new StoredPushSubscription
            {
                Endpoint = "https://fcm.googleapis.com/fcm/send/abc",
                P256dh = "key",
                Auth = "secret",
            }));

            // Everything written stays inside the users root, whatever the key looked like.
            var written = Directory.GetFiles(root, "push_subscriptions.json", SearchOption.AllDirectories);
            Assert.Single(written);
            Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(written[0]), StringComparison.Ordinal);
            Assert.DoesNotContain("..", written[0].Substring(Path.GetFullPath(root).Length), StringComparison.Ordinal);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ── TODO:5526 — the VAPID private key was plaintext JSON ────────────────────

    /// <summary>
    /// It sat in plaintext beside the DataProtection-encrypted <c>secrets/</c> store, with
    /// the provider already in the container. Anyone who could read the data root could then
    /// send push notifications the browser attributes to this origin — an alert-shaped
    /// phishing surface aimed at the users who most depend on alerts.
    /// </summary>
    [Fact]
    public void The_vapid_private_key_is_not_written_in_the_clear()
    {
        string root = TestTemp.NewDir("att-vapid-");
        try
        {
            var service = new VapidKeyService(root, NullLogger<VapidKeyService>(),
                new EphemeralDataProtectionProvider());

            string privateKey = service.PrivateKey;
            Assert.False(string.IsNullOrWhiteSpace(privateKey));

            string onDisk = File.ReadAllText(Path.Combine(root, "vapid-keys.json"));
            Assert.DoesNotContain(privateKey, onDisk, StringComparison.Ordinal);
            Assert.Contains("privateKeyProtected", onDisk, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void The_vapid_key_file_is_owner_only()
    {
        if (OperatingSystem.IsWindows()) return;   // ACL-governed there

        string root = TestTemp.NewDir("att-vapid-mode-");
        try
        {
            var service = new VapidKeyService(root, NullLogger<VapidKeyService>(),
                new EphemeralDataProtectionProvider());
            _ = service.PublicKey;

            var mode = File.GetUnixFileMode(Path.Combine(root, "vapid-keys.json"));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>
    /// A key file written by an older build must still work. Regenerating instead would
    /// orphan every browser subscription in the wild, which is the one failure this class
    /// exists to avoid — so the upgrade path is read-then-rewrite, not discard.
    /// </summary>
    [Fact]
    public void A_legacy_plaintext_key_file_is_honoured_and_upgraded()
    {
        string root = TestTemp.NewDir("att-vapid-legacy-");
        try
        {
            var (pub, priv) = ("legacy-public-key", "legacy-private-key");
            File.WriteAllText(Path.Combine(root, "vapid-keys.json"),
                $"{{\"PublicKey\":\"{pub}\",\"PrivateKey\":\"{priv}\"}}");

            var protection = new EphemeralDataProtectionProvider();
            var service = new VapidKeyService(root, NullLogger<VapidKeyService>(), protection);

            Assert.Equal(pub, service.PublicKey);
            Assert.Equal(priv, service.PrivateKey);   // subscriptions survive

            string onDisk = File.ReadAllText(Path.Combine(root, "vapid-keys.json"));
            Assert.DoesNotContain(priv, onDisk, StringComparison.Ordinal);

            // ...and the protected form round-trips on the next boot with the SAME provider.
            var reloaded = new VapidKeyService(root, NullLogger<VapidKeyService>(), protection);
            Assert.Equal(priv, reloaded.PrivateKey);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static Microsoft.Extensions.Logging.ILogger<VapidKeyService> NullLogger<T>()
        => Microsoft.Extensions.Logging.Abstractions.NullLogger<VapidKeyService>.Instance;

    // ── TODO:5542 — the DataProtection key ring ─────────────────────────────────

    /// <summary>
    /// <c>dp-keys</c> holds plaintext XML master keys for the auth cookie, the antiforgery
    /// token and every encrypted secret on the instance, so read access to that directory is
    /// read access to every session. <c>SERVER_SETUP.md</c> documented <c>chmod -R 700</c>,
    /// but documentation is not a control and the matching <c>UMask=0077</c> drop-in is still
    /// open — under a default 022 umask the directory is created world-readable.
    /// </summary>
    [Fact]
    public void The_key_ring_directory_is_tightened_to_owner_only()
    {
        if (OperatingSystem.IsWindows()) return;

        string root = TestTemp.NewDir("att-keyring-");
        string keyRing = Path.Combine(root, "dp-keys");
        try
        {
            Directory.CreateDirectory(keyRing);
            File.SetUnixFileMode(keyRing,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);   // the 0755 a default umask gives

            KeyRingPolicy.EnsurePrivate(keyRing);

            var mode = File.GetUnixFileMode(keyRing);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                mode);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void The_key_ring_directory_is_created_when_missing()
    {
        string root = TestTemp.NewDir("att-keyring-new-");
        string keyRing = Path.Combine(root, "dp-keys");
        try
        {
            KeyRingPolicy.EnsurePrivate(keyRing);
            Assert.True(Directory.Exists(keyRing));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>
    /// A blob that exists and will not decrypt is an incident, not "no value configured".
    /// The catch swallowed everything, so a lost or replaced key ring presented as an
    /// instance that had quietly forgotten its market-data key, its Schwab refresh token and
    /// its VAPID keypair, with nothing anywhere saying why.
    /// </summary>
    [Fact]
    public async Task An_undecryptable_secret_is_logged_at_error()
    {
        string root = TestTemp.NewDir("att-secure-store-");
        try
        {
            var paths = new AccessibleTrader.WebHost.Services.WebHostPathService(root);
            var recorder = new RecordingLogger<AccessibleTrader.WebHost.Services.WebHostSecureStorageService>();

            // Write with one key ring...
            var writer = new AccessibleTrader.WebHost.Services.WebHostSecureStorageService(
                new EphemeralDataProtectionProvider(), paths);
            await writer.SetAsync("market-data", "a-real-secret");

            // ...read with a DIFFERENT one, which is exactly what a lost dp-keys looks like.
            var reader = new AccessibleTrader.WebHost.Services.WebHostSecureStorageService(
                new EphemeralDataProtectionProvider(), paths, recorder);

            Assert.Null(await reader.GetAsync("market-data"));   // still safe for the caller
            Assert.Contains(recorder.Entries, e =>
                e.Level == Microsoft.Extensions.Logging.LogLevel.Error
                && e.Message.Contains("could not be decrypted", StringComparison.Ordinal));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>
    /// The vacuity control: a key that was never set must stay quiet. "Not configured" is
    /// the ordinary case and must not fill the log with errors.
    /// </summary>
    [Fact]
    public async Task A_missing_secret_is_not_an_error()
    {
        string root = TestTemp.NewDir("att-secure-store-quiet-");
        try
        {
            var recorder = new RecordingLogger<AccessibleTrader.WebHost.Services.WebHostSecureStorageService>();
            var store = new AccessibleTrader.WebHost.Services.WebHostSecureStorageService(
                new EphemeralDataProtectionProvider(),
                new AccessibleTrader.WebHost.Services.WebHostPathService(root),
                recorder);

            Assert.Null(await store.GetAsync("never-set"));
            Assert.DoesNotContain(recorder.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public readonly List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries = new();

        IDisposable? Microsoft.Extensions.Logging.ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    // ── TODO:5535 — --reset-link printed a live token to the journal ────────────

    /// <summary>
    /// A scan guard, because the CLI path exits the process before Kestrel starts and cannot
    /// be driven from a test host. It checks the PATH, not just the presence of the fix: the
    /// token must not reach stdout at all, and the replacement must actually be there.
    ///
    /// <para>
    /// Comment lines are excluded, which is not a nicety — the fix's own comment names the
    /// call it replaced, and a naive scan flags the documentation of the fix as the bug.
    /// (It did, on this test's first run.)
    /// </para>
    /// </summary>
    [Fact]
    public void The_reset_link_cli_prints_a_path_and_never_the_token()
    {
        string program = Path.Combine(RepoRoot(), "AccessibleTrader.WebHost", "Program.cs");
        var code = File.ReadAllLines(program)
                       .Where(l =>
                       {
                           var t = l.TrimStart();
                           return !t.StartsWith("//", StringComparison.Ordinal)
                               && !t.StartsWith("*", StringComparison.Ordinal)
                               && !t.StartsWith("/*", StringComparison.Ordinal);
                       })
                       .ToList();
        string source = string.Join("\n", code);

        Assert.Contains("Reset link written to: ", source, StringComparison.Ordinal);

        // The exact old line. Under systemctl, stdout IS the journal, so this persisted a
        // live one-day reset token — a full password change with no second factor — to disk
        // for anyone in systemd-journal, indefinitely.
        Assert.DoesNotContain("Console.WriteLine(resetUrl", source, StringComparison.Ordinal);

        // ...and it is written owner-only rather than merely moved to a file.
        int write = source.IndexOf("File.WriteAllText(resetFile", StringComparison.Ordinal);
        int chmod = source.IndexOf("File.SetUnixFileMode(resetFile", StringComparison.Ordinal);
        Assert.True(write > 0 && chmod > write,
            "the reset link must be written to a file AND restricted to its owner afterwards");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
