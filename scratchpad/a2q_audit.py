#!/usr/bin/env python3
"""A2q — false-catch audit of every campaign CATCH.

For each caught mutant, in a tree holding exactly 522e02d4 (QuietDesktopTests 5/5 first):
  1. apply the mutant ALONE, build, run ONLY the tests that failed in the campaign (C# or browser,
     whichever layer caught it) — they must fail again;
  2. restore (byte copy + touch), rebuild, run the same tests clean — they must pass.
A test that passes with the mutant, or fails clean, is a FLAKE and the catch becomes a survivor.
N06's only catch was one browser theory row; it is run REPEATS times each way, because one row
of a 25-row Tab-trap theory failing once is exactly what a flake looks like.

The filter is the test METHOD (theory rows are not filterable by display name), and the verdict
is read off the exact display names that failed in the campaign.
"""
import json, os, queue, re, shutil, sys, threading
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import a2q_sabotage as S
from a2q_mutants import MUTANTS

OUT = os.path.join(S.REPO, "scratchpad", "a2q_audit_results.json")
BY_ID = {m[0]: m for m in MUTANTS}
REPEATS = {"N06": 3}


def method_filter(names):
    meths = sorted({re.sub(r"\(.*$", "", n) for n in names})
    return "|".join(f"FullyQualifiedName~{m}" for m in meths)


def run_filtered(t, filt, proj):
    out = S.test(t, filt, proj=proj)[1]
    p = S.parse(out)
    return p, out


def main():
    results = json.load(open(os.path.join(S.REPO, "scratchpad", "a2q_sabotage_results.json")))
    catches = [r for r in results if r['status'] == 'CAUGHT']
    for i in range(1, S.N_TREES + 1):
        t = S.tree_path(i)
        for rel in {m[2] for m in MUTANTS}:
            assert open(os.path.join(t, rel), 'rb').read() == open(os.path.join(S.SOURCE, rel), 'rb').read(), (i, rel)
        ok, log = S.build(t); assert ok, log[-1500:]
        okb, log = S.build(t, S.BR_PROJ); assert okb, log[-1500:]
        q = S.parse(S.test(t, S.QUIET_FILTER)[1])
        assert q['failed'] == 0 and q['passed'] == S.QUIET_EXPECTED and not q['no_match'], (i, q)
        print(f"t{i}: 522e02d4, QuietDesktopTests {q['passed']}/{q['total']}", flush=True)

    qq = queue.Queue()
    for r in catches:
        qq.put(r)
    out_recs, lock = [], threading.Lock()

    def worker(i):
        t = S.tree_path(i)
        while True:
            try:
                r = qq.get_nowait()
            except queue.Empty:
                return
            browser = r.get('layer') == 'browser'
            layer = r['browser'] if browser else r['csharp']
            names = layer['failing']
            proj = S.BR_PROJ if browser else S.CS_PROJ
            filt = method_filter(names)
            mid, _, rel, find, repl, _ = BY_ID[r['id']]
            reps = REPEATS.get(r['id'], 1)
            rec = {'id': mid, 'layer': r.get('layer'), 'campaign_failing': names, 'filter': filt,
                   'campaign_messages': layer.get('messages'), 'mutant_runs': [], 'clean_runs': []}
            path = os.path.join(t, rel)
            os.makedirs(S.backup_dir(i), exist_ok=True)
            shutil.copyfile(path, S.bpath(i, rel))
            src = open(path, encoding='utf-8', newline='').read()
            assert src.count(find) == 1
            try:
                open(path, 'w', encoding='utf-8', newline='').write(src.replace(find, repl))
                ok, log = S.build(t, proj); assert ok, log[-1500:]
                for _ in range(reps):
                    p, out = run_filtered(t, filt, proj)
                    rec['mutant_runs'].append({'failed': p['failed'], 'passed': p['passed'], 'no_match': p['no_match'],
                                               'failing': p['failing'], 'messages': p['messages']})
            finally:
                shutil.copyfile(S.bpath(i, rel), path); os.utime(path, None)
            ok, log = S.build(t, proj); assert ok, log[-1500:]
            if browser: S.build(t)   # keep the C# test output in step with the restored source
            for _ in range(reps):
                p, out = run_filtered(t, filt, proj)
                rec['clean_runs'].append({'failed': p['failed'], 'passed': p['passed'], 'no_match': p['no_match'],
                                          'failing': p['failing'], 'messages': p['messages']})
            red_again = [n for n in names if all(n in m['failing'] for m in rec['mutant_runs'])]
            green_clean = all(m['failed'] == 0 and m['passed'] > 0 and not m['no_match'] for m in rec['clean_runs'])
            any_match = all(not m['no_match'] for m in rec['mutant_runs'])
            rec['verdict'] = 'HONEST' if (red_again and green_clean and any_match) else 'FLAKE'
            rec['red_again'] = red_again
            with lock:
                out_recs.append(rec)
                json.dump(sorted(out_recs, key=lambda x: x['id']), open(OUT, 'w'), indent=1)
            print(f"{mid}: {rec['verdict']}  mutant runs failed={[m['failed'] for m in rec['mutant_runs']]} "
                  f"clean runs failed={[m['failed'] for m in rec['clean_runs']]}  red again: {len(red_again)}/{len(names)}",
                  flush=True)

    ths = [threading.Thread(target=worker, args=(i,)) for i in range(1, S.N_TREES + 1)]
    for th in ths: th.start()
    for th in ths: th.join()

    same = all(open(os.path.join(S.tree_path(i), rel), 'rb').read() == open(os.path.join(S.SOURCE, rel), 'rb').read()
               for i in range(1, S.N_TREES + 1) for rel in {m[2] for m in MUTANTS})
    print("restored byte-identical" if same else "TREE NOT RESTORED", flush=True)


if __name__ == '__main__':
    main()
