using CommunityToolkit.Mvvm.ComponentModel;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Which side of a reference level counts as "beyond" it, and therefore which crossings the
    /// audio layer reports.
    ///
    /// <para>
    /// This exists because the crossing monitor used to decide by <b>sniffing the level's name</b>
    /// for "Overbought" / "Oversold", and silently ignored every level whose name did not match. A
    /// user who added their own level and ticked "Play Earcon on Crossing" got nothing, with no way
    /// to find out why — the checkbox was live, the code path was not.
    /// </para>
    /// </summary>
    public enum LevelCrossDirection
    {
        /// <summary>
        /// Infer from the level's name, preserving the historical behaviour for provider-declared
        /// levels: "Overbought"/"Extreme OB" watch above, "Oversold"/"Extreme OS" watch below, and
        /// anything else watches <b>both</b> sides. This is the default so that workspaces saved
        /// before the field existed keep working, and so that a provider adding a level need not
        /// think about it.
        /// </summary>
        Auto = 0,

        /// <summary>Beyond means above the level — an overbought line.</summary>
        Above,

        /// <summary>Beyond means below the level — an oversold line.</summary>
        Below,

        /// <summary>
        /// Either crossing counts. The right default for a level a person placed by hand: they care
        /// that price reached their line, not which way it was travelling.
        /// </summary>
        Both,
    }

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

        /// <summary>
        /// Which crossings of this level the audio layer reports. See <see cref="LevelCrossDirection"/>
        /// — <c>Auto</c> reproduces the old name-based behaviour and is the safe default for
        /// deserialised workspaces.
        /// </summary>
        [ObservableProperty] private LevelCrossDirection _crossDirection = LevelCrossDirection.Auto;

        /// <summary>
        /// True when a person added this level rather than the indicator declaring it. Purely
        /// informational — it drives wording ("your level at …") and lets the Properties dialog show
        /// user levels as removable while provider defaults are restored rather than deleted.
        /// </summary>
        [ObservableProperty] private bool _isUserDefined;

        /// <summary>
        /// Resolves <see cref="LevelCrossDirection.Auto"/> against the level's name.
        ///
        /// <para>
        /// The crucial line is the last one. The old code returned "no direction" here and the
        /// caller skipped the level entirely; returning <see cref="LevelCrossDirection.Both"/> is
        /// what makes a hand-placed level audible at all.
        /// </para>
        /// </summary>
        public LevelCrossDirection EffectiveCrossDirection
        {
            get
            {
                if (CrossDirection != LevelCrossDirection.Auto) return CrossDirection;

                string n = Name ?? string.Empty;
                if (n.Contains("Overbought", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Extreme OB", StringComparison.OrdinalIgnoreCase))
                    return LevelCrossDirection.Above;
                if (n.Contains("Oversold", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Extreme OS", StringComparison.OrdinalIgnoreCase))
                    return LevelCrossDirection.Below;

                return LevelCrossDirection.Both;
            }
        }

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
                CrossDirection  = CrossDirection,
                IsUserDefined   = IsUserDefined,
            };
        }
    }
}
