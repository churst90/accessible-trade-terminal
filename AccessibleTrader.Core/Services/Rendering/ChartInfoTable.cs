using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>One cell of an indicator's table on the chart.</summary>
    /// <param name="Text">What it says; empty for a blank cell.</param>
    /// <param name="Fill">Its background as #RRGGBBAA, or null for none.</param>
    /// <param name="ColSpan">How many columns it spans.</param>
    /// <param name="Size">0 small, 1 heading, -1 tiny (the original's three text sizes).</param>
    public sealed record TableCell(string Text, string? Fill = null, int ColSpan = 1, int Size = 0);

    /// <summary>An indicator's table on the chart at one bar: its rows of cells, top first.</summary>
    public sealed record IndicatorTable(string SeriesId, IReadOnlyList<IReadOnlyList<TableCell>> Rows);

    /// <summary>
    /// The tables the price pane's indicators draw in its bottom right corner, as TradingView
    /// scripts draw theirs: so far only Cipher C's, its cycle counts at the bar being read
    /// (<see cref="CipherCProvider.TableRows"/>). A visual mirror of what the chart already
    /// speaks, as the status lines are, so the page marks it aria-hidden.
    /// </summary>
    public static class ChartInfoTable
    {
        public static IReadOnlyList<IndicatorTable> Build(IEnumerable<ChartSeries> series, int index)
        {
            var tables = new List<IndicatorTable>();
            foreach (var s in series)
            {
                if (!s.IsVisible || s.IsDrawing || s.Pane != "Main"
                    || !s.IndicatorCode.Equals(CipherCProvider.Code, StringComparison.OrdinalIgnoreCase)) continue;
                var rows = CipherCProvider.TableRows(n => s.GetComponentData(n) is { Length: > 0 } a ? a : null, index);
                if (rows.Count > 0) tables.Add(new IndicatorTable(s.Name, rows));
            }
            return tables;
        }
    }
}
