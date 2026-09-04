#!/usr/bin/env python3
"""
Doc-drift guard.

Asserts four things the docs claim match reality:

  1. Every SystemCommand default binding in ShortcutManager.InitializeDefaultProfile()
     has its key chord documented somewhere in docs/SHORTCUTS.md.
  2. Every default binding also appears in docs/USER_MANUAL.md or docs/QUICKSTART.md —
     the two docs a user is actually pointed at, both of which had drifted by 2026-08-28
     while check 1 stayed green against the reference doc.
  3. EVERY plugin-count claim in docs/README.md matches the directory layout in
     Plugins/Providers/ and Plugins/Analytics/.
  4. EVERY test-count claim in docs/README.md agrees with the others and with
     `dotnet test --list-tests` output.

Checks 3 and 4 validate every occurrence, not the first one. They used to use `re.search`,
which validated whichever claim appeared first and left the rest unchecked — that is how
"29 data providers" survived in the README's most prominent section for three releases while
the guard reported green. Both carry a floor on the number of claims found, so a rephrase
that hides a claim from the regex fails instead of quietly reducing coverage.

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
MANUAL_MD = REPO / "docs" / "USER_MANUAL.md"
QUICKSTART_MD = REPO / "docs" / "QUICKSTART.md"
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
    "PAGEUP": ("Page Up", "PageUp"),
    "PAGEDOWN": ("Page Down", "PageDown"),
    "CONTEXTMENU": ("ContextMenu", "Context Menu"),
    "HOME": ("Home",),
    "END": ("End",),
    "ESCAPE": ("Escape",),
    "ENTER": ("Enter",),
    "RETURN": ("Enter",),
    "TAB": ("Tab",),
    "SPACE": ("Space",),
    " ": ("Space",),
    "DELETE": ("Delete",),
    "OEM2": ("/", "Slash"),
    "OEM4": ("[",),
    "OEM5": ("Backslash", "\\"),
    "OEM6": ("]",),
    "OEMMINUS": ("-",),
    "OEMPLUS": ("=",),
    "OEMCOMMA": (",", "Comma"),
    ",": (",", "Comma"),
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


def check_user_doc_coverage(errors: list[str]) -> None:
    """Every default binding must be reachable from the two USER-facing docs.

    SHORTCUTS.md is the reference and is checked above; it is also the doc nobody reads
    end to end. This check exists because the two docs a user is actually pointed at had
    both drifted: on 2026-08-28 the manual carried no undo/redo, no drawing context menu,
    no sub-pane navigation and no show-all/unmute-all, and the QUICKSTART's section titled
    "Complete Keyboard Shortcut Reference" was missing the entire quick-trade tier.

    Manual OR quickstart, not both: the quickstart is a reference table and the manual is
    prose with scenarios, so a chord legitimately lives in one or the other. Write the chord
    out literally in at least one of them — "Ctrl+Alt+Shift+1 / 2 / 3" reads fine but a user
    searching for "Ctrl+Alt+Shift+2" does not find it, and neither does this check.

    If this fails, the fix is to document the chord. Do NOT add an exemption list.
    """
    bindings = parse_default_bindings()
    if not bindings:
        return  # already reported by check_shortcut_drift

    corpus = (MANUAL_MD.read_text(encoding="utf-8")
              + "\n" + QUICKSTART_MD.read_text(encoding="utf-8")).lower()

    missing = []
    for cmd, variants in bindings:
        if any(v.lower() in corpus for v in variants):
            continue
        if any(v.lower().replace("+", "-") in corpus for v in variants):
            continue
        missing.append((cmd, variants[0]))

    if missing:
        errors.append(
            f"USER-DOC GUARD: {len(missing)} binding(s) appear in neither "
            "docs/USER_MANUAL.md nor docs/QUICKSTART.md:"
        )
        for cmd, chord in missing:
            errors.append(f"   * {cmd:28s} -> {chord}")


# Every provider-count claim in the README, in either phrasing the file uses:
#   "33 exchange, data, and analytics provider plugins (16 trading + 17 analytics)"
#   "33 data providers (16 trading in `Plugins/Providers/`, 17 analytics in `Plugins/Analytics/` …"
# Matched with finditer, not search. The `search` version validated whichever claim came first —
# in practice the correct one near the bottom of the file — and never saw the wrong one in the
# far more prominent Key Subsystems section, which read "29 data providers" for three releases.
README_PROVIDER_RE = re.compile(
    r"(?P<total>\d+)\s*(?:exchange|data)[^\(\n]*\("
    r"(?P<trading>\d+)\s*trading\b[^,\+\)]*[,\+]\s*(?P<analytics>\d+)\s*analytics\b",
    re.IGNORECASE,
)
README_TEST_RE = re.compile(r"\((?P<n>\d+)\s*tests", re.IGNORECASE)

# A guard that finds nothing passes for the wrong reason. Both counts are claimed in more than one
# place in the README, so a rephrase that drops a claim out of the regex's reach has to fail
# loudly rather than silently reduce coverage. Raise these if a claim is added; never lower them
# to make a run go green.
MIN_PROVIDER_CLAIMS = 3
MIN_TEST_CLAIMS = 2


def check_plugin_counts(errors: list[str]) -> None:
    actual_providers = sum(1 for p in PROVIDERS_DIR.iterdir() if p.is_dir())
    actual_analytics = sum(1 for p in ANALYTICS_DIR.iterdir() if p.is_dir())
    actual_total = actual_providers + actual_analytics

    md = README_MD.read_text(encoding="utf-8")
    matches = list(README_PROVIDER_RE.finditer(md))
    if len(matches) < MIN_PROVIDER_CLAIMS:
        errors.append(
            f"PLUGIN GUARD: found {len(matches)} provider-count claim(s) in docs/README.md, "
            f"expected at least {MIN_PROVIDER_CLAIMS}. Either a claim was removed or one was "
            "rephrased out of the regex's reach — fix the discovery, do not lower the floor."
        )
    for m in matches:
        claim = (int(m.group("total")), int(m.group("trading")), int(m.group("analytics")))
        if claim != (actual_total, actual_providers, actual_analytics):
            line = md.count("\n", 0, m.start()) + 1
            errors.append(
                f"PLUGIN GUARD: docs/README.md:{line} claims {claim[0]} plugins "
                f"({claim[1]} trading + {claim[2]} analytics); "
                f"filesystem has {actual_total} ({actual_providers} trading + {actual_analytics} analytics)."
            )


def check_test_count(errors: list[str]) -> None:
    md = README_MD.read_text(encoding="utf-8")
    matches = list(README_TEST_RE.finditer(md))
    if len(matches) < MIN_TEST_CLAIMS:
        errors.append(
            f"TEST GUARD: found {len(matches)} test-count claim(s) in docs/README.md, expected at "
            f"least {MIN_TEST_CLAIMS}. Fix the discovery, do not lower the floor."
        )
    if not matches:
        return
    claims = {int(m.group("n")): md.count("\n", 0, m.start()) + 1 for m in matches}
    if len(claims) > 1:
        errors.append(
            "TEST GUARD: docs/README.md claims different test counts in different places: "
            + ", ".join(f"{n} (line {ln})" for n, ln in sorted(claims.items()))
        )
    claim = min(claims)

    if os.environ.get("DOC_DRIFT_SKIP_TESTS") == "1":
        print(f"TEST GUARD: skipped (DOC_DRIFT_SKIP_TESTS=1). README claims {claim}.")
        return

    # Configuration must match whatever the caller pre-built, or `dotnet test`
    # triggers its own build. CI passes Release: the classic rzc pipeline
    # (UseRazorSourceGenerator=false, dotnet/razor#13184) crashes
    # nondeterministically on Debug WebHost builds ("This writer does not
    # support components") while Release has never tripped it.
    config = os.environ.get("DOC_DRIFT_CONFIG", "Debug")
    try:
        res = subprocess.run(
            ["dotnet", "test", str(TESTS_PROJ), "--list-tests", "--nologo",
             "--configuration", config],
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
    check_user_doc_coverage(errors)
    check_plugin_counts(errors)
    check_test_count(errors)

    if errors:
        print("Doc-drift guard found issues:\n", file=sys.stderr)
        for e in errors:
            print(e, file=sys.stderr)
        print("", file=sys.stderr)
        print("Update the relevant doc (docs/README.md, docs/SHORTCUTS.md, "
              "docs/USER_MANUAL.md or docs/QUICKSTART.md) and re-run.", file=sys.stderr)
        return 1

    print("Doc-drift guard: all four checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
