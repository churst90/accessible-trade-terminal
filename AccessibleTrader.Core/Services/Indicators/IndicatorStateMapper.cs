using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    public interface IIndicatorStateMapper
    {
        SeriesDataBuffer MapResultsToBuffer(ChartSeries series, Dictionary<string, double[]> results, int dataCount);
        SeriesDataBuffer MapInternalDataToBuffer(ChartSeries series, IReadOnlyList<Ohlcv> data);
    }

    public class IndicatorStateMapper : IIndicatorStateMapper
    {
        public SeriesDataBuffer MapResultsToBuffer(ChartSeries series, Dictionary<string, double[]> results, int dataCount)
        {
            var buffer = new SeriesDataBuffer { SeriesId = series.Id };

            foreach (var component in series.Components)
            {
                if (results == null)
                {
                    buffer.ComponentData[component.Name] = CreateNaNArray(dataCount);
                    continue;
                }

                var key = results.Keys.FirstOrDefault(k => k.Equals(component.Name, StringComparison.OrdinalIgnoreCase))
                       ?? results.Keys.FirstOrDefault(k => k.Contains(component.Name, StringComparison.OrdinalIgnoreCase))
                       ?? results.Keys.FirstOrDefault(k => component.Name.Contains(k, StringComparison.OrdinalIgnoreCase));

                if (key == null)
                {
                    string cleanComp = CleanString(component.Name);
                    key = results.Keys.FirstOrDefault(k => CleanString(k).Equals(cleanComp, StringComparison.OrdinalIgnoreCase));
                }

                if (key != null)
                {
                    buffer.ComponentData[component.Name] = results[key];
                }
                else
                {
                    buffer.ComponentData[component.Name] = CreateNaNArray(dataCount);
                }
            }

            // Also copy companion arrays that providers write to the buffer but do not register
            // as navigable components (e.g. "WT Momentum_color" for gradient speech/audio,
            // "Resistance_touches" for S/R touch count speech). Without this pass they are
            // silently dropped and GetComponentData returns an empty array, causing "no data"
            // speech and silent gradient blend audio.
            if (results != null)
                foreach (var kvp in results)
                {
                    if (!buffer.ComponentData.ContainsKey(kvp.Key))
                        buffer.ComponentData[kvp.Key] = kvp.Value;
                }

            return buffer;
        }

        public SeriesDataBuffer MapInternalDataToBuffer(ChartSeries series, IReadOnlyList<Ohlcv> data)
        {
            var buffer = new SeriesDataBuffer { SeriesId = series.Id };
            int count = data.Count;

            // The candle parts kept aside while the price series is a line or bricks
            // (SeriesConfig.CandleParts) are mapped too: a strategy or alert that names the body or
            // a wick still finds its column on a Line or Renko chart.
            foreach (var component in series.Components.Concat(series.Config.CandleParts))
            {
                var mapping = component.DataMapping?.ToLower();
                if (string.IsNullOrEmpty(mapping)) continue;

                var field = MapBar(mapping);
                var arr = new double[count];
                for (int i = 0; i < count; i++)
                    arr[i] = field == null ? double.NaN : field(data[i]);
                buffer.ComponentData[component.Name] = arr;
            }

            return buffer;
        }

        /// <summary>
        /// The bar field a price part's DataMapping names: open, high, low, close, volume, or hl2
        /// (the middle of the high and the low, the High-low chart's bar). Null for anything else.
        /// </summary>
        public static Func<Ohlcv, double>? MapBar(string? mapping) => mapping?.ToLowerInvariant() switch
        {
            "open" => b => (double)b.Open,
            "high" => b => (double)b.High,
            "low" => b => (double)b.Low,
            "close" => b => (double)b.Close,
            "volume" => b => (double)b.Volume,
            "hl2" => b => ((double)b.High + (double)b.Low) / 2.0,
            _ => null,
        };

        private double[] CreateNaNArray(int count)
        {
            var arr = new double[count];
            Array.Fill(arr, double.NaN);
            return arr;
        }

        private string CleanString(string input) => new string(input.Where(char.IsLetterOrDigit).ToArray());
    }
}

