#!/usr/bin/env python3
"""A2b — the 2026-08-28 RE-MEASURE of the mutation catch rate.

A2 measured 61% on 2026-08-26 against a 4,830-test suite. The suite is 5,754
now — roughly 900 tests later — and that number is what the production-readiness
grade turns on. This re-runs the SAME campaign so the two numbers compare.

Method is deliberately identical to `a2_sabotage.py`: apply one single-line
regression to production code, build, run the FULL suite, record whether
anything went red AND WHICH TESTS DID, revert. A mutant counts as CAUGHT only
if some test anywhere fails. Same commands, same configuration (Debug, the
default), same driver — only the mutant anchors changed, and only where they
had to.

RE-ANCHORING, and it is the honest part of this run:

  25 of the 28 anchors still matched the tree byte for byte and are used
  verbatim. Three did not, because the code they targeted was REWRITTEN by the
  work that happened in between. Each is re-anchored to the nearest equivalent
  edit in the current code, and the substitution is recorded here rather than
  buried:

  M21  B1 replaced the bare-string order protocol with the typed
       `OrderPlacement`, so `GeneralOrderService`'s two-clause prefix test is
       gone. The same two-clause test now lives in `OrderPlacement.Parse`'s
       reserved-prefix arm, and the mutant drops the `ProviderPrefix` clause
       from it — literally the same edit on the line that inherited the job.
       NOTE THE ASYMMETRY: the new code is defended in depth. A `PROVIDER_NOT*`
       sentinel is matched by an earlier branch and survives this mutation, so
       only the non-NOT_* codes fall through to "an order id — it went". The
       mutant is therefore STRICTLY HARDER TO CATCH than A2's was, which is a
       point in the refactor's favour and a caveat on the comparison.

  M27  The backtest cost model was rewritten in the HIGH pass (a reversal is
       two fills, and the entry commission used to be skipped entirely). The
       line that now computes the entry charge is `entryCommission`.

  M28  Same rewrite: entry slippage moved into `WithSlippage`. The mutant flips
       its sign, which is A2's edit — an entry that pays down to buy and up to
       sell, i.e. slippage in the trader's favour.

Carry A2's own trap in: FIVE mutants came back falsely CAUGHT by one unrelated
flaky test firing alone, which is the entire difference between the naive 79%
and the true 61%. Every single-test catch here is re-run in isolation by
`a2b_disambiguate.py` before it is counted.
"""
import json, os, re, subprocess, sys, time, importlib.util

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2b_sabotage_results.json")

# Import A2's list so the 25 unchanged mutants are provably the same bytes.
_spec = importlib.util.spec_from_file_location(
    "a2", os.path.join(REPO, "scratchpad", "a2_sabotage.py"))
_a2 = importlib.util.module_from_spec(_spec)
_saved_argv, sys.argv = sys.argv, ["a2_import_only", "--none"]
_spec.loader.exec_module(_a2)
sys.argv = _saved_argv

# (id, area, file, find, replace, what-it-breaks) — replacements for the three
# mutants whose target code no longer exists in the form A2 mutated it.
REANCHORED = {
    "M21": ("M21", "order outcome classification",
            "AccessibleTrader.Core/Services/Trading/OrderPlacement.cs",
            "            if (s.StartsWith(OrderCodes.OrderPrefix, StringComparison.Ordinal)\n"
            "             || s.StartsWith(OrderCodes.ProviderPrefix, StringComparison.Ordinal))",
            "            if (s.StartsWith(OrderCodes.OrderPrefix, StringComparison.Ordinal))",
            "a PROVIDER_* failure sentinel is treated as an order id"),

    "M27": ("M27", "backtest cost model",
            "AccessibleTrader.Core/Strategies/StrategyBacktester.cs",
            "                double entryCommission = fillPrice * qty * config.CommissionRate;",
            "                double entryCommission = 0;",
            "entry commission is never charged"),

    "M28": ("M28", "backtest cost model",
            "AccessibleTrader.Core/Strategies/StrategyBacktester.cs",
            "        return side == OrderSide.Buy ? price + slip : price - slip;",
            "        return side == OrderSide.Buy ? price - slip : price + slip;",
            "slippage is applied in the trader's favour on every entry"),
}

MUTANTS = [REANCHORED.get(m[0], m) for m in _a2.MUTANTS]


def run(cmd, timeout=3600):
    return subprocess.run(cmd, shell=True, cwd=REPO, capture_output=True, text=True, timeout=timeout)


def build():
    r = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
            "-p:UseRazorSourceGenerator=false -v:q --nologo")
    return r.returncode == 0, r.stdout[-4000:] + r.stderr[-2000:]


def test():
    r = run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
            "-p:UseRazorSourceGenerator=false --no-build --nologo")
    return r.returncode, r.stdout


SUMMARY_RE = re.compile(r'Failed:\s*(\d+),\s*Passed:\s*(\d+)')


def main():
    only = sys.argv[1:] or None
    results = []
    if os.path.exists(OUT):
        results = json.load(open(OUT))
    done = {r['id'] for r in results}

    for mid, area, relpath, find, repl, breaks in MUTANTS:
        if only and mid not in only:
            continue
        if mid in done:
            continue
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        n = original.count(find)
        rec = {'id': mid, 'area': area, 'file': relpath, 'breaks': breaks,
               'occurrences': n, 'reanchored': mid in REANCHORED}
        if n != 1:
            rec['status'] = 'BAD_ANCHOR'
            results.append(rec)
            json.dump(results, open(OUT, 'w'), indent=1)
            print(f"{mid}: BAD ANCHOR ({n} occurrences) — {relpath}", flush=True)
            continue
        t0 = time.time()
        try:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['status'] = 'NO_COMPILE'
                rec['log'] = log[-1500:]
                print(f"{mid}: DID NOT COMPILE", flush=True)
            else:
                code, out = test()
                m = SUMMARY_RE.search(out)
                rec['failed'] = int(m.group(1)) if m else -1
                rec['passed'] = int(m.group(2)) if m else -1
                names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                rec['failing_tests'] = names[:40]
                rec['status'] = 'CAUGHT' if rec['failed'] > 0 else 'SURVIVED'
                print(f"{mid}: {rec['status']} failed={rec['failed']} "
                      f"({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            # Restore, then TOUCH — MSBuild keeps the sabotaged binary otherwise.
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    build()
    print("\n=== summary")
    for r in results:
        print(f"  {r['id']} {r['status']:>10}  {r['area']}")


if __name__ == '__main__':
    main()
