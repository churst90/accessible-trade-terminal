using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// Phase D visual accessibility, all OPT-IN and OFF BY DEFAULT — the terminal
/// presents audio-first; these pins guarantee (1) nothing visual activates
/// until the user asks, and (2) when asked, the visual channel mirrors the
/// audio one exactly.
/// </summary>
public class VisualAccessibilityTests
{
    // ── Visual earcons ───────────────────────────────────────────────────────

    private static (ISonificationManager Sonify, ISoundPatchLibrary Lib, SpyEventBus Bus) EarconDeps()
    {
        var sonify = Substitute.For<ISonificationManager>();
        sonify.IsEnabled.Returns(true);
        var lib = Substitute.For<ISoundPatchLibrary>();
        lib.EarconOverrides.Returns(new EarconSettings());
        return (sonify, lib, new SpyEventBus());
    }

    [Fact]
    public void EveryEarcon_AlsoPublishesAVisualEvent_WithTheSameCadence()
    {
        var (sonify, lib, bus) = EarconDeps();
        var svc = new EarconService(sonify, lib, bus);

        svc.PlayOrderFill(OrderSide.Buy);
        svc.PlayStopHit();
        svc.PlayTakeProfitHit();

        var visuals = bus.Log.OfType<EarconVisualEvent>().ToList();
        Assert.Equal(3, visuals.Count);
        Assert.Contains(visuals, v => v.Label == "Buy order filled" && v.Tone == "positive");
        Assert.Contains(visuals, v => v.Label == "Stop loss hit" && v.Tone == "alert");
        Assert.Contains(visuals, v => v.Label == "Take profit hit" && v.Tone == "positive");
    }

    [Fact]
    public void VisualEvent_RespectsTheSameThrottleAsTheAudio()
    {
        // Two PlayInfo calls inside the 200 ms window → one tone, one badge.
        var (sonify, lib, bus) = EarconDeps();
        var svc = new EarconService(sonify, lib, bus);

        svc.PlayInfo();
        svc.PlayInfo();

        Assert.Single(bus.Log.OfType<EarconVisualEvent>());
    }

    [Fact]
    public void VisualEvent_StillFires_WhenChartSonificationIsDisabled()
    {
        // CONTRACT CHANGE 2026-07-21 (mute-tier redesign): F3 owns chart
        // sonification only; earcons have their own tier (Shift+F3). An earcon —
        // and its visual mirror — must survive sonification-off. Before this,
        // earcons silently died with F3.
        var (sonify, lib, bus) = EarconDeps();
        sonify.IsEnabled.Returns(false);
        var svc = new EarconService(sonify, lib, bus);

        svc.PlayStopHit();

        Assert.Single(bus.Log.OfType<EarconVisualEvent>());
    }

    [Fact]
    public void EarconService_WithNoEventBus_StillPlaysAudio_WithoutThrowing()
    {
        // The two-arg construction (tests, manual composition) must keep working.
        var (sonify, lib, _) = EarconDeps();
        new EarconService(sonify, lib).PlaySuccess();
        sonify.Received().PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
            Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
    }

    // ── Theme accessibility overrides ────────────────────────────────────────

    private static ISettingsManager SettingsWith(bool colorVision = false, bool hollow = false)
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(ThemeService.ColorVisionSafeKey, Arg.Any<JToken?>())
            .Returns(colorVision ? new JValue(true) : null);
        settings.GetSetting(ThemeService.HollowUpCandlesKey, Arg.Any<JToken?>())
            .Returns(hollow ? new JValue(true) : null);
        return settings;
    }

    [Fact]
    public void Retired_colour_overrides_are_ignored()
    {
        // Until 2026-09-03 eight app-level colours were layered over EVERY theme: a chart
        // background, a gradient end, a unified window fade with two ends, and an up/down candle
        // pair. They were retired with the settings restructure — a colour is a property of a
        // theme now, edited in the theme editor — and a value still sitting in settings.json
        // under one of the old paths must change nothing, or a user would carry an override they
        // have no control left to clear. The paths are literals on purpose: the constants are gone.
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        settings.GetSetting("appearance.backgroundColor", Arg.Any<JToken?>()).Returns(new JValue("#123456"));
        settings.GetSetting("appearance.backgroundGradient", Arg.Any<JToken?>()).Returns(new JValue(true));
        settings.GetSetting("appearance.backgroundColor2", Arg.Any<JToken?>()).Returns(new JValue("#654321"));
        settings.GetSetting("appearance.bullishColor", Arg.Any<JToken?>()).Returns(new JValue("#00A2FF"));
        settings.GetSetting("appearance.bearishColor", Arg.Any<JToken?>()).Returns(new JValue("#FF7700"));
        settings.GetSetting("appearance.unifiedGradient", Arg.Any<JToken?>()).Returns(new JValue(true));
        settings.GetSetting("appearance.unifiedGradientTop", Arg.Any<JToken?>()).Returns(new JValue("#909090"));
        settings.GetSetting("appearance.unifiedGradientBottom", Arg.Any<JToken?>()).Returns(new JValue("#101010"));

        var svc = new ThemeService(settings);
        svc.SetTheme(ThemeType.HighContrastDark);   // flat black; its own white/red candle pair

        var own = new ThemeService(SettingsWith());
        own.SetTheme(ThemeType.HighContrastDark);

        Assert.Equal(own.Current.Background,            svc.Current.Background);
        Assert.Equal(own.Current.BackgroundGradientEnd, svc.Current.BackgroundGradientEnd);
        Assert.Equal(own.Current.CandleBullishBody,     svc.Current.CandleBullishBody);
        Assert.Equal(own.Current.CandleBearishBody,     svc.Current.CandleBearishBody);
        Assert.Equal(own.Current.SurfaceRaised,         svc.Current.SurfaceRaised);
        Assert.Equal(own.Current.ChromeBottomEnd,       svc.Current.ChromeBottomEnd);
    }

    [Fact]
    public void Theme_Defaults_HaveBothOverridesOff()
    {
        var theme = new ThemeService(SettingsWith()).Current;
        Assert.False(theme.ColorVisionSafe);
        Assert.False(theme.HollowUpCandles);
    }

    [Fact]
    public void Theme_PicksUpOverrides_FromSettings_AndKeepsThemAcrossThemeSwitches()
    {
        var svc = new ThemeService(SettingsWith(colorVision: true, hollow: true));
        Assert.True(svc.Current.ColorVisionSafe);
        Assert.True(svc.Current.HollowUpCandles);

        // Switching theme must not silently drop the accessibility overrides.
        svc.SetTheme(ThemeType.Solarized);
        Assert.True(svc.Current.ColorVisionSafe);
        Assert.True(svc.Current.HollowUpCandles);
        Assert.Equal(ThemeType.Solarized, svc.Current.ThemeType);
    }

    [Fact]
    public void RefreshAccessibilityOverrides_ReReadsSettings_AndFiresThemeChanged()
    {
        var settings = Substitute.For<ISettingsManager>();
        JToken? colorVision = null;
        settings.GetSetting(ThemeService.ColorVisionSafeKey, Arg.Any<JToken?>())
            .Returns(_ => colorVision);
        var svc = new ThemeService(settings);
        Assert.False(svc.Current.ColorVisionSafe);

        bool fired = false;
        svc.ThemeChanged += (_, _) => fired = true;
        colorVision = new JValue(true); // the Settings dialog just flipped the toggle
        svc.RefreshAccessibilityOverrides();

        Assert.True(fired);
        Assert.True(svc.Current.ColorVisionSafe);
    }

    // ── Renderer color-vision override ───────────────────────────────────────

    private static AccessibleTrader.Sdk.Theming.ChartTheme ThemeWith(bool colorVisionSafe)
    {
        var svc = new ThemeService(SettingsWith());
        return svc.Current with { ColorVisionSafe = colorVisionSafe };
    }

    [Fact]
    public void ApplyColorVision_Off_PassesComponentColorsThroughUntouched()
    {
        var (up, down) = StandardRenderers.ApplyColorVision(
            ThemeWith(false), SKColors.Green, SKColors.Red);
        Assert.Equal(SKColors.Green, up);
        Assert.Equal(SKColors.Red, down);
    }

    [Fact]
    public void ApplyColorVision_On_ReplacesWithBlueUpOrangeDown()
    {
        var (up, down) = StandardRenderers.ApplyColorVision(
            ThemeWith(true), SKColors.Green, SKColors.Red);
        Assert.Equal(StandardRenderers.ColorVisionUp, up);
        Assert.Equal(StandardRenderers.ColorVisionDown, down);
        // Blue up / orange down — deliberately NOT a red or green in either slot.
        Assert.True(up.Blue > up.Red && up.Blue > up.Green, "up must read as blue");
        Assert.True(down.Red > down.Blue && down.Green > down.Blue, "down must read as orange");
    }
}
