#!/usr/bin/env python3
"""Block until the campaign log has at least N scored lines (or the campaign has finished),
then print the scored lines and the failing-test lines under them. Usage: a2q_wait.py N [log]"""
import os, re, sys, time
here = os.path.dirname(os.path.abspath(__file__))
n = int(sys.argv[1])
log = os.path.join(here, sys.argv[2] if len(sys.argv) > 2 else "a2q_campaign.log")
pat = re.compile(r"^[A-Z0-9]+: ")
while True:
    text = open(log).read() if os.path.exists(log) else ""
    lines = text.splitlines()
    if sum(1 for l in lines if pat.match(l)) >= n or "=== summary" in text or "restored byte-identical" in text:
        break
    time.sleep(20)
for l in lines:
    if pat.match(l) or l.startswith("      ") or "summary" in l or "rate" in l or "restored" in l or "proved" in l:
        print(l)
