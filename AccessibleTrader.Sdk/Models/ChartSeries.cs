using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Specifies how a component is visually rendered on the chart.
    /// </summary>
    /// <summary>
    /// Where a marker component is drawn vertically.
    ///
    /// <para>
    /// Markers that mean "something happened at this bar" should anchor to the BAR, not to an
    /// absolute price the indicator computed. Indicators run on RAW OHLCV while the main pane may
    /// be drawing Heikin-Ashi candles, whose highs and lows differ — so a price-anchored marker
    /// drifts away from the candle it is describing the moment the user switches chart type. It
    /// also disagrees with the speech and sonification layers, which already follow the transform.
    /// </para>
    /// </summary>
    public enum MarkerAnchor
    {
        /// <summary>Draw at the component's own value, mapped through the price axis. The default,
        /// and correct for anything whose value IS a price (levels, moving averages).</summary>
        Value,
        /// <summary>Draw just under the displayed bar's low. For bullish / support markers.</summary>
        BelowBar,
        /// <summary>Draw just above the displayed bar's high. For bearish / resistance markers.</summary>
        AboveBar
    }

    public enum ComponentDisplayType
    {
        Line,
        Bar,
        Histogram,
        Oscillator,
        Wick,
        Candle,
        Area,
        Level,
        Profile,
        Heatmap,
        Distribution,
        /// <summary>Small filled dot/circle at each bar's value Y position. Size driven by Thickness.</summary>
        Dot,
        /// <summary>Up or down triangle arrow anchored at value Y. Direction: positive = up, negative = down.</summary>
        Arrow,
        /// <summary>Stepped line: horizontal segment at current value, then vertical drop to next value.</summary>
        StepLine,
        /// <summary>Filled region between two named components in the same series (cloud/ribbon).
        /// Set UpperComponentName and LowerComponentName on this ComponentConfig.</summary>
        Cloud,
        /// <summary>Line with a gradient fill from the line down to the pane bottom (area chart style).</summary>
        Gradient,
        /// <summary>Small filled dot/circle rendered at the zero-line Y position, colored by value sign
        /// (green = value &gt;= 0, red = value &lt; 0). Used for Money Flow in Cipher B.</summary>
        ZeroDot,
        /// <summary>Continuous wave line with zero-crossing area fill: green fill above zero, red below.
        /// Used for Money Flow Wave in Cipher B — visually distinct from bars and single-color areas.</summary>
        ZeroArea,
        /// <summary>Upward-pointing triangle anchored at value Y. Fixed direction — independent of value sign.
        /// Use for bullish continuation signals where the indicator pre-determines direction.</summary>
        TriangleUp,
        /// <summary>Downward-pointing triangle anchored at value Y. Fixed direction — independent of value sign.
        /// Use for bearish continuation signals where the indicator pre-determines direction.</summary>
        TriangleDown,
        /// <summary>Rotated 45° square (diamond shape) at value Y. Visually distinct from Dot; ideal for divergence markers.</summary>
        Diamond,
        /// <summary>Axis-aligned filled square at value Y. Useful for POC/profile markers and discrete event flags.</summary>
        Square,
        /// <summary>X-shaped cross marker at value Y. Useful for invalidation signals or point-of-interest flags.</summary>
        Cross,
        /// <summary>
        /// Small filled dot at the component's Y value, color-graded by a companion "_color" data array.
        /// The main component array holds the Y position (e.g. close price). A second array named
        /// ComponentName + "_color" holds the raw oscillator value used to interpolate the dot color
        /// along a teal (negative) → gray (zero) → red (positive) gradient.
        /// Used for the WT Momentum ribbon on Cipher A — continuous color heat-map on every candle.
        /// </summary>
        GradientDot,
        /// <summary>
        /// Sentinel type for per-bar candle color overrides. The component data array holds a phase
        /// index (0–10) for each bar. The renderer maps this index to a sentiment phase color and
        /// uses it to paint the candle body, suppressing the default bullish/bearish coloring and
        /// hiding wicks. When no active series carries a CandleColor component, candles render
        /// normally. Adding or removing the indicator instantly reverts candle appearance.
        /// Only meaningful when hosted on a series with DefaultPane = "Main".
        /// </summary>
        CandleColor,
    }
    
    /// <summary>
    /// Specifies the dash pattern for a component's visual line.
    /// </summary>
    public enum DashStyle { Solid, Dash, Dot, DashDot }
    
    /// <summary>
    /// Defines the semantic meaning of the component within the context of an indicator or price action.
    /// </summary>
    public enum ComponentRole { None, PriceAction, Body, Wick, Median, UpperBand, LowerBand, Level, Signal, Histogram, Volume, Boundary }
    
    /// <summary>
    /// Determines how the amplitude (volume/intensity) of a sonified component is mapped to its data values.
    /// </summary>
    public enum AmplitudeMapping { None, Absolute, DeltaFromPrice, Size, ReferenceDeviation }
    
    /// <summary>
    /// Determines how the pitch (frequency) of a sonified component is mapped to its data values.
    /// </summary>
    /// <summary>Price maps the component's absolute price position within the viewport to pitch (200–1000 Hz).</summary>
    public enum PitchMapping { None, Value, Direction, PriceDirection, Price }
    
    /// <summary>
    /// Determines the source logic used to select the component's color.
    /// </summary>
    public enum ColorSource { Value, PriceAction }

    /// <summary>
    /// Represents the different types of chart drawings available.
    /// </summary>
    public enum DrawingType
    {
        None,
        HorizontalLine,
        VerticalLine,
        TrendLine,
        Channel,
        FibRetracement,
        FibExtension,
        Rectangle,
        TextLabel,
        GannFan,
        RiskReward,
        AnchoredVwap,
        MeasureTool,
        GannBox,
        AndrewsPitchfork,
        AngleFib
    }

    /// <summary>
    /// Represents a full data series on the chart.
    /// BRIDGING CLASS: Connects user configuration (SeriesConfig) with calculator data (SeriesDataBuffer).
    /// </summary>
    public partial class ChartSeries : ObservableObject
    {
        // ── Config (SSOT for UI) ───────────────────────────────────────────
        public string Id => Config.Id;
        public string Name => Config.Name;
        public string FriendlyName => Config.FriendlyName;
        public string IndicatorCode => Config.IndicatorCode;
        public string Pane => Config.Pane;
        public Dictionary<string, double> Parameters => Config.Parameters;
        
        public bool IsMuted { get => Config.IsMuted; set => Config.IsMuted = value; }
        public float Volume { get => Config.Volume; set => Config.Volume = value; }
        public bool IsVisible { get => Config.IsVisible; set => Config.IsVisible = value; }
        public bool IsAutoNarrated { get => Config.IsAutoNarrated; set => Config.IsAutoNarrated = value; }

        /// <summary>
        /// Whether this series' marker signals are spoken while focus is on a different series.
        /// See <see cref="SeriesConfig.AnnounceAcrossSeries"/> for what the distinction is for.
        /// </summary>
        public bool AnnounceAcrossSeries { get => Config.AnnounceAcrossSeries; set => Config.AnnounceAcrossSeries = value; }
        
        public ObservableCollection<ComponentConfig> Components => Config.Components;
        public ObservableCollection<LevelConfig> Levels => Config.Levels;
        public System.Collections.Generic.List<CloudFillConfig> CloudFills => Config.CloudFills;
        public System.Collections.Generic.List<ZoneBandConfig> ZoneBands => Config.ZoneBands;

        // ── Data (SSOT for Orchestrators) ──────────────────────────────────
        [JsonIgnore] public List<ProfileBin> ProfileBins { get => Data.ProfileBins; set => Data.ProfileBins = value; }
        [JsonIgnore] public List<List<ProfileBin>> HeatmapData { get => Data.HeatmapData; set => Data.HeatmapData = value; }

        public SeriesConfig Config { get; init; }
        public SeriesDataBuffer Data { get; init; }

        // ── Logic ──────────────────────────────────────────────────────────
        public bool IsDrawing => Drawing != null && Drawing.Type != DrawingType.None;
        [ObservableProperty] private DrawingData? _drawing;
        [ObservableProperty] private bool _isProfile;
        [ObservableProperty] private int _focusedBinIndex = -1;
        public bool RequiresFullRecalcOnTick { get; set; } = false;

        public ChartSeries(SeriesConfig config, SeriesDataBuffer data)
        {
            Config = config;
            Data = data;
        }

        public ChartSeries() 
        {
            Config = new SeriesConfig();
            Data = new SeriesDataBuffer { SeriesId = Config.Id };
        }

        public ChartSeries Clone()
        {
            return new ChartSeries(Config.Clone(), Data.Clone())
            {
                Drawing = Drawing?.Clone(),
                IsProfile = IsProfile,
                FocusedBinIndex = FocusedBinIndex,
                RequiresFullRecalcOnTick = RequiresFullRecalcOnTick
            };
        }

        /// <summary>
        /// Creates a new series instance using the CURRENT configuration but NEW data.
        /// This is the primary method for Orchestrators to update results without clobbering UI state.
        ///
        /// EVERY non-Config field must be carried across. This method runs on the very first
        /// calculation of a series (UpdateSeriesDataAction → SeriesReducer), so anything dropped
        /// here is dropped permanently — the store never sees the factory-built instance again.
        /// <see cref="RequiresFullRecalcOnTick"/> used to be omitted, which silently downgraded
        /// the pivot-based indicators (Market Structure, Value Deviation) to the scalar
        /// incremental path forever: their historical bars kept whatever was computed the first
        /// time, so a symbol change left the PREVIOUS asset's swings on the chart until the user
        /// removed and re-added the indicator.
        /// </summary>
        public ChartSeries WithData(SeriesDataBuffer newData)
        {
            return new ChartSeries(Config, newData)
            {
                Drawing = Drawing,
                IsProfile = IsProfile,
                FocusedBinIndex = FocusedBinIndex,
                RequiresFullRecalcOnTick = RequiresFullRecalcOnTick
            };
        }

        /// <summary>
        /// Safely retrieves data for a specific component.
        /// </summary>
        public double[] GetComponentData(string componentName)
        {
            if (Data.ComponentData.TryGetValue(componentName, out var arr)) return arr;
            return Array.Empty<double>();
        }
    }

    /// <summary>
    /// Holds the data required to render a user-defined chart drawing.
    /// </summary>
    public partial class DrawingData : ObservableObject
    {
        [ObservableProperty] private DrawingType _type;
        [ObservableProperty] private DateTime? _anchorDate1;
        [ObservableProperty] private double? _anchorPrice1;
        [ObservableProperty] private DateTime? _anchorDate2;
        [ObservableProperty] private double? _anchorPrice2;
        [ObservableProperty] private DateTime? _anchorDate3;
        [ObservableProperty] private double? _anchorPrice3;
        [ObservableProperty] private string _text = string.Empty;
        [ObservableProperty] private double _channelWidth;
        [ObservableProperty] private bool _isLocked;
        [ObservableProperty] private bool _extendLeft;
        [ObservableProperty] private bool _extendRight;

        // Tool-specific metadata
        [ObservableProperty] private double _stopLoss;
        [ObservableProperty] private double _takeProfit;
        [ObservableProperty] private double _riskRewardRatio;
        [ObservableProperty] private string _measureResult = string.Empty;

        public DrawingData Clone()
        {
            return new DrawingData {
                Type = Type, AnchorDate1 = AnchorDate1, AnchorPrice1 = AnchorPrice1,
                AnchorDate2 = AnchorDate2, AnchorPrice2 = AnchorPrice2,
                AnchorDate3 = AnchorDate3, AnchorPrice3 = AnchorPrice3,
                Text = Text, ChannelWidth = ChannelWidth, IsLocked = IsLocked,
                ExtendLeft = ExtendLeft, ExtendRight = ExtendRight,
                StopLoss = StopLoss, TakeProfit = TakeProfit, 
                RiskRewardRatio = RiskRewardRatio, MeasureResult = MeasureResult
            };
        }
    }

    /// <summary>
    /// Represents a single bin in a volume or time profile distribution.
    /// </summary>
    public record ProfileBin
    {
        public required double PriceLow { get; init; }
        public required double PriceHigh { get; init; }
        public double PriceMid => (PriceLow + PriceHigh) / 2.0;
        public double Price => PriceMid;
        public required double TotalVolume { get; init; }
        public double Density => TotalVolume;
        public required double TpoPeriodCount { get; init; }
        public required bool IsPOC { get; init; }
        public required bool IsValueArea { get; init; }
        public char? TpoLetter { get; init; }
        public int TpoBinIndex { get; init; }
        public bool IsSinglePrint { get; init; }
        public System.Collections.Immutable.ImmutableList<char> TpoLetters { get; init; }
            = System.Collections.Immutable.ImmutableList<char>.Empty;
    }
}
