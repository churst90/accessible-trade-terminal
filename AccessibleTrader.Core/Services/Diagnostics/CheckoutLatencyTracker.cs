using System.Collections.Concurrent;

namespace AccessibleTrader.Core.Services.Diagnostics
{
    /// <summary>
    /// Per-provider latency histogram for <c>MauiApiKeyCheckoutAdapter</c>.
    /// Pure measurement — feeds the data-driven decision on whether the 60-second
    /// session cache discussed in <c>docs/TODO.md</c> ("Hot-path credential cache")
    /// is justified. Singleton; lock-free reads via ConcurrentDictionary, lock
    /// per-provider only on the rolling-window write path.
    ///
    /// Memory cost: bounded — each provider keeps the most recent
    /// <see cref="WindowSize"/> samples. At 16 providers × 256 doubles that's
    /// ~32 KB total. Survives the lifetime of the host singleton; the user can
    /// reset the data via <see cref="Reset"/> or read a snapshot via
    /// <see cref="Snapshot"/>.
    /// </summary>
    public sealed class CheckoutLatencyTracker
    {
        public const int WindowSize = 256;

        private sealed class Window
        {
            public readonly object Gate = new();
            public readonly double[] SamplesMs = new double[WindowSize];
            public int Count;          // total samples seen
            public int NextIndex;      // ring-buffer write head
        }

        private readonly ConcurrentDictionary<string, Window> _windows = new();

        public void Record(string providerId, double elapsedMs)
        {
            var w = _windows.GetOrAdd(providerId, _ => new Window());
            lock (w.Gate)
            {
                w.SamplesMs[w.NextIndex] = elapsedMs;
                w.NextIndex = (w.NextIndex + 1) % WindowSize;
                w.Count++;
            }
        }

        public IReadOnlyList<ProviderLatencySnapshot> Snapshot()
        {
            var snapshots = new List<ProviderLatencySnapshot>();
            foreach (var kv in _windows)
            {
                var w = kv.Value;
                double[] copy;
                int count;
                lock (w.Gate)
                {
                    int n = Math.Min(w.Count, WindowSize);
                    copy = new double[n];
                    Array.Copy(w.SamplesMs, copy, n);
                    count = w.Count;
                }
                if (copy.Length == 0) continue;
                Array.Sort(copy);
                snapshots.Add(new ProviderLatencySnapshot(
                    ProviderId: kv.Key,
                    TotalSamples: count,
                    P50Ms: Percentile(copy, 0.50),
                    P95Ms: Percentile(copy, 0.95),
                    P99Ms: Percentile(copy, 0.99),
                    MaxMs: copy[copy.Length - 1]));
            }
            return snapshots
                .OrderByDescending(s => s.P95Ms)
                .ToList();
        }

        public void Reset() => _windows.Clear();

        private static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            // Linear interpolation between adjacent ranks (NIST handbook method).
            double rank = p * (sorted.Length - 1);
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            if (lo == hi) return sorted[lo];
            double frac = rank - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
        }
    }

    public readonly record struct ProviderLatencySnapshot(
        string ProviderId,
        int TotalSamples,
        double P50Ms,
        double P95Ms,
        double P99Ms,
        double MaxMs);
}
