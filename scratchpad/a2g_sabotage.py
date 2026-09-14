#!/usr/bin/env python3
"""A2g — the SIXTH mutant set, aimed at the area that IS this application's output.

WHY THIS AREA.

`Core/Services/Audio` is 19 files and 4,394 lines. Mutants ever applied to it,
across a2, a2b, a2c/fresh, a2d, a2e, a2f and a3: **ONE**, in AudioEngine.cs.
Eight of its 32 declared types are never named by either test project.

For a sighted user, audio is decoration. For this one it is the CHART. Pitch is
the value, pan is the bar's x-position, grit is the wick's length and the body's
size, noise is the overbought zone, timbre is which indicator you are listening
to. Every one of those is a rule written in exactly one place in this directory,
and almost none of those places has ever been asked whether a test would notice
if the rule changed.

This is also where the rules are DENSEST per line. `CreateAudioPoint` alone
decides pitch mapping, four amplitude mappings, waveform-by-reference-level,
boundary clicks, cross direction, zone noise, patch resolution and six additive
partial recipes — in one method, on the path every arrow key and every playback
bar takes.

SAMPLING FRAME: 17 of the 19 files below have never had a mutant. AudioEngine.cs
has had one (2026-08-26, a2b). No file in Services/Accessibility, Input,
Indicators, Rendering or Providers appears here — this is disjoint from a2d, a2e
and a2f, so the rates compare.

METHOD — identical to a2d/a2e/a2f so the numbers compare: apply one mutant,
build, run the FULL AccessibleTrader.Tests suite, record whether anything went
red and WHICH tests did, revert, touch. CAUGHT iff some test fails.

THE SEVEN HARNESS RULES (each learned from a run that lied — see the
sabotage-harness-rules note):
  1. `touch` after restoring, or MSBuild keeps the sabotaged binary.
  2. "No test matches the given testcase filter" is a FAILURE, not a pass.
  3. Assert the anchor is UNIQUE before patching. A 0- or 2-occurrence anchor
     records BAD_ANCHOR; an unapplied sabotage is UNVERIFIED, never a result.
  4. Restore from a file copy held in memory here, never `git checkout --`, and
     the tree must be COMMITTED before starting.
  5. DO NOT TOUCH THE REPO — SOURCE *OR DOCS* — WHILE A CAMPAIGN IS RUNNING.
     AboutDialogHonestyTests reads Directory.Build.props and diffs it against
     CHANGES.md and WHATSNEW.md, so a mid-run doc edit records FALSE CATCHES.
     CONTAMINATION FILTER: any mutant whose failing_tests list is exactly
     ["...AboutDialogHonestyTests.TheReleaseDocsNameTheVersionThatIsActuallyBuilt"]
     is UNVERIFIED and must be re-run.
  6. NEVER add `-v q --nologo` to the `dotnet test` line — quiet verbosity
     suppresses the per-test failure lines, which removes the FALSE-CATCH AUDIT.
  7. Print the failing names, not just the status, so a uniform result looks as
     wrong as it is.

Run DETACHED with setsid — a tracked background command is capped at ten minutes
and this takes ~2.5 hours. An IN-FLIGHT marker survives SIGKILL so a stale
sabotage is restored at startup.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2g_sabotage_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2g_sabotage_inflight.txt")

SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")

A = "AccessibleTrader.Core/Services/Audio/"

# (id, area, file, find, replace, what-it-breaks)
MUTANTS = [

    # ── AudioConstants.cs — the two rules every other file in here delegates to ──
    ("G01", "pan stops tracking the bar's pixel",
     A + "AudioConstants.cs",
     "            return Math.Clamp((2.0 * (relativeIndex + 0.5) / viewportWidth) - 1.0, -1.0, 1.0);",
     "            return Math.Clamp((2.0 * relativeIndex / viewportWidth) - 1.0, -1.0, 1.0);",
     "the centre-of-slot mapping the renderer uses becomes an edge mapping: every bar's stereo "
     "position sits half a slot left of where it is drawn, and the audio/visual lockstep the doc "
     "comment claims is quietly false again"),

    ("G02", "a Cross marker stops counting as a signal",
     A + "AudioConstants.cs",
     "            ComponentDisplayType.Cross,\n        };",
     "        };",
     "MarkerDisplayTypes is read by cluster ticks, signal speech AND the NaN guard: a component "
     "drawn as a cross fires no tick, is never spoken as a signal, and its silent bars stop being "
     "silenced — three channels, one set"),

    # ── AudioZoneHelper.cs — the overbought/oversold texture ────────────────────
    ("G03", "the overbought zone sounds where the oversold one is",
     A + "AudioZoneHelper.cs",
     "                bool inZone = (role == LevelRole.Overbought && val > lc.Value)\n"
     "                           || (role == LevelRole.Oversold   && val < lc.Value);",
     "                bool inZone = (role == LevelRole.Overbought && val < lc.Value)\n"
     "                           || (role == LevelRole.Oversold   && val > lc.Value);",
     "roughness arrives exactly where the chart is calm and vanishes at the extremes — an RSI at 80 "
     "is a clean sine and an RSI at 50 is gritty"),

    ("G04", "an explicitly EMPTY level subscription subscribes to everything",
     A + "AudioZoneHelper.cs",
     "            if (comp.SubscribedLevelNames.Count == 0) return false;",
     "            if (comp.SubscribedLevelNames.Count == 0) return true;",
     "a component that opted OUT of every level gets boundary clicks and cross chirps from all of "
     "them — the off value of a setting, which is the one nobody writes a fixture for"),

    # ── SignalTierClassifier.cs — which five of N signals you hear ──────────────
    ("G05", "the most significant signal is ranked last",
     A + "SignalTierClassifier.cs",
     "                return 1;",
     "                return 4;",
     "cluster ticks fire in tier order and stop at five: a main-pane SR/divergence diamond — the "
     "rarest and most important marker on the chart — is now the FIRST thing dropped when a bar is "
     "busy, which is exactly the bar where it matters"),

    # ── CrossEarcon.cs — the direction of a level cross ─────────────────────────
    ("G06", "an up-cross falls and a down-cross rises",
     A + "CrossEarcon.cs",
     "            double f1 = direction > 0 ? LowFreq : HighFreq;\n"
     "            double f2 = direction > 0 ? HighFreq : LowFreq;",
     "            double f1 = direction > 0 ? HighFreq : LowFreq;\n"
     "            double f2 = direction > 0 ? LowFreq : HighFreq;",
     "the one earcon whose entire job is to be unmistakable about direction now says the opposite: "
     "the terminal reports a breakdown as a breakout"),

    # ── PatchLayerNoise.cs — a patch must never silence the zone cue ────────────
    ("G07", "assigning a patch silences the overbought texture",
     A + "PatchLayerNoise.cs",
     "            if (layerIndex != 0)",
     "            if (layerIndex != 1)",
     "the zone cue rides layer 0 by contract; moved to layer 1 a single-layer patch (the common "
     "case) drops it entirely — the defect this class was extracted to prevent, back in both the "
     "navigation and playback renderers at once"),

    # ── EarconPatchPlayer.cs — the level-cue slots ──────────────────────────────
    ("G08", "the UI round-robin walks back over the level-cue slots",
     A + "EarconPatchPlayer.cs",
     "        public const int CueSlotStart = 26;",
     "        public const int CueSlotStart = 30;",
     "NavigationSonifier.UiRoundRobinSlots is derived from this constant: widen it and the eleventh "
     "UI note of a burst lands on 30/31 and cuts off the cross chirp — on precisely the bars where "
     "UI notes are busiest, which is what a level cross IS"),

    ("G09", "a missing override patch silences the cue instead of falling back",
     A + "EarconPatchPlayer.cs",
     "            if (patch == null) return false;",
     "            if (patch == null) return true;",
     "the return value means 'I played it, do not play yours'. A patch id pointing at a deleted "
     "patch now silences the approach/sustained/cross cue outright rather than falling back to the "
     "built-in tone — a broken reference reads as silence, the one failure this user cannot see"),

    # ── NavigationSonifier.cs — 767 lines, zero mutants ever ────────────────────
    ("G10", "a muted series still sounds under the arrow keys",
     A + "NavigationSonifier.cs",
     "            if (series == null || !series.IsVisible || series.IsMuted)\n"
     "            {\n"
     "                MuteAllNavigationSlots();",
     "            if (series == null || !series.IsVisible)\n"
     "            {\n"
     "                MuteAllNavigationSlots();",
     "mute is absolute in this app; here the series-level mute stops being consulted on the "
     "navigation path, so the one control for 'stop making this noise' does nothing under arrows"),

    ("G11", "the text-label tick fires only while you stand still",
     A + "NavigationSonifier.cs",
     "            if (_lastLabelEarconIndex == idx) return;",
     "            if (_lastLabelEarconIndex != idx) return;",
     "the de-duplication is inverted: arriving on a labelled bar is silent, and standing on one "
     "repeats the tick on every state push — the annotation marker announces everything except its "
     "own arrival"),

    ("G12", "the close line's pitch leaves the number it is spoken as",
     A + "NavigationSonifier.cs",
     "            bool followsCandleTransform = state.IsHeikinAshi && series.Id != CoreSeriesIds.Price;",
     "            bool followsCandleTransform = state.IsHeikinAshi;",
     "in Heikin-Ashi the price line would sweep to the smoothed average while the speech and the "
     "title bar quote the raw close: the two halves of one readout on different values"),

    ("G13", "a distribution series with no components goes silent",
     A + "NavigationSonifier.cs",
     "            => series.Components.Count > 0\n"
     "               && series.Components.All(c => c.IsMuted || !c.IsVisible || c.Volume <= 0f);",
     "            => series.Components.Count >= 0\n"
     "               && series.Components.All(c => c.IsMuted || !c.IsVisible || c.Volume <= 0f);",
     "`All` on an empty list is true, so a profile or heatmap still loading its components is "
     "treated as deliberately switched off — the widened guard whose comment says exactly why it "
     "must not be, narrowed back"),

    ("G14", "cross-indicator ticks move from playback to the arrow keys",
     A + "NavigationSonifier.cs",
     "                if (!crossSeriesMode && series.Id != excludeSeriesId) continue;",
     "                if (crossSeriesMode && series.Id != excludeSeriesId) continue;",
     "both directions wrong at once: arrowing one bar now fires every indicator's markers across "
     "the whole chart, and playback — where cross-indicator audio is the point — fires only the "
     "focused series'"),

    # ── ISonificationStrategy.cs — 550 lines, zero mutants ever ─────────────────
    ("G15", "every bar carries full grit, so size is inaudible",
     A + "ISonificationStrategy.cs",
     "            return (float)(0.30 * magnitudeNorm);",
     "            return 0.30f;",
     "BarGrit is THE encoding of magnitude for volume bars and every histogram — loudness is "
     "deliberately constant so texture can carry size. Pinned at full weight, a stub and a "
     "full-height bar are the same sound and the channel says nothing"),

    ("G16", "both wicks are the same tone again",
     A + "ISonificationStrategy.cs",
     '            if (string.Equals(comp.DataMapping, "high", StringComparison.OrdinalIgnoreCase)) return true;',
     '            if (string.Equals(comp.DataMapping, "high", StringComparison.OrdinalIgnoreCase)) return false;',
     "the exact defect reported from live use: every wick sonified as a LOWER wick — same 220 Hz "
     "pitch, and grit computed from the lower shadow even when the upper one is playing"),

    ("G17", "a muted component still sounds",
     A + "ISonificationStrategy.cs",
     "            float baseVolume = comp.Volume * (series.IsMuted || comp.IsMuted || !series.IsVisible || !comp.IsVisible ? 0 : series.Volume) * chartVolume;",
     "            float baseVolume = comp.Volume * (series.IsMuted || !series.IsVisible || !comp.IsVisible ? 0 : series.Volume) * chartVolume;",
     "the component-level mute stops reaching the volume on the one path both navigation and "
     "playback take — m on a component becomes a no-op everywhere at once"),

    ("G18", "the cross chirp's direction is inverted at the source",
     A + "ISonificationStrategy.cs",
     "            int crossDir = (triggerClick && prevVal.HasValue) ? Math.Sign(val - prevVal.Value) : 0;",
     "            int crossDir = (triggerClick && prevVal.HasValue) ? Math.Sign(prevVal.Value - val) : 0;",
     "CrossEarcon is fed from here by BOTH renderers, so one sign flip makes every cross chirp in "
     "the app — navigation and playback — announce the wrong direction"),

    ("G19", "entering a zone makes the sound CLEANER",
     A + "ISonificationStrategy.cs",
     "                if (zoneNoise > noiseAmt)\n"
     "                {\n"
     "                    noiseAmt  = zoneNoise;",
     "                if (zoneNoise < noiseAmt)\n"
     "                {\n"
     "                    noiseAmt  = zoneNoise;",
     "the 'take the stronger' rule inverts to 'take the weaker': a component with its own base "
     "texture gets SMOOTHER on entering overbought, which is the old defect this comment documents, "
     "reintroduced"),

    ("G20", "gradient and detuned voices lose their second slot",
     A + "ISonificationStrategy.cs",
     "            return 1 + (comp.UsesGradientSpeech || IsDetunedBuiltin(comp) ? 1 : 0);",
     "            return 1;",
     "the playback voice plan reserves slots from this count: a gradient ribbon loses its blend "
     "waveform and a detuned bell loses its second voice under Space, while both still sound under "
     "the arrow keys — the same nav/playback divergence PatchLayerNoise exists to prevent"),

    # ── AudioSequencer.cs — 654 lines, zero mutants ever ────────────────────────
    ("G21", 'an imported "ping" becomes a permanent drone',
     A + "AudioSequencer.cs",
     '            => string.Equals(envelopeType, "Ping", StringComparison.OrdinalIgnoreCase);',
     '            => string.Equals(envelopeType, "Ping", StringComparison.Ordinal);',
     "EnvelopeType is free text from provider metadata, saved workspaces and imported patch JSON. "
     "Case-sensitive here, a lowercase 'ping' is continuous with duration 0 — a playback slot that "
     "never decays, which is the failure this single helper was extracted to end"),

    ("G22", "the Background playback layer stops sitting back",
     A + "AudioSequencer.cs",
     "            AccessibleTrader.Sdk.Models.PlaybackLayer.Background => 0.60f,",
     "            AccessibleTrader.Sdk.Models.PlaybackLayer.Background => 1.00f,",
     "Background and Foreground both render at full, so the three-way mix collapses to two: the "
     "knob that decides what sits behind what during a full-chart playback stops moving anything"),

    ("G23", "a bar with no value drones instead of falling silent",
     A + "AudioSequencer.cs",
     "            if (audioPt.Frequency <= 0 && audioPt.Volume <= 0)",
     "            if (audioPt.Frequency < 0 && audioPt.Volume <= 0)",
     "the NaN answer is exactly AudioPoint(0, 0, ...), so this guard never fires again: a drawing "
     "with NaN on both sides of its span glides in from 0 Hz and back out, twice a pass — "
     "'there is nothing here' stops sounding like anything"),

    ("G24", "muting a series leaves its cloud fills playing",
     A + "AudioSequencer.cs",
     "                if (!series.IsVisible || series.IsMuted || series.Volume <= 0f) continue;",
     "                if (!series.IsVisible || series.Volume <= 0f) continue;",
     "the cloud pass runs OUTSIDE the voice plan, which is where mutes are filtered — so this line "
     "is the only place a muted series' fills are stopped, and without it muting a series does not "
     "mute the series"),

    ("G25", "cloud fills keep ringing after playback stops",
     A + "AudioSequencer.cs",
     "            for (int i = PlaybackSlotOffset; i < AudioEngine.MaxVoices; i++) _audioDriver.StopVoice(i);",
     "            for (int i = PlaybackSlotOffset; i <= PlaybackSlotEnd; i++) _audioDriver.StopVoice(i);",
     "slots 96-127 are left armed on Stop and on pause: the last bar's cloud chord drones on "
     "indefinitely, and the drone survives the thing that is supposed to end it"),

    # ── LevelCrossingMonitor.cs — 287 lines, zero mutants ever ──────────────────
    ("G26", "a chart announces a crossing that never happened",
     A + "LevelCrossingMonitor.cs",
     "            if (s.HasCrossed)",
     "            if (true)",
     "loading a chart whose price already sits above a hand-placed level fires the 'held beyond' "
     "tone a few bars later — the terminal reporting an event the market did not produce, which is "
     "worse than saying nothing"),

    ("G27", "the approach ping fires from twenty times further out",
     A + "LevelCrossingMonitor.cs",
     "            double band = (levelAbs > 0 ? levelAbs : 1.0) * ApproachBandFraction;",
     "            double band = (levelAbs > 0 ? levelAbs : 1.0) * 1.0;",
     "the 5% proximity band becomes 100% of the level's value, so 'approaching 70' starts at 0 — a "
     "cue that fires everywhere carries no information and masks the one that means something"),

    ("G28", "a level with the earcon switched off still fires",
     A + "LevelCrossingMonitor.cs",
     "                    if (!lc.IsVisible || !lc.PlayEarcon) continue;",
     "                    if (!lc.IsVisible) continue;",
     "'Play Earcon on Crossing' is a per-level tick box; unread here, every visible level on every "
     "indicator starts firing approach and sustained cues, and the user's way to quieten a busy "
     "chart stops working"),

    # ── SoundThemes.cs — 159 lines, zero mutants ever ───────────────────────────
    ("G29", "a theme overwrites the timbres that carry meaning",
     A + "SoundThemes.cs",
     "            if (role is ComponentRole.Body or ComponentRole.Wick or ComponentRole.Volume or ComponentRole.Histogram)\n"
     "                return Family.None;",
     "            if (role is ComponentRole.Body or ComponentRole.Wick or ComponentRole.Volume or ComponentRole.Histogram)\n"
     "                return Family.LineOverlay;",
     "candles, wicks, volume and histograms encode SIZE as grit computed per bar. Handing them a "
     "fixed factory patch silences that encoding — picking 'Orchestra' would quietly delete the "
     "wick-length and body-size channels"),

    # ── SonificationProfileProvider.cs — 113 lines, zero mutants ever ───────────
    ("G30", "histogram loudness encodes size again",
     A + "SonificationProfileProvider.cs",
     '        if (role == ComponentRole.Histogram || displayType == ComponentDisplayType.Bar || displayType == ComponentDisplayType.Histogram)\n'
     '            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.PriceDirection, 440, 1.0, false, "Sustain");',
     '        if (role == ComponentRole.Histogram || displayType == ComponentDisplayType.Bar || displayType == ComponentDisplayType.Histogram)\n'
     '            return new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.Size, PitchMapping.PriceDirection, 440, 1.0, false, "Sustain");',
     "the 2026-09-11 decision reversed: a MACD bar gets LOUDER as it grows while a volume bar gets "
     "ROUGHER — two encodings of 'how big' for two kinds of bar, and the loud one drops small bars "
     "toward silence"),

    # ── PlaybackPlan.cs — 103 lines, zero mutants ever ──────────────────────────
    ("G31", "series playback starts at bar zero instead of the cursor",
     A + "PlaybackPlan.cs",
     "            int start = Math.Clamp(Math.Max(0, state.CurrentDataIndex), 0, last);",
     "            int start = 0;",
     "Shift+Space means 'play this series from where I am standing'. From bar zero it replays the "
     "whole history every time, and the spoken plan — which is generated from this same record — "
     "describes the run correctly while the audio does something else"),

    # ── AudioEngine.cs — 845 lines, ONE mutant ever ─────────────────────────────
    ("G32", "stop-all stops nothing that is already sounding",
     A + "AudioEngine.cs",
     "                    if (v.IsActive) { v.Releasing = true; v.Continuous = false; }",
     "                    if (v.IsActive) { v.Continuous = false; }",
     "this is the user's 'make it stop'. Without the release, any voice command queued behind the "
     "stop-all re-arms the master gain, the deactivation never runs, and every voice keeps "
     "sounding — measured at RMS 0.397 after 40 buffers when the old version had this bug"),

    ("G33", "a volume the user set to zero comes back at full",
     A + "AudioEngine.cs",
     "                    if (cmd.IsActive && _stopAllFaded)",
     "                    if (cmd.IsActive)",
     "the flag is the only thing separating 'our stop-all fade' from 'the user chose silence'. "
     "Without it, setting the volume to 0% and pressing one arrow key restores full output — and "
     "the earcons that pass fixed literal volumes fire at full scale into headphones"),

    ("G34", "a volume of fifty reaches the mix as ten",
     A + "AudioEngine.cs",
     "            vol = Math.Clamp(vol, 0f, 1f);",
     "            vol = Math.Clamp(vol, 0f, 10f);",
     "the engine-boundary range check is the hearing-safety net under an imported patch whose "
     "fields nothing validates; widened tenfold it stops being one"),

    ("G35", "a ping stops decaying",
     A + "AudioEngine.cs",
     "                        renderVolume = (float)(v.TargetVolume * Math.Exp(-5.0 * progress));",
     "                        renderVolume = (float)(v.TargetVolume * Math.Exp(-0.5 * progress));",
     "the Ping envelope is what makes a marker a transient rather than a note; at a tenth of the "
     "decay rate every dot, arrow and wick holds most of its level for its whole duration and the "
     "sparse markers smear into the continuous bed"),

    # ── WavFileReader.cs / WavetableLibraryService.cs — user audio material ─────
    ("G36", "8-bit WAV imports arrive with a full-scale DC offset",
     A + "WavFileReader.cs",
     "                            8  => (bytes[off] - 128) / 128.0,                       // PCM8 is unsigned",
     "                            8  => bytes[off] / 128.0,                       // PCM8 is unsigned",
     "PCM8 is unsigned, so silence is 128. Read unsigned, an imported earcon sits between 0 and 2 "
     "instead of -1 and 1: a DC-offset blast that the clamp turns into a click and the limiter "
     "then ducks the whole mix for"),

    ("G37", "a single-cycle wavetable imports as a one-shot sample",
     A + "WavetableLibraryService.cs",
     "            bool asWavetable = mono.Length <= WavetableMaxFrames;",
     "            bool asWavetable = mono.Length >= WavetableMaxFrames;",
     "the classification inverts: an AKWF single cycle (~600 frames) registers as a SAMPLE — played "
     "once at natural speed with no pitch mapping, so a custom oscillator shape becomes an "
     "unpitched 14 ms click — and a long clip becomes a wavetable"),
]


def run(cmd, cwd=REPO, timeout=3600):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    code, out = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test():
    # Rule 6: NO `-v q --nologo` here. Quiet verbosity suppresses the per-test failure lines,
    # which silently removes the false-catch audit.
    return run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
               "-p:UseRazorSourceGenerator=false --no-build")


def recover_inflight():
    if not os.path.exists(INFLIGHT):
        return
    rel = open(INFLIGHT).read().strip()
    if rel:
        run(f"git checkout -- {rel}")
        print(f"recovered stale sabotage in {rel}", flush=True)
    os.remove(INFLIGHT)


def verify():
    """Rule 3, applied to the WHOLE SET before a minute of compute is spent."""
    ok = True
    for mid, area, relpath, find, repl, _ in MUTANTS:
        path = os.path.join(REPO, relpath)
        if not os.path.exists(path):
            print(f"{mid}: MISSING FILE {relpath}")
            ok = False
            continue
        src = open(path, encoding='utf-8-sig').read()
        n = src.count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences (need exactly 1) in {relpath}\n    {find[:120]!r}")
            ok = False
        if find == repl:
            print(f"{mid}: EQUIVALENT — find == replace")
            ok = False
    print(f"all {len(MUTANTS)} anchors unique" if ok else "ANCHOR CHECK FAILED")
    return ok


def main():
    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)

    recover_inflight()
    only = [a for a in sys.argv[1:] if a.startswith("G")] or None
    results = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in results}

    for mid, area, relpath, find, repl, breaks in MUTANTS:
        if (only and mid not in only) or mid in done:
            continue
        path = os.path.join(REPO, relpath)
        original = open(path, encoding='utf-8-sig').read()
        n = original.count(find)
        rec = {'id': mid, 'area': area, 'file': relpath, 'breaks': breaks, 'occurrences': n}
        if n != 1:
            rec['status'] = 'BAD_ANCHOR'
            results.append(rec)
            json.dump(results, open(OUT, 'w'), indent=1)
            print(f"{mid}: BAD ANCHOR ({n} occurrences) — {relpath}", flush=True)
            continue
        t0 = time.time()
        try:
            open(INFLIGHT, 'w').write(relpath)
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['status'] = 'NO_COMPILE'
                rec['log'] = log[-1500:]
                print(f"{mid}: DID NOT COMPILE — {area}", flush=True)
            else:
                code, out = test()
                m = SUMMARY_RE.search(out)
                rec['failed'] = int(m.group(1)) if m else -1
                rec['passed'] = int(m.group(2)) if m else -1
                names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                rec['failing_tests'] = names[:40]
                rec['status'] = ('UNPARSED' if rec['failed'] < 0
                                 else 'CAUGHT' if rec['failed'] > 0 else 'SURVIVED')
                # Rule 5 contamination filter.
                if names and all('AboutDialogHonestyTests' in x for x in names):
                    rec['status'] = 'UNVERIFIED_DOC_CONTAMINATION'
                print(f"{mid}: {rec['status']} failed={rec['failed']} "
                      f"({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)          # Rule 1
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    # THE CONTROL RUN: nothing sabotaged, everything must be green.
    build()
    code, out = test()
    m = SUMMARY_RE.search(out)
    print(f"\n=== CONTROL (nothing sabotaged): {m.group(0) if m else 'UNPARSED'}", flush=True)

    print("\n=== summary")
    for r in results:
        print(f"  {r['id']} {r['status']:>10}  {r['area']}")
    surv = [r['id'] for r in results if r['status'] == 'SURVIVED']
    caught = sum(1 for r in results if r['status'] == 'CAUGHT')
    total = caught + len(surv)
    if total:
        print(f"\ncatch rate {caught}/{total} = {100*caught/total:.1f}%   survivors: {surv}")


if __name__ == '__main__':
    main()
