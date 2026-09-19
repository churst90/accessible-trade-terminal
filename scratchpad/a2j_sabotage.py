#!/usr/bin/env python3
"""A2j — the NINTH mutant set: `Core/Services/Accessibility`.

WHY THIS AREA, AND WHY NOW.

15,898 lines in 49 files, and across eight prior campaigns plus the browser audit
exactly SIX of them have ever been mutated — the three tiny formatters
(SpeechPriceFormatter, SpeechTimeFormatter, QuantityFormatter), EarconService,
DotpadTactileDriver and AccessibilityFeedbackCoordinator. The seven largest files
in the directory — DrawingInteractionManager (1,362), AccessibilityFeedback-
Coordinator's neighbours SpeechFormatter (1,236), NarrationScanner (1,140),
TactileCanvasCoordinator (1,026), NavigationFeedbackManager (780), the Nudge
partial (728) and PlaybackNarration (626) — have had ZERO.

This is the layer that decides what a blind user HEARS. Everything else in the
terminal can be checked by looking; nothing here can. A2h measured the indicator
maths at 10.5% and that was a cliff; the question this set asks is whether the
layer that SPEAKS the maths is guarded any better.

SELECTION RULE: one mutant per decision a listener could catch you getting wrong
— an inverted direction word, a qualifier that stops being said, a scope that
widens to a series the user never asked to hear, a rarity ranking that reverts to
declaration order. Every mutant below is a sentence a user would file a bug about.
Several are RESTORATIONS of defects this repo has already fixed once, which is the
sharpest form of the question: is the fix guarded, or merely made?

SAMPLING FRAME: no file below appears in a2, a2b, fresh(a2c), a2d, a2e, a2f, a2g,
a2h, a2i or a3.

METHOD — identical to a2d..a2i so the numbers compare: apply one mutant, build,
run the FULL AccessibleTrader.Tests suite, record whether anything went red and
WHICH tests did, revert, touch. CAUGHT iff some test fails.

HARNESS RULES (each learned from a run that lied — see sabotage-harness-rules):
  1. `touch` after restoring, or MSBuild keeps the sabotaged binary. The prove
     script must also REBUILD before it exits, or a later `--no-build` run tests
     the last mutant (learned in A2h).
  2. "No test matches the given testcase filter" is a FAILURE, not a pass.
  3. Assert the anchor is UNIQUE before patching.
  4. Restore from a file copy, never `git checkout --`; commit before starting.
  5. DO NOT TOUCH THE REPO — SOURCE OR DOCS — WHILE A CAMPAIGN IS RUNNING.
  6. NEVER add `-v q --nologo` to `dotnet test` — it removes the false-catch audit.
  7. Print the failing NAMES, so a uniform result looks as wrong as it is.
  8. AUDIT THE CATCHES FOR BOOKKEEPING GUARDS (new in A2h): a mutant caught only
     by a pinned-list/exemption-hygiene test is NOT honestly caught, because the
     natural repair is to edit the list.
  9. CHECK EQUIVALENCE BEFORE WRITING A TEST (A2h/A2i): two of A2h's survivors and
     two of A2i's were mutants with no observable effect.

Run DETACHED with setsid; this takes ~2 hours.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2j_sabotage_results.json")
INFLIGHT = os.path.join(REPO, "scratchpad", "a2j_sabotage_inflight.txt")
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+)")

A = "AccessibleTrader.Core/Services/Accessibility/"

MUTANTS = [

    # ── SpeechFormatter: the arrow keys ──────────────────────────────────────
    ("J01", "the close line speaks the Heikin-Ashi close again",
     A + "SpeechFormatter.cs",
     "            bool readsRawBar = seriesId == CoreSeriesIds.Price;",
     "            bool readsRawBar = false;",
     "the three-disagreeing-prices defect restored: with HA on, the price line's summary reads "
     "an average of four prices that never traded while the browser title and the component "
     "path read the raw close"),

    ("J02", "a hidden component stops saying so on a left/right scan",
     A + "SpeechFormatter.cs",
     "                bool sayState = isYMove || !comp.IsVisible;",
     "                bool sayState = isYMove;",
     "arrowing along a hidden component gives a bare name and no values with nothing to explain "
     "the silence — the state IS the message for a component that has no value to read"),

    ("J03", "mute is never spoken, only hidden",
     A + "SpeechFormatter.cs",
     "                    ? VisibilityStateSpeech.Prefix(comp.IsVisible, comp.IsMuted)",
     "                    ? VisibilityStateSpeech.Prefix(comp.IsVisible, false)",
     "a muted component announces itself as though it were audible — the 2026-09-04 report "
     "('if I hide and mute both at once, if I unhide it should say muted but it doesn't')"),

    ("J04", "the date is never spoken when a reading crosses midnight",
     A + "SpeechFormatter.cs",
     "            bool dayChanged = _lastSpokenBarDay != day;",
     "            bool dayChanged = false;",
     "on an intraday chart every bar reads the bare time, so arrowing from 23:00 to 00:00 gives "
     "'23:00' then '00:00' with nothing to say a day was crossed"),

    ("J05", "a daily chart reads a meaningless time again",
     A + "SpeechFormatter.cs",
     "            if (PlaybackNarration.BarSeconds(state) >= 86400)",
     "            if (PlaybackNarration.BarSeconds(state) > 86400)",
     "daily bars fall out of the date-only branch and read '00:00' on every bar — the same "
     "time on every bar a daily chart has"),

    ("J06", "a sub-1 volume rounds to nothing",
     A + "SpeechFormatter.cs",
     "            return QuantityFormatter.Format(vol);",
     "            return vol.ToString(\"F0\", CultureInfo.InvariantCulture);",
     "a spot BTC candle carrying 0.35 BTC speaks 'Volume 0' and a bin holding 0.4 contracts "
     "speaks '0 contracts, 12.3 percent' — the crypto case, which is the common one here"),

    ("J07", "the candle components read the raw array instead of the bar as drawn",
     A + "SpeechFormatter.cs",
     "            if (sId == \"candles\")\n            {\n                double drawn = ChartMath.PriceComponentFallback(c, p);",
     "            if (false)\n            {\n                double drawn = ChartMath.PriceComponentFallback(c, p);",
     "with Heikin-Ashi on, the wick components answer with the RAW high and low while the "
     "series summary one keypress earlier described the HA candle — a lower wick of 19% "
     "reported for a candle drawn without one"),

    ("J08", "overbought and oversold swap",
     A + "SpeechFormatter.cs",
     "                if (role == LevelRole.Overbought && val >= lc.Value) return \"Overbought\";",
     "                if (role == LevelRole.Overbought && val <= lc.Value) return \"Overbought\";",
     "RSI at 80 speaks no zone and RSI at 20 speaks 'Overbought' — a directional claim a "
     "trader acts on, inverted"),

    ("J09", "a component that subscribes to NO level answers to all of them",
     A + "SpeechFormatter.cs",
     "            if (comp?.SubscribedLevelNames is not { } subs) return true;\n            if (subs.Count == 0) return false;",
     "            if (comp?.SubscribedLevelNames is not { } subs) return true;\n            if (subs.Count == 0) return true;",
     "on a pane like Aroon's, where Up and Down run 0-100 about 50 and the Oscillator runs "
     "+-100 about zero, a level belonging to one component starts speaking a zone for the others"),

    ("J10", "the band edge is the first one crossed rather than the tightest",
     A + "SpeechFormatter.cs",
     "                if (distance < bestDistance) { bestDistance = distance; band = label!; }",
     "                if (band.Length == 0) { bestDistance = distance; band = label!; }",
     "ADX at 60 says 'Strong' rather than 'Very Strong' — the reading names a band the value "
     "left some time ago"),

    ("J11", "a volume bar's direction inverts",
     A + "SpeechFormatter.cs",
     "            string dir = ctx.Pt.Close >= ctx.Pt.Open ? \"up\" : \"down\";",
     "            string dir = ctx.Pt.Close >= ctx.Pt.Open ? \"down\" : \"up\";",
     "the one word that says which way the bar the volume belongs to went, reversed — and the "
     "renderer still colours it the other way"),

    ("J12", "'N signals in view' counts signals that are not in view",
     A + "SpeechFormatter.cs",
     "            int end   = viewportStart < 0 || viewportLength <= 0 ? data.Length : Math.Min(data.Length, viewportStart + viewportLength);",
     "            int end   = data.Length;",
     "the landing announcement promises signals Ctrl+Left/Right cannot reach without panning — "
     "'12 signals in view' on a window holding two"),

    ("J13", "a sentiment phase is named one phase off",
     A + "SpeechFormatter.cs",
     "            int phaseIdx = Math.Clamp((int)Math.Round(ctx.Value), 0, AudioConstants.PhaseNames.Length - 1);",
     "            int phaseIdx = Math.Clamp((int)ctx.Value, 0, AudioConstants.PhaseNames.Length - 1);",
     "truncation instead of rounding, so a phase of 4.6 is spoken as phase 4's name — the "
     "component whose entire content is which phase it is in"),

    ("J14", "a profile says nothing until the first Up or Down",
     A + "SpeechFormatter.cs",
     "                return prefixMessage + ProfileLevels.Overview(series.ProfileBins);",
     "                return \"\";",
     "the series-switch prefix is discarded with the overview, so adding a profile announces "
     "NOTHING — Cody, 2026-09-11: 'the series name isn't read until I start moving around'"),

    ("J15", "a borrowed order-book snapshot is announced at the user's own time",
     A + "SpeechFormatter.cs",
     "            int cursorIdx = cursorDataIndex >= 0 ? cursorDataIndex : dataIndex;",
     "            int cursorIdx = dataIndex;",
     "standing on Tuesday's bar and hearing the live snapshot's 14:30 becomes indistinguishable "
     "from the book actually being Tuesday's — the one thing the user cannot cross-check"),

    # ── NarrationScanner: what speaks unprompted ─────────────────────────────
    ("J16", "the scan window reaches back past the seed",
     A + "NarrationScanner.cs",
     "                int scanFrom = Math.Max(seedCount, closedBound - PivotConfirmWindow);",
     "                int scanFrom = Math.Min(seedCount, closedBound - PivotConfirmWindow);",
     "pressing N announces history: the seed is the exclusive lower bound that stops the "
     "scanner reciting every signal already on the chart"),

    ("J17", "confirmed-bar narration fires on every intra-bar tick",
     A + "NarrationScanner.cs",
     "            if (!isBarClose) return;",
     "            if (false) return;",
     "overlay crosses, level crosses, the volume reading and the oscillator zones all speak off "
     "an UNCONFIRMED bar, so a wick that pokes through an EMA announces a cross that then unhappens"),

    ("J18", "a first sighting fires a cross",
     A + "NarrationScanner.cs",
     "                if (_lastPriceAboveOverlay.TryGetValue(key, out bool wasAbove) && wasAbove != nowAbove)",
     "                if (!_lastPriceAboveOverlay.TryGetValue(key, out bool wasAbove) || wasAbove != nowAbove)",
     "pressing N on a series announces 'price crossed above' for every overlay on it, at the "
     "moment the switch is flipped, about nothing that happened"),

    ("J19", "an oscillator in its own pane is compared against the price",
     A + "NarrationScanner.cs",
     "               && string.Equals(series.Pane, \"Main\", StringComparison.OrdinalIgnoreCase)\n               && string.IsNullOrEmpty(comp.SubPaneName)",
     "               && string.IsNullOrEmpty(comp.SubPaneName)",
     "'Price crossed above RSI 14 at 64,900' — two different Y axes compared as though they "
     "were one, announced in the same grammar as a real cross"),

    ("J20", "the volume reading at the close names the wrong direction",
     A + "NarrationScanner.cs",
     "                direction = bar.Close >= bar.Open ? \", up\" : \", down\";",
     "                direction = bar.Close >= bar.Open ? \", down\" : \", up\";",
     "the headless reading and the arrow-key reading of the same bar now disagree about which "
     "way it went"),

    ("J21", "a component subscribing to no level narrates every level",
     A + "NarrationScanner.cs",
     "            if (comp.SubscribedLevelNames is not { } subs) return true;\n            if (subs.Count == 0) return false;",
     "            if (comp.SubscribedLevelNames is not { } subs) return true;\n            if (subs.Count == 0) return true;",
     "the same widening as J09 on the narration path: crossings of levels belonging to another "
     "component of the same pane are announced as this one's"),

    # ── SeriesNarrationScope: who may speak at all ───────────────────────────
    ("J22", "an empty component selection silences the whole series",
     A + "SeriesNarrationScope.cs",
     "               && (!HasComponentSelection(series) || component.IsAutoNarrated);",
     "               && component.IsAutoNarrated;",
     "the AND this class exists to refuse: every series that exists today has narration on with "
     "no component flagged, so narration deletes itself for all of them"),

    ("J23", "a hidden, muted series narrates",
     A + "SeriesNarrationScope.cs",
     "            => series.IsAutoNarrated && series.IsVisible && !series.IsMuted;",
     "            => series.IsAutoNarrated;",
     "a series the user has switched off keeps talking — the exact complaint of 2026-09-05, "
     "'if a series or component is hidden, it should be excluded from the narration'"),

    ("J24", "a signal series starts reading a value on every bar as well",
     A + "SeriesNarrationScope.cs",
     "            if (series.Components.Any(c => IsMarkerDisplay(c.DisplayType) && !c.UsesGradientSpeech)) return null;",
     "            if (false) return null;",
     "a Cipher B narrating its markers also reads a running number at every close — the wall of "
     "speech the reading rule was scoped to avoid"),

    # ── PlaybackNarration: the words over the tones ──────────────────────────
    ("J25", "playback drops the rarest signal again",
     A + "PlaybackNarration.cs",
     "                .OrderBy(c => c.Fires)\n                .ThenBy(c => c.Order)",
     "                .OrderBy(c => c.Order)\n                .ThenBy(c => c.Fires)",
     "the gold-dot defect restored: Cipher B computes its Triple Confluence inside two other "
     "branches, so declaration order fills the two-clause ceiling with the routine markers and "
     "the rarest signal on the chart is never spoken in any playback"),

    ("J26", "playback speaks a signal on every bar",
     A + "PlaybackNarration.cs",
     "            => Math.Max(1, (int)Math.Ceiling(Math.Round(\n                MinSecondsBetweenLandmarks * BarsPerSecondAtUnitSpeed * Math.Max(0.1, speed), 6)));",
     "            => 1;",
     "at ten bars a second the rate limit is what keeps speech from running continuously over "
     "tones it is not about"),

    ("J27", "a landmark is spoken on every bar",
     A + "PlaybackNarration.cs",
     "            double minBars = MinSecondsBetweenLandmarks * barsPerSecond;",
     "            double minBars = 0;",
     "the unit is always Hour, so a 1-minute chart announces the clock sixty times a minute of "
     "playback instead of choosing a coarser calendar unit"),

    ("J28", "a completed playback reports as stopped",
     A + "PlaybackNarration.cs",
     "            && state.CurrentDataIndex >= state.Data.Count - 1;",
     "            && state.CurrentDataIndex > state.Data.Count - 1;",
     "'finished' and 'stopped' are different sentences on purpose — a user who hears 'finished' "
     "knows the whole range sounded, and now nobody ever does"),

    ("J29", "the jump to the start bar speaks a landmark",
     A + "PlaybackNarration.cs",
     "            if (!current.IsPlaying || current.IsPaused || !previous.IsPlaying || isFirstStep) return null;",
     "            if (!current.IsPlaying || current.IsPaused || !previous.IsPlaying) return null;",
     "the sequencer's first NavigateAction jumps the cursor from wherever the user left it to "
     "the plan's start, which is not a step through time, and the start sentence has already "
     "named that bar"),

    ("J30", "a level crossing is announced on every bar EXCEPT the crossing",
     A + "PlaybackNarration.cs",
     "                    if (nowAbove == (prev >= level.Value)) continue;",
     "                    if (nowAbove != (prev >= level.Value)) continue;",
     "ADX's 'very strong trend' is spoken on the four hundred bars where nothing happened and "
     "not on the handful where it crossed 25"),

    ("J31", "the silent-signals disclosure goes missing in the case it was written for",
     A + "PlaybackNarration.cs",
     "            var inScope = plan?.Series is { Count: > 0 } scoped\n                ? state.ActiveSeries.Where(s => scoped.Any(p => p.Id == s.Id)).ToList()\n                : state.ActiveSeries.ToList();",
     "            var inScope = state.ActiveSeries.ToList();",
     "playing ONE un-narrated series while some other series on the chart is flagged makes 'no "
     "series is set to narrate' false, so the sentence that explains the silence is withheld"),

    # ── ScanUtterance: the one utterance per scan ────────────────────────────
    ("J32", "the utterance is ordered by scan order rather than by consequence",
     A + "ScanUtterance.cs",
     "                .OrderBy(c => c.Tier).ThenBy(c => c.Order)",
     "                .OrderBy(c => c.Order)",
     "the five-clause cap then drops whatever was scanned last rather than the least "
     "consequential thing found, so a broken level loses its place to oscillator commentary"),

    ("J33", "the series name is dropped from the first clause and stutters on the rest",
     A + "ScanUtterance.cs",
     "                    if (prevSeries == clause.Series) text = text[prefix.Length..];",
     "                    if (prevSeries != clause.Series) text = text[prefix.Length..];",
     "exactly inverted: the opening clause arrives with no idea which series it is about, and "
     "every following clause about the same series repeats the name"),

    # ── DrawingSpeech: reading the user's own lines ──────────────────────────
    ("J34", "price above and below a drawing swap",
     A + "DrawingSpeech.cs",
     "            bool above = close > drawingValue;",
     "            bool above = close < drawingValue;",
     "'price above' on a line price is under, and the cross clause inverts with it — the "
     "narrator says the opposite of the chart about the user's own trend line"),

    ("J35", "'price on it' is decided by arithmetic rather than by the spoken price",
     A + "DrawingSpeech.cs",
     "            bool at = SpeechPriceFormatter.FormatPrice(close) == SpeechPriceFormatter.FormatPrice(drawingValue);",
     "            bool at = close == drawingValue;",
     "a line at 150.4999 under a close of 150.5001 is 'price above' while both read aloud as "
     "150.50 — indistinguishable by ear from a lie"),

    ("J36", "every component of a drawing reads its full name",
     A + "DrawingSpeech.cs",
     "            int keep = Math.Max(1, mine.Length - shared);",
     "            int keep = mine.Length;",
     "the seven fib levels go back to '0.0% Retracement', '23.6% Retracement' … — the shared "
     "tail repeated on every one of them, on every keypress"),

    # ── ProfileBinClassifier: the structure of a profile ─────────────────────
    ("J37", "one NaN bin silences every label in the profile",
     A + "ProfileBinClassifier.cs",
     "            var measured = allBins.Where(b => !double.IsNaN(b.TotalVolume)).ToList();",
     "            var measured = allBins.ToList();",
     "the mean becomes NaN, `mean > 0` is false, and every bin classifies as Normal, whose "
     "label is the empty string — no Point of Control, no Value Area High, no Low Volume Node, "
     "and nothing said to indicate the classifier gave up"),

    ("J38", "high and low volume nodes swap",
     A + "ProfileBinClassifier.cs",
     "                    if (bin.TotalVolume < mean * LvnThreshold)  return ProfileNodeType.LVN;",
     "                    if (bin.TotalVolume > mean * LvnThreshold)  return ProfileNodeType.LVN;",
     "almost every bin in the profile is announced as a Low Volume Node, including the busiest "
     "— and the sonification pitches them at 220 Hz to match"),

    # ── BinnedNavigationStrategy: moving through bins ────────────────────────
    ("J39", "Up and Down invert inside a profile",
     A + "BinnedNavigationStrategy.cs",
     "            int newBin = Math.Clamp(currentBin - delta, 0, binCount - 1);",
     "            int newBin = Math.Clamp(currentBin + delta, 0, binCount - 1);",
     "pressing Up walks DOWN the price bins — on the one series where the Y axis is the "
     "navigation axis"),

    ("J40", "left and right start moving a volume profile",
     A + "BinnedNavigationStrategy.cs",
     "                if (isProfile && !isHeatmap)\n                    return new NavigationResult(false);",
     "                if (isProfile && isHeatmap)\n                    return new NavigationResult(false);",
     "a profile aggregates across ALL time, so the cursor moves and the reading does not change "
     "— keys that appear to work and do nothing"),

    # ── NavigationFeedbackManager: the utterance around the value ────────────
    ("J41", "an empty chart answers arrow keys with silence",
     A + "NavigationFeedbackManager.cs",
     "                if (isUserInitiated)\n                    _speechRouter.Speak(\"No chart data yet.\", interrupt: true, SpeechChannel.Critical);",
     "                if (false)\n                    _speechRouter.Speak(\"No chart data yet.\", interrupt: true, SpeechChannel.Critical);",
     "a user cannot tell an empty chart from a dead keyboard — the silent-refusal class the "
     "feedback contract forbids"),

    ("J42", "support and resistance swap in the proximity clause",
     A + "NavigationFeedbackManager.cs",
     "                    if (LevelPolarity.IsResistance(zoneVal, bar.Close))",
     "                    if (!LevelPolarity.IsResistance(zoneVal, bar.Close))",
     "'near support at X' about a level above price — the 2026-08 BaseFrequency defect "
     "restored, and a directional claim a trader acts on"),

    ("J43", "the cross-series opt-out stops applying",
     A + "NavigationFeedbackManager.cs",
     "                if (series.Id != excludeSeriesId && !series.AnnounceAcrossSeries) continue;",
     "                if (false) continue;",
     "an indicator whose signals only mean something in its own context starts announcing them "
     "while the user reads the candles"),

    # ── VisibilityStateSpeech: the four-state lattice ────────────────────────
    ("J44", "hidden-and-muted reports as hidden only",
     A + "VisibilityStateSpeech.cs",
     "            (false, true) => \"hidden and muted\",",
     "            (false, true) => \"hidden\",",
     "the reported defect itself: two flags, two keys, and a readout that names one of them "
     "guarantees a second wrong guess"),

    # ── SignalClauseSpeech ───────────────────────────────────────────────────
    ("J45", "every signal clause stutters its own name",
     A + "SignalClauseSpeech.cs",
     "            if (clause.Contains(componentName, StringComparison.OrdinalIgnoreCase)) return clause;",
     "            if (false) return clause;",
     "'Bullish Divergence: Bullish divergence' on 47 of the 61 shipped templates, on every "
     "signal, in the ladder and in playback alike"),

    # ── CandlePatternSpeech: the one classifier ──────────────────────────────
    ("J46", "the forming bar is handed to the analyser as its own predecessor",
     A + "CandlePatternSpeech.cs",
     "            if (combined.Count > 0 && combined[^1].Date == forming.Date)\n                combined.RemoveAt(combined.Count - 1);",
     "            if (false)\n                combined.RemoveAt(combined.Count - 1);",
     "a bullish engulfing is tested against an earlier snapshot of the same bar, which it "
     "engulfs by construction as soon as the body grows — an engulfing announced on every "
     "growing bar"),

    ("J47", "the trailing context repeats the classified bar",
     A + "CandlePatternSpeech.cs",
     "                ? ChartMath.BarsAsDrawn(data, idx - 1, ContextBars - 1, heikinAshi)",
     "                ? ChartMath.BarsAsDrawn(data, idx, ContextBars - 1, heikinAshi)",
     "the same self-comparison on the ARROW-KEY route: the bar before the classified bar is the "
     "classified bar, so every multi-bar pattern is judged against a duplicate"),

    # ── BarDetailService: the direct question ────────────────────────────────
    ("J48", "a Bollinger squeeze is reported as an expansion",
     A + "BarDetailService.cs",
     "            if (pct < -0.10) return \"band squeezing, low volatility\";\n            if (pct >  0.10) return \"band expanding, volatility rising\";",
     "            if (pct > -0.10) return \"band squeezing, low volatility\";\n            if (pct <  0.10) return \"band expanding, volatility rising\";",
     "the volatility clause of the detail key inverts, so the setup a squeeze is traded for is "
     "announced whenever it is NOT happening"),

    # ── ChartLayoutDescriber: the orientation key ────────────────────────────
    ("J49", "the layout description counts what is visible as hidden",
     A + "ChartLayoutDescriber.cs",
     "            int hidden = state.ActiveSeries.SelectMany(s => s.Components).Count(c => !c.IsVisible);",
     "            int hidden = state.ActiveSeries.SelectMany(s => s.Components).Count(c => c.IsVisible);",
     "Alt+Shift+L exists to explain a silent or empty-looking chart; it now reports every "
     "working component as switched off and says nothing about the ones that are"),

    # ── The anchor nudge ─────────────────────────────────────────────────────
    ("J50", "an anchor cannot be nudged into the right margin",
     A + "DrawingInteractionManager.Nudge.cs",
     "                int maxIndex = data.Count - 1 + Math.Max(0, state.RightMarginBars);",
     "                int maxIndex = data.Count - 1;",
     "a trend line's forward anchor stops at the last bar, so the projection into empty space "
     "— the reason the right margin exists — cannot be built by keyboard"),
]


def run(cmd, cwd=REPO, timeout=3600):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build():
    code, out = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -v q --nologo")
    return code == 0, out


def test():
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
    ok = True
    for mid, area, relpath, find, repl, _ in MUTANTS:
        path = os.path.join(REPO, relpath)
        if not os.path.exists(path):
            print(f"{mid}: MISSING FILE {relpath}"); ok = False; continue
        src = open(path, encoding='utf-8-sig').read()
        n = src.count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences (need exactly 1) in {relpath}\n    {find[:120]!r}")
            ok = False
        if find == repl:
            print(f"{mid}: EQUIVALENT — find == replace"); ok = False
    print(f"all {len(MUTANTS)} anchors unique" if ok else "ANCHOR CHECK FAILED")
    return ok


def main():
    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)

    recover_inflight()
    only = [a for a in sys.argv[1:] if a.startswith("J")] or None
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
            results.append(rec); json.dump(results, open(OUT, 'w'), indent=1)
            print(f"{mid}: BAD ANCHOR ({n}) — {relpath}", flush=True); continue
        t0 = time.time()
        try:
            open(INFLIGHT, 'w').write(relpath)
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['status'] = 'NO_COMPILE'; rec['log'] = log[-1500:]
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
                print(f"{mid}: {rec['status']} failed={rec['failed']} ({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
            if os.path.exists(INFLIGHT):
                os.remove(INFLIGHT)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec); json.dump(results, open(OUT, 'w'), indent=1)

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
