using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.MyData;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// My Data v2 — imported datasets as series ON an existing chart, aligned to
    /// its bars by forward-fill (the COT trick: weekly data holds its value across
    /// daily bars). Three families per non-events dataset, all in the indicator
    /// dialog under "My Data":
    ///
    /// <list type="bullet">
    /// <item><b>My Data: X</b> — own pane, raw values, one navigable component per
    /// column (the multi-column promise: Income/Expenses/Net each its own voice).
    /// Optional "Normalize to 100" parameter rebases every column to 100 at its
    /// first value so different-magnitude columns compare by shape.</item>
    /// <item><b>My Data overlay: X</b> — ON the price pane, every column rebased
    /// so its first aligned value equals the chart's close there: relative
    /// performance against the loaded symbol. This is the %-compare engine.</item>
    /// <item><b>My Data ratio: X</b> — own pane, chart close ÷ dataset value
    /// (OHLCV datasets only — asset-vs-asset strength).</item>
    /// </list>
    /// </summary>
    public sealed class MyDataSeriesProvider : IIndicatorProvider
    {
        public const string SeriesPrefix = "MYDATA_SR_";
        public const string OverlayPrefix = "MYDATA_OV_";
        public const string RatioPrefix = "MYDATA_RT_";
        public const string ParamNormalize = "Normalize";
        public const string OhlcvColumn = "Close";

        private readonly IMyDataStore _store;

        public MyDataSeriesProvider(IMyDataStore store) => _store = store;

        public string Name => "MyData.Series";

        public List<IndicatorMetadata> GetIndicators()
        {
            var list = new List<IndicatorMetadata>();
            foreach (var ds in _store.Datasets.Where(d => d.Shape != MyDataShape.Events))
            {
                var columns = ColumnsOf(ds);

                list.Add(new IndicatorMetadata
                {
                    Code = SeriesPrefix + ds.Id,
                    Name = $"My Data: {ds.Name}",
                    Category = "My Data",
                    DefaultPane = ds.Name,
                    Components = columns.Select(c => LineComponent(c, "#4FC3F7")).ToList(),
                    Parameters = new List<IndicatorParameterMetadata>
                    {
                        new()
                        {
                            Name = ParamNormalize, DisplayName = "Normalize to 100",
                            Description = "1 = rebase every column to 100 at its first value, so columns of different size compare by shape. 0 = raw values.",
                            DataType = typeof(int), DefaultValue = 0, MinValue = 0, MaxValue = 1, Step = 1,
                        },
                    },
                });

                list.Add(new IndicatorMetadata
                {
                    Code = OverlayPrefix + ds.Id,
                    Name = $"My Data overlay: {ds.Name}",
                    Category = "My Data",
                    DefaultPane = "Main",
                    Components = columns.Select(c => LineComponent(c, "#FFB74D")).ToList(),
                });

                if (ds.Shape == MyDataShape.Ohlcv)
                {
                    list.Add(new IndicatorMetadata
                    {
                        Code = RatioPrefix + ds.Id,
                        Name = $"My Data ratio: {ds.Name}",
                        Category = "My Data",
                        DefaultPane = ds.Name + " ratio",
                        Components = new List<IndicatorComponentMetadata>
                        {
                            LineComponent("Ratio", "#BA68C8"),
                        },
                    });
                }
            }
            return list;
        }

        private static IndicatorComponentMetadata LineComponent(string name, string colorHex) => new()
        {
            Name = name,
            DisplayName = name,
            Role = ComponentRole.PriceAction, // continuous line semantics for sound defaults
            DisplayType = ComponentDisplayType.Line,
            DefaultColorHex = colorHex,
            DefaultThickness = 1.6f,
            DefaultPitchMapping = PitchMapping.Value,
            // Magnitude-aware value speech — imported units can be anything.
            SpeechTemplate = "{name}. {type}. {value:price}.",
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var (family, ds) = Decode(code);
            if (ds == null || data.Length == 0) return;
            var parsed = _store.GetParsed(ds.Id);
            if (parsed == null) return;

            bool normalize = family == SeriesPrefix && ReadInt(parameters, ParamNormalize) == 1;

            foreach (var column in ColumnsOf(ds))
            {
                var span = buffer.GetComponentSpan(family == RatioPrefix ? "Ratio" : column);
                span.Fill(double.NaN);

                var aligned = AlignForwardFill(parsed, column, data);
                int first = FirstFinite(aligned);
                if (first < 0) continue;

                for (int i = 0; i < data.Length; i++)
                {
                    double v = aligned[i];
                    if (double.IsNaN(v)) continue;
                    span[i] = family switch
                    {
                        OverlayPrefix => data[first].Close * (v / aligned[first]),
                        RatioPrefix => v == 0 ? double.NaN : data[i].Close / v,
                        _ => normalize ? 100.0 * (v / aligned[first]) : v,
                    };
                }

                if (family == RatioPrefix) break; // ratio uses one component only
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            // Forward-fill means the last bar's value only changes when the dataset
            // changes (an import), which re-runs full Calculate anyway. Live ticks
            // only move the ratio numerator — cheap enough to recompute fully there,
            // but a per-tick full pass is what the orchestrator does on import; the
            // final-bar ratio update is handled here.
            var (family, ds) = Decode(code);
            if (family != RatioPrefix || ds == null || data.Length == 0) return;
            var parsed = _store.GetParsed(ds.Id);
            if (parsed == null) return;
            var aligned = AlignForwardFill(parsed, OhlcvColumn, data);
            double v = aligned[^1];
            buffer.SetValue("Ratio", data.Length - 1,
                double.IsNaN(v) || v == 0 ? double.NaN : data[^1].Close / v);
        }

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            var (family, ds) = Decode(code);
            if (ds == null) return "";
            string kind = family switch
            {
                OverlayPrefix => "rebased to the chart for relative performance",
                RatioPrefix => "chart close divided by the dataset value",
                _ => "raw imported values",
            };
            return $"Imported dataset {ds.Name}, {kind}. Values forward-fill between data points.";
        }

        // ── Alignment ────────────────────────────────────────────────────────

        /// <summary>
        /// Forward-fills dataset values onto chart bars: each bar carries the last
        /// dataset value dated at or before it; NaN before the first data point.
        /// Internal for direct tests — this is the engine the compare feature shares.
        /// </summary>
        internal static double[] AlignForwardFill(ParsedDataset parsed, string column, ReadOnlySpan<Ohlcv> bars)
        {
            IReadOnlyList<DateTime> dates;
            IReadOnlyList<double> values;
            if (parsed.Shape == MyDataShape.Ohlcv)
            {
                dates = parsed.Bars.Select(b => b.Date).ToList();
                values = parsed.Bars.Select(b => b.Close).ToList();
            }
            else
            {
                if (!parsed.ColumnData.TryGetValue(column, out var v))
                {
                    var empty = new double[bars.Length];
                    Array.Fill(empty, double.NaN);
                    return empty;
                }
                dates = parsed.Dates;
                values = v;
            }
            return AlignForwardFill(dates, values, bars);
        }

        /// <summary>Raw-series overload — shared by the symbol-compare indicator,
        /// which feeds it fetched exchange bars instead of an imported dataset.</summary>
        internal static double[] AlignForwardFill(
            IReadOnlyList<DateTime> dates, IReadOnlyList<double> values, ReadOnlySpan<Ohlcv> bars)
        {
            var result = new double[bars.Length];
            Array.Fill(result, double.NaN);
            if (dates.Count == 0) return result;

            int j = -1;
            double carry = double.NaN;
            for (int i = 0; i < bars.Length; i++)
            {
                while (j + 1 < dates.Count && dates[j + 1] <= bars[i].Date)
                {
                    j++;
                    if (!double.IsNaN(values[j])) carry = values[j]; // gaps keep the last real value
                }
                result[i] = j >= 0 ? carry : double.NaN;
            }
            return result;
        }

        private static int FirstFinite(double[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (!double.IsNaN(values[i])) return i;
            return -1;
        }

        private IReadOnlyList<string> ColumnsOf(MyDataset ds) =>
            ds.Shape == MyDataShape.Ohlcv ? new[] { OhlcvColumn } : ds.Columns;

        private (string Family, MyDataset? Dataset) Decode(string code)
        {
            foreach (var prefix in new[] { SeriesPrefix, OverlayPrefix, RatioPrefix })
            {
                if (code.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string id = code[prefix.Length..];
                    return (prefix, _store.Datasets.FirstOrDefault(d => d.Id == id));
                }
            }
            return ("", null);
        }

        private static int ReadInt(Dictionary<string, object> parameters, string name)
        {
            if (parameters != null && parameters.TryGetValue(name, out var raw))
            {
                try { return Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture); }
                catch { }
            }
            return 0;
        }
    }
}
