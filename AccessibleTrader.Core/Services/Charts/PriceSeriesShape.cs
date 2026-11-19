using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// The main price series takes the shape of the chart type: three candle parts (upper wick,
    /// body, lower wick) on Candles and Heikin Ashi, one close line on Line, one filled close
    /// line on Area, one brick part on Renko, one block part on line break. Range bars are real
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
            _ => heikinAshi ? "Heikin Ashi" : "Candles",
        };

        /// <summary>The names this class gives the price series. A name the user gave it is
        /// none of these, and is never touched.</summary>
        private static readonly HashSet<string> ChartTypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "", "Candles", "Heikin Ashi", "Line", "Area", "Renko", "Range bars", "Line break", "Kagi",
        };

        /// <summary>What the price series' parts are.</summary>
        public enum Kind { Candles, Line, Area, Brick, Block, Kagi }

        /// <summary>The shape a chart type draws the price series in.</summary>
        public static Kind For(PriceChartStyle style) => style switch
        {
            PriceChartStyle.Line => Kind.Line,
            PriceChartStyle.Area => Kind.Area,
            PriceChartStyle.Renko => Kind.Brick,
            PriceChartStyle.LineBreak => Kind.Block,
            PriceChartStyle.Kagi => Kind.Kagi,
            _ => Kind.Candles,
        };

        /// <summary>The shape <paramref name="config"/>'s parts are in now.</summary>
        public static Kind Of(SeriesConfig config)
        {
            if (config.Components.Count != 1) return Kind.Candles;
            var only = config.Components[0];
            if (only.Name == BrickPart) return Kind.Brick;
            if (only.Name == BlockPart) return Kind.Block;
            if (only.Name == KagiPart) return Kind.Kagi;
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
            var reshaped = Apply(series, state.ChartStyle, state.IsHeikinAshi);
            if (ReferenceEquals(reshaped, series)) return state;

            var next = state with { ActiveSeries = state.ActiveSeries.SetItem(at, reshaped) };
            if (state.FocusedSeriesId == CoreSeriesIds.Candles)
                next = next with { FocusedComponentIndex = DefaultPart(reshaped.Config) };
            return next;
        }

        /// <summary><paramref name="series"/> in the shape <paramref name="style"/> draws, and
        /// named for it; the same instance when it is already, or when it is not the price series.</summary>
        public static ChartSeries Apply(ChartSeries series, PriceChartStyle style, bool heikinAshi = false)
        {
            if (!IsPriceSeries(series.Config)) return series;
            var target = For(style);
            string name = NameFor(style, heikinAshi);
            if (Of(series.Config) == target && !NeedsName(series.Config, name)) return series;

            var config = Named(Reshape(series.Config, target), name);
            return new ChartSeries(config, WithPartData(series.Data, config))
            {
                Drawing = series.Drawing,
                IsProfile = series.IsProfile,
                FocusedBinIndex = series.FocusedBinIndex,
                RequiresFullRecalcOnTick = series.RequiresFullRecalcOnTick,
            };
        }

        /// <summary>
        /// The computed values under the new part's name too: every part here is the close (or a
        /// wick's high/low), already in the buffer under the body's name, so the line or brick is
        /// heard and read at once rather than after the next recalculation.
        /// </summary>
        private static SeriesDataBuffer WithPartData(SeriesDataBuffer data, SeriesConfig config)
        {
            var missing = config.Components.Where(c => !data.ComponentData.ContainsKey(c.Name)).ToList();
            if (missing.Count == 0) return data;
            string? closeKey = new[] { "body", LinePart, BrickPart, BlockPart, KagiPart }.FirstOrDefault(data.ComponentData.ContainsKey);
            if (closeKey == null) return data;
            var copy = new SeriesDataBuffer
            {
                SeriesId = data.SeriesId,
                FirstBarDate = data.FirstBarDate,
                ComponentData = new Dictionary<string, double[]>(data.ComponentData),
                ProfileBins = data.ProfileBins,
                HeatmapData = data.HeatmapData,
            };
            foreach (var c in missing)
                if (string.Equals(c.DataMapping, "close", StringComparison.OrdinalIgnoreCase))
                    copy.ComponentData[c.Name] = data.ComponentData[closeKey];
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
            ComponentConfig? one = current == Kind.Candles ? null : copy.Components[0];

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
                Kind.Brick or Kind.Block or Kind.Kagi => ComponentDisplayType.Brick,
                _ => ComponentDisplayType.Line,
            };
            (part.Name, part.DisplayName) = target switch
            {
                Kind.Brick => (BrickPart, "Brick"),
                Kind.Block => (BlockPart, "Block"),
                Kind.Kagi => (KagiPart, "Kagi line"),
                _ => (LinePart, "Close"),
            };
            part.DisplayType = type;
            part.UserDisplayType = null;
            // Body, not PriceAction: the brick and the close line are the price itself, which is
            // what every "the price part" lookup (default focus, the price-bar reads) asks for.
            part.Role = ComponentRole.Body;
            part.DataMapping = "close";
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
            return part;
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
