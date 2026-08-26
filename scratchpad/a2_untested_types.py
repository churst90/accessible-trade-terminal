#!/usr/bin/env python3
"""A2 — which production types are never so much as NAMED by the test project.

Coverage tells you which lines ran. This tells you something coarser but
independent: which types nobody wrote a test *about*. A type that never appears
in a test file cannot have a deliberate test; if it shows coverage it is only
being dragged along by a test aimed at something else.
"""
import os, re, json, collections, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from test_census import mask

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
PROD = ["AccessibleTrader.Core", "AccessibleTrader.Sdk", "AccessibleTrader.WebHost",
        "AccessibleTrader.BlazorClient.Components", "AccessibleTrader.ScriptSandbox",
        "AccessibleTrader.ScriptWorker", "AccessibleTrader"]
TESTS = os.path.join(REPO, "AccessibleTrader.Tests")

DECL = re.compile(r'\b(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*'
                  r'(class|record|struct|interface|enum)\s+(\w+)')


def main():
    # every identifier that appears anywhere in the test project
    test_words = set()
    for dirpath, dirnames, filenames in os.walk(TESTS):
        dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj')]
        for f in filenames:
            if f.endswith(('.cs', '.razor')):
                txt = open(os.path.join(dirpath, f), encoding='utf-8-sig', errors='replace').read()
                test_words.update(re.findall(r'\b\w+\b', txt))

    by_project = collections.defaultdict(lambda: {'total': 0, 'unnamed': []})
    for proj in PROD:
        base = os.path.join(REPO, proj)
        if not os.path.isdir(base):
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj', '.vs')]
            for f in filenames:
                if not f.endswith('.cs'):
                    continue
                if f.endswith('.g.cs') or f.endswith('.Designer.cs'):
                    continue
                path = os.path.join(dirpath, f)
                src = mask(open(path, encoding='utf-8-sig', errors='replace').read())
                for kind, name in DECL.findall(src):
                    by_project[proj]['total'] += 1
                    if name not in test_words:
                        by_project[proj]['unnamed'].append(
                            (kind, name, os.path.relpath(path, REPO)))

    grand_t = grand_u = 0
    for proj, d in by_project.items():
        t, u = d['total'], len(d['unnamed'])
        grand_t += t
        grand_u += u
        pct = 100.0 * u / t if t else 0
        print(f"{proj:<45} {t - u:>4}/{t:<4} named by a test   ({pct:.0f}% never named)")
    print(f"{'TOTAL':<45} {grand_t - grand_u:>4}/{grand_t:<4} named   ({100.0*grand_u/grand_t:.0f}% never named)")

    json.dump({p: d['unnamed'] for p, d in by_project.items()},
              open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'a2_untested_types.json'), 'w'),
              indent=1)


if __name__ == '__main__':
    main()
