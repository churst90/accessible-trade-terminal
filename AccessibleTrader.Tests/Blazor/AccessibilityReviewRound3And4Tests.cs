// The eleven fixes from accessibility review rounds 3 and 4 (2026-11-27; Cody: "yes, proceed to
// address these items 1-11"):
//    1. Brick settings did nothing in the public demo (the dialog sat in the save/load block).
//    2. Settings "Check every (seconds)" corrected 5 to 10 and 900 to 600 in silence.
//    3. The toolbar's Find symbol box never said its match count while typing.
//    4. The Settings search count and the Export CSV result were never said; a failed export
//       was green.
//    5. The Minimize to tray checkbox said it took effect at once; it takes effect on Save.
//    6. Delete theme deleted at once, with no question.
//    7. Dialogs were spoken by internal names: "GoBar", "ShapeChangeWarning", and Kagi's
//       dialog was "Brick settings".
//    8. Fourteen Settings hints were not linked to their fields; one hint was read twice.
//    9. The timeframe quick picks did not say which one was selected.
//   10. The chart-type dialog's error was not tied to its field and was read twice.
//   11. Help named a "Watch" button and an "Indicators button" that do not exist, and listed
//       seven of the fifteen chart types.
// Each is asserted as what a screen reader gets: what is said, what a field is described by,
// its state, and the words on screen. Item 5 is in MinimizeToTraySettingTests.

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.BlazorClient.Components.Layout;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Theming;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests.Blazor;

public class AccessibilityReviewRound3And4Tests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoPaths.RepoRoot(), "AccessibleTrader.BlazorClient.Components" }.Concat(parts).ToArray()));

    private static WorkspaceState Chart(PriceChartStyle style = PriceChartStyle.Candles, int bars = 40)
    {
        var config = new SeriesConfig { Id = "candles", Name = "Candles" };
        return WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, bars)
                .Select(i => new Ohlcv(Start.AddHours(i), 100 + i, 101 + i, 99 + i, 100 + i, 10))),
            ActiveSeries = ImmutableList.Create(new ChartSeries(config, new SeriesDataBuffer { SeriesId = "candles" })),
            FocusedSeriesId = "candles",
            ChartStyle = style,
            CurrentDataShape = ProviderDataShape.Ohlcv,
            CurrentDataIndex = bars - 1,
            ViewportLength = 30,
            InitStatus = InitializationStatus.Ready,
        };
    }

    private static (BlazorTestHarness h, IRenderedComponent<SettingsModal> cut, List<FeedbackRequestEvent> said) OpenSettings(
        Action<BlazorTestHarness>? arrange = null)
    {
        var h = new BlazorTestHarness();
        arrange?.Invoke(h);
        var said = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(e => said.Add(e));
        var cut = h.OpenModal<SettingsModal>(bus => bus.Publish(new OpenSettingsEvent()));
        cut.WaitForAssertion(() => cut.Find("#s-bg-poll"));
        return (h, cut, said);
    }

    // ── 1: Brick settings in the demo ───────────────────────────────────────────────────────

    /// <summary>The (start, end, condition) of every <c>@if (…) { … }</c> block in a Razor file,
    /// comments removed (blanked, so offsets still line up).</summary>
    private static List<(int Start, int End, string Condition)> IfBlocks(string razor)
    {
        var blocks = new List<(int, int, string)>();
        foreach (Match m in Regex.Matches(razor, @"@if\s*\((?<cond>[^{}]*?)\)\s*\{"))
        {
            int depth = 0, i = m.Index + m.Length - 1;
            for (; i < razor.Length; i++)
            {
                if (razor[i] == '{') depth++;
                else if (razor[i] == '}' && --depth == 0) break;
            }
            blocks.Add((m.Index, i, m.Groups["cond"].Value));
        }
        return blocks;
    }

    private static string WithoutComments(string razor) =>
        Regex.Replace(razor, @"@\*.*?\*@", m => new string(' ', m.Length), RegexOptions.Singleline);

    [Fact]
    public void The_chart_type_dialog_is_not_behind_any_demo_gate()
    {
        string layout = WithoutComments(Source("Layout", "MainLayout.razor"));
        var demoBlocks = IfBlocks(layout).Where(b => b.Condition.Contains("Demo.")).ToList();

        // Vacuity: the scan must see the gates the save dialogs really sit behind.
        int save = layout.IndexOf("<SaveWorkspaceModal", StringComparison.Ordinal);
        Assert.True(save > 0, "SaveWorkspaceModal is gone from MainLayout; this scan proves nothing");
        Assert.Contains(demoBlocks, b => b.Start < save && save < b.End);

        int at = layout.IndexOf("<ChartTypeSettingsModal", StringComparison.Ordinal);
        Assert.True(at > 0, "ChartTypeSettingsModal is not in MainLayout at all");
        var gate = demoBlocks.FirstOrDefault(b => b.Start < at && at < b.End);
        Assert.True(gate == default,
            $"ChartTypeSettingsModal is inside @if ({gate.Condition}): the demo offers Renko, Kagi and the rest, and its settings button would do nothing");
    }

    // ── 7: dialogs spoken by their heading ──────────────────────────────────────────────────

    [Fact]
    public void A_dialog_is_spoken_by_its_spoken_name_and_falls_back_to_its_key()
    {
        Assert.Equal("Go dialog opened", MainLayout.ModalPhrase(new ModalStateChangedEvent(true, "GoBar", "Go")));
        Assert.Equal("Kagi settings dialog closed",
            MainLayout.ModalPhrase(new ModalStateChangedEvent(false, "Brick settings", "Kagi settings")));
        Assert.Equal("Settings dialog opened", MainLayout.ModalPhrase(new ModalStateChangedEvent(true, "Settings")));
    }

    [Theory]
    [InlineData(PriceChartStyle.Renko, "Brick settings")]
    [InlineData(PriceChartStyle.RangeBars, "Range bar settings")]
    [InlineData(PriceChartStyle.LineBreak, "Line break settings")]
    [InlineData(PriceChartStyle.Kagi, "Kagi settings")]
    [InlineData(PriceChartStyle.PointAndFigure, "Point and figure settings")]
    public void The_chart_type_dialog_and_its_button_are_named_after_the_chart(PriceChartStyle style, string title)
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => Chart(style));
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
        var events = new List<ModalStateChangedEvent>();
        h.EventBus.Subscribe<ModalStateChangedEvent>(events.Add);

        var dialog = h.Ctx.RenderComponent<ChartTypeSettingsModal>();
        h.EventBus.Publish(new OpenChartTypeSettingsEvent());
        dialog.WaitForAssertion(() => Assert.Contains(events, e => e.IsOpen));

        var opened = events.First(e => e.IsOpen);
        Assert.Equal("Brick settings", opened.ModalName);   // the key Escape and the stack match
        Assert.Equal(title, opened.SpokenName);
        Assert.Contains(title, dialog.Markup);               // what is said is what is shown

        var toolbar = h.Ctx.RenderComponent<Toolbar>();
        Assert.Contains(toolbar.FindAll("button"), b => b.GetAttribute("aria-label") == title);
    }

    [Fact]
    public void The_Go_bar_is_spoken_as_Go()
    {
        using var h = new BlazorTestHarness();
        h.With(new AccessibleTrader.Core.Services.GoBar.GoBarService(
            Substitute.For<ICommandDispatcher>(), h.ShortcutManager, h.EventBus, new ModalStack(h.EventBus)));
        var events = new List<ModalStateChangedEvent>();
        h.EventBus.Subscribe<ModalStateChangedEvent>(events.Add);

        var cut = h.OpenModal<GoBarModal>(bus => bus.Publish(new OpenGoBarEvent()));

        cut.WaitForAssertion(() => Assert.Contains(events, e => e.IsOpen));
        var opened = events.First(e => e.IsOpen);
        Assert.Equal("GoBar", opened.ModalName);
        Assert.Equal("Go dialog opened", MainLayout.ModalPhrase(opened));
    }

    // ── 2: Check every (seconds) ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("5", "10", "Background check seconds must be 10 to 600. Set to 10.")]
    [InlineData("900", "600", "Background check seconds must be 10 to 600. Set to 600.")]
    public void A_poll_outside_10_to_600_is_corrected_said_and_linked(string typed, string shown, string sentence)
    {
        var (h, cut, said) = OpenSettings();
        using var _ = h;

        cut.Find("#s-bg-poll").Change(typed);

        cut.WaitForAssertion(() =>
        {
            var field = cut.Find("#s-bg-poll");
            Assert.Equal(shown, field.GetAttribute("value"));
            Assert.Equal("s-bg-poll-err s-bg-poll-hint", field.GetAttribute("aria-describedby"));
            Assert.Equal(sentence, cut.Find("#s-bg-poll-err").TextContent.Trim());
        });
        Assert.Contains(said, e => e.Message == sentence && e.Type == FeedbackType.Error);
    }

    [Fact]
    public void A_poll_that_is_not_a_number_keeps_the_old_one_and_a_good_one_clears_the_error()
    {
        var (h, cut, said) = OpenSettings();
        using var _ = h;
        string before = cut.Find("#s-bg-poll").GetAttribute("value")!;

        cut.Find("#s-bg-poll").Change("abc");
        cut.WaitForAssertion(() =>
            Assert.Equal($"Background check seconds needs a number from 10 to 600. Kept at {before}.",
                cut.Find("#s-bg-poll-err").TextContent.Trim()));
        Assert.Equal(before, cut.Find("#s-bg-poll").GetAttribute("value"));

        cut.Find("#s-bg-poll").Change("45");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#s-bg-poll-err")));
        Assert.Equal("s-bg-poll-hint", cut.Find("#s-bg-poll").GetAttribute("aria-describedby"));
        Assert.Equal("45", cut.Find("#s-bg-poll").GetAttribute("value"));
    }

    [Fact]
    public void The_poll_label_and_its_registry_row_carry_the_unit()
    {
        var (h, cut, _) = OpenSettings();
        using var __ = h;
        Assert.Equal("Check every (seconds)", cut.Find("label[for='s-bg-poll']").TextContent.Trim());
        var row = AccessibleTrader.Core.Services.Settings.SettingsRegistry.All.Single(r => r.ControlId == "s-bg-poll");
        Assert.Equal("Check every (seconds)", row.Label);
    }

    // ── 3 and 4: counts said after a pause in typing ────────────────────────────────────────

    private static bool Said(List<AnnouncementEvent> spoken, Func<AnnouncementEvent, bool> match) =>
        SpinWait.SpinUntil(() => { lock (spoken) return spoken.Any(match); }, TimeSpan.FromSeconds(5));

    [Fact]
    public void The_Settings_search_says_its_count_once_typing_pauses_politely()
    {
        var (h, cut, _) = OpenSettings();
        using var _h = h;
        var spoken = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(e => { lock (spoken) spoken.Add(e); });

        cut.Find("#s-search").Input("t");
        cut.Find("#s-search").Input("zzqqxx");    // a second keystroke inside the pause

        Assert.True(Said(spoken, e => e.Message == "No matching settings."), "the count was never said");
        Thread.Sleep(PausedTypingReader.Pause * 2);
        lock (spoken)
        {
            var counts = spoken.Where(e => e.Message.Contains("matching setting")).ToList();
            // Only the count for what the user stopped on: the "t" read was replaced.
            var one = Assert.Single(counts);
            Assert.False(one.Interrupt);
        }
        Assert.Equal("No matching settings.", cut.Find("#s-search-count").TextContent.Trim());
    }

    [Theory]
    [InlineData(0, "No matching settings.")]
    [InlineData(1, "1 matching setting.")]
    [InlineData(7, "7 matching settings.")]
    public void The_search_count_reads_as_a_sentence(int n, string sentence) =>
        Assert.Equal(sentence, SettingsModal.SearchCountSentence(n));

    [Fact]
    public void The_symbol_find_box_says_its_matches_once_typing_pauses()
    {
        var list = new List<string> { "DIA", "GLD", "SPY", "SPYG", "SPYV", "TLT", "XSPY" };
        for (int i = 0; list.Count < 14_402; i++) list.Add($"S{i:D5}");
        list.Sort(StringComparer.Ordinal);
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Stock|Spot", "Alpaca", "DIA", "1d"),
            CurrentDataShape = ProviderDataShape.Ohlcv,
        });
        h.MarketOrchestrator.AvailableSymbols.Returns(list);
        h.MarketOrchestrator.SelectedSymbol.Returns("DIA");
        var spoken = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(e => { lock (spoken) spoken.Add(e); });

        var cut = h.Ctx.RenderComponent<Toolbar>();
        cut.Find("#symbol-find").Input("spy");

        Assert.True(Said(spoken, e => e.Message.Contains("4 matches") && !e.Interrupt),
            "the Find symbol count was never said");
        Assert.Equal(cut.Find("#symbol-find-help").TextContent.Trim(),
            spoken.First(e => e.Message.Contains("4 matches")).Message);
    }

    [Fact]
    public void A_failed_export_is_said_as_an_error_and_shown_as_one()
    {
        var (h, cut, said) = OpenSettings();
        using var _ = h;

        cut.Find("button[aria-label='Export CSV of the chart data']").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains(said, e => e.Type == FeedbackType.Error && e.Message!.StartsWith("Nothing exported")));
        var shown = cut.FindAll("p").Single(p => p.TextContent.StartsWith("Nothing exported"));
        Assert.Contains("field-error", shown.GetAttribute("class") ?? "");
        Assert.Null(shown.GetAttribute("role"));   // said once, by the event, not twice
    }

    // ── 6: Delete theme asks first ──────────────────────────────────────────────────────────

    private static ThemePreset CustomTheme(BlazorTestHarness h)
    {
        var mine = ThemePreset.Create("Mine", ThemeType.Paper).With("chartTop", new SKColor(0x12, 0x34, 0x56));
        var library = h.Ctx.Services.GetRequiredService<IThemeLibrary>();
        library.GetById(mine.Id).Returns(mine);
        library.All.Returns(new List<ThemePreset> { mine });
        h.SettingsManager.GetSetting(SettingsKeys.CustomThemeId, Arg.Any<JToken?>()).Returns(new JValue(mine.Id));
        return mine;
    }

    [Fact]
    public void Delete_theme_asks_first_and_deletes_nothing_until_confirmed()
    {
        ThemePreset mine = null!;
        var (h, cut, said) = OpenSettings(x => mine = CustomTheme(x));
        using var _ = h;
        var library = h.Ctx.Services.GetRequiredService<IThemeLibrary>();

        cut.Find("button#s-theme-delete").Click();

        h.WaitForFocus("s-theme-delete-confirm");
        library.DidNotReceive().Remove(Arg.Any<string>());
        Assert.Contains(said, e => e.Message!.StartsWith("Delete the theme Mine?"));
        Assert.Contains("cannot be undone", cut.Find("#s-theme-delete-confirm").GetAttribute("aria-label"));

        cut.Find("button#s-theme-delete-confirm").Click();
        library.Received(1).Remove(mine.Id);
    }

    [Fact]
    public void Cancel_keeps_the_theme_and_puts_focus_back_on_Delete()
    {
        var (h, cut, said) = OpenSettings(x => CustomTheme(x));
        using var _ = h;
        var library = h.Ctx.Services.GetRequiredService<IThemeLibrary>();

        cut.Find("button#s-theme-delete").Click();
        cut.WaitForElement("button#s-theme-delete-cancel").Click();

        h.WaitForFocus("s-theme-delete");
        Assert.Contains(said, e => e.Message == "Cancelled. Mine was not deleted.");
        library.DidNotReceive().Remove(Arg.Any<string>());
    }

    [Fact]
    public void Escape_backs_out_of_the_question_not_the_dialog()
    {
        var (h, cut, said) = OpenSettings(x => CustomTheme(x));
        using var _ = h;
        var library = h.Ctx.Services.GetRequiredService<IThemeLibrary>();

        cut.Find("button#s-theme-delete").Click();
        cut.WaitForElement("button#s-theme-delete-confirm");
        h.EventBus.Publish(new CloseTopModalEvent("Settings"));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("button#s-theme-delete")));
        Assert.NotNull(cut.Find("#s-bg-poll"));    // Settings is still open
        Assert.Contains(said, e => e.Message == "Cancelled. Mine was not deleted.");
        library.DidNotReceive().Remove(Arg.Any<string>());
    }

    // ── 8: hints linked, none read twice ────────────────────────────────────────────────────

    /// <summary>The tray checkbox is desktop-only; the harness's platform is all false.</summary>
    private static void PretendWindowsDesktop(BlazorTestHarness h)
    {
        var platform = Substitute.For<IRuntimePlatform>();
        platform.IsWindows.Returns(true);
        h.Ctx.Services.AddSingleton(platform);
    }

    [Theory]
    [InlineData("s-resume-session")]
    [InlineData("s-bg-monitor")]
    [InlineData("s-bg-live-tabs")]
    [InlineData("s-minimize-to-tray")]
    [InlineData("s-touch-nav")]
    [InlineData("s-magnet-snap")]
    [InlineData("s-swing-snap")]
    [InlineData("s-timestamp-where")]
    [InlineData("s-date-every-bar")]
    [InlineData("s-twelve-hour")]
    [InlineData("s-sound-theme")]
    [InlineData("s-braille-enabled")]
    [InlineData("s-theme")]
    [InlineData("s-trade-status-bar")]
    [InlineData("s-trade-status-figure")]
    public void The_hint_under_a_setting_is_read_with_it(string id)
    {
        var (h, cut, _) = OpenSettings(PretendWindowsDesktop);
        using var __ = h;

        var described = cut.Find("#" + id).GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrWhiteSpace(described), $"#{id} is described by nothing");
        foreach (var target in described!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            Assert.False(string.IsNullOrWhiteSpace(cut.Find("#" + target).TextContent),
                $"#{id} is described by #{target}, which is empty");
    }

    [Fact]
    public void No_two_settings_share_a_hint()
    {
        // Two fields pointing at one hint read it twice in a row, tabbing from one to the next.
        var (h, cut, _) = OpenSettings(PretendWindowsDesktop);
        using var __ = h;

        var targets = cut.FindAll("[aria-describedby]")
            .SelectMany(e => e.GetAttribute("aria-describedby")!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => (Target: t, Owner: e.Id)))
            .GroupBy(x => x.Target)
            .Where(g => g.Select(x => x.Owner).Distinct().Count() > 1)
            .Select(g => $"{g.Key} <- {string.Join(", ", g.Select(x => x.Owner))}")
            .ToList();
        Assert.True(cut.FindAll("[aria-describedby]").Count >= 15, "the scan saw too few fields to prove anything");
        Assert.Empty(targets);
    }

    // ── 9: the selected timeframe is pressed ────────────────────────────────────────────────

    [Fact]
    public void The_selected_timeframe_quick_pick_is_pressed_and_only_it()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => Chart());
        h.MarketOrchestrator.AvailableTimeframes.Returns(new List<string> { "1m", "1h", "1d", "1M" });
        h.MarketOrchestrator.SelectedTimeframe.Returns("1m");

        var cut = h.Ctx.RenderComponent<Toolbar>();

        var picks = cut.FindAll("[aria-label='Timeframe quick picks'] button");
        Assert.Equal(4, picks.Count);
        var pressed = picks.Where(b => b.GetAttribute("aria-pressed") == "true").ToList();
        Assert.Equal("Set timeframe to 1m", Assert.Single(pressed).GetAttribute("aria-label"));
        Assert.All(picks.Except(pressed), b => Assert.Equal("false", b.GetAttribute("aria-pressed")));
    }

    // ── 10: the chart-type dialog's error ───────────────────────────────────────────────────

    [Fact]
    public void A_refusal_marks_its_field_invalid_and_describes_it_with_the_error()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => Chart(PriceChartStyle.Renko));
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
        var said = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(said.Add);

        var cut = h.Ctx.RenderComponent<ChartTypeSettingsModal>();
        h.EventBus.Publish(new OpenChartTypeSettingsEvent());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#renko-atr-length")));
        Assert.Equal("false", cut.Find("#renko-atr-length").GetAttribute("aria-invalid"));
        Assert.Null(cut.Find("#renko-atr-length").GetAttribute("aria-describedby"));
        // The preview changes on every keystroke: a live region read it over and over.
        Assert.Null(cut.Find("#renko-box-preview").GetAttribute("role"));

        cut.Find("#renko-atr-length").Input("200");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Apply").Click();

        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("#renko-atr-length").GetAttribute("aria-invalid")));
        Assert.Equal(ChartTypeSettingsModal.ErrorId, cut.Find("#renko-atr-length").GetAttribute("aria-describedby"));
        var error = cut.Find("#" + ChartTypeSettingsModal.ErrorId);
        Assert.Contains("Renko needs at least 202 bars", error.TextContent);
        // Refuse speaks it; a role=status on the paragraph said it a second time.
        Assert.Null(error.GetAttribute("role"));
        Assert.Single(said, e => e.Message.Contains("Renko needs at least 202 bars"));
    }

    // ── 11: Help names what is really there ─────────────────────────────────────────────────

    [Fact]
    public void Help_names_the_real_buttons()
    {
        string help = Source("HelpModal.razor");
        string toolbar = Source("Toolbar.razor");
        string indicatorBar = Source("IndicatorBar.razor");

        Assert.Contains("<strong>Watch lists</strong> button", help);
        Assert.Contains("Label=\"Watch lists\"", toolbar);
        Assert.Contains("<strong>Add indicator to chart</strong> button on the indicator bar", help);
        Assert.Contains("AriaLabel=\"Add indicator to chart\"", indicatorBar);
        Assert.DoesNotContain("Indicators button in the toolbar", help);
        Assert.DoesNotContain("<strong>Watch</strong> button", help);
    }

    [Fact]
    public void Help_lists_every_chart_type_the_toolbar_offers()
    {
        string help = Source("HelpModal.razor");
        var row = Regex.Match(help, @"<tr><td>Alt\+C</td><td>(?<text>.*?)</td></tr>");
        Assert.True(row.Success, "Help has no Alt+C row");

        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => Chart());
        var names = h.Ctx.RenderComponent<Toolbar>().FindAll("#chart-type-select option")
            .Select(o => o.TextContent.Trim()).ToList();
        Assert.True(names.Count >= 15, $"the Chart type list has {names.Count} options");

        var missing = names.Where(n => !Regex.IsMatch(row.Groups["text"].Value, $@"(^|[:,] ){Regex.Escape(n)}(,|;)")).ToList();
        Assert.Empty(missing);
    }
}
