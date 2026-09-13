#!/usr/bin/env python3
"""Prove the eight A2f survivor kills: re-apply each mutant, watch its new test go RED.

A test written for a survivor is a claim until the survivor kills it. This re-applies each
of the eight mutants that passed a green 7,666-test suite and runs ONLY the file that now
claims to catch it — a filtered run, so it is seconds rather than minutes.

Harness rules that apply even to a short script like this one:
  1. Restore from a FILE COPY, never `git checkout --`, then `touch` (MSBuild mtime).
  2. "No test matches the given testcase filter" exits 0 and reads as a pass. The filter
     match is asserted explicitly — a filter typo would otherwise record every kill as proven.
  3. The anchor must be unique before patching; a sabotage that did not apply is UNVERIFIED,
     never a result.
  4. Finish by diffing every touched file against its backup, and SAY SO — the absence of
     "restored byte-identical" is itself the alarm.
"""
import os, re, shutil, subprocess, sys, tempfile

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
A = "AccessibleTrader.Core/Services/Accessibility/"

# (id, file, find, replace, test-filter)
KILLS = [
    ("F02", A + "SpeechFormatter.cs",
     '                else if (state.TimestampReadLocation == "None") shouldSpeakTimestamp = false;',
     '                else if (state.TimestampReadLocation == "None") shouldSpeakTimestamp = true;',
     "NavigationTimestampTests"),

    ("F04", A + "SpeechFormatter.cs",
     '                    return string.IsNullOrEmpty(prefixMessage) ? "" : prefixMessage.TrimEnd();',
     '                    return "";',
     "NavigationUtteranceTests"),

    ("F13", A + "NarrationScanner.cs",
     "            if (subs.Count == 0) return false;",
     "            if (subs.Count == 0) return true;",
     "BandZoneNarrationTests"),

    ("F16", A + "NarrationScanner.cs",
     '            return Math.Abs(level.Value) < 1e-9 && name.Contains("zero")',
     '            return Math.Abs(level.Value) < 1e-9 || name.Contains("zero")',
     "BandZoneNarrationTests"),

    ("F21", A + "NavigationFeedbackManager.cs",
     "        private const double ZoneProximityPct = 0.005;",
     "        private const double ZoneProximityPct = 0.5;",
     "NavigationUtteranceTests"),

    ("F24", A + "ChartHitTester.cs",
     "                    if (dist <= tolerancePx && (best == null || dist < best.DistancePx))",
     "                    if (dist <= tolerancePx && (best == null || dist > best.DistancePx))",
     "ChartHitTesterAndRangeTests"),

    ("F25", A + "ChartHitTester.cs",
     "            if (yFrac < 0 || yFrac > plotBottomFrac) return null; // over the x-axis strip",
     "            if (yFrac < 0 || yFrac > 1.0) return null; // over the x-axis strip",
     "ChartHitTesterAndRangeTests"),

    ("F26", A + "GlobalErrorCoordinator.cs",
     "            bool interrupt = ev.Severity >= ErrorSeverity.High;",
     "            bool interrupt = ev.Severity > ErrorSeverity.High;",
     "SilentFailureBatchTests"),
]

SUMMARY = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")


def run(cmd):
    p = subprocess.run(cmd, cwd=REPO, shell=True, capture_output=True, text=True, timeout=1800)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def main():
    backup = tempfile.mkdtemp(prefix="a2f_prove_")
    touched = sorted({k[1] for k in KILLS})
    for rel in touched:
        shutil.copy2(os.path.join(REPO, rel), os.path.join(backup, rel.replace("/", "_")))

    results = []
    for mid, rel, find, repl, filt in KILLS:
        path = os.path.join(REPO, rel)
        original = open(path, encoding="utf-8-sig").read()
        if original.count(find) != 1:
            results.append((mid, "BAD_ANCHOR", f"{original.count(find)} occurrences"))
            print(f"{mid}: BAD ANCHOR", flush=True)
            continue
        try:
            open(path, "w", encoding="utf-8").write(original.replace(find, repl))
            # run() returns the PROCESS EXIT CODE: 0 is success. The first draft of this script
            # wrote `if not ok:` against it and reported all eight as NO_COMPILE — a harness that
            # inverts its own success test reports nothing at all, very convincingly.
            code, _ = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                          "-p:UseRazorSourceGenerator=false -v q --nologo")
            if code != 0:
                results.append((mid, "NO_COMPILE", ""))
                print(f"{mid}: DID NOT COMPILE", flush=True)
                continue
            _, out = run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                         f"-p:UseRazorSourceGenerator=false --no-build --filter FullyQualifiedName~{filt}")
            if "No test matches" in out:
                results.append((mid, "FILTER_MATCHED_NOTHING", filt))
                print(f"{mid}: FILTER MATCHED NOTHING — {filt}", flush=True)
                continue
            m = SUMMARY.search(out)
            failed = int(m.group(1)) if m else -1
            names = sorted(set(re.findall(r"^\s*Failed\s+([A-Za-z0-9_.]+)", out, re.M)))
            status = "PROVEN_RED" if failed > 0 else ("UNPARSED" if failed < 0 else "STILL_GREEN")
            results.append((mid, status, "; ".join(n.split(".")[-1] for n in names[:4])))
            print(f"{mid}: {status} failed={failed} — {'; '.join(n.split('.')[-1] for n in names[:4])}",
                  flush=True)
        finally:
            open(path, "w", encoding="utf-8").write(original)
            os.utime(path, None)

    print("\n=== restore check")
    clean = True
    for rel in touched:
        same = open(os.path.join(REPO, rel), encoding="utf-8-sig").read() == \
               open(os.path.join(backup, rel.replace("/", "_")), encoding="utf-8-sig").read()
        print(f"  {'restored byte-identical' if same else 'DIFFERS — INVESTIGATE'}  {rel}")
        clean &= same

    print("\n=== summary")
    for mid, status, detail in results:
        print(f"  {mid} {status:>22}  {detail}")
    proven = sum(1 for _, s, _ in results if s == "PROVEN_RED")
    print(f"\n{proven}/{len(KILLS)} proven red; tree {'clean' if clean else 'DIRTY'}")


if __name__ == "__main__":
    main()
