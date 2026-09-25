#!/usr/bin/env python3
"""A2p FALSE-CATCH AUDIT.

For every CAUGHT mutant in a2p_sabotage_results.json, in one of the three tree copies:
  1. MUTANT: re-apply that mutant ALONE, build, and re-run the tests that failed for it in the
     campaign (method-level filter; theory cases are compared by FULL display name).
  2. CLEAN: restore (byte copy + touch), rebuild, and run the SAME filter on the clean tree.
A catch is HONEST iff at least one campaign-failing test fails again under the mutant AND
passes on the clean tree. Otherwise it is a FLAKE_CATCH and the mutant is scored SURVIVED.
A filter that matches nothing is FILTER_MATCHED_NOTHING — a failure of the audit, never a pass.

When a mutant broke more than MAX_METHODS test methods, the first MAX_METHODS (sorted) are
re-run; the result records that it was a sample.

The bookkeeping/proxy judgement (a catch made only by a pinned list, a parity test, a source
spelling scan) is made by reading the honest_by names; it is recorded in the report, not here.
"""
import json, os, queue, sys, threading, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import a2p_sabotage as S

OUT = os.path.join(S.REPO, "scratchpad", "a2p_audit_results.json")
MAX_METHODS = 30


def method_of(display):
    return display.split("(", 1)[0].strip()


def filter_for(names):
    methods = sorted({method_of(n) for n in names})
    sampled = len(methods) > MAX_METHODS
    methods = methods[:MAX_METHODS]
    return "|".join(f"FullyQualifiedName~{m}" for m in methods), methods, sampled


def main():
    only = [a for a in sys.argv[1:] if not a.startswith("-")] or None
    results = json.load(open(S.OUT))
    by_id = {m[0]: m for m in S.MUTANTS}
    caught = [r for r in results if r['status'] == 'CAUGHT' and (not only or r['id'] in only)]
    audit = json.load(open(OUT)) if os.path.exists(OUT) and only else {'mutants': []}
    audit['mutants'] = [a for a in audit['mutants'] if not only or a['id'] not in only]
    q = queue.Queue()
    for r in caught:
        q.put(r)
    lock = threading.Lock()

    def worker(i):
        t = S.tree_path(i)
        while True:
            try:
                r = q.get_nowait()
            except queue.Empty:
                return
            mid = r['id']
            _, area, rel, find, repl, _ = by_id[mid]
            filt, methods, sampled = filter_for(r['failing'])
            rec = {'id': mid, 'area': area, 'campaign_failing': r['failing'],
                   'filter_methods': methods, 'sampled': sampled, 'tree': f"t{i}"}
            m = S.apply_run_restore(i, mid, rel, find, repl, filt=filt)
            rec['mutant_status'] = m['status']
            if m.get('no_match'):
                rec['verdict'] = 'FILTER_MATCHED_NOTHING'
            elif m['status'] not in ('CAUGHT', 'SURVIVED'):
                rec['verdict'] = m['status']
            else:
                rec['mutant_rerun_failing'] = m['failing']
                ok, log = S.build(t)
                assert ok, log[-1500:]
                _, out = S.test(t, filt)
                c = S.parse(out)
                if c['no_match']:
                    rec['verdict'] = 'FILTER_MATCHED_NOTHING'
                else:
                    rec['clean_failing'] = c['failing']
                    rec['clean_passed'] = c['passed']
                    honest = [n for n in m['failing'] if n in r['failing'] and n not in c['failing']]
                    rec['honest_by'] = honest
                    rec['campaign_only'] = [n for n in r['failing']
                                            if method_of(n) in methods and n not in m['failing']]
                    rec['verdict'] = 'HONEST' if honest else 'FLAKE_CATCH'
            with lock:
                audit['mutants'].append(rec)
                audit['mutants'].sort(key=lambda a: [mm[0] for mm in S.MUTANTS].index(a['id']))
                json.dump(audit, open(OUT, 'w'), indent=1)
            print(f"{mid}: {rec['verdict']}  honest_by={len(rec.get('honest_by', []))} "
                  f"clean_failing={rec.get('clean_failing')} campaign_only={rec.get('campaign_only')}", flush=True)

    ths = [threading.Thread(target=worker, args=(i,)) for i in range(1, S.N_TREES + 1)]
    for th in ths: th.start()
    for th in ths: th.join()

    # every tree rebuilt clean and byte-identical to the worktree
    same = True
    for i in range(1, S.N_TREES + 1):
        ok, _ = S.build(S.tree_path(i))
        for rel in sorted({m[2] for m in S.MUTANTS}):
            import filecmp
            if not filecmp.cmp(os.path.join(S.tree_path(i), rel), os.path.join(S.REPO, rel), shallow=False):
                same = False; print(f"t{i}: {rel} NOT RESTORED")
    print("restored byte-identical" if same else "TREE NOT RESTORED", flush=True)
    honest = sum(1 for a in audit['mutants'] if a['verdict'] == 'HONEST')
    print(f"honest (flake audit only): {honest}/{len(audit['mutants'])} caught mutants", flush=True)


if __name__ == '__main__':
    main()
