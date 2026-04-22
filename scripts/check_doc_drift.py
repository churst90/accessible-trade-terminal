#!/usr/bin/env python3
"""
Doc-drift guard.

Asserts three things the README/SHORTCUTS docs claim match reality:

  1. Every SystemCommand default binding in ShortcutManager.InitializeDefaultProfile()
     has its key chord documented somewhere in docs/SHORTCUTS.md.
  2. The plugin count in docs/README.md matches the directory layout in
     Plugins/Providers/ and Plugins/Analytics/.
  3. The test count in docs/README.md matches `dotnet test --list-tests` output.

Run from the repo root:
    python scripts/check_doc_drift.py

Exits non-zero on any drift, printing every finding. Intended to run on every PR.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path


REPO = Path(__file__).resolve().parent.parent
SHORTCUTS_CS = REPO / "AccessibleTrader.Core" / "Services" / "ShortcutManager.cs"
SHORTCUTS_MD = REPO / "docs" / "SHORTCUTS.md"
README_MD = REPO / "docs" / "README.md"
PROVIDERS_DIR = REPO / "Plugins" / "Providers"
ANALYTICS_DIR = REPO / "Plugins" / "Analytics"
TESTS_PROJ = REPO / "AccessibleTrader.Tests" / "AccessibleTrader.Tests.csproj"


# Key normalisation: SystemCommand bindings use raw Win32 virtual-key strings,
# but SHORTCUTS.md reads in English. Each key maps to one or more acceptable
# doc forms — the guard passes if ANY form appears in the doc, since the doc
# mixes conventions ("Left Arrow" for primary rows, plain "Left" when chord-modified).
KEY_TO_DOC: dict[str, tuple[str, ...]] = {
    "LEFT": ("Left Arrow", "Left"),
    "ARROWLEFT": ("Left Arrow", "Left"),
    "RIGHT": ("Right Arrow", "Right"),
    "ARROWRIGHT": ("Right Arrow", "Right"),
    "UP": ("Up Arrow", "Up"),
    "ARROWUP": ("Up Arrow", "Up"),
    "DOWN": ("Down Arrow", "Down"),
    "ARROWDOWN": ("Down Arrow", "Down"),
    "PAGEUP": ("Page Up",),
    "PAGEDOWN": ("Page Down",),
    "HOME": ("Home",),
    "END": ("End",),
    "ESCAPE": ("Escape",),
    "ENTER": ("Enter",),
    "RETURN": ("Enter",),
    "TAB": ("Tab",),
    "SPACE": ("Space",),
    " ": ("Space",),
    "DELETE": ("Delete",),
    "OEM4": ("[",),
    "OEM5": ("Backslash", "\\"),
    "OEM6": ("]",),
    "OEMMINUS": ("-",),
    "OEMPLUS": ("=",),
    "OEMCOMMA": (",",),
    "\\": ("Backslash", "\\"),
}

# Keys whose duplicate alias emission we suppress (we only check the canonical one).
ALIAS_KEYS = {"ARROWLEFT", "ARROWRIGHT", "ARROWUP", "ARROWDOWN", " ", "RETURN"}


def normalise_key(raw: str) -> tuple[str, ...]:
    up = raw.upper() if len(raw) > 1 else raw
    return KEY_TO_DOC.get(up, (raw,))


def build_chord_variants(key: str, ctrl: bool, alt: bool, shift: bool) -> list[str]:
    """Returns every acceptable human form of a chord."""
    mods = []
    if ctrl:  mods.append("Ctrl")
    if alt:   mods.append("Alt")
    if shift: mods.append("Shift")
    variants = []
    for k in normalise_key(key):
        parts = mods + [k]
        variants.append("+".join(parts))
    return variants


BINDING_RE = re.compile(
    r'new\s*\(\s*SystemCommand\.(?P<cmd>\w+)\s*,\s*"(?P<key>[^"]+)"'
    r'(?P<mods>(?:\s*,\s*(?:Shift|Ctrl|Alt)\s*:\s*true)*)',
    re.MULTILINE,
)


def parse_default_bindings() -> list[tuple[str, list[str]]]:
    """Returns [(SystemCommand, [chord variants]), ...] for every default binding."""
    src = SHORTCUTS_CS.read_text(encoding="utf-8")
    # Find the method declaration (not the call site) — the declaration is the
    # "private void InitializeDefaultProfile()" signature.
    decl_re = re.compile(r"private\s+void\s+InitializeDefaultProfile\s*\(\s*\)")
    m = decl_re.search(src)
    if not m:
        raise RuntimeError("Could not find InitializeDefaultProfile declaration in ShortcutManager.cs")
    # Walk until the closing brace of the method.
    depth = 0
    i = src.find("{", m.end())
    method_start = i
    while i < len(src):
        if src[i] == "{": depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                break
        i += 1
    method_src = src[method_start:i]

    bindings = []
    for m in BINDING_RE.finditer(method_src):
        cmd = m.group("cmd")
        key = m.group("key")
        # Unescape common C# string literal escapes — the regex captures the raw source,
        # e.g. C# "\\" (runtime value: one backslash) arrives here as two chars.
        key = key.replace("\\\\", "\\").replace('\\"', '"')
        if key in ALIAS_KEYS:  # skip duplicate-alias entries
            continue
        mods = m.group("mods") or ""
        ctrl = "Ctrl: true" in mods
        alt = "Alt: true" in mods
        shift = "Shift: true" in mods
        bindings.append((cmd, build_chord_variants(key, ctrl, alt, shift)))
    return bindings


def check_shortcut_drift(errors: list[str]) -> None:
    bindings = parse_default_bindings()
    if not bindings:
        errors.append("SHORTCUT GUARD: parsed zero bindings from ShortcutManager.cs — regex is broken.")
        return

    md = SHORTCUTS_MD.read_text(encoding="utf-8")
    md_lower = md.lower()
    missing = []
    for cmd, variants in bindings:
        if any(v.lower() in md_lower for v in variants):
            continue
        # Fallback: accept "Ctrl-Shift-T" style hyphenation too.
        if any(v.lower().replace("+", "-") in md_lower for v in variants):
            continue
        missing.append((cmd, variants[0]))

    if missing:
        errors.append(
            f"SHORTCUT GUARD: {len(missing)} binding(s) in code are NOT documented in docs/SHORTCUTS.md:"
        )
        for cmd, chord in missing:
            errors.append(f"   * {cmd:28s} -> {chord}")


README_PROVIDER_RE = re.compile(
    r"(?P<total>\d+)\s*(?:exchange|data)[^\(]*\((?P<trading>\d+)\s*trading\s*\+\s*(?P<analytics>\d+)\s*analytics\)",
    re.IGNORECASE,
)
README_TEST_RE = re.compile(r"\((?P<n>\d+)\s*tests", re.IGNORECASE)


def check_plugin_counts(errors: list[str]) -> None:
    actual_providers = sum(1 for p in PROVIDERS_DIR.iterdir() if p.is_dir())
    actual_analytics = sum(1 for p in ANALYTICS_DIR.iterdir() if p.is_dir())
    actual_total = actual_providers + actual_analytics

    md = README_MD.read_text(encoding="utf-8")
    m = README_PROVIDER_RE.search(md)
    if not m:
        errors.append("PLUGIN GUARD: could not find '<N> exchange... (<N> trading + <N> analytics)' claim in docs/README.md.")
        return
    claim_total    = int(m.group("total"))
    claim_trading  = int(m.group("trading"))
    claim_analytics = int(m.group("analytics"))
    if (claim_total, claim_trading, claim_analytics) != (actual_total, actual_providers, actual_analytics):
        errors.append(
            f"PLUGIN GUARD: docs/README.md claims {claim_total} plugins "
            f"({claim_trading} trading + {claim_analytics} analytics); "
            f"filesystem has {actual_total} ({actual_providers} trading + {actual_analytics} analytics)."
        )


def check_test_count(errors: list[str]) -> None:
    md = README_MD.read_text(encoding="utf-8")
    m = README_TEST_RE.search(md)
    if not m:
        errors.append("TEST GUARD: could not find '(<N> tests' claim in docs/README.md.")
        return
    claim = int(m.group("n"))

    if os.environ.get("DOC_DRIFT_SKIP_TESTS") == "1":
        print(f"TEST GUARD: skipped (DOC_DRIFT_SKIP_TESTS=1). README claims {claim}.")
        return

    try:
        res = subprocess.run(
            ["dotnet", "test", str(TESTS_PROJ), "--list-tests", "--nologo"],
            capture_output=True, text=True, timeout=600, cwd=REPO,
        )
    except FileNotFoundError:
        errors.append("TEST GUARD: `dotnet` not on PATH; cannot verify test count.")
        return
    if res.returncode != 0:
        errors.append(f"TEST GUARD: `dotnet test --list-tests` failed:\n{res.stderr.strip()}")
        return

    # --list-tests prints "    Namespace.Class.TestName" lines after the header.
    # Any non-blank line starting with whitespace and containing a dot is a test.
    actual = sum(
        1 for ln in res.stdout.splitlines()
        if ln.startswith("    ") and "." in ln and not ln.lstrip().startswith(("The following", "Test run"))
    )
    if actual != claim:
        errors.append(f"TEST GUARD: docs/README.md claims {claim} tests; `dotnet test --list-tests` reports {actual}.")


def main() -> int:
    errors: list[str] = []
    check_shortcut_drift(errors)
    check_plugin_counts(errors)
    check_test_count(errors)

    if errors:
        print("Doc-drift guard found issues:\n", file=sys.stderr)
        for e in errors:
            print(e, file=sys.stderr)
        print("", file=sys.stderr)
        print("Update the relevant doc (docs/README.md or docs/SHORTCUTS.md) and re-run.", file=sys.stderr)
        return 1

    print("Doc-drift guard: all three checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
