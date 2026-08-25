using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// What the probe concluded about a script strategy.
    /// </summary>
    /// <param name="StrategyId">The script's <c>Id</c>.</param>
    /// <param name="Refused">
    /// True when the strategy demonstrably read a bar that had not happened, rewrote its decisions
    /// when older bars arrived, or gave two different answers to the same question. A refused
    /// strategy does not load.
    /// </param>
    /// <param name="Findings">What to tell the author. Empty when nothing was found.</param>
    /// <param name="Notes">
    /// Things worth saying that are not refusals — chiefly "this emitted no orders at all on the
    /// check series, so nothing about it was established".
    /// </param>
    /// <param name="SignalsSeen">Orders the strategy emitted across the whole probe.</param>
    public record ScriptStrategyCausalityReport(
        string StrategyId,
        bool Refused,
        IReadOnlyList<string> Findings,
        IReadOnlyList<string> Notes,
        int SignalsSeen)
    {
        public static ScriptStrategyCausalityReport Clean(string id, int signals) =>
            new(id, false, Array.Empty<string>(), Array.Empty<string>(), signals);
    }

    /// <summary>
    /// Proves — or refutes — that a script strategy decides each bar from that bar's past, by
    /// running it and comparing the ORDERS it emits.
    ///
    /// <para>
    /// <see cref="CustomIndicatorCausalityProbe"/> does this for scripted indicators by comparing
    /// component arrays. A strategy has no component arrays, and it never passes through
    /// <c>SignalCatalog</c>, so the gate that stops <c>ICHIMOKU.Chikou Span</c> from becoming a
    /// condition does not apply to it at all. What a strategy does have is a decision per bar, and
    /// a decision is comparable: the same bars in must produce the same orders on the same bars
    /// out. That is the whole contract, and it is checked the same two ways the indicator contract
    /// is checked.
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><b>Prefix.</b> Run over the first k bars and over all of them. An order that appears,
    /// disappears or changes at a shared bar was decided using a bar that had not happened yet.
    /// The vector here is not subtle: <c>state.Data</c> holds the WHOLE series from the first
    /// <c>OnBar</c> call — the backtester hands it over intact, exactly as this probe does — so
    /// <c>state.Data[i + 1].Close</c> compiles, runs, and backtests like genius.</item>
    /// <item><b>Suffix.</b> Run over all the bars and over <c>bars.Skip(k)</c>, comparing by bar
    /// DATE rather than by index. A strategy that anchors on an array index — the shape a Pine
    /// port arrives in, where <c>bar_index</c> transliterates to <c>history[n]</c> — decides
    /// differently once the user scrolls back and older bars are prepended.</item>
    /// <item><b>Determinism.</b> The full series is run twice first. A strategy that consults a
    /// clock or a random number cannot be backtested at all, and it would otherwise be reported as
    /// a look-ahead, which is a different accusation and the wrong one.</item>
    /// </list>
    ///
    /// <para>
    /// Same epistemics as the indicator probe: measurement can refute a claim of causality and can
    /// never prove one. A strategy that emits no orders on the check series has established
    /// nothing — that is reported as a note, not as a pass, because silence is not evidence. The
    /// probe hands the strategy an empty <see cref="WorkspaceState.ActiveSeries"/>, so a strategy
    /// whose conditions read indicator components will be one of those silent cases; closing that
    /// gap means computing a component stack per run and is filed rather than faked.
    /// </para>
    /// </summary>
    public static class ScriptStrategyCausalityProbe
    {
        /// <summary>
        /// Bars per probe run. Shorter than the indicator probe's 700 because a strategy is driven
        /// one bar at a time — this is 400 <c>OnBar</c> calls per run and seven runs — and a
        /// strategy's decision surface settles faster than a recursive filter's output.
        /// </summary>
        public const int ProbeLength = 400;

        private static readonly int[] PrefixLengths = { 150, 260 };
        private static readonly int[] SuffixDrops = { 17, 61 };

        /// <summary>
        /// Bars of the shorter suffix run to ignore. A strategy legitimately builds internal state
        /// from the bars it has been shown, so its first decisions after a shorter warmup may
        /// differ for reasons that are not causality violations at all.
        /// </summary>
        private const int SuffixWarmup = 120;

        /// <summary>
        /// The two check series, matching the indicator probe: one regular hourly series and one
        /// irregularly spaced. Flavour 3 is not optional — a strategy that asks the data what
        /// timeframe it is on cannot be caught by a series whose every gap is the same.
        /// </summary>
        private static readonly int[] Flavours = { 0, 3 };

        private static readonly ChartIdentity ProbeIdentity =
            new("Spot", "probe", "PROBE/USD", "1h");

        /// <summary>
        /// Wall-clock budget for the whole probe.
        ///
        /// <para>
        /// This runs inside <c>CompileStrategyAsync</c>, and <c>StrategyAutoLoader</c> calls that
        /// once per armed script at app start. Twelve runs of 400 bars is nothing for an ordinary
        /// strategy — the whole suite of these finishes in under a second — but a strategy that
        /// recomputes a long indicator on every bar is quadratic in what it was handed, and a gate
        /// that hangs startup would be a worse bug than the one it catches. The budget is checked
        /// BETWEEN runs (a synchronous <c>OnBar</c> cannot be interrupted from outside), and
        /// running out is reported as a note saying what was and was not established — never as a
        /// pass, and never as a refusal.
        /// </para>
        /// </summary>
        public static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

        /// <param name="budget">
        /// Overrides <see cref="Budget"/>. Exists so a test can prove the budget path without
        /// spending eight real seconds to do it — production never passes it.
        /// </param>
        public static ScriptStrategyCausalityReport Probe(ITradingStrategy prototype, TimeSpan? budget = null)
        {
            ArgumentNullException.ThrowIfNull(prototype);
            var limit = budget ?? Budget;

            string id = prototype.Id ?? "";
            var findings = new List<string>();
            var notes = new List<string>();
            int signalsSeen = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool ranOut = false;

            bool OutOfTime()
            {
                if (clock.Elapsed < limit) return false;
                ranOut = true;
                return true;
            }

            foreach (int flavour in Flavours)
            {
                if (OutOfTime()) break;
                var full = CausalityProbeSeries.Bars(flavour, ProbeLength);

                Trace whole;
                try { whole = Run(prototype, full); }
                catch (Exception ex)
                {
                    return Failure(id,
                        $"The strategy threw while being checked, on {full.Count} ordinary bars: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
                signalsSeen += whole.Signals.Count;

                // ── Does it give the same answer twice? ──────────────────────────────────────
                Trace repeat;
                try { repeat = Run(prototype, full); }
                catch (Exception ex)
                {
                    return Failure(id,
                        $"The strategy threw on its second run over the same {full.Count} bars: " +
                        $"{ex.GetType().Name}: {ex.Message}. Whatever it carries between runs, it " +
                        $"cannot be replayed, and a strategy that cannot be replayed cannot be backtested.");
                }

                var wobble = FirstDifference(whole, repeat, whole.Dates, skip: 0);
                if (wobble != null)
                    return Failure(id,
                        $"The strategy gave two different answers to the same {full.Count} bars: " +
                        $"{wobble}. Something outside the bars is reaching the decision — a clock, a " +
                        $"random number, or state left over from a previous run. Nothing else can be " +
                        $"checked until that is fixed, and a backtest of it would mean nothing.");

                // ── Does a bar's decision change when the FUTURE arrives? ────────────────────
                foreach (int k in PrefixLengths)
                {
                    if (OutOfTime()) break;
                    Trace shortRun;
                    try { shortRun = Run(prototype, full.Take(k).ToList()); }
                    catch (Exception ex)
                    {
                        return Failure(id,
                            $"The strategy threw on the first {k} bars but not on {full.Count}: " +
                            $"{ex.GetType().Name}: {ex.Message}. It has to work on a freshly loaded " +
                            $"chart, which is always the short case.");
                    }

                    var diff = FirstDifference(shortRun, whole, shortRun.Dates, skip: 0);
                    if (diff != null)
                        findings.Add(
                            $"With {k} bars loaded the strategy decides differently than it does with " +
                            $"{full.Count}: {diff}. A bar's decision cannot depend on a later bar. The " +
                            $"usual cause is reading past the end of the history it was handed — note " +
                            $"that `state.Data` holds the WHOLE series from the first bar, so " +
                            $"`state.Data[i + 1]` is future data that a backtest will happily pay you for.");
                }

                // ── Does a bar's decision change when OLDER bars arrive? ─────────────────────
                foreach (int k in SuffixDrops)
                {
                    if (OutOfTime()) break;
                    Trace shortRun;
                    try { shortRun = Run(prototype, full.Skip(k).ToList()); }
                    catch (Exception ex)
                    {
                        return Failure(id,
                            $"The strategy threw on the last {full.Count - k} bars but not on " +
                            $"{full.Count}: {ex.GetType().Name}: {ex.Message}.");
                    }

                    var dates = shortRun.Dates.Skip(SuffixWarmup).ToList();
                    var diff = FirstDifference(shortRun, whole, dates, skip: 0);
                    if (diff != null)
                        findings.Add(
                            $"Prepending {k} older bars changes the strategy's decision on a bar that " +
                            $"was already there: {diff}. Scrolling back would rewrite trades it had " +
                            $"already taken. Something is anchored to the start of the array — a bar " +
                            $"count, a bucket, or a running total — where it should be anchored to the " +
                            $"bar's date.");
                }

                if (findings.Count > 0) break;   // one explanation is enough; stop burning the user's time
            }

            if (findings.Count > 0)
                return new ScriptStrategyCausalityReport(id, Refused: true, findings, notes, signalsSeen);

            if (ranOut)
                notes.Add(
                    $"The causality check ran out of its {limit.TotalSeconds:F0}-second budget before " +
                    "finishing, so part of it was not done — the strategy was not refused, but it was " +
                    "not fully checked either. A strategy this slow to replay will also be slow to " +
                    "backtest.");

            if (signalsSeen == 0)
                notes.Add(
                    "The strategy emitted no orders at all on the two check series, so its causality " +
                    "could not be established either way — it was not refused, it was not exercised. " +
                    "A strategy whose conditions read indicator components will land here, because the " +
                    "check series carries bars and no computed components.");

            return new ScriptStrategyCausalityReport(id, Refused: false, Array.Empty<string>(), notes, signalsSeen);
        }

        /// <summary>
        /// One run: a FRESH instance, driven a bar at a time exactly the way
        /// <c>StrategyBacktester</c> drives one — <c>Initialize</c> with an empty history and a
        /// state whose <c>Data</c> is the whole series, then <c>OnBar</c> per bar with a history
        /// that grows. Reproducing that shape is the point: it is what makes the future reachable,
        /// and a probe that handed over a truncated state would prove the strategy safe under
        /// conditions it never actually runs in.
        /// </summary>
        private static Trace Run(ITradingStrategy prototype, List<Ohlcv> bars)
        {
            var strategy = (ITradingStrategy?)Activator.CreateInstance(prototype.GetType())
                ?? throw new InvalidOperationException(
                    "the strategy class could not be instantiated a second time (it needs a public " +
                    "parameterless constructor)");

            var state = WorkspaceState.Initial with
            {
                Identity = ProbeIdentity,
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                ActiveSeries = ImmutableList<ChartSeries>.Empty,
                CurrentDataIndex = bars.Count - 1,
                // Suppresses the live event/audio publication a strategy may do — this is a replay,
                // and a probe that rang the terminal's bells 400 times would be its own bug.
                IsBacktesting = true,
            };

            var parameters = DefaultParameters(strategy);
            strategy.Initialize(ImmutableList<Ohlcv>.Empty, state, parameters);

            var trace = new Trace();
            var history = ImmutableList<Ohlcv>.Empty;
            for (int i = 0; i < bars.Count; i++)
            {
                history = history.Add(bars[i]);
                trace.Dates.Add(bars[i].Date);
                var signal = strategy.OnBar(bars[i], history, state);
                if (signal != null) trace.Signals[bars[i].Date] = Fingerprint(signal);
            }

            try { strategy.OnStop(); } catch { /* a strategy's own teardown is not the probe's business */ }
            return trace;
        }

        /// <summary>
        /// The parameter map <c>Initialize</c> gets: every declared parameter at its declared
        /// default. A fresh dictionary per run, because a strategy is free to mutate what it is
        /// handed and one that did would carry state from one run into the next and make the
        /// whole comparison meaningless.
        /// </summary>
        private static Dictionary<string, object> DefaultParameters(ITradingStrategy strategy)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var p in strategy.Parameters ?? Array.Empty<StrategyParameter>())
            {
                if (p?.Name is { Length: > 0 } name && p.DefaultValue != null)
                    map[name] = p.DefaultValue;
            }
            return map;
        }

        /// <summary>
        /// Everything about an order that a trade would notice, and nothing that it would not.
        ///
        /// <para>
        /// <see cref="StrategySignal.Rationale"/> is deliberately excluded: it is prose meant for
        /// the user, it routinely embeds a formatted number, and a strategy that spells a level
        /// slightly differently between runs has not done anything wrong. Everything the order
        /// router reads IS included, because a difference in any of it is a different trade.
        /// </para>
        /// </summary>
        private static string Fingerprint(StrategySignal s)
        {
            var sb = new StringBuilder();
            sb.Append(s.Side).Append('|').Append(s.OrderType).Append('|');
            Num(sb, s.Quantity); sb.Append('|');
            Num(sb, s.LimitPrice); sb.Append('|');
            Num(sb, s.StopLoss); sb.Append('|');
            Num(sb, s.TakeProfit); sb.Append('|');
            Num(sb, s.Confidence); sb.Append('|');
            sb.Append(s.StopAdjust).Append('|').Append(s.TrailAtrPeriod).Append('|');
            Num(sb, s.TrailAtrMultiple); sb.Append('|');
            Ladder(sb, s.TpLadder); sb.Append('|');
            Ladder(sb, s.TpClosePortions);
            return sb.ToString();

            static void Num(StringBuilder sb, double? v) =>
                // Rounded, so that an ordinary floating-point wobble between two runs of the same
                // arithmetic is not reported as a strategy reading the future. Nine digits is far
                // finer than any price this would trade at and far coarser than that wobble.
                sb.Append(v is null ? "-" : Math.Round(v.Value, 9).ToString("R", CultureInfo.InvariantCulture));

            static void Ladder(StringBuilder sb, IReadOnlyList<double>? values)
            {
                if (values is null) { sb.Append('-'); return; }
                for (int i = 0; i < values.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Num(sb, values[i]);
                }
            }
        }

        /// <summary>
        /// The first bar, among <paramref name="dates"/>, where the two runs disagree — including
        /// the case where one emitted an order and the other did not, which is the commonest shape
        /// of the failure and the one a naive "compare the orders both produced" would miss
        /// entirely.
        /// </summary>
        private static string? FirstDifference(Trace a, Trace b, IReadOnlyList<DateTime> dates, int skip)
        {
            for (int i = skip; i < dates.Count; i++)
            {
                var date = dates[i];
                a.Signals.TryGetValue(date, out var x);
                b.Signals.TryGetValue(date, out var y);
                if (string.Equals(x, y, StringComparison.Ordinal)) continue;

                return $"at {date:u} one run says [{Describe(x)}] and the other says [{Describe(y)}]";
            }
            return null;

            static string Describe(string? fingerprint) => fingerprint ?? "no order";
        }

        private static ScriptStrategyCausalityReport Failure(string id, string error) =>
            new(id, Refused: true, new[] { error }, Array.Empty<string>(), SignalsSeen: 0);

        /// <summary>One run's decisions: the bars it saw, and the order it emitted on each.</summary>
        private sealed class Trace
        {
            public List<DateTime> Dates { get; } = new();
            public Dictionary<DateTime, string> Signals { get; } = new();
        }
    }
}
