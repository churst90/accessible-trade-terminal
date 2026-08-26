#!/usr/bin/env python3
"""A2 — classify the source-scanning ("grep guard") tests.

A scan guard never executes the code it guards; it asserts over the TEXT of a
.cs/.razor file. That makes it structurally incapable of noticing whether the
call it found is on the path that runs — the failure mode already recorded for
Polygon (a `body.Contains("TheRightCall")` guard that stayed green while an
earlier catch clause routed around it).

This counts them and splits presence-checks from the rest.
"""
import os, re, json, collections, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from test_census import mask, TEST_ATTR, METHOD, body_at, SKIP, ROOT

READS_SOURCE = re.compile(
    r'File\.ReadAllText|File\.ReadAllLines|Directory\.GetFiles|Directory\.EnumerateFiles'
    r'|ReadSource|SourceText|ReadAllRazor|LoadSource')

# helper indirection: many files read the source once in a helper and the test
# method only calls the helper.
HELPER_HINT = re.compile(r'\b(Source|Src|Text|Body|Markup|Razor|File)\w*\s*\(')


def main():
    files = []
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj')]
        for f in filenames:
            if f.endswith('.cs'):
                files.append(os.path.join(dirpath, f))

    per_file = {}
    total_scan_tests = 0
    presence_only = []
    for path in sorted(files):
        raw = open(path, encoding='utf-8-sig').read()
        m = mask(raw)
        if not READS_SOURCE.search(m):
            continue
        rel = os.path.relpath(path, ROOT)
        tests = []
        for am in TEST_ATTR.finditer(m):
            mm = METHOD.search(m, am.end())
            if not mm:
                continue
            open_idx = m.index('{', mm.end() - 1)
            end = body_at(m, open_idx)
            body = m[open_idx:end + 1]
            tests.append((mm.group(1), body, raw[:mm.start()].count('\n') + 1))
        # file-level: does the whole file only ever assert Contains/DoesNotContain/Matches?
        asserts = re.findall(r'\bAssert\.(\w+)', m)
        kinds = collections.Counter(asserts)
        textual = sum(v for k, v in kinds.items()
                      if k in ('Contains', 'DoesNotContain', 'Matches', 'DoesNotMatch',
                               'True', 'False', 'Empty', 'NotEmpty'))
        per_file[rel] = {'tests': len(tests), 'assert_kinds': dict(kinds),
                         'textual_asserts': textual, 'total_asserts': len(asserts)}
        total_scan_tests += len(tests)
        if len(asserts) and textual == len(asserts):
            presence_only.append(rel)

    print(f"source-scanning test FILES: {len(per_file)}")
    print(f"test methods inside them:   {total_scan_tests}")
    print(f"files whose every assertion is textual: {len(presence_only)}")
    print()
    for rel in sorted(per_file, key=lambda r: -per_file[r]['tests']):
        d = per_file[rel]
        flag = "TEXT-ONLY" if rel in presence_only else "         "
        print(f"  {flag} {d['tests']:>3} tests  {d['textual_asserts']:>3}/{d['total_asserts']:>3} textual  {rel}")

    json.dump({'per_file': per_file, 'presence_only': presence_only},
              open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'a2_scanguards.json'), 'w'), indent=1)


if __name__ == '__main__':
    main()
