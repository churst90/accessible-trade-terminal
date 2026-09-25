#!/usr/bin/env python3
"""A2n — the TENTH mutant set: `Core/Services/Trading` and `Core/Services/Scripting`.

WHY THESE TWO, AND WHY TOGETHER.

Trading (~2,600 lines, 14 files) has been touched eight times across four campaigns, all in
three small files (BarFill, ProtectiveLevelValidator, ManagedExitRules) plus one line each of
OrderPlacement and WithdrawalReleasePolicy. QuickTradeService, QuickTradeExecutor,
QuickTradeEquity, WithdrawalService, WalletService, PortfolioValuation and MarketDataPriceSource
have never been mutated. This is the layer that turns a keystroke into an order and a typed
word into a withdrawal.

Scripting (~2,560 lines, 15 files) has NEVER been mutated. It holds the script sandbox's
policy — whether a missing OS sandbox is refused or silently downgraded, what the bwrap argv
denies, and the timeouts and quotas that stop a hostile or hung script. A security control
whose guards have never been shown to fail.

SELECTION RULE: one mutant per decision a user (or an attacker) could exploit or be hurt by —
an order announced as placed when refused, a side or order type flipped, a double press that
places twice, a withdrawal that needs no confirmation, a sandbox rule that stops refusing, a
timeout that never fires. Sites already mutated in a2/a2b/fresh are excluded
(BarFill M01/M16, ProtectiveLevelValidator M02/M18, WithdrawalReleasePolicy M06,
OrderPlacement M21, ManagedExitRules N10/N11).

SAFETY: the sandbox mutants weaken the bwrap argv or the refusal policy. The only scripts the
suite runs inside a real worker are benign fixtures (echo-close, read-a-canary-file,
read-an-env-var). No test fixture writes, opens a socket or spawns a process from inside a
worker, so no mutant here can do anything outside the test process beyond reading a canary the
test itself wrote. Mac/Windows launcher mutants cannot execute on Linux at all.

METHOD — identical to A2j so the numbers compare: apply one mutant, build, run the FULL
AccessibleTrader.Tests suite, record whether anything went red and WHICH tests did, restore,
touch. CAUGHT iff some test fails. NEW here: a mutant that makes the suite HANG (a timeout that
never fires) is killed after HANG_LIMIT and recorded as HANG, reported separately.

HARNESS RULES (sabotage-harness-rules.md):
  1. `touch` after restoring.  2. "No test matches" is a failure (prove script).
  3. Anchor UNIQUE before patching.  4. Restore from a FILE COPY (scratchpad/a2n_backup),
     never `git checkout --`; commit before starting.  5. Do not touch the tree mid-run.
  6. NEVER `-v q` on dotnet test.  7. Print the failing NAMES.
  8. Audit catches for bookkeeping guards.  9. Check equivalence before writing a test.
"""
import json, os, re, shutil, signal, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2n_sabotage_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2n_sabotage_inflight.txt")
BACKUP = os.path.join(REPO, "scratchpad", "a2n_backup")
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")
HANG_LIMIT = int(os.environ.get("A2N_HANG_LIMIT", "1200"))

T = "AccessibleTrader.Core/Services/Trading/"
S = "AccessibleTrader.Core/Services/Scripting/"

MUTANTS = [
    # ══ TRADING ═══════════════════════════════════════════════════════════════

    # ── OrderPlacement: the vocabulary every order announcement is read from ──
    ("T01", "trading", T + "OrderPlacement.cs",
     "                return new OrderPlacement(OrderOutcome.Duplicate, s, null,",
     "                return new OrderPlacement(OrderOutcome.Placed, s, null,",
     "a suppressed duplicate is announced as placed — the user believes the second order went"),

    ("T02", "trading", T + "OrderPlacement.cs",
     "            if (s.StartsWith(OrderCodes.Uncertain, StringComparison.OrdinalIgnoreCase))",
     "            if (false)",
     "ORDER_UNCERTAIN falls to the reserved-prefix refusal: 'Not placed' for an order that is "
     "probably live — how the same position gets opened twice"),

    ("T03", "trading", T + "OrderPlacement.cs",
     "        public bool Succeeded => Outcome is OrderOutcome.Placed or OrderOutcome.Accepted;",
     "        public bool Succeeded => Outcome is OrderOutcome.Placed or OrderOutcome.Accepted or OrderOutcome.Uncertain;",
     "an uncertain order counts as a clean success, so its 'verify before retrying' is never spoken"),

    ("T05", "trading", T + "OrderPlacement.cs",
     "                return new OrderPlacement(OrderOutcome.Unavailable, s, null,",
     "                return new OrderPlacement(OrderOutcome.Accepted, s, null,",
     "PROVIDER_NOT_* (the provider cannot trade) is announced as accepted"),

    # ── QuickTradeExecutor: the event → the order ────────────────────────────
    ("T08", "trading", T + "QuickTradeExecutor.cs",
     "                    Side: e.IsLong ? OrderSide.Buy : OrderSide.Sell,",
     "                    Side: e.IsLong ? OrderSide.Sell : OrderSide.Buy,",
     "a quick long is sent as a sell"),

    ("T09", "trading", T + "QuickTradeExecutor.cs",
     "                    Type: e.EntryPrice.HasValue ? OrderType.Limit : OrderType.Market,",
     "                    Type: OrderType.Market,",
     "shift+enter's limit at the cursor goes to market at whatever price"),

    ("T10", "trading", T + "QuickTradeExecutor.cs",
     "                    StopLoss: e.StopPrice);",
     "                    StopLoss: null);",
     "the stop the whole size was computed from is never sent — an unprotected position"),

    ("T11", "trading", T + "QuickTradeExecutor.cs",
     "                if (!placement.Succeeded)",
     "                if (false)",
     "a refused quick trade is dropped on the floor after 'sent' was spoken — the original defect"),

    # ── QuickTradeService: arming, stop inference, placing ───────────────────
    ("T13", "trading", T + "QuickTradeService.cs",
     "            bool isLong = (double)bar.Low < entry;",
     "            bool isLong = (double)bar.Low > entry;",
     "a stop below the price arms a SHORT — the direction inference inverted"),

    ("T15", "trading", T + "QuickTradeService.cs",
     "                entry = (double)bar.Close;",
     "                entry = State.EntryPrice ?? 0;",
     "a limit order is priced at the last close instead of the bar under the cursor"),

    ("T16", "trading", T + "QuickTradeService.cs",
     "                EntryPrice: market ? null : entry,",
     "                EntryPrice: entry,",
     "control+enter (market) is sent as a limit at the last close"),

    ("T17", "trading", T + "QuickTradeService.cs",
     "            Disarm(announce: false);\n        }",
     "        }",
     "the arming survives placing — a second press of enter places the same trade again"),

    ("T18", "trading", T + "QuickTradeArming.cs",
     "                ? Math.Abs(EntryPrice.Value - StopPrice.Value)",
     "                ? EntryPrice.Value - StopPrice.Value",
     "a short's stop distance is negative, so risk sizing refuses every short"),

    ("T19", "trading", T + "QuickTradeArming.cs",
     "        public double RiskCash => AccountEquity * RiskPercent / 100.0;",
     "        public double RiskCash => AccountEquity * RiskPercent / 10.0;",
     "1% armed spends 10% — a tenfold position"),

    ("T20", "trading", T + "QuickTradeService.cs",
     "                        if (State.Stage == QuickTradeStage.Idle) Arm(riskPercent);",
     "                        Arm(riskPercent);",
     "a late balance re-arms over a trade the user has already set a stop for"),

    # ── QuickTradeEquity: whose money sizes the trade ────────────────────────
    ("T22", "trading", T + "QuickTradeEquity.cs",
     "            return _byUser.GetOrAdd(userKey, _ => new QuickTradeEquity());",
     "            return _byUser.GetOrAdd(\"anon\", _ => new QuickTradeEquity());",
     "hosted users share one equity — a trade is sized from another user's balance"),

    ("T23", "trading", T + "QuickTradeEquity.cs",
     "                if (!IsCashAsset(asset) || !double.IsFinite(amount) || amount < 0) continue;",
     "                if (!double.IsFinite(amount) || amount < 0) continue;",
     "a coin count (50,000 DOGE) is read as 50,000 dollars of equity"),

    # ── PortfolioValuation ───────────────────────────────────────────────────
    ("T24", "trading", T + "PortfolioValuation.cs",
     "        public bool IsComplete => PricedCount == TotalCount;",
     "        public bool IsComplete => PricedCount > 0;",
     "a portfolio with unpriced holdings is summarised as complete — the missing assets unsaid"),

    # ── WithdrawalService: the one path that moves funds off the venue ───────
    ("T26", "trading", T + "WithdrawalService.cs",
     "            if (!string.Equals(typedConfirmation?.Trim(), ConfirmationPhrase, StringComparison.Ordinal))",
     "            if (typedConfirmation is null)",
     "any typed text (or an empty field) confirms a withdrawal"),

    ("T27", "trading", T + "WithdrawalService.cs",
     "            if (!_released)\n                return (null, \"withdrawals are not enabled in this build\", NoLease.Instance);",
     "            if (false)\n                return (null, \"withdrawals are not enabled in this build\", NoLease.Instance);",
     "the choke-point release gate is gone — a caller that skips CanWithdraw reaches the venue"),

    ("T28", "trading", T + "WithdrawalService.cs",
     "                return (p, null, new CredentialLease(raw, _keys, _logger, provider));",
     "                return (p, null, NoLease.Instance);",
     "the withdrawal-enabled key stays configured and signs every later order"),

    # ── WalletService: deposit addresses ─────────────────────────────────────
    ("T32", "trading", T + "WalletService.cs",
     "                if (!check.IsDisplayable)",
     "                if (false)",
     "a malformed deposit address from the venue is displayed — funds sent to it are gone"),

    ("T33", "trading", T + "WalletService.cs",
     "            if (!string.IsNullOrWhiteSpace(a.Address.Memo))",
     "            if (false)",
     "a deposit that REQUIRES a memo never says so — funds lost without it"),

    # ── MarketDataPriceSource / exits / validator / fill ─────────────────────
    ("T36", "trading", T + "ManagedExitRules.cs",
     "            Math.Min(remainingQuantity, initialQuantity * portion);",
     "            initialQuantity * portion;",
     "a ladder rung closes more than remains — the exit flips the position"),

    # ══ SCRIPTING ═════════════════════════════════════════════════════════════

    # ── SandboxPolicy: refuse unless explicitly overridden ───────────────────
    ("S01", "scripting", S + "SandboxPolicy.cs",
     "           && (value.Equals(\"1\", StringComparison.Ordinal)\n            || value.Equals(\"true\", StringComparison.OrdinalIgnoreCase));",
     "           && (value.Length > 0);",
     "ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=0 (or =false) turns the sandbox OFF"),

    ("S02", "scripting", S + "SandboxPolicy.cs",
     "        if (!overrideAllowed)\n            throw new ScriptSandboxUnavailableException(details, remedy);",
     "        if (false)\n            throw new ScriptSandboxUnavailableException(details, remedy);",
     "a missing OS sandbox never refuses — the silent downgrade the policy exists to prevent"),

    ("S03", "scripting", S + "SandboxPolicy.cs",
     "        log?.Record(new AccessibleTrader.Sdk.Services.SecurityEvent(\n            DateTime.UtcNow,\n            AccessibleTrader.Sdk.Services.SecurityEventKind.UnsandboxedScriptOverride,",
     "        if (DateTime.MinValue.Year > 1) log?.Record(new AccessibleTrader.Sdk.Services.SecurityEvent(\n            DateTime.UtcNow,\n            AccessibleTrader.Sdk.Services.SecurityEventKind.UnsandboxedScriptOverride,",
     "running unsandboxed under the override leaves no security event — the override happens invisibly"),

    # ── LinuxBwrapLauncher: the policy decision ──────────────────────────────
    ("S04", "scripting", S + "LinuxBwrapLauncher.cs",
     "                _allowUnsandboxed ?? SandboxPolicy.AllowUnsandboxedFallback,",
     "                _allowUnsandboxed ?? true,",
     "the PRODUCTION constructor (no seam) downgrades silently when bwrap is missing"),

    ("S05", "scripting", S + "LinuxBwrapLauncher.cs",
     "            SandboxPolicy.RecordUnsandboxedFallback(nameof(LinuxBwrapLauncher), \"bwrap not found\");\n            SandboxApplied = false;",
     "            SandboxPolicy.RecordUnsandboxedFallback(nameof(LinuxBwrapLauncher), \"bwrap not found\");\n            SandboxApplied = true;",
     "an unsandboxed worker reports itself sandboxed"),

    # ── LinuxBwrapLauncher: the argv ─────────────────────────────────────────
    ("S06", "scripting", S + "LinuxBwrapLauncher.cs",
     "            \"--unshare-all\",        // no network (and new pid/ipc/uts/cgroup ns)\n",
     "",
     "the worker gets the host's network namespace — a script can phone home"),

    ("S07", "scripting", S + "LinuxBwrapLauncher.cs",
     "        args.Add(\"--clearenv\");",
     "",
     "the worker inherits the host environment block (credentials on some machines)"),

    ("S08", "scripting", S + "LinuxBwrapLauncher.cs",
     "        args.AddRange(new[] { \"--ro-bind\", \"/\", \"/\" });   // whole filesystem read-only",
     "        args.AddRange(new[] { \"--bind\", \"/\", \"/\" });",
     "the whole filesystem is WRITABLE from inside the sandbox"),

    ("S09", "scripting", S + "LinuxBwrapLauncher.cs",
     "            \"--die-with-parent\",    // worker exits if the host dies\n",
     "",
     "a sandbox outlives a crashed host"),

    ("S12", "scripting", S + "LinuxBwrapLauncher.cs",
     "                if (IsUnder(path, home!))",
     "                if (!IsUnder(path, home!))",
     "re-binds the paths OUTSIDE the home and leaves the worker/runtime hidden under the tmpfs"),

    # ── OutOfProcessScriptHost: timeouts, quotas, cap ────────────────────────
    ("S14", "scripting", S + "OutOfProcessScriptHost.cs",
     "        if (newCount > cap)",
     "        if (false)",
     "the concurrent-worker cap never refuses — 100 compiles spawn 100 processes"),

    ("S15", "scripting", S + "OutOfProcessScriptHost.cs",
     "        timeoutCts.CancelAfter(timeout);\n\n        await _ioGate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);\n        try\n        {\n            await FrameCodec.WriteFrameAsync(_stdin, send, payload, timeoutCts.Token).ConfigureAwait(false);",
     "\n        await _ioGate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);\n        try\n        {\n            await FrameCodec.WriteFrameAsync(_stdin, send, payload, timeoutCts.Token).ConfigureAwait(false);",
     "a hung Calculate/OnBar NEVER times out — the script timeout never fires"),

    ("S16", "scripting", S + "OutOfProcessScriptHost.cs",
     "            try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); } catch { }\n            RecordSecurityEvent(",
     "            RecordSecurityEvent(",
     "a timed-out worker is left running, holding its slot and spinning a core"),

    ("S18", "scripting", S + "OutOfProcessScriptHost.cs",
     "                if (ws > 0 && ws > _maxWorkingSet)",
     "                if (ws > 0 && ws > _maxWorkingSet * 1000)",
     "the memory quota never trips"),

    ("S19", "scripting", S + "OutOfProcessScriptHost.cs",
     "                        if (fraction > _maxCpuFraction)",
     "                        if (fraction > _maxCpuFraction + 100)",
     "the CPU quota never trips"),

    ("S20", "scripting", S + "OutOfProcessScriptHost.cs",
     "                    default:\n                        throw new InvalidDataException(\n                            $\"script worker {_scriptId} sent unexpected opcode 0x{(byte)opcode:X2} during {operation}\");",
     "                    default:\n                        continue;",
     "a protocol violation mid-call is silently skipped instead of refused"),

    ("S21", "scripting", S + "OutOfProcessScriptHost.cs",
     "        Interlocked.Decrement(ref _activeWorkerCount);\n    }\n}",
     "    }\n}",
     "disposing a worker never gives its slot back — after 16, scripting is dead for the session"),

    # ── OutOfProcessStrategy: the history a script strategy decides on ───────
    ("S23", "scripting", S + "OutOfProcessStrategy.cs",
     "                         && count >= _sentBarCount\n                         && firstTicks == _sentFirstBarTicks;",
     "                         && count >= _sentBarCount;",
     "a scrollback prepend is sent as an append — the strategy decides on a spliced history"),

    # ── the conservative defaults ────────────────────────────────────────────
    ("S26", "scripting", S + "IScriptWorkerLauncher.cs",
     "    bool SandboxApplied => false;",
     "    bool SandboxApplied => true;",
     "the unsandboxed DefaultProcessLauncher claims to be sandboxed"),

    ("S27", "scripting", S + "RefusingScriptWorkerLauncher.cs",
     "        => throw new ScriptingNotSupportedOnPlatformException(_platform);",
     "        => new DefaultProcessLauncher().Launch(workerExecutablePath);",
     "iOS/macCatalyst run the worker unsandboxed instead of refusing"),

    # ── platform launchers (unreachable on Linux; measured honestly) ─────────
    ("S28", "scripting", S + "MacSandboxExecLauncher.cs",
     "            SandboxPolicy.EnforceOrThrow(\n                SandboxPolicy.AllowUnsandboxedFallback,\n                details: missing,",
     "            SandboxPolicy.EnforceOrThrow(\n                true,\n                details: missing,",
     "macOS: a missing sandbox-exec/profile silently downgrades"),

]


def run(cmd, cwd=REPO, timeout=3600):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    code, out = run("nice -n 10 dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test():
    """Full suite. Killed as a process GROUP after HANG_LIMIT, so a mutant that removes a
    timeout cannot stall the campaign (and cannot leave a testhost behind)."""
    p = subprocess.Popen("nice -n 10 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                         "-p:UseRazorSourceGenerator=false --no-build",
                         cwd=REPO, shell=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                         text=True, start_new_session=True)
    try:
        out, _ = p.communicate(timeout=HANG_LIMIT)
        return p.returncode, out or "", False
    except subprocess.TimeoutExpired:
        try:
            os.killpg(p.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        out, _ = p.communicate()
        return -9, out or "", True


def backup_path(relpath):
    return os.path.join(BACKUP, relpath.replace("/", "__"))


def snapshot_all():
    os.makedirs(BACKUP, exist_ok=True)
    for rel in sorted({m[2] for m in MUTANTS}):
        dst = backup_path(rel)
        if not os.path.exists(dst):
            shutil.copyfile(os.path.join(REPO, rel), dst)


def restore(rel):
    shutil.copyfile(backup_path(rel), os.path.join(REPO, rel))
    os.utime(os.path.join(REPO, rel), None)   # rule 1: newer than the sabotaged build output


def recover_inflight():
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    if rel:
        restore(rel)
        print(f"recovered stale sabotage in {rel} from the file copy", flush=True)
    os.remove(INFLIGHT)


def verify_restored():
    ok = True
    for rel in sorted({m[2] for m in MUTANTS}):
        a = open(os.path.join(REPO, rel), 'rb').read()
        b = open(backup_path(rel), 'rb').read()
        if a != b:
            print(f"NOT RESTORED: {rel}"); ok = False
    print("restored byte-identical" if ok else "TREE NOT RESTORED")
    return ok


def verify():
    ok = True
    ids = [m[0] for m in MUTANTS]
    if len(ids) != len(set(ids)):
        print("DUPLICATE IDS"); ok = False
    for mid, area, relpath, find, repl, _ in MUTANTS:
        path = os.path.join(REPO, relpath)
        if not os.path.exists(path):
            print(f"{mid}: MISSING FILE {relpath}"); ok = False; continue
        src = open(path, 'rb').read().decode('utf-8')
        n = src.count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences (need exactly 1) in {relpath}\n    {find[:120]!r}")
            ok = False
        if find == repl:
            print(f"{mid}: EQUIVALENT — find == replace"); ok = False
    print(f"all {len(MUTANTS)} anchors unique" if ok else "ANCHOR CHECK FAILED")
    return ok


def main():
    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)
    if not verify():
        sys.exit(1)

    snapshot_all()
    recover_inflight()
    only = [a for a in sys.argv[1:] if re.match(r"^[TS]\d\d$", a)] or None
    results = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in results}

    for mid, area, relpath, find, repl, breaks in MUTANTS:
        if (only and mid not in only) or mid in done:
            continue
        path = os.path.join(REPO, relpath)
        raw = open(path, 'rb').read()
        original = raw.decode('utf-8')
        rec = {'id': mid, 'area': area, 'file': relpath, 'breaks': breaks,
               'occurrences': original.count(find)}
        if rec['occurrences'] != 1:
            rec['status'] = 'BAD_ANCHOR'
            results.append(rec); json.dump(results, open(OUT, 'w'), indent=1)
            print(f"{mid}: BAD ANCHOR — {relpath}", flush=True); continue
        t0 = time.time()
        try:
            open(INFLIGHT, 'w').write(relpath)
            with open(path, 'wb') as fh:
                fh.write(original.replace(find, repl).encode('utf-8'))
            ok, log = build()
            if not ok:
                rec['status'] = 'NO_COMPILE'; rec['log'] = log[-1500:]
                print(f"{mid}: DID NOT COMPILE — {area}\n{log[-800:]}", flush=True)
            else:
                code, out, hung = test()
                m = SUMMARY_RE.search(out)
                rec['failed'] = int(m.group(1)) if m else -1
                rec['passed'] = int(m.group(2)) if m else -1
                names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                rec['failing_tests'] = names[:40]
                if hung:
                    rec['status'] = 'HANG'
                    rec['tail'] = out[-2000:]
                else:
                    rec['status'] = ('UNPARSED' if rec['failed'] < 0
                                     else 'CAUGHT' if rec['failed'] > 0 else 'SURVIVED')
                    if rec['status'] == 'UNPARSED':
                        rec['tail'] = out[-2000:]
                print(f"{mid}: {rec['status']} failed={rec['failed']} ({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            restore(relpath)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec); json.dump(results, open(OUT, 'w'), indent=1)

    build()
    code, out, hung = test()
    m = SUMMARY_RE.search(out)
    print(f"\n=== CONTROL (nothing sabotaged): {m.group(0) if m else 'UNPARSED'}"
          f"{' HUNG' if hung else ''}", flush=True)
    names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
    if names:
        print("    control failures: " + "; ".join(names), flush=True)
    verify_restored()

    print("\n=== summary")
    for area in ("trading", "scripting"):
        rs = [r for r in results if r['area'] == area]
        c = sum(1 for r in rs if r['status'] == 'CAUGHT')
        s = [r['id'] for r in rs if r['status'] == 'SURVIVED']
        h = [r['id'] for r in rs if r['status'] == 'HANG']
        other = [(r['id'], r['status']) for r in rs if r['status'] not in ('CAUGHT', 'SURVIVED', 'HANG')]
        tot = c + len(s)
        print(f"  {area}: caught {c}/{tot}" + (f" = {100*c/tot:.1f}%" if tot else "")
              + f"  survivors {s}  hangs {h}  other {other}")


if __name__ == '__main__':
    main()
