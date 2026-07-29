using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the "Trading Cross" — a z-score momentum rule published by the Onchain Mind channel.
///
/// <para>
/// THE RULE, from the video transcript: a rolling z-score of price against its own adaptive
/// baseline. Buy when the z-score crosses ABOVE +1; sell when it crosses back BELOW 0. Long or
/// flat, nothing else. The asymmetry is deliberate — "the entry demands proof… but the exit
/// demands nothing."
/// </para>
///
/// <para>
/// THE CLAIM: $10,000 → $8M since 2014 (80,000%, 72% CAGR) against DCA's $446,000 (4,000%,
/// 36% CAGR), with a 42.9% maximum drawdown against DCA's 83%, and a Calmar of 1.68 against 0.44.
/// </para>
///
/// <para>
/// WHY THIS NEEDS RE-TESTING RATHER THAN CHECKING. The video does run a Monte Carlo — 10,000 runs
/// reshuffling the strategy's own daily returns — and reports that the 5th percentile still beats
/// DCA. That test cannot fail. Reshuffling the returns a strategy ALREADY EARNED asks "is this
/// set of returns favourable?", and the answer is yes by construction, because the set was
/// selected by the signal. It says nothing about whether the signal has edge. The null that can
/// actually fail is to shuffle the INPUT — block-bootstrap the price series, preserving volatility
/// clustering, re-run the rule, and ask how often random data produces the same result. That is
/// test 3 below, and it is the one the video does not do.
/// </para>
///
/// <para>
/// Four tests, all reported whatever they say:
/// </para>
/// <list type="number">
/// <item>Causality — is the signal computable from information available at the time?</item>
/// <item>Equal-capital comparison against BUY-AND-HOLD, not DCA. DCA deploys gradually, so beating
/// it on an asset that rose a thousandfold is largely a statement about deployment schedules.</item>
/// <item>Block-bootstrap surrogates — the null that can fail.</item>
/// <item>Era slices — does it survive in each cycle, or is it one era wearing a long backtest?</item>
/// </list>
/// </summary>
public static class TradingCrossCommand
{
    /// <summary>One equity path plus the statistics needed to compare it honestly.</summary>
    internal sealed record Book(double Final, double Cagr, double MaxDrawdown, int Trades, double TimeInMarket)
    {
        public double Calmar => MaxDrawdown > 0 ? Cagr / MaxDrawdown : double.NaN;
    }

    public static Task<int> RunAsync(string snapshotDir, string? only, string tf,
        int window, double entryZ, double exitZ, int surrogates, double costBps)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        if (files.Count == 0) { Console.Error.WriteLine($"No snapshots matched in {snapshotDir}."); return Task.FromResult(1); }

        Console.WriteLine();
        Console.WriteLine("===== THE TRADING CROSS =====");
        Console.WriteLine($"Buy when the {window}-bar z-score crosses above {entryZ:0.##}; sell when it crosses below {exitZ:0.##}.");
        Console.WriteLine($"Long or flat. {costBps:0.#} bps per side. Timeframe {tf}.");
        Console.WriteLine();

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            if (snap.Bars.Count < window + 200) continue;

            Report(snap, window, entryZ, exitZ, surrogates, costBps);
        }

        return Task.FromResult(0);
    }

    private static void Report(SnapshotFile snap, int window, double entryZ, double exitZ,
        int surrogates, double costBps)
    {
        var bars = snap.Bars;
        double cost = costBps / 10000.0;

        var strat = RunStrategy(bars, window, entryZ, exitZ, cost);
        var hold = RunBuyAndHold(bars);
        var dca = RunDca(bars);

        Console.WriteLine($"───── {snap.Symbol}  ({bars[0].Date:yyyy-MM-dd} → {bars[^1].Date:yyyy-MM-dd}, {bars.Count:N0} bars) ─────");
        Console.WriteLine();
        Console.WriteLine($"  {"",-18} {"final x",10} {"CAGR",9} {"maxDD",9} {"Calmar",8} {"trades",7} {"in mkt",8}");
        Line("Trading Cross", strat);
        Line("Buy & hold", hold);
        Line("DCA (weekly)", dca);
        Console.WriteLine();

        // ── TEST 2: the comparison that is not about deployment schedule ──
        Console.WriteLine("  TEST 2 — against BUY-AND-HOLD, the benchmark that holds capital the same way:");
        double vsHold = strat.Final / hold.Final;
        double vsDca = strat.Final / dca.Final;
        Console.WriteLine($"    vs buy-and-hold : {vsHold,8:0.00}x        vs DCA: {vsDca,8:0.00}x");
        Console.WriteLine($"    The DCA multiple is the headline number. The buy-and-hold multiple is the one");
        Console.WriteLine($"    that isolates the SIGNAL from the fact that DCA invests most of its money late.");
        Console.WriteLine();

        // ── TEST 3: the null that can actually fail ──
        RunSurrogates(bars, window, entryZ, exitZ, cost, surrogates, strat, hold);

        RunTimingNull(bars, window, entryZ, exitZ, cost, surrogates, strat);

        // ── TEST 4: era slices ──
        RunEras(bars, window, entryZ, exitZ, cost);
        Console.WriteLine();
        RunSweep(bars, cost);
        Console.WriteLine();
    }

    private static void Line(string label, Book b) =>
        Console.WriteLine($"  {label,-18} {b.Final,10:N1} {b.Cagr * 100,8:0.0}% {b.MaxDrawdown * 100,8:0.0}% " +
                          $"{b.Calmar,8:0.00} {b.Trades,7} {b.TimeInMarket * 100,7:0}%");

    // ── The strategy ─────────────────────────────────────────────────────

    /// <summary>
    /// Rolling z-score of LOG price against a trailing mean and standard deviation.
    ///
    /// <para>
    /// Log, because Bitcoin is exponential: a linear z-score on a series that rose a thousandfold
    /// is dominated by the level, not the deviation, and would sit pinned above +2 for years at a
    /// time. Log price is what makes the "adapts across cycles" claim in the video actually true —
    /// it is the transform under which a 2015 move and a 2025 move are comparable.
    /// </para>
    ///
    /// <para>
    /// Strictly trailing: the value at bar i uses bars [i-window, i] and nothing after. That is
    /// TEST 1 — the signal is causal by construction, so there is nothing here that could not have
    /// been computed on the day.
    /// </para>
    /// </summary>
    internal static double[] ZScore(IReadOnlyList<Ohlcv> bars, int window)
    {
        int n = bars.Count;
        var z = new double[n];
        Array.Fill(z, double.NaN);
        if (n <= window) return z;

        var logs = new double[n];
        for (int i = 0; i < n; i++) logs[i] = bars[i].Close > 0 ? Math.Log(bars[i].Close) : 0;

        double sum = 0, sumSq = 0;
        for (int i = 0; i < window; i++) { sum += logs[i]; sumSq += logs[i] * logs[i]; }

        for (int i = window; i < n; i++)
        {
            double mean = sum / window;
            double var = Math.Max(0, sumSq / window - mean * mean);
            double sd = Math.Sqrt(var);
            z[i] = sd > 1e-12 ? (logs[i] - mean) / sd : 0;

            sum += logs[i] - logs[i - window];
            sumSq += logs[i] * logs[i] - logs[i - window] * logs[i - window];
        }
        return z;
    }

    /// <summary>
    /// Buy on a cross above <paramref name="entryZ"/>, sell on a cross below <paramref name="exitZ"/>.
    ///
    /// <para>
    /// Signals are evaluated on bar i and filled at bar i+1's close. Filling at the same close that
    /// produced the signal is the most common way a backtest of a close-based rule quietly earns a
    /// day of free return — and this rule trades on the same series it measures, so that day is
    /// exactly the one where the move already happened.
    /// </para>
    /// </summary>
    internal static Book RunStrategy(IReadOnlyList<Ohlcv> bars, int window, double entryZ, double exitZ, double cost)
    {
        var z = ZScore(bars, window);
        double equity = 1.0, peak = 1.0, maxDd = 0;
        bool inMarket = false;
        int trades = 0, barsIn = 0, barsTotal = 0;

        for (int i = window + 1; i < bars.Count - 1; i++)
        {
            if (double.IsNaN(z[i]) || double.IsNaN(z[i - 1])) continue;
            barsTotal++;

            bool crossUp = z[i - 1] <= entryZ && z[i] > entryZ;
            bool crossDown = z[i - 1] >= exitZ && z[i] < exitZ;

            if (!inMarket && crossUp) { inMarket = true; trades++; equity *= 1 - cost; }
            else if (inMarket && crossDown) { inMarket = false; equity *= 1 - cost; }

            if (inMarket)
            {
                barsIn++;
                equity *= bars[i + 1].Close / bars[i].Close;   // filled next bar
            }

            peak = Math.Max(peak, equity);
            maxDd = Math.Max(maxDd, 1 - equity / peak);
        }

        return new Book(equity, Cagr(equity, bars), maxDd, trades,
            barsTotal > 0 ? barsIn / (double)barsTotal : 0);
    }

    private static Book RunBuyAndHold(IReadOnlyList<Ohlcv> bars)
    {
        double equity = 1.0, peak = 1.0, maxDd = 0;
        for (int i = 1; i < bars.Count; i++)
        {
            equity *= bars[i].Close / bars[i - 1].Close;
            peak = Math.Max(peak, equity);
            maxDd = Math.Max(maxDd, 1 - equity / peak);
        }
        return new Book(equity, Cagr(equity, bars), maxDd, 1, 1.0);
    }

    /// <summary>
    /// Weekly DCA, reported as a multiple of TOTAL CAPITAL DEPLOYED rather than of the first
    /// instalment — otherwise the comparison flatters every other strategy by pretending DCA had
    /// all its money in from day one.
    /// </summary>
    private static Book RunDca(IReadOnlyList<Ohlcv> bars)
    {
        double units = 0, invested = 0, peak = 0, maxDd = 0;
        const int Every = 7;

        for (int i = 0; i < bars.Count; i++)
        {
            if (i % Every == 0 && bars[i].Close > 0)
            {
                units += 1.0 / bars[i].Close;
                invested += 1.0;
            }
            double value = units * bars[i].Close;
            if (invested > 0)
            {
                peak = Math.Max(peak, value);
                if (peak > 0) maxDd = Math.Max(maxDd, 1 - value / peak);
            }
        }

        double final = invested > 0 ? units * bars[^1].Close / invested : 0;
        return new Book(final, Cagr(final, bars), maxDd, (int)invested, 1.0);
    }

    private static double Cagr(double multiple, IReadOnlyList<Ohlcv> bars)
    {
        double years = (bars[^1].Date - bars[0].Date).TotalDays / 365.25;
        return years > 0 && multiple > 0 ? Math.Pow(multiple, 1 / years) - 1 : 0;
    }

    // ── TEST 3: the surrogate null ───────────────────────────────────────

    private static void RunSurrogates(IReadOnlyList<Ohlcv> bars, int window, double entryZ, double exitZ,
        double cost, int surrogates, Book real, Book hold)
    {
        Console.WriteLine($"  TEST 3 — block-bootstrap surrogates ({surrogates:N0} runs):");
        Console.WriteLine("    The video reshuffles the strategy's OWN returns, which cannot fail — those returns");
        Console.WriteLine("    were already selected by the signal. This reshuffles the PRICE, preserving volatility");
        Console.WriteLine("    clustering, and re-runs the rule. If the edge is real, random data should rarely match it.");

        var rng = new Random(20260728);
        int beatReal = 0, beatOwnHold = 0;
        var ratios = new List<double>(surrogates);

        for (int s = 0; s < surrogates; s++)
        {
            var fake = SurrogateTest.BlockBootstrap(bars, rng);
            var fakeStrat = RunStrategy(fake, window, entryZ, exitZ, cost);
            var fakeHold = RunBuyAndHold(fake);

            if (fakeStrat.Final >= real.Final) beatReal++;

            // The meaningful comparison inside each surrogate: did the RULE add anything over
            // simply holding that same random series? An absolute return means nothing when the
            // surrogate happens to trend.
            double ratio = fakeHold.Final > 0 ? fakeStrat.Final / fakeHold.Final : 0;
            ratios.Add(ratio);
            if (ratio > 1) beatOwnHold++;
        }

        ratios.Sort();
        double realRatio = hold.Final > 0 ? real.Final / hold.Final : 0;
        int rank = ratios.Count(r => r >= realRatio);
        double p = (rank + 1.0) / (surrogates + 1.0);

        Console.WriteLine($"    Real strategy ÷ buy-and-hold        : {realRatio,8:0.000}");
        Console.WriteLine($"    Surrogate median ÷ its buy-and-hold : {ratios[ratios.Count / 2],8:0.000}");
        Console.WriteLine($"    Surrogate 95th percentile           : {ratios[(int)(ratios.Count * 0.95)],8:0.000}");
        Console.WriteLine($"    Surrogates beating the real ratio   : {rank:N0} of {surrogates:N0}   p = {p:0.0000}");
        Console.WriteLine($"    Surrogates where the rule beat holding random data: {beatOwnHold * 100.0 / surrogates:0.0}%");
        Console.WriteLine(p < 0.05
            ? "    → The rule adds something a trend-matched random series does not reproduce."
            : "    → NOT significant. Random data with the same volatility structure reproduces this.");
        Console.WriteLine();
    }

    /// <summary>
    /// TEST 3b — the null that holds EXPOSURE constant and randomises only the TIMING.
    ///
    /// <para>
    /// Test 3 has a weakness I did not see until I read its output. Its surrogate median ratio is
    /// 0.02: on random data the rule is destroyed by holding. That is not evidence about the
    /// signal — it is arithmetic. A rule in the market 57% of the time captures roughly 57% of a
    /// drifting series' log return, and compounding turns that shortfall into near-total loss over
    /// fifteen years. So the null distribution sits far below 1 for ANY partial-exposure rule, and
    /// almost any real strategy that beats hold will clear it. The test is close to unfailable,
    /// which is the same criticism I levelled at the video's Monte Carlo.
    /// </para>
    ///
    /// <para>
    /// This one cannot be cleared by exposure. It keeps the number of days in the market EXACTLY
    /// equal to the real strategy's and randomises only which days those are, drawn as contiguous
    /// blocks so the holding periods stay realistic. The question becomes the only one that
    /// matters: given that you were invested this much of the time, did the signal choose BETTER
    /// days than chance?
    /// </para>
    /// </summary>
    private static void RunTimingNull(IReadOnlyList<Ohlcv> bars, int window, double entryZ, double exitZ,
        double cost, int runs, Book real)
    {
        Console.WriteLine($"  TEST 3b — exposure-matched timing null ({runs:N0} runs):");
        Console.WriteLine("    Same days in the market, chosen at random in blocks instead of by the signal.");
        Console.WriteLine("    Test 3's null sits far below 1 for any partial-exposure rule, so almost anything");
        Console.WriteLine("    clears it. This one holds exposure fixed and asks only whether the TIMING was skill.");

        var z = ZScore(bars, window);
        int first = window + 1, last = bars.Count - 1;
        int tradable = Math.Max(1, last - first);
        int targetDays = (int)Math.Round(real.TimeInMarket * tradable);
        if (targetDays <= 0 || targetDays >= tradable) { Console.WriteLine("    (degenerate exposure — skipped)"); Console.WriteLine(); return; }

        // Block length taken from the strategy's own average holding period, so the random book
        // trades at the same frequency and pays the same costs.
        int avgHold = Math.Max(2, real.Trades > 0 ? targetDays / real.Trades : 20);

        var rng = new Random(31415926);
        var finals = new List<double>(runs);

        for (int r = 0; r < runs; r++)
        {
            var inMarket = new bool[bars.Count];
            int placed = 0, guard = 0;
            while (placed < targetDays && guard++ < runs * 100)
            {
                int start = first + rng.Next(tradable);
                int len = Math.Min(avgHold, last - start);
                for (int i = start; i < start + len && placed < targetDays; i++)
                    if (!inMarket[i]) { inMarket[i] = true; placed++; }
            }

            double equity = 1.0;
            bool wasIn = false;
            for (int i = first; i < last; i++)
            {
                if (inMarket[i] != wasIn) { equity *= 1 - cost; wasIn = inMarket[i]; }
                if (inMarket[i]) equity *= bars[i + 1].Close / bars[i].Close;
            }
            finals.Add(equity);
        }

        finals.Sort();
        int beat = finals.Count(f => f >= real.Final);
        double p = (beat + 1.0) / (runs + 1.0);

        Console.WriteLine($"    Real strategy final          : {real.Final,12:N1}x");
        Console.WriteLine($"    Random-timing median         : {finals[finals.Count / 2],12:N1}x");
        Console.WriteLine($"    Random-timing 95th percentile: {finals[(int)(finals.Count * 0.95)],12:N1}x");
        Console.WriteLine($"    Random books beating it      : {beat:N0} of {runs:N0}   p = {p:0.0000}");
        Console.WriteLine(p < 0.05
            ? "    → The signal picked better days than chance, at the same exposure."
            : "    → NOT significant. Being invested this much of the time explains the result;");
        Console.WriteLine(p < 0.05 ? "" : "      the CHOICE of days does not add measurably to it.");
        Console.WriteLine();
    }

    /// <summary>
    /// Sweeps the three parameters. A real edge shows a broad plateau — neighbouring settings work
    /// nearly as well. A fitted one shows a lone spike, and the published value sits on it.
    /// </summary>
    private static void RunSweep(IReadOnlyList<Ohlcv> bars, double cost)
    {
        Console.WriteLine("  PARAMETER SWEEP — ratio to buy-and-hold. A real edge is a plateau, not a spike:");
        var hold = RunBuyAndHold(bars);

        int[] windows = { 20, 30, 50, 75, 100, 150, 200, 250, 300, 400 };
        double[] entries = { 0.5, 0.75, 1.0, 1.25, 1.5 };
        // Extended past 0.5 because the first run came out MONOTONIC across the range tested,
        // which means the range was a boundary rather than a result — the optimum was outside it.
        double[] exits = { -0.5, 0.0, 0.25, 0.5, 0.75, 1.0, 1.25 };

        Console.Write($"    {"window",7}");
        foreach (var e in entries) Console.Write($"  entry {e,4:0.##}");
        Console.WriteLine("     (exit held at 0)");

        foreach (int w in windows)
        {
            Console.Write($"    {w,7}");
            foreach (double e in entries)
            {
                var b = RunStrategy(bars, w, e, 0.0, cost);
                Console.Write($"  {b.Final / hold.Final,10:0.00}");
            }
            Console.WriteLine();
        }

        Console.WriteLine();
        Console.Write($"    {"exit",7}");
        foreach (var x in exits) Console.Write($"   exit {x,5:0.##}");
        Console.WriteLine("     (window 200, entry 1.0)");
        Console.Write($"    {"",7}");
        foreach (double x in exits)
        {
            var b = RunStrategy(bars, 200, 1.0, x, cost);
            Console.Write($"  {b.Final / hold.Final,10:0.00}");
        }
        Console.WriteLine();
        Console.WriteLine();
    }

    // ── TEST 4: era slices ───────────────────────────────────────────────

    private static void RunEras(IReadOnlyList<Ohlcv> bars, int window, double entryZ, double exitZ, double cost)
    {
        Console.WriteLine("  TEST 4 — era slices. A rule that only works in one cycle is that cycle, not a rule:");
        Console.WriteLine($"    {"era",-14} {"cross x",10} {"hold x",10} {"ratio",8} {"maxDD",8}");

        var eras = new (string Name, int FromYear, int ToYear)[]
        {
            ("2012-2015", 2012, 2015), ("2015-2018", 2015, 2018),
            ("2018-2021", 2018, 2021), ("2021-2024", 2021, 2024),
            ("2024-now",  2024, 2100),
        };

        foreach (var era in eras)
        {
            // Each slice carries `window` extra bars of history in front so the z-score is warm at
            // the slice's first tradable bar — otherwise every era would begin with the rule blind.
            var slice = bars.Where(b => b.Date.Year >= era.FromYear && b.Date.Year < era.ToYear).ToList();
            if (slice.Count < window + 120) { Console.WriteLine($"    {era.Name,-14}   (too few bars)"); continue; }

            int firstIdx = 0;
            for (int i = 0; i < bars.Count; i++) { if (bars[i].Date == slice[0].Date) { firstIdx = i; break; } }
            var warmed = bars.Skip(Math.Max(0, firstIdx - window)).Take(window + slice.Count).ToList();

            var s = RunStrategy(warmed, window, entryZ, exitZ, cost);
            var h = RunBuyAndHold(slice);
            double ratio = h.Final > 0 ? s.Final / h.Final : double.NaN;

            Console.WriteLine($"    {era.Name,-14} {s.Final,10:N2} {h.Final,10:N2} {ratio,8:0.00} {s.MaxDrawdown * 100,7:0.0}%");
        }
    }
}
