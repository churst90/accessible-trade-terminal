using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public class DataExportService : IDataExportService
    {
        public Task<string> ExportToCsvAsync(
            IReadOnlyList<Ohlcv> data,
            int viewportStart,
            int viewportLength,
            IReadOnlyList<ChartSeries> seriesList)
        {
            var visibleData = data.Skip(viewportStart).Take(viewportLength).ToList();
            if (!visibleData.Any()) return Task.FromResult(string.Empty);

            // Build header: Date,Open,High,Low,Close,Volume + one column per visible component
            var indicatorCols = new List<(string Header, string SeriesId, string CompName)>();
            foreach (var series in seriesList.Where(s => s.IsVisible && !s.IsDrawing))
            {
                foreach (var comp in series.Components.Where(c => c.IsVisible))
                {
                    var compData = series.GetComponentData(comp.Name);
                    if (compData != null)
                        indicatorCols.Add(($"{series.FriendlyName}.{comp.Name}", series.Id, comp.Name));
                }
            }

            var sb = new StringBuilder();
            sb.Append("Date,Open,High,Low,Close,Volume");
            foreach (var (header, _, _) in indicatorCols)
                sb.Append(',').Append(EscapeCsv(header));
            sb.AppendLine();

            for (int i = 0; i < visibleData.Count; i++)
            {
                int absIdx = viewportStart + i;
                var bar = visibleData[i];
                sb.Append(bar.Date.ToString("yyyy-MM-dd HH:mm:ss")).Append(',');
                sb.Append(bar.Open).Append(',');
                sb.Append(bar.High).Append(',');
                sb.Append(bar.Low).Append(',');
                sb.Append(bar.Close).Append(',');
                sb.Append(bar.Volume);

                foreach (var (_, sid, compName) in indicatorCols)
                {
                    var series = seriesList.FirstOrDefault(s => s.Id == sid);
                    var compData = series?.GetComponentData(compName);
                    double val = compData != null && absIdx < compData.Length ? compData[absIdx] : double.NaN;
                    sb.Append(',').Append(double.IsNaN(val) ? "" : val.ToString("G6"));
                }
                sb.AppendLine();
            }

            return Task.FromResult(sb.ToString());
        }

        private static string EscapeCsv(string s) =>
            s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
