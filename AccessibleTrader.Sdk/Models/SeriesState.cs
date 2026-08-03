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
                ComponentData = ComponentData.ToDictionary(k => k.Key, v => (double[])v.Value.Clone()),
                ProfileBins = ProfileBins.ToList(),
                HeatmapData = HeatmapData.Select(l => l.ToList()).ToList()
            };
        }
    }
}
