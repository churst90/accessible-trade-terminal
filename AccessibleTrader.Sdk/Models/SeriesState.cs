using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Holds user-controlled configuration for a series.
    /// This is the Single Source of Truth for visual/audio preferences.
    /// </summary>
    public partial class SeriesConfig : ObservableObject
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        [ObservableProperty] private string _friendlyName = "";
        public string IndicatorCode { get; set; } = "";
        public string Pane { get; set; } = "Main";
        public Dictionary<string, double> Parameters { get; set; } = new();

        /// <summary>
        /// Parameter values that are not numbers. Providers declare these with
        /// <c>DataType = typeof(string)</c> — a comparison symbol, an MA type, a pivot
        /// period, a threshold mode — and receive them through the same
        /// <c>Dictionary&lt;string, object&gt;</c> that carries <see cref="Parameters"/>.
        ///
        /// <para>
        /// Kept as a second dictionary rather than widening <see cref="Parameters"/> to
        /// <c>object</c>: the numeric dictionary is read by name in a few hundred places and
        /// persisted in every saved workspace, and boxing all of it to move four indicators
        /// forward is the wrong trade. Before this existed, <c>IndicatorModelFactory</c> ran
        /// every value through <c>double.TryParse</c> and dropped whatever failed on the
        /// floor with no error — so <c>COMPARE</c> and <c>COMPARE_RATIO</c> rendered blank
        /// forever, Cipher B's Percentile threshold mode and its four feeding parameters were
        /// unreachable, MA Cloud's MA-type selector did nothing, and Pivot Levels ignored its
        /// period. What the UI offered, the provider could never receive.
        /// </para>
        /// </summary>
        public Dictionary<string, string> StringParameters { get; set; } = new();

        [ObservableProperty] private bool _isMuted;
        [ObservableProperty] private float _volume = 1.0f;
        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private bool _isAutoNarrated;
        [ObservableProperty] private bool _speakHeaderFirst = true;
        [ObservableProperty] private bool _includeTimestamp = false;

        /// <summary>
        /// Whether this series' marker signals are spoken while focus is on a DIFFERENT series.
        ///
        /// <para>
        /// The distinction the setting exists for: some indicators produce points of interest that
        /// are worth knowing about wherever you are on the chart — a support zone, a break of
        /// structure, a Cipher B divergence — while others produce output that only means something
        /// inside their own context. Reading the latter while the user is navigating an unrelated
        /// oscillator is noise, and noise is what makes people switch narration off entirely.
        /// </para>
        ///
        /// <para>
        /// Default true, because the common case is a sparse marker that a trader would want called
        /// out anywhere. Turn it off for a busy indicator whose signals are only meaningful when you
        /// are reading that indicator. Either way the series always speaks its own signals when it
        /// IS the focused one — this governs the cross-series case only.
        /// </para>
        /// </summary>
        [ObservableProperty] private bool _announceAcrossSeries = true;

        public ObservableCollection<ComponentConfig> Components { get; set; } = new();
        public ObservableCollection<LevelConfig> Levels { get; set; } = new();
        /// <summary>Cloud fills between pairs of components. Visual-only — not navigable or audible.</summary>
        public List<CloudFillConfig> CloudFills { get; set; } = new();
        /// <summary>Horizontal zone bands centred on a carry-forward level value. Visual-only — not navigable or audible.</summary>
        public List<ZoneBandConfig> ZoneBands { get; set; } = new();

        // ── Per-series pane range overrides (analytics provider hints) ────────────
        /// <summary>
        /// Hard lower bound for this series's pane auto-scale. When set, ViewportRangeCalculator
        /// clamps the pane's min to this value, so a bounded metric like FNG always shows 0–100
        /// even if current data is 10–90. Populated from SymbolRenderHints.RangeMin on analytics
        /// loads. Null = use data-driven auto-scale (default for OHLCV and unbounded metrics).
        /// </summary>
        public double? RangeMin { get; set; }

        /// <summary>
        /// Hard upper bound for this series's pane auto-scale. See <see cref="RangeMin"/>.
        /// </summary>
        public double? RangeMax { get; set; }

        /// <summary>
        /// For drawing series (trendlines, channels, fibs, labels): the anchor
        /// data. Runtime lives on <c>ChartSeries.Drawing</c>; this copy exists so
        /// workspace saves round-trip drawings — synced in at capture time and
        /// rehydrated onto the series on restore, after which the indicator
        /// orchestrator recomputes the component arrays against loaded data.
        /// </summary>
        public DrawingData? Drawing { get; set; }

        public SeriesConfig Clone()
        {
            var c = new SeriesConfig
            {
                Id = Id, Name = Name, FriendlyName = FriendlyName, IndicatorCode = IndicatorCode,
                Pane = Pane, IsMuted = IsMuted, Volume = Volume, IsVisible = IsVisible,
                IsAutoNarrated = IsAutoNarrated,
                SpeakHeaderFirst = SpeakHeaderFirst, IncludeTimestamp = IncludeTimestamp,
                AnnounceAcrossSeries = AnnounceAcrossSeries,
                RangeMin = RangeMin, RangeMax = RangeMax,
                Drawing = Drawing
            };
            foreach (var comp in Components) c.Components.Add(comp.Clone());
            foreach (var level in Levels) c.Levels.Add(level.Clone());
            foreach (var fill in CloudFills) c.CloudFills.Add(fill.Clone());
            foreach (var band in ZoneBands) c.ZoneBands.Add(band.Clone());
            foreach (var p in Parameters) c.Parameters[p.Key] = p.Value;
            return c;
        }    }

    /// <summary>
    /// Holds calculator-generated data for a series.
    /// Decoupled from configuration to prevent UI-state clobbering during async updates.
    /// </summary>
    public class SeriesDataBuffer
    {
        public string SeriesId { get; init; } = "";

        /// <summary>
        /// The date of the bar these arrays start at — index 0 — or <c>null</c> when the buffer
        /// was built somewhere that does not know (an empty buffer from
        /// <c>SeriesManagementService</c>, a restored workspace, the backtester).
        ///
        /// <para>
        /// It exists because array LENGTHS cannot tell an append from a prepend. Three values
        /// against six bars is the same arithmetic whether the three new bars arrived at the end
        /// (a live tick, where the old values are still on their own bars) or at the front (a
        /// scrollback fetch, where every old value has moved right by three). The incremental
        /// update path grew the array with <c>Array.Copy</c> either way, so a scrollback left the
        /// previous history's values smeared onto the wrong bars, NaN across the middle, and one
        /// fresh value at the right edge — which is what a user sees as "only the latest bar on
        /// the indicators is populated".
        /// </para>
        /// </summary>
        public DateTime? FirstBarDate { get; set; }

        // Component data arrays (parallel to SeriesConfig.Components)
        public Dictionary<string, double[]> ComponentData { get; set; } = new();
        
        // Specialized distribution data
        public List<ProfileBin> ProfileBins { get; set; } = new();
        public List<List<ProfileBin>> HeatmapData { get; set; } = new();

        public SeriesDataBuffer Clone()
        {
            return new SeriesDataBuffer
            {
                SeriesId = SeriesId,
                FirstBarDate = FirstBarDate,
                ComponentData = ComponentData.ToDictionary(k => k.Key, v => (double[])v.Value.Clone()),
                ProfileBins = ProfileBins.ToList(),
                HeatmapData = HeatmapData.Select(l => l.ToList()).ToList()
            };
        }
    }
}
