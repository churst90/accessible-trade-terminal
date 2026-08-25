using System.Collections.Concurrent;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Screening;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Screening
{
    /// <summary>
    /// Default <see cref="IScreenerService"/>.
    ///
    /// <para>
    /// The screen reuses the Strategy Composer's condition tree wholesale: the same
    /// <see cref="ISignalCatalog"/> resolves signal ids, the same <see cref="IConditionEvaluator"/>
    /// applies the operators, and the same indicator providers compute the data. A screen is
    /// therefore "a strategy's entry condition asked across many symbols at one instant" and
    /// inherits every indicator and operator the composer supports without a second DSL.
    /// </para>
    ///
    /// <para>
    /// Concurrency is deliberately low (<see cref="MaxConcurrency"/>). Screens run against live
    /// exchange REST endpoints; fanning 200 symbols out at once is the fastest possible way to
    /// get an API key rate-limited or banned. Fetches go through <see cref="IDataService"/>,
    /// which is already wrapped in the shared rate limiter and on-disk cache.
    /// </para>
    /// </summary>
    public sealed class ScreenerService : IScreenerService
    {
        /// <summary>Simultaneous per-symbol fetches. Kept low on purpose — see class remarks.</summary>
        public const int MaxConcurrency = 4;

        private readonly IDataService _data;
        private readonly IOfflineWorkspaceBuilder _builder;
        private readonly IConditionEvaluator _evaluator;
        private readonly ISignalCatalog _catalog;
        private readonly IMultiTimeframeDataService? _mtf;

        public ScreenerService(
            IDataService data,
            IOfflineWorkspaceBuilder builder,
            IConditionEvaluator evaluator,
            ISignalCatalog catalog,
            IMultiTimeframeDataService? mtf = null)
        {
            _data = data;
            _builder = builder;
            _evaluator = evaluator;
            _catalog = catalog;
            _mtf = mtf;
        }

        public async Task<ScreenerRunResult> RunAsync(
            ScreenerSpec spec,
            IReadOnlyList<WatchlistEntry> entries,
            IProgress<ScreenerProgress>? progress = null,
            CancellationToken ct = default)
        {
            var rows = new ConcurrentDictionary<int, ScreenerRow>();
            int total = entries.Count;
            int completed = 0;

            // Indicator codes are resolved once for the whole run: the tree is identical for
            // every symbol, so there is no reason to walk it 200 times.
            var codes = ResolveIndicatorCodes(spec);
            var htfPairs = ResolveHtfPairs(spec);

            using var gate = new SemaphoreSlim(MaxConcurrency);
            var tasks = new List<Task>(total);

            for (int i = 0; i < total; i++)
            {
                int index = i;
                var entry = entries[index];
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        rows[index] = await ScreenOneAsync(spec, entry, codes, htfPairs, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        gate.Release();
                        progress?.Report(new ScreenerProgress(Interlocked.Increment(ref completed), total, entry.Symbol));
                    }
                }, ct));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var ordered = new List<ScreenerRow>(total);
            for (int i = 0; i < total; i++)
                if (rows.TryGetValue(i, out var row)) ordered.Add(row);

            return new ScreenerRunResult(spec.Id, spec.Name, DateTime.Now, ordered);
        }

        private async Task<ScreenerRow> ScreenOneAsync(
            ScreenerSpec spec,
            WatchlistEntry entry,
            IReadOnlyList<string> codes,
            IReadOnlyList<(string Timeframe, string IndicatorCode)> htfPairs,
            CancellationToken ct)
        {
            try
            {
                var request = new MarketDataRequest(entry.SubType, entry.Symbol, spec.Timeframe, spec.BarCount);
                var (bars, _) = await _data.FetchOhlcvAsync(entry.Provider, request).ConfigureAwait(false);

                // Two bars is the floor for any meaningful comparison (every "crosses" operator
                // reads current and previous). Anything below that is not a screen result.
                if (bars == null || bars.Count < 2)
                    return Empty(entry, ScreenerRowStatus.InsufficientHistory,
                        $"{bars?.Count ?? 0} bars returned for {spec.Timeframe}.");

                // Pre-warm any higher-timeframe indicator the tree references. Without this the
                // evaluator returns false for HTF leaves by design (it will not silently degrade
                // to active-timeframe data), which would make an MTF screen report zero matches.
                if (_mtf != null)
                {
                    foreach (var (timeframe, indicatorCode) in htfPairs)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            await _mtf.PrewarmIndicatorAsync(
                                entry.SubType, entry.Provider, entry.Symbol, timeframe,
                                indicatorCode, new Dictionary<string, object>(), spec.BarCount).ConfigureAwait(false);
                        }
                        catch
                        {
                            // A failed pre-warm leaves the leaf false. The row still evaluates so the
                            // user sees the symbol rather than losing it from the report entirely.
                        }
                    }
                }

                var identity = new ChartIdentity(entry.SubType, entry.Provider, entry.Symbol, spec.Timeframe);
                var failures = new Dictionary<string, string>();
                var state = await _builder.BuildAsync(identity, bars, codes, failures, ct).ConfigureAwait(false);

                bool matched = true;
                double score = 0, maxScore = 0;
                if (spec.Root != null)
                {
                    var evaluation = _evaluator.Evaluate(spec.Root, bars, state);
                    matched = evaluation.OverallTrue;
                    score = evaluation.Score;
                    maxScore = evaluation.MaxScore;
                }

                var last = bars[^1];
                var prev = bars[^2];
                double pct = prev.Close > 0 ? (last.Close - prev.Close) / prev.Close * 100.0 : double.NaN;

                return new ScreenerRow(
                    entry,
                    ScreenerRowStatus.Evaluated,
                    matched,
                    score,
                    maxScore,
                    last.Close,
                    pct,
                    last.Date,
                    ReadColumns(spec, state, bars.Count),
                    failures.Count == 0
                        ? null
                        : "Indicators skipped: " + string.Join(", ", failures.Select(f => $"{f.Key} ({f.Value})")));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Empty(entry, ScreenerRowStatus.Failed, ex.Message);
            }
        }

        /// <summary>
        /// Reads the spec's extra value columns at the last bar. Values are clipped to the bar
        /// count for the same future-leak reason the evaluator clips: an indicator array can be
        /// longer than the bar list when a provider pads.
        /// </summary>
        private Dictionary<string, double> ReadColumns(ScreenerSpec spec, WorkspaceState state, int barCount)
        {
            var result = new Dictionary<string, double>();
            if (spec.Columns == null) return result;

            foreach (var columnId in spec.Columns)
            {
                double value = double.NaN;
                var desc = _catalog.GetById(columnId);
                if (desc != null)
                {
                    var series = state.ActiveSeries.FirstOrDefault(s =>
                        string.Equals(s.IndicatorCode, desc.IndicatorCode, StringComparison.OrdinalIgnoreCase));
                    var data = series?.GetComponentData(desc.ComponentName);
                    if (data is { Length: > 0 })
                    {
                        int idx = Math.Min(barCount, data.Length) - 1;
                        if (idx >= 0) value = data[idx];
                    }
                }
                result[columnId] = value;
            }
            return result;
        }

        private static ScreenerRow Empty(WatchlistEntry entry, ScreenerRowStatus status, string detail) =>
            new(entry, status, false, 0, 0, double.NaN, double.NaN, default,
                new Dictionary<string, double>(), detail);

        /// <summary>
        /// Every indicator code the spec needs: the core projections (price comparisons and the
        /// feature columns depend on them), plus one per distinct signal descriptor referenced by
        /// the tree or the column list.
        /// </summary>
        internal IReadOnlyList<string> ResolveIndicatorCodes(ScreenerSpec spec)
        {
            var codes = new List<string>(OfflineWorkspaceBuilder.CoreProjectedCodes);
            var seen = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);

            void AddSignal(string? signalId)
            {
                if (string.IsNullOrEmpty(signalId)) return;
                var desc = _catalog.GetById(signalId);
                // Fall back to the id's own prefix: descriptor ids are "{CODE}.{component}", so an
                // id the catalog doesn't know still names its indicator. Better to attempt the
                // computation than to skip the leaf silently.
                string? code = desc?.IndicatorCode
                    ?? (signalId.Contains('.') ? signalId[..signalId.IndexOf('.')] : null);
                if (!string.IsNullOrEmpty(code) && seen.Add(code)) codes.Add(code);
            }

            foreach (var leaf in EnumerateLeaves(spec.Root))
            {
                AddSignal(leaf.SignalDescriptorId);
                AddSignal(leaf.SecondSignalDescriptorId);
            }
            if (spec.Columns != null)
                foreach (var columnId in spec.Columns) AddSignal(columnId);

            return codes;
        }

        /// <summary>Distinct (higher timeframe, indicator code) pairs that need pre-warming.</summary>
        internal IReadOnlyList<(string Timeframe, string IndicatorCode)> ResolveHtfPairs(ScreenerSpec spec)
        {
            var pairs = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var leaf in EnumerateLeaves(spec.Root))
            {
                if (string.IsNullOrEmpty(leaf.Timeframe)) continue;
                foreach (var signalId in new[] { leaf.SignalDescriptorId, leaf.SecondSignalDescriptorId })
                {
                    if (string.IsNullOrEmpty(signalId)) continue;
                    var desc = _catalog.GetById(signalId);
                    if (desc == null) continue;
                    if (seen.Add($"{leaf.Timeframe}|{desc.IndicatorCode}"))
                        pairs.Add((leaf.Timeframe!, desc.IndicatorCode));
                }
            }
            return pairs;
        }

        internal static IEnumerable<ConditionLeaf> EnumerateLeaves(ConditionNode? node)
        {
            if (node == null) yield break;
            if (node is ConditionLeaf leaf) { yield return leaf; yield break; }
            if (node is ConditionGroup group)
                foreach (var child in group.Children)
                    foreach (var descendant in EnumerateLeaves(child))
                        yield return descendant;
        }
    }
}
