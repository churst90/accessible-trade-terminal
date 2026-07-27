using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>What a swing point is relative to the previous swing of the same kind.</summary>
    public enum SwingLabel
    {
        /// <summary>First swing of its kind — nothing to compare against yet.</summary>
        Initial,
        HigherHigh,
        LowerHigh,
        HigherLow,
        LowerLow
    }

    /// <summary>The structural read at a given bar.</summary>
    public enum StructureState
    {
        /// <summary>Not enough confirmed swings to say anything.</summary>
        Undefined,
        /// <summary>Most recent high and low were both higher than their predecessors.</summary>
        Uptrend,
        /// <summary>Most recent high and low were both lower.</summary>
        Downtrend,
        /// <summary>Highs and lows disagree — the market is not making directional progress.</summary>
        Range
    }

    /// <summary>A confirmed swing pivot.</summary>
    /// <param name="BarIndex">Bar the pivot formed on.</param>
    /// <param name="ConfirmedAtIndex">
    /// Bar at which the pivot could first be KNOWN — <c>BarIndex + Span</c>. Everything downstream
    /// keys off this, never off <see cref="BarIndex"/>, because a pivot is only visible in
    /// hindsight and using its own bar would leak the future into every signal built on it.
    /// </param>
    /// <param name="IsHigh">True for a swing high, false for a swing low.</param>
    /// <param name="Price">The extreme price of the pivot bar.</param>
    public record SwingPoint(int BarIndex, int ConfirmedAtIndex, bool IsHigh, double Price, SwingLabel Label, DateTime Time);

    /// <summary>A structural event — a break of the prior swing extreme.</summary>
    /// <param name="IsBullish">True when the break was upward.</param>
    /// <param name="IsChangeOfCharacter">
    /// True when the break went AGAINST the prevailing structure (the first crack in a trend);
    /// false when it continued it (a plain break of structure).
    /// </param>
    public record StructureBreak(int BarIndex, DateTime Time, bool IsBullish, bool IsChangeOfCharacter, double Level);

    /// <summary>Everything the analyzer derives for one bar series.</summary>
    public record SwingStructure(
        IReadOnlyList<SwingPoint> Swings,
        IReadOnlyList<StructureBreak> Breaks,
        StructureState[] StatePerBar,
        double[] LastHighPrice,
        double[] LastLowPrice);

    /// <summary>
    /// Options for <see cref="ISwingStructureAnalyzer"/>.
    /// </summary>
    /// <param name="Span">
    /// Bars either side that a pivot must dominate. Larger = fewer, more significant swings.
    /// </param>
    /// <param name="MinSwingAtr">
    /// A new swing must differ from the previous swing of the OPPOSITE kind by at least this many
    /// ATR. Without it, a flat drift produces a swing every few bars and the labels become noise
    /// dressed up as structure.
    /// </param>
    /// <param name="AtrPeriod">ATR lookback used for the significance filter.</param>
    public record SwingOptions(int Span = 5, double MinSwingAtr = 1.0, int AtrPeriod = 14)
    {
        public static SwingOptions Default { get; } = new();
    }

    /// <summary>
    /// Labels market structure: higher highs, higher lows, lower highs, lower lows, the trend
    /// state they imply, and the breaks between states.
    ///
    /// <para>
    /// This is deliberately DESCRIPTIVE, not predictive. Testing established that the geometric
    /// systems built on this kind of structure carry no edge over a random series — but the
    /// description itself is unambiguous once the swing rule is fixed, and it is the single thing
    /// a sighted trader reads off a chart's shape for free. Turning it into words and tones is
    /// the whole point: it hands a blind trader the same situational read, without ever claiming
    /// it predicts anything.
    /// </para>
    ///
    /// <para>
    /// NO LOOKAHEAD ANYWHERE. A pivot at bar i is only known at bar i+Span, so
    /// <see cref="SwingStructure.StatePerBar"/> and the carry-forward level arrays only change
    /// from the confirmation bar onward. That makes the output safe to feed to a strategy, and it
    /// also makes the narration honest: it says what was knowable at the time.
    /// </para>
    /// </summary>
    public interface ISwingStructureAnalyzer
    {
        SwingStructure Analyze(IReadOnlyList<Ohlcv> bars, SwingOptions? options = null);
    }

    /// <inheritdoc cref="ISwingStructureAnalyzer"/>
    public sealed class SwingStructureAnalyzer : ISwingStructureAnalyzer
    {
        public SwingStructure Analyze(IReadOnlyList<Ohlcv> bars, SwingOptions? options = null)
        {
            var opts = options ?? SwingOptions.Default;
            int n = bars.Count;

            var states = new StructureState[n];
            var lastHigh = new double[n];
            var lastLow = new double[n];
            Array.Fill(lastHigh, double.NaN);
            Array.Fill(lastLow, double.NaN);

            var swings = new List<SwingPoint>();
            var breaks = new List<StructureBreak>();
            if (n == 0) return new SwingStructure(swings, breaks, states, lastHigh, lastLow);

            var atr = IndicatorMath.Atr(ToArray(bars), opts.AtrPeriod);

            // ── Pass 1: raw fractal pivots ────────────────────────────────────
            var raw = new List<(int Index, bool IsHigh, double Price)>();
            for (int i = opts.Span; i < n - opts.Span; i++)
            {
                bool isHigh = true, isLow = true;
                for (int j = i - opts.Span; j <= i + opts.Span && (isHigh || isLow); j++)
                {
                    if (j == i) continue;
                    if (bars[j].High >= bars[i].High) isHigh = false;
                    if (bars[j].Low <= bars[i].Low) isLow = false;
                }
                if (isHigh) raw.Add((i, true, bars[i].High));
                if (isLow) raw.Add((i, false, bars[i].Low));
            }
            raw.Sort((a, b) => a.Index.CompareTo(b.Index));

            // ── Pass 2: alternate high/low and apply the ATR significance filter ──
            // Two highs in a row means the market never made a meaningful low between them, so
            // the higher one replaces the lower. This is what stops a choppy stretch from
            // emitting a dozen "structure" points that all describe the same move.
            var kept = new List<(int Index, bool IsHigh, double Price)>();
            foreach (var p in raw)
            {
                if (kept.Count == 0) { kept.Add(p); continue; }
                var last = kept[^1];

                if (last.IsHigh == p.IsHigh)
                {
                    bool replace = p.IsHigh ? p.Price > last.Price : p.Price < last.Price;
                    if (replace) kept[^1] = p;
                    continue;
                }

                double a = atr[Math.Min(p.Index, atr.Length - 1)];
                if (double.IsNaN(a) || a <= 0) { kept.Add(p); continue; }
                if (Math.Abs(p.Price - last.Price) < a * opts.MinSwingAtr) continue;

                kept.Add(p);
            }

            // ── Pass 3: label each swing against the previous one of its kind ──
            double? prevHigh = null, prevLow = null;
            foreach (var p in kept)
            {
                SwingLabel label;
                if (p.IsHigh)
                {
                    label = prevHigh == null ? SwingLabel.Initial
                        : p.Price > prevHigh ? SwingLabel.HigherHigh : SwingLabel.LowerHigh;
                    prevHigh = p.Price;
                }
                else
                {
                    label = prevLow == null ? SwingLabel.Initial
                        : p.Price > prevLow ? SwingLabel.HigherLow : SwingLabel.LowerLow;
                    prevLow = p.Price;
                }

                int confirmed = Math.Min(p.Index + opts.Span, n - 1);
                swings.Add(new SwingPoint(p.Index, confirmed, p.IsHigh, p.Price, label, bars[p.Index].Date));
            }

            // ── Pass 4: per-bar state and carry-forward levels, from confirmation on ──
            int si = 0;
            SwingLabel? lastHighLabel = null, lastLowLabel = null;
            double curHigh = double.NaN, curLow = double.NaN;
            var state = StructureState.Undefined;

            for (int i = 0; i < n; i++)
            {
                while (si < swings.Count && swings[si].ConfirmedAtIndex <= i)
                {
                    var s = swings[si];
                    if (s.IsHigh) { lastHighLabel = s.Label; curHigh = s.Price; }
                    else { lastLowLabel = s.Label; curLow = s.Price; }

                    state = DeriveState(lastHighLabel, lastLowLabel, state);
                    si++;
                }

                states[i] = state;
                lastHigh[i] = curHigh;
                lastLow[i] = curLow;
            }

            // ── Pass 5: structure breaks ──────────────────────────────────────
            // A break is a CLOSE beyond the carried swing level. Character changes are breaks
            // against the prevailing state; continuations are breaks with it.
            bool brokeHigh = false, brokeLow = false;
            double trackedHigh = double.NaN, trackedLow = double.NaN;

            for (int i = 1; i < n; i++)
            {
                if (!double.IsNaN(lastHigh[i]) && lastHigh[i] != trackedHigh) { trackedHigh = lastHigh[i]; brokeHigh = false; }
                if (!double.IsNaN(lastLow[i]) && lastLow[i] != trackedLow) { trackedLow = lastLow[i]; brokeLow = false; }

                if (!brokeHigh && !double.IsNaN(trackedHigh) && bars[i].Close > trackedHigh)
                {
                    breaks.Add(new StructureBreak(i, bars[i].Date, true,
                        states[i] == StructureState.Downtrend, trackedHigh));
                    brokeHigh = true;
                }
                if (!brokeLow && !double.IsNaN(trackedLow) && bars[i].Close < trackedLow)
                {
                    breaks.Add(new StructureBreak(i, bars[i].Date, false,
                        states[i] == StructureState.Uptrend, trackedLow));
                    brokeLow = true;
                }
            }

            return new SwingStructure(swings, breaks, states, lastHigh, lastLow);
        }

        /// <summary>
        /// Uptrend needs a higher high AND a higher low; downtrend the mirror. Anything else is a
        /// range. The previous state is retained while only one side has spoken, so a single fresh
        /// swing does not flip the read on its own.
        /// </summary>
        private static StructureState DeriveState(SwingLabel? high, SwingLabel? low, StructureState previous)
        {
            if (high == null || low == null) return StructureState.Undefined;

            bool highUp = high == SwingLabel.HigherHigh;
            bool lowUp = low == SwingLabel.HigherLow;
            bool highDown = high == SwingLabel.LowerHigh;
            bool lowDown = low == SwingLabel.LowerLow;

            if (highUp && lowUp) return StructureState.Uptrend;
            if (highDown && lowDown) return StructureState.Downtrend;
            if (high == SwingLabel.Initial || low == SwingLabel.Initial) return previous;
            return StructureState.Range;
        }

        private static Ohlcv[] ToArray(IReadOnlyList<Ohlcv> bars)
        {
            if (bars is Ohlcv[] a) return a;
            var copy = new Ohlcv[bars.Count];
            for (int i = 0; i < bars.Count; i++) copy[i] = bars[i];
            return copy;
        }
    }
}
