#!/usr/bin/env python3
"""A2 — which test methods never assert anything, even through a helper.

A test with no assertion is a smoke test: it only proves the code did not
throw. That is a legitimate thing to want (several here say so in a comment),
but it is not what a green tick implies, so the count is worth knowing.
"""
import os, re, json, collections, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from test_census import mask, TEST_ATTR, METHOD, body_at, ROOT

ASSERTISH = re.compile(r'\bAssert\.\w+|\.Received\b|\.DidNotReceive\b|Assert\w+\s*\(')
CALL = re.compile(r'\b(\w+)\s*\(')


def main():
    rows = []
    total = 0
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj')]
        for f in sorted(filenames):
            if not f.endswith('.cs'):
                continue
            path = os.path.join(dirpath, f)
            raw = open(path, encoding='utf-8-sig').read()
            m = mask(raw)
            rel = os.path.relpath(path, ROOT)

            # every member body in the file, by name
            mem = collections.defaultdict(list)
            for mm in METHOD.finditer(m):
                try:
                    oi = m.index('{', mm.end() - 1)
                except ValueError:
                    continue
                mem[mm.group(1)].append(m[oi:body_at(m, oi) + 1])

            def asserts_transitively(body, depth=0, seen=frozenset()):
                if ASSERTISH.search(body):
                    return True
                if depth >= 3:
                    return False
                for callee in set(CALL.findall(body)) - seen:
                    for b in mem.get(callee, []):
                        if asserts_transitively(b, depth + 1, seen | {callee}):
                            return True
                return False

            done = set()
            for am in TEST_ATTR.finditer(m):
                mm = METHOD.search(m, am.end())
                if not mm:
                    continue
                try:
                    oi = m.index('{', mm.end() - 1)
                except ValueError:
                    continue
                if oi in done:
                    continue
                done.add(oi)
                total += 1
                body = m[oi:body_at(m, oi) + 1]
                if not asserts_transitively(body):
                    rows.append({'file': rel, 'name': mm.group(1),
                                 'line': raw[:mm.start()].count('\n') + 1})

    print(f"total test methods: {total}")
    print(f"no assertion, even through a helper (3 hops): {len(rows)}")
    for r in rows:
        print(f"  {r['file']}:{r['line']}  {r['name']}")
    json.dump(rows, open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                      'a2_noassert.json'), 'w'), indent=1)


if __name__ == '__main__':
    main()
