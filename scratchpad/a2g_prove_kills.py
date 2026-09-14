#!/usr/bin/env python3
"""A2g — prove every new guard RED by reintroducing the exact mutant it was written for.

A test written against a defect you have already fixed is a claim until you have watched it
fail. Each entry below re-applies one A2g survivor and runs ONLY the tests that should now
catch it; the run must FAIL, and the failing names are printed so a green-by-accident or a
red-for-the-wrong-reason is visible rather than inferred.

Harness rules that matter here (see the sabotage-harness-rules note):
  1. `touch` after restoring, or MSBuild keeps the sabotaged binary.
  2. "No test matches the given testcase filter" is a FAILURE, not a pass — a filter naming a
     class that does not exist exits 0 and reads as "the guard did not fire".
  3. Assert the anchor is UNIQUE before patching.
  4. Restore from a file copy, never `git checkout --`.
  7. Print the failing NAMES. A2f's prove-kills script inverted its own success test and
     reported NO_COMPILE for all eight kills on a clean build; a uniform result has to look
     as wrong as it is.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2g_prove_kills_results.json")
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")
NO_MATCH = "No test matches the given testcase filter"

A = "AccessibleTrader.Core/Services/Audio/"

# (id, file, find, replace, test filter)
KILLS = [
    ("G02", A + "AudioConstants.cs",
     "            ComponentDisplayType.Cross,\n        };", "        };",
     "NavigationSonifierClusterTests"),

    ("G04", A + "AudioZoneHelper.cs",
     "            if (comp.SubscribedLevelNames.Count == 0) return false;",
     "            if (comp.SubscribedLevelNames.Count == 0) return true;",
     "LevelMeaningAndScopeTests"),

    ("G08", A + "EarconPatchPlayer.cs",
     "        public const int CueSlotStart = 26;",
     "        public const int CueSlotStart = 30;",
     "UiEarconSlotTests"),

    ("G09", A + "EarconPatchPlayer.cs",
     "            if (patch == null) return false;",
     "            if (patch == null) return true;",
     "UiEarconSlotTests"),

    ("G10", A + "NavigationSonifier.cs",
     "            if (series == null || !series.IsVisible || series.IsMuted)\n            {\n                MuteAllNavigationSlots();",
     "            if (series == null || !series.IsVisible)\n            {\n                MuteAllNavigationSlots();",
     "MuteIsAbsoluteTests"),

    ("G13", A + "NavigationSonifier.cs",
     "            => series.Components.Count > 0\n               && series.Components.All(c => c.IsMuted || !c.IsVisible || c.Volume <= 0f);",
     "            => series.Components.Count >= 0\n               && series.Components.All(c => c.IsMuted || !c.IsVisible || c.Volume <= 0f);",
     "MuteIsAbsoluteTests"),

    ("G21", A + "AudioSequencer.cs",
     '            => string.Equals(envelopeType, "Ping", StringComparison.OrdinalIgnoreCase);',
     '            => string.Equals(envelopeType, "Ping", StringComparison.Ordinal);',
     "PlaybackVoiceLifecycleTests"),

    ("G22", A + "AudioSequencer.cs",
     "            AccessibleTrader.Sdk.Models.PlaybackLayer.Background => 0.60f,",
     "            AccessibleTrader.Sdk.Models.PlaybackLayer.Background => 1.00f,",
     "PlaybackLayerTests"),

    ("G25", A + "AudioSequencer.cs",
     "            for (int i = PlaybackSlotOffset; i < AudioEngine.MaxVoices; i++) _audioDriver.StopVoice(i);",
     "            for (int i = PlaybackSlotOffset; i <= PlaybackSlotEnd; i++) _audioDriver.StopVoice(i);",
     "PlaybackVoiceLifecycleTests"),

    # G33 is an EQUIVALENT MUTANT, established by a three-way experiment on 2026-09-13:
    # dropping `&& _stopAllFaded` changes nothing observable (re-arming to the user's own value
    # is idempotent), while re-arming to a LITERAL is the historical defect and is caught. The
    # kill below is therefore aimed at the load-bearing half, not at the flag.
    ("G33", A + "AudioEngine.cs",
     "                        _targetMasterGain = _userMasterGain;",
     "                        _targetMasterGain = 1.0f;",
     "AudioSafetyTests"),

    ("G34", A + "AudioEngine.cs",
     "            vol = Math.Clamp(vol, 0f, 1f);",
     "            vol = Math.Clamp(vol, 0f, 10f);",
     "AudioSafetyTests"),

    ("G35", A + "AudioEngine.cs",
     "                        renderVolume = (float)(v.TargetVolume * Math.Exp(-5.0 * progress));",
     "                        renderVolume = (float)(v.TargetVolume * Math.Exp(-0.5 * progress));",
     "AudioSafetyTests"),

    ("G36", A + "WavFileReader.cs",
     "                            8  => (bytes[off] - 128) / 128.0,                       // PCM8 is unsigned",
     "                            8  => bytes[off] / 128.0,                       // PCM8 is unsigned",
     "WavetableTests"),

    ("G37", A + "WavetableLibraryService.cs",
     "            bool asWavetable = mono.Length <= WavetableMaxFrames;",
     "            bool asWavetable = mono.Length >= WavetableMaxFrames;",
     "WavetableTests"),
]


def run(cmd, timeout=1800):
    p = subprocess.run(cmd, cwd=REPO, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def main():
    results = []
    for kid, relpath, find, repl, filt in KILLS:
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        n = original.count(find)
        rec = {'id': kid, 'file': relpath, 'filter': filt, 'occurrences': n}
        if n != 1:                                                   # rule 3
            rec['status'] = 'BAD_ANCHOR'
            results.append(rec)
            print(f"{kid}: BAD ANCHOR ({n}) — {relpath}", flush=True)
            continue
        t0 = time.time()
        try:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            build_code, build_log = run(
                "dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                "-p:UseRazorSourceGenerator=false -v q --nologo")
            if build_code != 0:                                      # rule 7: name it, don't infer
                rec['status'] = 'NO_COMPILE'
                rec['log'] = build_log[-1200:]
                print(f"{kid}: DID NOT COMPILE\n{build_log[-600:]}", flush=True)
            else:
                _, out = run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                             f"-p:UseRazorSourceGenerator=false --no-build --filter FullyQualifiedName~{filt}")
                if NO_MATCH in out:                                  # rule 2
                    rec['status'] = 'FILTER_MATCHED_NOTHING'
                    print(f"{kid}: FILTER MATCHED NOTHING ({filt}) — this is a FAILURE", flush=True)
                else:
                    m = SUMMARY_RE.search(out)
                    rec['failed'] = int(m.group(1)) if m else -1
                    rec['passed'] = int(m.group(2)) if m else -1
                    names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                    rec['failing_tests'] = names[:20]
                    rec['status'] = ('UNPARSED' if rec['failed'] < 0
                                     else 'RED' if rec['failed'] > 0 else 'STILL_GREEN')
                    print(f"{kid}: {rec['status']} failed={rec['failed']} passed={rec['passed']} "
                          f"({time.time()-t0:.0f}s)", flush=True)
                    for t in names[:6]:
                        print(f"      {t}", flush=True)
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)                                     # rule 1
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    print("\n=== summary")
    for r in results:
        print(f"  {r['id']} {r['status']:>22}  {r['filter']}")
    red = sum(1 for r in results if r['status'] == 'RED')
    print(f"\n{red}/{len(results)} proved RED")

    # Rule 4's closing check: every file byte-identical to HEAD.
    code, diff = run("git diff --stat -- AccessibleTrader.Core/Services/Audio/")
    print("restored byte-identical" if not diff.strip() else f"TREE NOT RESTORED:\n{diff}")


if __name__ == '__main__':
    main()
