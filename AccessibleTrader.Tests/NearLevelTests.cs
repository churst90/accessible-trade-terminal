using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// "Near resistance at …" while arrowing the candles, for every kind of level on the chart
/// (Cody, 2026-11-21, choosing option A): pivots, Cipher A's pivot high and low, a profile's
/// point of control and value area, your own horizontal lines and the prices your alerts watch,
/// as well as Cipher SR's zone lines, which were the only ones before. Each names itself after
/// the price, since with six kinds of level the price alone no longer says which line it is.
///
/// <para>
/// The bar is the middle one of three: low 10, high 12, close 11. A level at 12 is above the
/// close (resistance), one at 10 below it (support); 14 is well outside the 0.5% window.
/// </para>
/// </summary>
public sealed class NearLevelTests
{
    private static ChartSeries Candles()
    {
        var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
        var data = new SeriesDataBuffer { SeriesId = config.Id };
        var body = new ComponentConfig { Name = "Body", DisplayName = "Body", IsVisible = true };
        config.Components.Add(body);
        data.ComponentData[body.Name] = new double[] { 0, 0, 0 };
        return new ChartSeries(config, data);
    }

    private static ChartSeries Indicator(string code, string component, double value, bool zoneLine = false)
    {
        var config = new SeriesConfig { Id = code + component, IndicatorCode = code, Name = code, Pane = "Main" };
        var data = new SeriesDataBuffer { SeriesId = config.Id };
        config.Components.Add(new ComponentConfig { Name = component, DisplayName = component, IsVisible = true, IsZoneLine = zoneLine });
        data.ComponentData[component] = new[] { value, value, value };
        return new ChartSeries(config, data);
    }

    private static ChartSeries HorizontalLine(double price) => new(
        new SeriesConfig { Id = "hline", Name = "Horizontal line", Pane = "Main" },
        new SeriesDataBuffer { SeriesId = "hline" })
    {
        Drawing = new DrawingData { Type = DrawingType.HorizontalLine, AnchorPrice1 = price },
    };

    private static ProfileBin Bin(double mid, bool poc, bool valueArea) => new()
    {
        PriceLow = mid - 0.1, PriceHigh = mid + 0.1, TotalVolume = poc ? 100 : 10,
        TpoPeriodCount = 0, IsPOC = poc, IsValueArea = valueArea,
    };

    private static AlertDefinition PriceAlert(double level, string? symbol = null, bool active = true,
        AlertCondition condition = AlertCondition.CrossesAbove) => new()
    {
        Id = Guid.NewGuid().ToString(), Name = "level", Target = AlertTarget.Price,
        Condition = condition, Threshold = level, Delivery = AlertDelivery.Speech,
        Symbol = symbol, IsActive = active,
    };

    /// <summary>Everything said on arriving at the middle bar from the left.</summary>
    private static string Arrive(IEnumerable<ChartSeries> others, params AlertDefinition[] alerts)
        => Arrive(null, others, alerts);

    private static string Arrive(IAppSettings? settings, IEnumerable<ChartSeries> others, params AlertDefinition[] alerts)
    {
        var candles = Candles();
        var bars = new TimeSeriesBuffer<Ohlcv>(
            new Ohlcv(new DateTime(2024, 1, 1), 10, 11, 9, 10, 1),
            new Ohlcv(new DateTime(2024, 1, 2), 10, 12, 10, 11, 1),
            new Ohlcv(new DateTime(2024, 1, 3), 11, 13, 10, 12, 1));
        var state = WorkspaceState.Initial with
        {
            Data = bars,
            ActiveSeries = ImmutableList.Create(candles).AddRange(others),
            FocusedSeriesId = candles.Id,
            CurrentDataIndex = 1,
            InitStatus = InitializationStatus.Ready,
            LastInteractionContext = InteractionContext.Component,
            SymbolDisplayName = "BTC/USD",
        };

        var orchestrator = Substitute.For<IAlertOrchestrator>();
        orchestrator.GetAlerts().Returns(alerts);
        var spy = new SpySpeechRouter();
        new NavigationFeedbackManager(spy, new SpeechFormatter(), settings, orchestrator) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");
        return string.Join(" | ", spy.SpokenTexts.Where(t => t != null));
    }

    private static string Arrive(params ChartSeries[] others) => Arrive(others, Array.Empty<AlertDefinition>());

    [Fact]
    public void Cipher_SRs_zone_line_keeps_its_sentence()
    {
        // Unchanged, and unnamed: it is the support/resistance indicator.
        Assert.Contains("Near resistance at 12.00.", Arrive(Indicator("CIPHER_SR", "Resistance", 12, zoneLine: true)));
    }

    [Theory]
    [InlineData(PivotLevelsProvider.CompR1, 12, "Near resistance at 12.00, pivot R1.")]
    [InlineData(PivotLevelsProvider.CompPP, 10, "Near support at 10.00, pivot point.")]
    [InlineData(PivotLevelsProvider.CompCamL3, 10, "Near support at 10.00, Camarilla L3.")]
    public void A_pivot_line_is_a_level(string component, double at, string expected)
    {
        Assert.Contains(expected, Arrive(Indicator(PivotLevelsProvider.Code, component, at)));
    }

    [Fact]
    public void The_pivot_zone_oscillator_is_not_a_price()
    {
        // +1 / -1, not a price: at 12 by coincidence it must still say nothing.
        Assert.DoesNotContain("Near", Arrive(Indicator(PivotLevelsProvider.Code, PivotLevelsProvider.CompZone, 12)));
    }

    [Fact]
    public void Cipher_As_pivot_high_and_low_are_levels()
    {
        string said = Arrive(
            Indicator("CIPHER_A", CipherAProvider.CompPivotHigh, 12),
            Indicator("CIPHER_A", CipherAProvider.CompPivotLow, 10));
        Assert.Contains("Near resistance at 12.00, Cipher A pivot high.", said);
        Assert.Contains("Near support at 10.00, Cipher A pivot low.", said);
    }

    [Fact]
    public void A_profiles_point_of_control_and_value_area_are_levels()
    {
        var profile = Indicator("VPVR", "Profile", double.NaN);
        profile.ProfileBins = new List<ProfileBin> { Bin(10, poc: false, valueArea: true), Bin(12, poc: true, valueArea: true) };
        string said = Arrive(profile);
        // The POC and the value area high are both at 12; the POC is listed first and wins the tie.
        Assert.Contains("Near resistance at 12.00, point of control.", said);
        Assert.Contains("Near support at 10.00, value area low.", said);
    }

    [Fact]
    public void Your_horizontal_line_is_a_level()
    {
        Assert.Contains("Near resistance at 12.00, your line.", Arrive(HorizontalLine(12)));
    }

    [Fact]
    public void A_price_alert_on_this_symbol_is_a_level()
    {
        Assert.Contains("Near support at 10.00, alert.",
            Arrive(Array.Empty<ChartSeries>(), PriceAlert(10, symbol: "btc/usd", condition: AlertCondition.Touches)));
    }

    [Fact]
    public void An_alert_on_another_symbol_or_switched_off_is_not()
    {
        Assert.DoesNotContain("Near", Arrive(Array.Empty<ChartSeries>(),
            PriceAlert(12, symbol: "ETH/USD"), PriceAlert(10, active: false)));
    }

    [Fact]
    public void An_ordinary_indicator_line_is_not_a_level()
    {
        // The control for every test above: not any line at the right price counts.
        Assert.DoesNotContain("Near", Arrive(Indicator("SMA", "SMA", 12)));
    }

    [Fact]
    public void A_muted_or_far_level_says_nothing()
    {
        var muted = Indicator(PivotLevelsProvider.Code, PivotLevelsProvider.CompR1, 12);
        muted.Config.IsMuted = true;
        Assert.DoesNotContain("Near", Arrive(muted, HorizontalLine(14)));
    }

    [Fact]
    public void The_nearest_level_on_each_side_is_the_one_said()
    {
        // R1 at 12 and your line at 11.5 are both resistance; 11.5 is nearer the close of 11.
        string said = Arrive(Indicator(PivotLevelsProvider.Code, PivotLevelsProvider.CompR1, 12), HorizontalLine(11.5));
        Assert.Contains("Near resistance at 11.50, your line.", said);
        Assert.DoesNotContain("pivot R1", said);
    }

    [Fact]
    public void Levels_are_said_by_default()
    {
        // Through the real settings at their defaults, not the null the other tests pass.
        var settings = TestAppSettings.Defaults();
        Assert.True(settings.AnnounceNearbyLevels);
        Assert.Contains("Near resistance at 12.00, pivot R1.",
            Arrive(settings, new[] { Indicator(PivotLevelsProvider.Code, PivotLevelsProvider.CompR1, 12) }));
    }

    [Fact]
    public void The_setting_off_silences_every_kind_of_level()
    {
        // Cody, 2026-11-22: "want the option to enable or disable that functionality".
        var settings = TestAppSettings.Defaults();
        settings.AnnounceNearbyLevels = false;
        string said = Arrive(settings,
            new[]
            {
                Indicator("CIPHER_SR", "Resistance", 12, zoneLine: true),
                Indicator(PivotLevelsProvider.Code, PivotLevelsProvider.CompPP, 10),
                HorizontalLine(11.9),
            },
            PriceAlert(10.05, symbol: "BTC/USD"));
        Assert.DoesNotContain("Near", said);
        Assert.False(string.IsNullOrWhiteSpace(said)); // the bar itself is still read
    }
}
