#!/usr/bin/env python3
"""A2q — mutation campaign over `AccessibleTrader.Core/Services/Accessibility`.

The mutants live in a2q_mutants.py (50 selected from 82 drafted; the reasons for each drop are
there). This file is the harness, adapted from a2p_sabotage.py, with one addition: a mutant the
C# suite does not catch is NOT scored a survivor until the BROWSER suite (real Chromium against
the real WebHost) has also run with it applied, in the same tree.

METHOD. Three independent copies of the tree (rsync, no bin/obj, no .git, empty scratchpad),
each with its own build output, each baselined on BOTH suites first. One worker per copy pulls
mutants off a queue: byte-copy backup, apply, build, run the FULL C# suite, and on a C# survival
build and run the FULL browser suite; record full failing display names and the first error
line; restore from the byte copy and `touch`. Never two mutants in one tree; never a source edit
in a tree while its tests run. Browser hosts bind Kestrel to port 0, so concurrent runs in
different trees cannot collide.

HARNESS RULES (each learned from a run that lied):
  1. Baseline first. A run whose passed+failed is short of the baseline total is ABORTED and
     re-queued, never scored.
  2. Anchor asserted to occur EXACTLY ONCE before patching; NO_COMPILE / BAD_ANCHOR are
     UNVERIFIED, never results.
  3. Restore with a byte copy + touch; never `git checkout --`; byte-compare at the end.
  4. NEVER `-v q` / `--nologo` on `dotnet test`.
  5. Full display names are captured up to the trailing `[duration]`.
  6. A filter that matches nothing is a failure, not a pass.
  7. A CONTROL run (nothing sabotaged) of both suites ends every tree's work.

Usage:
  a2q_sabotage.py --verify          anchors unique in the worktree
  a2q_sabotage.py --setup           create/refresh the tree copies, build and baseline each
  a2q_sabotage.py [C01 A02 ...]     run the campaign (all pending mutants by default)
  a2q_sabotage.py --control         control run of both suites + byte-identity check
"""
import filecmp, json, os, queue, re, shutil, subprocess, sys, threading, time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from a2q_mutants import MUTANTS  # noqa: E402

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2q_sabotage_results.json")
BASELINE_OUT = os.path.join(REPO, "scratchpad", "a2q_tree_baseline.json")
CONTROL_OUT = os.path.join(REPO, "scratchpad", "a2q_control.json")
TREES_ROOT = "/home/cody/.cache/a2q-trees"
BASE_COMMIT = "87a4c257"
N_TREES = 3
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)")
FAILED_LINE_RE = re.compile(r"^\s+Failed (.+?) \[[^\]]*\]\s*$", re.M)

CS_PROJ = "AccessibleTrader.Tests/AccessibleTrader.Tests.csproj"
BR_PROJ = "AccessibleTrader.BrowserTests/AccessibleTrader.BrowserTests.csproj"


def baseline_totals():
    """The worktree baseline (scratchpad/a2q_baseline.json), written after a2q_baseline.sh."""
    b = json.load(open(os.path.join(REPO, "scratchpad", "a2q_baseline.json")))
    return b["csharp_total"], b["browser_total"]


def run(cmd, cwd, timeout=5400):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build(tree, proj=CS_PROJ):
    code, out = run(f"nice -n 5 dotnet build {proj} -p:UseRazorSourceGenerator=false -nr:false -v q --nologo", tree)
    return code == 0, out


def test(tree, filt=None, proj=CS_PROJ):
    cmd = f"nice -n 5 dotnet test {proj} -p:UseRazorSourceGenerator=false --no-build"
    if filt:
        cmd += f' --filter "{filt}"'
    return run(cmd, tree)


def parse(out):
    m = SUMMARY_RE.search(out)
    res = {'failed': -1, 'passed': -1, 'skipped': -1, 'total': -1}
    if m:
        res.update(failed=int(m.group(1)), passed=int(m.group(2)),
                   skipped=int(m.group(3)), total=int(m.group(4)))
    names, msgs = [], {}
    lines = out.splitlines()
    for i, ln in enumerate(lines):
        fm = FAILED_LINE_RE.match(ln)
        if not fm:
            continue
        name = fm.group(1)
        if name not in names:
            names.append(name)
        for j in range(i + 1, min(i + 6, len(lines))):
            if lines[j].strip() == "Error Message:" and j + 1 < len(lines):
                msgs.setdefault(name, lines[j + 1].strip()[:400])
                break
    res['failing'] = names
    res['messages'] = msgs
    res['no_match'] = "No test matches the given testcase filter" in out
    return res


def tree_path(i):
    return os.path.join(TREES_ROOT, f"t{i}")


def backup_dir(i):
    return os.path.join(TREES_ROOT, f"backup{i}")


def bpath(i, rel):
    return os.path.join(backup_dir(i), rel.replace("/", "__"))


def verify(root=REPO):
    ok = True
    for mid, area, rel, find, repl, _ in MUTANTS:
        path = os.path.join(root, rel)
        if not os.path.exists(path):
            print(f"{mid}: MISSING FILE {rel}"); ok = False; continue
        src = open(path, encoding='utf-8', newline='').read()
        n = src.count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences (need exactly 1) in {rel}\n    {find[:120]!r}"); ok = False
        if find == repl:
            print(f"{mid}: find == replace"); ok = False
    ids = [m[0] for m in MUTANTS]
    if len(set(ids)) != len(ids):
        print("DUPLICATE IDS"); ok = False
    print(f"all {len(MUTANTS)} anchors unique in {root}" if ok else "ANCHOR CHECK FAILED")
    return ok


def setup():
    cs_total, br_total = baseline_totals()
    os.makedirs(TREES_ROOT, exist_ok=True)
    results = {}
    lock = threading.Lock()

    def one(i):
        t = tree_path(i)
        os.makedirs(t, exist_ok=True)
        code, out = run(f"rsync -a --delete --exclude bin/ --exclude obj/ --exclude .git "
                        f"--exclude scratchpad/ ./ {t}/", REPO)
        assert code == 0, out
        os.makedirs(os.path.join(t, "scratchpad"), exist_ok=True)
        rec = {}
        ok, log = build(t)
        if not ok:
            with lock: results[i] = {'build': False, 'log': log[-2000:]}
            return
        p = parse(test(t)[1])
        rec['csharp'] = {k: p[k] for k in ('failed', 'passed', 'skipped', 'total', 'failing')}
        ok, log = build(t, BR_PROJ)
        if not ok:
            with lock: results[i] = {**rec, 'browser_build': False, 'log': log[-2000:]}
            return
        p = parse(test(t, proj=BR_PROJ)[1])
        rec['browser'] = {k: p[k] for k in ('failed', 'passed', 'skipped', 'total', 'failing')}
        with lock:
            results[i] = rec
        print(f"tree t{i}: C# {rec['csharp']['failed']}F/{rec['csharp']['passed']}P/{rec['csharp']['total']}  "
              f"browser {rec['browser']['failed']}F/{rec['browser']['passed']}P/{rec['browser']['total']}", flush=True)

    ths = [threading.Thread(target=one, args=(i,)) for i in range(1, N_TREES + 1)]
    for th in ths: th.start()
    for th in ths: th.join()
    json.dump({'worktree_csharp_total': cs_total, 'worktree_browser_total': br_total, 'trees': results},
              open(BASELINE_OUT, 'w'), indent=1)
    good = all('browser' in r and r['csharp']['failed'] == 0 and r['csharp']['total'] == cs_total
               and r['browser']['failed'] == 0 and r['browser']['total'] == br_total
               for r in results.values())
    print("all trees baselined green" if good else "BASELINE PROBLEM", flush=True)
    return good


def score_run(out, baseline_total, filt):
    p = parse(out)
    rec = {k: p[k] for k in ('failed', 'passed', 'skipped', 'total', 'failing', 'messages', 'no_match')}
    if p['failed'] < 0:
        rec['status'] = 'UNPARSED'; rec['log'] = out[-2000:]
    elif filt is None and p['failed'] + p['passed'] < baseline_total:
        rec['status'] = 'ABORTED'; rec['log'] = out[-2000:]
    elif filt is not None and p['no_match']:
        rec['status'] = 'NO_MATCH'
    else:
        rec['status'] = 'CAUGHT' if p['failed'] > 0 else 'SURVIVED'
    return rec


def apply_run_restore(i, rel, find, repl, filt=None, browser_on_survival=True):
    """Apply one mutant in tree i, build, test (C#, then browser if C# missed it), restore."""
    cs_total, br_total = baseline_totals()
    t = tree_path(i)
    path = os.path.join(t, rel)
    os.makedirs(backup_dir(i), exist_ok=True)
    shutil.copyfile(path, bpath(i, rel))
    original = open(path, encoding='utf-8', newline='').read()
    n = original.count(find)
    rec = {'occurrences': n}
    if n != 1:
        rec['status'] = 'BAD_ANCHOR'
        return rec
    try:
        with open(path, 'w', encoding='utf-8', newline='') as fh:
            fh.write(original.replace(find, repl))
        ok, log = build(t)
        if not ok:
            rec['status'] = 'NO_COMPILE'
            rec['log'] = "\n".join(l for l in log.splitlines() if "error" in l)[-1500:]
            return rec
        cs = score_run(test(t, filt)[1], cs_total, filt)
        rec['csharp'] = cs
        if cs['status'] in ('ABORTED', 'UNPARSED', 'NO_MATCH'):
            rec['status'] = cs['status']
            return rec
        if cs['status'] == 'CAUGHT':
            rec['status'] = 'CAUGHT'; rec['layer'] = 'C#'
            return rec
        if not browser_on_survival:
            rec['status'] = 'SURVIVED_CSHARP_ONLY'
            return rec
        ok, log = build(t, BR_PROJ)
        if not ok:
            rec['status'] = 'NO_COMPILE'
            rec['log'] = "browser build: " + "\n".join(l for l in log.splitlines() if "error" in l)[-1500:]
            return rec
        br = score_run(test(t, proj=BR_PROJ)[1], br_total, None)
        rec['browser'] = br
        if br['status'] in ('ABORTED', 'UNPARSED'):
            rec['status'] = br['status']
        elif br['status'] == 'CAUGHT':
            rec['status'] = 'CAUGHT'; rec['layer'] = 'browser'
        else:
            rec['status'] = 'SURVIVED'
        return rec
    finally:
        shutil.copyfile(bpath(i, rel), path)
        os.utime(path, None)


def campaign(only):
    results = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in results if r['status'] in ('CAUGHT', 'SURVIVED', 'NO_COMPILE', 'BAD_ANCHOR')}
    results = [r for r in results if r['id'] in done]
    q = queue.Queue()
    for m in MUTANTS:
        if (only and m[0] not in only) or m[0] in done:
            continue
        q.put((m, 0))
    lock = threading.Lock()
    order = [mm[0] for mm in MUTANTS]

    def worker(i):
        while True:
            try:
                m, attempt = q.get_nowait()
            except queue.Empty:
                return
            mid, area, rel, find, repl, why = m
            t0 = time.time()
            rec = apply_run_restore(i, rel, find, repl)
            rec.update(id=mid, area=area, file=rel, find=find, replace=repl, rationale=why,
                       tree=f"t{i}", seconds=round(time.time() - t0), attempt=attempt)
            if rec['status'] in ('ABORTED', 'UNPARSED') and attempt < 2:
                print(f"{mid}: {rec['status']} in t{i} (attempt {attempt}) — re-queued", flush=True)
                q.put((m, attempt + 1))
                continue
            with lock:
                results.append(rec)
                results.sort(key=lambda r: order.index(r['id']))
                json.dump(results, open(OUT, 'w'), indent=1)
            cs = rec.get('csharp', {})
            print(f"{mid}: {rec['status']} layer={rec.get('layer', '-')} C#failed={cs.get('failed')} "
                  f"({rec['seconds']}s, t{i}) — {area}", flush=True)
            for name in cs.get('failing', [])[:6]:
                print(f"      {name}", flush=True)
            for name in rec.get('browser', {}).get('failing', [])[:6]:
                print(f"      [browser] {name}", flush=True)

    ths = [threading.Thread(target=worker, args=(i,)) for i in range(1, N_TREES + 1)]
    for th in ths: th.start()
    for th in ths: th.join()
    return results


def control_and_restore_check():
    ctrl = {}
    lock = threading.Lock()

    def one(i):
        t = tree_path(i)
        ok, _ = build(t)
        p = parse(test(t)[1])
        okb, _ = build(t, BR_PROJ)
        pb = parse(test(t, proj=BR_PROJ)[1])
        same = True
        for rel in sorted({m[2] for m in MUTANTS}):
            a = os.path.join(t, rel)
            # Against the COMMITTED source (87a4c257), not the worktree: the worktree carries
            # this campaign's production fixes, the trees deliberately do not.
            head = subprocess.run(["git", "-C", REPO, "show", f"{BASE_COMMIT}:{rel}"],
                                  capture_output=True).stdout
            if open(a, 'rb').read() != head:
                same = False; print(f"t{i}: {rel} DIFFERS from {BASE_COMMIT}")
            b = bpath(i, rel)
            if os.path.exists(b) and not filecmp.cmp(a, b, shallow=False):
                same = False; print(f"t{i}: {rel} DIFFERS from its backup")
        with lock:
            ctrl[f"t{i}"] = {'build': ok and okb,
                             'csharp': {k: p[k] for k in ('failed', 'passed', 'total', 'failing')},
                             'browser': {k: pb[k] for k in ('failed', 'passed', 'total', 'failing')},
                             'restored_byte_identical': same}
        print(f"CONTROL t{i}: build_ok={ok and okb} C# {p['failed']}F/{p['passed']}P/{p['total']}  "
              f"browser {pb['failed']}F/{pb['passed']}P/{pb['total']}  "
              f"{'restored byte-identical' if same else 'TREE NOT RESTORED'}", flush=True)

    ths = [threading.Thread(target=one, args=(i,)) for i in range(1, N_TREES + 1)]
    for th in ths: th.start()
    for th in ths: th.join()
    json.dump(ctrl, open(CONTROL_OUT, 'w'), indent=1)
    return ctrl


def summary(results):
    print("\n=== summary")
    for r in results:
        print(f"  {r['id']:>4} {r['status']:>10} {r.get('layer', ''):>8}  {r['area']}")
    caught = sum(1 for r in results if r['status'] == 'CAUGHT')
    surv = [r['id'] for r in results if r['status'] == 'SURVIVED']
    total = caught + len(surv)
    if total:
        print(f"\nraw catch rate {caught}/{total} = {100*caught/total:.1f}%   survivors: {surv}")


def main():
    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)
    if "--setup" in sys.argv:
        assert verify()
        sys.exit(0 if setup() else 1)
    if "--control" in sys.argv:
        control_and_restore_check()
        return
    if "--summary" in sys.argv:
        summary(json.load(open(OUT)))
        return
    assert verify()
    for i in range(1, N_TREES + 1):
        assert verify(tree_path(i)), f"tree t{i} anchors"
    only = [a for a in sys.argv[1:] if not a.startswith("-")] or None
    summary(campaign(only))


if __name__ == '__main__':
    main()
