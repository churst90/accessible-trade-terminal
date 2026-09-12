using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>What one poll's bars produced. See <see cref="HeadlessChart.ObserveAsync"/>.</summary>
    /// <param name="Narration">The composed ladder for the bar that closed, or null when nothing
    /// closed, nothing was found, or this was the first sighting.</param>
    /// <param name="NeedsMoreHistory">The bars handed in do not reach back to the last bar this
    /// chart holds — a poll was missed for longer than the catch-up window — and the caller
    /// should fetch <see cref="HeadlessChart.BarsNeeded"/> bars and observe again.</param>
    /// <param name="BarClosed">Whether the newest bar advanced since the last observation.</param>
    /// <param name="State">The chart as the alert evaluator needs it — the held bars in
    /// <c>Data</c>, every series in <c>ActiveSeries</c> recomputed against them — or null when
    /// there was nothing to compute (no bars, or a gap the caller must close first). Populated on
    /// the FIRST sighting too: a price alert has two closes to compare from the first poll.</param>
    /// <param name="PreviousValues">Every indicator component's value at the newest bar as of the
    /// PREVIOUS observation — the crossover memory <c>AlertEvaluator</c> reads for an indicator
    /// alert. Empty on the first sighting, which is the warm-up: an indicator alert cannot cross
    /// from a value nobody has seen.</param>
    public sealed record HeadlessObservation(
        string? Narration, bool NeedsMoreHistory, bool BarClosed,
        WorkspaceState? State = null,
        IReadOnlyDictionary<string, double>? PreviousValues = null);

    /// <summary>
    /// <b>ONE saved chart, with the browser closed.</b> The state the narration ladder and the
    /// alert evaluator both need, composed per chart without a store — design (B) of
    /// docs/BACKGROUND_MONITOR_PHASE3_SCOPE.md §2.
    ///
    /// <para>
    /// ── What it is ─────────────────────────────────────────────────────────────
    /// Cody, 2026-09-11: <i>"I also want the narration ladder to also be spoken when the
    /// browser is closed too."</i> In-session the ladder is <c>AutoNarrationService</c>: bound to
    /// the focused chart's store, scanning after every redraw. With no browser there is no store,
    /// no redraw and no focused chart — there is the last autosaved session, which says which
    /// charts were open and which of their series carry the N flag, and a background monitor that
    /// re-fetches each chart once a minute. This class is the piece between the two: it holds the
    /// saved tab's series, rebuilt exactly as a workspace load rebuilds them
    /// (<c>SeriesManagementService.MaterializeSaved</c> — derived name, component selection,
    /// levels, the lot), keeps enough bars to compute them, recomputes them through the
    /// store-free <see cref="IIndicatorEngine"/> on every poll, and runs the very same
    /// <see cref="NarrationScanner"/> over the result.
    /// </para>
    ///
    /// <para>
    /// ── And the ALERTS on the chart, since 2026-09-11 (D4's second half) ─────────
    /// The background alert monitor used to hand the evaluator <c>WorkspaceState.Initial</c> —
    /// no <c>Data</c>, no <c>ActiveSeries</c> — so four of its five "cannot watch this in the
    /// background" refusals were self-inflicted (scope §1 F3): indicator, POC, trend and zone
    /// alerts read a chart, and the chart was blank. The state this class already built for the
    /// ladder is exactly the state those alerts need. So the template it holds is the narrated
    /// series PLUS every series an alert references (built from the saved tab's config when the
    /// tab has that indicator, from the indicator's defaults when it does not — see
    /// <see cref="HeadlessChartFactory"/>), the scanner filters on the N flag as it always has,
    /// and every observation returns the computed <see cref="HeadlessObservation.State"/> and
    /// the previous poll's component values. Profiles are computed too, over the whole buffer,
    /// because a POC alert reads one.
    /// </para>
    ///
    /// <para>
    /// ── The bars are a BUFFER, not a window ────────────────────────────────────
    /// The scanner remembers markers by bar INDEX, so the indices it sees must mean the same bar
    /// from one poll to the next. A fresh <c>Limit: N</c> fetch each minute would shift every
    /// index by one, and the 20-bar marker window would re-announce a signal it had just
    /// announced. So the first sighting fetches <see cref="BarsNeeded"/> bars (the indicators'
    /// stability window plus the scanner's look-back) and every later poll fetches
    /// <see cref="CatchUpLimit"/> and MERGES: a bar newer than anything held is appended, a bar
    /// with a date already held REPLACES it. The replace matters as much as the append — the
    /// previous poll saw the bar that has now closed while it was still forming, and the reading
    /// spoken at the close ("Volume 12,345") must be its final value, not the mid-bar one.
    /// When the buffer grows past twice what is needed, the front is trimmed and the scanner is
    /// told by how much (<see cref="NarrationScanner.ShiftIndices"/>).
    /// </para>
    ///
    /// <para>
    /// ── What it does NOT decide ────────────────────────────────────────────────
    /// Whether the ladder is SPOKEN, or an alert DELIVERED. The monitor owns that: the master
    /// narration switch, the per-chart ownership rule (a browser that has this chart open is
    /// already narrating it and evaluating its alerts), and the composition with the bar-close
    /// sentence. This class observes and composes. It observes even while a browser covers the
    /// chart, for the same reason the monitor tracks covered charts' timestamps: the moment the
    /// browser closes, the seed is already warm.
    /// </para>
    /// </summary>
    public sealed class HeadlessChart
    {
        /// <summary>The fewest bars worth computing an indicator on, whatever it declares.</summary>
        public const int MinBars = 50;
        /// <summary>A headless poll is not a chart load: the in-session load is 200 bars, and an
        /// EMA 200 with the scanner's look-back on top fits comfortably under this.</summary>
        public const int MaxBars = 500;
        /// <summary>Bars fetched per poll once the buffer is warm: the forming bar, the one that
        /// just closed, and one more so a poll that ran a few seconds late still overlaps.</summary>
        public const int CatchUpLimit = 3;

        private readonly ChartIdentity _identity;
        private readonly IIndicatorEngine _engine;
        private readonly IIndicatorStateMapper _mapper;
        private readonly IIndicatorContextAnalyzer _analyzer;
        private readonly IProfileService? _profiles;
        private readonly ILogger? _logger;
        private readonly List<ChartSeries> _template;
        private readonly List<Ohlcv> _bars = new();
        private NarrationScanner _scanner;
        private Dictionary<string, double> _lastValues = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every series this chart carries: the saved tab's narrated ones and the ones
        /// its alerts reference.</summary>
        public IReadOnlyList<ChartSeries> Series => _template;

        /// <summary>The narrated series this chart carries, as rebuilt from the saved tab.</summary>
        public IReadOnlyList<ChartSeries> NarratedSeries => _template.Where(s => s.IsAutoNarrated).ToList();

        /// <summary>Whether there is anything to narrate at all — a saved tab with nothing under
        /// N costs no scan.</summary>
        public bool HasNarratedSeries => _template.Any(s => s.IsAutoNarrated);

        /// <summary>Whether there is anything to compute at all. A chart with no series still
        /// observes its bars — price alerts read those — but computes nothing.</summary>
        public bool HasSeries => _template.Count > 0;

        /// <summary>Indicator codes an alert on this chart references that could not be built —
        /// a plugin that is not loaded, a code nothing answers to. The monitor SAYS so, once:
        /// an alert on an indicator that does not exist here is an alert that is not being
        /// watched, and silence must never read as coverage.</summary>
        public IReadOnlyList<string> MissingIndicators { get; }

        /// <summary>How many bars the first fetch (and a catch-up after a long gap) should ask for.</summary>
        public int BarsNeeded { get; }

        /// <summary>How many bars are held right now. Zero until the first observation.</summary>
        public int BufferedBars => _bars.Count;

        /// <summary>What the NEXT fetch should ask for: the full window on a cold buffer, the
        /// catch-up window once it is warm.</summary>
        public int FetchLimit => _bars.Count == 0 ? BarsNeeded : CatchUpLimit;

        internal HeadlessChart(
            ChartIdentity identity,
            IReadOnlyList<ChartSeries> template,
            IIndicatorEngine engine,
            IIndicatorStateMapper mapper,
            IIndicatorContextAnalyzer analyzer,
            ILogger? logger,
            IProfileService? profiles = null,
            IReadOnlyList<string>? missingIndicators = null)
        {
            _identity = identity;
            _engine = engine;
            _mapper = mapper;
            _analyzer = analyzer;
            _profiles = profiles;
            _logger = logger;
            _template = template.ToList();
            _scanner = new NarrationScanner(analyzer);
            MissingIndicators = missingIndicators ?? Array.Empty<string>();
            BarsNeeded = ComputeBarsNeeded();
        }

        /// <summary>
        /// The indicators' own stability windows, plus the scanner's look-back, clamped.
        /// Asked of the providers directly — the same numbers <c>IBacktestWarmupAnalyzer</c>
        /// reads, without going through a strategy spec that does not exist here.
        /// </summary>
        private int ComputeBarsNeeded()
        {
            int window = 0;
            foreach (var s in _template)
            {
                if (string.IsNullOrEmpty(s.IndicatorCode)) continue;
                try
                {
                    var provider = _engine.GetProvider(s.IndicatorCode);
                    if (provider == null) continue;
                    window = Math.Max(window, provider.GetStabilityWindow(s.IndicatorCode, s.BuildParameterMap()));
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Stability window unavailable for {Code}; using the floor.", s.IndicatorCode);
                }
            }
            return Math.Clamp(window + NarrationScanner.PivotConfirmWindow + 5, MinBars, MaxBars);
        }

        /// <summary>
        /// Hand over one poll's bars. Returns the ladder for the bar that closed since the last
        /// call, or null — and the computed state either way, for the alerts. The first call
        /// SEEDS the ladder and says nothing: the alternative is announcing, on startup, every
        /// signal in the look-back window as if it were news.
        /// </summary>
        /// <param name="fetched">Oldest first. The newest bar is the FORMING one, as every
        /// provider returns it and as the in-session store holds it.</param>
        /// <param name="isFullHistory">True when <paramref name="fetched"/> was asked for with
        /// <see cref="BarsNeeded"/>: a gap in front of it cannot be closed by fetching more, so the
        /// buffer is replaced and re-seeded rather than asking again.</param>
        public async Task<HeadlessObservation> ObserveAsync(IReadOnlyList<Ohlcv> fetched, bool isFullHistory, CancellationToken ct)
        {
            // Nothing to compute and nothing to scan: a tab with nothing under N and no alert
            // that reads a series costs no buffer. Price alerts on such a chart read the fetched
            // bars directly (the monitor builds them a bare state).
            if (fetched == null || fetched.Count == 0 || !HasSeries)
                return new HeadlessObservation(null, false, false);

            var sorted = fetched.OrderBy(b => b.Date).ToList();

            if (_bars.Count == 0)
            {
                _bars.AddRange(sorted);
                var seeded = await SeedAsync(ct).ConfigureAwait(false);
                return new HeadlessObservation(null, false, false, seeded, Snapshot(seeded));
            }

            // No overlap between what we hold and what arrived: a poll was missed for longer than
            // the catch-up window covers. Ask for the full window once; if even that does not
            // reach back, start over — the newest bars are the only ones still true, and the
            // scanner's memory of a chart it has not seen for an hour is worth less than a seed.
            if (sorted[0].Date > _bars[^1].Date)
            {
                if (!isFullHistory) return new HeadlessObservation(null, NeedsMoreHistory: true, false);

                _logger?.LogInformation("Headless chart {Symbol} {Timeframe} re-seeded after a gap.",
                    _identity.Symbol, _identity.Timeframe);
                _bars.Clear();
                _bars.AddRange(sorted);
                _scanner = new NarrationScanner(_analyzer);
                _lastValues.Clear();
                var reseeded = await SeedAsync(ct).ConfigureAwait(false);
                return new HeadlessObservation(null, false, false, reseeded, Snapshot(reseeded));
            }

            int appended = Merge(sorted);
            Trim();

            var state = await ComputeStateAsync(ct).ConfigureAwait(false);
            var previous = Snapshot(state);

            if (appended == 0)
                return new HeadlessObservation(null, false, false, state, previous);   // the forming bar refreshed; no close

            // The bar that CLOSED is the one before the newest (forming) bar. After a missed poll
            // several bars closed at once; the newest closed one is the only one still news, and
            // the scanner's marker window covers the ones before it.
            int closedBound = _bars.Count - 2;
            string? text = HasNarratedSeries
                ? _scanner.ScanAll(state.ActiveSeries, state, closedBound, isBarClose: true)
                : null;
            return new HeadlessObservation(text, false, true, state, previous);
        }

        /// <summary>Append what is newer, replace what is already held. Returns how many were appended.</summary>
        private int Merge(List<Ohlcv> sorted)
        {
            int appended = 0;
            foreach (var bar in sorted)
            {
                if (bar.Date > _bars[^1].Date)
                {
                    _bars.Add(bar);
                    appended++;
                    continue;
                }
                // Walk back from the end looking for the same bar. Bounded: the fetch is a few
                // bars wide, so anything further back than that is history we already hold.
                for (int i = _bars.Count - 1, steps = 0; i >= 0 && steps < sorted.Count + 2; i--, steps++)
                {
                    if (_bars[i].Date == bar.Date) { _bars[i] = bar; break; }
                    if (_bars[i].Date < bar.Date) break;
                }
            }
            return appended;
        }

        /// <summary>Keep the buffer bounded, and keep the scanner's indices meaning the same bars.</summary>
        private void Trim()
        {
            if (_bars.Count <= BarsNeeded * 2) return;
            int drop = _bars.Count - BarsNeeded;
            _bars.RemoveRange(0, drop);
            _scanner.ShiftIndices(drop);
        }

        private async Task<WorkspaceState> SeedAsync(CancellationToken ct)
        {
            var state = await ComputeStateAsync(ct).ConfigureAwait(false);
            foreach (var s in state.ActiveSeries)
                if (s.IsAutoNarrated) _scanner.Seed(s, state);
            return state;
        }

        /// <summary>
        /// Swap in this poll's component values as the memory for the next one, and return the
        /// memory that was there — what the evaluator compares THIS poll against. The same
        /// snapshot <c>BackgroundWorkspaceMonitor.SnapshotIndicatorValues</c> takes for the
        /// in-session background tabs, keyed the way <c>AlertEvaluator</c> reads it.
        /// </summary>
        private IReadOnlyDictionary<string, double> Snapshot(WorkspaceState state)
        {
            var previous = _lastValues;
            var next = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            int idx = state.CurrentDataIndex;
            foreach (var series in state.ActiveSeries)
            {
                if (string.IsNullOrEmpty(series.IndicatorCode)) continue;
                foreach (var comp in series.Components)
                {
                    var data = series.GetComponentData(comp.Name);
                    if (data == null || idx < 0 || idx >= data.Length) continue;
                    next[$"{series.IndicatorCode}.{comp.Name}"] = data[idx];
                }
            }
            _lastValues = next;
            return previous;
        }

        /// <summary>
        /// The state the scan and the evaluator need and nothing else: the held bars, every
        /// series recomputed against them, the cursor on the newest bar. Never dispatched
        /// anywhere. Same shape as <c>BackgroundWorkspaceMonitor.BuildState</c>, which does this
        /// for the in-session background tabs' alerts.
        /// </summary>
        private async Task<WorkspaceState> ComputeStateAsync(CancellationToken ct)
        {
            var computed = ImmutableList.CreateBuilder<ChartSeries>();
            foreach (var series in _template)
                computed.Add(await ComputeSeriesAsync(series, ct).ConfigureAwait(false));

            return WorkspaceState.Initial with
            {
                Identity = _identity,
                SymbolDisplayName = _identity.Symbol ?? "",
                Data = new TimeSeriesBuffer<Ohlcv>(_bars),
                ActiveSeries = computed.ToImmutable(),
                CurrentDataIndex = _bars.Count - 1,
                // The whole buffer is the "visible range" here: a visible-range profile (VPVR,
                // TPO) is the profile of the last BarsNeeded bars, which is what a chart showing
                // that many bars would draw.
                ViewportStartIndex = 0,
                ViewportLength = _bars.Count,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
            };
        }

        /// <summary>
        /// One series against the held bars, the way <c>IndicatorOrchestrator</c> computes it for
        /// the focused chart: core series map straight off the bars, indicators go through the
        /// engine and the production mapper (so companion arrays — touch counts, gradient
        /// colours — arrive too), dynamic zone bands land on the config as they do in-session,
        /// and a PROFILE gets its bins from the profile service over the whole buffer (a POC
        /// alert and the profile ladder both read them). Drawings and heatmaps have nothing
        /// either consumer reads and are left empty.
        /// </summary>
        private async Task<ChartSeries> ComputeSeriesAsync(ChartSeries series, CancellationToken ct)
        {
            try
            {
                if (series.IsDrawing
                    || series.Components.Any(c => c.DisplayType is ComponentDisplayType.Heatmap))
                    return series;

                if (series.IsProfile || ProfileAnchoring.IsProfileCode(series.IndicatorCode))
                    return ComputeProfile(series);

                if (series.Components.Any(c => !string.IsNullOrEmpty(c.DataMapping)))
                {
                    var mapped = _mapper.MapInternalDataToBuffer(series, _bars);
                    mapped.FirstBarDate = _bars[0].Date;
                    return series.WithData(mapped);
                }

                if (string.IsNullOrEmpty(series.IndicatorCode)) return series;

                var parameters = series.BuildParameterMap();
                // The same hidden hints the orchestrator stamps, for providers that route a
                // remote fetch per asset (funding rate, open interest).
                if (!string.IsNullOrEmpty(_identity.Symbol)) parameters["__symbol"] = _identity.Symbol;
                if (!string.IsNullOrEmpty(_identity.Provider)) parameters["__provider"] = _identity.Provider;
                if (!string.IsNullOrEmpty(_identity.Timeframe)) parameters["__timeframe"] = _identity.Timeframe;

                var (results, zoneBands) = await _engine
                    .CalculateWithBandsAsync(series.IndicatorCode, _bars, parameters, ct)
                    .ConfigureAwait(false);
                var buffer = _mapper.MapResultsToBuffer(series, results, _bars.Count);
                buffer.FirstBarDate = _bars[0].Date;

                if (zoneBands.Count > 0)
                {
                    series.Config.ZoneBands.Clear();
                    series.Config.ZoneBands.AddRange(zoneBands);
                }
                return series.WithData(buffer);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Headless chart: indicator {Code} failed for {Symbol}; it will say nothing this poll.",
                    series.IndicatorCode, _identity.Symbol);
                return series;
            }
        }

        /// <summary>
        /// The profile's bins, exactly as <c>IndicatorOrchestrator</c> computes them for an open
        /// chart — the anchoring rule picks the bars (a session, a fixed range, the "visible"
        /// range, which here is the whole buffer) and the profile service bins them.
        /// </summary>
        private ChartSeries ComputeProfile(ChartSeries series)
        {
            if (_profiles == null || _bars.Count == 0) return series;

            string codeUpper = series.IndicatorCode.ToUpperInvariant();
            var slice = ProfileAnchoring.Slice(codeUpper, _bars, series.Config?.Parameters, 0, _bars.Count);
            if (slice == null || slice.Count == 0) return series;

            var bins = ProfileAnchoring.CountsTime(codeUpper)
                ? _profiles.CalculateMarketProfile(slice)
                : _profiles.CalculateVolumeProfile(slice);

            var buffer = new SeriesDataBuffer { SeriesId = series.Id, ProfileBins = bins ?? new List<ProfileBin>() };
            buffer.FirstBarDate = _bars[0].Date;
            return series.WithData(buffer);
        }
    }
}
