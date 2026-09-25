#!/usr/bin/env python3
"""A2o — prove every new guard RED on the mutant it was written for, and GREEN on control.

All SEVEN survivors are here, plus O17 — caught in the campaign only by a PARITY pin
(DemoCsp_DiffersFromStrictCsp_OnlyInFrameAncestors), whose natural repair is to copy the
edit into the demo policy; scored NOT honestly caught, A2h's bookkeeping rule. O17d is that
repair. None was equivalent (checked before writing — the A2h/A2i
rule), and each is a behaviour a user or an attacker would meet:

  O11 — the refusal page never recognises the auth tier: a locked-out sign-in is told
        "too many requests". The integration test read "try again" only; the Render()
        unit tests pass the tier in by hand, so the CALL SITE's classification was free.
  O21 — the --unsafe-remote-full opt-out inverted: Full mode served on a public bind.
        Every other WebHost test runs on TestServer, whose address list is empty.
  O22 — --demo boots the FULL terminal. The demo head was booted, never asked its tier.
  O31 — a live circuit survives a rotated security stamp.
  O32 — revalidation ignores lockout (lockout keeps the stamp, so O31's test can't see it).
  O43 — the live-region fallback leaves the region ON: every later phrase doubled.
  O45 — the browser audio path treats every buffer as silence: no sonification at all.
        Needed a production SEAM (internal ctor taking the player probe); the anchor below
        is the threshold, which the seam did not move.

Filters name ONE test class each so a red result names the guard that caught it. After
the mutant runs, the SAME filter is run on the clean tree (the control half). Harness
rules as ever: byte backups, touch, unique anchors, "No test matches" is a failure, no
`-v q` on test, REBUILD before exiting, byte-compare at the end.
"""
import filecmp, json, os, re, shutil, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2o_prove_kills_results.json")
BACKUP_DIR = "/tmp/a2o/prove_backup"
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")
NO_MATCH = "No test matches the given testcase filter"

W = "AccessibleTrader.WebHost/"
S = W + "Services/"

KILLS = [
    ("O11", W + "Program.cs",
     "            bool isAuthTier = AuthRateLimitPolicy.IsAuthMutation(http.Request.Method, http.Request.Path);",
     "            bool isAuthTier = false;",
     "WebHostSecurityHardeningIntegrationTests.A_rate_limited_request_carries_Retry_After_and_an_announceable_body"),

    ("O17", S + "SecurityPolicy.cs",
     "    public const string ContentSecurityPolicy =\n        \"default-src 'self'; \"\n        + \"script-src 'self'; \"",
     "    public const string ContentSecurityPolicy =\n        \"default-src 'self'; \"\n        + \"script-src 'self' 'unsafe-inline'; \"",
     "WebHostSecurityPolicyTests.Script_src_is_exactly_self_in_every_policy"),

    # O17's twin on the demo copy: the edit the parity pin's "natural repair" would make.
    ("O17d", S + "SecurityPolicy.cs",
     "    public const string DemoContentSecurityPolicy =\n        \"default-src 'self'; \"\n        + \"script-src 'self'; \"",
     "    public const string DemoContentSecurityPolicy =\n        \"default-src 'self'; \"\n        + \"script-src 'self' 'unsafe-inline'; \"",
     "WebHostSecurityPolicyTests.Script_src_is_exactly_self_in_every_policy"),

    ("O21", W + "Program.cs",
     'if (hostMode == HostMode.Full && !args.Contains("--unsafe-remote-full"))',
     'if (hostMode == HostMode.Full && args.Contains("--unsafe-remote-full"))',
     "FullModeBindRefusalIntegrationTests"),

    ("O22", W + "Program.cs",
     "             : demoMode        ? HostMode.Demo",
     "             : demoMode        ? HostMode.Full",
     "DemoHeadIsLockedDownIntegrationTests"),

    ("O31", W + "Account/IdentityRevalidatingAuthenticationStateProvider.cs",
     "            return principalStamp == userStamp;",
     "            return principalStamp != null;",
     "CircuitRevalidationTests"),

    ("O32", W + "Account/IdentityRevalidatingAuthenticationStateProvider.cs",
     "            if (userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(user))",
     "            if (false && userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(user))",
     "CircuitRevalidationTests"),

    ("O43", S + "WebHostSpeechManager.cs",
     "                    b.LiveRegionEnabled = previous;",
     "                    b.LiveRegionEnabled = true;",
     "SpeechFallbackTests"),

    ("O45", S + "WebHostAudioDriver.cs",
     "                            if (MathF.Abs(floats[i]) > 1e-4f) { silent = false; break; }",
     "                            if (MathF.Abs(floats[i]) > 1e4f) { silent = false; break; }",
     "BrowserAudioPumpTests"),
]


def run(cmd, timeout=3600):
    p = subprocess.run(cmd, cwd=REPO, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    code, out = run("nice -n 10 dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out   # True == built (A2f rule 7: never invert this)


def test(filt):
    _, out = run("nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                 f"-p:UseRazorSourceGenerator=false --no-build --filter FullyQualifiedName~{filt}")
    if NO_MATCH in out:
        return 'FILTER_MATCHED_NOTHING', -1, -1, []
    m = SUMMARY_RE.search(out)
    failed = int(m.group(1)) if m else -1
    passed = int(m.group(2)) if m else -1
    names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
    status = 'UNPARSED' if failed < 0 else ('RED' if failed > 0 else 'GREEN')
    return status, failed, passed, names


def bpath(rel):
    return os.path.join(BACKUP_DIR, rel.replace("/", "__"))


def main():
    only = [a for a in sys.argv[1:] if a.startswith("O")] or None
    os.makedirs(BACKUP_DIR, exist_ok=True)
    files = sorted({k[1] for k in KILLS})
    for rel in files:
        shutil.copyfile(os.path.join(REPO, rel), bpath(rel))

    results = []
    for kid, relpath, find, repl, filt in KILLS:
        if only and kid not in only:
            continue
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8', newline='').read()
        n = original.count(find)
        rec = {'id': kid, 'file': relpath, 'filter': filt, 'occurrences': n}
        if n != 1:
            rec['status'] = 'BAD_ANCHOR'
            results.append(rec)
            print(f"{kid}: BAD ANCHOR ({n}) — {relpath}", flush=True)
            continue
        try:
            with open(path, 'w', encoding='utf-8', newline='') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['mutant'] = 'NO_COMPILE'
                print(f"{kid}: DID NOT COMPILE\n{log[-600:]}", flush=True)
            else:
                st, f, p, names = test(filt)
                rec.update(mutant=st, mutant_failed=f, mutant_passed=p, mutant_failing=names)
                print(f"{kid} MUTANT: {st} failed={f} passed={p}", flush=True)
                for t in names[:6]:
                    print(f"      {t}", flush=True)
        finally:
            shutil.copyfile(bpath(relpath), path)
            os.utime(path, None)

        ok, log = build()
        assert ok, log[-1500:]
        st, f, p, names = test(filt)
        rec.update(control=st, control_failed=f, control_passed=p, control_failing=names)
        print(f"{kid} CONTROL: {st} failed={f} passed={p}", flush=True)
        rec['status'] = 'PROVED' if rec.get('mutant') == 'RED' and st == 'GREEN' else 'NOT_PROVED'
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    build()
    print("\n=== summary")
    for r in results:
        print(f"  {r['id']:>4} {r.get('status'):>11}  mutant={r.get('mutant')} control={r.get('control')}  {r['filter']}")
    proved = sum(1 for r in results if r.get('status') == 'PROVED')
    print(f"\n{proved}/{len(results)} proved RED on the mutant and GREEN on control")
    same = all(filecmp.cmp(os.path.join(REPO, rel), bpath(rel), shallow=False) for rel in files)
    print("restored byte-identical" if same else "TREE NOT RESTORED")


if __name__ == '__main__':
    main()
