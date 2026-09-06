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

    /// <summary>
    /// What a reference line MEANS on its pane, as opposed to what it is called.
    ///
    /// <para>
    /// This exists for the same reason <see cref="LevelCrossDirection"/> does, and it is the same
    /// bug one layer up. Sixteen providers declare the line a bounded oscillator swings about, and
    /// they spell it four different ways — <c>Zero</c> (7), <c>Midpoint</c> (5), <c>Neutral</c> (3),
    /// <c>Midline</c> (1). <c>IndicatorCrossingEngine</c> matched the literal string "Zero", so
    /// Ctrl+Left/Right could reach the midline of seven indicators and not of the other nine.
    /// RSI is the clearest case: it declares <c>Midpoint</c> at 50 with an earcon that fires, and
    /// the navigation could never jump to it.
    /// </para>
    ///
    /// <para>
    /// Roles are also what the <c>0</c> key needs. "Add a reference level" used to mean literal
    /// zero on any pane that was not the price pane — which put a line at the floor of every
    /// 0–100 oscillator, a value RSI never visits. A role says which line is the meaningful
    /// constant without anyone having to parse a name.
    /// </para>
    /// </summary>
    public enum LevelRole
    {
        /// <summary>
        /// Infer from the name, so that the ~350 provider level declarations and every workspace
        /// saved before this field existed keep their behaviour without being rewritten. See
        /// <see cref="LevelConfig.EffectiveRole"/> for the inference.
        /// </summary>
        Auto = 0,

        /// <summary>The line the value oscillates about: zero, the midpoint, 50.</summary>
        Neutral,

        /// <summary>An upper extreme — overbought.</summary>
        Overbought,

        /// <summary>A lower extreme — oversold.</summary>
        Oversold,

        /// <summary>
        /// A declared line with no navigational meaning — "Fear", "Trending", "Long Crowded".
        /// Explicit rather than absent so a provider can say "this is not a midline" and stop
        /// the name inference guessing.
        /// </summary>
        None,
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
        /// What this line means on its pane. See <see cref="LevelRole"/>; <c>Auto</c> infers from
        /// the name, which is what every existing declaration relies on.
        /// </summary>
        [ObservableProperty] private LevelRole _role = LevelRole.Auto;

        /// <summary>
        /// Resolves <see cref="LevelRole.Auto"/> against the level's name. The four spellings of
        /// the midline are collapsed here, in one place, rather than at each of the readers that
        /// used to sniff for one of them.
        /// </summary>
        public LevelRole EffectiveRole
        {
            get
            {
                if (Role != LevelRole.Auto) return Role;

                string n = Name ?? string.Empty;
                if (n.Contains("Overbought", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Extreme OB", StringComparison.OrdinalIgnoreCase))
                    return LevelRole.Overbought;
                if (n.Contains("Oversold", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Extreme OS", StringComparison.OrdinalIgnoreCase))
                    return LevelRole.Oversold;

                // The four spellings, and only as WHOLE names: "Zero Lag EMA" is not a midline,
                // and a Contains() check would have said it was.
                if (n.Equals("Zero", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Midpoint", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Midline", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Neutral", StringComparison.OrdinalIgnoreCase))
                    return LevelRole.Neutral;

                return LevelRole.None;
            }
        }

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
                Role            = Role,
            };
        }
    }
}
