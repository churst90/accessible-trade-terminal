#!/usr/bin/env python3
"""For every event type Published on the EventBus, find who Subscribes to it.
An event published with no subscriber is a message sent into a void."""
import os, re, collections, json

ROOT = "/home/cody/external-rescue/Github/accessible-trade-terminal"
SKIP = {"bin", "obj", ".git", "node_modules", "dist", "publish"}

pub = collections.defaultdict(list)   # type -> [(file,line)]
sub = collections.defaultdict(list)
obs = collections.defaultdict(list)   # AsObservable<T>/SubscribeCoalesced/Sampled

PUB_RE = re.compile(r'\.Publish(?:<[^>]*>)?\s*\(\s*new\s+([A-Za-z_][\w\.]*)\s*[\(\{]')
PUB_GEN_RE = re.compile(r'\.Publish<\s*([A-Za-z_][\w\.]*)\s*>')
PUB_VAR_RE = re.compile(r'\.Publish\s*\(\s*([a-z_]\w*)\s*\)')
SUB_RE = re.compile(r'\.Subscribe<\s*([A-Za-z_][\w\.]*)\s*>')
OBS_RE = re.compile(r'\.(?:AsObservable|SubscribeCoalesced|SubscribeSampled)<\s*([A-Za-z_][\w\.]*)\s*>')

files = []
for dp, dn, fn in os.walk(ROOT):
    dn[:] = [d for d in dn if d not in SKIP]
    for f in fn:
        if f.endswith(('.cs', '.razor')):
            files.append(os.path.join(dp, f))

pubvar = collections.defaultdict(list)
for path in files:
    rel = os.path.relpath(path, ROOT)
    try: src = open(path, encoding='utf-8-sig').read()
    except Exception: continue
    for i, line in enumerate(src.splitlines(), 1):
        if line.lstrip().startswith('//'): continue
        for m in PUB_RE.finditer(line):  pub[m.group(1).split('.')[-1]].append((rel, i))
        for m in PUB_GEN_RE.finditer(line): pub[m.group(1).split('.')[-1]].append((rel, i))
        for m in PUB_VAR_RE.finditer(line): pubvar[m.group(1)].append((rel, i))
        for m in SUB_RE.finditer(line):  sub[m.group(1).split('.')[-1]].append((rel, i))
        for m in OBS_RE.finditer(line):  obs[m.group(1).split('.')[-1]].append((rel, i))

def is_test(f): return f.startswith('AccessibleTrader.Tests/')

print("=== events PUBLISHED in production with NO production subscriber ===")
orphans = []
for t, sites in sorted(pub.items()):
    psites = [s for s in sites if not is_test(s[0])]
    if not psites: continue
    consumers = [s for s in sub[t] + obs[t] if not is_test(s[0])]
    if not consumers:
        orphans.append((t, psites))
for t, psites in orphans:
    print(f"\n  {t}   ({len(psites)} publish site(s), 0 subscribers)")
    for f, l in psites[:6]:
        print(f"      {f}:{l}")

print(f"\n  ORPHAN COUNT: {len(orphans)}")

print("\n=== events SUBSCRIBED in production but never PUBLISHED in production ===")
dead = []
for t, sites in sorted(list(sub.items()) + list(obs.items())):
    ssites = [s for s in sites if not is_test(s[0])]
    if not ssites: continue
    producers = [s for s in pub[t] if not is_test(s[0])]
    if not producers:
        dead.append((t, ssites))
seen = set()
for t, ssites in dead:
    if t in seen: continue
    seen.add(t)
    print(f"  {t}  <- {ssites[0][0]}:{ssites[0][1]}  ({len(ssites)} subscriber site(s))")
print(f"\n  NEVER-PUBLISHED COUNT: {len(seen)}")
print("\n  (note: .Publish(variable) sites resolved by name, not type:", len(pubvar), "distinct vars)")
