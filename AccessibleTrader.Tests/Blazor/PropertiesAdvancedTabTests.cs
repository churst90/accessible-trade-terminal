using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// Settings the original indicator has no input for go on an Advanced tab (Cody, 2026-10-23), so
/// General holds what the original offers. Cipher B is the case he named: its legacy money flow,
/// gated buy dot, adaptive thresholds, anchor suppression and cooldown are ours, not Market
/// Cipher B's.
/// </summary>
public class PropertiesAdvancedTabTests
{
    private static IndicatorMetadata CipherBMeta() => new CipherBProvider().GetIndicators()[0];

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.PropertiesModal> Open(BlazorTestHarness h, IndicatorMetadata meta)
    {
        var factory = new IndicatorModelFactory(
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());
        var series = factory.CreateSeriesFromMetadata(meta, meta.Name, "Pane_X",
            SeriesManagementService.DefaultParameterList(meta), componentOverrides: null);
        var state = WorkspaceState.Initial with { ActiveSeries = ImmutableList.Create(series), FocusedSeriesId = series.Id };
        h.WorkspaceStore.State.Returns(_ => state);
        h.IndicatorService.GetAvailableIndicators().Returns(new List<IndicatorMetadata> { meta });
        return h.OpenModal<AccessibleTrader.BlazorClient.Components.PropertiesModal>(bus => bus.Publish(new OpenPropertiesEvent(null)));
    }

    private static List<string> Labels(IRenderedComponent<AccessibleTrader.BlazorClient.Components.PropertiesModal> cut) =>
        cut.FindAll("#props-tabpanel label").Select(l => l.TextContent.Trim()).ToList();

    [Fact]
    public void Cipher_B_s_own_settings_are_on_Advanced_and_the_originals_inputs_on_General()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h, CipherBMeta());

        cut.WaitForAssertion(() =>
        {
            var general = Labels(cut);
            Assert.Contains("Money Flow Period", general);
            Assert.Contains("Buy Dot Level", general);
            Assert.DoesNotContain("Money Flow Formula", general);
            Assert.DoesNotContain("Buy Dot Rule", general);
            Assert.DoesNotContain("Cooldown Bars", general);
        });

        cut.Find("button#props-tab-advanced").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("button#props-tab-advanced").GetAttribute("aria-selected"));
            var advanced = Labels(cut);
            foreach (var p in CipherBMeta().Parameters.Where(p => p.IsAdvanced))
                Assert.Contains(p.DisplayName, advanced);
            Assert.DoesNotContain("Money Flow Period", advanced);
        });
    }

    [Fact]
    public void An_indicator_with_no_settings_of_ours_has_no_Advanced_tab()
    {
        var meta = new IndicatorMetadata
        {
            Code = "PLAIN", Name = "Plain", DefaultPane = "Main",
            Parameters = { new IndicatorParameterMetadata { Name = "Period", DisplayName = "Period", DataType = typeof(int), DefaultValue = 14.0 } },
            Components = { new IndicatorComponentMetadata { Name = "Line", DisplayName = "Line" } },
        };
        using var h = new BlazorTestHarness();
        var cut = Open(h, meta);

        cut.WaitForAssertion(() => Assert.Contains("Period", Labels(cut)));
        Assert.Empty(cut.FindAll("button#props-tab-advanced"));
    }

    [Fact]
    public void The_arrow_keys_reach_the_Advanced_tab_and_wrap_from_it()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h, CipherBMeta());
        var tablist = cut.WaitForElement("[role='tablist']");

        tablist.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "End" });
        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("button#props-tab-advanced").GetAttribute("aria-selected")));

        cut.Find("[role='tablist']").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowRight" });
        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("button#props-tab-general").GetAttribute("aria-selected")));
    }
}
