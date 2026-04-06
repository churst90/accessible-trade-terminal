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

        public ObservableCollection<ComponentConfig> Components { get; set; } = new();
        public ObservableCollection<LevelConfig> Levels { get; set; } = new();
        /// <summary>Cloud fills between pairs of components. Visual-only — not navigable or audible.</summary>
        public List<CloudFillConfig> CloudFills { get; set; } = new();
        /// <summary>Horizontal zone bands centred on a carry-forward level value. Visual-only — not navigable or audible.</summary>
        public List<ZoneBandConfig> ZoneBands { get; set; } = new();

        public SeriesConfig Clone()
        {
            var c = new SeriesConfig
            {
                Id = Id, Name = Name, FriendlyName = FriendlyName, IndicatorCode = IndicatorCode,
                Pane = Pane, IsMuted = IsMuted, Volume = Volume, IsVisible = IsVisible,
                IsAutoNarrated = IsAutoNarrated,
                SpeakHeaderFirst = SpeakHeaderFirst, IncludeTimestamp = IncludeTimestamp
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
