using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Analysis;

public interface IIndicatorContextAnalyzer
{
    /// <summary>Returns context for the first registered component found on this series.</summary>
    IndicatorContext? Analyze(ChartSeries series, WorkspaceState state);

    /// <summary>Returns context for every registered component found on this series.</summary>
    IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state);

    void RegisterDefinition(IndicatorContextDefinition definition);
}
