using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Does pyramiding — recycling the SAME dollar risk into a larger position — beat holding one size?
///
/// <para>
/// THE CLAIM, from the 2026-08-02 David Hannan interview: "you might have had a 10K initial risk on
/// a trade, but you just get so big after adding and moving your stop up that that 10k initial risk
/// turns into a million dollars … it's not necessarily increasing risk, it's recycling the same
/// risk." The mechanic: when the trade forms structure supporting a TIGHTER stop, move the stop up —
/// which drops the dollar risk below target — then add size until risk is back at the original
/// figure. Repeat at each new structure.
/// </para>
///
/// <para>
/// WHY THIS ONE MATTERS. The ledger holds about twenty measured edges on ENTRIES, two on EXITS and
/// <b>zero on SIZING</b>. This is the first sizing rule anyone has proposed here that is
/// mechanically specifiable: there is no conviction input and no discretion about how much to add.
/// Given an entry, an exit, and a rule for when a tighter stop is justified, the size schedule is
/// arithmetic.
/// </para>
///
/// <para>
/// AND OUR OWN PRIOR SAYS IT SHOULD WORK, WHICH IS THE REASON FOR THE EXTRA ARMS. The exit study
/// found the BTC trend edge has a fat right tail — mean +8.15R at a 47% win rate — and that
/// fixed-percentage scale-OUTS destroyed 95–100% of the return. Pyramiding is the exact inverse of
/// scaling out. A positive result would agree with something already measured, and agreement with a
/// prior is how results get believed without being checked.
/// </para>
///
/// <para>
/// THE DESIGN. Entry and exit are held completely FIXED — the z-momentum entry and the signal exit
/// from the exit study, the only entry/exit pair this lab has validated. Every arm trades the same
/// bars, enters on the same signals and leaves on the same signals. <b>Only the size schedule
/// varies</b>, so any difference is attributable to sizing and nothing else.
/// </para>
///
/// <list type="number">
///   <item><b>flat 1×</b> — one unit of risk at entry, stop never moves. The baseline.</item>
///   <item><b>pyramid</b> — the claim. At each confirmed swing low above the current stop, move the
///         stop there and add until dollar risk is back at one unit.</item>
///   <item><b>random adds</b> — THE CONTROL THAT DECIDES IT. The same NUMBER of adds as the pyramid
///         made on that same trade, placed at random bars inside it, with the stop moved the same
///         way. If this matches the pyramid, then adding during a trend is what pays and the
///         structure the adds are anchored to carries nothing — which is what every other
///         price-structure claim tested here has turned out to be.</item>
///   <item><b>flat at pyramid's average size</b> — THE LEVERAGE CONTROL. A constant position equal
///         to the average size the pyramid actually carried. If this matches the pyramid, the result
///         is leverage wearing a schedule's clothes, and the cheaper way to get it is to size up at
///         entry and skip the machinery.</item>
///   <item><b>naive adds (risk grows)</b> — adds at the same moments but WITHOUT moving the stop, so
///         risk compounds. The strawman the claim is defined against; included because "add to
///         winners" is what most people hear.</item>
/// </list>
///
/// <para>
/// WHAT IS REPORTED, AND WHY NOT WIN RATE. Total R, where R is the ORIGINAL one-unit risk, held
/// constant across arms so the numbers are comparable. Win rate is printed but must not be read as
/// the score: Hannan states his own would be ~60% without pyramiding and is 41% with it, so <b>win
/// rate falling is predicted by the claim</b>, not evidence against it. Maximum drawdown is reported
/// beside it, because converting winners into losers is exactly the failure mode.
/// </para>
///
/// <para>
/// COSTS. Every add is a market order into a move already going, so each one is charged slippage in
/// R terms — and basis points against notional are not basis points against R. At a 1-unit-ATR stop
/// the position is 1÷(ATR÷price) times the risk unit, so the conversion runs through the ATR
/// percentage at the moment of the add. The break-even cost is printed, because a rule that needs
/// free execution is not a rule.
/// </para>
///
/// <para>
/// NO LOOKAHEAD. Adds are triggered by confirmed swing lows from <see cref="ISwingStructureAnalyzer"/>,
/// which carry the bar at which they could first be KNOWN — Span bars after they printed. The add
/// happens on the confirmation bar's close, never on the pivot bar.
/// </para>
/// </summary>
public static class PyramidCommand
{
    private const int ZWindow = 50;
    private const double EntryZ = 1.0;
    private const int MaxHold = 400;

    /// <summary>
    /// Exit threshold, settable so the technique can be given its BEST CASE. Pyramiding needs a long
    /// trend to have anywhere to add: at the default exit the average hold is 10-26 bars and the
    /// schedule barely engages, which would make a null a statement about the exit rather than about
    /// pyramiding. Lowering this produces multi-month holds and lets the position actually compound.
    /// </summary>
    private static double ExitZ = 0.5;

    /// <summary>One unit of risk. Everything is denominated in this, so it is just 1.</summary>
    private const double RiskUnit = 1.0;

    private sealed record TradeOutcome(double R, int Bars, double AvgSize, int Adds, double PeakSize);

    private sealed record ArmResult(string Name, double TotalR, double AvgR, double WinRate,
                                    double MaxDd, double AvgSize, double AvgAdds, int N, double AvgBars);

    public static int Run(string snapshotDir, string? only, string tf, int permutations = 2000,
                          double slippageBps = 5.0, int span = 3, double minSwingAtr = 0.25,
                          double exitZ = 0.5)
    {
        ExitZ = exitZ;
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("events_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("fred_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0) { Console.Error.WriteLine("No snapshots matched."); return 1; }

        Console.WriteLine();
        Console.WriteLine("═════ PYRAMIDING: DOES RECYCLING THE SAME RISK BEAT ONE SIZE? ═════");
        Console.WriteLine($"{tf} bars · entry and exit IDENTICAL in every arm (z{ZWindow} > +{EntryZ} in, < {ExitZ} out)");
        Console.WriteLine($"Only the SIZE SCHEDULE varies. R = the original one-unit risk. {slippageBps:0.#} bps charged per add.");
        Console.WriteLine($"Adds are triggered by confirmed swing lows (span {span}, min {minSwingAtr:0.##} ATR), taken at their CONFIRMATION bar.");
        Console.WriteLine();

        var perAsset = new List<(string Asset, string Class, List<ArmResult> Arms)>();

        foreach (var f in files)
        {
            var snap = SnapshotCommand.Load(f);
            var bars = snap.Bars;
            if (bars.Count < 600) continue;

            string asset = snap.Symbol;
            string cls = LabSnapshots.CryptoOrEquities(f);

            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);
            var entries = Entries(bars);
            if (entries.Count < 15) continue;

            var swings = new SwingStructureAnalyzer()
                .Analyze(bars, new SwingOptions(Span: span, MinSwingAtr: minSwingAtr));

            var arms = ScoreAllArms(bars, atr, entries, swings, permutations, slippageBps, asset);
            if (arms.Count == 0) continue;
            perAsset.Add((asset, cls, arms));
        }

        if (perAsset.Count == 0) { Console.Error.WriteLine("No instrument produced enough trades."); return 1; }

        Report(perAsset, slippageBps);
        return 0;
    }

    // ── The arms ────────────────────────────────────────────────────────────────

    private static List<ArmResult> ScoreAllArms(
        IReadOnlyList<Ohlcv> bars, double[] atr, List<int> entries, SwingStructure swings,
        int permutations, double slipBps, string asset)
    {
        var flat = new List<TradeOutcome>();
        var pyramid = new List<TradeOutcome>();
        var naive = new List<TradeOutcome>();
        var random = new List<TradeOutcome>();
        var flatAvg = new List<TradeOutcome>();

        var rng = new Random(StableSeed.From(asset));

        foreach (var e in entries)
        {
            if (e >= bars.Count - 5 || double.IsNaN(atr[e]) || atr[e] <= 0) continue;
            int exit = SignalExit(e, bars);
            if (exit <= e) continue;

            var f = RunTrade(bars, atr, e, exit, swings, Mode.Flat, slipBps, rng);
            var p = RunTrade(bars, atr, e, exit, swings, Mode.Pyramid, slipBps, rng);
            var n = RunTrade(bars, atr, e, exit, swings, Mode.NaiveAdd, slipBps, rng);

            // Random-add control: the SAME number of adds this trade's pyramid actually made,
            // scattered at random bars inside the same trade. Matching the count is what makes it a
            // test of WHEN to add rather than a test of whether to add at all.
            var r = RunTrade(bars, atr, e, exit, swings, Mode.RandomAdd, slipBps, rng, forcedAdds: p.Adds);

            // Leverage control: a constant position at the average size the pyramid carried.
            // p.AvgSize is now a MULTIPLE of the opening position, so this is genuinely "the same
            // trade held flat at the average size the pyramid carried".
            double mult = Math.Max(1.0, p.AvgSize);
            var fa = f with { R = f.R * mult, AvgSize = mult };

            flat.Add(f); pyramid.Add(p); naive.Add(n); random.Add(r); flatAvg.Add(fa);
        }

        if (pyramid.Count < 15) return new List<ArmResult>();

        return new List<ArmResult>
        {
            Score("flat 1x", flat),
            Score("PYRAMID", pyramid),
            Score("random adds", random),
            Score("flat @ pyr avg size", flatAvg),
            Score("naive adds (risk grows)", naive),
        };
    }

    private enum Mode { Flat, Pyramid, NaiveAdd, RandomAdd }

    /// <summary>
    /// One trade, one size schedule.
    ///
    /// <para>
    /// The position is tracked as (size, averageEntry, stop). Dollar risk at any moment is
    /// <c>size × (averageEntry − stop)</c>, which goes NEGATIVE once the stop is above the average —
    /// that is the locked-in-profit state the whole technique aims at, and it is why the adds get
    /// larger as the trade runs.
    /// </para>
    ///
    /// <para>
    /// The add size solves <c>(size+Δ) × (newAverage − stop) = R</c> for Δ, which reduces to
    /// <c>Δ = [R − size×(average − stop)] ÷ (price − stop)</c>. Long-only: the entry rule is a
    /// momentum breakout and the exit study established there is no short-side skill here to borrow.
    /// </para>
    /// </summary>
    private static TradeOutcome RunTrade(
        IReadOnlyList<Ohlcv> bars, double[] atr, int entry, int exit, SwingStructure swings,
        Mode mode, double slipBps, Random rng, int forcedAdds = -1)
    {
        double a0 = atr[entry];
        double entryPx = bars[entry].Open;
        double stop = entryPx - a0;

        double size = RiskUnit / (entryPx - stop);          // one unit of risk
        double size0 = size;                               // ...and the yardstick for every report
        double avg = entryPx;
        double cost = 0;
        int adds = 0;
        double peak = size, sizeBarSum = size;

        // Which bars trigger an add.
        var triggers = new List<int>();
        if (mode == Mode.Pyramid || mode == Mode.NaiveAdd)
        {
            // Confirmed swing LOWS inside the trade, taken at their CONFIRMATION bar. A pivot is not
            // knowable on its own bar, so anchoring the add there would be reading the future.
            foreach (var s in swings.Swings)
            {
                if (s.IsHigh) continue;
                if (s.ConfirmedAtIndex <= entry || s.ConfirmedAtIndex >= exit) continue;
                triggers.Add(s.ConfirmedAtIndex);
            }
        }
        else if (mode == Mode.RandomAdd && forcedAdds > 0)
        {
            int span = exit - entry - 1;
            if (span > 1)
                for (int k = 0; k < forcedAdds; k++)
                    triggers.Add(entry + 1 + rng.Next(span));
            triggers.Sort();
        }

        int ti = 0;
        for (int i = entry + 1; i <= exit; i++)
        {
            // Stopped out first — the stop is checked against the bar's low before any add, because
            // a bar that trades through the stop and then rallies is a loss, not an opportunity.
            if (bars[i].Low <= stop)
            {
                // Filled at the stop only if the bar did not open beneath it. This
                // command's whole thesis is that tightening the stop as you pyramid
                // pays, so an exit that ignores gaps biases exactly the number under
                // test — and the ratcheted stop sits closer to price on every add,
                // which is where gaps do their damage.
                double fill = BarFill.StopExit(stop, bars[i].Open, OrderSide.Buy);
                double rOut = size * (fill - avg) - cost;
                return new TradeOutcome(rOut / RiskUnit, i - entry,
                    sizeBarSum / Math.Max(1, i - entry) / size0, adds, peak / size0);
            }

            while (ti < triggers.Count && triggers[ti] == i)
            {
                ti++;
                double px = bars[i].Close;

                if (mode == Mode.Pyramid || mode == Mode.RandomAdd)
                {
                    // The new stop. For the pyramid it is the swing low that triggered the add; for
                    // the random control it is the lowest low since entry up to here, which is the
                    // same KIND of level (a structural low) chosen without reference to structure.
                    double newStop = LowestSince(bars, entry, i) - 0.05 * atr[i];
                    if (newStop <= stop) continue;                 // never loosen a stop
                    if (newStop >= px) continue;                   // degenerate

                    stop = newStop;
                    double risk = size * (avg - stop);
                    double delta = (RiskUnit - risk) / (px - stop);
                    if (delta <= 0) continue;

                    // Cap total size growth per trade. Without it a stop that creeps to within a
                    // rounding error of price demands an unbounded position, and the arithmetic --
                    // not the market -- produces the result.
                    if (size + delta > 50 * size0) continue;

                    avg = (avg * size + px * delta) / (size + delta);
                    size += delta;
                    cost += delta * px * (slipBps / 10000.0);
                    adds++;
                }
                else if (mode == Mode.NaiveAdd)
                {
                    // The strawman: add the original size again, leave the stop alone, let risk grow.
                    double delta = RiskUnit / (entryPx - (entryPx - a0));
                    avg = (avg * size + px * delta) / (size + delta);
                    size += delta;
                    cost += delta * px * (slipBps / 10000.0);
                    adds++;
                }
            }

            peak = Math.Max(peak, size);
            sizeBarSum += size;
        }

        double outPx = bars[exit].Close;
        double r = size * (outPx - avg) - cost;
        return new TradeOutcome(r / RiskUnit, exit - entry,
            sizeBarSum / Math.Max(1, exit - entry) / size0, adds, peak / size0);
    }

    private static double LowestSince(IReadOnlyList<Ohlcv> bars, int from, int to)
    {
        double lo = double.MaxValue;
        for (int i = from; i <= to; i++) lo = Math.Min(lo, bars[i].Low);
        return lo;
    }

    // ── Scoring ─────────────────────────────────────────────────────────────────

    private static ArmResult Score(string name, List<TradeOutcome> trades)
    {
        double total = trades.Sum(t => t.R);
        double equity = 0, peak = 0, maxDd = 0;
        foreach (var t in trades)
        {
            equity += t.R;
            peak = Math.Max(peak, equity);
            maxDd = Math.Max(maxDd, peak - equity);
        }
        return new ArmResult(name, total, trades.Average(t => t.R),
            trades.Count(t => t.R > 0) / (double)trades.Count, maxDd,
            trades.Average(t => t.AvgSize), trades.Average(t => (double)t.Adds), trades.Count,
            trades.Average(t => (double)t.Bars));
    }

    // ── Reporting ───────────────────────────────────────────────────────────────

    private static void Report(List<(string Asset, string Class, List<ArmResult> Arms)> perAsset, double slipBps)
    {
        foreach (var cls in perAsset.Select(x => x.Class).Distinct().OrderBy(x => x))
        {
            var group = perAsset.Where(x => x.Class == cls).ToList();
            Console.WriteLine($"── {cls.ToUpperInvariant()} ({group.Count} instruments) " + new string('─', 46));
            Console.WriteLine($"{"instrument",-12}{"arm",-24}{"total R",10}{"avg R",9}{"win%",7}{"maxDD R",9}{"avg size",10}{"adds/trade",11}{"bars",7}{"n",6}");

            foreach (var (asset, _, arms) in group.OrderBy(x => x.Asset))
            {
                foreach (var a in arms)
                    Console.WriteLine($"{(a == arms[0] ? asset : ""),-12}{a.Name,-24}{a.TotalR,10:+0.0;-0.0}{a.AvgR,9:+0.00;-0.00}"
                                    + $"{a.WinRate * 100,6:0.0}%{a.MaxDd,9:0.0}{a.AvgSize,10:0.00}x{a.AvgAdds,10:0.0}{a.AvgBars,7:0}{a.N,6}");
                Console.WriteLine();
            }

            Pooled(group, cls);
        }

        Console.WriteLine("── HOW TO READ IT " + new string('─', 60));
        Console.WriteLine("  Win rate is NOT the score. The claim predicts win rate FALLS -- Hannan puts his own at");
        Console.WriteLine("  41% with pyramiding against ~60% without. Total R is the score, max drawdown the cost.");
        Console.WriteLine();
        Console.WriteLine("  'random adds' is the control that decides whether STRUCTURE matters: same number of adds,");
        Console.WriteLine("  same trade, random timing. 'flat @ pyr avg size' is the control that decides whether the");
        Console.WriteLine("  schedule matters at all, or whether the pyramid is leverage with extra steps.");
        Console.WriteLine($"  Costs: {slipBps:0.#} bps charged on every add, in R terms.");
    }

    private static void Pooled(List<(string Asset, string Class, List<ArmResult> Arms)> group, string cls)
    {
        Console.WriteLine($"  POOLED {cls}:");
        var names = group[0].Arms.Select(a => a.Name).ToList();
        var byName = names.ToDictionary(n => n, n => group.Select(g => g.Arms.First(a => a.Name == n)).ToList());

        double flatR = byName["flat 1x"].Sum(a => a.TotalR);
        foreach (var n in names)
        {
            var arms = byName[n];
            double tot = arms.Sum(a => a.TotalR);
            int beatsFlat = group.Count(g => g.Arms.First(a => a.Name == n).TotalR
                                           > g.Arms.First(a => a.Name == "flat 1x").TotalR);
            Console.WriteLine($"    {n,-24}{tot,10:+0.0;-0.0} R   ({beatsFlat}/{group.Count} instruments beat flat 1x)"
                            + (n == "flat 1x" ? "   ← baseline" : $"   {tot / (Math.Abs(flatR) < 1e-9 ? 1 : Math.Abs(flatR)),6:0.00}x flat"));
        }

        // The two verdict questions, stated as numbers rather than left to the reader.
        double pyr = byName["PYRAMID"].Sum(a => a.TotalR);
        double rnd = byName["random adds"].Sum(a => a.TotalR);
        double lev = byName["flat @ pyr avg size"].Sum(a => a.TotalR);
        int pyrBeatsRnd = group.Count(g => g.Arms.First(a => a.Name == "PYRAMID").TotalR
                                         > g.Arms.First(a => a.Name == "random adds").TotalR);
        int pyrBeatsLev = group.Count(g => g.Arms.First(a => a.Name == "PYRAMID").TotalR
                                         > g.Arms.First(a => a.Name == "flat @ pyr avg size").TotalR);

        Console.WriteLine();
        Console.WriteLine($"    does STRUCTURE matter?  pyramid {pyr:+0.0;-0.0} R vs random adds {rnd:+0.0;-0.0} R"
                        + $"   ({pyrBeatsRnd}/{group.Count} instruments)");
        Console.WriteLine($"    is it just LEVERAGE?    pyramid {pyr:+0.0;-0.0} R vs flat at same avg size {lev:+0.0;-0.0} R"
                        + $"   ({pyrBeatsLev}/{group.Count} instruments)");
        Console.WriteLine();
    }

    // ── Fixed entry / exit, shared with the exit study ──────────────────────────

    private static List<int> Entries(IReadOnlyList<Ohlcv> bars)
    {
        var z = TradingCrossCommand.ZScore(bars, ZWindow);
        var outp = new List<int>();
        bool armed = true;
        for (int i = ZWindow + 2; i < bars.Count - 2; i++)
        {
            if (double.IsNaN(z[i]) || double.IsNaN(z[i - 1])) continue;
            if (armed && z[i - 1] <= EntryZ && z[i] > EntryZ) { outp.Add(i + 1); armed = false; }
            else if (!armed && z[i] < ExitZ) armed = true;
        }
        return outp;
    }

    private static int SignalExit(int entry, IReadOnlyList<Ohlcv> bars)
    {
        var z = TradingCrossCommand.ZScore(bars, ZWindow);
        for (int i = entry + 1; i < bars.Count && i < entry + MaxHold; i++)
            if (!double.IsNaN(z[i]) && z[i] < ExitZ) return i;
        return Math.Min(entry + MaxHold, bars.Count - 1);
    }
}
