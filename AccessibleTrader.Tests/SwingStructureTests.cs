using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Coverage for <see cref="SwingStructureAnalyzer"/>.
    ///
    /// This engine is descriptive, so its whole value rests on the description being CORRECT and
    /// HONEST. The invariants that matter: labels must match the actual price sequence; a pivot
    /// must never be visible before the bar that could confirm it; noise must not be promoted to
    /// structure; and a state must not flip on half the evidence.
    /// </summary>
    public class SwingStructureTests
    {
        private static readonly DateTime Start = new(2026, 1, 1);

        /// <summary>Builds a zig-zag from a list of turning-point prices, interpolating between them.</summary>
        private static List<Ohlcv> ZigZag(IEnumerable<double> turns, int barsPerLeg = 12, double noise = 0.2)
        {
            var pts = turns.ToList();
            var bars = new List<Ohlcv>();
            int day = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double from = pts[i], to = pts[i + 1];
                for (int k = 0; k < barsPerLeg; k++)
                {
                    double t = (k + 1) / (double)barsPerLeg;
                    double price = from + (to - from) * t;
                    bars.Add(new Ohlcv(Start.AddDays(day++), price, price + noise, price - noise, price, 100));
                }
            }
            return bars;
        }

        private static SwingStructure Analyze(List<Ohlcv> bars, SwingOptions? o = null) =>
            new SwingStructureAnalyzer().Analyze(bars, o ?? new SwingOptions(Span: 3, MinSwingAtr: 0.5));

        // ── Labelling ─────────────────────────────────────────────────────────

        [Fact]
        public void RisingZigZag_ProducesHigherHighsAndHigherLows()
        {
            var bars = ZigZag(new[] { 100.0, 120, 110, 140, 130, 160 });
            var s = Analyze(bars);

            var highs = s.Swings.Where(x => x.IsHigh).Select(x => x.Label).ToList();
            var lows = s.Swings.Where(x => !x.IsHigh).Select(x => x.Label).ToList();

            Assert.Contains(SwingLabel.HigherHigh, highs);
            Assert.DoesNotContain(SwingLabel.LowerHigh, highs);
            Assert.Contains(SwingLabel.HigherLow, lows);
            Assert.DoesNotContain(SwingLabel.LowerLow, lows);
        }

        [Fact]
        public void FallingZigZag_ProducesLowerHighsAndLowerLows()
        {
            var bars = ZigZag(new[] { 160.0, 130, 145, 110, 125, 90 });
            var s = Analyze(bars);

            var highs = s.Swings.Where(x => x.IsHigh).Select(x => x.Label).ToList();
            var lows = s.Swings.Where(x => !x.IsHigh).Select(x => x.Label).ToList();

            Assert.Contains(SwingLabel.LowerHigh, highs);
            Assert.DoesNotContain(SwingLabel.HigherHigh, highs);
            Assert.Contains(SwingLabel.LowerLow, lows);
            Assert.DoesNotContain(SwingLabel.HigherLow, lows);
        }

        [Fact]
        public void RisingZigZag_EndsInAnUptrendState()
        {
            var bars = ZigZag(new[] { 100.0, 120, 110, 140, 130, 160 });
            var s = Analyze(bars);
            Assert.Equal(StructureState.Uptrend, s.StatePerBar[^1]);
        }

        [Fact]
        public void FallingZigZag_EndsInADowntrendState()
        {
            var bars = ZigZag(new[] { 160.0, 130, 145, 110, 125, 90 });
            var s = Analyze(bars);
            Assert.Equal(StructureState.Downtrend, s.StatePerBar[^1]);
        }

        [Fact]
        public void HigherHighWithLowerLow_ReadsAsRangeNotTrend()
        {
            // Expanding structure: highs rising, lows falling. Calling that a trend would be a lie,
            // and it is exactly the shape that traps people into trend-following a broadening range.
            var bars = ZigZag(new[] { 100.0, 130, 90, 145, 80, 150 });
            var s = Analyze(bars);
            Assert.Equal(StructureState.Range, s.StatePerBar[^1]);
        }

        // ── No lookahead ──────────────────────────────────────────────────────

        [Fact]
        public void PivotIsConfirmedSpanBarsAfterItForms_NeverOnItsOwnBar()
        {
            var bars = ZigZag(new[] { 100.0, 130, 110, 150 });
            var opts = new SwingOptions(Span: 4, MinSwingAtr: 0.5);
            var s = Analyze(bars, opts);

            Assert.NotEmpty(s.Swings);
            foreach (var sw in s.Swings)
                Assert.True(sw.ConfirmedAtIndex > sw.BarIndex,
                    "a pivot must not be knowable on the bar it forms");
        }

        [Fact]
        public void CarryForwardLevelsDoNotChangeBeforeConfirmation()
        {
            var bars = ZigZag(new[] { 100.0, 130, 110, 150 });
            var opts = new SwingOptions(Span: 4, MinSwingAtr: 0.5);
            var s = Analyze(bars, opts);

            var firstHigh = s.Swings.FirstOrDefault(x => x.IsHigh);
            Assert.NotNull(firstHigh);

            // Before confirmation, the carried high must not yet be this pivot's price.
            for (int i = 0; i < firstHigh!.ConfirmedAtIndex; i++)
                Assert.NotEqual(firstHigh.Price, s.LastHighPrice[i]);

            Assert.Equal(firstHigh.Price, s.LastHighPrice[firstHigh.ConfirmedAtIndex]);
        }

        // ── Noise rejection ───────────────────────────────────────────────────

        [Fact]
        public void FlatNoise_ProducesNoStructure()
        {
            // Tiny wiggles around a flat price. Without the ATR floor this emits a swing every
            // few bars and the labels become noise wearing a structure costume.
            var bars = new List<Ohlcv>();
            for (int i = 0; i < 200; i++)
            {
                double p = 100 + (i % 2 == 0 ? 0.01 : -0.01);
                bars.Add(new Ohlcv(Start.AddDays(i), p, p + 0.02, p - 0.02, p, 100));
            }

            var s = new SwingStructureAnalyzer().Analyze(bars, new SwingOptions(Span: 5, MinSwingAtr: 2.0));
            Assert.True(s.Swings.Count <= 2, $"expected almost no swings in flat noise, got {s.Swings.Count}");
        }

        [Fact]
        public void ConsecutiveSameKindPivots_CollapseToTheMoreExtremeOne()
        {
            var bars = ZigZag(new[] { 100.0, 120, 115, 135, 105, 150 });
            var s = Analyze(bars);

            // Highs and lows must alternate — never two highs in a row.
            for (int i = 1; i < s.Swings.Count; i++)
                Assert.NotEqual(s.Swings[i - 1].IsHigh, s.Swings[i].IsHigh);
        }

        // ── Breaks ────────────────────────────────────────────────────────────

        [Fact]
        public void BreakingTheSwingHighInADowntrend_IsAChangeOfCharacter()
        {
            // The downtrend has to be ESTABLISHED before a break can change its character, which
            // needs two lower highs and two lower lows on the board first. The original fixture
            // only produced one high, so the state was still Undefined and there was no character
            // to change — the analyzer was right and the test was wrong.
            var bars = ZigZag(new[] { 160.0, 130, 150, 120, 140, 100, 200 });
            var s = Analyze(bars);

            Assert.Contains(s.Breaks, b => b.IsBullish && b.IsChangeOfCharacter);
        }

        [Fact]
        public void ContinuationBreaks_AreNotFlaggedAsCharacterChanges()
        {
            var bars = ZigZag(new[] { 100.0, 120, 110, 140, 130, 170 });
            var s = Analyze(bars);

            var bullish = s.Breaks.Where(b => b.IsBullish).ToList();
            Assert.NotEmpty(bullish);
            // In a clean uptrend every upward break continues the structure.
            Assert.All(bullish, b => Assert.False(b.IsChangeOfCharacter));
        }

        [Fact]
        public void EmptySeries_IsHandledWithoutThrowing()
        {
            var s = new SwingStructureAnalyzer().Analyze(Array.Empty<Ohlcv>());
            Assert.Empty(s.Swings);
            Assert.Empty(s.Breaks);
        }

        // ── Provider surface ──────────────────────────────────────────────────

        [Fact]
        public void Provider_EmitsStateAsPlusOneMinusOneOrZero()
        {
            var bars = ZigZag(new[] { 100.0, 120, 110, 140, 130, 160 });
            var provider = new SwingStructureProvider();
            var buffer = new TestBuffer(bars.Count);

            provider.Calculate(SwingStructureProvider.Code, bars.ToArray(),
                new Dictionary<string, object> { ["Span"] = 3, ["MinSwingAtr"] = 0.5 }, buffer);

            var state = buffer.Data[SwingStructureProvider.CompState];
            Assert.All(state, v => Assert.True(double.IsNaN(v) || v == 1.0 || v == -1.0 || v == 0.0));
            Assert.Equal(1.0, state[^1]);
        }

        [Fact]
        public void Provider_DetailFactNamesTheStateAndPositionInRange()
        {
            var bars = ZigZag(new[] { 100.0, 120, 110, 140, 130, 160 });
            var provider = new SwingStructureProvider();
            var buffer = new TestBuffer(bars.Count);
            var parameters = new Dictionary<string, object> { ["Span"] = 3, ["MinSwingAtr"] = 0.5 };

            provider.Calculate(SwingStructureProvider.Code, bars.ToArray(), parameters, buffer);
            string fact = provider.GetDetailFact(SwingStructureProvider.Code, bars.ToArray(),
                buffer.Data, bars.Count - 1, parameters);

            Assert.Contains("uptrend", fact, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("swing low", fact, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%", fact);
        }

        [Fact]
        public void Provider_SpeaksStructureStateInWordsNotNumbers()
        {
            var provider = new SwingStructureProvider();
            var empty = new Dictionary<string, double[]>();
            var bar = new Ohlcv(Start, 1, 1, 1, 1, 1);

            Assert.Contains("uptrend", provider.GetComponentSpeech("Structure State", 1.0, bar, empty, 0)!);
            Assert.Contains("downtrend", provider.GetComponentSpeech("Structure State", -1.0, bar, empty, 0)!);
            Assert.Contains("range", provider.GetComponentSpeech("Structure State", 0.0, bar, empty, 0)!);
        }

        /// <summary>Minimal <see cref="AccessibleTrader.Sdk.Interfaces.IIndicatorResultBuffer"/> for provider tests.</summary>
        private sealed class TestBuffer : AccessibleTrader.Sdk.Interfaces.IIndicatorResultBuffer
        {
            public readonly Dictionary<string, double[]> Data = new();
            private readonly int _n;
            public TestBuffer(int n) => _n = n;

            public Span<double> GetComponentSpan(string componentName)
            {
                if (!Data.TryGetValue(componentName, out var arr))
                {
                    arr = new double[_n];
                    Data[componentName] = arr;
                }
                return arr;
            }

            public void SetValue(string componentName, int index, double value) =>
                GetComponentSpan(componentName)[index] = value;

            public void WriteZoneBands(string indicatorCode, List<ZoneBandConfig> zoneBands) { }
            public IReadOnlyList<ZoneBandConfig> ReadZoneBands(string indicatorCode) => Array.Empty<ZoneBandConfig>();
        }
    }
}
