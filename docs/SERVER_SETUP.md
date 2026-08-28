# Server setup & deployment (WebHost)

How to build, configure, and run the `AccessibleTrader.WebHost` on a Linux server. The
desktop/MAUI heads are not covered here — this is the browser/server side only.

## The three run modes

The same binary serves three modes, selected by flags/config:

| Mode | Flag | Who | Path base | Auth | Persistence |
|---|---|---|---|---|---|
| **Local** | (none) | one user on their own machine | `/` | none | local single dir |
| **Demo** | `--demo` | anonymous public taste | `/app/` | none | none (ephemeral) |
| **Hosted** | `--accounts` | logged-in paper-trading users | `/terminal/` | Identity login | per-user under the data root |

`--demo` and `--accounts` are mutually exclusive deployments (run two services, or one).
Outside both flags it's the plain single-user local app — unchanged.

**Local mode only:** the plain (no-flag) local app also registers a **desktop
system-tray applet** and background-alert monitoring so the server is usable with
the browser closed (see the User Manual). Both are gated to `HostMode.Full` and
are **never** active under `--demo` or `--accounts` — a hosted/demo server has no
local user at the box and stays headless.

## Prerequisites

- **.NET 10 runtime** (or publish self-contained, below — then no runtime needed).
- **`bubblewrap`** (`bwrap`) if you allow custom user scripts — since 2026-07 script
  execution is **refused** when bwrap is missing (it no longer silently falls back to
  process-isolation-only). (Hosted mode disables custom scripts anyway, so this only
  matters for a local/full server.)
- **nginx** (TLS termination + reverse proxy) for any public deployment.
- Audio is **browser-side** (WebAudio) for remote users, so a headless server needs no
  audio stack. (PipeWire/PulseAudio matters only for a *local* Linux user running the
  WebHost on their own desktop, where Orca speaks over D-Bus.)

## Build / publish

Publish self-contained per-RID. **Two gotchas, both already handled by the project but
worth knowing:**

```bash
dotnet publish AccessibleTrader.WebHost/AccessibleTrader.WebHost.csproj \
    -c Release \
    --runtime linux-x64 --self-contained true \
    -p:ServerPublish=true \
    -p:PublishSingleFile=false -p:PublishTrimmed=false \
    -o /opt/accessible-trader/app
```

- **Keep `-p:ServerPublish=true`.** Under `OutputType=WinExe` the SDK drops `blazor.web.js` from
  the static-asset manifest → the Blazor circuit never boots ("no data loaded"). Since 2026-08-26
  the `WinExe` condition is also gated on `IsOSPlatform('Windows')`, so a Linux publish no longer
  depends on this flag to stay safe — but keep passing it: it is what the release workflow uses on
  every RID, and it keeps the command correct if you ever publish from Windows.
  `WebHostStaticAssetManifestTests` fails the build's manifest if the asset goes missing again.
- **`plugins_trusted.manifest` is generated into the publish output automatically** (the
  `GeneratePluginTrustManifestOnPublish` target hashes the published plugin DLLs). Without a
  matching manifest, `PluginTrustPolicy.RequireTrusted` refuses every plugin → no data.

Smoke test after publish:
```bash
curl -sf http://127.0.0.1:5150/terminal/_framework/blazor.web.js   # must be 200 (hosted)
```

## Environment variables

| Variable | Mode | Purpose |
|---|---|---|
| `Kestrel__Endpoints__Http__Url` | all | bind address, e.g. `http://127.0.0.1:5150` (behind nginx) |
| `ASPNETCORE_ENVIRONMENT` | all | `Production` for deploys |
| `Accounts__Enabled` | hosted | `true` (equivalent to `--accounts`) |
| `Accounts__DataRoot` | hosted | where per-user data + `auth.db` + `dp-keys` live, e.g. `/var/lib/accessible-trader-terminal` |
| `TWELVEDATA_APIKEY` | demo/hosted | server-side stock/forex market-data key (read-only; never a trading credential). Falls back to `DEMO_TWELVEDATA_APIKEY`. |
| `FRED_APIKEY` | demo/hosted | server-side FRED key — unlocks the Economic market (CPI, NFP, GDP, unemployment, fed funds, yields). Read-only public research data. Falls back to `FRED_API_KEY`. |
| `ACCOUNTS_SEED_EMAIL` / `ACCOUNTS_SEED_PASSWORD` | hosted (optional) | provision one owner/admin account at startup, bypassing the public password policy. Idempotent. |
| `XDG_DATA_HOME` / `XDG_CACHE_HOME` | optional | isolate a staging instance's state from a live one |
| `ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS` | desktop/local only | `1` opts into running custom scripts when the OS sandbox primitive is missing (bwrap / sandbox-exec / AppContainer). Default: script execution is **refused** without the sandbox. Every launch under the override is recorded to the security event log. Never set this on a server. |

Bitstamp crypto needs **no** key. Real broker keys are never held server-side (hosted is
paper-only; the API-keys modal is gated off).

### Market data available

Neither server-keyed build holds user broker keys, so both curate the provider/market lists
down to sources that work without one — but they curate to **different widths**, and that
distinction is the whole design.

**Demo (`--demo`, anonymous)** — a guided taste. One provider per market, no choice:

- **Crypto → Bitstamp** (live WebSocket, no key, hundreds of pairs)
- **Stock / Forex → Twelve Data** (seeded key)

Demo also clamps symbols, timeframes and indicators to a tight whitelist.

**Hosted (`--accounts`, logged in)** — the full app minus the desktop-only differentiators.
Every provider that needs no user key is offered, and the choice between them is the user's:

- **Crypto** — Bitstamp, Binance, Kraken, MEXC (all live WebSocket); Gemini and
  Kraken Futures historical-only
- **Stock / Forex / Index** — Twelve Data (seeded key). Its free-tier symbol-*list* endpoints
  are unusable (`/stocks` is empty, `/forex_pairs` returns 1000+ obscure pairs), so a curated
  starter list of majors is shown; **symbol search still charts any other valid ticker.**
- **Economic** — FRED (seeded key: CPI, NFP, GDP, unemployment, fed funds, yields), SEC EDGAR
- **OnChain** — CoinGecko, CoinMetrics, DefiLlama, Mempool, BGeometrics
- **Derivatives** — BinanceDerivatives, OkxDerivatives, Deribit, BinanceVision, CFTC, FINRA
- **Sentiment** — AlternativeMe, Wikipedia
- **MyData** — the user's own imported datasets, from their per-user directory

Hosted keeps the full timeframe + indicator suite and free symbol search.

The membership rule for hosted, kept in `DemoPolicy.HostedProviders`: a provider belongs there
if its public data needs **no credential at all**, or the server **seeds** its key at startup.
Anything else must stay out — hosted has the API-keys modal switched off, so a key-required
provider can only ever render as a dead-end "API key required". Live streaming is a separate,
narrower list (`HostedStreamingProviders`): only venues with a public WebSocket, because asking
a historical-only provider to stream just loops on reconnects.

To offer more, seed the key in the `seeds` table in `Program.cs` and add the provider to
`HostedProviders` (and `HostedStreamingProviders` if it streams).

### Analytics series cache

Economic / on-chain / derivatives / sentiment fetches are cached to `CacheDirectory` (shared
across hosted users — public data, one copy for everyone), keyed by
provider+market+symbol+timeframe+window. TTL is half the bar interval, clamped to 15 min … 12 h,
so a daily FRED series is fetched at most twice a day however many people chart it. Tradeable
markets are never cached: their last bar moves on every tick.

## Data layout (hosted)

Under `Accounts__DataRoot`:
```
auth.db          Identity accounts (AspNetUsers, …)
users/{userId}/  per-user data — settings, sound design, paper-trading, journal, and:
                   Workspaces/       saved layouts, alerts.json, __last-session__ autosave
                   IndicatorPrefs/   per-indicator colours, thickness, sonification
                   SecurityEvents/   this user's audit log
cache/           SHARED HTTP / analytics-series cache (public market data, one for everyone)
dp-keys/         DataProtection key ring (auth cookies + antiforgery; persisted so restarts
                 don't log everyone out). Owner-only 0700, asserted at startup.
secrets/         encrypted process-wide market-data secrets (e.g. the Twelve Data key),
                 pinned under the data root so the instance is self-contained
SecurityEvents/  INSTANCE-level audit log — sandbox fallbacks, plugin trust rejections,
                 OAuth token failures. Properties of the server, not of one account;
                 per-user auth events live in users/{userId}/SecurityEvents/ instead
vapid-keys.json  Web Push keypair. Public key plain, private key DataProtection-encrypted
                 (file mode 0600); losing it orphans every browser push subscription
reset-links/     0600 files holding admin-minted password-reset URLs, swept after 2 days
```
`users/anon/` may appear empty in unauthenticated contexts — harmless (the app gates with
`RequireAuthorization`, so anonymous requests can't persist anything).

> Until 2026-08-27 that was **not** harmless: every authentication audit event is written
> from a Razor Page, a Razor Page request is not a Blazor circuit, and `ICurrentUser` was
> only ever populated by the circuit handler — so all users' sign-ins, failures, lockouts,
> 2FA changes and password resets pooled into `users/anon/SecurityEvents/`, email
> addresses and client IPs included, in a directory whose name says it holds no user data
> (and which `HostedAlertMonitor` skips when pruning). If you have an `anon` directory
> with a `SecurityEvents/` folder in it, that is the old pooled log: it is PII, it is not
> attributable to any one account, and it can be deleted once you have read it.

> **Anything under `users/{id}/` is per-user by virtue of going through `IPlatformPathService`.**
> A service that builds its own path from `Environment.GetFolderPath(LocalApplicationData)`
> silently opts out of that and gets one shared directory for the whole server — which is how
> workspaces, indicator preferences and the security event log all came to be shared. Use
> `IPlatformPathService` for user state and `PlatformPaths` for machine-level paths; never
> `GetFolderPath` directly. See the warning under *Upgrading* for why the latter is unsafe on
> Unix even in the single-user case.

### Upgrading an instance that predates per-user workspaces

Workspaces, indicator preferences and security events used to live in ONE shared directory.
After upgrading they resolve under `users/{userId}/`, so existing state has to be moved or it
simply disappears from the UI — the files are still on disk, the app just no longer looks there.
Saved layouts, **`alerts.json`** (background alert monitoring goes quiet without it) and the
`__last-session__` autosave are the ones that matter. Move the old directory's contents into the
right account's folder rather than copying only the autosave:

```bash
systemctl stop accessible-trader-terminal
OLD=/var/lib/accessible-trader-terminal/xdg-data/AccessibleTrader
NEW=$OLD/users/<userId>
cp -rn "$OLD/Workspaces"/.      "$NEW/Workspaces/"      2>/dev/null || true
cp -rn "$OLD/IndicatorPrefs"/.  "$NEW/IndicatorPrefs/"  2>/dev/null || true
systemctl start accessible-trader-terminal
```

Leave the originals in place as a backup until the account confirms its layouts and alerts are
back. With more than one account there is no correct automatic answer — the shared directory has
no record of who wrote what, so pick the account it belonged to deliberately.

> **Check where the old state actually is before you start.** On Unix,
> `Environment.GetFolderPath(LocalApplicationData)` returns an **empty string** when the
> directory it resolves to does not exist — including when `XDG_DATA_HOME` names a directory
> nobody created. The resulting path is *relative* and resolves against the process's working
> directory, so state could be sitting in the deployment directory (which a redeploy replaces)
> rather than the state root the unit file names. Create the XDG directories in `ExecStartPre`.

### Historical OHLCV store

Closed bars are written to `trader_local.db` on every successful fetch and served back on
scrollback, so panning through history stops re-hitting the provider. Retention is the newest
50,000 bars per (market, provider, symbol, timeframe). The live edge is always fetched fresh.

Note the location: this database is **not** under `Accounts__DataRoot`. It resolves to
`$XDG_DATA_HOME/AccessibleTrader/trader_local.db` (default `~/.local/share/AccessibleTrader/`),
because the factory must stay a singleton and so cannot read the per-circuit path service. Its
contents are public market data, so sharing is harmless — but two services on one box **will**
write to the same file unless each gets its own `XDG_DATA_HOME`, which is the same isolation the
secret store needs. Set it per service.

The schema is created with EF's `EnsureCreated`, which is **create-or-nothing**: it will not
alter a database that already has tables. There is no migration path, so if `OhlcvEntity` ever
gains a column, delete `trader_local.db` on deploy and let it rebuild — it is a cache, and
nothing in it is authoritative. The store logs at **Error** when its table is unusable, which is
the signal to do that; ordinary misses log at Warning.

> **Co-located demo + terminal:** when `Accounts__DataRoot` is set, the shared secret store
> lives under it (`secrets/`). This matters if you run the `--demo` and `--accounts`
> services on the same box: without it, both resolve their secret store to the default
> `~/.local/share/AccessibleTrader` and clobber each other's encrypted market-data secret
> (last writer wins, because each persists DataProtection keys to a different ring). Pinning
> the secret store under each instance's own data root makes them independent. Setting
> `XDG_DATA_HOME`/`XDG_CACHE_HOME` per service achieves the same isolation for everything else.

## systemd unit (hosted example)

`/etc/systemd/system/accessible-trader-terminal.service`:
```ini
[Unit]
Description=Accessible Trader (hosted accounts terminal)
After=network.target

[Service]
User=debian
WorkingDirectory=/opt/accessible-trader/app
ExecStart=/opt/accessible-trader/app/AccessibleTrader.WebHost --accounts --no-launch
EnvironmentFile=-/etc/accessible-trader-terminal.env
Restart=on-failure

[Install]
WantedBy=multi-user.target
```
`/etc/accessible-trader-terminal.env`:
```ini
ASPNETCORE_ENVIRONMENT=Production
Kestrel__Endpoints__Http__Url=http://127.0.0.1:5150
Accounts__Enabled=true
Accounts__DataRoot=/var/lib/accessible-trader-terminal
TWELVEDATA_APIKEY=xxxxxxxx
# ACCOUNTS_SEED_EMAIL=owner@example.com
# ACCOUNTS_SEED_PASSWORD=...
```
The demo is a parallel service with `--demo`, its own port (e.g. 5146), and
`DEMO_TWELVEDATA_APIKEY`.

## nginx reverse proxy

TLS terminates at nginx; the app trusts `X-Forwarded-Proto`/`-For` (via
`UseForwardedHeaders`, enabled in accounts mode) so cookies are marked `Secure` and the
rate limiter sees real client IPs. Proxy the subpath to the app's loopback port, and pass
the WebSocket upgrade for the `/_blazor` SignalR circuit:

```nginx
# in the trade.codyhurst.com server { } block
location /terminal/ {
    proxy_pass http://127.0.0.1:5150;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection $connection_upgrade;   # map: ""→"", "websocket"→"upgrade"
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    limit_conn att_terminal 10;                        # per-IP connection cap
}
```
(`/app/` for the demo is the same shape on the demo's port.) The app *also* runs an
in-process per-IP fixed-window rate limiter in accounts mode, so nginx `limit_conn` and
the app limiter are belt-and-braces. Since 2026-07 the app limiter has two tiers per
client IP (`AuthRateLimitPolicy`): 200 req / 10 s for general traffic, and a strict
10 attempts / 5 min for POSTs to `/account/login` and `/account/register`, so one IP
cannot brute-force credentials at page-load rates. Identity's per-account lockout is
the second layer behind it.

## Security checklist (hosted)

- HTTPS only at nginx; `Secure` + `HttpOnly` + `SameSite=Lax` auth cookie (14-day sliding),
  named `__Host-att.auth` since 2026-07 — the `__Host-` prefix pins it to the exact host
  over HTTPS (Path=/, no Domain) and the neutral name drops the ASP.NET Identity
  fingerprint. NOTE: deploying this rename signs every existing session out once.
  `SameSite=Lax` also blocks cross-site WebSocket hijacking of the Blazor circuit —
  browsers do not attach Lax cookies to cross-site WebSocket handshakes.
- Identity lockout: 10 failed attempts → 15-minute cool-off, enforced for new accounts.
  Sign-in shows the same generic message whether the password was wrong or the account
  is locked (no enumeration oracle); the real reason is in the security event log,
  which since 2026-07 records login success/failure/lockout and registration with the
  real client IP (via `X-Forwarded-For`). Registration has a screen-reader-safe
  honeypot and returns a generic message on duplicate email.
- Optional TOTP two-factor authentication (2026-07): any user can enroll an
  authenticator app at `/account/security` → "Set up two-factor authentication".
  Enrollment is accessible-first (copyable setup key grouped in fours; the QR code
  is the phone-scan convenience), issues ten single-use recovery codes shown once,
  and every 2FA event (enable, disable, challenge success/failure, recovery-code
  use, code regeneration) lands in the security event log with the client IP.
  Disabling 2FA or regenerating codes requires the CURRENT password (a hijacked
  session can't quietly strip the second factor), and disabling resets the
  authenticator key so re-enrollment always mints a fresh secret. Failed codes
  count toward the same 10-attempt lockout as passwords. No new infrastructure —
  Identity's default token providers, same auth.db.
- Response security headers are set by the app on every response (`SecurityHeadersPolicy`,
  added 2026-07): CSP (`script-src 'self'`, `frame-ancestors 'none'`),
  `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`,
  `Permissions-Policy`, and HSTS on HTTPS requests (accurate behind nginx because
  `X-Forwarded-Proto` is honoured). Don't set conflicting duplicates in nginx.
- `dp-keys/` persisted and **backed up** (losing it invalidates all sessions/antiforgery
  and orphans the encrypted secret store). Restrict it to the service user:
  `chmod -R 700` on the directory, owned by the service account, no other readers.
  Since 2026-08-27 the app also asserts this at startup — it tightens `dp-keys/` to
  `0700` itself and **refuses to start** if the directory is still readable or writable
  beyond its owner. The keys are stored unencrypted, so read access to that directory is
  read access to every session; a documented `chmod` is not a control, and the matching
  `UMask=0077` unit drop-in is still an open item.
- Back up `auth.db` + `users/` + `dp-keys/` (single instance = single disk).

## Password reset (admin-mediated — no mail server)

Users who forget their password use **Forgot password** on the sign-in page, which
never confirms whether an address exists; it tells them to contact support and logs
an `AuthPasswordResetRequested` security event (with IP) so you can see the request.
You then mint a reset link out of band:

```
dotnet AccessibleTrader.WebHost.dll --accounts --reset-link user@example.com
```

This mints a one-time reset URL (Identity token, default 1-day expiry) WITHOUT starting
the server, writes it to an owner-only (`0600`) file under `reset-links/` in the data
root, and prints **the path** — not the token. Read the file, deliver the link to the
user through a trusted channel, then delete it; links older than two days are swept on
the next run. The user sets their own new password on the ResetPassword page (the admin
never sees it); success is audited as `AuthPasswordReset`. Unknown emails produce the
same generic CLI output (no enumeration even at the console).

> The command used to print the URL to stdout. Under `systemctl` that is the journal,
> so a live password-reset token — a full password change with no second-factor
> challenge — was persisted to disk, readable by anyone in `systemd-journal`,
> indefinitely, long after the reset had been used. If you ran an older build this way,
> scrub the journal (`journalctl --vacuum-time=…`) and rotate any link that was minted.
- Real-money trading and broker keys are **desktop-only** — never on the server.
- Custom user scripts are **off** in hosted mode (server-side Roslyn = RCE risk). Anywhere
  scripts ARE enabled (local WebHost / desktop Linux), `bubblewrap` must be installed —
  without it, script execution is refused rather than silently unsandboxed.
- Bind Kestrel to loopback (`127.0.0.1`); nginx is the only public entry.

## Known limitations (current)

- **No transactional email** → no email confirmation or self-service password reset
  (`RequireConfirmedAccount=false`). Add SMTP before scaling. Anyone can register any email.
- **Per-user OHLCV cache** (first cut); a shared symbol-keyed connection pool is the
  documented efficiency optimisation (see `HOSTED_ACCOUNTS_STRATEGY.md`).

See also: `HOSTED_AUTH_PERSISTENCE_DESIGN.md`, `HOSTED_ACCOUNTS_STRATEGY.md`,
`WEBHOST_MULTI_USER_SCOPING.md`, and `RELEASING.md`.
