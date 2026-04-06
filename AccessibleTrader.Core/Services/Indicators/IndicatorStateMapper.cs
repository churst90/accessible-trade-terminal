using System;
using System.Collections.Generic;
using System.Linq;
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

            foreach (var component in series.Components)
            {
                var mapping = component.DataMapping?.ToLower();
                if (string.IsNullOrEmpty(mapping)) continue;

                var arr = new double[count];
                for (int i = 0; i < count; i++)
                {
                    arr[i] = mapping switch
                    {
                        "open" => (double)data[i].Open,
                        "high" => (double)data[i].High,
                        "low" => (double)data[i].Low,
                        "close" => (double)data[i].Close,
                        "volume" => (double)data[i].Volume,
                        _ => double.NaN
                    };
                }
                buffer.ComponentData[component.Name] = arr;
            }

            return buffer;
        }

        private double[] CreateNaNArray(int count)
        {
            var arr = new double[count];
            Array.Fill(arr, double.NaN);
            return arr;
        }

        private string CleanString(string input) => new string(input.Where(char.IsLetterOrDigit).ToArray());
    }
}

