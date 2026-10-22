using System.Text;
using System.Text.Json;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Writes a workspace profile with drawings added to its tabs, as the drawing tools would have
/// saved them.</b>
///
/// <para>
/// For the trend line study (2026-10-22): the lines found by rule are put on the user's own charts
/// as ordinary Trend line and Text label drawings, not as part of any indicator. Each drawing is
/// built by <see cref="DrawingPictureTool.BuildDrawing"/> (the real calculators and component
/// configs), added to the matching tab of a saved profile, and written with the same
/// <c>JsonConvert</c> call as <c>WorkspaceLibraryService.SaveProfile</c>. The file is then read back
/// the way <c>LoadProfile</c> reads it and every drawing is recomputed over the spec's bars; the
/// line's price at each of its anchors is written to a report next to the profile, so a line that
/// would restore in the wrong place is seen before the profile is opened.
/// </para>
///
/// <para>
/// Does nothing unless <c>AT_WORKSPACE_SPEC</c> names a JSON file, so it never writes in CI. The
/// spec: <c>{"base":"Camel cycles.json","out":"x.json","tabs":[{"symbol":"SPY","timeframe":"1d",
/// "bars":[["2026-08-13",o,h,l,c],...],"drawings":[ same entries as DrawingPictureTool ]}]}</c>.
/// </para>
/// </summary>
public sealed class WorkspaceDrawingTool
{
    [Fact]
    public void Workspace_drawings_are_written_only_when_a_spec_is_given()
    {
        string? specPath = Environment.GetEnvironmentVariable("AT_WORKSPACE_SPEC");
        if (string.IsNullOrWhiteSpace(specPath)) return;

        using var doc = JsonDocument.Parse(File.ReadAllText(specPath));
        var spec = doc.RootElement;
        var config = JsonConvert.DeserializeObject<WorkspaceConfiguration>(File.ReadAllText(spec.GetProperty("base").GetString()!))!;

        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        var drawingService = DrawingPictureTool.RealDrawingService();

        var barsByTab = new Dictionary<TabConfiguration, List<Ohlcv>>();
        foreach (var tabSpec in spec.GetProperty("tabs").EnumerateArray())
        {
            string symbol = tabSpec.GetProperty("symbol").GetString()!, timeframe = tabSpec.GetProperty("timeframe").GetString()!;
            var tab = config.Tabs.Single(t => t.Symbol == symbol && t.Timeframe == timeframe);
            var bars = DrawingPictureTool.Bars(tabSpec);
            barsByTab[tab] = bars;
            int n = tab.Series.Count(s => s.Drawing is { Type: not DrawingType.None });
            foreach (var d in tabSpec.GetProperty("drawings").EnumerateArray())
            {
                var (seriesConfig, _, drawing) = DrawingPictureTool.BuildDrawing(d, ++n, bars, factory, drawingService);
                seriesConfig.Drawing = drawing;
                tab.Series.Add(seriesConfig);
            }
        }

        string outPath = spec.GetProperty("out").GetString()!;
        File.WriteAllText(outPath, JsonConvert.SerializeObject(config, Formatting.Indented));

        // Read back as LoadProfile does, and recompute each drawing from its saved anchors.
        var reread = JsonConvert.DeserializeObject<WorkspaceConfiguration>(File.ReadAllText(outPath))!;
        var report = new StringBuilder();
        foreach (var (tab, bars) in barsByTab)
        {
            var saved = reread.Tabs.Single(t => t.Symbol == tab.Symbol && t.Timeframe == tab.Timeframe);
            report.AppendLine($"{saved.Symbol} {saved.Timeframe}: {bars.Count} bars {bars[0].Date:yyyy-MM-dd} to {bars[^1].Date:yyyy-MM-dd}");
            foreach (var s in saved.Series.Where(s => s.Drawing is { Type: not DrawingType.None }))
            {
                var dr = s.Drawing!;
                var values = drawingService.CalculateDrawingData(dr, bars);
                report.Append($"  {s.Name}: {dr.Type} {dr.AnchorDate1:yyyy-MM-dd} {dr.AnchorPrice1:0.00}");
                if (dr.AnchorDate2 is { } d2) report.Append($" -> {d2:yyyy-MM-dd} {dr.AnchorPrice2:0.00}");
                foreach (var (component, arr) in values)
                {
                    string At(DateTime? date)
                    {
                        if (date is not { } dd) return "";
                        int i = bars.FindIndex(b => b.Date >= dd);
                        return i < 0 || i >= arr.Length ? "none" : arr[i].ToString("0.00");
                    }
                    int drawn = arr.Count(v => !double.IsNaN(v));
                    report.Append($" | {component}: at anchor1 {At(dr.AnchorDate1)}, at anchor2 {At(dr.AnchorDate2)}, " +
                                  $"last bar {(arr.Length > 0 ? arr[^1].ToString("0.00") : "none")}, {drawn} bars drawn");
                }
                report.AppendLine();
            }
        }
        File.WriteAllText(Path.ChangeExtension(outPath, ".report.txt"), report.ToString());
    }
}
