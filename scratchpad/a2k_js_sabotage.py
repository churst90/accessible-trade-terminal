#!/usr/bin/env python3
"""A2k — the JS harness, mutated for the first time.

WHY. Every campaign so far (A2..A2j) mutated C#. The JavaScript the app runs in the browser
(keyboard.js, treeKeyboard.js, canvasRegion.js, and the WebHost's reconnect.js, audio.js,
webSpeech.js, webPush.js, service-worker.js) has NEVER been mutated, and it is where the
bug class that passed 8,000 green tests keeps living: Alt+T, Space cancelling every button,
the `;` keys that never reached .NET. Reading it for this campaign found two real defects
before a single mutant ran (fixed in 78891c03).

KILLERS, in order, short-circuiting on the first layer that goes red:
  1. the zero-dependency node suites in tools/jstests (seconds);
  2. the C# tests that read JS or wwwroot source (--no-build; they read the files);
  3. the FULL browser suite (build + ~3.5 min), only if 1 and 2 stayed green.
Survivors are then re-run against the FULL C# suite by a2k_survivor_check.py, so a C# reader
this filter missed cannot manufacture a survivor.

SELECTION RULE: one mutant per decision a user would notice: a key that stops reaching the
app, a dialog you can Tab out of, a button Space no longer presses, a gesture that zooms the
wrong way, an announcement that stops being said.

HARNESS RULES (sabotage-harness-rules): cp-restore + touch + diff; anchor must be UNIQUE; a
non-matching filter is a failure; never -v q; print failing NAMES; control run at the end;
do not touch the repo while it runs.
"""
import json, os, re, shutil, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2k_sabotage_results.json")
SCRATCH = os.environ.get("A2K_SCRATCH", "/tmp/a2k")
os.makedirs(SCRATCH, exist_ok=True)

K = "AccessibleTrader.BlazorClient.Components/wwwroot/js/keyboard.js"
T = "AccessibleTrader.BlazorClient.Components/wwwroot/js/treeKeyboard.js"
C = "AccessibleTrader.BlazorClient.Components/wwwroot/js/canvasRegion.js"
R = "AccessibleTrader.WebHost/wwwroot/js/reconnect.js"
A = "AccessibleTrader.WebHost/wwwroot/js/audio.js"
W = "AccessibleTrader.WebHost/wwwroot/js/webSpeech.js"
P = "AccessibleTrader.WebHost/wwwroot/js/webPush.js"
SW = "AccessibleTrader.WebHost/wwwroot/service-worker.js"

M = [
 # ── keyboard.js: Space activation ──────────────────────────────────────────
 ("K01", "Space on a <summary> plays the chart instead of opening it", K,
  "    if (tag === 'BUTTON' || tag === 'SUMMARY') return true;",
  "    if (tag === 'BUTTON') return true;"),
 ("K02", "a disabled role=button still counts as a Space target", K,
  "    if (target.hasAttribute && target.hasAttribute('disabled')) return false;", ""),
 ("K03", "Space no longer toggles a role=checkbox", K,
  "    'button', 'checkbox', 'switch',", "    'button', 'switch',"),
 ("K04", "Space activation carve-out removed: Space cancels every button", K,
  "            if (e.key === ' ' && !isModified && !isShifted && isSpaceActivationTarget(e.target)) return;", ""),

 # ── keyboard.js: modal stack, focus return, inert ──────────────────────────
 ("K05", "a pushed dialog forgets what opened it", K,
  "            next.push({ name: names[d], returnTo: document.activeElement });",
  "            next.push({ name: names[d], returnTo: null });"),
 ("K06", "closing a stacked dialog returns focus nowhere", K,
  "        if (removed && next.length > 0) this._returnFocusAfterClose(removed);", ""),
 ("K07", "the background is never inerted", K,
  "        const open = this._modalStack.length > 0;", "        const open = false;"),
 ("K08", "a non-top close yanks the user to the heading", K,
  "        if (top && top.contains(document.activeElement)) return;", ""),
 ("K09", "the top dialog is resolved from the BOTTOM of the stack", K,
  "        for (let i = stack.length - 1; i >= 0; i--) {",
  "        for (let i = 0; i < stack.length; i++) {"),
 ("K10", "visible-dialog filter reverts to offsetParent", K,
  "            .filter(el => el.getClientRects().length > 0);\n    },",
  "            .filter(el => el.offsetParent !== null);\n    },"),

 # ── keyboard.js: Tab trap ──────────────────────────────────────────────────
 ("K11", "Shift+Tab on the first control walks out of the dialog", K,
  "            } else if (e.shiftKey && idx === 0) {\n                e.preventDefault();\n                last.focus();",
  "            } else if (false) {\n                e.preventDefault();\n                last.focus();"),
 ("K12", "Tab on the last control walks out of the dialog", K,
  "            } else if (!e.shiftKey && idx === focusables.length - 1) {",
  "            } else if (false) {"),
 ("K13", "the opening heading is a one-keystroke hole in the trap", K,
  "            if (!inside || idx === -1) {", "            if (!inside) {"),
 ("K14", "<summary> is not a tab stop to the trap (Help unreadable)", K,
  "                'summary:not([tabindex=\"-1\"]), iframe", "                'iframe"),
 ("K15", "a roved-out <summary tabindex=-1> counts as a tab stop", K,
  "                'summary:not([tabindex=\"-1\"]), iframe", "                'summary, iframe"),
 ("K16", "the trap ignores alertdialog", K,
  "                document.querySelectorAll('[role=\"dialog\"], [role=\"alertdialog\"]'))",
  "                document.querySelectorAll('[role=\"dialog\"]'))"),

 # ── keyboard.js: which keys reach .NET ─────────────────────────────────────
 ("K17", "the pin keys ; and : stop reaching .NET", K,
  "                ';', ':',\n", ""),
 ("K18", "the Menu key stops opening the context menu", K,
  "                'ContextMenu',\n", ""),
 ("K19", "AltGr counts as a chord: å/€/@ swallowed in text fields", K,
  "            const isModified = (e.ctrlKey || e.altKey) && !isAltGr;",
  "            const isModified = (e.ctrlKey || e.altKey);"),
 ("K20", "Escape is swallowed by a focused text field", K,
  "            if ((isFormControl || isEditable) && !isModified && e.key !== 'Escape' && !isFunctionKey) return;",
  "            if ((isFormControl || isEditable) && !isModified && !isFunctionKey) return;"),
 ("K21", "F-keys are dead inside text fields again", K,
  "            if ((isFormControl || isEditable) && !isModified && e.key !== 'Escape' && !isFunctionKey) return;",
  "            if ((isFormControl || isEditable) && !isModified && e.key !== 'Escape') return;"),
 ("K22", "Shift+F10 in a text field loses the native context menu", K,
  "            const isFunctionKey = /^F\\d{1,2}$/.test(e.key) && !(e.key === 'F10' && isShifted);",
  "            const isFunctionKey = /^F\\d{1,2}$/.test(e.key);"),
 ("K23", "Alt+Shift+Arrow in a field is trapped (macOS select-by-word lost)", K,
  "            if ((isFormControl || isEditable) && isArrowKey && isShifted && !e.ctrlKey) return;", ""),
 ("K24", "scroll keys are trapped under a dialog (tall dialogs unreadable)", K,
  "                if (!owner) return;\n", ""),
 ("K25", "scroll keys are released even inside a tablist/tree", K,
  "                const owner = e.target.closest ? e.target.closest(ARROW_WIDGET_SELECTOR) : null;",
  "                const owner = null;"),
 ("K26", "single letters fire from anywhere, not only the chart", K,
  "            if (isSingleLetter && !self._chartFocused) return;", ""),
 ("K27", "modifier chords are no longer hard-stopped", K,
  "            if (isModified) e.stopImmediatePropagation();", ""),
 ("K28", "macOS Option chords lose the e.code fallback", K,
  "        key = e.code;\n    }", "    }"),
 ("K29", "Shift+/ ('?') no longer maps to OEM2", K,
  "    else if (key === '?') key = 'OEM2';", ""),
 ("K30", "releasing an arrow never stops the sustained navigation tone", K,
  "                dotnetHelper.invokeMethodAsync('OnKeyUp', e.key);", ""),
 ("K31", "shifted arrows are released as scroll keys under a dialog", K,
  "            if (self._openModalCount > 0 && !isModified && !(isShifted && isArrowKey)",
  "            if (self._openModalCount > 0 && !isModified"),

 # ── keyboard.js: mouse, wheel, touch ───────────────────────────────────────
 ("K32", "a drag released off the chart never ends", K,
  "            if (!buttonDown) return;\n            buttonDown = false;",
  "            return;"),
 ("K33", "the wheel zooms the wrong way", K,
  "            const direction = e.deltaY < 0 ? 1 : -1;", "            const direction = e.deltaY < 0 ? -1 : 1;"),
 ("K34", "Shift+wheel pans the wrong way", K,
  "                    dotnetHelper.invokeMethodAsync('OnWheelPan', delta > 0 ? 1 : -1);",
  "                    dotnetHelper.invokeMethodAsync('OnWheelPan', delta > 0 ? -1 : 1);"),
 ("K35", "right-click shows the browser's menu over the chart", K,
  "        el.addEventListener('contextmenu', function (e) {\n            e.preventDefault();",
  "        el.addEventListener('contextmenu', function (e) {"),
 ("K36", "pinch-spread zooms out", K,
  "                    dotnetHelper.invokeMethodAsync('OnWheel', 1, centroidFraction);",
  "                    dotnetHelper.invokeMethodAsync('OnWheel', -1, centroidFraction);"),
 ("K37", "a double-tap is never recognised", K,
  "                const isDoubleTap = (now - touchState.lastTapAt) < DOUBLE_TAP_MS",
  "                const isDoubleTap = false"),
 ("K38", "long-press never opens the context menu", K,
  "                    touchState.mode = 'consumed';   // long-press: eat the touchend\n",
  "                    return;\n"),
 ("K39", "Explore mode pans instead of speaking", K,
  "            if (e.touches.length === 1 && window.accessibleTrader._touchExploreMode) {",
  "            if (false) {"),
 ("K40", "a tap is a drag: slop ignored", K,
  "                if (Math.hypot(x - touchState.startX, y - touchState.startY) < TAP_SLOP_PX) return;", ""),

 # ── keyboard.js: helpers called from .NET ──────────────────────────────────
 ("K41", "focusElement gives up after one frame (Alt+T race returns)", K,
  "        let framesLeft = 10;", "        let framesLeft = 1;"),
 ("K42", "focusElement yanks focus back after the user moved it", K,
  "            if (document.activeElement !== startedOn) return;\n            requestAnimationFrame(attempt);",
  "            requestAnimationFrame(attempt);"),
 ("K43", "focusElement treats 'found' as success (inert refusal not retried)", K,
  "                if (document.activeElement === el || el.contains(document.activeElement)) return;",
  "                return;"),
 ("K44", "the chart never learns it lost focus", K,
  "        this._chartFocused = !!focused;", "        if (focused) this._chartFocused = true;"),
 ("K45", "text size is not clamped", K,
  "        const clamped = Math.min(250, Math.max(50, percent | 0));", "        const clamped = percent | 0;"),
 ("K46", "a desktop with a coarse primary pointer shows the touch bar", K,
  "            && !window.matchMedia('(any-pointer: fine)').matches;", ";"),
 ("K47", "the chart renders at 1x on a HiDPI screen", K,
  "        return [rect.width, rect.height, window.devicePixelRatio || 1];",
  "        return [rect.width, rect.height, 1];"),

 # ── treeKeyboard.js ────────────────────────────────────────────────────────
 ("T01", "a stale aria-expanded outranks <details open>", T,
  "        if (d) return d.open;", "        if (d && !treeitem.hasAttribute('aria-expanded')) return d.open;"),
 ("T02", "items inside a closed <details> are walked into", T,
  "                if (p.tagName === 'DETAILS' && !p.open) return false;", ""),
 ("T03", "the roving tabindex is never reset (many Tab stops)", T,
  "            for (let i = 0; i < all.length; i++) all[i].setAttribute('tabindex', '-1');", ""),
 ("T04", "ArrowRight on a collapsed group skips past instead of expanding", T,
  "                if (isGroup(current) && !isExpanded(current)) {", "                if (false) {"),
 ("T05", "ArrowLeft never reaches the parent", T,
  "                const parent = findParent(current, tree);", "                const parent = null;"),
 ("T06", "a shifted arrow is taken by the tree (anchor nudge lost)", T,
  "        if (e.shiftKey || e.ctrlKey || e.altKey || e.metaKey) return;",
  "        if (e.ctrlKey || e.altKey || e.metaKey) return;"),
 ("T07", "End goes to the first item", T,
  "                focusTreeitem(items[items.length - 1]);", "                focusTreeitem(items[0]);"),
 ("T08", "Space on a leaf does nothing", T,
  "                else activatePrimary(current);", ""),

 # ── canvasRegion.js ────────────────────────────────────────────────────────
 ("C01", "the reported bottom inset is the rect's bottom, not the gap below it", C,
  "        const bottom = Math.max(0, window.innerHeight - rect.bottom);",
  "        const bottom = Math.max(0, rect.bottom);"),
 ("C02", "scrolling no longer re-reports the chart rect", C,
  "            window.addEventListener('scroll', scheduleReport, { capture: true, passive: true });", ""),
 ("C03", "a chart scrolled above the viewport reports a negative top", C,
  "        const top = Math.max(0, rect.top);", "        const top = rect.top;"),

 # ── reconnect.js ───────────────────────────────────────────────────────────
 ("R01", "a failed reconnect says nothing", R,
  "                return 'Could not reconnect to the terminal. The server may be restarting. ' +\n"
  "                       'Press the Retry now button to try again, or wait and it will retry on its own.';",
  "                return '';"),
 ("R02", "a repeated sentence is written unchanged (and may not re-announce)", R,
  "        if (text === lastAnnounced) text += ' ';", ""),
 ("R03", "an attempt-counter bump is not re-announced", R,
  "                if (state === lastState && state !== 'show') return;",
  "                if (state === lastState) return;"),
 ("R04", "the reassurance about orders is dropped", R,
  "                       ' Your chart and any orders already placed are unaffected.';", "                       '';"),

 # ── audio.js ───────────────────────────────────────────────────────────────
 ("A01", "left and right channels are swapped", A,
  "            left[i]  = interleaved[i * 2];\n            right[i] = interleaved[i * 2 + 1];",
  "            left[i]  = interleaved[i * 2 + 1];\n            right[i] = interleaved[i * 2];"),
 ("A02", "a resync seam is not declicked", A,
  "        if (resync) {", "        if (false) {"),
 ("A03", "chunks pushed while suspended burst-play later", A,
  "            // Drop this chunk rather than queueing into a context that may never\n            // start — would burst-play once it does.\n            return;",
  "            // (mutant) fall through"),
 ("A04", "overrun is never resynced (latency grows without bound)", A,
  "        if (nextStartTime < now + 0.005 || nextStartTime > now + MAX_LEAD) {",
  "        if (nextStartTime < now + 0.005) {"),

 # ── webSpeech.js ───────────────────────────────────────────────────────────
 ("W01", "an interrupting announcement no longer interrupts", W,
  "                window.speechSynthesis.cancel();", ""),
 ("W02", "the speech-output choice is read from a different key than it is saved to", W,
  "        try { return window.localStorage.getItem('att.speechOutput') || ''; }",
  "        try { return window.localStorage.getItem('att.speechMode') || ''; }"),

 # ── webPush.js / service-worker.js ─────────────────────────────────────────
 ("P01", "the push subscription sends p256dh and auth swapped", P,
  "                        p256dh: json.keys.p256dh,\n                        auth: json.keys.auth,",
  "                        p256dh: json.keys.auth,\n                        auth: json.keys.p256dh,"),
 ("P02", "every alert notification replaces the last (one shared tag)", SW,
  "        tag: 'att-' + body,", "        tag: 'att',"),
]

NODE_SUITES = ["gesture-tests.mjs", "keyboard-tests.mjs", "tree-tests.mjs", "boot-tests.mjs"]
CSHARP_FILTER = "|".join("FullyQualifiedName~" + c for c in [
    "MouseHandlerWiringTests", "ShortcutManagerTests", "ModalCloseDispatchTests",
    "PointerToPlotMappingTests", "DrawingInteractionManagerMouseDispatchTests",
    "ModalContractScanTests", "ShortcutReachabilityTests", "ChromeAccessibilityScanTests",
    "ModalBackgroundRegionScanTests", "ObjectTreeDisclosureStateTests", "TradingDashboardModalTests",
    "DrawingContextMenuKeyboardTests", "WebHostSpeechManagerForwardingTests", "BrowserAudioBridgeTests",
    "HostParityTests", "DialogContrastScanTests", "SharedStylesheetParityTests", "ThemeCoverageTests",
    "VisuallyHiddenClassTests", "WebHostStaticAssetManifestTests"])

FAIL_RE = re.compile(r"^\s+Failed (\S+)", re.M)


def sh(cmd, timeout=1800):
    p = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True, timeout=timeout, shell=isinstance(cmd, str))
    return p.returncode, p.stdout + p.stderr


def node_layer():
    failing = []
    for s in NODE_SUITES:
        rc, out = sh(["node", f"tools/jstests/{s}"])
        names = [l.strip() for l in out.splitlines() if l.strip().startswith("FAIL")]
        if rc != 0 and not names:
            names = [f"{s}: exit {rc} " + out.strip().splitlines()[-1][:120] if out.strip() else f"{s}: exit {rc}"]
        failing += [f"{s}: {n}" for n in names]
    return failing


def dotnet_layer(project, filt=None, build=False):
    if build:
        rc, out = sh(["dotnet", "build", project, "-p:UseRazorSourceGenerator=false"])
        if rc != 0:
            return None, "BUILD FAILED: " + "\n".join(l for l in out.splitlines() if " error " in l)[:500]
    cmd = ["dotnet", "test", project, "--no-build", "-p:UseRazorSourceGenerator=false"]
    if filt:
        cmd += ["--filter", filt]
    rc, out = sh(cmd)
    if "No test matches" in out:
        return None, "FILTER MATCHED NOTHING"
    summ = re.findall(r"(Passed!|Failed!)\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+)", out)
    if not summ:
        return None, "NO SUMMARY LINE:\n" + out[-800:]
    return FAIL_RE.findall(out), summ[-1]


def apply(path, old, new):
    full = os.path.join(REPO, path)
    s = open(full, encoding="utf-8").read()
    n = s.count(old)
    if n != 1:
        raise RuntimeError(f"anchor occurs {n} times in {path}: {old[:70]!r}")
    open(full, "w", encoding="utf-8").write(s.replace(old, new))


def restore(path):
    full = os.path.join(REPO, path)
    shutil.copy(os.path.join(SCRATCH, path.replace("/", "__")), full)
    os.utime(full, None)


def main():
    only = set(sys.argv[1:])
    files = sorted({m[2] for m in M})
    for f in files:
        shutil.copy(os.path.join(REPO, f), os.path.join(SCRATCH, f.replace("/", "__")))

    # Anchor pre-flight: every mutant must apply uniquely BEFORE anything runs.
    bad = []
    for mid, _, f, old, new in M:
        s = open(os.path.join(REPO, f), encoding="utf-8").read()
        if s.count(old) != 1:
            bad.append(f"{mid}: {s.count(old)} matches")
    if bad:
        print("ANCHOR PRE-FLIGHT FAILED:\n  " + "\n  ".join(bad)); sys.exit(2)
    print(f"pre-flight: {len(M)} anchors unique", flush=True)
    if os.environ.get("A2K_PREFLIGHT_ONLY"):
        return

    results = json.load(open(OUT)) if os.path.exists(OUT) and only else {}
    for mid, desc, f, old, new in M:
        if only and mid not in only:
            continue
        t0 = time.time()
        apply(f, old, new)
        try:
            layer, failing, note = None, [], ""
            nf = node_layer()
            if nf:
                layer, failing = "node", nf
            else:
                cf, summ = dotnet_layer("AccessibleTrader.Tests/AccessibleTrader.Tests.csproj", CSHARP_FILTER)
                if cf is None:
                    note = f"C# layer unusable: {summ}"
                elif cf:
                    layer, failing = "csharp", cf
                if layer is None:
                    bf, summ = dotnet_layer("AccessibleTrader.BrowserTests/AccessibleTrader.BrowserTests.csproj", build=True)
                    if bf is None:
                        note += f" browser layer unusable: {summ}"
                    elif bf:
                        layer, failing = "browser", bf
        finally:
            restore(f)
        results[mid] = {"desc": desc, "file": f, "caught": layer is not None, "layer": layer,
                        "failing": failing[:25], "note": note.strip(), "secs": round(time.time() - t0)}
        print(f"{mid} {'CAUGHT by ' + layer if layer else 'SURVIVED'} ({results[mid]['secs']}s) "
              f"{failing[:4]} {note}", flush=True)
        json.dump(results, open(OUT, "w"), indent=1)

    # CONTROL: nothing mutated, every layer green, and every file byte-identical.
    for f in files:
        if open(os.path.join(REPO, f), "rb").read() != open(os.path.join(SCRATCH, f.replace("/", "__")), "rb").read():
            print(f"!! {f} NOT restored byte-identical"); sys.exit(3)
    print("all files restored byte-identical", flush=True)
    if not only:
        nf = node_layer()
        cf, cs = dotnet_layer("AccessibleTrader.Tests/AccessibleTrader.Tests.csproj", CSHARP_FILTER)
        bf, bs = dotnet_layer("AccessibleTrader.BrowserTests/AccessibleTrader.BrowserTests.csproj", build=True)
        print(f"CONTROL node={nf} csharp={cf} {cs} browser={bf} {bs}", flush=True)
        results["_control"] = {"node": nf, "csharp": cf, "browser": bf, "browser_summary": str(bs)}
        json.dump(results, open(OUT, "w"), indent=1)


if __name__ == "__main__":
    main()
