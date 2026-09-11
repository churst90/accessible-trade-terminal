using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>What one poll's bars produced. See <see cref="HeadlessChartNarrator.ObserveAsync"/>.</summary>
    /// <param name="Narration">The composed ladder for the bar that closed, or null when nothing
    /// closed, nothing was found, or this was the first sighting.</param>
    /// <param name="NeedsMoreHistory">The bars handed in do not reach back to the last bar this
    /// narrator holds — a poll was missed for longer than the catch-up window — and the caller
    /// should fetch <see cref="HeadlessChartNarrator.BarsNeeded"/> bars and observe again.</param>
    /// <param name="BarClosed">Whether the newest bar advanced since the last observation.</param>
    public sealed record HeadlessObservation(string? Narration, bool NeedsMoreHistory, bool BarClosed);

    /// <summary>
    /// <b>The narration ladder for ONE saved chart, with the browser closed.</b>
    ///
    /// <para>
    /// ── What it is ─────────────────────────────────────────────────────────────
    /// Cody, 2026-09-11: <i>"I also want the narration ladder to also be spoken when the
    /// browser is closed too."</i> In-session the ladder is <c>AutoNarrationService</c>: bound to
    /// the focused chart's store, scanning after every redraw. With no browser there is no store,
    /// no redraw and no focused chart — there is the last autosaved session, which says which
    /// charts were open and which of their series carry the N flag, and a background monitor that
    /// re-fetches each chart once a minute. This class is the piece between the two: it holds the
    /// saved tab's NARRATED series, rebuilt exactly as a workspace load rebuilds them
    /// (<c>SeriesManagementService.MaterializeSaved</c> — derived name, component selection,
    /// levels, the lot), keeps enough bars to compute them, recomputes them through the
    /// store-free <see cref="IIndicatorEngine"/> on every poll, and runs the very same
    /// <see cref="NarrationScanner"/> over the result. Design (B) of
    /// docs/BACKGROUND_MONITOR_PHASE3_SCOPE.md §2: compose the state the scan needs, per chart,
    /// without the store.
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
    /// Whether the ladder is SPOKEN. The monitor owns that: the master narration switch, the
    /// per-chart ownership rule (a browser that has this chart open is already narrating it),
    /// and the composition with the bar-close sentence. This class observes and composes. It
    /// observes even while a browser covers the chart, for the same reason the monitor tracks
    /// covered charts' timestamps: the moment the browser closes, the seed is already warm.
    /// </para>
    /// </summary>
    public sealed class HeadlessChartNarrator
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
        private readonly ILogger? _logger;
        private readonly List<ChartSeries> _template;
        private readonly List<Ohlcv> _bars = new();
        private NarrationScanner _scanner;

        /// <summary>The narrated series this chart carries, as rebuilt from the saved tab.</summary>
        public IReadOnlyList<ChartSeries> NarratedSeries => _template;

        /// <summary>Whether there is anything to narrate at all — a saved tab with nothing under
        /// N costs no fetch and no computation.</summary>
        public bool HasNarratedSeries => _template.Count > 0;

        /// <summary>How many bars the first fetch (and a catch-up after a long gap) should ask for.</summary>
        public int BarsNeeded { get; }

        /// <summary>How many bars are held right now. Zero until the first observation.</summary>
        public int BufferedBars => _bars.Count;

        /// <summary>What the NEXT fetch should ask for: the full window on a cold buffer, the
        /// catch-up window once it is warm.</summary>
        public int FetchLimit => _bars.Count == 0 ? BarsNeeded : CatchUpLimit;

        internal HeadlessChartNarrator(
            ChartIdentity identity,
            IReadOnlyList<ChartSeries> narratedTemplate,
            IIndicatorEngine engine,
            IIndicatorStateMapper mapper,
            IIndicatorContextAnalyzer analyzer,
            ILogger? logger)
        {
            _identity = identity;
            _engine = engine;
            _mapper = mapper;
            _analyzer = analyzer;
            _logger = logger;
            _template = narratedTemplate.ToList();
            _scanner = new NarrationScanner(analyzer);
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
        /// call, or null. The first call SEEDS and says nothing — the alternative is announcing,
        /// on startup, every signal in the look-back window as if it were news.
        /// </summary>
        /// <param name="fetched">Oldest first. The newest bar is the FORMING one, as every
        /// provider returns it and as the in-session store holds it.</param>
        /// <param name="isFullHistory">True when <paramref name="fetched"/> was asked for with
        /// <see cref="BarsNeeded"/>: a gap in front of it cannot be closed by fetching more, so the
        /// buffer is replaced and re-seeded rather than asking again.</param>
        public async Task<HeadlessObservation> ObserveAsync(IReadOnlyList<Ohlcv> fetched, bool isFullHistory, CancellationToken ct)
        {
            if (fetched == null || fetched.Count == 0 || !HasNarratedSeries)
                return new HeadlessObservation(null, false, false);

            var sorted = fetched.OrderBy(b => b.Date).ToList();

            if (_bars.Count == 0)
            {
                _bars.AddRange(sorted);
                await SeedAsync(ct).ConfigureAwait(false);
                return new HeadlessObservation(null, false, false);
            }

            // No overlap between what we hold and what arrived: a poll was missed for longer than
            // the catch-up window covers. Ask for the full window once; if even that does not
            // reach back, start over — the newest bars are the only ones still true, and the
            // scanner's memory of a chart it has not seen for an hour is worth less than a seed.
            if (sorted[0].Date > _bars[^1].Date)
            {
                if (!isFullHistory) return new HeadlessObservation(null, NeedsMoreHistory: true, false);

                _logger?.LogInformation("Headless narration for {Symbol} {Timeframe} re-seeded after a gap.",
                    _identity.Symbol, _identity.Timeframe);
                _bars.Clear();
                _bars.AddRange(sorted);
                _scanner = new NarrationScanner(_analyzer);
                await SeedAsync(ct).ConfigureAwait(false);
                return new HeadlessObservation(null, false, false);
            }

            int appended = Merge(sorted);
            if (appended == 0)
                return new HeadlessObservation(null, false, false);   // the forming bar refreshed; no close

            Trim();

            var state = await ComputeStateAsync(ct).ConfigureAwait(false);
            // The bar that CLOSED is the one before the newest (forming) bar. After a missed poll
            // several bars closed at once; the newest closed one is the only one still news, and
            // the scanner's marker window covers the ones before it.
            int closedBound = _bars.Count - 2;
            string? text = _scanner.ScanAll(state.ActiveSeries, state, closedBound, isBarClose: true);
            return new HeadlessObservation(text, false, true);
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

        private async Task SeedAsync(CancellationToken ct)
        {
            var state = await ComputeStateAsync(ct).ConfigureAwait(false);
            foreach (var s in state.ActiveSeries) _scanner.Seed(s, state);
        }

        /// <summary>
        /// The state the scan needs and nothing else: the held bars, the narrated series
        /// recomputed against them, the cursor on the newest bar. Never dispatched anywhere.
        /// Same shape as <c>BackgroundWorkspaceMonitor.BuildState</c>, which does this for the
        /// in-session background tabs' alerts.
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
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
            };
        }

        /// <summary>
        /// One series against the held bars, the way <c>IndicatorOrchestrator</c> computes it for
        /// the focused chart: core series map straight off the bars, indicators go through the
        /// engine and the production mapper (so companion arrays — touch counts, gradient
        /// colours — arrive too), and dynamic zone bands land on the config as they do in-session.
        /// Drawings, profiles and heatmaps have nothing the scanner reads and are left empty.
        /// </summary>
        private async Task<ChartSeries> ComputeSeriesAsync(ChartSeries series, CancellationToken ct)
        {
            try
            {
                if (series.IsDrawing || series.IsProfile
                    || series.Components.Any(c => c.DisplayType is ComponentDisplayType.Heatmap
                                                 or ComponentDisplayType.Profile or ComponentDisplayType.Distribution))
                    return series;

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
                _logger?.LogWarning(ex, "Headless narration: indicator {Code} failed for {Symbol}; it will say nothing this poll.",
                    series.IndicatorCode, _identity.Symbol);
                return series;
            }
        }
    }
}
