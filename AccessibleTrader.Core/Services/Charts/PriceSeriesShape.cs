using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// The main price series takes the shape of the chart type: three candle parts (upper wick,
    /// body, lower wick) on Candles and Heikin Ashi, one close line on Line, one filled close
    /// line on Area, one brick part on Renko, one block part on line break, one Kagi line on Kagi, one column on point and figure. Range bars are real
    /// candles and keep the three candle parts.
    ///
    /// <para>
    /// Cody, 2026-11-19, after trying Line and Renko: "I still hear 3 components like the candles
    /// are still there ... if a line is drawn then the candles series should be replaced with a
    /// line series and same for the bricks". The first version left the candle parts in place and
    /// sent the arrows to the separate Price indicator instead — which his charts do not have, so
    /// he walked a line chart's wicks, and on Renko three parts that each said "Up brick". The
    /// series' parts ARE what everything else meets (drawing, sound, speech, the arrows, the
    /// part list, Properties), so the parts are what change.
    /// </para>
    ///
    /// <para>
    /// The candle parts are kept in <see cref="SeriesConfig.CandleParts"/> while the series is a
    /// line or bricks, and come back with their settings when Candles or Heikin Ashi is chosen.
    /// The one part is made from the body: its colours, volume and mute go with it, and come back
    /// to the body. A saved workspace stores the candle shape (<see cref="Canonical"/>), so the
    /// background monitor, alerts, templates and older builds reading a save meet the series they
    /// always have; the store re-applies the shape after every change (<see cref="Apply(WorkspaceState)"/>).
    /// </para>
    /// </summary>
    public static class PriceSeriesShape
    {
        /// <summary>The one part's name on Line and Area. "line", like the Price series' part, so
        /// every lookup that maps a part name to a bar field already reads it as the close.</summary>
        public const string LinePart = "line";

        /// <summary>The one part's name on a brick chart.</summary>
        public const string BrickPart = "brick";

        /// <summary>The one part's name on a line break chart. "Block", not "line", so a line break
        /// chart is never mistaken for the Line chart by ear.</summary>
        public const string BlockPart = "block";

        /// <summary>The one part's name on a Kagi chart, said "Kagi line": never plain "line",
        /// which is the Line chart's.</summary>
        public const string KagiPart = "kagi";

        /// <summary>The one part's name on a point and figure chart, said "Column".</summary>
        public const string PnfPart = "pnf";

        /// <summary>The one part's name on a Bars (OHLC) chart, said "Bar": one stop that holds
        /// the four prices, as the bar draws them.</summary>
        public const string BarPart = "ohlc";

        /// <summary>The one part's name on a Baseline chart: the close, said "Close" as on Line,
        /// but never "line", so a lookup that means the Line chart's part is never fooled.</summary>
        public const string BaselinePart = "baseline";

        /// <summary>The one part's name on a High-low chart, said "High-low bar". Its value is
        /// the bar's middle, (high + low) / 2, where it is drawn and heard; no open or close.</summary>
        public const string HighLowPart = "highlow";

        /// <summary>The three parts of an HLC area chart, top to bottom: the high line, the
        /// close line, the low line.</summary>
        public const string HlcHighPart = "hlc_high";
        public const string HlcClosePart = "hlc_close";
        public const string HlcLowPart = "hlc_low";

        /// <summary>True on the chart types that draw a line through the closes (Line, Area,
        /// Baseline, HLC area): the separate Price series' close line only repeats it there.</summary>
        public static bool DrawsCloseLine(PriceChartStyle style) =>
            style is PriceChartStyle.Line or PriceChartStyle.Area or PriceChartStyle.Baseline or PriceChartStyle.HlcArea;

        /// <summary>True for the price series' one part on a chart that has one: the line, brick,
        /// block, Kagi line, column, bar, baseline close or high-low bar.</summary>
        public static bool IsOnePartName(string? name) => name is LinePart or BrickPart or BlockPart
            or KagiPart or PnfPart or BarPart or BaselinePart or HighLowPart;

        /// <summary>
        /// The price series' name on each chart type: the Chart type list's own words. Cody,
        /// 2026-11-19: "when i change to renko and use pg up/down and land on that series, it is
        /// still called candles even though they are renko bricks ... we just need to change the
        /// name of the main series to whatever it is that series is."
        /// </summary>
        public static string NameFor(PriceChartStyle style, bool heikinAshi) => style switch
        {
            PriceChartStyle.Line => "Line",
            PriceChartStyle.Area => "Area",
            PriceChartStyle.Renko => "Renko",
            PriceChartStyle.RangeBars => "Range bars",
            PriceChartStyle.LineBreak => "Line break",
            PriceChartStyle.Kagi => "Kagi",
            PriceChartStyle.PointAndFigure => "Point and figure",
            PriceChartStyle.Bars => "Bars",
            PriceChartStyle.HollowCandles => "Hollow candles",
            PriceChartStyle.Baseline => "Baseline",
            PriceChartStyle.HighLow => "High-low",
            PriceChartStyle.HlcArea => "HLC area",
            PriceChartStyle.VolumeCandles => "Volume candles",
            _ => heikinAshi ? "Heikin Ashi" : "Candles",
        };

        /// <summary>The names this class gives the price series. A name the user gave it is
        /// none of these, and is never touched.</summary>
        private static readonly HashSet<string> ChartTypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "", "Candles", "Heikin Ashi", "Line", "Area", "Renko", "Range bars", "Line break", "Kagi",
            "Point and figure", "Bars", "Hollow candles", "Baseline", "High-low", "HLC area", "Volume candles",
        };

        /// <summary>What the price series' parts are.</summary>
        /// <remarks>Hollow candles and Volume candles are <see cref="Kind.Candles"/>: the same three
        /// parts, told apart by the hidden values the chart type adds (<see cref="HollowGreenKey"/>,
        /// <see cref="VolumeKey"/>).</remarks>
        public enum Kind { Candles, Line, Area, Brick, Block, Kagi, Column, Bar, Baseline, HighLow, HlcArea }

        /// <summary>The shape a chart type draws the price series in.</summary>
        public static Kind For(PriceChartStyle style) => style switch
        {
            PriceChartStyle.Line => Kind.Line,
            PriceChartStyle.Area => Kind.Area,
            PriceChartStyle.Renko => Kind.Brick,
            PriceChartStyle.LineBreak => Kind.Block,
            PriceChartStyle.Kagi => Kind.Kagi,
            PriceChartStyle.PointAndFigure => Kind.Column,
            PriceChartStyle.Bars => Kind.Bar,
            PriceChartStyle.Baseline => Kind.Baseline,
            PriceChartStyle.HighLow => Kind.HighLow,
            PriceChartStyle.HlcArea => Kind.HlcArea,
            _ => Kind.Candles,
        };

        /// <summary>The shape <paramref name="config"/>'s parts are in now.</summary>
        public static Kind Of(SeriesConfig config)
        {
            if (config.Components.Count == 3 && config.Components.Any(c => c.Name == HlcClosePart)) return Kind.HlcArea;
            if (config.Components.Count != 1) return Kind.Candles;
            var only = config.Components[0];
            if (only.Name == BarPart) return Kind.Bar;
            if (only.Name == BaselinePart) return Kind.Baseline;
            if (only.Name == HighLowPart) return Kind.HighLow;
            if (only.Name == BrickPart) return Kind.Brick;
            if (only.Name == BlockPart) return Kind.Block;
            if (only.Name == KagiPart) return Kind.Kagi;
            if (only.Name == PnfPart) return Kind.Column;
            if (only.Name == LinePart)
                return only.DisplayType == ComponentDisplayType.Area ? Kind.Area : Kind.Line;
            return Kind.Candles;
        }

        /// <summary>True for the main price series.</summary>
        public static bool IsPriceSeries(SeriesConfig config) => config.Id == CoreSeriesIds.Candles;

        /// <summary>
        /// Every series in <paramref name="state"/> in the shape its chart type draws. The same
        /// state when nothing changes, so the store's "did anything change" test still holds.
        /// The focused part stays on the price: the one part on a line or bricks, the body back
        /// on candles.
        /// </summary>
        public static WorkspaceState Apply(WorkspaceState state)
        {
            int at = state.ActiveSeries.FindIndex(s => s.Id == CoreSeriesIds.Candles);
            if (at < 0) return state;
            var series = state.ActiveSeries[at];
            var reshaped = WithPnfBoxes(WithKagiThickness(Apply(series, state.ChartStyle, state.IsHeikinAshi, state.Data), state), state);
            reshaped = WithStyleValues(reshaped, state);
            if (ReferenceEquals(reshaped, series)) return state;

            var next = state with { ActiveSeries = state.ActiveSeries.SetItem(at, reshaped) };
            // Back to the body only when the SHAPE changed. The hidden values (hollow colours,
            // volumes, ranges, Kagi thickness, column boxes) are refreshed on every tick, and
            // resetting then threw focus off a wick onto the body as the newest bar moved
            // (Cody, 2026-11-20: "I'll hear the sound of the body of the bar and focus will jump
            // to the body").
            if (state.FocusedSeriesId == CoreSeriesIds.Candles && !ReferenceEquals(reshaped.Config, series.Config))
                next = next with { FocusedComponentIndex = DefaultPart(reshaped.Config) };
            return next;
        }

        /// <summary>
        /// Each Kagi line's thickness at its end, 1 thick and 0 thin, in the price series' buffer
        /// under this key, so its sound can tell thick from thin as the drawing and the words do
        /// (Cody, 2026-11-19: "thickness audible"). Hidden: the "__" keeps it out of the parts.
        /// </summary>
        public const string KagiThickKey = "__kagi_thick";

        /// <summary>One thickness array per drawn-bar list, so the reduces that do not change the
        /// bars (every arrow key) cost a lookup, and the series is replaced only when they do.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<Ohlcv>, double[]> KagiThickCache = new();

        /// <summary>The thickness of every line in <paramref name="lines"/>, as <see cref="KagiThickKey"/> holds it.</summary>
        public static double[] KagiThickness(IReadOnlyList<Ohlcv> lines) =>
            KagiThickCache.GetValue(lines, l => KagiLines.All(l).Select(look => look.ThickAtEnd ? 1.0 : 0.0).ToArray());

        /// <summary>The price series with the Kagi lines' thickness in its buffer when the chart
        /// is Kagi; the same instance when it is not, or already holds this list's thickness.</summary>
        private static ChartSeries WithKagiThickness(ChartSeries series, WorkspaceState state)
        {
            if (state.ChartStyle != PriceChartStyle.Kagi || state.Data == null || Of(series.Config) != Kind.Kagi)
                return series;
            return WithHidden(series, KagiThickKey, KagiThickness(state.Data));
        }

        /// <summary>
        /// Each point and figure column's number of boxes, in the price series' buffer under this
        /// key, so the parts that see only the series (its sound, its part's words, the status line
        /// and the drawing) know a column of 2 boxes from one of 12, as the chart shows it.
        /// Hidden, as <see cref="KagiThickKey"/> is.
        /// </summary>
        public const string PnfBoxesKey = "__pnf_boxes";

        private sealed record PnfBoxesHeld(double Box, double[] Boxes);

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<Ohlcv>, PnfBoxesHeld> PnfBoxesCache = new();

        /// <summary>The boxes in every column of <paramref name="columns"/> at <paramref name="box"/>,
        /// as <see cref="PnfBoxesKey"/> holds it: one array per column list and box.</summary>
        public static double[] PnfBoxes(IReadOnlyList<Ohlcv> columns, double box)
        {
            if (PnfBoxesCache.TryGetValue(columns, out var held) && held.Box.Equals(box)) return held.Boxes;
            var boxes = columns.Select(c => (double)PointFigureColumns.Boxes(c, box)).ToArray();
            PnfBoxesCache.AddOrUpdate(columns, new PnfBoxesHeld(box, boxes));
            return boxes;
        }

        private static ChartSeries WithPnfBoxes(ChartSeries series, WorkspaceState state)
        {
            if (state.ChartStyle != PriceChartStyle.PointAndFigure || state.Data == null
                || Of(series.Config) != Kind.Column)
                return series;
            return WithHidden(series, PnfBoxesKey, PnfBoxes(state.Data, state.BrickBoxSize));
        }

        /// <summary>
        /// Each bar's colour on a Hollow candles chart, 1 green and 0 red: TradingView's rule, the
        /// close against the PREVIOUS close (the first bar, which has none, against its own open).
        /// Whether the body is hollow is the close against its own open, which the bar already
        /// holds. Hidden, as <see cref="KagiThickKey"/> is; present only on Hollow candles, so the
        /// body's sound and words know the chart is hollow candles by finding it.
        /// </summary>
        public const string HollowGreenKey = "__hollow_green";

        /// <summary>Each bar's volume on a Volume candles chart, so the body's sound and words can
        /// weigh a bar against the bars around it as the drawing's width does. Present only there.</summary>
        public const string VolumeKey = "__volume";

        /// <summary>The Baseline chart's chosen base, one value: a price, or NaN for the first
        /// visible close (<see cref="BaselineBase"/>). Present only on Baseline.</summary>
        public const string BaselineKey = "__baseline";

        /// <summary>Each bar's range, high minus low, on a High-low chart, so the bar's weight can
        /// be heard against the tallest bar in view as it is drawn. Present only there.</summary>
        public const string RangeKey = "__hl_range";

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<Ohlcv>, double[]> HollowCache = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<Ohlcv>, double[]> RangeCache = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<Ohlcv>, double[]> VolumeCache = new();
        private static readonly Dictionary<double, double[]> BaselineCache = new();

        /// <summary>Every bar's colour in <paramref name="bars"/>, as <see cref="HollowGreenKey"/> holds it.</summary>
        public static double[] HollowGreen(IReadOnlyList<Ohlcv> bars) =>
            HollowCache.GetValue(bars, b =>
            {
                var green = new double[b.Count];
                for (int i = 0; i < b.Count; i++)
                    green[i] = (i == 0 ? b[i].Close >= b[i].Open : b[i].Close >= b[i - 1].Close) ? 1.0 : 0.0;
                return green;
            });

        /// <summary>Every bar's range in <paramref name="bars"/>, as <see cref="RangeKey"/> holds it.</summary>
        public static double[] Ranges(IReadOnlyList<Ohlcv> bars) =>
            RangeCache.GetValue(bars, b => b.Select(x => x.High - x.Low).ToArray());

        /// <summary>Every bar's volume in <paramref name="bars"/>, as <see cref="VolumeKey"/> holds it.</summary>
        public static double[] Volumes(IReadOnlyList<Ohlcv> bars) =>
            VolumeCache.GetValue(bars, b => b.Select(x => x.Volume).ToArray());

        /// <summary>The one-value array <see cref="BaselineKey"/> holds for <paramref name="price"/>
        /// (0 or less: the first visible close). One array per price, so an unchanged base never
        /// replaces the series.</summary>
        private static double[] BaselineValue(double price)
        {
            double held = price > 0 && double.IsFinite(price) ? price : double.NaN;
            lock (BaselineCache)
            {
                if (!BaselineCache.TryGetValue(held, out var arr))
                    BaselineCache[held] = arr = new[] { held };
                return arr;
            }
        }

        /// <summary>
        /// The Baseline chart's base for a view that starts at bar <paramref name="viewportStart"/>:
        /// the chosen price, or the close of the first bar in view (TradingView's base moves with
        /// the view the same way when it is not fixed). NaN when <paramref name="series"/> is not
        /// on a Baseline chart or has no close there. The ONE rule the drawing, the sound and the
        /// words read the base from.
        /// </summary>
        public static double BaselineBase(ChartSeries series, int viewportStart)
        {
            if (!series.Data.ComponentData.TryGetValue(BaselineKey, out var chosen) || chosen.Length == 0) return double.NaN;
            if (double.IsFinite(chosen[0])) return chosen[0];
            if (!series.Data.ComponentData.TryGetValue(BaselinePart, out var closes) || closes.Length == 0) return double.NaN;
            return closes[Math.Clamp(viewportStart, 0, closes.Length - 1)];
        }

        /// <summary>True when <paramref name="series"/> is the price series drawn as volume candles.</summary>
        public static bool IsVolumeCandles(ChartSeries series) => series.Data.ComponentData.ContainsKey(VolumeKey);

        /// <summary>True when <paramref name="series"/> is the price series drawn as hollow candles.</summary>
        public static bool IsHollow(ChartSeries series) => series.Data.ComponentData.ContainsKey(HollowGreenKey);

        /// <summary>Bar <paramref name="index"/>'s colour on Hollow candles: true green, false red,
        /// null when the series is not hollow candles or has no colour there.</summary>
        public static bool? HollowIsGreen(ChartSeries series, int index) =>
            series.Data.ComponentData.TryGetValue(HollowGreenKey, out var green) && index >= 0 && index < green.Length
                ? green[index] >= 0.5
                : null;

        /// <summary>
        /// The hidden values the chart type needs and no others: <see cref="HollowGreenKey"/> on
        /// Hollow candles, <see cref="VolumeKey"/> on Volume candles, <see cref="RangeKey"/> on
        /// High-low, <see cref="BaselineKey"/> on Baseline. Taken away on every other type, because their presence IS how the sound and
        /// the words of the shared candle parts know which chart they are on.
        /// </summary>
        private static ChartSeries WithStyleValues(ChartSeries series, WorkspaceState state)
        {
            var bars = state.Data;
            series = state.ChartStyle == PriceChartStyle.HollowCandles && bars != null && Of(series.Config) == Kind.Candles
                ? WithHidden(series, HollowGreenKey, HollowGreen(bars))
                : WithoutHidden(series, HollowGreenKey);
            series = state.ChartStyle == PriceChartStyle.VolumeCandles && bars != null && Of(series.Config) == Kind.Candles
                ? WithHidden(series, VolumeKey, Volumes(bars))
                : WithoutHidden(series, VolumeKey);
            series = state.ChartStyle == PriceChartStyle.HighLow && bars != null && Of(series.Config) == Kind.HighLow
                ? WithHidden(series, RangeKey, Ranges(bars))
                : WithoutHidden(series, RangeKey);
            series = state.ChartStyle == PriceChartStyle.Baseline && Of(series.Config) == Kind.Baseline
                ? WithHidden(series, BaselineKey, BaselineValue(state.Renko.BaselinePrice))
                : WithoutHidden(series, BaselineKey);
            return series;
        }

        /// <summary><paramref name="series"/> without the hidden <paramref name="key"/>; the same
        /// instance when it has none.</summary>
        private static ChartSeries WithoutHidden(ChartSeries series, string key)
        {
            if (!series.Data.ComponentData.ContainsKey(key)) return series;
            var data = new Dictionary<string, double[]>(series.Data.ComponentData);
            data.Remove(key);
            return WithBuffer(series, data);
        }

        /// <summary><paramref name="series"/> with <paramref name="values"/> under the hidden
        /// <paramref name="key"/>; the same instance when it already holds that very array.</summary>
        private static ChartSeries WithHidden(ChartSeries series, string key, double[] values)
        {
            if (series.Data.ComponentData.TryGetValue(key, out var held) && ReferenceEquals(held, values))
                return series;
            return WithBuffer(series, new Dictionary<string, double[]>(series.Data.ComponentData) { [key] = values });
        }

        private static ChartSeries WithBuffer(ChartSeries series, Dictionary<string, double[]> componentData)
        {
            var data = series.Data;
            var copy = new SeriesDataBuffer
            {
                SeriesId = data.SeriesId,
                FirstBarDate = data.FirstBarDate,
                ComponentData = componentData,
                ProfileBins = data.ProfileBins,
                HeatmapData = data.HeatmapData,
            };
            return new ChartSeries(series.Config, copy)
            {
                Drawing = series.Drawing,
                IsProfile = series.IsProfile,
                FocusedBinIndex = series.FocusedBinIndex,
                RequiresFullRecalcOnTick = series.RequiresFullRecalcOnTick,
            };
        }

        /// <summary><paramref name="series"/> in the shape <paramref name="style"/> draws, and
        /// named for it; the same instance when it is already, or when it is not the price series.</summary>
        public static ChartSeries Apply(ChartSeries series, PriceChartStyle style, bool heikinAshi = false,
            IReadOnlyList<Ohlcv>? bars = null)
        {
            if (!IsPriceSeries(series.Config)) return series;
            var target = For(style);
            string name = NameFor(style, heikinAshi);
            if (Of(series.Config) == target && !NeedsName(series.Config, name)) return series;

            var config = Named(Reshape(series.Config, target), name);
            return new ChartSeries(config, WithPartData(series.Data, config, bars))
            {
                Drawing = series.Drawing,
                IsProfile = series.IsProfile,
                FocusedBinIndex = series.FocusedBinIndex,
                RequiresFullRecalcOnTick = series.RequiresFullRecalcOnTick,
            };
        }

        /// <summary>
        /// The computed values under the new part's name too: a close part takes the close already
        /// in the buffer under the body's name, so the line or brick is heard and read at once
        /// rather than after the next recalculation. A part mapped to the high, the low or their
        /// middle (HLC area, High-low) is worked out from <paramref name="bars"/> when they are
        /// given, as the recalculation would.
        /// </summary>
        private static SeriesDataBuffer WithPartData(SeriesDataBuffer data, SeriesConfig config, IReadOnlyList<Ohlcv>? bars = null)
        {
            var missing = config.Components.Where(c => !data.ComponentData.ContainsKey(c.Name)).ToList();
            if (missing.Count == 0) return data;
            string? closeKey = new[] { "body", LinePart, BrickPart, BlockPart, KagiPart, PnfPart, BarPart, BaselinePart, HlcClosePart }
                .FirstOrDefault(data.ComponentData.ContainsKey);
            if (closeKey == null && bars == null) return data;
            var copy = new SeriesDataBuffer
            {
                SeriesId = data.SeriesId,
                FirstBarDate = data.FirstBarDate,
                ComponentData = new Dictionary<string, double[]>(data.ComponentData),
                ProfileBins = data.ProfileBins,
                HeatmapData = data.HeatmapData,
            };
            foreach (var c in missing)
            {
                if (closeKey != null && string.Equals(c.DataMapping, "close", StringComparison.OrdinalIgnoreCase))
                    copy.ComponentData[c.Name] = data.ComponentData[closeKey];
                else if (bars != null && Indicators.IndicatorStateMapper.MapBar(c.DataMapping) is { } field)
                    copy.ComponentData[c.Name] = bars.Select(field).ToArray();
            }
            return copy;
        }

        /// <summary>
        /// The config to SAVE: the candle shape, whatever the chart type. The live config is left
        /// alone. Every other series, and a price series already candles, comes back as it is.
        /// </summary>
        public static SeriesConfig Canonical(SeriesConfig config)
        {
            if (!IsPriceSeries(config)) return config;
            if (Of(config) == Kind.Candles && !NeedsName(config, "Candles")) return config;
            return Named(Reshape(config, Kind.Candles), "Candles");
        }

        /// <summary>True when a name this class gave (or no name) differs from <paramref name="name"/>.</summary>
        private static bool NeedsName(SeriesConfig config, string name) =>
            Renames(config.Name, name) || Renames(config.FriendlyName, name);

        private static bool Renames(string? current, string name) =>
            string.IsNullOrEmpty(current)
                ? name != "Candles"   // an unnamed candle series is left as it was
                : ChartTypeNames.Contains(current) && !string.Equals(current, name, StringComparison.Ordinal);

        /// <summary><paramref name="config"/> (already a copy) with the chart type's name in place
        /// of a chart-type name; a name the user gave stays.</summary>
        private static SeriesConfig Named(SeriesConfig config, string name)
        {
            if (Renames(config.Name, name)) config.Name = name;
            if (Renames(config.FriendlyName, name)) config.FriendlyName = name;
            return config;
        }

        /// <summary>A copy of <paramref name="config"/> with its parts in <paramref name="target"/>.</summary>
        public static SeriesConfig Reshape(SeriesConfig config, Kind target)
        {
            var copy = config.Clone();
            var current = Of(config);
            if (current == target) return copy;

            // The candle parts: the live ones, or the ones kept aside while it was a line/bricks.
            List<ComponentConfig> candleParts = current == Kind.Candles
                ? copy.Components.ToList()
                : copy.CandleParts.ToList();
            ComponentConfig? one = current == Kind.Candles ? null
                : current == Kind.HlcArea ? copy.Components.FirstOrDefault(c => c.Name == HlcClosePart) ?? copy.Components[0]
                : copy.Components[0];

            copy.Components.Clear();
            copy.CandleParts.Clear();

            if (target == Kind.Candles)
            {
                if (candleParts.Count == 0) candleParts = MinimalCandleParts(one);
                // The one part's mute, volume and visibility are the price's: they go back to
                // the body, so muting the line and choosing Candles does not unmute the price.
                var body = Body(candleParts);
                if (one != null && body != null)
                {
                    body.IsMuted = one.IsMuted;
                    body.IsVisible = one.IsVisible;
                    body.Volume = one.Volume;
                }
                foreach (var c in candleParts) copy.Components.Add(c);
                return copy;
            }

            foreach (var c in candleParts) copy.CandleParts.Add(c);
            if (target == Kind.HlcArea)
            {
                // Three lines, top to bottom as drawn. The close line takes the price's mute,
                // volume and visibility, as the one part does; the high and low start heard.
                var body = Body(candleParts);
                var close = MakePart(target, body, one);
                copy.Components.Add(HlcEdge(close, HlcHighPart, "High", "high"));
                copy.Components.Add(close);
                copy.Components.Add(HlcEdge(close, HlcLowPart, "Low", "low"));
                return copy;
            }
            copy.Components.Add(MakePart(target, Body(candleParts), one));
            return copy;
        }

        /// <summary>The part focus lands on: the body on candles, the one part otherwise.</summary>
        public static int DefaultPart(SeriesConfig config)
        {
            for (int i = 0; i < config.Components.Count; i++)
                if (config.Components[i].Role == ComponentRole.Body) return i;
            return 0;
        }

        private static ComponentConfig? Body(List<ComponentConfig> parts) =>
            parts.FirstOrDefault(c => c.Role == ComponentRole.Body || c.DisplayType == ComponentDisplayType.Candle)
            ?? parts.FirstOrDefault();

        /// <summary>
        /// The one part for a line, area or brick chart. Made from the body (colours, volume,
        /// mute, visibility), and from the part it replaces when switching between those (Line
        /// to Renko keeps a mute set on the line). Its sound is its own type's, from the one
        /// table every part's default sound comes from.
        /// </summary>
        private static ComponentConfig MakePart(Kind target, ComponentConfig? body, ComponentConfig? previous)
        {
            var part = body?.Clone() ?? new ComponentConfig();
            var type = target switch
            {
                Kind.Area => ComponentDisplayType.Area,
                Kind.Brick or Kind.Block or Kind.Kagi or Kind.Column => ComponentDisplayType.Brick,
                // A bar and a high-low bar are drawn by the price series' own renderer, as a
                // candle is, so they keep the candle's display type.
                Kind.Bar or Kind.HighLow => ComponentDisplayType.Candle,
                _ => ComponentDisplayType.Line,
            };
            (part.Name, part.DisplayName) = target switch
            {
                Kind.Brick => (BrickPart, "Brick"),
                Kind.Block => (BlockPart, "Block"),
                Kind.Kagi => (KagiPart, "Kagi line"),
                Kind.Column => (PnfPart, "Column"),
                Kind.Bar => (BarPart, "Bar"),
                Kind.Baseline => (BaselinePart, "Close"),
                Kind.HighLow => (HighLowPart, "High-low bar"),
                Kind.HlcArea => (HlcClosePart, "Close"),
                _ => (LinePart, "Close"),
            };
            part.DisplayType = type;
            part.UserDisplayType = null;
            // Body, not PriceAction: the brick and the close line are the price itself, which is
            // what every "the price part" lookup (default focus, the price-bar reads) asks for.
            part.Role = ComponentRole.Body;
            // A high-low bar has no close on screen: its value is its middle.
            part.DataMapping = target == Kind.HighLow ? "hl2" : "close";
            part.IsAreaFill = target == Kind.Area;
            part.SpeechTemplate = "{name}. {type}. {value:price}.";
            if (previous != null)
            {
                part.IsMuted = previous.IsMuted;
                part.IsVisible = previous.IsVisible;
                part.Volume = previous.Volume;
            }

            var profile = new SonificationProfileProvider().GetProfile(type, part.Role, part.Name);
            part.Waveform = profile.Waveform;
            part.AboveReferenceWaveform = profile.AboveWaveform;
            part.BelowReferenceWaveform = profile.BelowWaveform;
            part.AmplitudeMapping = profile.AmplitudeMapping;
            part.PitchMapping = profile.PitchMapping;
            part.BaseFrequency = profile.BaseFrequency;
            part.TriggerBoundaryClick = profile.TriggerBoundaryClick;
            part.EnvelopeType = profile.EnvelopeType;
            // Up high, down low, as the body: the brick's pair is the body's own (user-tuned or not).
            part.BullishFrequency = body?.BullishFrequency ?? 440;
            part.BearishFrequency = body?.BearishFrequency ?? 220;
            // A body's own sound patch is a candle timbre; the line and the brick have their own.
            part.SoundPatchId = null;
            part.BullishSoundPatchId = null;
            part.BearishSoundPatchId = null;
            // A high-low bar has no direction to put in the pitch (no open or close is drawn), so
            // its pitch is where its middle sits, as a line's is.
            if (target == Kind.HighLow) part.PitchMapping = PitchMapping.Value;
            // The baseline's crossing click is its own (DefaultSonificationStrategy), against the
            // base the chart draws, not a fixed reference level.
            if (target == Kind.Baseline) { part.TriggerBoundaryClick = false; part.ReferenceLevel = null; }
            return part;
        }

        /// <summary>The high or low line of an HLC area chart, made from the close line: a line
        /// at its own price, heard where it is drawn.</summary>
        private static ComponentConfig HlcEdge(ComponentConfig close, string name, string display, string mapping)
        {
            var edge = close.Clone();
            edge.Name = name;
            edge.DisplayName = display;
            edge.Role = ComponentRole.PriceAction;
            edge.DataMapping = mapping;
            edge.IsMuted = false;
            edge.IsVisible = true;
            edge.Volume = close.Volume;
            var profile = new SonificationProfileProvider().GetProfile(ComponentDisplayType.Line, edge.Role, name);
            edge.PitchMapping = profile.PitchMapping;
            edge.AmplitudeMapping = profile.AmplitudeMapping;
            edge.EnvelopeType = profile.EnvelopeType;
            return edge;
        }

        /// <summary>
        /// Candle parts when none were kept (a save written by a path that kept the line shape
        /// without them): the one part as the body, with a wick either side of it.
        /// </summary>
        private static List<ComponentConfig> MinimalCandleParts(ComponentConfig? one)
        {
            var body = one?.Clone() ?? new ComponentConfig();
            body.Name = "body";
            body.DisplayName = "Body";
            body.DisplayType = ComponentDisplayType.Candle;
            body.Role = ComponentRole.Body;
            body.DataMapping = "close";
            body.IsAreaFill = false;
            var bodyProfile = new SonificationProfileProvider().GetProfile(ComponentDisplayType.Candle, ComponentRole.Body, "body");
            body.AmplitudeMapping = bodyProfile.AmplitudeMapping;
            body.PitchMapping = bodyProfile.PitchMapping;
            body.EnvelopeType = bodyProfile.EnvelopeType;

            ComponentConfig Wick(string name, string display, string mapping)
            {
                var w = body.Clone();
                w.Name = name;
                w.DisplayName = display;
                w.DisplayType = ComponentDisplayType.Wick;
                w.Role = ComponentRole.PriceAction;
                w.DataMapping = mapping;
                var p = new SonificationProfileProvider().GetProfile(ComponentDisplayType.Wick, ComponentRole.PriceAction, name);
                w.AmplitudeMapping = p.AmplitudeMapping;
                w.PitchMapping = p.PitchMapping;
                w.EnvelopeType = p.EnvelopeType;
                return w;
            }

            return new List<ComponentConfig>
            {
                Wick("upper_wick", "Upper Wick", "high"),
                body,
                Wick("lower_wick", "Lower Wick", "low"),
            };
        }
    }
}
