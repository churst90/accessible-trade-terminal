using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface IDataExportService
    {
        /// <summary>
        /// Exports visible OHLCV data plus all computed indicator component values
        /// for the current viewport to CSV format. Returns the CSV content as a string.
        /// </summary>
        Task<string> ExportToCsvAsync(
            IReadOnlyList<Ohlcv> data,
            int viewportStart,
            int viewportLength,
            IReadOnlyList<ChartSeries> seriesList);
    }
}
