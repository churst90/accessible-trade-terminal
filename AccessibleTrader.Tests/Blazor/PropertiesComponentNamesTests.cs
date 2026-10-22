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
/// Properties names a component the way everything else does — by its DISPLAY name. Its component
/// pickers, fieldset legends and per-field labels read the machine name, so Cipher B's "Buy dot"
/// would have been "Oversold Crossover" in
/// this dialog alone. The option VALUES stay machine names: the selection keys on them.
/// </summary>
public class PropertiesComponentNamesTests
{
    private static ChartSeries CipherB()
    {
        var factory = new IndicatorModelFactory(
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());
        return factory.CreateSeriesFromMetadata(new CipherBProvider().GetIndicators()[0], "Cipher B", "Pane_CIPHER_B",
            new List<(string, string)>(), componentOverrides: null);
    }

    [Theory]
    [InlineData("appearance", "Component: ")]
    [InlineData("speech", "Speech: ")]
    [InlineData("sonification", "Acoustics: ")]
    public void Each_tab_names_Cipher_B_dots_by_their_display_names(string tab, string legendPrefix)
    {
        using var h = new BlazorTestHarness();
        var series = CipherB();
        var state = WorkspaceState.Initial with { ActiveSeries = ImmutableList.Create(series), FocusedSeriesId = series.Id };
        h.WorkspaceStore.State.Returns(_ => state);

        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.PropertiesModal>(bus => bus.Publish(new OpenPropertiesEvent(null)));
        cut.WaitForElement($"button#props-tab-{tab}").Click();

        cut.WaitForAssertion(() =>
        {
            var legends = cut.FindAll("legend").Select(l => l.TextContent.Trim()).ToList();
            Assert.Contains($"{legendPrefix}Buy dot", legends);
            Assert.DoesNotContain(legends, l => l.Contains("Oversold Crossover") || l.Contains("Triple Confluence"));

            var options = cut.FindAll("option").Where(o => o.GetAttribute("value") == CipherBProvider.CompBlue).ToList();
            Assert.NotEmpty(options);
            Assert.All(options, o => Assert.Equal("Buy dot", o.TextContent.Trim()));

            var labelled = cut.FindAll("[aria-label]").Select(e => e.GetAttribute("aria-label")!).ToList();
            Assert.Contains(labelled, a => a.EndsWith(" for Buy dot"));
            Assert.DoesNotContain(labelled, a => a.Contains("Triple Confluence"));
        });
    }
}
