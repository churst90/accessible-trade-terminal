#!/usr/bin/env python3
"""A2 — per-test-method classification.

For every [Fact]/[Theory] method, decide:
  * does it (directly, or via a helper defined in the same file) read production
    SOURCE TEXT rather than run production code?
  * are all of its assertions textual (Contains / DoesNotContain / Matches)?

A method that is both is a grep guard: it can only ever tell you a string is
present somewhere in a file, never that the code path runs.
"""
import os, re, json, collections, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from test_census import mask, TEST_ATTR, METHOD, body_at, ROOT

READS_SOURCE = re.compile(
    r'File\.ReadAllText|File\.ReadAllLines|Directory\.GetFiles|Directory\.EnumerateFiles')
TEXTUAL = {'Contains', 'DoesNotContain', 'Matches', 'DoesNotMatch'}
ASSERT = re.compile(r'\bAssert\.(\w+)')
CALL = re.compile(r'\b([A-Z]\w+)\s*\(')


def members(masked, raw):
    """All method-ish members in the file: name -> body (masked)."""
    out = {}
    for mm in METHOD.finditer(masked):
        try:
            open_idx = masked.index('{', mm.end() - 1)
        except ValueError:
            continue
        end = body_at(masked, open_idx)
        out.setdefault(mm.group(1), []).append(masked[open_idx:end + 1])
    # also expression-bodied / field initialisers that read source
    return out


def main():
    files = []
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj')]
        for f in filenames:
            if f.endswith('.cs'):
                files.append(os.path.join(dirpath, f))

    grep_guards = []
    scan_backed = []
    total = 0
    for path in sorted(files):
        raw = open(path, encoding='utf-8-sig').read()
        m = mask(raw)
        rel = os.path.relpath(path, ROOT)
        file_reads_source = bool(READS_SOURCE.search(m))
        mem = members(m, raw)
        # fields/properties whose declaration line mentions a source read
        source_fields = set()
        for line in m.splitlines():
            if READS_SOURCE.search(line):
                fm = re.search(r'\b(\w+)\s*(?:\([^)]*\))?\s*(?:=>|=)', line)
                if fm:
                    source_fields.add(fm.group(1))
        # helper methods (any arity) that return source text
        for name, bodies in mem.items():
            if any(READS_SOURCE.search(b) for b in bodies):
                source_fields.add(name)
        for am in TEST_ATTR.finditer(m):
            mm = METHOD.search(m, am.end())
            if not mm:
                continue
            try:
                open_idx = m.index('{', mm.end() - 1)
            except ValueError:
                continue
            end = body_at(m, open_idx)
            body = m[open_idx:end + 1]
            total += 1
            # transitive (1 hop) source read through same-file helpers
            reads = bool(READS_SOURCE.search(body))
            if not reads and file_reads_source:
                for callee in set(CALL.findall(body)):
                    for b in mem.get(callee, []):
                        if READS_SOURCE.search(b):
                            reads = True
                            break
                    if reads:
                        break
                # class-level fields/properties whose initialiser reads source
                if not reads:
                    for fname in source_fields:
                        if re.search(r'\b' + re.escape(fname) + r'\b', body):
                            reads = True
                            break
            if not reads:
                continue
            kinds = ASSERT.findall(body)
            rec = {'file': rel, 'name': mm.group(1),
                   'line': raw[:mm.start()].count('\n') + 1,
                   'asserts': collections.Counter(kinds)}
            scan_backed.append(rec)
            if kinds and set(kinds) <= TEXTUAL:
                grep_guards.append(rec)

    print(f"total test methods:                      {total}")
    print(f"methods that read production SOURCE TEXT: {len(scan_backed)}")
    print(f"  ...and assert ONLY on that text:        {len(grep_guards)}")
    print()
    by_file = collections.Counter(r['file'] for r in grep_guards)
    for f, c in by_file.most_common(40):
        print(f"  {c:>3}  {f}")
    json.dump({'grep_guards': [{k: (dict(v) if isinstance(v, collections.Counter) else v)
                                for k, v in r.items()} for r in grep_guards],
               'scan_backed_count': len(scan_backed), 'total': total},
              open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'a2_permethod.json'), 'w'), indent=1)


if __name__ == '__main__':
    main()
