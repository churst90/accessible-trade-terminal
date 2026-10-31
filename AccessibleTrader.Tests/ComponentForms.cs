using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// A series built with each component that has a second form ("Read as", <see cref="IndicatorComponentMetadata.OtherForm"/>)
/// in that form, by the path a chart saved that way takes: its own components, with the choice,
/// handed back to the factory as the saved state.
/// </summary>
internal static class ComponentForms
{
    public static bool HasOtherForm(IndicatorMetadata meta) => meta.Components.Any(c => c.OtherForm != null);

    public static ChartSeries InOtherForm(IIndicatorModelFactory factory, IndicatorMetadata meta)
    {
        var first = factory.CreateSeriesFromMetadata(meta, meta.Name, meta.DefaultPane, new List<(string, string)>(), null);
        var saved = first.Config.Components.Concat(first.Config.ExcludedComponents).Select(c => c.Clone()).ToList();
        foreach (var c in saved)
            if (meta.Components.FirstOrDefault(m => m.Name == c.Name)?.OtherForm is { } other)
                c.UserDisplayType = other.DisplayType;
        return factory.CreateSeriesFromMetadata(meta, meta.Name, meta.DefaultPane, new List<(string, string)>(), saved);
    }
}
