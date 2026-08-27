using AccessibleTrader.Sdk.Models;
using AccessibleTrader.StrategyLab;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The lab's statistical primitives, and the first known-input/known-output arithmetic
    /// tests it has ever had.
    ///
    /// <para>
    /// The A2-era census found <b>59 of the 80 lab files have zero test references</b>, and
    /// <b>zero of the commands that produced a <c>docs/*_FINDINGS.md</c> are guarded</b> —
    /// there was no test anywhere asserting a known equity multiple, return, drawdown or CAGR
    /// for any lab command. <c>StrategyLabTests.cs</c> covers <c>TradeR</c>, a bootstrap CI,
    /// marker-side classification and snapshot cache-key round-tripping; none of the backtest
    /// arithmetic.
    /// </para>
    ///
    /// <para>
    /// This file does not close that gap — it closes the part of it the 2026-08-27 HIGH pass
    /// touched: the permutation machinery whose defects made published p-values wrong, and the
    /// snapshot hash that lets a verdict name the sample it was computed on.
    /// </para>
    /// </summary>
    public class LabStatisticsTests
    {
        // ── Block permutation ────────────────────────────────────────────────

        /// <summary>
        /// A pool with a real difference between the two halves, built so the rows within each
        /// block are correlated — which is the situation a row-wise shuffle mishandles.
        /// </summary>
        private static double[] BlockyPool(int blocks, int blockSize, double groupBShift)
        {
            var pool = new double[blocks * blockSize];
            var rng = new Random(1);
            for (int b = 0; b < blocks; b++)
            {
                // One draw per BLOCK, repeated across it — maximal within-block dependence,
                // which is what overlapping forward windows approximate.
                double level = rng.NextDouble() - 0.5;
                for (int i = 0; i < blockSize; i++)
                    pool[b * blockSize + i] = level + (b * blockSize + i >= pool.Length / 2 ? groupBShift : 0);
            }
            return pool;
        }

        [Fact]
        public void Block_permutation_is_more_conservative_than_row_wise_on_correlated_data()
        {
            // The whole point of the finding: rows that share their forward window are not
            // exchangeable, so a row-wise null is too narrow and significance is inflated by
            // roughly the square root of the horizon.
            const int blockSize = 20;
            var pool = BlockyPool(blocks: 40, blockSize: blockSize, groupBShift: 0.0);

            int n = pool.Length / 2;
            double observed = pool.Take(n).Average() - pool.Skip(n).Average();

            double rowWise = LabStats.PermutationP(pool, n, n, observed, runs: 2000, seed: 7);
            double blocky = LabStats.BlockPermutationP(pool, n, n, observed, runs: 2000, seed: 7,
                                                       blockSize: blockSize);

            Assert.True(blocky >= rowWise,
                $"block p ({blocky:0.0000}) should not be smaller than row-wise ({rowWise:0.0000}) "
                + "on data whose rows are correlated within a block.");
        }

        [Fact]
        public void A_block_size_of_one_is_the_plain_row_wise_test()
        {
            // Non-overlapping rows need no correction, and a caller that says so must get
            // exactly the old behaviour rather than a subtly different one.
            var pool = BlockyPool(blocks: 20, blockSize: 5, groupBShift: 0.3);
            int n = pool.Length / 2;
            double observed = pool.Take(n).Average() - pool.Skip(n).Average();

            Assert.Equal(
                LabStats.PermutationP(pool, n, n, observed, runs: 500, seed: 11),
                LabStats.BlockPermutationP(pool, n, n, observed, runs: 500, seed: 11, blockSize: 1),
                6);
        }

        [Fact]
        public void A_real_difference_is_still_detected()
        {
            // Vacuity check: a "correction" that returns 1.0 for everything would satisfy the
            // conservatism assertion above and destroy the tool.
            var rng = new Random(3);
            var pool = new double[800];
            for (int i = 0; i < pool.Length; i++)
                pool[i] = rng.NextDouble() + (i < 400 ? 1.0 : 0.0);   // a large, obvious gap

            double observed = pool.Take(400).Average() - pool.Skip(400).Average();
            double p = LabStats.BlockPermutationP(pool, 400, 400, observed, runs: 2000, seed: 5,
                                                  blockSize: 20);

            Assert.True(p < 0.05, $"an obvious difference came back at p = {p:0.0000}");
        }

        [Fact]
        public void Too_few_blocks_reports_no_evidence_rather_than_a_computed_looking_p()
        {
            // With three blocks there are six orderings; a p from that is a number with the
            // shape of a result and none of the content.
            var pool = BlockyPool(blocks: 3, blockSize: 10, groupBShift: 5.0);
            int n = pool.Length / 2;
            double observed = pool.Take(n).Average() - pool.Skip(n).Average();

            Assert.Equal(1.0, LabStats.BlockPermutationP(pool, n, n, observed, 500, 5, blockSize: 10));
        }

        [Fact]
        public void Block_permutation_is_reproducible_from_its_seed()
        {
            var pool = BlockyPool(blocks: 30, blockSize: 10, groupBShift: 0.2);
            int n = pool.Length / 2;
            double observed = pool.Take(n).Average() - pool.Skip(n).Average();

            Assert.Equal(
                LabStats.BlockPermutationP(pool, n, n, observed, 500, seed: 42, blockSize: 10),
                LabStats.BlockPermutationP(pool, n, n, observed, 500, seed: 42, blockSize: 10),
                10);
        }

        // ── Snapshot provenance ──────────────────────────────────────────────

        private static List<Ohlcv> Bars(int n, double start = 100) =>
            Enumerable.Range(0, n)
                .Select(i => new Ohlcv(
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                    start + i, start + i + 1, start + i - 1, start + i, 1000 + i))
                .ToList();

        [Fact]
        public void The_same_bars_hash_the_same()
        {
            Assert.Equal(SnapshotFile.HashBars(Bars(50)), SnapshotFile.HashBars(Bars(50)));
        }

        [Fact]
        public void A_different_sample_hashes_differently()
        {
            // This is the whole point. The xs-momentum decay note recorded "p = 0.0044 vs the
            // recorded 0.0045 (permutation noise)" — but the routine is seeded and
            // deterministic, so the p cannot move unless the DATA moved. It was a different
            // sample, unrecorded, and nothing could have said so.
            Assert.NotEqual(SnapshotFile.HashBars(Bars(50)), SnapshotFile.HashBars(Bars(51)));
            Assert.NotEqual(SnapshotFile.HashBars(Bars(50)), SnapshotFile.HashBars(Bars(50, start: 101)));
        }

        [Fact]
        public void A_single_changed_bar_changes_the_hash()
        {
            var a = Bars(50);
            var b = Bars(50);
            b[25] = b[25] with { Close = b[25].Close + 0.00001 };

            Assert.NotEqual(SnapshotFile.HashBars(a), SnapshotFile.HashBars(b));
        }

        [Fact]
        public void Reordering_the_bars_changes_the_hash()
        {
            // Order is part of the sample: the same bars in a different order are a different
            // series to everything downstream.
            var a = Bars(50);
            var b = Bars(50);
            (b[10], b[11]) = (b[11], b[10]);

            Assert.NotEqual(SnapshotFile.HashBars(a), SnapshotFile.HashBars(b));
        }

        [Fact]
        public void The_hash_does_not_depend_on_the_machines_culture()
        {
            // A comma-decimal machine formatting 1.5 as "1,5" would hash the same data
            // differently, which would make the provenance record useless across machines.
            var bars = Bars(20);
            string invariant = SnapshotFile.HashBars(bars);

            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.Equal(invariant, SnapshotFile.HashBars(bars));
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }
    }
}
