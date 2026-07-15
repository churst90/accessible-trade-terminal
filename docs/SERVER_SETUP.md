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

- **`-p:ServerPublish=true` is required.** Without it, Release defaults to `OutputType=WinExe`,
  which drops `blazor.web.js` from the published static-asset manifest → the Blazor circuit
  never boots ("no data loaded").
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
| `ACCOUNTS_SEED_EMAIL` / `ACCOUNTS_SEED_PASSWORD` | hosted (optional) | provision one owner/admin account at startup, bypassing the public password policy. Idempotent. |
| `XDG_DATA_HOME` / `XDG_CACHE_HOME` | optional | isolate a staging instance's state from a live one |
| `ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS` | desktop/local only | `1` opts into running custom scripts when the OS sandbox primitive is missing (bwrap / sandbox-exec / AppContainer). Default: script execution is **refused** without the sandbox. Every launch under the override is recorded to the security event log. Never set this on a server. |

Bitstamp crypto needs **no** key. Real broker keys are never held server-side (hosted is
paper-only; the API-keys modal is gated off).

### Market data available (hosted + demo)

Because no user broker keys are held server-side, the server-keyed builds (`--accounts`
**and** `--demo`) curate the provider/market lists down to the sources that actually work
without a user key, instead of showing dead-end "API key required" entries:

- **Crypto → Bitstamp** (live WebSocket, no key, hundreds of pairs)
- **Stock / Forex → Twelve Data** (seeded key). Its free-tier symbol-*list* endpoints are
  unusable (`/stocks` is empty, `/forex_pairs` returns 1000+ obscure pairs), so a curated
  starter list of majors is shown; **symbol search still charts any other valid ticker.**

Demo additionally clamps symbols/timeframes/indicators to a tight whitelist; **Hosted keeps
the full timeframe + indicator suite and free symbol search** — only the data *sources* are
curated. To offer more (extra crypto venues, indices, commodities), seed additional
server-side market-data keys and extend `ProviderForMarket` + the curated lists in
`DemoPolicy` / `MarketOrchestrator`.

## Data layout (hosted)

Under `Accounts__DataRoot`:
```
auth.db          Identity accounts (AspNetUsers, …)
users/{userId}/  per-user data — settings, workspaces, sound design, paper-trading, journal
cache/           SHARED OHLCV / HTTP cache (public market data, one for everyone)
dp-keys/         DataProtection key ring (auth cookies + antiforgery; persisted so restarts
                 don't log everyone out)
secrets/         encrypted process-wide market-data secrets (e.g. the Twelve Data key),
                 pinned under the data root so the instance is self-contained
```
`users/anon/` may appear empty in unauthenticated contexts — harmless (the app gates with
`RequireAuthorization`, so anonymous requests can't persist anything).

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
- Response security headers are set by the app on every response (`SecurityHeadersPolicy`,
  added 2026-07): CSP (`script-src 'self'`, `frame-ancestors 'none'`),
  `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`,
  `Permissions-Policy`, and HSTS on HTTPS requests (accurate behind nginx because
  `X-Forwarded-Proto` is honoured). Don't set conflicting duplicates in nginx.
- `dp-keys/` persisted and **backed up** (losing it invalidates all sessions/antiforgery
  and orphans the encrypted secret store). Restrict it to the service user:
  `chmod -R 700` on the directory, owned by the service account, no other readers.
- Back up `auth.db` + `users/` + `dp-keys/` (single instance = single disk).

## Password reset (admin-mediated — no mail server)

Users who forget their password use **Forgot password** on the sign-in page, which
never confirms whether an address exists; it tells them to contact support and logs
an `AuthPasswordResetRequested` security event (with IP) so you can see the request.
You then mint a reset link out of band:

```
dotnet AccessibleTrader.WebHost.dll --accounts --reset-link user@example.com
```

This prints a one-time reset URL (Identity token, default 1-day expiry) WITHOUT
starting the server; deliver it to the user through a trusted channel. The user sets
their own new password on the ResetPassword page (the admin never sees it); success
is audited as `AuthPasswordReset`. Unknown emails produce the same generic CLI output
(no enumeration even at the console).
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
- The shared market-data key currently writes to the default app-data path, not the data
  root (tidy-up item).

See also: `HOSTED_AUTH_PERSISTENCE_DESIGN.md`, `HOSTED_ACCOUNTS_STRATEGY.md`,
`WEBHOST_MULTI_USER_SCOPING.md`, and `RELEASING.md`.
