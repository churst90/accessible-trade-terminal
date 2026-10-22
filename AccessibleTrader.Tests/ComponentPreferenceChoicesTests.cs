using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// "Save as Defaults" stores the axis-label and background-tint CHOICES with the indicator type's
/// defaults, and a new chart takes them only where it has no choice of its own: a choice made on one
/// chart is more specific than a type-wide default (Cody, 2026-10-19). Null means "never chose", so a
/// default saved from an untouched component leaves the indicator's own value in force.
/// </summary>
public sealed class ComponentPreferenceChoicesTests
{
    private const string IndicatorCode = "PREFERENCE_CHOICES";

    [Fact]
    public void An_axis_label_off_preference_applies_to_a_fresh_chart()
    {
        var series = CreateSeries(new[] { Preference("Line", axisLabel: false) }, "Line");

        Assert.False(series.Components.Single().ShowAxisLabelInEffect());
    }

    [Fact]
    public void A_price_chart_tint_preference_applies_to_a_fresh_chart()
    {
        var series = CreateSeries(new[] { Preference("Line", backgroundTint: BackgroundTint.PriceChart) }, "Line");

        Assert.Equal(BackgroundTint.PriceChart, series.Components.Single().BackgroundTintInEffect());
    }

    [Fact]
    public void A_chart_choice_wins_while_unset_choices_follow_the_type_preference()
    {
        var preferences = new[]
        {
            Preference("Own choice", axisLabel: false, backgroundTint: BackgroundTint.PriceChart),
            Preference("No chart choice", axisLabel: false, backgroundTint: BackgroundTint.PriceChart),
        };
        var savedChartChoice = new ComponentConfig
        {
            Name = "Own choice",
            UserShowAxisLabel = true,
            UserBackgroundTint = BackgroundTint.ThisPane,
        };
        var series = CreateSeries(preferences, "Own choice", "No chart choice", new[] { savedChartChoice });

        var own = series.Components.Single(c => c.Name == "Own choice");
        Assert.True(own.ShowAxisLabelInEffect());
        Assert.Equal(BackgroundTint.ThisPane, own.BackgroundTintInEffect());

        var unset = series.Components.Single(c => c.Name == "No chart choice");
        Assert.False(unset.ShowAxisLabelInEffect());
        Assert.Equal(BackgroundTint.PriceChart, unset.BackgroundTintInEffect());
    }

    /// <summary>
    /// The fill and drawing switches (Properties → Appearance, 2026-10-22) follow the same split:
    /// the type default reaches a chart that never chose, and a chart's own choice wins. A saved
    /// "no fill" overrides an indicator that declares one.
    /// </summary>
    [Fact]
    public void Fill_and_drawing_preferences_apply_where_the_chart_has_no_choice()
    {
        var preferences = new[]
        {
            Preference("Own choice", fill: true, drawPath: false),
            Preference("No chart choice", fill: true, drawPath: false),
        };
        var savedChartChoice = new ComponentConfig { Name = "Own choice", UserIsAreaFill = false, UserDrawPath = true };
        var series = CreateSeries(preferences, "Own choice", "No chart choice", new[] { savedChartChoice });

        var own = series.Components.Single(c => c.Name == "Own choice");
        Assert.False(own.IsAreaFillInEffect());
        Assert.True(own.DrawsPathInEffect());

        var unset = series.Components.Single(c => c.Name == "No chart choice");
        Assert.True(unset.IsAreaFillInEffect());
        Assert.False(unset.DrawsPathInEffect());
    }

    [Fact]
    public void A_preference_file_without_the_new_keys_loads_with_both_choices_null()
    {
        var paths = new TempWorkspacePaths();
        var service = new IndicatorPreferencesService(NullLogger<IndicatorPreferencesService>.Instance, paths);
        var filePath = Path.Combine(paths.AppDataDirectory, "IndicatorPrefs", $"{IndicatorCode}.json");
        File.WriteAllText(filePath,
            "{\"Components\":[{\"Name\":\"Line\",\"ColorHex\":\"#123456\"}],\"Levels\":[]}");

        var loaded = Assert.Single(service.GetPreferences(IndicatorCode)!);
        Assert.Equal("#123456", loaded.ColorHex);
        Assert.Null(loaded.UserShowAxisLabel);
        Assert.Null(loaded.UserBackgroundTint);
        Assert.Null(loaded.UserIsAreaFill);
        Assert.Null(loaded.UserDrawPath);
    }

    private static ChartSeries CreateSeries(ComponentPreference[] preferences, params string[] components) =>
        CreateSeries(preferences, components, null);

    private static ChartSeries CreateSeries(ComponentPreference[] preferences, string first, string second,
        ComponentConfig[]? savedComponents = null) =>
        CreateSeries(preferences, new[] { first, second }, savedComponents);

    private static ChartSeries CreateSeries(ComponentPreference[] preferences, string[] components,
        ComponentConfig[]? savedComponents)
    {
        var preferenceService = Substitute.For<IIndicatorPreferencesService>();
        preferenceService.GetPreferences(IndicatorCode).Returns(preferences.ToList());
        var factory = new IndicatorModelFactory(new MockStylingService(), preferenceService);
        var metadata = new IndicatorMetadata
        {
            Name = "Preference Choices",
            Code = IndicatorCode,
            Components = components.Select(name => new IndicatorComponentMetadata
            {
                Name = name,
                DisplayType = ComponentDisplayType.Line,
                DefaultShowAxisLabel = true,
                DefaultBackgroundTint = BackgroundTint.Off,
                PathName = "arc",
            }).ToList(),
        };
        return factory.CreateSeriesFromMetadata(metadata, metadata.Name, "Main", new(), savedComponents?.ToList());
    }

    private static ComponentPreference Preference(string name, bool? axisLabel = null,
        BackgroundTint? backgroundTint = null, bool? fill = null, bool? drawPath = null) =>
        new()
        {
            Name = name, UserShowAxisLabel = axisLabel, UserBackgroundTint = backgroundTint,
            UserIsAreaFill = fill, UserDrawPath = drawPath,
        };
}
