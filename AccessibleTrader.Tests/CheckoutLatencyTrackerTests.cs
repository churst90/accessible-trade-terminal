using AccessibleTrader.Core.Services.Diagnostics;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pin the per-provider P50/P95/P99 percentile arithmetic so the
    /// measurement that feeds the "Hot-path credential cache" decision is
    /// trustworthy. Sample math uses NIST-handbook linear interpolation
    /// between adjacent ranks.
    /// </summary>
    public class CheckoutLatencyTrackerTests
    {
        [Fact]
        public void EmptyTracker_HasNoSnapshots()
        {
            var t = new CheckoutLatencyTracker();
            Assert.Empty(t.Snapshot());
        }

        [Fact]
        public void SingleSample_ReportsThatValueAtEveryPercentile()
        {
            var t = new CheckoutLatencyTracker();
            t.Record("Binance", 12.5);

            var snap = t.Snapshot().Single();
            Assert.Equal("Binance", snap.ProviderId);
            Assert.Equal(1, snap.TotalSamples);
            Assert.Equal(12.5, snap.P50Ms);
            Assert.Equal(12.5, snap.P95Ms);
            Assert.Equal(12.5, snap.P99Ms);
            Assert.Equal(12.5, snap.MaxMs);
        }

        [Fact]
        public void HundredEvenSamples_PercentileOrderingHolds()
        {
            var t = new CheckoutLatencyTracker();
            for (int i = 1; i <= 100; i++) t.Record("Coinbase", i);

            var snap = t.Snapshot().Single();
            Assert.Equal(100, snap.TotalSamples);
            Assert.True(snap.P50Ms >= 50.0 && snap.P50Ms <= 51.0);
            Assert.True(snap.P95Ms >= 95.0 && snap.P95Ms <= 96.0);
            Assert.True(snap.P99Ms >= 98.0 && snap.P99Ms <= 100.0);
            Assert.Equal(100.0, snap.MaxMs);
            // Monotonic ordering across percentiles.
            Assert.True(snap.P50Ms <= snap.P95Ms);
            Assert.True(snap.P95Ms <= snap.P99Ms);
        }

        [Fact]
        public void ExceedingWindowSize_KeepsOnlyMostRecentSamples()
        {
            // Record WindowSize + 50 samples. The first 50 should age out;
            // the snapshot should reflect only the last WindowSize.
            var t = new CheckoutLatencyTracker();
            int n = CheckoutLatencyTracker.WindowSize + 50;
            for (int i = 1; i <= n; i++) t.Record("Kraken", i);

            var snap = t.Snapshot().Single();
            Assert.Equal(n, snap.TotalSamples);  // Total counter still increments
            // Max is the last value seen in the window. Window holds samples
            // from index 51 to n inclusive (the most recent WindowSize entries),
            // so max == n.
            Assert.Equal(n, snap.MaxMs);
            // P50 of [51..n] is around (51+n)/2.
            double expectedP50 = (51.0 + n) / 2.0;
            Assert.True(System.Math.Abs(snap.P50Ms - expectedP50) < 2.0,
                $"P50 was {snap.P50Ms}, expected ~{expectedP50}.");
        }

        [Fact]
        public void MultipleProviders_TrackedIndependently_OrderedByP95Desc()
        {
            var t = new CheckoutLatencyTracker();
            // Slow provider.
            for (int i = 0; i < 10; i++) t.Record("Slow", 100);
            // Fast provider.
            for (int i = 0; i < 10; i++) t.Record("Fast", 5);

            var snaps = t.Snapshot();
            Assert.Equal(2, snaps.Count);
            // Snapshot ordering: highest P95 first.
            Assert.Equal("Slow", snaps[0].ProviderId);
            Assert.Equal("Fast", snaps[1].ProviderId);
        }

        [Fact]
        public void Reset_ClearsAllProviders()
        {
            var t = new CheckoutLatencyTracker();
            t.Record("X", 1);
            t.Record("Y", 2);
            Assert.Equal(2, t.Snapshot().Count);

            t.Reset();
            Assert.Empty(t.Snapshot());
        }
    }
}
