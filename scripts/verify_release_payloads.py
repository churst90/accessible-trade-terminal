#!/usr/bin/env python3
"""
Assert that every built release asset actually CONTAINS what it is supposed to contain.

    python3 scripts/verify_release_payloads.py release/

WHY THIS EXISTS, in one paragraph. v2.12.0 was the release that found four runtime payloads
staged into $(OutDir) and never into the publish. The MSBuild fix was correct. But two of the
four are third-party binaries that live outside the repository, every csproj item is
Exists()-guarded so a build without them succeeds in silence, and the release is built by
GitHub Actions — which did not have them. The result was a green run, a published release, and
two headline claims in WHATSNEW that were untrue of every zip a user could download: NVDA-direct
speech was absent, so the chart was still silent for an NVDA user, and the Dot Pad SDK was
absent, so tactile output was still inert. Nobody found that by building. It was found by
reading the published assets.

PublishStagingParityTests reads the csproj as XML and verifies the RULES. Whether the file will
exist on the machine that runs the release is a different question, and no test that reads this
repository can answer it. This script answers it, against the bytes, inside the release job,
before anything is published.

The list of payloads is NOT in this file. It is packaging/release-payloads.json, which
ReleasePayloadManifestTests reads too, so the build rules and the artifact check cannot drift
apart.

Exit status is 0 when every asset present satisfies its rules, 1 otherwise. An asset that was
not built at all is reported and skipped rather than failed: release.yml publishes whatever
heads succeeded, so a missing macOS zip is a failed macOS job's business, not this script's. A
zip that IS present and is missing a payload is always a failure.

One rule reports without blocking, marked `"severity": "warn"` in the manifest: the ReadyToRun
check. Every other rule names a payload without which a feature is silently dead; that one is
about how fast the app starts, and a corrected release must not be held hostage to an unrelated
performance regression. It prints as an advisory and as a GitHub ::warning::.
"""

from __future__ import annotations

import fnmatch
import json
import struct
import sys
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
MANIFEST = REPO_ROOT / "packaging" / "release-payloads.json"


# ──────────────────────────────────────────────────────────────────────────────
# ReadyToRun detection
# ──────────────────────────────────────────────────────────────────────────────
def is_ready_to_run(data: bytes) -> bool | None:
    """
    True when this PE carries precompiled native code, False when it is IL only, None when the
    bytes are not a managed PE we can read.

    The tell is the CLI header's ManagedNativeHeader directory (offset 64, 8 bytes): crossgen
    points it at the R2R header, and a plain IL assembly leaves it zeroed. That one field is the
    whole difference between the 2.11.0 Windows app and the 2.12.0 one.
    """
    try:
        if data[:2] != b"MZ":
            return None
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        if data[pe:pe + 4] != b"PE\0\0":
            return None
        n_sections = struct.unpack_from("<H", data, pe + 6)[0]
        opt_size = struct.unpack_from("<H", data, pe + 20)[0]
        magic = struct.unpack_from("<H", data, pe + 24)[0]
        pe32_plus = magic == 0x20B
        dd = pe + 24 + (112 if pe32_plus else 96)

        # Data directory 14 is the CLI header.
        cli_rva, cli_size = struct.unpack_from("<II", data, dd + 14 * 8)
        if not cli_rva:
            return None  # native, not managed

        sections = []
        so = pe + 24 + opt_size
        for i in range(n_sections):
            vsize, vaddr, rsize, raddr = struct.unpack_from("<IIII", data, so + i * 40 + 8)
            sections.append((vaddr, max(vsize, rsize), raddr))

        def to_offset(rva: int) -> int | None:
            for vaddr, size, raddr in sections:
                if vaddr <= rva < vaddr + size:
                    return raddr + (rva - vaddr)
            return None

        off = to_offset(cli_rva)
        if off is None:
            return None
        # CLI header: +64 is ManagedNativeHeader (RVA, size).
        native_rva, native_size = struct.unpack_from("<II", data, off + 64)
        return native_size > 0
    except Exception:
        return None


# ──────────────────────────────────────────────────────────────────────────────
# Matching
# ──────────────────────────────────────────────────────────────────────────────
def entries_under_root(names: list[str], root: str) -> list[str] | None:
    """
    Paths inside the asset, relative to the spec's root.

    `root` is "" for a flat zip and a glob such as "*.app/Contents/MonoBundle/" for the macOS
    bundle, whose top-level directory name is the app's and not ours to hard-code. Returns None
    when the root matches nothing at all — that is itself a failure worth naming, because it
    means the asset is not shaped the way the spec thinks it is.
    """
    if not root:
        return names

    prefixes = set()
    for n in names:
        parts = n.split("/")
        for i in range(1, len(parts) + 1):
            candidate = "/".join(parts[:i]) + "/"
            if fnmatch.fnmatch(candidate, root):
                prefixes.add(candidate)
    if not prefixes:
        return None

    out = []
    for p in prefixes:
        out.extend(n[len(p):] for n in names if n.startswith(p) and len(n) > len(p))
    return out


def check_asset(path: Path, spec: dict) -> tuple[list[str], list[str]]:
    """
    Returns (failures, advisories). A failure blocks the release; an advisory is printed and
    does not.

    The distinction exists because one rule is genuinely different in kind. Every `required`
    entry names a payload without which a feature is silently dead — a mute chart, an inert
    Braille tab, a market dropdown with no providers. The ReadyToRun check is about how fast
    the app starts. Both are worth knowing; only one is worth refusing to ship over, and
    conflating them would mean a corrected release blocked by an unrelated regression.
    """
    failures: list[str] = []
    advisories: list[str] = []
    with zipfile.ZipFile(path) as zf:
        raw_names = [i.filename for i in zf.infolist() if not i.is_dir()]
        names = entries_under_root(raw_names, spec.get("root", ""))
        if names is None:
            return ([
                f"nothing inside the asset matches the expected root '{spec['root']}'. "
                f"The asset is not shaped the way packaging/release-payloads.json expects, so "
                f"no payload could be checked at all. Top-level entries: "
                f"{sorted({n.split('/')[0] for n in raw_names})[:6]}"
            ], [])

        lower = {n.lower(): n for n in names}

        for rule in spec["required"]:
            why = rule["why"]

            if "file" in rule:
                if rule["file"].lower() not in lower:
                    failures.append(f"MISSING {rule['file']} — {why}")

            elif "anyOf" in rule:
                if not any(c.lower() in lower for c in rule["anyOf"]):
                    failures.append(
                        f"MISSING all of {', '.join(rule['anyOf'])} (any one would do) — {why}")

            elif "glob" in rule:
                hits = [n for n in names if fnmatch.fnmatch(n, rule["glob"])]
                need = rule.get("min", 1)
                if len(hits) < need:
                    failures.append(
                        f"FOUND {len(hits)} entries matching {rule['glob']}, expected at least "
                        f"{need} — {why}")

            else:
                failures.append(f"malformed rule in the manifest: {rule!r}")

        r2r = spec.get("readyToRun")
        if r2r:
            sink = advisories if r2r.get("severity") == "warn" else failures
            for asm in r2r["assemblies"]:
                real = lower.get(asm.lower())
                if real is None:
                    # Its absence is a separate matter; only judge what is there.
                    continue
                # The entry name inside the zip may carry the root prefix we stripped.
                member = next(n for n in raw_names if n.endswith(real))
                verdict = is_ready_to_run(zf.read(member))
                if verdict is None:
                    sink.append(f"{asm} could not be read as a managed PE")
                elif not verdict:
                    sink.append(
                        f"{asm} is IL-only — the publish is not ReadyToRun. "
                        + " ".join(w for w in r2r["why"] if w))

    return failures, advisories


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__)
        return 2

    directory = Path(argv[1])
    if not directory.is_dir():
        print(f"::error::{directory} is not a directory")
        return 1

    manifest = json.loads(MANIFEST.read_text())
    assets = sorted(p for p in directory.glob("*.zip"))
    print(f"Verifying {len(assets)} asset(s) in {directory} against {MANIFEST.name}\n")

    failed = False
    checked = 0

    for spec in manifest["assets"]:
        matches = [a for a in assets if fnmatch.fnmatch(a.name, spec["match"])]
        if not matches:
            print(f"-- {spec['id']}: no asset matching {spec['match']} was built; skipping.")
            print(f"   (If {spec['head']} was meant to ship, its job failed — look there.)\n")
            continue

        for asset in matches:
            checked += 1
            problems, advisories = check_asset(asset, spec)

            if problems:
                failed = True
                print(f"FAIL {asset.name}  [{spec['head']}]")
                for p in problems:
                    print(f"     - {p}")
                    print(f"::error file={asset.name}::{p}")
            else:
                n = len(spec["required"]) + len(spec.get("readyToRun", {}).get("assemblies", []))
                print(f"ok   {asset.name}  [{spec['head']}] — {n} checks")

            for a in advisories:
                print(f"     ~ (advisory) {a}")
                print(f"::warning file={asset.name}::{a}")
            if problems or advisories:
                print()

    print()
    if failed:
        print("::error::A published asset would have been missing a runtime payload.")
        print("Nothing has been released. Fix the build, do not relax this check: every rule "
              "here exists because a user downloaded a build without it and the feature was "
              "silently inert.")
        return 1

    if checked == 0:
        print("::error::No assets matched any spec — nothing was verified.")
        return 1

    print(f"All {checked} built asset(s) carry their runtime payloads.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
