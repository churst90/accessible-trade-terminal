#!/usr/bin/env python3
"""Two censuses on EventBus subscriptions:
  (a) LEAK  — the IDisposable returned by Subscribe is discarded (never stored/disposed).
  (b) THROW — the handler body has no try/catch, so it can propagate into Publish."""
import os, re, collections
ROOT = "/home/cody/external-rescue/Github/accessible-trade-terminal"
SKIP = {"bin", "obj", ".git", "node_modules", "dist", "publish"}

files = []
for dp, dn, fn in os.walk(ROOT):
    dn[:] = [d for d in dn if d not in SKIP]
    for f in fn:
        if f.endswith(('.cs', '.razor')):
            files.append(os.path.join(dp, f))

SUB = re.compile(r'(?P<pre>[^\n]*?)\.(?P<kind>Subscribe|SubscribeCoalesced|SubscribeSampled)<\s*(?P<t>[\w\.]+)\s*>\s*\(')

def match_paren(s, i):
    d = 0
    while i < len(s):
        if s[i] == '(': d += 1
        elif s[i] == ')':
            d -= 1
            if d == 0: return i
        i += 1
    return -1

leaks, guarded, unguarded = [], [], []
for path in files:
    rel = os.path.relpath(path, ROOT)
    if rel.startswith('AccessibleTrader.Tests/'): continue
    try: src = open(path, encoding='utf-8-sig').read()
    except Exception: continue
    for m in SUB.finditer(src):
        open_paren = src.index('(', m.end() - 1)
        close = match_paren(src, open_paren)
        if close < 0: continue
        body = src[open_paren:close + 1]
        line = src.count('\n', 0, m.start()) + 1
        # Everything on the line before the '.Subscribe' token.
        line_start = src.rfind('\n', 0, m.start('kind')) + 1
        pre = src[line_start:m.start('kind') - 1].rstrip()
        # The disposable is kept if the call is assigned, returned, added to a
        # collection, or passed as an argument.
        # Strip the receiver chain ("_subs.Add(bus" -> "_subs.Add(") so the token
        # immediately before the receiver decides whether the result is kept.
        stem = re.sub(r'[\w\.]+$', '', pre).rstrip()
        captured = bool(re.search(r'(=|\breturn\b|\(|,|\{)$', stem)) or stem.endswith('=>')
        entry = (rel, line, m.group('t'))
        if not captured: leaks.append(entry)
        # Handler guarded?
        if 'try' in body and 'catch' in body:
            guarded.append(entry)
        else:
            # a method-group handler (e.g. Subscribe<T>(OnFoo)) — resolve the method body
            mg = re.fullmatch(r'\(\s*([A-Za-z_]\w*)\s*\)', body.strip())
            if mg:
                name = mg.group(1)
                mm = re.search(r'\b(?:private|public|internal|protected)[^\n]*\b' + re.escape(name) + r'\s*\([^)]*\)\s*\{', src)
                if mm:
                    mb_open = src.index('{', mm.end() - 1)
                    mb_close = match_paren(src.replace('{', '(').replace('}', ')'), mb_open)
                    mbody = src[mb_open:mb_close + 1] if mb_close > 0 else ''
                    if 'try' in mbody and 'catch' in mbody:
                        guarded.append(entry); continue
            unguarded.append(entry)

print(f"TOTAL production Subscribe sites: {len(leaks)+0} leaked / {len(guarded)+len(unguarded)} total")
print(f"  handler GUARDED   (try/catch): {len(guarded)}")
print(f"  handler UNGUARDED (can throw): {len(unguarded)}")
print(f"  IDisposable DISCARDED (leak) : {len(leaks)}")
print("\n-- unguarded, by event type (top 20) --")
for t, n in collections.Counter(e[2] for e in unguarded).most_common(20):
    print(f"  {n:3}  {t}")
print("\n-- discarded-IDisposable sites (top 25) --")
for f, l, t in leaks[:25]:
    print(f"  {f}:{l}  <{t}>")
