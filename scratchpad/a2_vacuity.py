#!/usr/bin/env python3
"""A2 — vacuity census.

Two shapes, both recorded in this repo's own history as having produced green
tests that guarded nothing:

  (1) EMPTY-BASELINE GUARD. The test discovers a set (files on disk, types in an
      assembly), filters it for offenders, and asserts the offender list is
      empty. If discovery returns nothing the assertion passes for the wrong
      reason. The fix is a vacuity check — assert the DISCOVERED set is
      non-empty first. This counts how many such tests have one.

  (2) SELF-MIRRORING. The expected value is computed by calling the same
      production symbol the assertion is testing, so the test restates the
      implementation instead of pinning the behaviour.
"""
import os, re, json, collections, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from test_census import mask, TEST_ATTR, METHOD, body_at, ROOT

DISCOVERY = re.compile(
    r'Directory\.(?:GetFiles|EnumerateFiles)|Assembly\.\w+|GetTypes\(\)|DefinedTypes'
    r'|GetExportedTypes|typeof\(\w+\)\.Assembly')
EMPTY = re.compile(r'Assert\.Empty\s*\(')
VACUITY = re.compile(r'Assert\.(?:NotEmpty|True)\s*\(|Assert\.(?:Equal|InRange)\s*\(\s*\d+')


def iter_tests():
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj')]
        for f in sorted(filenames):
            if not f.endswith('.cs'):
                continue
            path = os.path.join(dirpath, f)
            raw = open(path, encoding='utf-8-sig').read()
            m = mask(raw)
            seen = set()
            for am in TEST_ATTR.finditer(m):
                mm = METHOD.search(m, am.end())
                if not mm:
                    continue
                try:
                    open_idx = m.index('{', mm.end() - 1)
                except ValueError:
                    continue
                end = body_at(m, open_idx)
                key = (mm.group(1), open_idx)
                if key in seen:
                    continue
                seen.add(key)
                yield (os.path.relpath(path, ROOT), mm.group(1),
                       raw[:mm.start()].count('\n') + 1, m[open_idx:end + 1], m)


def main():
    total = 0
    discovery_empty = []
    no_vacuity_check = []
    for rel, name, line, body, filetext in iter_tests():
        total += 1
        if EMPTY.search(body) and (DISCOVERY.search(body) or DISCOVERY.search(filetext)):
            discovery_empty.append((rel, name, line))
            if not VACUITY.search(body):
                no_vacuity_check.append((rel, name, line))

    print(f"total test methods:                                   {total}")
    print(f"discovery-driven tests that assert Assert.Empty:       {len(discovery_empty)}")
    print(f"  ...with NO vacuity check in the same method:         {len(no_vacuity_check)}")
    print()
    by_file = collections.Counter(r for r, _, _ in no_vacuity_check)
    for f, c in by_file.most_common(30):
        print(f"  {c:>3}  {f}")
    json.dump({'discovery_empty': discovery_empty, 'no_vacuity_check': no_vacuity_check},
              open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'a2_vacuity.json'), 'w'), indent=1)


if __name__ == '__main__':
    main()
