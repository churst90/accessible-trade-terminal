#!/usr/bin/env python3
"""A2 — sabotage campaign against the test suite.

Each mutant is a single plausible regression in production code. For each one:
apply, build, run the FULL suite, record whether anything went red and which
tests did, then revert. A mutant that survives is a hole in the suite, not a
bug in the app.

The full suite is the instrument on purpose: the question A2 asks is whether a
GREEN SUITE means the app works, so a mutant only counts as caught if *some*
test anywhere fails.
"""
import json, os, re, subprocess, sys, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2_sabotage_results.json")

# (id, area, file, find, replace, what-it-breaks)
MUTANTS = [
    ("M01", "money/backtest honesty",
     "AccessibleTrader.Core/Services/Trading/BarFill.cs",
     "            return executableAtOpen ? barOpen : level;",
     "            return level;",
     "a gapped stop books the loss at the level the market skipped"),

    ("M02", "money/order validation",
     "AccessibleTrader.Core/Services/Trading/ProtectiveLevelValidator.cs",
     "bool mustBeBelow = level == ProtectiveLevel.StopLoss ? isLong : !isLong;",
     "bool mustBeBelow = level == ProtectiveLevel.StopLoss ? isLong : isLong;",
     "a short's take-profit rule stops inverting; the exact bug the file exists to prevent"),

    ("M03", "speech formatting",
     "AccessibleTrader.Core/Services/Accessibility/SpeechPriceFormatter.cs",
     "int decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(abs)), 2, 10);",
     "int decimals = 2;",
     "sub-dollar assets narrate as 0.00 again"),

    ("M04", "speech formatting",
     "AccessibleTrader.Core/Services/Accessibility/QuantityFormatter.cs",
     '                abs >= 0.01  ? "N4" :        //           0.0340',
     '                abs >= 0.01  ? "N2" :        //           0.0340',
     "0.0340 and 0.0350 both speak as 0.03"),

    ("M05", "money/position sizing",
     "AccessibleTrader.Core/Strategies/RiskPercentPositionSizer.cs",
     "double riskAmount = accountBalance * _riskPercent / 100.0;",
     "double riskAmount = accountBalance * _riskPercent;",
     "every risk-percent position is sized 100x too large"),

    ("M06", "release gate",
     "AccessibleTrader.Core/Services/Trading/WithdrawalReleasePolicy.cs",
     "services?.GetService(typeof(WithdrawalReleasePolicy)) as WithdrawalReleasePolicy ?? Shipped;",
     "services?.GetService(typeof(WithdrawalReleasePolicy)) as WithdrawalReleasePolicy ?? new WithdrawalReleasePolicy(true);",
     "an unregistered host opens the withdrawal gate instead of closing it"),

    ("M07", "indicator warmup",
     "AccessibleTrader.Sdk/Indicators/IndicatorMath.cs",
     "                r[i] = warmup < period ? double.NaN : ema;",
     "                r[i] = warmup < period - 1 ? double.NaN : ema;",
     "EMA emits one bar earlier than its warmup allows"),

    ("M08", "indicator math",
     "AccessibleTrader.Sdk/Indicators/IndicatorMath.cs",
     "            double k = 2.0 / (period + 1.0);",
     "            double k = 2.0 / period;",
     "EMA smoothing factor is wrong for every period"),

    ("M09", "sandbox blocklist",
     "AccessibleTrader.Core/Services/RoslynScriptingService.cs",
     '            "System.IO",\n            "System.Net",',
     '            "System.Net",',
     "user scripts may touch the filesystem"),

    ("M10", "sandbox blocklist",
     "AccessibleTrader.Core/Services/RoslynScriptingService.cs",
     '            "System.Type.GetType",\n',
     '',
     "string-keyed type lookup defeats every namespace filter"),

    ("M11", "modal focus",
     "AccessibleTrader.BlazorClient.Components/WalletModal.razor",
     '        try { await JSRuntime.InvokeVoidAsync("accessibleTrader.focusElement", "wallet-asset"); }\n        catch { /* focus is best-effort; Tab still reaches it */ }',
     '',
     "the Wallet modal opens without moving focus into it — the Alt+T bug class"),

    ("M12", "earcon mute tiers",
     "AccessibleTrader.Core/Services/Accessibility/EarconService.cs",
     "            if (!breakThroughMutes && !AmbientEarconsAudible()) return;",
     "            if (!AmbientEarconsAudible()) return;",
     "a margin-call alert marked break-through is silenced by the earcon mute"),

    ("M13", "order speech",
     "AccessibleTrader.Core/Services/Accessibility/AccessibilityFeedbackCoordinator.cs",
     '                _speechRouter.Speak($"Order rejected for {e.Order.Symbol}.{why}", interrupt: true, channel: SpeechChannel.OrderEvent);',
     '                _ = why;',
     "an order rejection is never spoken at all"),

    ("M14", "alerts",
     "AccessibleTrader.Core/Services/AlertEvaluator.cs",
     "AlertCondition.CrossesAbove   => !double.IsNaN(prevValue) && prevValue < (alert.Threshold ?? 0) && currentValue >= (alert.Threshold ?? 0),",
     "AlertCondition.CrossesAbove   => !double.IsNaN(prevValue) && currentValue >= (alert.Threshold ?? 0),",
     "a crossing alert degrades to a level alert and re-fires on every bar"),

    ("M15", "moving averages",
     "AccessibleTrader.Core/Services/Indicators/MovingAverageHelper.cs",
     "                r[i] = cnt == period ? sum / period : double.NaN;",
     "                r[i] = cnt > 0 ? sum / cnt : double.NaN;",
     "SMA silently averages a short window when the source has gaps"),

    ("M16", "order fill side",
     "AccessibleTrader.Core/Services/Trading/BarFill.cs",
     "            side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;",
     "            side;",
     "a long's stop exit is priced as if it were a buy"),

    ("M17", "speech routing",
     "AccessibleTrader.Core/Services/Accessibility/AccessibilityFeedbackCoordinator.cs",
     '                _speechRouter.Speak(FormatFill("Order filled", e.Order), interrupt: true, channel: SpeechChannel.OrderEvent);',
     '                _speechRouter.Speak(FormatFill("Order filled", e.Order), interrupt: false, channel: SpeechChannel.OrderEvent);',
     "a fill queues behind chatter instead of interrupting it"),

    ("M18", "protective entry",
     "AccessibleTrader.Core/Services/Trading/ProtectiveLevelValidator.cs",
     "            if (!double.IsFinite(value) || value <= 0)",
     "            if (!double.IsFinite(value))",
     "a stop loss of 0 or a negative price is accepted"),

    # ---- batch 2: different suite mechanisms, not different code ----

    ("M19", "accessible name in markup",
     "AccessibleTrader.BlazorClient.Components/AlertsModal.razor",
     '<button @onclick="Close" aria-label="Close alerts dialog">Close</button>',
     '<button @onclick="Close">Close</button>',
     "a dialog button loses its accessible name"),

    ("M20", "keyboard shortcut table",
     "AccessibleTrader.Core/Services/ShortcutManager.cs",
     's.Add(new(SystemCommand.OpenTradingDashboard, "T", Alt: true)); // Alt+T',
     's.Add(new(SystemCommand.OpenTradingDashboard, "Y", Alt: true)); // Alt+T',
     "the documented Alt+T shortcut silently becomes Alt+Y"),

    ("M21", "order outcome classification",
     "AccessibleTrader.Core/Services/GeneralOrderService.cs",
     '            result.StartsWith("ORDER_", StringComparison.Ordinal)\n            || result.StartsWith("PROVIDER_", StringComparison.Ordinal);',
     '            result.StartsWith("ORDER_", StringComparison.Ordinal);',
     "a PROVIDER_* failure sentinel is treated as an order id"),

    ("M22", "WebHost security headers",
     "AccessibleTrader.WebHost/Services/SecurityPolicy.cs",
     '        + "frame-ancestors \'none\'";',
     '        + "frame-ancestors *";',
     "the strict CSP stops forbidding framing — clickjacking"),

    ("M23", "crash-safe persistence",
     "AccessibleTrader.Core/Services/AtomicFile.cs",
     "                File.Move(tempPath, finalPath, overwrite: true);",
     "                File.Copy(tempPath, finalPath, overwrite: true);",
     "the write stops being atomic and leaves its temp file behind"),

    ("M24", "indicator causality",
     "AccessibleTrader.Core/Services/Indicators/SwingStructureProvider.cs",
     "            for (int i = 0; i <= index && i < highs.Length && i < lows.Length; i++)",
     "            for (int i = 0; i <= index + 1 && i < highs.Length && i < lows.Length; i++)",
     "swing narration reads one bar into the future"),

    ("M25", "focus ring contrast",
     "AccessibleTrader.Core/Services/Theming/ThemeCssBridge.cs",
     "            Luminance(theme.SurfaceRaised) > 0.5 ? new SKColor(0, 32, 176) : new SKColor(255, 255, 0);",
     "            Luminance(theme.SurfaceRaised) > 0.5 ? new SKColor(255, 255, 0) : new SKColor(0, 32, 176);",
     "the focus ring is yellow on light chrome and blue on dark — invisible on both"),

    ("M26", "text contrast",
     "AccessibleTrader.Core/Services/Theming/ThemeCssBridge.cs",
     "            Luminance(surface) > 0.5 ? new SKColor(12, 15, 20) : new SKColor(255, 255, 255);",
     "            Luminance(surface) > 0.5 ? new SKColor(255, 255, 255) : new SKColor(12, 15, 20);",
     "ink is near-black on dark surfaces and white on light ones"),

    # ---- batch 3: predicted survivors, filed 2026-08-24 as unverified ----
    # Every BacktestConfig in the suite sets CommissionRate: 0 and SlippagePercent: 0
    # (10 constructions, 3 files, no exceptions). If that observation is right, the
    # cost model can be deleted outright without turning the suite red.

    ("M27", "backtest cost model",
     "AccessibleTrader.Core/Strategies/StrategyBacktester.cs",
     "                double commission = fillPrice * qty * config.CommissionRate;",
     "                double commission = 0;",
     "entry commission is never charged"),

    ("M28", "backtest cost model",
     "AccessibleTrader.Core/Strategies/StrategyBacktester.cs",
     "                double slippage = fillPrice * config.SlippagePercent;\n                fillPrice += signal.Side == OrderSide.Buy ? slippage : -slippage;",
     "                double slippage = fillPrice * config.SlippagePercent;\n                fillPrice -= signal.Side == OrderSide.Buy ? slippage : -slippage;",
     "slippage is applied in the trader's favour on every entry"),
]


def run(cmd, timeout=1800):
    return subprocess.run(cmd, shell=True, cwd=REPO, capture_output=True, text=True, timeout=timeout)


def build():
    r = run("dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
            "-p:UseRazorSourceGenerator=false -v:q --nologo")
    return r.returncode == 0, r.stdout[-4000:] + r.stderr[-2000:]


def test():
    r = run("dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
            "-p:UseRazorSourceGenerator=false --no-build --nologo")
    return r.returncode, r.stdout


FAILED_RE = re.compile(r'^\s*(?:\[xUnit[^\]]*\]\s*)?(?:Failed|\s+Failed)\s+(\S+)', re.M)
SUMMARY_RE = re.compile(r'Failed:\s*(\d+),\s*Passed:\s*(\d+)')


def main():
    only = sys.argv[1:] or None
    results = []
    if os.path.exists(OUT):
        results = json.load(open(OUT))
    done = {r['id'] for r in results}

    for mid, area, relpath, find, repl, breaks in MUTANTS:
        if only and mid not in only:
            continue
        if mid in done:
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
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original.replace(find, repl))
            ok, log = build()
            if not ok:
                rec['status'] = 'NO_COMPILE'
                rec['log'] = log[-1500:]
                print(f"{mid}: DID NOT COMPILE", flush=True)
            else:
                code, out = test()
                m = SUMMARY_RE.search(out)
                rec['failed'] = int(m.group(1)) if m else -1
                rec['passed'] = int(m.group(2)) if m else -1
                names = sorted(set(re.findall(r'^\s*Failed\s+([A-Za-z0-9_.]+)', out, re.M)))
                rec['failing_tests'] = names[:40]
                rec['status'] = 'CAUGHT' if rec['failed'] > 0 else 'SURVIVED'
                print(f"{mid}: {rec['status']} failed={rec['failed']} "
                      f"({time.time()-t0:.0f}s) — {area}", flush=True)
                if names:
                    print("      " + "; ".join(names[:6]), flush=True)
        finally:
            with open(path, 'w', encoding='utf-8') as fh:
                fh.write(original)
            os.utime(path, None)
        rec['seconds'] = round(time.time() - t0)
        results.append(rec)
        json.dump(results, open(OUT, 'w'), indent=1)

    # restore a clean build at the end
    build()
    print("\n=== summary")
    for r in results:
        print(f"  {r['id']} {r['status']:>10}  {r['area']}")


if __name__ == '__main__':
    main()
