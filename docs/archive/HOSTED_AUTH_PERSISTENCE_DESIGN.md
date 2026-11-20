# Hosted auth & per-user persistence — implementation guide

A detailed engineering guide for adding accounts and per-user persistence to the **WebHost
only** (the single, multi-user server instance). The MAUI/desktop clients are **not**
touched — they stay single-user and store settings locally exactly as they do today (this
guide explains why that needs zero new work). Builds on `WEBHOST_MULTI_USER_SCOPING.md`
(per-circuit scoping) and `HOSTED_ACCOUNTS_STRATEGY.md` (the paper-only, education-first
product direction).

---

## 1. Scope & principles

- **Server-side only, single instance, multi-user.** One WebHost process serves many
  authenticated users; per-user data lives on that instance's disk + a DB.
- **Paper-only hosted.** No real broker keys are held server-side (the strategy doc's whole
  point). So the secrets problem is minimal — accounts mostly hold *preferences and
  paper-trading state*, not funds-moving credentials.
- **Clients are local and untouched.** The desktop is one user per device; it already
  persists locally. No auth, no per-user routing there.
- **Route, don't rewrite** (the central idea — §2).
- **Accessible auth is non-negotiable** (§12) — the login flow must be fully usable by a
  screen reader, or the whole audience is locked out at the door.

Non-goals (now): horizontal scaling / multiple server instances; real-money trading on the
server; desktop ↔ cloud sync; social/OAuth login (all possible later, noted where relevant).

## 2. The central insight: route, don't rewrite

Almost every per-user thing the app persists already goes through **one chokepoint**:
`IPlatformPathService.AppDataDirectory`. `SettingsManager` writes `settings.json` there,
the workspace library writes workspaces there, the sound-patch library, the paper-trading
provider, indicator preferences — all under `AppDataDirectory`.

So the bulk of the work is **not** rewriting those services. It is:

1. Add **authentication** so each circuit knows *who* the user is.
2. Replace `WebHostPathService` (a fixed directory) with a **`UserScopedPathService`** that
   returns `…/users/{userId}/` for the current circuit's user.

Do that and `SettingsManager`, the workspace library, sound design, paper trading, etc.
become per-user **for free**, because they already read/write `AppDataDirectory`.

Crucially, `IPlatformPathService` also exposes **`CacheDirectory`** (the OHLCV / HTTP
cache). That stays **shared** across all users — it's public market data, so a shared cache
is exactly the pooling win from the BYOA analysis. So: `AppDataDirectory` → per-user;
`CacheDirectory` → shared.

```
{dataRoot}/                         (server install data root)
  cache/                            CacheDirectory — SHARED (public OHLCV, HTTP)
  users/
    {userId-A}/                     AppDataDirectory for user A
      settings.json
      workspaces/
      sound-patches/
      paper-trading.json
      journal.jsonl
    {userId-B}/                     …isolated per user
  auth.db                          Identity (accounts) — see §4
```

## 3. Architecture & data flow

```
Browser ──HTTP──> Identity login/register page (Razor/SSR) ──sets auth cookie──┐
   │                                                                            │
   └──SignalR circuit (interactive)──> AuthenticationStateProvider <── cookie ──┘
                                              │ ClaimsPrincipal (userId)
                                              ▼
                            CircuitHandler captures userId → ICurrentUser (scoped)
                                              │
                                              ▼
                            UserScopedPathService.AppDataDirectory = users/{userId}/
                                              │
        ┌─────────────────────────────────────┼───────────────────────────────┐
        ▼                 ▼                     ▼                ▼               ▼
   SettingsManager   WorkspaceLibrary    SoundPatchLibrary  PaperTrading    Journal
   (per-user, no code change — they already use AppDataDirectory)        (needs persist, §8)
```

Auth cookies must be set during an **HTTP request**, not over the SignalR circuit — so
login/register/logout are HTTP endpoints (Razor Pages or non-interactive form posts). The
interactive circuit only *reads* the resulting identity.

## 4. Identity layer (ASP.NET Core Identity + EF Core)

Use **ASP.NET Core Identity** — it gives password hashing (PBKDF2), email confirmation,
lockout, and reset out of the box, and it's self-contained (no third-party dependency).

- **User model:** a custom `AppUser : IdentityUser` so we can hang account fields off it:

  ```csharp
  public class AppUser : IdentityUser
  {
      public DateTime CreatedUtc { get; set; }
      public string Tier { get; set; } = "free";   // free | supporter | … (gates premium indicators, §HOSTED_ACCOUNTS_STRATEGY)
      public DateTime? LastSeenUtc { get; set; }
  }
  ```

- **DB:** keep it simple for a single instance — **SQLite** (the codebase already uses it
  via `AppDbContext`). Put Identity in its **own** context + file (`auth.db`) so it backs up
  and migrates independently of the OHLCV cache DB:

  ```csharp
  public class AuthDbContext : IdentityDbContext<AppUser>
  {
      public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) {}
  }
  ```

  Enable WAL mode (`PRAGMA journal_mode=WAL`) for better read/write concurrency under many
  circuits. **If concurrency ever outgrows SQLite, swap the provider for Postgres** — one
  line, since it's all EF Core. (Out of scope now.)

- **DI registration (WebHost `ServiceCollectionExtensions` / `Program.cs`):**

  ```csharp
  builder.Services.AddDbContext<AuthDbContext>(o =>
      o.UseSqlite($"Data Source={Path.Combine(dataRoot, "auth.db")}"));

  builder.Services.AddIdentityCore<AppUser>(o =>
      {
          o.Password.RequiredLength = 10;          // accessible-friendly: length over symbol soup
          o.User.RequireUniqueEmail = true;
          o.SignIn.RequireConfirmedEmail = false;   // turn on once email sending is wired
          o.Lockout.MaxFailedAccessAttempts = 10;   // brute-force guard
      })
      .AddEntityFrameworkStores<AuthDbContext>()
      .AddSignInManager()
      .AddDefaultTokenProviders();

  builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
      .AddIdentityCookies();
  builder.Services.AddAuthorization();
  builder.Services.AddCascadingAuthenticationState();
  ```

- **Login/register/logout UI:** scaffold the Identity Razor Pages **or** use the .NET Blazor
  Identity components, rendered as static SSR (form posts), not interactive. Either way they
  set the cookie via HTTP. Audit them for accessibility (§12).

- **Pipeline (`Program.cs`):** add `app.UseAuthentication(); app.UseAuthorization();` (after
  `UseRouting`/before `MapRazorComponents`), and map the Identity endpoints.

- **Protect the app:** require auth for the trading UI, but keep the **public demo route
  anonymous**:

  ```razor
  @* Routes.razor *@
  <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(MainLayout)">
      <NotAuthorized>
          @* redirect anonymous users to /login (except the demo route) *@
      </NotAuthorized>
  </AuthorizeRouteView>
  ```

## 5. Current-user plumbing into the per-circuit scope

A small **scoped** holder, populated once per circuit:

```csharp
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }     // the stable Identity user id (a GUID string)
    string DataKey { get; }     // safe folder name: UserId, or "anon-{circuitId}" for demo
}

public sealed class CurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; private set; }
    public string? UserId { get; private set; }
    public string DataKey { get; private set; } = "anon";
    internal void Set(string? userId)            // called once by the circuit handler
    {
        UserId = userId;
        IsAuthenticated = !string.IsNullOrEmpty(userId);
        DataKey = IsAuthenticated ? userId! : "anon";
    }
}
```

Populate it from the authenticated principal as the circuit opens (extend the existing
`WebHostBrowserCircuitHandler`, which already does per-circuit setup):

```csharp
public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct)
{
    var state = await _authStateProvider.GetAuthenticationStateAsync();
    var id = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    _currentUser.Set(id);                 // _currentUser is the scoped CurrentUser
    WebHostShortcutRemap.ApplyBrowserHostOverrides(_shortcuts, _logger);  // existing work
}
```

> **Why `NameIdentifier` (the GUID), never the email or a user-supplied name:** the value
> becomes a directory name — using the immutable GUID prevents path-traversal and rename
> headaches. Validate/whitelist it to `[A-Za-z0-9-]` anyway (defence in depth).

## 6. `UserScopedPathService` (the keystone)

Replace `WebHostPathService` with one that routes `AppDataDirectory` per-user but keeps
`CacheDirectory` shared. Read `ICurrentUser` **lazily** (on property access), not in the
constructor — so it resolves *after* the circuit handler has set the user:

```csharp
public sealed class UserScopedPathService : IPlatformPathService
{
    private readonly ICurrentUser _user;
    private readonly string _dataRoot;     // server install data root
    private readonly string _sharedCache;  // {dataRoot}/cache — SHARED

    public UserScopedPathService(ICurrentUser user, IConfiguration cfg)
    {
        _user = user;
        _dataRoot = cfg["AccessibleTrader:DataRoot"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessibleTrader");
        _sharedCache = EnsureDir(Path.Combine(_dataRoot, "cache"));
    }

    public string AppDataDirectory => EnsureDir(
        Path.Combine(_dataRoot, "users", Sanitize(_user.DataKey)));   // per-user, lazy

    public string CacheDirectory => _sharedCache;                     // shared (public data)

    private static string Sanitize(string s) =>
        new string(s.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
    private static string EnsureDir(string p) { Directory.CreateDirectory(p); return p; }
}
```

Register it **scoped** in the WebHost (replacing the `WebHostPathService` registration);
`ICurrentUser` scoped; keep `AppDbContext`'s cache factory pointed at the shared cache.

## 7. What becomes per-user — for free vs. needs work

| Data | Current storage | Change needed |
|---|---|---|
| Settings (`settings.json`) | `AppDataDirectory` | **None** — per-user via the path swap |
| Workspaces (layouts) | workspace library files under `AppDataDirectory` | **None** |
| Sound design (waveforms, bell patches, per-indicator audio) | sound-patch library / indicator prefs under `AppDataDirectory` | **None** |
| Paper-trading record (positions/fills/P&L) | `PaperTradingProvider` JSON under `AppDataDirectory` | **None** |
| Alerts (definitions, delivery config) | settings/alert store under `AppDataDirectory` | **None** (verify the path) |
| **Journal** (speech/alerts/setups log) | in-memory ring buffer (`IJournalService`) | **Add persistence** — §8 |
| Account identity / tier | — | New (Identity, §4) |
| Education progress (future) | — | New table on `AuthDbContext` |
| Real broker keys | n/a (paper-only hosted) | **Stays desktop-only** |

So the only existing service needing real work is the **Journal**; the rest is the path
swap plus a per-circuit "load this user's last workspace" step (§9).

## 8. Journal persistence (the one new bit)

`IJournalService` is an in-memory ring buffer today (great for a session). To make it a
durable, per-account *learning log*:

- On `AddSpeech`/entry, also append a line to `{AppDataDirectory}/journal.jsonl` (one JSON
  object per line — cheap, append-only, crash-safe; reuse `AtomicFile` semantics for any
  rewrite/trim). Cap the file (e.g. keep the last N days or M MB; rotate).
- On circuit start, load the tail of `journal.jsonl` into the ring so the user sees history.
- Keep it a thin decorator around the existing in-memory service so the hot path stays
  in-memory and the file write is async/batched.

(If you prefer queryable history — "show my divergence alerts last month" — a
`JournalEntries` table on a per-user DB is the alternative; the file approach is simpler and
fits the existing file-per-user model.)

## 9. Loading user state on circuit start

The per-circuit init already exists (`MainLayout.OnInitializedAsync` →
`IAppStartupService.InitializeAsync`). Order it so identity comes first:

1. Circuit opens → `WebHostBrowserCircuitHandler` sets `ICurrentUser` (§5).
2. `MainLayout.OnInitializedAsync` runs → `AppStartupService.InitializeAsync` (per circuit).
   By now `AppDataDirectory` resolves to the user's dir, so providers/orchestrator/indicators
   initialise against the right data.
3. Add a step: **load the user's last workspace + settings** into the scoped
   `IWorkspaceStore` (via the workspace library), so a returning user lands on their saved
   layout. "Save workspace" already writes to `AppDataDirectory` → now per-user.

> **Ordering caveat (the trickiest part):** services that read files **in their constructor**
> (e.g. `SettingsManager.LoadSettings()`) must not be resolved *before* `ICurrentUser` is set,
> or they'll read the wrong dir. Two safe options: (a) ensure those services are first
> resolved inside the per-circuit init (after the circuit handler), or (b) make them load
> lazily (first access) instead of in the ctor. Prefer (b) for `SettingsManager` — a one-line
> change to defer `LoadSettings()` — it removes the ordering dependency entirely.

## 10. Demo / anonymous coexistence

The existing `--demo` mode stays: anonymous visitors get `DemoPolicy` (curated, paper,
**non-persistent**). Wire it together cleanly:

- Anonymous circuit → `ICurrentUser.IsAuthenticated == false` → `DataKey = "anon-{circuitId}"`
  → an **ephemeral** per-circuit dir that's deleted on circuit close (or a tmpfs path). Demo
  writes go nowhere durable. `DemoPolicy.AllowSettingsPersist` already returns false in demo,
  so most writes are already suppressed.
- Authenticated circuit → real per-user dir, persistence on, full (paper) feature set.
- Generalise `DemoPolicy` into a per-user **policy/tier** object: anonymous → demo tier;
  `free`/`supporter` → progressively more (premium indicators, history depth) per
  `HOSTED_ACCOUNTS_STRATEGY.md`.

## 11. Security

- **Path isolation:** per-user dir derived from the Identity GUID, sanitised to
  `[A-Za-z0-9-]` — no traversal, no collision. A circuit can only ever see its own user's dir.
- **Authorization:** the app requires auth (except the demo route); a circuit can't reach
  another user's state because the scope only knows its own `ICurrentUser`.
- **Passwords:** Identity's PBKDF2 hashing; lockout on repeated failures; reasonable length
  policy (favour length over symbol-soup — easier for screen-reader users, equally strong).
- **Secrets at rest:** DataProtection (already used by `WebHostSecureStorageService`) for any
  per-user secret; persist the DataProtection **key ring** to disk (and back it up) so cookies
  + protected data survive restarts. For paper-only there are few secrets; real broker keys
  never reach the server.
- **Anti-forgery:** already `UseAntiforgery`; Identity forms carry tokens.
- **Transport:** HTTPS at nginx (already); set secure cookie flags.
- **Rate-limiting:** Identity lockout for login; add the planned **Blazor Server circuit
  rate-limiter** before public exposure (DoS guard) — tracked in TODO L9 follow-ups.
- **Backups:** back up `auth.db` + the `users/` tree + the DataProtection key ring. Single
  instance = single disk = back it up.

## 12. Accessibility of the auth flow (do not skip)

This is the front door for the entire audience — if a blind user can't register/log in,
nothing else matters:

- Plain semantic HTML forms, every input with an associated `<label>`; no placeholder-only
  labels.
- Validation errors announced via an `aria-live` region and tied to fields
  (`aria-describedby`), not just colour.
- Logical tab/focus order; focus moved to the first error on a failed submit.
- **No CAPTCHA that blocks screen readers** — if you need bot protection, use an accessible
  method (email confirmation, rate-limiting, honeypot, or an accessible challenge).
- Test the whole flow end-to-end with Orca/NVDA/VoiceOver before launch. The Identity
  scaffolded pages are a *starting point*, not accessible-by-default — audit them.

## 13. The desktop client (unchanged — and why it needs nothing)

The MAUI client is one user per device and already persists locally through the **same
interfaces**: `ISettingsManager` → `MauiPathService.AppDataDirectory` (a fixed local dir),
secrets → the OS keychain. Because the persistence services are written against
`IPlatformPathService`/`ISettingsManager`, the desktop "stores user settings locally" with
**zero new work** — it just has one user and one fixed directory, where the WebHost swaps in
the user-scoped path service. No auth, no accounts, no routing on the desktop.

> **Optional future (explicitly out of scope now):** a desktop "sign in to sync" that pulls
> *non-sensitive* account data (workspaces, sound design, education progress) from the cloud
> account read-only — **never** syncing broker keys or enabling server-side real trading.
> The shared interfaces make this addable later without disturbing the local-only default.

## 14. Phased rollout

- **Phase A — Identity.** `AppUser`, `AuthDbContext` (SQLite/WAL), Identity DI + endpoints,
  **accessible** login/register/reset, `app.UseAuthentication/UseAuthorization`, app gated by
  `[Authorize]` with the demo route anonymous. (Email confirmation can come slightly later.)
- **Phase B — Per-user routing.** `ICurrentUser` + circuit-handler capture +
  `UserScopedPathService` (replace `WebHostPathService`). Now settings/workspaces/sound/paper
  are per-user. Make `SettingsManager` load lazily (ordering caveat, §9).
- **Phase C — Load on login.** Per-circuit init loads the user's last workspace/settings;
  "Save workspace" persists to the account.
- **Phase D — Journal durability** (§8) + verify alerts persist per user.
- **Phase E — Account UI & tiers.** Profile page (email, tier, sign out, delete account),
  generalise `DemoPolicy` → per-user tier policy (premium-indicator gating).
- **Phase F — Hardening.** Circuit rate-limiter, login lockout tuning, backups, DataProtection
  key-ring persistence, a full accessible-auth pass with a screen reader.

## 15. New / changed files (checklist)

**New (WebHost):** `AppUser`, `AuthDbContext`, `ICurrentUser`/`CurrentUser`,
`UserScopedPathService`, Identity pages/components, EF migration for `auth.db`, an
`IJournalPersistence` decorator (Phase D).
**Modified (WebHost):** `ServiceCollectionExtensions` (Identity + auth + scoped
`ICurrentUser` + swap path service to `UserScopedPathService`), `Program.cs`
(`UseAuthentication`/`UseAuthorization`, map Identity endpoints), `WebHostBrowserCircuitHandler`
(set `ICurrentUser`), `Routes.razor`/`MainLayout` (auth gate + load user state).
**Modified (Core, shared, low-risk):** `SettingsManager` — defer `LoadSettings()` to first
access (removes the ordering dependency; harmless on MAUI).
**Untouched:** the MAUI head, every file-based per-user service, the desktop persistence path.

## 16. Open decisions (settle before Phase A)

- **DB:** SQLite to start (recommended), Postgres if/when concurrency demands.
- **Email:** is transactional email available (confirmation, password reset)? If not, start
  with `RequireConfirmedEmail = false` and a manual reset, add email later.
- **Demo ↔ accounts:** does the public site offer *both* an anonymous demo route and a
  "create account" path, or does sign-in become the default with demo as a labelled
  "try without an account"? (Recommend: keep the anonymous demo; add a prominent
  "create a free account to save your work".)
- **Money model / tiers:** determines what the tier policy gates (see
  `HOSTED_ACCOUNTS_STRATEGY.md` open decisions).

---

*Cross-references: `WEBHOST_MULTI_USER_SCOPING.md` (per-circuit scoping this builds on),
`HOSTED_ACCOUNTS_STRATEGY.md` (product direction & tiers), `PLATFORMS.md` (the WebHost
secure-storage / path model).*
