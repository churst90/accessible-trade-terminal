# A2o — mutation campaign over `AccessibleTrader.WebHost` (C# only)

Date: 2026-09-24. Branch `worktree-agent-ad812a2e561900dd6`. No docs edited (docs/ is the
caller's to update). No `wwwroot/*.js` touched.

## Frame

~12,200 lines of C# in the WebHost head; before this pass only four of its files had ever
been mutated (SecurityPolicy.cs once — A2 M22, frame-ancestors — HostedAlertMonitor.cs,
RecentAlertsBuffer.cs, UserScopedPathService.cs). 48 mutants over 16 files, weighted to
security and to what a user hears:

| Area | Mutants |
|---|---|
| Auth rate limiting (tier coverage, limit, per-IP key, middleware wired) | O01–O06 |
| The 429 refusal (wait sentence, tier wording, Retry-After, HTML body, call-site tier, minutes) | O07–O12 |
| Response headers (nosniff, XFO, CSP swap, HSTS, script-src) | O13–O17 |
| Full-mode loopback bind guard (wildcard, 0.0.0.0, first-only, call-site flag) | O18–O21 |
| Mode gating (demo→Full, diag auth, /Error anonymous, hosted paper-account guard) | O22–O25 |
| Account pages (lockout ×2, enumeration oracles ×3, circuit revalidation ×2) | O26–O32 |
| Push + secrets (private-IP endpoint, cap order, VAPID plaintext, key ring 0700, secure-storage naming, PowerShell quoting) | O33–O38 |
| Speech + audio bridges (double speech, screen-reader mode, Orca priority, speech-off, fallback restore, cancel race, browser silence threshold) | O39–O45 |
| Headless (reject reason, coverage inverted, connection-down release) | O46–O48 |

Every anchor was asserted unique before the run (`a2o_sabotage.py --verify`), and again per
mutant. Baseline: build clean, full suite 8,088/8,088 green.

## Numbers

- Raw: **41 / 48 caught = 85.4%.**
- False-catch audit (`a2o_audit.py`): the union of all 97 catching tests ran green on the
  clean tree; every one of the 41 catches re-failed with its mutant re-applied alone. **0
  flake-catches.**
- Bookkeeping/proxy audit: **O17** (strict CSP gains `'unsafe-inline'` in script-src) was
  caught ONLY by `DemoCsp_DiffersFromStrictCsp_OnlyInFrameAncestors`, a parity pin whose
  natural repair is to make the same edit to the demo copy — after which both policies allow
  inline script and the suite is green (the directive test asserted
  `Contains("script-src 'self'")`, which `'self' 'unsafe-inline'` satisfies). Scored NOT
  honestly caught, per A2h's rule.
- **Honest rate: 40 / 48 = 83.3%.** The series now reads 73.1 / 72.0 / 69.2 / 62.2 / 10.5 /
  73.5 / 82.0 / **83.3**.
- Control run at the end of the campaign: 8,087 passed, 1 failed —
  `ChartAreaBarSliderTests.Flicking_the_slider_routes_through_the_arrow_key_navigation_pipeline`,
  unrelated to WebHost; re-run alone on the clean tree three times, green each time. A
  CPU-contention flake. (`OrderPostRetrySafetyTests.ExecuteOnceAsync_returns_the_result_and_still_takes_a_rate_slot`
  also went red once, alongside O08's real catchers, and again on O08's audit rerun — a
  timing test that is unrelated to any WebHost line; O08 is honest on its two named tests.)
- Every file restored byte-identical after the campaign, the audit, and the prove run.
- Final full suite with the new guards: **8,099 / 8,099 green** (8,088 + 11 new test cases).

## Survivors and how each was closed (8, all non-equivalent, all proved 9/9)

`a2o_prove_kills.py`: each kill RED on its mutant, GREEN on the clean tree, by one class filter.

| Id | Behaviour | Why it survived | Closed by |
|---|---|---|---|
| O11 | A locked-out sign-in's 429 page says "too many requests", not "too many sign-in attempts" | `Render()` unit tests pass the tier in by hand; the integration test read only "try again" | two assertions added to `WebHostSecurityHardeningIntegrationTests.A_rate_limited_request_carries_Retry_After_and_an_announceable_body` ("sign-in attempts", "5 minutes") |
| O17 (+O17d) | `script-src 'self' 'unsafe-inline'` | only a parity pin; the directive assertion could not distinguish | `WebHostSecurityPolicyTests.Script_src_is_exactly_self_in_every_policy` (theory over BOTH policies; O17d is the parity pin's "repair") |
| O21 | `--unsafe-remote-full` inverted at the call site: Full mode served on a public bind | `FullModeBindPolicyTests` pin the classifier only; every WebHost integration test runs on TestServer, whose address list is empty | `FullModeBindRefusalIntegrationTests` — .NET 10 `WebApplicationFactory.UseKestrel()`, endpoint via `Kestrel:Endpoints:Http:Url` at port 0; 0.0.0.0 must stop the host, 127.0.0.1 must keep serving |
| O22 | `--demo` boots `HostMode.Full` (live trading, API keys, Roslyn, unauthenticated alert endpoints on the public demo) | the demo head was booted (prefix, base href, negotiate) but nothing asked WHICH tier | `DemoHeadIsLockedDownIntegrationTests` — registered policy, `/app/alerts/recent` absent on the wire, the app's own CSP has frame-ancestors 'self' |
| O31 | A live circuit survives a rotated security stamp (password reset / 2FA enrolment doesn't evict a stolen session) | only the provider's REGISTRATION was tested | `CircuitRevalidationTests.A_password_reset_evicts_a_circuit_opened_before_it` |
| O32 | Revalidation ignores lockout | same; and lockout does not rotate the stamp, so O31's test cannot see it (asserted in the test) | `CircuitRevalidationTests.A_locked_out_account_loses_its_open_circuit` |
| O43 | The live-region fallback leaves the region ON after one failed spd-say/Orca call — every later phrase heard twice | no test ever drove a server-side backend whose process start fails | `SpeechFallbackTests` (spd-say path that does not exist; waits on the backend's own warning, no sleeps) |
| O45 | Browser audio path treats every buffer as silence — no sonification on the demo or any player-less host | the pump had never been run by a test: the only ctor probed the real filesystem and would start pw-cat | `BrowserAudioPumpTests` (silence is NOT published / a tone IS), via a new internal ctor seam |

## Production changes

One, and it is a test seam, not a fix: `WebHostAudioDriver` gains an `internal` constructor
taking the player probe (`Func<string,bool> fileExists`); the public constructor chains to it
with `File.Exists`. DI only sees public constructors, so resolution is unchanged.

## Real defects

**None demonstrated.** Two candidates were examined and rejected:

- `RateLimitRejection.Render` floors minutes (`retryAfterSeconds / 60`), so 299 s would read
  "about 4 minutes". Not reachable: a fixed-window limiter's failed lease reports the whole
  window (300 s) as RetryAfter, and the new integration assertion ("5 minutes", green on the
  clean tree) confirms that end to end. Unverified for any future sliding/token-bucket limiter.
- Every Blazor response carries a SECOND `Content-Security-Policy` header,
  `frame-ancestors 'self'`, added by the interactive-server endpoint. Browsers enforce the
  intersection of policies, so hosted/Full's `'none'` still wins. Not a defect, but any test
  that reads "the" CSP header off a Blazor page with `Single()` or `Contains` will be fooled
  by it — the demo test reads the policy that sets `default-src`.

## Lessons

1. **The classifier is guarded; the CALL SITE is not.** Three of the eight survivors (O11, O21, O22) are the
   same shape: a well-tested pure rule (`FullModeBindPolicy.IsLoopback`, `IsAuthMutation`,
   `RateLimitRejection.Render`, `DemoPolicy`'s per-tier flags) whose one caller in
   `Program.cs` could be inverted with the whole suite green. Unit tests of a rule do not test
   that the rule is asked, or asked with the right argument.
2. **TestServer has no addresses.** A guard that reads `IServerAddressesFeature` is vacuous
   under every `WebApplicationFactory` test unless the test opts into Kestrel. .NET 10's
   `UseKestrel()` makes that cheap; override `Kestrel:Endpoints` to port 0 or parallel
   worktrees collide on appsettings' 5145.
3. **A parity pin is a proxy.** `A == B'` catches a one-sided edit and blesses the same edit
   made twice. Pin the property on both sides.
4. **The rate was high because the security code was guarded against named defects** —
   32 of the 38 security-side mutants (O01–O38) were caught honestly, almost all by a test
   whose name is the defect; speech/audio 5/7, headless 3/3.
   The weak spots were the two places nobody had a harness for: a real socket, and a live
   circuit's second look at its principal.

## Could not verify

- `FullModeBindRefusalIntegrationTests` binds 0.0.0.0 on an ephemeral port for as long as
  Program.cs takes to read its addresses and stop (on the RED run, up to the 20 s timeout).
  Its behaviour on Windows/macOS CI (firewall prompts on an all-interfaces bind) was not run.
- `BrowserAudioPumpTests` and `SpeechFallbackTests` are timing-tolerant (10–15 s ceilings,
  condition-polled) but were run only on this Linux box under contention.
- Whether the browser actually PLAYS what the pump publishes is the JS side (audio.js), out
  of scope here.

## Files

- `scratchpad/a2o_sabotage.py`, `a2o_sabotage_results.json` — the campaign
- `scratchpad/a2o_audit.py`, `a2o_audit_results.json` — false-catch audit
- `scratchpad/a2o_prove_kills.py`, `a2o_prove_kills_results.json` — 9/9 proved
- Tests: `AccessibleTrader.Tests/WebHost/{DemoHeadIsLockedDownIntegrationTests,
  FullModeBindRefusalIntegrationTests, CircuitRevalidationTests, SpeechFallbackTests,
  BrowserAudioPumpTests}.cs` (new); `WebHostSecurityPolicyTests.cs`,
  `WebHostSecurityHardeningIntegrationTests.cs` (extended)
