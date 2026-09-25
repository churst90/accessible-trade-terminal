#!/usr/bin/env python3
"""A2p — mutation campaign over `AccessibleTrader.Sdk`.

WHY THIS AREA.

The Sdk is ~9,900 lines in 113 files and is referenced by the Core, every plugin and every head,
so a silent Sdk defect lands everywhere. Before A2p only five of its files had ever been mutated
(IndicatorMath, LevelConfig, RateLimiter, RestSigning, SymbolValidator). No mutant here repeats
one of those earlier sites (a2 / a2d / a2e / fresh): the RateLimiter and IndicatorMath mutants
below are on different lines.

SELECTION RULE: weighted toward what can hurt a user or their money (a deposit address shown
for the wrong network, a stream that goes dead while the UI says Connected, a live bar with the
wrong volume, a backtest with a look-ahead leaf, a risk default ten times larger) and toward
files never mutated. Each mutant is a single plausible edit: off-by-one, inverted/dropped
condition, wrong operand, dropped call, wrong default. Several are RESTORATIONS of defects the
source comments say were fixed once.

METHOD. Three independent copies of the tree (rsync, no bin/obj, no .git), each with its own
build output, each baselined to the full suite first. One worker thread per copy pulls mutants
off a queue: back up the file (byte copy), apply the edit, build, run the FULL
AccessibleTrader.Tests suite, record the failing tests' FULL display names and first error
line, restore from the byte copy and `touch`. Never two mutants in one tree; never a source
edit in a tree while its tests run.

HARNESS RULES (each learned from a run that lied):
  1. Baseline first. A run whose passed+failed is short of the baseline total is ABORTED (a
     crashed host prints a green summary for the tests it reached) and is re-queued, never
     scored.
  2. Anchor asserted to occur EXACTLY ONCE before patching; NO_COMPILE / BAD_ANCHOR are
     UNVERIFIED, never results.
  3. Restore with a byte copy + touch; never `git checkout --`; byte-compare at the end.
  4. NEVER `-v q` on `dotnet test`.
  5. Theory names contain spaces and quotes: the full display name is captured up to the
     trailing `[duration]`, not with a regex that stops at a space.
  6. A CONTROL run (nothing sabotaged) ends every tree's work.

Usage:
  a2p_sabotage.py --verify          anchors unique in the worktree
  a2p_sabotage.py --setup           create/refresh the tree copies, build and baseline each
  a2p_sabotage.py [P01 W02 ...]     run the campaign (all pending mutants by default)
"""
import filecmp, json, os, queue, re, shutil, subprocess, sys, threading, time

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(REPO, "scratchpad", "a2p_sabotage_results.json")
BASELINE_OUT = os.path.join(REPO, "scratchpad", "a2p_baseline.json")
TREES_ROOT = "/home/cody/.cache/a2p-trees"
N_TREES = 3
BASELINE_TOTAL = 8208          # full suite on the unmutated worktree, 2026-09-25
SUMMARY_RE = re.compile(r"Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)")
FAILED_LINE_RE = re.compile(r"^\s+Failed (.+?) \[[^\]]*\]\s*$", re.M)

SDK = "AccessibleTrader.Sdk/"

MUTANTS = [
    # ── Trading/CryptoAddressValidator.cs — a deposit address shown for the wrong network ──
    ("P01", "network names matched by raw substring again",
     SDK + "Trading/CryptoAddressValidator.cs",
     "            if (names.Any(n => net == n)) return true;",
     "            if (names.Any(n => net.Contains(n))) return true;",
     "restores the TETHER→ETH bug class: 'WBTC (ERC20)' contains BTC and is routed to the Bitcoin check, which refuses a correct 0x deposit address"),

    ("P02", "bech32m (taproot) checksum no longer accepted",
     SDK + "Trading/CryptoAddressValidator.cs",
     "            if (checksum != 1 && checksum != 0x2bc830a3)",
     "            if (checksum != 1)",
     "every bc1p taproot deposit address is declared corrupt and hidden"),

    ("P03", "EVM length check only rejects SHORT addresses",
     SDK + "Trading/CryptoAddressValidator.cs",
     "            if (a.Length != 42)",
     "            if (a.Length < 42)",
     "a 43-character 0x string (one extra character) is shown as a structurally valid deposit address"),

    ("P04", "Tron addresses no longer required to start with T",
     SDK + "Trading/CryptoAddressValidator.cs",
     '            if (Looks(net, "TRON", "TRC20"))         return Base58Check(address, "a Tron address", expectPrefix: "T");',
     '            if (Looks(net, "TRON", "TRC20"))         return Base58Check(address, "a Tron address");',
     "a Bitcoin 1… address returned for a TRC20 deposit passes base58check and is shown as VERIFIED — USDT sent there is lost"),

    ("P05", "only VERIFIED addresses are displayable",
     SDK + "Trading/CryptoAddressValidator.cs",
     "        public bool IsDisplayable => Result != AddressCheck.Malformed;",
     "        public bool IsDisplayable => Result == AddressCheck.Verified;",
     "every ETH/ERC20/Solana deposit address (StructureOnly) and every unknown network is hidden"),

    ("P06", "every '1' in a base58 address counts as a leading zero",
     SDK + "Trading/CryptoAddressValidator.cs",
     "                if (c != '1') break;",
     "                if (c != '1') continue;",
     "legacy addresses with an interior '1' fail their checksum and are hidden"),

    # ── Services/ReconnectingWebSocket.cs — the live feed ─────────────────────
    ("W01", "giving up no longer reports a disconnect",
     SDK + "Services/ReconnectingWebSocket.cs",
     "                            _onDisconnected?.Invoke();\n                            return;",
     "                            return;",
     "restores the dead-feed-says-Connected bug: the chart just stops and the UI says the provider is fine"),

    ("W02", "a successful reconnect does not reset the attempt counter",
     SDK + "Services/ReconnectingWebSocket.cs",
     "                            await ConnectInternalAsync(ct).ConfigureAwait(false);\n                            reconnectAttempts = 0;",
     "                            await ConnectInternalAsync(ct).ConfigureAwait(false);",
     "failures accumulate across a whole session: after ten blips spread over days the socket gives up for good"),

    ("W03", "the heartbeat ignores the configured keepalive payload",
     SDK + "Services/ReconnectingWebSocket.cs",
     "                        var pingBytes = Encoding.UTF8.GetBytes(_heartbeatMessage);",
     '                        var pingBytes = Encoding.UTF8.GetBytes("ping");',
     "MEXC's {\"method\":\"PING\"} is never sent; idle sockets are dropped by the venue"),

    ("W04", "heartbeat declares death one failure late",
     SDK + "Services/ReconnectingWebSocket.cs",
     "                    if (consecutiveFailures >= MaxConsecutiveHeartbeatFailures)",
     "                    if (consecutiveFailures > MaxConsecutiveHeartbeatFailures)",
     "a half-open connection is noticed after four failed pings (two minutes) rather than three"),

    ("W05", "public SendAsync writes to the socket directly again",
     SDK + "Services/ReconnectingWebSocket.cs",
     "            await SendFrameAsync(bytes, ct).ConfigureAwait(false);",
     "            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);",
     "restores the overlapping-SendAsync bug: a subscribe that lands on the heartbeat tick throws and the chart never updates"),

    ("W06", "oversize check is per FRAME, not per message",
     SDK + "Services/ReconnectingWebSocket.cs",
     "                        if (ms.Length + result.Count > MaxMessageBytes)",
     "                        if (result.Count > MaxMessageBytes)",
     "a hostile endpoint streams an unbounded fragmented message and OOMs the app"),

    ("W07", "reconnect no longer cancels the previous generation's loops",
     SDK + "Services/ReconnectingWebSocket.cs",
     "            try { retired.Cancel(); } catch (ObjectDisposedException) { return; }",
     "            // cancel dropped",
     "a ConnectAsync after a symbol switch leaves the old receive loop running: two loops, duplicate messages"),

    # ── Services/RateLimiter.cs (new sites; a2/a2d mutated the cap and the 4xx range) ──
    ("R01", "HttpClient timeouts are not retried",
     SDK + "Services/RateLimiter.cs",
     "                return !ct.IsCancellationRequested;",
     "                return false;",
     "every transient HTTP timeout on a GET fails straight to the user instead of retrying"),

    ("R02", "after a full window the request count is not reset",
     SDK + "Services/RateLimiter.cs",
     "                        await Task.Delay(waitTime, ct).ConfigureAwait(false);\n                    _requestCount = 0;",
     "                        await Task.Delay(waitTime, ct).ConfigureAwait(false);",
     "once saturated, EVERY later request waits a whole window: throughput collapses to one per window"),

    # ── Plugins/ProviderResult.cs + TransportFailure.cs — what the user is told ──
    ("PR1", "a 403 is not a permission error",
     SDK + "Plugins/ProviderResult.cs",
     "                return code == System.Net.HttpStatusCode.Unauthorized\n                    || code == System.Net.HttpStatusCode.Forbidden;",
     "                return code == System.Net.HttpStatusCode.Unauthorized;",
     "a key lacking a scope is reported as 'failed' (retry later) instead of 'refused — check the key's permissions'"),

    ("PR2", "FromException classification inverted",
     SDK + "Plugins/ProviderResult.cs",
     "            IsPermissionError(ex)\n                ? ProviderResult<T>.NotPermitted",
     "            !IsPermissionError(ex)\n                ? ProviderResult<T>.NotPermitted",
     "every timeout says 'check your API key permissions'; every real 401 says 'failed'"),

    ("TF1", "429 treated as permanent",
     SDK + "Plugins/TransportFailure.cs",
     "                    if (code is 408 or 429) return true;",
     "                    if (code is 408) return true;",
     "a rate-limited venue is reported as a hard failure instead of a transient one"),

    # ── Models/TimeframeUtility.cs ────────────────────────────────────────────
    ("T01", "month period end approximated as 30 days",
     SDK + "Models/TimeframeUtility.cs",
     '            if (match.Groups[2].Value == "M")\n                return DateTime.SpecifyKind(periodStart, DateTimeKind.Utc).AddMonths(value);',
     '            if (match.Groups[2].Value == "MM")\n                return DateTime.SpecifyKind(periodStart, DateTimeKind.Utc).AddMonths(value);',
     "a 31-day month looks CLOSED a day early — a still-forming monthly bar is treated as final"),

    ("T02", "weekly bars aligned to Sunday",
     SDK + "Models/TimeframeUtility.cs",
     "                long offsetMs = 4 * 86400000L; // Offset from Thu to Mon",
     "                long offsetMs = 3 * 86400000L; // Offset from Thu to Mon",
     "every weekly bar starts on Sunday: weekly OHLC disagrees with every venue"),

    ("T03", "base timeframe picks the SMALLEST divisor",
     SDK + "Models/TimeframeUtility.cs",
     "                .OrderByDescending(x => x.Ms)",
     "                .OrderBy(x => x.Ms)",
     "a 4h chart is built from 1m bars: 240x the requests and a far shorter history"),

    # ── Models/TimestampParser.cs ─────────────────────────────────────────────
    ("TS1", "nanosecond epochs divided by 1000 only once",
     SDK + "Models/TimestampParser.cs",
     "                    while (ts > MaxPlausibleMilliseconds) ts /= 1000;",
     "                    if (ts > MaxPlausibleMilliseconds) ts /= 1000;",
     "restores the year-57000 bar: a nanosecond venue timestamp read as milliseconds"),

    ("TS2", "Local DateTimes relabelled instead of converted",
     SDK + "Models/TimestampParser.cs",
     "                return dt.Kind == DateTimeKind.Local",
     "                return dt.Kind == DateTimeKind.Unspecified",
     "restores the bars-in-the-future bug: a Local wall-clock reading is stamped UTC"),

    # ── Models/MarketKey.cs ───────────────────────────────────────────────────
    ("MK1", "a bare category is adopted as its own sub-type",
     SDK + "Models/MarketKey.cs",
     '            return parts.Count > 1 ? parts[parts.Count - 1] : "";',
     '            return parts.Count > 0 ? parts[parts.Count - 1] : "";',
     "restores the Crypto|Crypto|Crypto|Spot growth: SubType(\"Crypto\") = \"Crypto\""),

    # ── Models/TimeSeriesBuffer.cs + Collections/CircularBuffer.cs ────────────
    ("TB1", "ReplaceLast writes into the shared array again",
     SDK + "Models/TimeSeriesBuffer.cs",
     "            var copy = new T[_data.Length];\n            Array.Copy(_data, copy, Count);\n            copy[Count - 1] = item;",
     "            var copy = _data;\n            copy[Count - 1] = item;",
     "restores the torn-bar bug: every reader holding an earlier buffer sees the live tick's write"),

    ("TB2", "RemoveFirst drops the LAST element",
     SDK + "Models/TimeSeriesBuffer.cs",
     "            Array.Copy(_data, 1, newArray, 0, Count - 1);",
     "            Array.Copy(_data, 0, newArray, 0, Count - 1);",
     "trimming the oldest bar deletes the newest one"),

    ("CB1", "a full circular buffer never advances its head",
     SDK + "Collections/CircularBuffer.cs",
     "                // Buffer is full, the head moves forward to evict the oldest item\n                _head = (_head + 1) % _capacity;",
     "                // Buffer is full, the head moves forward to evict the oldest item",
     "once full, index 0 is the NEWEST item and the order is rotated"),

    # ── Models/WindowedBars.cs — the requested window ─────────────────────────
    ("WB1", "limit keeps the OLDEST bars",
     SDK + "Models/WindowedBars.cs",
     "                bars = bars.Skip(drop).ToList();",
     "                bars = bars.Take(request.Limit).ToList();",
     "a request for the N bars ending at Until gets the N oldest, and volumes drift from their bars"),

    ("WB2", "the bar AT Until is excluded",
     SDK + "Models/WindowedBars.cs",
     "                    if (until.HasValue && bars[i].Date > until.Value) continue;",
     "                    if (until.HasValue && bars[i].Date >= until.Value) continue;",
     "the last bar of a date-ranged request is silently missing"),

    ("WB3", "an empty window is not explained",
     SDK + "Models/WindowedBars.cs",
     "                if (keptBars.Count == 0)",
     "                if (keptBars.Count < 0)",
     "a chart scrolled outside the venue's window goes blank with no word about which dates exist"),

    # ── Models/BarBucketConsolidator.cs + Ohlcv.UpdateWith — the live bar ────
    ("BB1", "cumulative ticks re-add the running total",
     SDK + "Models/BarBucketConsolidator.cs",
     "                    double volumeDelta = tick.Date == _sourceBarDate",
     "                    double volumeDelta = false",
     "restores kline volume inflation: every ~1s update re-adds the running volume"),

    ("BB2", "replayed older ticks restart the bucket",
     SDK + "Models/BarBucketConsolidator.cs",
     "                if (_bucket is { } current && periodStart < current.Date)\n                    return null;\n",
     "",
     "a reconnect replay throws away the accumulated current period"),

    ("OH1", "a trade tick replaces the bar's volume instead of adding",
     SDK + "Models/Ohlcv.cs",
     "                Volume = this.Volume + tick.Volume",
     "                Volume = tick.Volume",
     "the live bar's volume is the last trade's size"),

    # ── Models/ComponentCausality.cs — look-ahead in backtests ────────────────
    ("CC1", "Undeclared components are publishable",
     SDK + "Models/ComponentCausality.cs",
     "            Effective(indicator, component) == ComponentCausality.Causal;",
     "            Effective(indicator, component) != ComponentCausality.Lookahead;",
     "every component nobody vetted becomes a strategy leaf — the fake-edge assumption"),

    ("CC2", "a component's own causality is ignored",
     SDK + "Models/ComponentCausality.cs",
     "            return component.Causality ?? indicator.Causality;",
     "            return indicator.Causality;",
     "Ichimoku's Chikou Span (Lookahead on a Causal indicator) is published as a strategy leaf"),

    ("AP1", "whole-day metrics published the same day",
     SDK + "Plugins/AnalyticsPublicationLag.cs",
     "            => DateTime.SpecifyKind(metricDay.Date, DateTimeKind.Utc).AddDays(1);",
     "            => DateTime.SpecifyKind(metricDay.Date, DateTimeKind.Utc);",
     "a daily metric is visible at 00:00 of the day it describes — look-ahead in every backtest reading it"),

    ("SF1", "a two-letter base wins the quote split",
     SDK + "Models/SymbolFormat.cs",
     "                if (b.Length >= 3) return (b, q);",
     "                if (b.Length >= 2) return (b, q);",
     "XBTUSD splits as XB/TUSD: the wrong symbol sent to the venue"),

    # ── Wrong defaults ────────────────────────────────────────────────────────
    ("D1", "min reward/risk default 0.5",
     SDK + "Strategies/RiskPlan.cs",
     "    double MinRewardRiskRatio = 1.5,",
     "    double MinRewardRiskRatio = 0.5,",
     "setups that risk twice what they target ring the bell"),

    ("D2", "position-sizing risk default 5% (not 0.5%)",
     SDK + "Strategies/RiskPlan.cs",
     "    double RiskPercent = 0.005,",
     "    double RiskPercent = 0.05,",
     "every default-sized trade risks ten times what the plan says"),

    ("D3", "alert cooldown default zero",
     SDK + "Alerts/AlertDefinition.cs",
     "    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(30);",
     "    public TimeSpan Cooldown { get; init; } = TimeSpan.Zero;",
     "a repeating alert speaks on every tick"),

    ("D4", "every alert pierces the ambient mutes by default",
     SDK + "Alerts/AlertDefinition.cs",
     "    public bool BreakThroughMutes { get; init; } = false;",
     "    public bool BreakThroughMutes { get; init; } = true;",
     "Shift+F2/Shift+F3 mute nothing"),

    ("D5", "speech starts OFF",
     SDK + "Models/WorkspaceState.cs",
     "            IsSpeechEnabled: true,",
     "            IsSpeechEnabled: false,",
     "a blind user's first run is silent"),

    ("D6", "backtest commission default is ten times too low",
     SDK + "Strategies/BacktestConfig.cs",
     "    double CommissionRate = 0.001,      // 0.1% per trade",
     "    double CommissionRate = 0.0001,     // 0.1% per trade",
     "every default backtest under-charges commission tenfold and flatters the strategy"),

    # ── Services/PluginHostServices.cs ────────────────────────────────────────
    ("PH1", "fallback HttpClient ignores the caller's timeout",
     SDK + "Services/PluginHostServices.cs",
     "                Timeout = timeout ?? TimeSpan.FromSeconds(60),",
     "                Timeout = TimeSpan.FromSeconds(60),",
     "a provider asking for a 10 s timeout hangs for 60"),

    ("PH2", "fallback HttpClient has no response cap",
     SDK + "Services/PluginHostServices.cs",
     "                MaxResponseContentBufferSize = maxResponseBytes,\n",
     "",
     "a hostile CDN can OOM the app on the no-host-bridge path"),

    # ── Theming ───────────────────────────────────────────────────────────────
    ("TH1", "translucent colours serialised without alpha",
     SDK + "Theming/ThemePreset.cs",
     "            : $\"#{c.Red:x2}{c.Green:x2}{c.Blue:x2}{c.Alpha:x2}\";",
     "            : $\"#{c.Red:x2}{c.Green:x2}{c.Blue:x2}\";",
     "volume bars and value-area fills become opaque blocks over price after a theme round-trip"),

    ("TH2", "default muted text drops below WCAG AA",
     SDK + "Theming/ChartTheme.cs",
     "    public SKColor TextMuted { get; init; } = new(170, 170, 170);",
     "    public SKColor TextMuted { get; init; } = new(100, 100, 100);",
     "muted text on the default sunken surface is ~3.2:1 — unreadable for low-vision users"),

    # ── Indicators/IndicatorMath.cs (fresh lines) ─────────────────────────────
    ("IM1", "ATR seed averages one bar short",
     SDK + "Indicators/IndicatorMath.cs",
     "            for (int i = 1; i <= period; i++) sum += tr[i];",
     "            for (int i = 1; i < period; i++) sum += tr[i];",
     "every ATR (and every ATR stop) is biased low from its first value"),

    ("IM2", "true range ignores a gap DOWN",
     SDK + "Indicators/IndicatorMath.cs",
     "                r[i] = Math.Max(hl, Math.Max(hpc, lpc));",
     "                r[i] = Math.Max(hl, hpc);",
     "a gap-down bar's true range is understated: ATR stops too tight after gaps"),
]


# ─────────────────────────────────────────────────────────────────────────────

def run(cmd, cwd, timeout=5400):
    p = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True, timeout=timeout)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def build(tree):
    code, out = run("nice -n 5 dotnet build AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
                    "-p:UseRazorSourceGenerator=false -nr:false -v q --nologo", tree)
    return code == 0, out


def test(tree, filt=None):
    cmd = ("nice -n 5 dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj "
           "-p:UseRazorSourceGenerator=false --no-build")
    if filt:
        cmd += f' --filter "{filt}"'
    return run(cmd, tree)


def parse(out):
    """-> dict(failed, passed, skipped, total, failing=[full display names], messages={name: first line})"""
    m = SUMMARY_RE.search(out)
    res = {'failed': -1, 'passed': -1, 'skipped': -1, 'total': -1}
    if m:
        res.update(failed=int(m.group(1)), passed=int(m.group(2)),
                   skipped=int(m.group(3)), total=int(m.group(4)))
    names, msgs = [], {}
    lines = out.splitlines()
    for i, ln in enumerate(lines):
        fm = FAILED_LINE_RE.match(ln)
        if not fm:
            continue
        name = fm.group(1)
        if name not in names:
            names.append(name)
        # "  Error Message:" then the message on the following line(s)
        for j in range(i + 1, min(i + 6, len(lines))):
            if lines[j].strip() == "Error Message:" and j + 1 < len(lines):
                msgs.setdefault(name, lines[j + 1].strip()[:400])
                break
    res['failing'] = names
    res['messages'] = msgs
    res['no_match'] = "No test matches the given testcase filter" in out
    return res


def tree_path(i):
    return os.path.join(TREES_ROOT, f"t{i}")


def backup_dir(i):
    return os.path.join(TREES_ROOT, f"backup{i}")


def bpath(i, rel):
    return os.path.join(backup_dir(i), rel.replace("/", "__"))


def verify(root=REPO):
    ok = True
    for mid, area, rel, find, repl, _ in MUTANTS:
        path = os.path.join(root, rel)
        if not os.path.exists(path):
            print(f"{mid}: MISSING FILE {rel}"); ok = False; continue
        src = open(path, encoding='utf-8', newline='').read()
        n = src.count(find)
        if n != 1:
            print(f"{mid}: {n} occurrences (need exactly 1) in {rel}\n    {find[:120]!r}"); ok = False
        if find == repl:
            print(f"{mid}: find == replace"); ok = False
    ids = [m[0] for m in MUTANTS]
    if len(set(ids)) != len(ids):
        print("DUPLICATE IDS"); ok = False
    print(f"all {len(MUTANTS)} anchors unique in {root}" if ok else "ANCHOR CHECK FAILED")
    return ok


def setup():
    os.makedirs(TREES_ROOT, exist_ok=True)
    results = {}
    lock = threading.Lock()

    def one(i):
        t = tree_path(i)
        os.makedirs(t, exist_ok=True)
        code, out = run(f"rsync -a --delete --exclude bin/ --exclude obj/ --exclude .git "
                        f"--exclude scratchpad/ ./ {t}/", REPO)
        assert code == 0, out
        os.makedirs(os.path.join(t, "scratchpad"), exist_ok=True)
        ok, log = build(t)
        if not ok:
            with lock: results[i] = {'build': False, 'log': log[-2000:]}
            return
        _, out = test(t)
        p = parse(out)
        with lock:
            results[i] = {'build': True, **{k: p[k] for k in ('failed', 'passed', 'skipped', 'total', 'failing')}}
        print(f"tree t{i}: {p['failed']} failed / {p['passed']} passed / total {p['total']}", flush=True)

    ths = [threading.Thread(target=one, args=(i,)) for i in range(1, N_TREES + 1)]
    for th in ths: th.start()
    for th in ths: th.join()
    json.dump({'repo_baseline_total': BASELINE_TOTAL, 'trees': results}, open(BASELINE_OUT, 'w'), indent=1)
    good = all(r.get('build') and r['failed'] == 0 and r['total'] == BASELINE_TOTAL for r in results.values())
    print("all trees baselined green at", BASELINE_TOTAL if good else "BASELINE PROBLEM", flush=True)
    return good


def apply_run_restore(i, mid, rel, find, repl, filt=None):
    """Apply one mutant in tree i, build, test, restore. Returns a record."""
    t = tree_path(i)
    path = os.path.join(t, rel)
    os.makedirs(backup_dir(i), exist_ok=True)
    shutil.copyfile(path, bpath(i, rel))
    original = open(path, encoding='utf-8', newline='').read()
    n = original.count(find)
    rec = {'occurrences': n}
    if n != 1:
        rec['status'] = 'BAD_ANCHOR'
        return rec
    try:
        with open(path, 'w', encoding='utf-8', newline='') as fh:
            fh.write(original.replace(find, repl))
        ok, log = build(t)
        if not ok:
            rec['status'] = 'NO_COMPILE'
            rec['log'] = "\n".join(l for l in log.splitlines() if "error" in l)[-1500:]
            return rec
        _, out = test(t, filt)
        p = parse(out)
        rec.update({k: p[k] for k in ('failed', 'passed', 'skipped', 'total', 'failing', 'messages', 'no_match')})
        if p['failed'] < 0:
            rec['status'] = 'UNPARSED'; rec['log'] = out[-2000:]
        elif filt is None and p['failed'] + p['passed'] < BASELINE_TOTAL:
            rec['status'] = 'ABORTED'; rec['log'] = out[-2000:]
        else:
            rec['status'] = 'CAUGHT' if p['failed'] > 0 else 'SURVIVED'
        return rec
    finally:
        shutil.copyfile(bpath(i, rel), path)
        os.utime(path, None)


def campaign(only):
    results = json.load(open(OUT)) if os.path.exists(OUT) else []
    done = {r['id'] for r in results if r['status'] in ('CAUGHT', 'SURVIVED', 'NO_COMPILE', 'BAD_ANCHOR')}
    results = [r for r in results if r['id'] in done]
    q = queue.Queue()
    for m in MUTANTS:
        if (only and m[0] not in only) or m[0] in done:
            continue
        q.put((m, 0))
    lock = threading.Lock()

    def worker(i):
        while True:
            try:
                m, attempt = q.get_nowait()
            except queue.Empty:
                return
            mid, area, rel, find, repl, why = m
            t0 = time.time()
            rec = apply_run_restore(i, mid, rel, find, repl)
            rec.update(id=mid, area=area, file=rel, find=find, replace=repl, rationale=why,
                       tree=f"t{i}", seconds=round(time.time() - t0), attempt=attempt)
            if rec['status'] in ('ABORTED', 'UNPARSED') and attempt < 2:
                print(f"{mid}: {rec['status']} in t{i} (attempt {attempt}) — re-queued", flush=True)
                q.put((m, attempt + 1))
                continue
            with lock:
                results.append(rec)
                results.sort(key=lambda r: [mm[0] for mm in MUTANTS].index(r['id']))
                json.dump(results, open(OUT, 'w'), indent=1)
            print(f"{mid}: {rec['status']} failed={rec.get('failed')} total={rec.get('total')} "
                  f"({rec['seconds']}s, t{i}) — {area}", flush=True)
            for name in rec.get('failing', [])[:6]:
                print(f"      {name}", flush=True)

    ths = [threading.Thread(target=worker, args=(i,)) for i in range(1, N_TREES + 1)]
    for th in ths: th.start()
    for th in ths: th.join()
    return results


def control_and_restore_check():
    ctrl = {}
    lock = threading.Lock()

    def one(i):
        t = tree_path(i)
        ok, _ = build(t)
        _, out = test(t)
        p = parse(out)
        same = True
        for rel in sorted({m[2] for m in MUTANTS}):
            a = os.path.join(t, rel)
            if not filecmp.cmp(a, os.path.join(REPO, rel), shallow=False):
                same = False; print(f"t{i}: {rel} DIFFERS from the worktree")
            b = bpath(i, rel)
            if os.path.exists(b) and not filecmp.cmp(a, b, shallow=False):
                same = False; print(f"t{i}: {rel} DIFFERS from its backup")
        with lock:
            ctrl[f"t{i}"] = {'build': ok, 'failed': p['failed'], 'passed': p['passed'], 'total': p['total'],
                             'failing': p['failing'], 'restored_byte_identical': same}
        print(f"CONTROL t{i}: build_ok={ok} failed={p['failed']} passed={p['passed']} total={p['total']} "
              f"{'restored byte-identical' if same else 'TREE NOT RESTORED'}", flush=True)

    ths = [threading.Thread(target=one, args=(i,)) for i in range(1, N_TREES + 1)]
    for th in ths: th.start()
    for th in ths: th.join()
    return ctrl


def main():
    if "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)
    if "--setup" in sys.argv:
        assert verify()
        sys.exit(0 if setup() else 1)
    if "--control" in sys.argv:
        ctrl = control_and_restore_check()
        data = json.load(open(OUT))
        json.dump(data, open(OUT, 'w'), indent=1)
        json.dump(ctrl, open(os.path.join(REPO, "scratchpad", "a2p_control.json"), 'w'), indent=1)
        return
    assert verify()
    for i in range(1, N_TREES + 1):
        assert verify(tree_path(i)), f"tree t{i} anchors"
    only = [a for a in sys.argv[1:] if not a.startswith("-")] or None
    results = campaign(only)
    ctrl = control_and_restore_check()
    json.dump(ctrl, open(os.path.join(REPO, "scratchpad", "a2p_control.json"), 'w'), indent=1)
    print("\n=== summary")
    for r in results:
        print(f"  {r['id']:>4} {r['status']:>10}  {r['area']}")
    caught = sum(1 for r in results if r['status'] == 'CAUGHT')
    surv = [r['id'] for r in results if r['status'] == 'SURVIVED']
    total = caught + len(surv)
    if total:
        print(f"\nraw catch rate {caught}/{total} = {100*caught/total:.1f}%   survivors: {surv}")


if __name__ == '__main__':
    main()
