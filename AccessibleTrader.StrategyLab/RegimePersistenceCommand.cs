using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Have market regimes actually got shorter since COVID?
///
/// <para>
/// THE CLAIM, from the 2026-08-02 Peter Tuchman interview (NYSE floor broker since 1985): the
/// traditional vocabulary — bull market, bear market, correction — has stopped meaning anything
/// because the market now flips between those states far faster than it used to. It is the only
/// claim in that interview that is both falsifiable and testable on data already on disk, and it is
/// the kind of thing almost nobody checks: it is repeated because it feels true.
/// </para>
///
/// <para>
/// FALSIFIABLE FORM: the rate at which a mechanically-defined market regime reverses, measured in
/// flips per year, is higher in the post-COVID era than in equal-length earlier eras of the same
/// instrument. If the post-COVID rate sits inside the spread of earlier eras, the claim is false.
/// </para>
///
/// <para>
/// FOUR DESIGN DECISIONS, each avoiding a specific trap.
/// </para>
/// <list type="number">
///   <item>
///     <b>A mechanical label, not a journalist's.</b> His anecdote is about how the PRESS used the
///     words, and press usage is exactly the thing that could have changed while the market did not.
///     So a regime here is a confirmed percentage swing: a run that ends when price reverses by
///     theta from its running extreme. theta = 10% is the "correction" definition and theta = 20% is
///     the textbook bull/bear definition; both are reported, because picking whichever one answers
///     is the oldest trick there is.
///   </item>
///   <item>
///     <b>Eras balanced by LENGTH, not by calendar.</b> The post-COVID window is about six years
///     while "before COVID" is fifty. Comparing a 6-year window against a 50-year average compares
///     a sample of 6 years against a mean, and short windows are more variable by construction, so
///     the recent one would look extreme however the market behaved. Instead each instrument's
///     history is tiled backwards from the end into slices of EXACTLY the post-COVID length, and the
///     recent slice is ranked against its own siblings.
///   </item>
///   <item>
///     <b>A shuffled-returns surrogate per slice — the control that decides this.</b> A fixed-
///     percentage detector fires more often when volatility is higher, mechanically, with no change
///     in market character whatsoever. Post-COVID volatility is higher. So "regimes are shorter" and
///     "volatility went up" would produce identical output. The surrogate shuffles that slice's own
///     daily returns and re-runs the same detector, which preserves the slice's volatility and
///     destroys everything else. If the surrogate reproduces the era pattern, the finding belongs to
///     the detector and to volatility, not to the market — the same verdict the cycle-length study
///     reached. Reported as observed / surrogate.
///   </item>
///   <item>
///     <b>A volatility-scaled threshold as a second reading of the same question.</b> theta is
///     rescaled per slice by that slice's volatility relative to the instrument's own median slice
///     volatility, so the detector asks "a move large relative to THIS era's noise" in every era. If
///     the recent era is still exceptional under a scaled threshold, the claim has content beyond
///     "volatility rose".
///   </item>
/// </list>
///
/// <para>
/// ON THE p-VALUE. Thirty-eight US equities and ETFs are not thirty-eight independent samples —
/// SPY, VTI, DIA and the nine sector funds are mostly the same portfolio. A pooled permutation that
/// redraws each instrument independently would treat that correlation as evidence and produce a
/// p-value far too small. The null used here draws ONE uniform per permutation and maps it to a
/// slice index in every instrument simultaneously, so the whole market moves to a different era
/// together and the cross-sectional correlation is preserved. Its floor is 1/(number of slices),
/// which is stated in the output rather than hidden.
/// </para>
///
/// <para>
/// NO LOOKAHEAD CLAIM IS MADE. A swing pivot is only knowable theta later, so this segmentation
/// could not be traded as written. That is fine: the question is descriptive — how long did regimes
/// last — not "can you trade the flip". The incomplete final leg of every slice is dropped, because
/// its length is unknown at the edge of the data.
/// </para>
/// </summary>
public static class RegimePersistenceCommand
{
    /// <summary>One equal-length era of one instrument, measured every way the study needs.</summary>
    private sealed record Slice(
        int Index,               // 0 = most recent
        DateTime From,
        DateTime To,
        int Bars,
        double AnnVol,
        double FlipsPerYear,      // fixed threshold
        double MeanDurationBars,  // fixed threshold, completed regimes only
        int CompletedRegimes,
        double SurrogateFlipsPerYear,
        double ScaledFlipsPerYear,
        double ScaledTheta);

    private sealed record Instrument(string Symbol, string Class, IReadOnlyList<Slice> Slices);

    public static int Run(string snapshotDir, string? only, string? recentStart,
                          int permutations = 20000, int surrogates = 200)
    {
        if (!Directory.Exists(snapshotDir))
        {
            Console.Error.WriteLine($"No snapshot directory at {snapshotDir}");
            return 1;
        }

        DateTime recent = DateTime.TryParse(recentStart, out var rs)
            ? DateTime.SpecifyKind(rs, DateTimeKind.Unspecified)
            : new DateTime(2020, 3, 1);

        var chosen = PickFiles(snapshotDir, only);
        if (chosen.Count == 0)
        {
            Console.Error.WriteLine("No daily equity snapshots matched.");
            return 1;
        }

        // Two thresholds, both reported. 10% is a "correction", 20% is a bear market. Reporting one
        // of them would be choosing the answer.
        var thetas = new[] { 0.10, 0.20 };
        var byTheta = new Dictionary<double, List<Instrument>>();
        foreach (var theta in thetas) byTheta[theta] = new List<Instrument>();

        int skippedShort = 0;
        foreach (var (symbol, file) in chosen.OrderBy(kv => kv.Key))
        {
            var snap = SnapshotCommand.Load(file);
            var bars = snap.Bars.Where(b => b.Close > 0).OrderBy(b => b.Date).ToList();
            if (bars.Count < 500) { skippedShort++; continue; }

            int recentLen = bars.Count(b => b.Date >= recent);
            if (recentLen < 250) { skippedShort++; continue; }

            // Tile backwards from the newest bar so the most recent slice is EXACTLY the post-COVID
            // window. Any leading remainder is discarded rather than padded — an under-length slice
            // would have a mechanically lower flip count and would land in the oldest era every time.
            var windows = new List<(int Start, int End)>();   // [start, end)
            for (int end = bars.Count; end - recentLen >= 0; end -= recentLen)
                windows.Add((end - recentLen, end));
            if (windows.Count < 4) { skippedShort++; continue; }   // need the recent era plus 3 to rank against

            foreach (var theta in thetas)
                byTheta[theta].Add(Measure(symbol, LabSnapshots.CryptoOrEquities(file), bars, windows, theta, surrogates));
        }

        if (byTheta[thetas[0]].Count == 0)
        {
            Console.Error.WriteLine("No instrument had enough history to tile into four equal eras.");
            return 1;
        }

        Report(byTheta, thetas, recent, permutations, surrogates, skippedShort);
        return 0;
    }

    // ── Measurement ─────────────────────────────────────────────────────────────

    private static Instrument Measure(string symbol, string cls, List<Ohlcv> bars,
                                      List<(int Start, int End)> windows, double theta, int surrogates)
    {
        // First pass: the fixed-threshold reading plus each slice's volatility, which the scaled
        // threshold needs before it can be defined.
        var raw = new List<(int Idx, int Start, int End, double Vol, int Flips, double MeanDur, int Regimes)>();
        foreach (var (start, end) in windows)
        {
            int idx = raw.Count;
            var closes = new double[end - start];
            for (int i = start; i < end; i++) closes[i - start] = bars[i].Close;

            var durations = Durations(Zigzag(closes, theta));
            // A flip is a transition BETWEEN two regimes, which is why this counts durations rather
            // than pivots. The first pivot only marks where the record becomes measurable — on a
            // monotonically rising slice it is the first bar, and counting it would add a constant
            // phantom flip to every slice and shrink every relative difference toward zero. In a
            // study whose answer is a null, a bias pointing at the null is the one to be sure about.
            raw.Add((idx, start, end, AnnVol(closes), durations.Count,
                     durations.Count == 0 ? double.NaN : durations.Average(), durations.Count));
        }

        double medianVol = Median(raw.Select(r => r.Vol).ToList());

        var slices = new List<Slice>();
        foreach (var r in raw)
        {
            var closes = new double[r.End - r.Start];
            for (int i = r.Start; i < r.End; i++) closes[i - r.Start] = bars[i].Close;
            double years = closes.Length / 252.0;

            // Volatility-scaled threshold: the same question asked in this era's own units.
            double scaledTheta = medianVol > 0 ? theta * (r.Vol / medianVol) : theta;
            scaledTheta = Math.Clamp(scaledTheta, 0.02, 0.60);
            double scaledFlips = Durations(Zigzag(closes, scaledTheta)).Count / years;

            // Surrogate: this slice's OWN returns, shuffled. Same length, same distribution, same
            // volatility — everything except the order in which the moves arrived.
            double surrogateFlips = SurrogateFlipsPerYear(closes, theta, surrogates, symbol, r.Idx);

            slices.Add(new Slice(
                r.Idx, bars[r.Start].Date, bars[r.End - 1].Date, closes.Length, r.Vol,
                r.Flips / years, r.MeanDur, r.Regimes, surrogateFlips, scaledFlips, scaledTheta));
        }

        return new Instrument(symbol, cls, slices);
    }

    /// <summary>
    /// Confirmed percentage-swing segmentation. A leg continues while price extends the running
    /// extreme and ends when price retraces <paramref name="theta"/> from it; the running extreme
    /// becomes a pivot. Returns pivot bar indices. The leg before the first pivot and the leg after
    /// the last are both incomplete and carry no duration.
    /// </summary>
    internal static List<int> Zigzag(double[] close, double theta)
    {
        var pivots = new List<int>();
        if (close.Length < 3) return pivots;

        int dir = 0;
        int hiIdx = 0, loIdx = 0;
        double hi = close[0], lo = close[0];
        double ext = close[0];
        int extIdx = 0;

        for (int i = 1; i < close.Length; i++)
        {
            double c = close[i];
            if (dir == 0)
            {
                if (c > hi) { hi = c; hiIdx = i; }
                if (c < lo) { lo = c; loIdx = i; }

                // Whichever confirms first sets the direction, and the extreme it reversed from is
                // the first pivot. The running extreme for the new leg is then re-derived from the
                // bars between that pivot and here, because they were tracked under the other sign.
                if (c <= hi * (1 - theta) && hiIdx <= i)
                {
                    pivots.Add(hiIdx);
                    dir = -1;
                    (ext, extIdx) = MinBetween(close, hiIdx, i);
                }
                else if (c >= lo * (1 + theta) && loIdx <= i)
                {
                    pivots.Add(loIdx);
                    dir = 1;
                    (ext, extIdx) = MaxBetween(close, loIdx, i);
                }
                continue;
            }

            if (dir == 1)
            {
                if (c > ext) { ext = c; extIdx = i; }
                else if (c <= ext * (1 - theta))
                {
                    pivots.Add(extIdx);
                    dir = -1;
                    (ext, extIdx) = MinBetween(close, extIdx, i);
                }
            }
            else
            {
                if (c < ext) { ext = c; extIdx = i; }
                else if (c >= ext * (1 + theta))
                {
                    pivots.Add(extIdx);
                    dir = 1;
                    (ext, extIdx) = MaxBetween(close, extIdx, i);
                }
            }
        }

        return pivots;
    }

    private static (double Value, int Index) MinBetween(double[] c, int from, int to)
    {
        double best = c[from]; int bi = from;
        for (int i = from + 1; i <= to; i++) if (c[i] < best) { best = c[i]; bi = i; }
        return (best, bi);
    }

    private static (double Value, int Index) MaxBetween(double[] c, int from, int to)
    {
        double best = c[from]; int bi = from;
        for (int i = from + 1; i <= to; i++) if (c[i] > best) { best = c[i]; bi = i; }
        return (best, bi);
    }

    private static List<int> Durations(List<int> pivots)
    {
        var d = new List<int>();
        for (int i = 1; i < pivots.Count; i++) d.Add(pivots[i] - pivots[i - 1]);
        return d;
    }

    /// <summary>
    /// Mean flips/year the SAME detector finds in return-shuffled versions of this exact slice.
    /// Shuffling preserves the slice's return distribution — and therefore its volatility, the whole
    /// confound — while destroying any persistence, trend or regime structure. Anything the observed
    /// series has above this is the part that is about the market rather than about the detector.
    /// </summary>
    private static double SurrogateFlipsPerYear(double[] close, double theta, int draws,
                                                string symbol, int sliceIdx)
    {
        if (draws <= 0 || close.Length < 10) return double.NaN;

        var rets = new double[close.Length - 1];
        for (int i = 1; i < close.Length; i++) rets[i - 1] = Math.Log(close[i] / close[i - 1]);

        var rng = new Random(StableSeed.From($"{symbol}|{sliceIdx}|{theta}"));
        var path = new double[close.Length];
        var shuffled = new double[rets.Length];
        double years = close.Length / 252.0;
        double total = 0;

        for (int d = 0; d < draws; d++)
        {
            Array.Copy(rets, shuffled, rets.Length);
            for (int i = shuffled.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            path[0] = close[0];
            for (int i = 1; i < path.Length; i++) path[i] = path[i - 1] * Math.Exp(shuffled[i - 1]);
            total += Durations(Zigzag(path, theta)).Count;
        }

        return total / draws / years;
    }

    private static double AnnVol(double[] close)
    {
        if (close.Length < 3) return double.NaN;
        var r = new double[close.Length - 1];
        for (int i = 1; i < close.Length; i++) r[i - 1] = Math.Log(close[i] / close[i - 1]);
        double m = r.Average();
        double v = r.Sum(x => (x - m) * (x - m)) / (r.Length - 1);
        return Math.Sqrt(v) * Math.Sqrt(252) * 100;
    }

    // ── Statistics ──────────────────────────────────────────────────────────────

    private enum Arm { Fixed, Scaled, SurrogateRatio }

    private static double Value(Slice s, Arm arm) => arm switch
    {
        Arm.Fixed => s.FlipsPerYear,
        Arm.Scaled => s.ScaledFlipsPerYear,
        _ => s.SurrogateFlipsPerYear > 0 ? s.FlipsPerYear / s.SurrogateFlipsPerYear : double.NaN
    };

    /// <summary>
    /// Observed statistic: mean across instruments of the recent slice's value expressed as a
    /// percentage of that instrument's own earlier slices. Instrument-relative so a fund that flips
    /// often in every era cannot dominate.
    /// </summary>
    private static double RecentLift(List<Instrument> insts, Arm arm, int sliceIdx = 0)
    {
        var vals = new List<double>();
        foreach (var ins in insts)
        {
            if (sliceIdx >= ins.Slices.Count) continue;
            double v = Value(ins.Slices[sliceIdx], arm);
            var others = ins.Slices.Where(s => s.Index != sliceIdx)
                                   .Select(s => Value(s, arm))
                                   .Where(x => !double.IsNaN(x) && x > 0).ToList();
            if (double.IsNaN(v) || others.Count == 0) continue;
            vals.Add(v / others.Average() - 1.0);
        }
        return vals.Count == 0 ? double.NaN : vals.Average() * 100;
    }

    /// <summary>
    /// Correlation-preserving null. One uniform per permutation, mapped to a slice index in EVERY
    /// instrument at once, so a draw moves the whole market to a different era together. Drawing
    /// per-instrument instead would treat SPY, VTI and DIA as three independent votes and shrink the
    /// p-value to nothing.
    /// </summary>
    private static (double P, double Floor) SharedEraP(List<Instrument> insts, Arm arm, int permutations)
    {
        double observed = RecentLift(insts, arm);
        if (double.IsNaN(observed)) return (double.NaN, double.NaN);

        int maxSlices = insts.Max(i => i.Slices.Count);
        var rng = new Random(20260802);
        int atLeastAsExtreme = 0;

        for (int p = 0; p < permutations; p++)
        {
            double u = rng.NextDouble();
            var vals = new List<double>();
            foreach (var ins in insts)
            {
                int idx = Math.Min((int)(u * ins.Slices.Count), ins.Slices.Count - 1);
                double v = Value(ins.Slices[idx], arm);
                var others = ins.Slices.Where(s => s.Index != idx)
                                       .Select(s => Value(s, arm))
                                       .Where(x => !double.IsNaN(x) && x > 0).ToList();
                if (double.IsNaN(v) || others.Count == 0) continue;
                vals.Add(v / others.Average() - 1.0);
            }
            if (vals.Count > 0 && vals.Average() * 100 >= observed) atLeastAsExtreme++;
        }

        return ((atLeastAsExtreme + 1.0) / (permutations + 1.0), 1.0 / maxSlices);
    }

    /// <summary>Rank of the recent slice among its siblings, 1 = most flips (shortest regimes).</summary>
    private static int RecentRank(Instrument ins, Arm arm)
    {
        double v = Value(ins.Slices[0], arm);
        if (double.IsNaN(v)) return -1;
        return 1 + ins.Slices.Skip(1).Count(s => { double o = Value(s, arm); return !double.IsNaN(o) && o > v; });
    }

    // ── Reporting ───────────────────────────────────────────────────────────────

    private static void Report(Dictionary<double, List<Instrument>> byTheta, double[] thetas,
                               DateTime recent, int permutations, int surrogates, int skipped)
    {
        var any = byTheta[thetas[0]];
        Console.WriteLine();
        Console.WriteLine("═════ HAVE MARKET REGIMES GOT SHORTER SINCE COVID? ═════");
        Console.WriteLine($"{any.Count} instruments · recent era starts {recent:yyyy-MM-dd} · "
                        + $"{permutations:N0} shared-era permutations · {surrogates} shuffles per slice"
                        + (skipped > 0 ? $" · {skipped} instrument(s) too short" : ""));
        Console.WriteLine();
        Console.WriteLine("Claim (Tuchman, NYSE floor, 2026-08-02): the market now flips between bull, bear and");
        Console.WriteLine("correction so fast the labels have stopped meaning anything.");
        Console.WriteLine("Regime = a confirmed percentage swing. Each instrument's history is tiled BACKWARDS");
        Console.WriteLine("from today into slices of exactly the post-COVID length, so eras are compared like");
        Console.WriteLine("for like. Slice 0 is the recent era.");
        Console.WriteLine();

        foreach (var theta in thetas)
        {
            var insts = byTheta[theta];
            Console.WriteLine($"── THRESHOLD {theta * 100:0}%  ({(theta >= 0.20 ? "textbook bull/bear" : "correction")}) "
                            + new string('─', 42));

            int maxSlices = insts.Max(i => i.Slices.Count);
            Console.WriteLine($"{"era",4}{"instruments",12}{"window",26}{"ann vol",9}{"flips/yr",10}"
                            + $"{"shuffled",10}{"obs/shuf",10}{"vol-scaled",12}{"mean dur",10}");

            for (int k = 0; k < maxSlices; k++)
            {
                var have = insts.Where(i => i.Slices.Count > k).Select(i => i.Slices[k]).ToList();
                if (have.Count == 0) continue;
                var durs = have.Where(s => !double.IsNaN(s.MeanDurationBars)).ToList();
                double ratio = have.Where(s => s.SurrogateFlipsPerYear > 0)
                                   .Select(s => s.FlipsPerYear / s.SurrogateFlipsPerYear)
                                   .DefaultIfEmpty(double.NaN).Average();
                string window = $"{have.Min(s => s.From):yyyy-MM}…{have.Max(s => s.To):yyyy-MM}";
                Console.WriteLine($"{k,4}{have.Count,12}{window,26}{have.Average(s => s.AnnVol),8:0.0}%"
                                + $"{have.Average(s => s.FlipsPerYear),10:0.00}"
                                + $"{have.Where(s => !double.IsNaN(s.SurrogateFlipsPerYear)).Select(s => s.SurrogateFlipsPerYear).DefaultIfEmpty(double.NaN).Average(),10:0.00}"
                                + $"{ratio,10:0.00}"
                                + $"{have.Average(s => s.ScaledFlipsPerYear),12:0.00}"
                                + $"{(durs.Count == 0 ? double.NaN : durs.Average(s => s.MeanDurationBars)),10:0}");
            }
            Console.WriteLine();

            foreach (var arm in new[] { Arm.Fixed, Arm.Scaled, Arm.SurrogateRatio })
            {
                double lift = RecentLift(insts, arm);
                var (p, floor) = SharedEraP(insts, arm, permutations);
                int ranked1 = insts.Count(i => RecentRank(i, arm) == 1);
                double meanRank = insts.Select(i => (double)RecentRank(i, arm)).Where(r => r > 0).Average();
                string label = arm switch
                {
                    Arm.Fixed => "fixed threshold      ",
                    Arm.Scaled => "vol-scaled threshold ",
                    _ => "vs shuffled surrogate"
                };
                Console.WriteLine($"  {label}: recent era {lift,7:+0.0;-0.0}% vs its own earlier eras · "
                                + $"p {p:0.000} (floor {floor:0.000}) · fastest era in {ranked1}/{insts.Count} · mean rank {meanRank:0.0}");
            }
            Console.WriteLine();
        }

        Verdict(byTheta, thetas, permutations);
    }

    private static void Verdict(Dictionary<double, List<Instrument>> byTheta, double[] thetas, int permutations)
    {
        Console.WriteLine("── VERDICT " + new string('─', 66));

        int supported = 0, total = 0;
        foreach (var theta in thetas)
        {
            var insts = byTheta[theta];
            double rawLift = RecentLift(insts, Arm.Fixed);
            var (rawP, _) = SharedEraP(insts, Arm.Fixed, permutations);
            double ratioLift = RecentLift(insts, Arm.SurrogateRatio);
            var (ratioP, _) = SharedEraP(insts, Arm.SurrogateRatio, permutations);
            double scaledLift = RecentLift(insts, Arm.Scaled);
            var (scaledP, _) = SharedEraP(insts, Arm.Scaled, permutations);

            total++;
            bool raw = rawP < 0.05 && rawLift > 0;
            bool surv = ratioP < 0.05 && ratioLift > 0 && scaledP < 0.05 && scaledLift > 0;
            if (surv) supported++;

            Console.WriteLine($"  theta {theta * 100:0}%: raw {(raw ? "faster" : "NOT faster")} "
                            + $"({rawLift:+0.0;-0.0}%, p={rawP:0.000}) · after removing volatility "
                            + $"{(surv ? "STILL faster" : "no longer distinguishable")} "
                            + $"(shuffle-ratio {ratioLift:+0.0;-0.0}% p={ratioP:0.000}; vol-scaled {scaledLift:+0.0;-0.0}% p={scaledP:0.000})");
        }

        Console.WriteLine();
        if (supported == 0)
            Console.WriteLine("  NULL. Whatever the raw flip counts say, the post-COVID era is not distinguishable");
        else if (supported == total)
            Console.WriteLine("  SUPPORTED at every threshold, and it survives the volatility controls.");
        else
            Console.WriteLine("  SPLIT across thresholds — which is itself a reason to disbelieve it, since the");

        if (supported == 0)
        {
            Console.WriteLine("  from equal-length earlier eras once its higher volatility is accounted for. A fixed-");
            Console.WriteLine("  percentage detector fires more often when the market moves more; that is arithmetic,");
            Console.WriteLine("  not a change in market character.");
        }
        else if (supported != total)
        {
            Console.WriteLine("  claim is not supposed to depend on where you put the line.");
        }
        Console.WriteLine();
        Console.WriteLine("  Caveats: 38 US instruments are not 38 independent samples, which is why the null moves");
        Console.WriteLine("  every instrument to the same era together. The snapshot universe is survivors only.");
        Console.WriteLine("  Two thresholds x three arms = 6 tests; at alpha 0.05 expect 0.3 by chance.");
    }

    // ── Plumbing ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Daily equity snapshots, one file per symbol. Yahoo is preferred over TwelveData wherever both
    /// exist: TwelveData caps at 5,000 bars (2006) while Yahoo reaches 1970, and this study spends
    /// its entire power budget on how many equal-length eras fit before the recent one.
    /// </summary>
    private static Dictionary<string, string> PickFiles(string dir, string? only)
    {
        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(dir, "*_1d.json").OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (name.StartsWith("xs_", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith("events_", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith("fred_", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = name.Split('_');
            string provider = parts[0];
            string symbol = string.Join('_', parts[1..^1]);
            if (only != null && !symbol.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;

            bool better = !chosen.TryGetValue(symbol, out var existing)
                          || (provider.Equals("yahoo", StringComparison.OrdinalIgnoreCase)
                              && !Path.GetFileName(existing).StartsWith("yahoo", StringComparison.OrdinalIgnoreCase));
            if (better) chosen[symbol] = f;
        }
        return chosen;
    }

    private static double Median(List<double> xs)
    {
        var s = xs.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
        if (s.Count == 0) return double.NaN;
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
}
