using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The two cycle indicators swapped names on 2026-11-04, and their codes did not.</b> Cody: "swap the
/// names of the indicators such that cipher c is the cycle position more complete one and make our clone
/// of camel's indicator the cycle position one". The more complete one (<c>CYCLE_POSITION</c>) is shown
/// as "Cipher C"; the copy of the original (<c>CIPHER_C</c>) as "Cycle Position". A chart saved before
/// the swap carries the old names; it must come back under the new ones, with its code and settings.
/// </summary>
public sealed class CipherCCyclePositionNameSwapTests
{
    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    [Fact]
    public void The_more_complete_one_is_Cipher_C_and_the_copy_of_the_original_is_Cycle_Position()
    {
        var complete = new CyclePositionProvider();
        var copy = new CipherCProvider();
        Assert.Equal("Cipher C", complete.Name);
        Assert.Equal("Cipher C", complete.GetIndicators().Single().Name);
        Assert.Equal("Cycle Position", copy.Name);
        Assert.Equal("Cycle Position", copy.GetIndicators().Single().Name);
        // The codes are what saved charts, strategies and alerts hold; they stay.
        Assert.Equal("CYCLE_POSITION", CyclePositionProvider.Code);
        Assert.Equal("CIPHER_C", CipherCProvider.Code);
    }

    [Theory]
    [InlineData("CIPHER_C", "Cipher C", "Cycle Position")]
    [InlineData("CYCLE_POSITION", "Cycle Position", "Cipher C")]
    public void A_chart_saved_before_the_swap_comes_back_under_the_new_name(string code, string savedName, string shownName)
    {
        IIndicatorProvider provider = code == CipherCProvider.Code ? new CipherCProvider() : new CyclePositionProvider();
        var providers = new List<IIndicatorProvider> { provider };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
        var meta = provider.GetIndicators();
        var saved = new SeriesConfig { Id = "cyc-1", Name = savedName, FriendlyName = savedName, IndicatorCode = code, Pane = "Main" };
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);

        var series = SeriesManagementService.MaterializeSaved(saved, meta[0], Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, new MockIndicatorPreferencesService());

        Assert.Equal(code, series.Config.IndicatorCode);
        Assert.Equal(shownName, series.Config.FriendlyName);
    }

    [Fact]
    public void The_more_complete_ones_description_and_settings_name_the_copy_by_its_new_name()
    {
        var meta = new CyclePositionProvider().GetIndicators().Single();
        Assert.DoesNotContain("Cipher C", meta.Description);
        Assert.All(meta.Parameters, p => Assert.DoesNotContain("Cipher C", p.Description ?? ""));
    }
}
