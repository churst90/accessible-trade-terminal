using System;
using System.Reflection;
using Xunit;
using AccessibleTrader.Core.Services.Input;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests for the internal static scan helpers in <see cref="IndicatorCrossingEngine"/>:
    /// ScanSignCrossing (zero-line) and ScanThresholdCrossing (OB/OS).
    ///
    /// These helpers are internal but critical enough to regression-test directly.
    /// Reflection is used to invoke them without exposing them to public API.
    /// </summary>
    public class CrossingNavigationTests
    {
        // ── Reflection access ─────────────────────────────────────────────────

        private static readonly Type DispatcherType =
            typeof(IndicatorCrossingEngine);

        /// <summary>Invokes CommandDispatcher.ScanSignCrossing via reflection.</summary>
        private static int InvokeScanSignCrossing(double[] data, int current, bool scanRight, double threshold)
        {
            var method = DispatcherType.GetMethod(
                "ScanSignCrossing",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("ScanSignCrossing not found.");

            var result = method.Invoke(null, new object[] { data, current, scanRight, threshold });
            return (int)result!;
        }

        /// <summary>Invokes CommandDispatcher.ScanThresholdCrossing via reflection.</summary>
        private static int InvokeScanThresholdCrossing(
            double[] data, int current, bool scanRight,
            double level, bool aboveIsZone, out string message)
        {
            var method = DispatcherType.GetMethod(
                "ScanThresholdCrossing",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("ScanThresholdCrossing not found.");

            object?[] args = { data, current, scanRight, level, aboveIsZone, null };
            var result = method.Invoke(null, args);
            message = (string)(args[5] ?? string.Empty);
            return (int)result!;
        }

        // ── 1. ScanSignCrossing — zero-line crossing found ─────────────────────
        // Data: [5, 3, 1, -1, -3] — crosses threshold=0 between indices 2 and 3.
        // Scanning left from index 4 should find index 3 (the first bar that crossed below zero).

        [Fact]
        public void ScanZeroLineCrossing_FindsCrossing_InSimpleData()
        {
            double[] data = { 5.0, 3.0, 1.0, -1.0, -3.0 };
            int found = InvokeScanSignCrossing(data, current: 4, scanRight: false, threshold: 0.0);

            // The crossing is between index 2 (positive) and index 3 (negative).
            // ScanSignCrossing returns the first index where the sign flipped.
            Assert.True(found == 3 || found == 2,
                $"Expected crossing at index 2 or 3 but got {found}.");
        }

        // ── 2. ScanThresholdCrossing — RSI entering overbought ─────────────────
        // Data: [50, 60, 65, 71, 75] — crosses above 70 between index 2 and 3.
        // Scanning left from index 4 should find index 3.

        [Fact]
        public void ScanThresholdCrossing_FindsOverboughtEntry()
        {
            double[] data = { 50.0, 60.0, 65.0, 71.0, 75.0 };
            int found = InvokeScanThresholdCrossing(
                data, current: 4, scanRight: false,
                level: 70.0, aboveIsZone: true, out string message);

            // The crossover into OB territory happens between index 2 (65 < 70) and index 3 (71 >= 70).
            Assert.Equal(3, found);
            Assert.False(string.IsNullOrEmpty(message),
                "ScanThresholdCrossing should produce a speech message for OB entry.");
        }

        // ── 3. ScanSignCrossing — no crossing returns -1 ─────────────────────
        // Data all positive: [5, 3, 1, 2, 4] — no crossing through 0, should return -1.

        [Fact]
        public void ScanZeroLineCrossing_AllPositive_ReturnsNegativeOne()
        {
            double[] data = { 5.0, 3.0, 1.0, 2.0, 4.0 };
            int found = InvokeScanSignCrossing(data, current: 4, scanRight: false, threshold: 0.0);
            Assert.Equal(-1, found);
        }
    }
}
