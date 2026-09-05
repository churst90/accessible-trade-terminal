using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Analysis;

public interface IIndicatorContextAnalyzer
{
    /// <summary>Returns context for the first registered component found on this series.</summary>
    IndicatorContext? Analyze(ChartSeries series, WorkspaceState state);

    /// <summary>Returns context for every registered component found on this series.</summary>
    IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state);

    void RegisterDefinition(IndicatorContextDefinition definition);

    /// <summary>
    /// Whether a registered definition gives this component its own overbought/oversold
    /// vocabulary.
    ///
    /// <para>
    /// Asked by the bar-close narrator so that ONE voice describes a threshold. An indicator
    /// with a registered definition AND declared levels — RSI is both, 70 and 30 twice over —
    /// would otherwise say "RSI overbought." and "crossed above overbought, 70." in the same
    /// breath. Where a definition exists it wins, because its wording was written for that
    /// indicator ("Anchor wave overbought", "Trigger positive"); everything else falls to the
    /// generic level-crossing sentence.
    /// </para>
    /// </summary>
    bool HasZoneThresholds(string indicatorCode, string componentName);
}
