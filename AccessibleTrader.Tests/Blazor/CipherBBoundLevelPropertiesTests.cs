using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// In Properties a Cipher B line and its setting are one number (2026-10-31): changing Trigger 2
/// on General moves the dotted −53 line and the money-flow bar, typing a value for the −60 line on
/// Levels changes Over Sold Level 1 (the buy dot's threshold), and Save keeps them together.
/// </summary>
public class CipherBBoundLevelPropertiesTests
{
    private static ChartSeries CipherB()
    {
        var providers = new List<IIndicatorProvider> { new CipherBProvider() };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
        var meta = new CipherBProvider().GetIndicators();
        var factory = new IndicatorModelFactory(
            new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());
        var saved = new SeriesConfig { Id = "cb-1", Name = "Cipher B", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B" };
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        return SeriesManagementService.MaterializeSaved(saved, meta[0], factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, new MockIndicatorPreferencesService());
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.PropertiesModal> Open(BlazorTestHarness h, ChartSeries series)
    {
        var state = WorkspaceState.Initial with { ActiveSeries = ImmutableList.Create(series), FocusedSeriesId = series.Id };
        h.WorkspaceStore.State.Returns(_ => state);
        h.IndicatorService.GetAvailableIndicators().Returns(new List<IndicatorMetadata> { new CipherBProvider().GetIndicators()[0] });
        return h.OpenModal<AccessibleTrader.BlazorClient.Components.PropertiesModal>(bus => bus.Publish(new OpenPropertiesEvent(null)));
    }

    private static AngleSharp.Dom.IElement LevelValue(IRenderedComponent<AccessibleTrader.BlazorClient.Components.PropertiesModal> cut, string name) =>
        cut.Find($"input[aria-label='Value of level {name}']");

    [Fact]
    public void A_setting_moves_its_line_and_a_line_moves_its_setting_and_save_keeps_both()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h, CipherB());

        cut.WaitForElement($"#prop-{CipherBProvider.ParamTrigger2}").Change("-30");

        cut.Find("button#props-tab-levels").Click();
        cut.WaitForAssertion(() => Assert.Equal("-30", LevelValue(cut, "Oversold").GetAttribute("value")));
        LevelValue(cut, "Extreme OS").Change("-45");

        cut.Find("button#props-tab-general").Click();
        cut.WaitForAssertion(() => Assert.Equal("-45", cut.Find($"#prop-{CipherBProvider.ParamOSLevel1}").GetAttribute("value")));

        cut.Find("#props-save").Click();
        h.WorkspaceStore.Received().Dispatch(Arg.Is<UpdateSeriesAction>(a => Saved(a.Series.Single(s => s.Id == "cb-1"))));
    }

    private static bool Saved(ChartSeries s) =>
        s.Parameters[CipherBProvider.ParamTrigger2] == -30
        && s.Parameters[CipherBProvider.ParamOSLevel1] == -45
        && s.Levels.Single(l => l.Name == "Oversold").Value == -30
        && s.Levels.Single(l => l.Name == "Extreme OS").Value == -45
        && s.Config.ZoneBands.Single(b => b.DisplayName == "Money Flow Bar") is { FixedTop: -70, FixedBottom: -80 };

    /// <summary>A level bound to nothing keeps its own value field.</summary>
    [Fact]
    public void The_zero_line_is_not_a_setting_and_keeps_its_own_value()
    {
        using var h = new BlazorTestHarness();
        var cut = Open(h, CipherB());
        cut.WaitForElement("button#props-tab-levels").Click();
        cut.WaitForAssertion(() => LevelValue(cut, "Zero"));
        LevelValue(cut, "Zero").Change("5");
        cut.Find("#props-save").Click();
        h.WorkspaceStore.Received().Dispatch(Arg.Is<UpdateSeriesAction>(a =>
            a.Series.Single(s => s.Id == "cb-1").Levels.Single(l => l.Name == "Zero").Value == 5
            && a.Series.Single(s => s.Id == "cb-1").Parameters[CipherBProvider.ParamTrigger2] == -53));
    }
}
