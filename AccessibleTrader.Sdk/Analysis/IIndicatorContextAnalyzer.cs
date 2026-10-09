using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Analysis;

public interface IIndicatorContextAnalyzer
{
    /// <summary>Returns context for the first registered component found on this series.</summary>
    IndicatorContext? Analyze(ChartSeries series, WorkspaceState state);

    /// <summary>Returns context for every registered component found on this series.</summary>
    IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state);

    /// <summary>
    /// Context for ONE named component at ONE bar — what an alert needs. <see cref="Analyze"/>
    /// answers for the first registered component at the reading cursor, which is right for
    /// describing the bar the user is on and wrong for an alert twice over: it ignored the
    /// component the alert named (a Cipher B "Anchor Wave" alert read the Wave Trend) and it
    /// watched wherever the arrow keys had left the cursor instead of the market. A null
    /// <paramref name="componentName"/> keeps <see cref="Analyze"/>'s choice of component.
    /// </summary>
    IndicatorContext? AnalyzeAt(ChartSeries series, string? componentName, int dataIndex);

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
