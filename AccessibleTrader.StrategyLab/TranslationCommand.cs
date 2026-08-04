using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Does a LATE cycle high predict a faster, deeper decline into the next low?
///
/// <para>
/// ── The claim ───────────────────────────────────────────────────────────────
/// Camel Finance, live: <i>"when we overextend an extra right translate like this, we typically come
/// down harder and faster to correct the four-year cycle. Rather than have this kind of 10 to 12
/// month orderly walk down, we tend to puke into the low after heavily right translating."</i>
/// </para>
///
/// <para>
/// ── Why this one is testable when the cycle counts were not ────────────────
/// <c>camel-cycle-counts</c> is already Falsified: shuffled-return surrogates reproduced the claimed
/// cycle LENGTH on every asset, so the length was a property of the detector rather than of the
/// market. This is a different shape of claim. It does not ask when the low arrives; it asks
/// whether, <b>given</b> a cycle, the position of its high conditions the decline that follows.
/// A conditional shape claim survives the objection that killed a timing claim, because both arms
/// are measured inside the same detector — whatever the detector invents, it invents for both.
/// </para>
///
/// <para>
/// ── The control that decides it ─────────────────────────────────────────────
/// Our own cycle work already found that <b>translation is momentum in cycle vocabulary</b>. A cycle
/// that peaks late is a cycle that trended, so "late high → hard fall" may be nothing more than
/// "what went up fast comes down fast" — a statement about volatility clustering that needs no
/// cycles at all. The named control arm is therefore <b>plain trailing return over the same window</b>:
/// if sorting on trailing return produces the same spread as sorting on translation, the cycle
/// framing has added nothing.
/// </para>
///
/// <para>
/// ── And the surrogate arm ───────────────────────────────────────────────────
/// Phase-randomised surrogates (shuffled log returns, rebuilt path) run through the identical
/// detector. If high translation appears to predict decline shape in surrogates too, the
/// relationship belongs to the measurement rather than to the market — exactly how the cycle-length
/// claim died.
/// </para>
/// </summary>
internal static class TranslationCommand
{
    /// <summary>
    /// A completed cycle: low to low, with the high somewhere in between.
    /// </summary>
    private sealed record Cycle(
        int LowIndex, int NextLowIndex, int HighIndex,
        double LowPrice, double NextLowPrice, double HighPrice)
    {
        public int Length => NextLowIndex - LowIndex;

        /// <summary>
        /// Where the high printed, as a fraction of the cycle. 0.5 is dead centre; above ~0.5 is
        /// "right translated" in the cycle vocabulary.
        /// </summary>
        public double Translation => Length > 0 ? (double)(HighIndex - LowIndex) / Length : 0.5;

        /// <summary>How far price fell from the high into the next low, as a fraction.</summary>
        public double DeclineDepth => HighPrice > 0 ? (HighPrice - NextLowPrice) / HighPrice : 0;

        /// <summary>
        /// How much of the cycle the decline occupied.
        ///
        /// <para>
        /// <b>CAUTION — this is very nearly a tautology and must not be used as the "faster"
        /// measure.</b> Translation is <c>(high − low) / length</c> and this is
        /// <c>(nextLow − high) / length</c>, so the two sum to exactly 1 by construction. The first
        /// run of this command duly reported a −0.524 "effect" for a translation gap of +0.524,
        /// which is not a finding about markets, it is arithmetic restating its own input. Kept
        /// only as a diagnostic; <see cref="DeclineVelocity"/> is the real measure.
        /// </para>
        /// </summary>
        public double DeclineShare => Length > 0 ? (double)(NextLowIndex - HighIndex) / Length : 0;

        /// <summary>Bars from the high to the next low. The decline's actual duration.</summary>
        public int DeclineBars => NextLowIndex - HighIndex;

        /// <summary>
        /// <b>The honest reading of "harder and faster": fractional decline per bar.</b>
        ///
        /// <para>
        /// Depth alone cannot answer the claim either, because a late high mechanically leaves
        /// fewer bars for price to fall in — so a shallower drop is expected regardless of any
        /// market behaviour. Velocity divides the two, which is what "puke into the low" actually
        /// describes: a lot of ground covered in few bars.
        /// </para>
        /// </summary>
        public double DeclineVelocity => DeclineBars > 0 ? DeclineDepth / DeclineBars : 0;

        /// <summary>Trailing return into the high — the cheap alternative explanation.</summary>
        public double AdvanceReturn => LowPrice > 0 ? (HighPrice - LowPrice) / LowPrice : 0;
    }

    public static int Run(string snapshotDir, string? only, string tf, int span, int surrogates)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("events_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("fred_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0) { Console.Error.WriteLine("No snapshots matched."); return 1; }

        Console.WriteLine();
        Console.WriteLine("═════ RIGHT TRANSLATION: DOES A LATE CYCLE HIGH MEAN A HARDER FALL? ═════");
        Console.WriteLine($"{tf} bars · cycle lows by {span}-bar pivot with a {span}-bar confirmation lag");
        Console.WriteLine("Claim: a high printed late in the cycle is followed by a faster, deeper decline.");
        Console.WriteLine();

        var all = new List<(string Symbol, Cycle C)>();
        foreach (var path in files)
        {
            var snap = SnapshotCommand.Load(path);
            if (snap.Bars.Count < span * 6) continue;
            foreach (var c in Cycles(snap.Bars, span)) all.Add((snap.Symbol, c));
        }

        if (all.Count < 20)
        {
            Console.WriteLine($"Only {all.Count} complete cycles found — too few to say anything.");
            return 0;
        }

        Console.WriteLine($"{all.Count} complete cycles across {all.Select(x => x.Symbol).Distinct().Count()} symbols.");
        Console.WriteLine();

        // ── The claim, as a tercile split on translation ────────────────────────
        var byTranslation = all.Select(x => x.C).OrderBy(c => c.Translation).ToList();
        int k = byTranslation.Count / 3;
        var early = byTranslation.Take(k).ToList();
        var late = byTranslation.TakeLast(k).ToList();

        Console.WriteLine($"{"arm",-28}{"n",6}{"translation",13}{"depth",10}{"velocity %/bar",16}");
        Console.WriteLine(new string('-', 74));
        Row("EARLY high (bottom third)", early);
        Row("LATE high (top third)", late);
        Console.WriteLine(new string('-', 74));

        double depthGap = late.Average(c => c.DeclineDepth) - early.Average(c => c.DeclineDepth);
        double shareGap = late.Average(c => c.DeclineVelocity) - early.Average(c => c.DeclineVelocity);
        Console.WriteLine($"{"LATE − EARLY",-28}{"",6}{"",13}{depthGap * 100,9:+0.00;-0.00}%{shareGap * 100,15:+0.0000;-0.0000}");
        Console.WriteLine();
        Console.WriteLine("  The claim needs VELOCITY more positive — more ground covered per bar.");
        Console.WriteLine("  Raw depth cannot decide it: a late high mechanically leaves fewer bars to");
        Console.WriteLine("  fall in, so a shallower drop is expected whatever the market does.");
        Console.WriteLine();

        // ── Control 1: the cheap alternative ────────────────────────────────────
        //
        // Sort the SAME cycles on trailing advance instead of on translation. If that reproduces
        // the spread, "late high" was only ever a proxy for "went up a lot", and no cycle
        // vocabulary is doing any work.
        var byAdvance = all.Select(x => x.C).OrderBy(c => c.AdvanceReturn).ToList();
        var weak = byAdvance.Take(k).ToList();
        var strong = byAdvance.TakeLast(k).ToList();
        double advDepthGap = strong.Average(c => c.DeclineDepth) - weak.Average(c => c.DeclineDepth);
        double advShareGap = strong.Average(c => c.DeclineVelocity) - weak.Average(c => c.DeclineVelocity);

        Console.WriteLine("── CONTROL: sort on trailing advance instead of translation ──");
        Console.WriteLine($"  depth gap {advDepthGap * 100:+0.00;-0.00}%   velocity gap {advShareGap * 100:+0.0000;-0.0000} %/bar");
        Console.WriteLine("  If this matches the translation split, translation is momentum renamed.");
        Console.WriteLine();

        // ── Control 2: the same detector on shuffled returns ────────────────────
        int depthBeats = 0, shareBeats = 0, usable = 0;
        var rng = new Random(20260804);
        foreach (var path in files)
        {
            var snap = SnapshotCommand.Load(path);
            if (snap.Bars.Count < span * 6) continue;

            for (int s = 0; s < Math.Max(1, surrogates / Math.Max(1, files.Count)); s++)
            {
                var shuffled = ShuffleReturns(snap.Bars, rng);
                var cyc = Cycles(shuffled, span).ToList();
                if (cyc.Count < 6) continue;

                var sorted = cyc.OrderBy(c => c.Translation).ToList();
                int sk = sorted.Count / 3;
                if (sk == 0) continue;
                usable++;

                double sd = sorted.TakeLast(sk).Average(c => c.DeclineDepth)
                          - sorted.Take(sk).Average(c => c.DeclineDepth);
                double ss = sorted.TakeLast(sk).Average(c => c.DeclineVelocity)
                          - sorted.Take(sk).Average(c => c.DeclineVelocity);
                if (sd >= depthGap) depthBeats++;
                if (ss >= shareGap) shareBeats++;
            }
        }

        Console.WriteLine("── CONTROL: the same detector on shuffled-return surrogates ──");
        if (usable == 0)
        {
            Console.WriteLine("  No usable surrogate draws — cannot judge.");
        }
        else
        {
            Console.WriteLine($"  {usable} surrogate draws.");
            Console.WriteLine($"  depth gap matched or beaten in {depthBeats} ({100.0 * depthBeats / usable:F1}%)");
            Console.WriteLine($"  velocity gap matched or beaten in {shareBeats} ({100.0 * shareBeats / usable:F1}%)");
            Console.WriteLine("  These are one-sided p-values. The cycle LENGTH claim died exactly here.");
        }
        Console.WriteLine();

        // ── Verdict ─────────────────────────────────────────────────────────────
        bool depthReal = depthGap > 0 && usable > 0 && (double)depthBeats / usable < 0.05;
        bool shareReal = shareGap > 0 && usable > 0 && (double)shareBeats / usable < 0.05;
        bool cheapExplains = Math.Abs(advDepthGap) >= Math.Abs(depthGap) * 0.7;

        Console.WriteLine("── VERDICT ──");
        if (!depthReal && !shareReal)
        {
            Console.WriteLine("  NULL. Translation does not condition the decline beyond what the same");
            Console.WriteLine("  detector produces on shuffled returns. This is the second claim from");
            Console.WriteLine("  this source to die on surrogates, and for the same reason.");
        }
        else if (cheapExplains)
        {
            Console.WriteLine("  NOT AN EDGE. A spread exists, but sorting on plain trailing advance");
            Console.WriteLine("  reproduces most of it — translation is momentum in cycle vocabulary,");
            Console.WriteLine("  which is what our earlier cycle work already concluded.");
        }
        else
        {
            Console.WriteLine("  SURVIVES BOTH CONTROLS at this sample size. Not an edge yet: it still");
            Console.WriteLine("  needs an era split, a per-asset-class breakdown, and a breadth count");
            Console.WriteLine("  before it goes near the registry as anything but Untested.");
        }
        Console.WriteLine();
        return 0;

        void Row(string label, List<Cycle> arm) =>
            Console.WriteLine($"{label,-28}{arm.Count,6}{arm.Average(c => c.Translation),13:F3}"
                            + $"{arm.Average(c => c.DeclineDepth) * 100,9:F2}%{arm.Average(c => c.DeclineVelocity) * 100,16:F4}");
    }

    // ── The detector ────────────────────────────────────────────────────────────

    /// <summary>
    /// Cycles as low → next low, with the highest high between them.
    ///
    /// <para>
    /// Deliberately mechanical and fixed in advance. The source revises its cycle count as price
    /// arrives — using "inversion" and "failed cycle" as escape hatches — so adopting that would
    /// test a person's judgement rather than a rule. A pivot low here is simply the lowest low in a
    /// window of <paramref name="span"/> bars either side, and it is only KNOWN <c>span</c> bars
    /// after it printed, which is the same confirmation-lag discipline the chart-formation detector
    /// uses.
    /// </para>
    /// </summary>
    private static IEnumerable<Cycle> Cycles(IReadOnlyList<Ohlcv> bars, int span)
    {
        var lows = new List<int>();
        for (int i = span; i < bars.Count - span; i++)
        {
            double v = (double)bars[i].Low;
            bool isPivot = true;
            for (int j = i - span; j <= i + span && isPivot; j++)
                if (j != i && (double)bars[j].Low < v) isPivot = false;
            if (isPivot) lows.Add(i);
        }

        for (int n = 1; n < lows.Count; n++)
        {
            int a = lows[n - 1], b = lows[n];
            if (b - a < span * 2) continue;   // too short to contain a cycle shape

            int hi = a;
            for (int i = a; i <= b; i++)
                if ((double)bars[i].High > (double)bars[hi].High) hi = i;

            // A high sitting exactly on either low is not a cycle shape.
            if (hi == a || hi == b) continue;

            yield return new Cycle(a, b, hi,
                (double)bars[a].Low, (double)bars[b].Low, (double)bars[hi].High);
        }
    }

    /// <summary>
    /// A surrogate with the same return distribution and no time structure: shuffle the log
    /// returns and rebuild the path. Any cycle the detector finds here is manufactured by the
    /// detector.
    /// </summary>
    private static List<Ohlcv> ShuffleReturns(IReadOnlyList<Ohlcv> bars, Random rng)
    {
        var rets = new List<double>(bars.Count);
        for (int i = 1; i < bars.Count; i++)
        {
            double a = (double)bars[i - 1].Close, b = (double)bars[i].Close;
            rets.Add(a > 0 && b > 0 ? Math.Log(b / a) : 0);
        }
        for (int i = rets.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (rets[i], rets[j]) = (rets[j], rets[i]);
        }

        var outp = new List<Ohlcv>(bars.Count);
        double px = (double)bars[0].Close;
        outp.Add(new Ohlcv { Date = bars[0].Date, Open = px, High = px, Low = px, Close = px, Volume = 1 });
        for (int i = 0; i < rets.Count; i++)
        {
            double prev = px;
            px *= Math.Exp(rets[i]);
            outp.Add(new Ohlcv
            {
                Date = bars[i + 1].Date,
                Open = prev,
                Close = px,
                High = Math.Max(prev, px),
                Low = Math.Min(prev, px),
                Volume = 1,
            });
        }
        return outp;
    }
}
