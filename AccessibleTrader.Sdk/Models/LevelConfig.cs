using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccessibleTrader.Sdk.Models
{
    public partial class LevelConfig : ObservableObject
    {
        public string Name { get; set; } = "";
        [ObservableProperty] private double _value;
        [ObservableProperty] private string _colorHex = "#888888";
        [ObservableProperty] private float _thickness = 1.0f;
        [ObservableProperty] private DashStyle _dashStyle = DashStyle.Dash;
        [ObservableProperty] private bool _isVisible = true;

        // ── Per-level audio behavior ─────────────────────────────────────────
        /// <summary>When true, crossing this level fires an earcon click.</summary>
        [ObservableProperty] private bool _playEarcon = false;
        /// <summary>Volume of the earcon click [0,1].</summary>
        [ObservableProperty] private float _earconVolume = 0.7f;
        /// <summary>Additive noise amount applied while value is in the zone beyond this level [0,1].</summary>
        [ObservableProperty] private float _zoneNoiseAmount = 0f;
        /// <summary>Noise colour: "white", "pink", or "brown".</summary>
        [ObservableProperty] private string _zoneNoiseType = "pink";

        public LevelConfig Clone()
        {
            return new LevelConfig
            {
                Name            = Name,
                Value           = Value,
                ColorHex        = ColorHex,
                Thickness       = Thickness,
                DashStyle       = DashStyle,
                IsVisible       = IsVisible,
                PlayEarcon      = PlayEarcon,
                EarconVolume    = EarconVolume,
                ZoneNoiseAmount = ZoneNoiseAmount,
                ZoneNoiseType   = ZoneNoiseType,
            };
        }
    }
}
