#!/usr/bin/env python3
"""A2o — mutation campaign over the C# in `AccessibleTrader.WebHost`.

WHY THIS AREA.

~12,200 lines in the WebHost head and only THREE of its files had ever been mutated
(SecurityPolicy.cs once — the CSP frame-ancestors in A2 M22 — HostedAlertMonitor.cs,
RecentAlertsBuffer.cs, UserScopedPathService.cs). This is the head that faces the public
internet: auth rate limiting, the security headers, the Full-mode loopback bind guard, the
DemoPolicy tiers, the account pages' enumeration refusals, the push/secret stores, and the
speech/audio bridges that are the only voice the hosted and local-web terminal has.

SELECTION RULE, weighted toward security and toward what a user HEARS: a rate-limit tier that
stops covering a credential endpoint, a header dropped, Full mode served on a non-loopback bind,
a demo that becomes the full terminal, a refusal page that stops saying how long to wait, an
enumeration oracle reopened, a speech bridge that double-speaks or goes silent. Several are
RESTORATIONS of defects this repo already fixed once (the comments above each site say so) —
the sharpest form of the question: is the fix guarded, or merely made?

SAMPLING FRAME: no mutant repeats a2 M22, fresh N23/N24 or a2d D18/D22.

METHOD — identical to A2j so the rates compare: apply one mutant, build, run the FULL
AccessibleTrader.Tests suite (not the browser suite), record whether anything went red and
WHICH tests did, restore from a FILE COPY, touch. CAUGHT iff some test fails.

HARNESS RULES (sabotage-harness-rules):
  1. `touch` after restoring, or MSBuild keeps the sabotaged binary.
  2. "No test matches the given testcase filter" is a FAILURE (prove script).
  3. Assert the anchor is UNIQUE before patching (--verify, and again per mutant).
  4. Restore from a byte copy in /tmp, never `git checkout --` (the A2j template's
     recover_inflight used git checkout; this one restores the byte backup instead, and
     compares bytes, so a BOM or CRLF cannot be silently rewritten either).
  5. Do not touch the repo while it runs.
  6. NEVER `-v q` on `dotnet test` — it empties failing_tests and removes the audit.
  7. Print failing NAMES.
  8. Audit catches for bookkeeping guards and FLAKES (CPU is shared with three other
     campaigns): see a2o_audit.py.
  9. Check equivalence before writing a test.
"""
import filecmp, json, os, re, shutil, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2o_sabotage_results.json")
BACKUP_DIR = "/tmp/a2o/backup"
INFLIGHT = "/tmp/a2o/inflight.json"
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")

W = "AccessibleTrader.WebHost/"
S = W + "Services/"
P = W + "Pages/Account/"

MUTANTS = [
    # ── Rate limiting: which POSTs are in the strict tier ─────────────────────
    ("O01", "the login POST leaves the auth tier",
     S + "SecurityPolicy.cs",
     '&& (path.StartsWithSegments("/account/login")',
     '&& (path.StartsWithSegments("/account/login-off")',
     "password guessing on the front door runs at 200 per 10 s per IP again — the 72k/hour bug"),

    ("O02", "the signed-in security page leaves the auth tier",
     S + "SecurityPolicy.cs",
     '|| path.StartsWithSegments("/account/security")',
     '|| path.StartsWithSegments("/account/security-off")',
     "a stolen session brute-forces the current password through 'Turn off 2FA' at general rates"),

    ("O03", "forgot-password leaves the auth tier",
     S + "SecurityPolicy.cs",
     '|| path.StartsWithSegments("/account/forgotpassword")',
     '|| path.StartsWithSegments("/account/forgotpassword-off")',
     "every POST appends attacker text to the security log: an unauthenticated disk-fill"),

    ("O04", "auth tier is ten times looser",
     S + "SecurityPolicy.cs",
     "    public const int AuthPermitLimit = 10;",
     "    public const int AuthPermitLimit = 100;",
     "100 credential attempts per 5 minutes per IP"),

    ("O05", "every IP shares ONE auth bucket",
     S + "SecurityPolicy.cs",
     '                $"auth:{ip}",',
     '                "auth:shared",',
     "one attacker's ten POSTs lock every visitor on earth out of signing in"),

    ("O06", "the rate limiter is never put in the pipeline",
     W + "Program.cs",
     "    app.UseRateLimiter();",
     "    // app.UseRateLimiter();",
     "the whole abuse guard is registered and never runs"),

    # ── The 429 refusal: what a rate-limited person is told ──────────────────
    ("O07", "the refusal stops saying how long to wait",
     S + "SecurityPolicy.cs",
     '             + "<p>Wait about " + wait + ", then try again.</p>"',
     '             + "<p>Please try again later.</p>"',
     "a screen-reader user hears 'please wait' with no number — the header is for machines"),

    ("O08", "the tier wording is swapped",
     S + "SecurityPolicy.cs",
     "        string what = isAuthTier",
     "        string what = !isAuthTier",
     "a shared-NAT visitor loading a page is told 'too many sign-in attempts'"),

    ("O09", "429 goes out without Retry-After",
     W + "Program.cs",
     "            http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);",
     "            // Retry-After dropped",
     "the machine-readable wait disappears; clients retry blind"),

    ("O10", "only NON-document requests get the readable body",
     W + "Program.cs",
     '            if (http.Request.Headers.Accept.Any(a => a != null && a.Contains("text/html", StringComparison.OrdinalIgnoreCase)))',
     '            if (!http.Request.Headers.Accept.Any(a => a != null && a.Contains("text/html", StringComparison.OrdinalIgnoreCase)))',
     "the browser gets an empty 429 again — the silent refusal restored — and SignalR gets HTML"),

    ("O11", "the refusal never recognises the auth tier",
     W + "Program.cs",
     "            bool isAuthTier = AuthRateLimitPolicy.IsAuthMutation(http.Request.Method, http.Request.Path);",
     "            bool isAuthTier = false;",
     "a locked-out sign-in is told 'too many requests', not 'too many sign-in attempts'"),

    ("O12", "the spoken wait rounds minutes down to one",
     S + "SecurityPolicy.cs",
     '$"{System.Math.Max(1, retryAfterSeconds / 60)} minute"',
     '$"{System.Math.Max(1, retryAfterSeconds / 600)} minute"',
     "a five-minute lockout is announced as 'about 1 minute'"),

    # ── Response headers ─────────────────────────────────────────────────────
    ("O13", "nosniff dropped",
     S + "SecurityPolicy.cs",
     '        h["X-Content-Type-Options"] = "nosniff";',
     '        // nosniff dropped',
     "MIME-sniffing re-enabled on every response"),

    ("O14", "non-demo modes allow same-origin framing",
     S + "SecurityPolicy.cs",
     '            h["X-Frame-Options"] = "DENY";',
     '            h["X-Frame-Options"] = "SAMEORIGIN";',
     "the hosted accounts terminal can be framed by any page on the origin"),

    ("O15", "hosted/desktop get the demo's relaxed CSP",
     S + "SecurityPolicy.cs",
     '            h["Content-Security-Policy"] = ContentSecurityPolicy;',
     '            h["Content-Security-Policy"] = DemoContentSecurityPolicy;',
     "frame-ancestors 'self' on the terminal that holds accounts"),

    ("O16", "HSTS sent on plain HTTP and never on HTTPS",
     S + "SecurityPolicy.cs",
     "        if (ctx.Request.IsHttps)",
     "        if (!ctx.Request.IsHttps)",
     "the hosted site loses HSTS; a local http run pins HSTS for localhost"),

    ("O17", "the strict CSP allows inline script",
     S + "SecurityPolicy.cs",
     "    public const string ContentSecurityPolicy =\n        \"default-src 'self'; \"\n        + \"script-src 'self'; \"",
     "    public const string ContentSecurityPolicy =\n        \"default-src 'self'; \"\n        + \"script-src 'self' 'unsafe-inline'; \"",
     "any injected <script> runs — the CSP's whole XSS value gone"),

    # ── Full mode must never be served off loopback ──────────────────────────
    ("O18", "wildcard binds read as loopback",
     S + "SecurityPolicy.cs",
     "            return false; // wildcard binds (\"+\", \"*\") and garbage both fail closed",
     "            return true; // wildcard binds (\"+\", \"*\") and garbage both fail closed",
     "http://+:80 serves the unauthenticated full terminal to every interface"),

    ("O19", "0.0.0.0 counted as loopback",
     S + "SecurityPolicy.cs",
     "            && System.Net.IPAddress.IsLoopback(ip);",
     "            && (System.Net.IPAddress.IsLoopback(ip) || ip.Equals(System.Net.IPAddress.Any));",
     "the commonest public bind of all passes the guard"),

    ("O20", "only the first bound address is checked",
     S + "SecurityPolicy.cs",
     "            if (!IsLoopback(address)) return address;",
     "            return IsLoopback(address) ? null : address;",
     "localhost:5000 + 0.0.0.0:5001 → the second, public, bind is never looked at"),

    ("O21", "the --unsafe-remote-full opt-out is inverted",
     W + "Program.cs",
     'if (hostMode == HostMode.Full && !args.Contains("--unsafe-remote-full"))',
     'if (hostMode == HostMode.Full && args.Contains("--unsafe-remote-full"))',
     "the bind guard runs only for operators who asked to skip it"),

    # ── Mode gating ──────────────────────────────────────────────────────────
    ("O22", "--demo boots the FULL terminal",
     W + "Program.cs",
     "             : demoMode        ? HostMode.Demo",
     "             : demoMode        ? HostMode.Full",
     "the public demo gets live trading, the API-keys modal and server-side scripts"),

    ("O23", "the diagnostic journal is anonymous on the accounts head",
     W + "Program.cs",
     "    if (accountsEnabled) diag.RequireAuthorization();",
     "    // diag auth dropped",
     "with --enable-diag, an anonymous dump of a spoken transcript"),

    ("O24", "the error page requires a sign-in",
     W + "Program.cs",
     "        StatusCodes.Status500InternalServerError);\n}).AllowAnonymous();",
     "        StatusCodes.Status500InternalServerError);\n}).RequireAuthorization();",
     "the sign-in page's own failure redirects to the sign-in page"),

    ("O25", "the hosted paper-account guard only fires with NO user object",
     S + "PaperAccountAttachment.cs",
     "            if (demo?.IsHosted == true && currentUser?.IsAuthenticated != true)",
     "            if (demo?.IsHosted == true && currentUser == null)",
     "a pre-circuit anonymous scope binds every hosted user to the shared 'anon' paper account"),

    # ── Account pages ────────────────────────────────────────────────────────
    ("O26", "sign-in failures stop counting toward lockout",
     P + "Login.cshtml.cs",
     "                email, Input.Password, Input.RememberMe, lockoutOnFailure: true);",
     "                email, Input.Password, Input.RememberMe, lockoutOnFailure: false);",
     "Identity's ten-failure lockout never trips at the front door"),

    ("O27", "a locked-out sign-in says so (enumeration oracle)",
     P + "Login.cshtml.cs",
     '            Error = "Email or password is incorrect.";',
     '            Error = result.IsLockedOut ? "This account is locked." : "Email or password is incorrect.";',
     "a distinct lockout message confirms the address is registered"),

    ("O28", "the security page's re-confirmation stops counting failures",
     P + "Security.cshtml.cs",
     "                var result = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);",
     "                var result = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);",
     "the 2FA-disable form is an unlimited password oracle for a stolen session"),

    ("O29", "registration echoes 'email already taken'",
     P + "Register.cshtml.cs",
     '                if (e.Code is "DuplicateEmail" or "DuplicateUserName")',
     '                if (e.Code is "DuplicateUserName")',
     "the DuplicateEmail description reaches the page — an enumeration oracle"),

    ("O30", "reset-password echoes the raw token error",
     P + "ResetPassword.cshtml.cs",
     '                    if (e.Code is "InvalidToken")',
     '                    if (e.Code is "InvalidToken-off")',
     "'Invalid token.' for a real address vs the generic message for an unknown one"),

    ("O31", "a live circuit survives a rotated security stamp",
     W + "Account/IdentityRevalidatingAuthenticationStateProvider.cs",
     "            return principalStamp == userStamp;",
     "            return principalStamp != null;",
     "password reset / 2FA enrolment no longer evicts a stolen session's open circuit"),

    ("O32", "revalidation ignores lockout",
     W + "Account/IdentityRevalidatingAuthenticationStateProvider.cs",
     "            if (userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(user))",
     "            if (false && userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(user))",
     "a locked-out account's open circuit keeps working"),

    # ── Push and secrets ─────────────────────────────────────────────────────
    ("O33", "private-IP push endpoints accepted",
     S + "Push/PushSubscriptionStore.cs",
     "                return AccessibleTrader.Core.Services.Alerts.OutboundNetworkGuard.IsPublic(literal);",
     "                return true;",
     "a signed-in user aims the server's push sender at 169.254.169.254"),

    ("O34", "the subscription cap evicts the NEWEST",
     S + "Push/PushSubscriptionStore.cs",
     "                while (next.Count > MaxSubscriptionsPerUser) next.RemoveAt(0);",
     "                while (next.Count > MaxSubscriptionsPerUser) next.RemoveAt(next.Count - 1);",
     "the browser the user just subscribed never receives a push"),

    ("O35", "the VAPID private key is written in the clear",
     S + "Push/VapidKeyService.cs",
     "                if (_protector != null)\n                    stored.PrivateKeyProtected",
     "                if (_protector == null)\n                    stored.PrivateKeyProtected",
     "the one secret on the box that skips DataProtection — again"),

    ("O36", "the key ring is tightened to 0750, not 0700",
     S + "KeyRingPolicy.cs",
     "                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);",
     "                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);",
     "a fresh deploy under umask 022 refuses to start (or, without the after-check, serves a group-readable ring)"),

    ("O37", "secure-storage files are named by the raw key",
     S + "WebHostSecureStorageService.cs",
     '            return Path.Combine(_secretsDir, Convert.ToHexString(hash) + ".bin");',
     '            return Path.Combine(_secretsDir, key + ".bin");',
     "a key containing '../' writes outside secrets/, and names leak what is stored"),

    ("O38", "PowerShell literals stop doubling quotes",
     S + "DesktopDeliveryPlan.cs",
     """            => "'" + (s ?? string.Empty).Replace("'", "''") + "'";""",
     """            => "'" + (s ?? string.Empty) + "'";""",
     "an alert or symbol text with an apostrophe breaks out of the toast/speech command on Windows"),

    # ── Speech and audio: what the user hears ────────────────────────────────
    ("O39", "the live region stays on under Orca/spd-say",
     S + "WebHostSpeechManager.cs",
     "            => backend == SpeechBackend.BrowserTts && mode != SpeechOutputMode.BrowserVoice;",
     "            => mode != SpeechOutputMode.BrowserVoice;",
     "every phrase heard twice — the 2026-07-23 double-speech restored"),

    ("O40", "'screen reader' mode still speaks through the browser voice",
     S + "WebHostSpeechManager.cs",
     "                    if (_outputMode != SpeechOutputMode.ScreenReader)",
     "                    if (true)",
     "the user who chose their screen reader hears the browser voice on top of it"),

    ("O41", "spd-say preferred over Orca",
     S + "WebHostSpeechManager.cs",
     "            if (gdbusPath is not null && orcaAvailable) return SpeechBackend.OrcaDBus;",
     "            if (gdbusPath is not null && orcaAvailable && spdSayPath is null) return SpeechBackend.OrcaDBus;",
     "the user's Orca voice, rate and pitch are bypassed whenever spd-say exists"),

    ("O42", "speech switched off still speaks",
     S + "WebHostSpeechManager.cs",
     "            if (string.IsNullOrWhiteSpace(text) || !_inner.IsSpeechEnabled) return;",
     "            if (string.IsNullOrWhiteSpace(text)) return;",
     "muting speech mutes the live region but Orca/browser voice keep talking"),

    ("O43", "the live-region fallback leaves the region ON",
     S + "WebHostSpeechManager.cs",
     "                    b.LiveRegionEnabled = previous;",
     "                    b.LiveRegionEnabled = true;",
     "after one failed Orca call every later phrase is spoken twice"),

    ("O44", "the Orca interrupt is not awaited",
     S + "WebHostSpeechManager.cs",
     '                if (interrupt && _spdSayPath != null) RunSpdSayToCompletion("-S");',
     '                if (interrupt && _spdSayPath != null) StartSpdSay("-S");',
     "the cancel can land after the message and clip the utterance it cleared the way for"),

    ("O45", "the browser audio path treats every buffer as silence",
     S + "WebHostAudioDriver.cs",
     "                            if (MathF.Abs(floats[i]) > 1e-4f) { silent = false; break; }",
     "                            if (MathF.Abs(floats[i]) > 1e4f) { silent = false; break; }",
     "the demo and every host without a local PCM sink hear no sonification at all"),

    # ── Headless (browser closed) ────────────────────────────────────────────
    ("O46", "a headless order rejection drops its reason",
     S + "HeadlessOrderAnnouncer.cs",
     """                string why = string.IsNullOrWhiteSpace(e.Reason) ? "" : " " + e.Reason.TrimEnd('.') + ".";""",
     """                string why = "";""",
     "'Order rejected for BTC.' — and not why"),

    ("O47", "headless announces exactly the venues a browser is covering",
     S + "HeadlessOrderAnnouncer.cs",
     "            if (covered)\n",
     "            if (!covered)\n",
     "fills said twice while a tab is open, and never once it closes"),

    ("O48", "a dropped connection keeps its coverage",
     S + "WebHostBrowserCircuitHandler.cs",
     "            ReleaseCoverage();\n            SetPresence(false);",
     "            SetPresence(false);",
     "a laptop lid closes and the headless monitor still thinks the tab is speaking alerts"),
]


def run(cmd, cwd=REPO, timeout=5400):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    # -v q is fine on BUILD; never on test (rule 6).
    code, out = run("nice -n 10 dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test():
    return run("nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
               "-p:UseRazorSourceGenerator=false --no-build")


def backup_path(relpath):
    return os.path.join(BACKUP_DIR, relpath.replace("/", "__"))


def recover_inflight():
    if not os.path.exists(INFLIGHT):
        return
    rel = json.load(open(INFLIGHT))["file"]
    src = backup_path(rel)
    dst = os.path.join(REPO, rel)
    shutil.copyfile(src, dst)
    os.utime(dst, None)
    print(f"recovered stale sabotage in {rel} from byte backup", flush=True)
    os.remove(INFLIGHT)


def verify():
    ok = True
    for mid, area, relpath, find, repl, _ in MUTANTS:
        path = os.path.join(REPO, relpath)
        if not os.path.exists(path):
            print(f"{mid}: MISSING FILE {relpath}"); ok = False; continue
        src = open(path, encoding='utf-8', newline='').read()
        n = src.count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences (need exactly 1) in {relpath}\n    {find[:120]!r}")
            ok = False
        if find == repl:
            print(f"{mid}: EQUIVALENT — find == replace"); ok = False
    ids = [m[0] for m in MUTANTS]
    if len(set(ids)) != len(ids):
        print("DUPLICATE IDS"); ok = False
    print(f"all {len(MUTANTS)} anchors unique" if ok else "ANCHOR CHECK FAILED")
    return ok


def main():
    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)

    os.makedirs(BACKUP_DIR, exist_ok=True)
    recover_inflight()
    # Byte backups of every file any mutant touches, taken once from the committed tree.
    for rel in sorted({m[2] for m in MUTANTS}):
        b = backup_path(rel)
        if not os.path.exists(b):
            shutil.copyfile(os.path.join(REPO, rel), b)

    only = [a for a in sys.argv[1:] if a.startswith("O")] or None
    results = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in results}

    for mid, area, relpath, find, repl, breaks in MUTANTS:
        if (only and mid not in only) or mid in done:
            continue
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8', newline='').read()
        n = original.count(find)
        rec = {'id': mid, 'area': area, 'file': relpath, 'breaks': breaks, 'occurrences': n}
        if n != 1:
            rec['status'] = 'BAD_ANCHOR'
            results.append(rec); json.dump(results, open(OUT, 'w'), indent=1)
            print(f"{mid}: BAD ANCHOR ({n}) — {relpath}", flush=True); continue
        t0 = time.time()
        try:
            json.dump({'file': relpath}, open(INFLIGHT, 'w'))
            with open(path, 'w', encoding='utf-8', newline='') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['status'] = 'NO_COMPILE'; rec['log'] = log[-1500:]
                print(f"{mid}: DID NOT COMPILE — {area}\n{log[-800:]}", flush=True)
            else:
                code, out = test()
                m = SUMMARY_RE.search(out)
                rec['failed'] = int(m.group(1)) if m else -1
                rec['passed'] = int(m.group(2)) if m else -1
                names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                rec['failing_tests'] = names[:60]
                rec['status'] = ('UNPARSED' if rec['failed'] < 0
                                 else 'CAUGHT' if rec['failed'] > 0 else 'SURVIVED')
                if rec['status'] == 'UNPARSED':
                    rec['log'] = out[-2000:]
                print(f"{mid}: {rec['status']} failed={rec['failed']} ({time.time()-t0:.0f}s) — {area}", flush=True)
                for t in names[:8]:
                    print("      " + t, flush=True)
        finally:
            shutil.copyfile(backup_path(relpath), path)
            os.utime(path, None)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec); json.dump(results, open(OUT, 'w'), indent=1)

    ok, _ = build()
    code, out = test()
    m = SUMMARY_RE.search(out)
    names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
    print(f"\n=== CONTROL (nothing sabotaged): build_ok={ok} {m.group(0) if m else 'UNPARSED'}", flush=True)
    for t in names:
        print("      control failure: " + t, flush=True)

    all_same = True
    for rel in sorted({mm[2] for mm in MUTANTS}):
        same = filecmp.cmp(os.path.join(REPO, rel), backup_path(rel), shallow=False)
        all_same &= same
        if not same:
            print(f"NOT RESTORED: {rel}")
    print("restored byte-identical" if all_same else "TREE NOT RESTORED")

    print("\n=== summary")
    for r in results:
        print(f"  {r['id']} {r['status']:>10}  {r['area']}")
    surv = [r['id'] for r in results if r['status'] == 'SURVIVED']
    caught = sum(1 for r in results if r['status'] == 'CAUGHT')
    total = caught + len(surv)
    if total:
        print(f"\ncatch rate {caught}/{total} = {100*caught/total:.1f}%   survivors: {surv}")


if __name__ == '__main__':
    main()
