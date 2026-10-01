#!/usr/bin/env python3
"""A2q — record the three mutants that were in flight when the 2026-10-01 pause killed the run.

INTERRUPTED is never a result: campaign() treats only CAUGHT / SURVIVED / NO_COMPILE / BAD_ANCHOR
as done, so these three are dropped from the results and re-queued when the campaign resumes.
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from a2q_mutants import MUTANTS

P = os.path.join(os.path.dirname(os.path.abspath(__file__)), "a2q_sabotage_results.json")
by = {m[0]: m for m in MUTANTS}
order = [m[0] for m in MUTANTS]
r = json.load(open(P))
r = [x for x in r if x['status'] != 'INTERRUPTED']
for mid, tree in (("C09", "t1"), ("C07", "t2"), ("C10", "t3")):
    m = by[mid]
    r.append({'id': mid, 'area': m[1], 'file': m[2], 'find': m[3], 'replace': m[4], 'rationale': m[5],
              'tree': tree, 'status': 'INTERRUPTED',
              'note': "2026-10-01 ~00:50: run killed mid-mutant on the coordinator's instruction (test "
                      "runs were playing sound on Cody's desktop). Tree file restored from its byte "
                      "backup by a2q_restore_trees.py. Not scored; re-queued on resume."})
r.sort(key=lambda x: order.index(x['id']))
json.dump(r, open(P, 'w'), indent=1)
print([(x['id'], x['status']) for x in r])
