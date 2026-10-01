#!/usr/bin/env python3
"""A2q — restore every tree file the campaign ever backed up (byte copy + touch), and report.

Used after the 2026-10-01 pause, when the campaign process was killed mid-mutant (SIGKILL, so its
own `finally` restore never ran). Compares each tree file against its byte backup, restores any
that differ, then byte-compares each against the committed source (87a4c257), exported to
~/.cache/a2q-trees/head_blobs/ beforehand with `git show`.
"""
import os, shutil

ROOT = "/home/cody/.cache/a2q-trees"
HEAD_DIR = "/home/cody/.cache/a2q-trees/head_blobs"   # outside the repo: .cs copies under scratchpad would be seen by source scans

ok = True
for i in (1, 2, 3):
    bdir, t = f"{ROOT}/backup{i}", f"{ROOT}/t{i}"
    for b in sorted(os.listdir(bdir)):
        rel = b.replace("__", "/")
        path = os.path.join(t, rel)
        backup = open(os.path.join(bdir, b), 'rb').read()
        if open(path, 'rb').read() != backup:
            print(f"t{i}: {rel} was MUTATED -> restoring from backup")
            shutil.copyfile(os.path.join(bdir, b), path)
            os.utime(path, None)
        head_blob = os.path.join(HEAD_DIR, b)
        if os.path.exists(head_blob):
            same = open(path, 'rb').read() == open(head_blob, 'rb').read()
            if not same:
                ok = False
                print(f"t{i}: {rel} DIFFERS from 87a4c257")
        else:
            ok = False
            print(f"t{i}: no exported 87a4c257 blob for {rel}")
print("restored byte-identical" if ok else "NOT RESTORED")
