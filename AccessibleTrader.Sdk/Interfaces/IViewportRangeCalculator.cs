using System.Collections.Immutable;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces;

public record ViewportRangeResult(
    (double Min, double Max) MainRange,
    ImmutableDictionary<string, (double Min, double Max)> PaneRanges
);

public interface IViewportRangeCalculator
{
    ViewportRangeResult Calculate(WorkspaceState state);
}
