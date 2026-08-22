using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// An indicator that draws an empty line is the hardest kind of defect to report: nothing
    /// throws, nothing is logged, and to the user it is indistinguishable from a market that had
    /// nothing to say. Seventeen were in that state on 2026-08-22.
    ///
    /// <para>
    /// Two causes, one symptom. <see cref="SkenderCalculationCore"/> resolves a calculation by
    /// reflecting on <c>"Get" + Code</c>, and writes one span per public property of the result,
    /// keyed on the PROPERTY NAME. So a code whose method has a different name resolves to nothing,
    /// and a component whose declared <c>Name</c> is not a property of that result is never
    /// written. Both are silent by construction — an ordinal dictionary lookup that misses cannot
    /// announce itself.
    /// </para>
    ///
    /// <para>
    /// Bollinger Bands was one of them, and it is one of the seven indicators the anonymous public
    /// demo offers by name.
    /// </para>
    /// </summary>
    public class IndicatorsThatRenderNothingTests
    {
        private static readonly IIndicatorProvider[] SkenderProviders =
        {
            new SkenderTrendProvider(),
            new SkenderBandProvider(),
            new SkenderVolatilityProvider(),
            new SkenderVolumeProvider(),
            new SkenderBoundedOscillatorProvider(),
            new SkenderZeroCrossProvider(),
        };

        // ── Every offered indicator can actually be computed ─────────────────

        [Fact]
        public void EveryOfferedSkenderIndicator_ResolvesACalculation()
        {
            // The guard the audit asked for. It covers both causes at once and is the only thing
            // that stops this class recurring, because the failure is otherwise invisible.
            var service = new IndicatorService(SkenderProviders, NullLogger<IndicatorService>.Instance);

            var unresolvable = service.GetAvailableIndicators()
                .Select(m => m.Code)
                .Where(code => !SkenderCalculationCore.CanResolve(code))
                .ToList();

            Assert.True(unresolvable.Count == 0,
                "Offered but not computable: " + string.Join(", ", unresolvable));
        }

        [Theory]
        [InlineData("Bb", "BollingerBands")]      // the one the public demo ships
        [InlineData("Kc", "Keltner")]
        [InlineData("ChandelierExit", "Chandelier")]
        [InlineData("UltOsc", "Ultimate")]
        [InlineData("Mom", "Roc")]                // Momentum is the first column of Skender's ROC
        public void AMisnamedCode_IsAliasedToTheRealMethod(string code, string method)
        {
            Assert.Equal(method, SkenderCalculationCore.SkenderMethodName(code));
            Assert.True(SkenderCalculationCore.CanResolve(code), $"{code} still resolves to nothing");
        }

        [Theory]
        [InlineData("Ppo")]     // Skender 2.5.0 has GetPvo (volume), not PPO
        [InlineData("Zlema")]
        [InlineData("Tma")]
        [InlineData("Hv")]
        [InlineData("Eom")]
        public void AnIndicatorSkenderDoesNotImplement_IsNotOffered(string code)
        {
            // Withheld rather than listed-and-blank. A menu entry that can never produce a value
            // costs the user their time and gives them nothing to report but "it does not work".
            var service = new IndicatorService(SkenderProviders, NullLogger<IndicatorService>.Instance);

            Assert.DoesNotContain(service.GetAvailableIndicators(),
                m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void NonSkenderIndicators_AreNeverFilteredOut()
        {
            // The filter keys on the provider type. A Cipher or custom indicator has no Skender
            // method by design and must not be dropped by a check aimed at Skender.
            var service = new IndicatorService(
                new IIndicatorProvider[] { new SwingStructureProvider() },
                NullLogger<IndicatorService>.Instance);

            Assert.Contains(service.GetAvailableIndicators(),
                m => m.Code == SwingStructureProvider.Code);
        }

        // ── Every declared component is a name the calculation writes ────────

        [Fact]
        public void EveryDeclaredComponent_ReceivesAValue()
        {
            // The second cause: the component name has to be a property of the result type, or the
            // span is never written and the line renders empty.
            var bars = SyntheticBars(400);
            var failures = new List<string>();

            foreach (var provider in SkenderProviders)
            {
                foreach (var meta in provider.GetIndicators())
                {
                    if (!SkenderCalculationCore.CanResolve(meta.Code)) continue;   // covered above

                    var parameters = meta.Parameters.ToDictionary(
                        p => p.Name, p => (object)p.DefaultValue!);
                    var data = new Dictionary<string, double[]>();
                    var buffer = new IndicatorResultBuffer(data, bars.Length);

                    try { provider.Calculate(meta.Code, bars, parameters, buffer); }
                    catch (Exception ex) { failures.Add($"{meta.Code}: threw {ex.GetType().Name}"); continue; }

                    foreach (var component in meta.Components)
                    {
                        var span = data.TryGetValue(component.Name, out var arr)
                            ? arr : Array.Empty<double>();
                        if (span.Length == 0 || span.All(double.IsNaN))
                            failures.Add($"{meta.Code}.{component.Name}");
                    }
                }
            }

            Assert.True(failures.Count == 0,
                "Declared components that never receive a value (they render as empty lines):\n  "
                + string.Join("\n  ", failures));
        }

        /// <summary>Bars with enough movement and history for every lookback to warm up.</summary>
        private static Ohlcv[] SyntheticBars(int count)
        {
            var bars = new Ohlcv[count];
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double price = 100;
            for (int i = 0; i < count; i++)
            {
                // A deterministic wave plus drift: never flat (which would make a range-based
                // indicator legitimately NaN and hide a real miss), never random (which would make
                // a failure unreproducible).
                price += Math.Sin(i / 7.0) * 1.5 + 0.05;
                double high = price + 1.2, low = price - 1.1;
                bars[i] = new Ohlcv(t0.AddHours(i), price - 0.3, high, low, price, 1000 + (i % 97));
            }
            return bars;
        }
    }
}
