using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Ambient cache for per-bar VPVR / TPO profile bin snapshots used during backtests to
    /// avoid future-leak. Live trading leaves this cache empty and
    /// <see cref="Levels.VolumeProfileLevelProvider"/> falls through to <c>series.ProfileBins</c>
    /// (the workspace's eagerly-computed live state). Backtests populate the cache once per
    /// bar — the level provider then reads the bar-i bin snapshot instead of the live one,
    /// preventing the strategy at backtest bar 100 from seeing the final profile state at bar 800.
    ///
    /// Keyed by series identifier (the indicator code is good enough — there's only one VPVR
    /// per chart in the current architecture).
    /// </summary>
    public interface IBacktestProfileCache
    {
        /// <summary>True when the cache currently holds a snapshot for any series — i.e. we are
        /// inside a backtest bar loop. The level provider checks this to decide whether to
        /// consult the cache or fall through to <c>series.ProfileBins</c>.</summary>
        bool IsActive { get; }

        /// <summary>Sets the bar-i bin snapshot for a series (overwrites any prior snapshot).</summary>
        void Set(string seriesIdOrCode, IReadOnlyList<ProfileBin> bins);

        /// <summary>Retrieves the current snapshot for a series, or null if none has been set.</summary>
        IReadOnlyList<ProfileBin>? Get(string seriesIdOrCode);

        /// <summary>Drops every snapshot. Call between backtest runs and after the run completes.</summary>
        void Clear();
    }

    /// <summary>
    /// Default in-memory <see cref="IBacktestProfileCache"/>. Singleton — one cache per
    /// application instance, exclusively populated by <c>StrategyBacktester</c> while a
    /// backtest is running.
    /// </summary>
    public class BacktestProfileCache : IBacktestProfileCache
    {
        private readonly Dictionary<string, IReadOnlyList<ProfileBin>> _snapshots
            = new(System.StringComparer.OrdinalIgnoreCase);

        public bool IsActive => _snapshots.Count > 0;

        public void Set(string seriesIdOrCode, IReadOnlyList<ProfileBin> bins)
        {
            if (string.IsNullOrEmpty(seriesIdOrCode)) return;
            _snapshots[seriesIdOrCode] = bins ?? new List<ProfileBin>();
        }

        public IReadOnlyList<ProfileBin>? Get(string seriesIdOrCode)
        {
            if (string.IsNullOrEmpty(seriesIdOrCode)) return null;
            return _snapshots.TryGetValue(seriesIdOrCode, out var bins) ? bins : null;
        }

        public void Clear() => _snapshots.Clear();
    }
}
