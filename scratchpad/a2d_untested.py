#!/usr/bin/env python3
"""A2d — which production AREAS are never named by any test project.

The 2026-08-25 version of this (a2_untested_types.py) covered Core/Sdk/WebHost and
the two script projects, and counted the test project only. This one adds the
plugin tree and the StrategyLab (both are ProjectReferenced by the test project, so
a mutant in either is genuinely in scope), and counts BOTH test projects —
AccessibleTrader.Tests and AccessibleTrader.BrowserTests.

What it measures, stated plainly so the number is not over-read: a type whose NAME
appears nowhere in either test project cannot have a test deliberately about it. It
can still be executed — dragged along by a test aimed at something else — so this
is a floor on "untested", not a coverage figure. It is the coarse, independent
signal coverage cannot give: nobody wrote a test *about* this.

Output is grouped by directory, because the question being answered is "which
AREAS have no tests", not "which types".
"""
import os, re, json, collections, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from test_census import mask

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
PROD = ["AccessibleTrader.Core", "AccessibleTrader.Sdk", "AccessibleTrader.WebHost",
        "AccessibleTrader.BlazorClient.Components", "AccessibleTrader.BlazorClient",
        "AccessibleTrader.ScriptSandbox", "AccessibleTrader.ScriptWorker",
        "AccessibleTrader.StrategyLab", "Plugins"]
TEST_PROJECTS = ["AccessibleTrader.Tests", "AccessibleTrader.BrowserTests"]

DECL = re.compile(r'\b(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*'
                  r'(class|record|struct|interface|enum)\s+(\w+)')


def main():
    test_words = set()
    for proj in TEST_PROJECTS:
        base = os.path.join(REPO, proj)
        if not os.path.isdir(base):
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj')]
            for f in filenames:
                if f.endswith(('.cs', '.razor')):
                    txt = open(os.path.join(dirpath, f), encoding='utf-8-sig', errors='replace').read()
                    test_words.update(re.findall(r'\b\w+\b', txt))

    by_dir = collections.defaultdict(lambda: {'total': 0, 'unnamed': []})
    for proj in PROD:
        base = os.path.join(REPO, proj)
        if not os.path.isdir(base):
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj', '.vs')]
            for f in filenames:
                if not f.endswith('.cs') or f.endswith(('.g.cs', '.Designer.cs')):
                    continue
                path = os.path.join(dirpath, f)
                rel = os.path.relpath(path, REPO)
                area = os.path.dirname(rel)
                src = mask(open(path, encoding='utf-8-sig', errors='replace').read())
                for kind, name in DECL.findall(src):
                    by_dir[area]['total'] += 1
                    if name not in test_words:
                        by_dir[area]['unnamed'].append((kind, name, rel))

    rows = []
    for area, d in by_dir.items():
        t, u = d['total'], len(d['unnamed'])
        rows.append((u, t, area, d['unnamed']))
    rows.sort(key=lambda r: (-r[0], r[2]))

    print(f"{'unnamed/total':>14}  area")
    for u, t, area, _ in rows:
        if u == 0:
            continue
        print(f"{u:>6}/{t:<7}  {area}")
    gt = sum(r[1] for r in rows)
    gu = sum(r[0] for r in rows)
    print(f"\nTOTAL {gt - gu}/{gt} named by a test  ({100.0 * gu / gt:.0f}% of declared types never named)")
    print(f"areas with ZERO named types: "
          f"{sum(1 for u, t, a, _ in rows if u == t)} of {len(rows)}")

    json.dump({a: {'total': t, 'unnamed': un} for u, t, a, un in rows},
              open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                'a2d_untested.json'), 'w'), indent=1)


if __name__ == '__main__':
    main()
