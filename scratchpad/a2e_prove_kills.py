#!/usr/bin/env python3
"""Re-apply each A2e survivor's mutant and prove the NEW test goes red.

A fix for a survivor that has not been proven red is a guess. This re-applies the
exact mutant text from a2e_sabotage.py and runs the whole suite; the mutant is
PROVED_RED only if the new test for it is among the failures, not merely if
something failed.

E14 and E25 are NOT here. Both are equivalent mutants — see a2e_survivors.md:
  * E14 removes a whitespace guard that Trim() already subsumes.
  * E25 changes an unreachable defensive return (IPAddress is always v4 or v6).
Writing a test for either would be writing a test that cannot fail on the real
defect, which is the pathology this whole exercise exists to find.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2e_prove_kills_results.json")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from a2e_sabotage import MUTANTS, build, test, SUMMARY_RE   # same text, one source

# survivor id -> the test that must fail when its mutant is re-applied
EXPECTED = {
    "E03": "F1ReachesHelpFromInsideAnOpenDialog",
    "E04": "TheZeroKeyReadsTheNeutralOfTheCOMPONENTUnderTheCursor",
    "E09": "TheNEARESTCrossingWins",
    "E10": "AContinuousLineWithNoCrossingRuleSaysSo",
    "E17": "RepeatIfStillActiveWaitsForTheCooldownItWasGiven",
    "E24": "AnUnconfiguredChannelIsNotAskedToDeliver",
    "E27": "A_name_answering_with_one_public_and_one_private_address_is_refused",
}

BY_ID = {m[0]: m for m in MUTANTS}


def main():
    results = []
    for mid, want in EXPECTED.items():
        _, area, relpath, find, repl, breaks = BY_ID[mid]
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        if original.count(find) != 1:
            print(f"{mid}: BAD ANCHOR — cannot prove", flush=True)
            results.append({'id': mid, 'status': 'BAD_ANCHOR'})
            continue
        t0 = time.time()
        try:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec = {'id': mid, 'status': 'NO_COMPILE'}
            else:
                code, out = test()
                names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                hit = [n for n in names if want in n]
                m = SUMMARY_RE.search(out)
                rec = {
                    'id': mid, 'expected_test': want,
                    'failed': int(m.group(1)) if m else -1,
                    'failing_tests': names[:20],
                    'status': 'PROVED_RED' if hit else 'NOT_PROVED',
                }
            print(f"{mid}: {rec['status']} ({time.time()-t0:.0f}s) — {area}", flush=True)
            if rec.get('failing_tests'):
                print("      " + "; ".join(rec['failing_tests'][:5]), flush=True)
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    build()
    print("\n=== summary")
    for r in results:
        print(f"  {r['id']} {r['status']}")
    bad = [r['id'] for r in results if r['status'] != 'PROVED_RED']
    print("\nALL PROVED" if not bad else f"\nNOT PROVED: {bad}")


if __name__ == '__main__':
    main()
