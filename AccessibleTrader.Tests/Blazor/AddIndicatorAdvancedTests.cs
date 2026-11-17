// AddIndicatorModal — the Advanced group (Cody, 2026-11-17: "add indicator dialog: Yes, add it").

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// Add Indicator splits settings the way Properties does: the settings the original indicator does
/// not have (<see cref="IndicatorParameterMetadata.IsAdvanced"/>) are in a collapsed "Advanced"
/// group after the rest, and are still sent when the indicator is added. It used to list every
/// setting in one group, so Cycle Position's six cycle-length overrides came between the user and
/// the Add button.
/// </summary>
public class AddIndicatorAdvancedTests
{
    private static IndicatorMetadata Ind(string code, params IndicatorParameterMetadata[] ps)
    {
        var m = new IndicatorMetadata { Code = code, Name = code, Category = "Test" };
        m.Parameters.AddRange(ps);
        return m;
    }

    private static IndicatorParameterMetadata Num(string name, double def, bool advanced = false) =>
        new() { Name = name, DisplayName = name, DataType = typeof(double), DefaultValue = def, IsAdvanced = advanced };

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.AddIndicatorModal> Open(BlazorTestHarness h, string code)
    {
        h.IndicatorService.GetAvailableIndicators().Returns(new List<IndicatorMetadata>
        {
            Ind("WITH_ADVANCED", Num("Length", 14), Num("Deep", 3, advanced: true), Num("Shallow", 1, advanced: true)),
            Ind("PLAIN", Num("Length", 20)),
        });
        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.AddIndicatorModal>(bus => bus.Publish(new OpenAddIndicatorEvent()));
        cut.WaitForElement("#indicator-name").Change(code);
        return cut;
    }

    [Fact]
    public void Advanced_settings_are_in_a_collapsed_group_after_the_others()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h, "WITH_ADVANCED");

        var main = cut.Find("fieldset");
        Assert.Equal("Parameters", main.QuerySelector("legend")!.TextContent.Trim());
        Assert.NotNull(main.QuerySelector("#param-Length"));
        Assert.Null(main.QuerySelector("#param-Deep"));

        var advanced = cut.Find("details#add-indicator-advanced");
        Assert.False(advanced.HasAttribute("open"));
        Assert.Equal("Advanced (2 settings)", advanced.QuerySelector("summary")!.TextContent.Trim());
        Assert.NotNull(advanced.QuerySelector("#param-Deep"));
        Assert.NotNull(advanced.QuerySelector("#param-Shallow"));
        Assert.Null(advanced.QuerySelector("#param-Length"));
    }

    [Fact]
    public void An_indicator_without_advanced_settings_has_no_advanced_group()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h, "PLAIN");
        Assert.NotNull(cut.Find("#param-Length"));
        Assert.Empty(cut.FindAll("#add-indicator-advanced"));
    }

    [Fact]
    public void Advanced_settings_are_still_sent_when_the_indicator_is_added()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h, "WITH_ADVANCED");
        cut.Find("#param-Deep").Change("5");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Add").Click();

        h.SeriesManager.Received(1).RegisterSeriesFromMetadata(
            Arg.Is<IndicatorMetadata>(m => m.Code == "WITH_ADVANCED"),
            Arg.Is<Dictionary<string, object>>(p => p.Count == 3 && Convert.ToDouble(p["Deep"]) == 5
                && Convert.ToDouble(p["Shallow"]) == 1 && Convert.ToDouble(p["Length"]) == 14));
    }
}
