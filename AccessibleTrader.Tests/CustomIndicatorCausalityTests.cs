using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The causality contract, applied to code the app compiles at runtime.
    ///
    /// <para>
    /// <c>IndicatorCausalityTests</c> proves the built-in providers at build time by enumerating
    /// them reflectively. That mechanism cannot reach a scripted indicator: it does not exist when
    /// the suite runs, and it is written by a user who has no reviewer and who very often ported it
    /// from Pine, where <c>bar_index</c> is idiomatic and transliterates straight into an array
    /// index that moves the moment history is prepended. So the same two sweeps run against the
    /// compiled instance at registration instead, and these tests are the proof that the sweeps
    /// still catch each shape.
    /// </para>
    ///
    /// <para>
    /// Each fake below is a real defect written down as small as it will go: the Chikou shape
    /// (reads a later bar), the bar_index shape (pinned to array index 0), the running-total shape,
    /// and the "declared Causal while doing neither" shape. If the probe ever stops failing one of
    /// them it has stopped being worth running.
    /// </para>
    /// </summary>
    public class CustomIndicatorCausalityTests
    {
        // ── Fakes ─────────────────────────────────────────────────────────────────────────────

        private abstract class Fake : ICustomIndicator
        {
            public string Id => GetType().Name;
            public string DisplayName => GetType().Name;
            public abstract string[] ComponentNames { get; }
            public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Line };
            public Dictionary<string, double> DefaultParameters => new() { ["Period"] = 14 };
            public virtual ComponentCausality[] Causality => Array.Empty<ComponentCausality>();
            public abstract double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters);
        }

        /// <summary>An honest SMA. Every window is trailing and nothing is anchored anywhere.</summary>
        private sealed class CleanSma : Fake
        {
            public override string[] ComponentNames => new[] { "Sma" };
            public override ComponentCausality[] Causality => new[] { ComponentCausality.Causal };
            public override double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                int period = (int)parameters["Period"];
                var outp = new double[data.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    if (i < period - 1) { outp[i] = double.NaN; continue; }
                    double sum = 0;
                    for (int j = i - period + 1; j <= i; j++) sum += data[j].Close;
                    outp[i] = sum / period;
                }
                return new[] { outp };
            }
        }

        /// <summary>The Chikou shape: bar i holds the close of bar i+26.</summary>
        private sealed class ReadsTheFuture : Fake
        {
            public override string[] ComponentNames => new[] { "Lagging" };
            public override double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                var outp = new double[data.Length];
                for (int i = 0; i < data.Length; i++)
                    outp[i] = i + 26 < data.Length ? data[i + 26].Close : double.NaN;
                return new[] { outp };
            }
        }

        /// <summary>The same thing, but the script says so. A declared lagging span is legitimate
        /// on a chart — it just never becomes a strategy leaf.</summary>
        private sealed class DeclaredLaggingSpan : Fake
        {
            public override string[] ComponentNames => new[] { "Lagging" };
            public override ComponentCausality[] Causality => new[] { ComponentCausality.Lookahead };
            public override double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                var outp = new double[data.Length];
                for (int i = 0; i < data.Length; i++)
                    outp[i] = i + 26 < data.Length ? data[i + 26].Close : double.NaN;
                return new[] { outp };
            }
        }

        /// <summary>
        /// The bar_index shape, and the one the prefix sweep alone would miss entirely: a bucket
        /// cut from the array position rather than the bar's date. Appending bars never disturbs
        /// it; prepending them re-cuts every bucket in the series.
        /// </summary>
        private sealed class BucketsFromArrayIndex : Fake
        {
            public override string[] ComponentNames => new[] { "WeeklyClose" };
            public override ComponentCausality[] Causality => new[] { ComponentCausality.Causal };
            public override double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                var outp = new double[data.Length];
                double held = double.NaN;
                for (int i = 0; i < data.Length; i++)
                {
                    if (i % 7 == 6) held = data[i].Close;   // "last bar of the week"
                    outp[i] = held;
                }
                return new[] { outp };
            }
        }

        /// <summary>A running total, which has no start other than the start of the data.</summary>
        private sealed class RunningTotal : Fake
        {
            public override string[] ComponentNames => new[] { "Cumulative" };
            public override ComponentCausality[] Causality => new[] { ComponentCausality.Causal };
            public override double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                var outp = new double[data.Length];
                double acc = 0;
                for (int i = 0; i < data.Length; i++) { acc += data[i].Volume; outp[i] = acc; }
                return new[] { outp };
            }
        }

        /// <summary>Returns fewer arrays than it named components.</summary>
        private sealed class ShapeMismatch : Fake
        {
            public override string[] ComponentNames => new[] { "A", "B" };
            public override double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
                => new[] { new double[data.Length] };
        }

        /// <summary>Works on a long series and divides by zero on a short one — which is what a
        /// freshly loaded chart always is.</summary>
        private sealed class ThrowsOnShortSeries : Fake
        {
            public override string[] ComponentNames => new[] { "Fragile" };
            public override double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                if (data.Length < 500) throw new InvalidOperationException("not enough bars");
                return new[] { new double[data.Length] };
            }
        }

        /// <summary>Names a component and never writes a value to it.</summary>
        private sealed class NeverProducesAValue : Fake
        {
            public override string[] ComponentNames => new[] { "Silent" };
            public override ComponentCausality[] Causality => new[] { ComponentCausality.Causal };
            public override double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
            {
                var outp = new double[data.Length];
                Array.Fill(outp, double.NaN);
                return new[] { outp };
            }
        }

        // ── The contract ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void AnHonestIndicatorIsProvedAndPublishable()
        {
            var report = CustomIndicatorCausalityProbe.Probe(new CleanSma());

            Assert.False(report.Failed);
            Assert.Empty(report.Findings);
            var sma = report.For("Sma");
            Assert.NotNull(sma);
            Assert.Equal(ComponentCausality.Causal, sma!.Measured);
            Assert.True(sma.Publishable);
            Assert.True(report.AnyPublishable);
        }

        [Fact]
        public void AComponentThatReadsALaterBarIsCaughtAndRefused()
        {
            var report = CustomIndicatorCausalityProbe.Probe(new ReadsTheFuture());

            var v = report.For("Lagging");
            Assert.NotNull(v);
            Assert.Equal(ComponentCausality.Lookahead, v!.Measured);
            Assert.False(v.Publishable);
            Assert.False(report.AnyPublishable);
            Assert.Contains("look-ahead", string.Join(" ", report.Findings));
        }

        [Fact]
        public void ADeclaredLaggingSpanIsAcceptedButNeverPublished()
        {
            // The probe can refute a claim of causality. It cannot refute a claim of look-ahead,
            // and it must not try: an author saying "this reads ahead" is always taken at their word.
            var report = CustomIndicatorCausalityProbe.Probe(new DeclaredLaggingSpan());

            var v = report.For("Lagging");
            Assert.NotNull(v);
            Assert.Equal(ComponentCausality.Lookahead, v!.Declared);
            Assert.False(v.Publishable);
        }

        [Fact]
        public void ABucketCutFromTheArrayIndexIsCaughtByTheSuffixSweepOnly()
        {
            var indicator = new BucketsFromArrayIndex();
            var report = CustomIndicatorCausalityProbe.Probe(indicator);

            var v = report.For("WeeklyClose");
            Assert.NotNull(v);
            Assert.False(v!.Publishable);

            // Precisely the point of having both sweeps. Appending bars leaves every existing
            // bucket alone, so a prefix comparison of this indicator agrees everywhere — which is
            // how it would have shipped if the suffix sweep did not exist.
            var full = CausalityProbeSeries.Bars(0, CustomIndicatorCausalityProbe.ProbeLength);
            var whole = Calc(indicator, full);
            var prefix = Calc(indicator, full.Take(400).ToList());
            for (int i = 0; i < 400; i++)
                Assert.True(NanSafeEqual(whole[0][i], prefix[0][i]),
                    $"bar {i} differs under APPEND, so this fake no longer isolates the prepend case");

            Assert.Contains("pinned to the start of the array", string.Join(" ", report.Findings));
        }

        [Fact]
        public void DeclaringCausalWhileMovingIsReportedAsAnOverruledClaim()
        {
            var report = CustomIndicatorCausalityProbe.Probe(new RunningTotal());

            var v = report.For("Cumulative");
            Assert.NotNull(v);
            Assert.Equal(ComponentCausality.Causal, v!.Declared);
            Assert.Equal(ComponentCausality.Lookahead, v.Measured);
            Assert.False(v.Publishable);
            Assert.Contains("declaration has been overruled", string.Join(" ", report.Findings));
        }

        [Fact]
        public void AnIndicatorWhoseArraysDoNotMatchItsComponentsFailsOutright()
        {
            var report = CustomIndicatorCausalityProbe.Probe(new ShapeMismatch());

            Assert.True(report.Failed);
            Assert.False(report.AnyPublishable);
            Assert.Contains("component arrays", report.Error);
        }

        [Fact]
        public void AnIndicatorThatOnlyWorksOnALongSeriesFailsOutright()
        {
            // A freshly loaded chart is always the short case, so this is a crash the user meets
            // on the first draw and never in a backtest.
            var report = CustomIndicatorCausalityProbe.Probe(new ThrowsOnShortSeries());

            Assert.True(report.Failed);
            Assert.Contains("freshly loaded chart", report.Error);
        }

        [Fact]
        public void AComponentWithNoValuesEstablishesNothingAndIsNotPublished()
        {
            // Silence is not evidence — the same position NotExercisedByTheseSeries takes for the
            // built-ins. It draws; it is not offered to a strategy.
            var report = CustomIndicatorCausalityProbe.Probe(new NeverProducesAValue());

            var v = report.For("Silent");
            Assert.NotNull(v);
            Assert.Equal(ComponentCausality.Undeclared, v!.Measured);
            Assert.False(v.Publishable);
            Assert.Contains("produced no value", string.Join(" ", report.Findings));
        }

        // ── The gate ──────────────────────────────────────────────────────────────────────────

        [Fact]
        public void RegistrationIsWhereTheProbeRunsAndTheVerdictIsKept()
        {
            // Register is the only door into the app for a scripted indicator, which is why the
            // probe lives there rather than at any one of its callers.
            var registry = new CustomIndicatorRegistry();
            registry.Register(new CleanSma());
            registry.Register(new RunningTotal());

            Assert.True(registry.IsPublishable(nameof(CleanSma), "Sma"));
            Assert.False(registry.IsPublishable(nameof(RunningTotal), "Cumulative"));

            // Unknown ids and unknown components are refused rather than assumed.
            Assert.False(registry.IsPublishable("NOT_REGISTERED", "Sma"));
            Assert.False(registry.IsPublishable(nameof(CleanSma), "NoSuchComponent"));
            Assert.Null(registry.Causality("NOT_REGISTERED"));

            registry.Unregister(nameof(CleanSma));
            Assert.Null(registry.Causality(nameof(CleanSma)));
            Assert.False(registry.IsPublishable(nameof(CleanSma), "Sma"));
        }

        [Fact]
        public void AnIndicatorWritingNoCausalityAtAllGetsTheUndeclaredDefault()
        {
            // The declaration is a default interface member, so indicators written before it
            // existed — and every Pine port the transpiler emits — still compile. What they must
            // not get is the benefit of the doubt.
            Assert.Equal(ComponentCausality.Undeclared,
                CausalityContract.Declared(new ReadsTheFuture().Causality, 0));
            Assert.Equal(ComponentCausality.Undeclared, CausalityContract.Declared(null, 0));

            // Short arrays repeat their last entry, matching the DisplayTypes rule.
            var declared = new[] { ComponentCausality.Lookahead, ComponentCausality.Causal };
            Assert.Equal(ComponentCausality.Lookahead, CausalityContract.Declared(declared, 0));
            Assert.Equal(ComponentCausality.Causal, CausalityContract.Declared(declared, 1));
            Assert.Equal(ComponentCausality.Causal, CausalityContract.Declared(declared, 7));
        }

        private static double[][] Calc(ICustomIndicator ind, List<Ohlcv> bars) =>
            ind.Calculate(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
                new Dictionary<string, double>(ind.DefaultParameters));

        private static bool NanSafeEqual(double a, double b) =>
            (double.IsNaN(a) && double.IsNaN(b)) || a == b;
    }
}
