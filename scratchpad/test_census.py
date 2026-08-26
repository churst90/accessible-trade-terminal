#!/usr/bin/env python3
"""A2 — census of the test suite itself.

Parses every .cs under AccessibleTrader.Tests with a brace matcher that masks
comments and string literals (grep line-counts were what made the 2026-08-24
censuses ~half wrong), extracts each [Fact]/[Theory] method body, and classifies
what the test actually does.
"""
import os, re, sys, json, collections

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "AccessibleTrader.Tests")
ROOT = os.path.normpath(ROOT)


def mask(src: str) -> str:
    """Replace comment and string-literal content with spaces, preserving offsets."""
    out = list(src)
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == '/' and i + 1 < n and src[i + 1] == '/':
            j = src.find('\n', i)
            j = n if j < 0 else j
            for k in range(i, j):
                out[k] = ' '
            i = j
        elif c == '/' and i + 1 < n and src[i + 1] == '*':
            j = src.find('*/', i + 2)
            j = n if j < 0 else j + 2
            for k in range(i, j):
                if src[k] != '\n':
                    out[k] = ' '
            i = j
        elif c == '"' and i + 2 < n and src[i:i + 3] == '"""':
            # raw string literal
            m = re.match(r'"{3,}', src[i:])
            q = m.group(0)
            j = src.find(q, i + len(q))
            j = n if j < 0 else j + len(q)
            for k in range(i, j):
                if src[k] != '\n':
                    out[k] = ' '
            i = j
        elif c == '"':
            verbatim = i > 0 and src[i - 1] == '@'
            j = i + 1
            while j < n:
                if verbatim:
                    if src[j] == '"':
                        if j + 1 < n and src[j + 1] == '"':
                            j += 2
                            continue
                        j += 1
                        break
                    j += 1
                else:
                    if src[j] == '\\':
                        j += 2
                        continue
                    if src[j] == '"' or src[j] == '\n':
                        j += 1
                        break
                    j += 1
            for k in range(i + 1, min(j, n) - 1):
                if src[k] != '\n':
                    out[k] = ' '
            i = j
        elif c == "'":
            j = i + 1
            while j < n:
                if src[j] == '\\':
                    j += 2
                    continue
                if src[j] == "'":
                    j += 1
                    break
                j += 1
            for k in range(i + 1, max(i + 1, j - 1)):
                out[k] = ' '
            i = j
        else:
            i += 1
    return ''.join(out)


TEST_ATTR = re.compile(r'\[\s*(Fact|Theory)\b', re.I)
SKIP = re.compile(r'\bSkip\s*=')
METHOD = re.compile(r'(?:public|private|internal|protected)[^;{}()]*?\b(\w+)\s*\([^;{}]*\)\s*(?:where[^{]*)?\{')


def body_at(src, open_idx):
    depth = 0
    i = open_idx
    n = len(src)
    while i < n:
        if src[i] == '{':
            depth += 1
        elif src[i] == '}':
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return n - 1


def main():
    files = []
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj')]
        for f in filenames:
            if f.endswith('.cs'):
                files.append(os.path.join(dirpath, f))

    tests = []
    for path in sorted(files):
        raw = open(path, encoding='utf-8-sig').read()
        m = mask(raw)
        for am in TEST_ATTR.finditer(m):
            # attribute block: from '[' to the method's '{'
            start = am.start()
            # find the method signature after the attribute list
            mm = METHOD.search(m, am.end())
            if not mm:
                continue
            attr_block = m[start:mm.start()]
            # ensure no intervening method opened between
            open_idx = m.index('{', mm.end() - 1)
            end = body_at(m, open_idx)
            body_masked = m[open_idx:end + 1]
            body_raw = raw[open_idx:end + 1]
            tests.append({
                'file': os.path.relpath(path, ROOT),
                'name': mm.group(1),
                'kind': am.group(1),
                'skip': bool(SKIP.search(attr_block)),
                'line': raw[:mm.start()].count('\n') + 1,
                'body': body_masked,
                'body_raw': body_raw,
                'attr': attr_block,
            })

    # dedupe by (file, name, line)
    seen = set()
    uniq = []
    for t in tests:
        k = (t['file'], t['name'], t['line'])
        if k in seen:
            continue
        seen.add(k)
        uniq.append(t)
    tests = uniq

    ASSERT = re.compile(r'\bAssert\.(\w+)')
    RECEIVED = re.compile(r'\.Received\b|\.DidNotReceive\b')
    THROWS = re.compile(r'Assert\.(Throws|ThrowsAsync|ThrowsAny)')
    HELPER_ASSERT = re.compile(r'\b(Should\w*|Expect\w*|AssertNo\w*|Verify\w*)\s*\(')

    stats = collections.Counter()
    no_assert = []
    weak_only = []
    scan_tests = []
    empty_baseline = []

    WEAK = {'NotNull', 'True', 'False', 'NotEmpty', 'IsType', 'IsAssignableFrom'}

    for t in tests:
        b = t['body']
        asserts = ASSERT.findall(b)
        t['asserts'] = asserts
        has_rec = bool(RECEIVED.search(b))
        has_helper = bool(HELPER_ASSERT.search(b))
        stats['total'] += 1
        if t['skip']:
            stats['skipped'] += 1
        if not asserts and not has_rec and not has_helper:
            no_assert.append(t)
            stats['no_assert'] += 1
        # source-scanning
        if re.search(r'File\.ReadAllText|File\.ReadAllLines|Directory\.GetFiles|ReadSource|SourceOf|ReadFile', b):
            scan_tests.append(t)
            stats['scan'] += 1
        if asserts and set(asserts) <= {'NotNull'}:
            weak_only.append(t)
            stats['notnull_only'] += 1
        if 'Empty' in asserts:
            empty_baseline.append(t)

    print(f"test methods (Fact/Theory attributes): {stats['total']}")
    print(f"  skipped: {stats['skipped']}")
    print(f"  no assertion of any recognised kind: {stats['no_assert']}")
    print(f"  only Assert.NotNull: {stats['notnull_only']}")
    print(f"  read source/text files (scan tests): {stats['scan']}")
    print(f"  contain an Assert.Empty: {len(empty_baseline)}")

    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'a2_census.json'), 'w') as fh:
        json.dump({
            'no_assert': [{'file': t['file'], 'name': t['name'], 'line': t['line']} for t in no_assert],
            'notnull_only': [{'file': t['file'], 'name': t['name'], 'line': t['line']} for t in weak_only],
            'scan': sorted({t['file'] for t in scan_tests}),
            'skipped': [{'file': t['file'], 'name': t['name'], 'line': t['line']} for t in tests if t['skip']],
            'empty_assert': [{'file': t['file'], 'name': t['name'], 'line': t['line']} for t in empty_baseline],
            'per_file': collections.Counter(t['file'] for t in tests),
        }, fh, indent=1)

    print()
    print("--- no-assertion tests")
    for t in no_assert[:60]:
        print(f"  {t['file']}:{t['line']} {t['name']}")
    print()
    print("--- NotNull-only tests")
    for t in weak_only[:60]:
        print(f"  {t['file']}:{t['line']} {t['name']}")
    print()
    print("--- skipped")
    for t in tests:
        if t['skip']:
            print(f"  {t['file']}:{t['line']} {t['name']}")


if __name__ == '__main__':
    main()
