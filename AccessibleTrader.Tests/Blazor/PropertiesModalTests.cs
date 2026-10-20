// PropertiesModal — bUnit coverage. Per-indicator settings modal that opens
// via OpenPropertiesEvent(seriesId). Reads ActiveSeries from WorkspaceStore;
// does nothing if the supplied series id can't be resolved.

using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class PropertiesModalTests
{
    /// <summary>Build a minimal but valid ChartSeries the modal can clone +
    /// render. Default ChartSeries() seeds Config + Data so this is a
    /// one-liner; we only override FriendlyName so test assertions can match
    /// on the title.</summary>
    private static ChartSeries NewSeries(string id, string friendlyName)
    {
        var config = new SeriesConfig
        {
            Id = id,
            Name = friendlyName,
            FriendlyName = friendlyName,
        };
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = id });
    }

    private static void SeedActiveSeries(BlazorTestHarness h, params ChartSeries[] series)
    {
        var state = WorkspaceState.Initial with
        {
            ActiveSeries = series.ToImmutableList(),
            FocusedSeriesId = series.Length > 0 ? series[0].Id : null,
        };
        h.WorkspaceStore.State.Returns(_ => state);
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.PropertiesModal>
        OpenProperties(BlazorTestHarness h, string? overrideId = null) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.PropertiesModal>(
            bus => bus.Publish(new OpenPropertiesEvent(overrideId)));

    /// <summary>The component always emits a top-level &lt;style&gt; block; presence
    /// of the dialog itself is the closed/open marker.</summary>
    private static bool DialogRendered(IRenderedComponent<AccessibleTrader.BlazorClient.Components.PropertiesModal> cut)
        => cut.FindAll("[role='dialog']").Count > 0;

    [Fact]
    public void PropertiesModal_HiddenByDefault_NoDialog()
    {
        using var h = new BlazorTestHarness();

        var cut = h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.PropertiesModal>();

        Assert.False(DialogRendered(cut));
    }

    [Fact]
    public void PropertiesModal_OpenWithoutFocusedSeries_RemainsHidden()
    {
        using var h = new BlazorTestHarness();
        // Default WorkspaceStore has WorkspaceState.Initial → no ActiveSeries,
        // no FocusedSeriesId. Guard: ShowAsync must early-return.

        var cut = OpenProperties(h);

        Assert.False(DialogRendered(cut));
    }

    [Fact]
    public void PropertiesModal_OpenWithMissingSeriesId_RemainsHidden()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("series-A", "Series A"));

        // Open with an id that doesn't match any active series.
        var cut = OpenProperties(h, overrideId: "does-not-exist");

        Assert.False(DialogRendered(cut));
    }

    [Fact]
    public void PropertiesModal_OpenWithValidSeries_RendersDialog()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        var dialog = cut.WaitForElement("[role='dialog']");
        Assert.Equal("props-title", dialog.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void PropertiesModal_Title_IncludesSeriesFriendlyName()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        var title = cut.WaitForElement("h2#props-title");
        Assert.Contains("RSI 14", title.TextContent);
    }

    [Fact]
    public void PropertiesModal_DefaultActiveTab_IsGeneral()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        Assert.Equal("true", cut.WaitForElement("button#props-tab-general").GetAttribute("aria-selected"));
    }

    [Fact]
    public void PropertiesModal_ClickAppearanceTab_SwitchesActive()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-appearance").Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true",  cut.Find("button#props-tab-appearance").GetAttribute("aria-selected"));
            Assert.Equal("false", cut.Find("button#props-tab-general").GetAttribute("aria-selected"));
        });
    }

    [Fact]
    public void PropertiesModal_ClickSonificationTab_SwitchesActive()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-sonification").Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("button#props-tab-sonification").GetAttribute("aria-selected"));
        });
    }

    [Fact]
    public void PropertiesModal_ClickSpeechTab_SwitchesActive()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-speech").Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("button#props-tab-speech").GetAttribute("aria-selected"));
        });
    }

    /// <summary>
    /// Five tabs since 2026-09-12, when Levels got one of its own. Cody: <i>"levels and zones
    /// should be in the indicator properties shift f12 modal in their own tab."</i> They used to
    /// be the last fieldset on Appearance, below every component's colour and thickness, so
    /// reaching a reference line meant tabbing past all of the styling first — and a reference
    /// line is not styling. It has a value, it chimes when the price crosses it, it roughens the
    /// tone while you are beyond it, and the 0 key switches it off.
    /// </summary>
    [Fact]
    public void PropertiesModal_FiveTabs_AllPresent()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        Assert.NotNull(cut.WaitForElement("button#props-tab-general"));
        Assert.NotNull(cut.Find("button#props-tab-appearance"));
        Assert.NotNull(cut.Find("button#props-tab-levels"));
        Assert.NotNull(cut.Find("button#props-tab-sonification"));
        Assert.NotNull(cut.Find("button#props-tab-speech"));
    }

    /// <summary>
    /// The Levels tab shows the reference levels, and the Appearance tab no longer does — a
    /// control in two places is two places to keep in step, and the whole point of the move is
    /// that the levels are reachable without walking the styling.
    /// </summary>
    [Fact]
    public void PropertiesModal_LevelsTab_HoldsTheReferenceLevels_AndAppearanceDoesNot()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        cut.WaitForElement("button#props-tab-appearance").Click();
        Assert.DoesNotContain("Reference Levels", cut.Find("#props-tabpanel").TextContent);

        cut.Find("button#props-tab-levels").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("button#props-tab-levels").GetAttribute("aria-selected"));
            Assert.Contains("Reference Levels", cut.Find("#props-tabpanel").TextContent);
        });
    }

    /// <summary>
    /// The Levels tab mentions the 0 key only where the key does something (2026-10-09). It used
    /// to say "Or press 0 on the chart" on every series, the candles included — where the key now
    /// answers "No reference level applies".
    /// </summary>
    [Theory]
    [InlineData("Main", null, false)]          // a price-pane series
    [InlineData("Pane_Obv", null, false)]      // its own pane, but no declared neutral
    [InlineData("Pane_Rsi", 50.0, true)]       // its own pane, swinging about 50
    public void PropertiesModal_LevelsTab_OffersTheZeroKey_OnlyWhereItApplies(string pane, double? neutral, bool offered)
    {
        using var h = new BlazorTestHarness();
        var series = NewSeries("s-1", "Series");
        series.Config.Pane = pane;
        series.Config.Components.Add(new ComponentConfig
        {
            Name = "Value", DisplayName = "Value", DisplayType = ComponentDisplayType.Line, IsVisible = true, ReferenceLevel = neutral,
        });
        SeedActiveSeries(h, series);

        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-levels").Click();

        cut.WaitForAssertion(() =>
        {
            string panel = cut.Find("#props-tabpanel").TextContent;
            Assert.Contains("Add a level at the cursor", panel);   // the tab rendered
            Assert.Equal(offered, panel.Contains("press 0", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void PropertiesModal_ApplyButton_PresentWhenOpen()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);

        // Found by id, not by its wording: this dialog's commit button was "Apply Changes"
        // until 2026-09-04 and is "Save" now, because every dialog that can commit says Save.
        var save = cut.Find("button#props-save");
        Assert.Equal("Save", save.TextContent.Trim());
    }

    [Fact]
    public void PropertiesModal_CancelButton_HidesModal()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, NewSeries("rsi-1", "RSI 14"));

        var cut = OpenProperties(h);
        // FindAll immediately after open races the initial render on starved
        // runners — wait for the button to exist before grabbing it.
        cut.WaitForAssertion(() =>
            Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Cancel"));
        var cancel = cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel");

        cancel.Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() => Assert.False(DialogRendered(cut)));
    }

    /// <summary>A series with a directional (Candle) component and a non-directional (Line) one,
    /// for exercising the Sonification tab's per-component patch controls.</summary>
    private static ChartSeries SeriesWithComponents()
    {
        var config = new SeriesConfig { Id = "cs", Name = "Candles", FriendlyName = "Candles" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Body", DisplayName = "Body", DisplayType = ComponentDisplayType.Candle, IsVisible = true,
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "Line", DisplayName = "Line", DisplayType = ComponentDisplayType.Line, IsVisible = true,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "cs" });
    }

    [Fact]
    public void PropertiesModal_SonificationTab_ShowsSoundPatchDropdown()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, SeriesWithComponents());

        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-sonification").Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(cut.FindAll("label"), l => l.TextContent.Contains("Sound Patch"));
        });
    }

    [Fact]
    public void PropertiesModal_GreenRedPatches_ShownForDirectionalComponentOnly()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, SeriesWithComponents()); // one Candle (directional) + one Line (not)

        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-sonification").Click();

        // Exactly one green/red pair — the candle body's; the line omits them.
        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("label"), l => l.TextContent.Contains("Green (bullish) patch"));
            Assert.Single(cut.FindAll("label"), l => l.TextContent.Contains("Red (bearish) patch"));
        });
    }
    // ── Fixed-range profile anchors ──────────────────────────────────────────────

    /// <summary>A Volume Profile (Fixed Range) as SeriesManagementService builds one: the bin
    /// count the user chose, plus the two anchor timestamps it captured from the viewport.</summary>
    private static ChartSeries FixedRangeProfile()
    {
        var config = new SeriesConfig
        {
            Id = "vp", IndicatorCode = "VPFR", Name = "Volume Profile (Fixed Range)",
            FriendlyName = "Volume Profile (Fixed Range)", Pane = "Main",
        };
        config.Parameters["BinCount"] = 50;
        config.Parameters[AccessibleTrader.Core.Services.ProfileAnchoring.AnchorStartParam] =
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        config.Parameters[AccessibleTrader.Core.Services.ProfileAnchoring.AnchorEndParam] =
            new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        config.Components.Add(new ComponentConfig
        {
            Name = "Profile", DisplayName = "Profile", DisplayType = ComponentDisplayType.Distribution, IsVisible = true,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "vp" }) { IsProfile = true };
    }

    [Fact]
    public void FixedRangeProfile_DescribesItsAnchor_InsteadOfListingUnixSecondsAsFields()
    {
        // The anchor is two Unix timestamps the user never typed. They were rendered as number
        // inputs labelled "AnchorStart" and "AnchorEnd" — meaningless to read and one typo away
        // from moving the profile onto a different stretch of chart.
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, FixedRangeProfile());

        var cut = OpenProperties(h);

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("#prop-AnchorStart"));
            Assert.Empty(cut.FindAll("#prop-AnchorEnd"));
            Assert.Single(cut.FindAll("#prop-BinCount"));            // the real knob is still editable
            var text = cut.Find("#prop-anchor-range").TextContent;
            Assert.Contains("Anchored to the bars from", text);
            Assert.Contains("2024", text);
        });
    }

    [Fact]
    public void FixedRangeProfile_KeepsItsAnchor_ThroughApply()
    {
        // Apply rebuilds Parameters from the editable list. With the anchor lifted out of that
        // list, this is the line that would otherwise turn a fixed-range profile into a
        // whole-history one on the first Save.
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, FixedRangeProfile());
        var cut = OpenProperties(h);
        cut.WaitForElement("#props-save");

        cut.Find("#props-save").Click();

        h.WorkspaceStore.Received().Dispatch(Arg.Is<UpdateSeriesAction>(a =>
            a.Series.Single(s => s.Id == "vp").Parameters.ContainsKey(
                AccessibleTrader.Core.Services.ProfileAnchoring.AnchorStartParam)
            && a.Series.Single(s => s.Id == "vp").Parameters.ContainsKey(
                AccessibleTrader.Core.Services.ProfileAnchoring.AnchorEndParam)
            && a.Series.Single(s => s.Id == "vp").Parameters.ContainsKey("BinCount")));
    }

    // ── Pitch follows (PitchMapping), exposed 2026-09-24 ─────────────────────────

    /// <summary>A candle series as the chart builds one: a body that plays the up/down pair,
    /// and a wick whose pitch is fixed whatever its mapping says.</summary>
    private static ChartSeries CandleBodyAndWick()
    {
        var config = new SeriesConfig { Id = "cs", Name = "Candles", FriendlyName = "Candles" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Body", DisplayName = "Body", DisplayType = ComponentDisplayType.Candle,
            Role = ComponentRole.Body, PitchMapping = PitchMapping.PriceDirection, IsVisible = true,
        });
        config.Components.Add(new ComponentConfig
        {
            Name = "Wick", DisplayName = "Wick", DisplayType = ComponentDisplayType.Wick,
            Role = ComponentRole.Wick, IsVisible = true,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "cs" });
    }

    [Fact]
    public void PitchFollows_is_offered_with_the_current_mapping_and_not_for_a_wick()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, CandleBodyAndWick());
        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-sonification").Click();

        cut.WaitForAssertion(() =>
        {
            var select = cut.Find("#son-body-pitch");
            Assert.Equal("PriceDirection", select.GetAttribute("value"));
            Assert.Equal("son-body-pitch-hint", select.GetAttribute("aria-describedby"));
            Assert.Contains("open and close", cut.Find("#son-body-pitch-hint").TextContent);
            Assert.Empty(cut.FindAll("#son-wick-pitch"));
        });
    }

    [Fact]
    public void PitchFollows_the_value_is_saved_to_the_component()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, CandleBodyAndWick());
        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-sonification").Click();

        cut.WaitForElement("#son-body-pitch").Change("Value");
        cut.WaitForAssertion(() =>
            Assert.Contains("moves when you pan or zoom", cut.Find("#son-body-pitch-hint").TextContent));
        cut.Find("#props-save").Click();

        h.WorkspaceStore.Received().Dispatch(Arg.Is<UpdateSeriesAction>(a =>
            a.Series.Single(s => s.Id == "cs").Components.Single(c => c.Name == "Body").PitchMapping
                == PitchMapping.Value));
    }

    // ── Background tint, 2026-10-17 ──────────────────────────────────────────────

    /// <summary>A component that tints the price chart the way Top/Bottom Detector's Distribution
    /// Confidence does: at or above 0.5, nothing below, deepening to 1.0.</summary>
    private static ChartSeries TintingSeries()
    {
        var config = new SeriesConfig { Id = "tbd", Name = "Top/Bottom Detector", FriendlyName = "Top/Bottom Detector" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Distribution Confidence", DisplayName = "Distribution Confidence",
            DisplayType = ComponentDisplayType.Oscillator, IsVisible = true,
            BackgroundTint = BackgroundTint.PriceChart,
            TintColorAboveHex = "#20EF5350", TintColorBelowHex = "#00000000",
            TintMidline = 0.5, TintFullAt = 1.0,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "tbd" });
    }

    /// <summary>
    /// Appearance offers Background tint as a labelled select: the visible label is attached to
    /// it, its accessible name keeps those words and adds the component, and a hint read through
    /// aria-describedby says in words where it shades, on which side of which value, and that it
    /// is visual only — so the choice never rests on a colour. It shows the tint in effect.
    /// </summary>
    [Fact]
    public void BackgroundTint_is_a_labelled_select_with_a_spoken_hint()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, TintingSeries());
        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-appearance").Click();

        cut.WaitForAssertion(() =>
        {
            var select = cut.Find("select#app-distribution-confidence-tint");
            Assert.Equal("Background tint", cut.Find("label[for='app-distribution-confidence-tint']").TextContent.Trim());
            Assert.Equal("Background tint for Distribution Confidence", select.GetAttribute("aria-label"));
            Assert.Equal("PriceChart", select.GetAttribute("value"));
            Assert.Equal(new[] { "Off", "This pane", "Price chart" },
                select.QuerySelectorAll("option").Select(o => o.TextContent.Trim()).ToArray());
            Assert.Equal("app-distribution-confidence-tint-hint", select.GetAttribute("aria-describedby"));
            var hint = cut.Find("#app-distribution-confidence-tint-hint").TextContent;
            Assert.Contains("the price chart", hint);
            Assert.Contains("at or above 0.5", hint);
            Assert.Contains("deepening up to 1", hint);
            Assert.Contains("Visual only", hint);
        });
    }

    /// <summary>Choosing a tint records the USER's choice (UserBackgroundTint, which the workspace
    /// keeps) and leaves the indicator's own default alone; the hint follows the choice.</summary>
    [Fact]
    public void Choosing_a_background_tint_is_saved_as_the_users_choice()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, TintingSeries());
        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-appearance").Click();

        cut.WaitForElement("select#app-distribution-confidence-tint").Change("ThisPane");
        cut.WaitForAssertion(() =>
            Assert.Contains("this component's own pane", cut.Find("#app-distribution-confidence-tint-hint").TextContent));
        cut.Find("select#app-distribution-confidence-tint").Change("Off");
        cut.WaitForAssertion(() =>
            Assert.StartsWith("Off", cut.Find("#app-distribution-confidence-tint-hint").TextContent.Trim()));
        cut.Find("#props-save").Click();

        h.WorkspaceStore.Received().Dispatch(Arg.Is<UpdateSeriesAction>(a =>
            a.Series.Single(s => s.Id == "tbd").Components.Single().UserBackgroundTint == BackgroundTint.Off
            && a.Series.Single(s => s.Id == "tbd").Components.Single().BackgroundTint == BackgroundTint.PriceChart));
    }

    /// <summary>A ribbon-like series: two lines (one labelled on the axis by its indicator, one not)
    /// and a marker, which carries no axis label and so is offered no switch.</summary>
    private static ChartSeries RibbonSeries()
    {
        var config = new SeriesConfig { Id = "ca", Name = "Cipher A", FriendlyName = "Cipher A" };
        config.Components.Add(new ComponentConfig { Name = "EMA 5", DisplayName = "EMA 5", DisplayType = ComponentDisplayType.Line, IsVisible = true });
        config.Components.Add(new ComponentConfig { Name = "EMA 11", DisplayName = "EMA 11", DisplayType = ComponentDisplayType.Line, IsVisible = true, ShowAxisLabel = false });
        config.Components.Add(new ComponentConfig { Name = "Green Dot", DisplayName = "Green Dot", DisplayType = ComponentDisplayType.Dot, IsVisible = true });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "ca" });
    }

    /// <summary>
    /// Properties → Appearance → "Value label on the axis" (2026-10-18): a checkbox per line,
    /// named with its component for a screen reader's form-field list, checked as the label is
    /// drawn (the indicator's default until the user chooses); not offered on a marker.
    /// </summary>
    [Fact]
    public void Value_label_on_the_axis_is_offered_per_line_with_its_component_in_its_name()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, RibbonSeries());
        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-appearance").Click();

        cut.WaitForAssertion(() =>
        {
            var five = cut.Find("input#app-ema-5-axis-label");
            Assert.Equal("checkbox", five.GetAttribute("type"));
            Assert.Equal("Value label on the axis for EMA 5", five.GetAttribute("aria-label"));
            Assert.Equal("Value label on the axis", cut.Find("label[for='app-ema-5-axis-label']").TextContent.Trim());
            Assert.True(five.HasAttribute("checked"));
            Assert.False(cut.Find("input#app-ema-11-axis-label").HasAttribute("checked"));
            Assert.Empty(cut.FindAll("input#app-green-dot-axis-label"));
        });
    }

    /// <summary>Ticking the box records the USER's choice (UserShowAxisLabel, which the workspace
    /// keeps) and leaves the indicator's own default alone.</summary>
    [Fact]
    public void Choosing_the_axis_label_is_saved_as_the_users_choice()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, RibbonSeries());
        var cut = OpenProperties(h);
        cut.WaitForElement("button#props-tab-appearance").Click();

        cut.WaitForElement("input#app-ema-11-axis-label").Change(true);
        cut.WaitForElement("input#app-ema-5-axis-label").Change(false);
        cut.Find("#props-save").Click();

        h.WorkspaceStore.Received().Dispatch(Arg.Is<UpdateSeriesAction>(a =>
            a.Series.Single(s => s.Id == "ca").Components.Single(c => c.Name == "EMA 11").UserShowAxisLabel == true
            && a.Series.Single(s => s.Id == "ca").Components.Single(c => c.Name == "EMA 11").ShowAxisLabel == false
            && a.Series.Single(s => s.Id == "ca").Components.Single(c => c.Name == "EMA 5").UserShowAxisLabel == false
            && a.Series.Single(s => s.Id == "ca").Components.Single(c => c.Name == "Green Dot").UserShowAxisLabel == null));
    }

    private static ChartSeries SeriesWithPreferenceChoices()
    {
        const string id = "preference-choices";
        var config = new SeriesConfig
        {
            Id = id,
            Name = "Preference choices",
            FriendlyName = "Preference choices",
            IndicatorCode = "PREFERENCE_CHOICES",
        };
        config.Components.Add(new ComponentConfig
        {
            Name = "Chosen",
            DisplayType = ComponentDisplayType.Line,
            UserShowAxisLabel = false,
            UserBackgroundTint = BackgroundTint.PriceChart,
        });
        config.Components.Add(new ComponentConfig { Name = "Untouched", DisplayType = ComponentDisplayType.Line });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = id });
    }

    // Reflection keeps the pre-fix Save as Defaults test compilable before these fields are added.
    private static object? PreferenceValue(ComponentPreference preference, string propertyName) =>
        typeof(ComponentPreference).GetProperty(propertyName)?.GetValue(preference);

    [Fact]
    public void Save_as_defaults_copies_both_choices_and_keeps_unset_choices_null()
    {
        using var h = new BlazorTestHarness();
        SeedActiveSeries(h, SeriesWithPreferenceChoices());
        var cut = OpenProperties(h);
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"),
            b => b.TextContent.Trim() == "Save as Defaults"));

        List<ComponentPreference>? saved = null;
        h.IndicatorPreferences
            .When(s => s.SavePreferences(Arg.Any<string>(), Arg.Any<List<ComponentPreference>>()))
            .Do(call => saved = call.ArgAt<List<ComponentPreference>>(1));
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save as Defaults").Click();

        h.IndicatorPreferences.Received(1).SavePreferences("PREFERENCE_CHOICES", Arg.Any<List<ComponentPreference>>());
        var chosen = Assert.Single(saved!, p => p.Name == "Chosen");
        Assert.Equal(false, PreferenceValue(chosen, nameof(ComponentConfig.UserShowAxisLabel)));
        Assert.Equal(BackgroundTint.PriceChart, PreferenceValue(chosen, nameof(ComponentConfig.UserBackgroundTint)));

        var untouched = Assert.Single(saved!, p => p.Name == "Untouched");
        Assert.Null(PreferenceValue(untouched, nameof(ComponentConfig.UserShowAxisLabel)));
        Assert.Null(PreferenceValue(untouched, nameof(ComponentConfig.UserBackgroundTint)));
    }
}
