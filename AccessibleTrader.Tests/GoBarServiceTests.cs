using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The words and the sequencing: the polite suggestion sentence, the key from the live
/// profile, the key-only refusal, and "closed, then dispatched".
/// </summary>
public class GoBarServiceTests
{
    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private static (GoBarService Service, ICommandDispatcher Dispatcher, SpyEventBus Bus,
                    ShortcutManager Shortcuts) Build(
        DemoPolicy? demo = null,
        IRuntimePlatform? platform = null,
        IBrowserSpeechOutput? speech = null)
    {
        var dir = TestTemp.NewDir("att-gobar-service-");
        var shortcuts = new ShortcutManager(new TempPaths(dir));
        var bus = new SpyEventBus();
        var stack = new ModalStack(bus);
        var dispatcher = Substitute.For<ICommandDispatcher>();
        return (new GoBarService(dispatcher, shortcuts, bus, stack,
            demo: demo, platform: platform, browserSpeech: speech), dispatcher, bus, shortcuts);
    }

    private static GoBarOption Option(SystemCommand command)
    {
        var entry = GoBarRegistry.All.Single(e => e.Command == command);
        return new GoBarOption(entry.Name, Entry: entry,
            CanonicalText: entry.Codes.Count > 0 ? entry.Codes[0] : entry.Name,
            Help: entry.Help);
    }

    [Fact]
    public void ACommandTypedByNameLeadsAndNamesItsKey()
    {
        var (service, _, _, _) = Build();

        var suggestion = service.Suggest("pulse");

        // Stage 6c lists the setting "Pulse: crypto change measured from" after the command;
        // the command is still first and the sentence is about it.
        var command = Assert.Single(suggestion.Options,
            o => o.Entry?.Command == SystemCommand.MarketPulse);
        Assert.Equal("Market pulse", command.Name);
        Assert.StartsWith("Market pulse, Alt plus P.", suggestion.Sentence);
    }

    [Fact]
    public void TwoMatchesUseTheSingularMore()
    {
        var (service, _, _, _) = Build();

        var suggestion = service.Suggest("load");

        Assert.True(suggestion.Options.Count >= 2,
            "fixture floor: 'load' should find at least Load a workspace and Load the selected chart, found "
            + string.Join(", ", suggestion.Options.Select(m => m.Entry!.Command)));
        Assert.StartsWith("Load a workspace, Control plus Alt plus W.", suggestion.Sentence);
        Assert.EndsWith($"{suggestion.Options.Count - 1} more.", suggestion.Sentence);
        if (suggestion.Options.Count == 2)
            Assert.Contains(" 1 more.", suggestion.Sentence);
    }

    [Fact]
    public void ManyMatchesCountTheRest()
    {
        var (service, _, _, _) = Build();

        var suggestion = service.Suggest("pl");

        Assert.True(suggestion.Options.Count > 2,
            "fixture floor: 'pl' should find more than two commands");
        Assert.EndsWith($" {suggestion.Options.Count - 1} more.", suggestion.Sentence);
    }

    [Fact]
    public void TheSpokenKeyComesFromTheLiveProfile()
    {
        var (service, _, _, shortcuts) = Build();

        shortcuts.UpdateBinding(SystemCommand.MarketPulse, "Q", alt: true);

        var suggestion = service.Suggest("pulse");

        Assert.StartsWith("Market pulse, Alt plus Q.", suggestion.Sentence);
    }

    [Fact]
    public void KeyOnlySpeaksTheKeyAndDoesNotDispatch()
    {
        var (service, dispatcher, bus, _) = Build();

        service.Run(Option(SystemCommand.CloseModal));

        var spoken = Assert.Single(bus.Log.OfType<AnnouncementEvent>());
        Assert.Equal("Close the dialog is a key, Escape; it does nothing from the Go bar.",
                     spoken.Message);
        Assert.Equal(SpeechChannel.Interface, spoken.Channel);
        dispatcher.DidNotReceiveWithAnyArgs().DispatchFromGoBar(default);
    }

    [Fact]
    public void ANormalCommandIsDispatchedOnlyAfterTheBoxHasClosed()
    {
        var (service, dispatcher, bus, _) = Build();
        bus.Publish(new ModalStateChangedEvent(true, "GoBar"));

        service.Run(Option(SystemCommand.MarketPulse));

        Assert.Contains(bus.Log, e => e is CloseGoBarRequestEvent);
        dispatcher.DidNotReceiveWithAnyArgs().DispatchFromGoBar(default);

        bus.Publish(new ModalStateChangedEvent(false, "GoBar"));

        dispatcher.Received(1).DispatchFromGoBar(SystemCommand.MarketPulse);
        Assert.Empty(bus.Log.OfType<AnnouncementEvent>()); // no refusal spoken
    }

    [Fact]
    public void ADialogStillOpenUnderneathIsNamedInTheRefusal()
    {
        var (service, dispatcher, bus, _) = Build();
        bus.Publish(new ModalStateChangedEvent(true, "GoBar"));
        bus.Publish(new ModalStateChangedEvent(true, "Settings"));

        service.Run(Option(SystemCommand.MarketPulse));
        bus.Publish(new ModalStateChangedEvent(false, "GoBar"));

        dispatcher.DidNotReceiveWithAnyArgs().DispatchFromGoBar(default);
        var spoken = bus.Log.OfType<AnnouncementEvent>().Last();
        Assert.Equal("Market pulse can't run while Settings is open.", spoken.Message);
        Assert.Equal(SpeechChannel.Interface, spoken.Channel);
    }

    [Fact]
    public void NothingMatchesSaysTheTypedTextBack()
    {
        var (service, _, _, _) = Build();

        var suggestion = service.Suggest("zzzz");

        Assert.Empty(suggestion.Options);
        Assert.Equal("Nothing matches zzzz.", suggestion.Sentence);
    }
}

/// <summary>
/// Stage 6c's Go bar surface: settings by label, SET and HELP as closed forms, and the typed
/// money commands through the same suggestion list. No dashboard and no trading service is
/// built here; a money option must publish a request and nothing else.
/// </summary>
public class GoBarSettingsAndMoneyTests
{
    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private static (GoBarService Service, ICommandDispatcher Dispatcher, SpyEventBus Bus)
        Build(DemoPolicy? demo = null, IRuntimePlatform? platform = null)
    {
        var shortcuts = new ShortcutManager(new TempPaths(TestTemp.NewDir("att-gobar-6c-")));
        var bus = new SpyEventBus();
        var stack = new ModalStack(bus);
        var dispatcher = Substitute.For<ICommandDispatcher>();
        return (new GoBarService(dispatcher, shortcuts, bus, stack,
            demo: demo ?? new DemoPolicy(isDemo: false),
            platform: platform), dispatcher, bus);
    }

    private static IRuntimePlatform Browser()
    {
        var platform = Substitute.For<IRuntimePlatform>();
        platform.IsBrowserHost.Returns(true);
        return platform;
    }

    // ── Settings by name ───────────────────────────────────────────────────

    [Fact]
    public void SetAloneAndHelpAloneAreTheSixACommands()
    {
        var (service, _, _) = Build();

        Assert.Equal(SystemCommand.OpenSettings, Assert.Single(service.Suggest("SET").Options).Entry?.Command);
        Assert.Equal(SystemCommand.OpenHelp, Assert.Single(service.Suggest("HELP").Options).Entry?.Command);
    }

    [Fact]
    public void SetTextOffersOnlySettingsAndSpeaksTheSettingShape()
    {
        var (service, _, _) = Build();

        var suggestion = service.Suggest("SET text");

        Assert.NotEmpty(suggestion.Options);
        Assert.All(suggestion.Options, o => Assert.IsType<GoBarSettingAction>(o.Action));
        Assert.DoesNotContain(suggestion.Options, o => o.Entry != null);
        var size = suggestion.Options.Single(o => o.Name == "Text size");
        Assert.StartsWith("Text size, setting, Appearance tab.", suggestion.Sentence);
        Assert.Equal("SET Text size", size.CanonicalText);
    }

    [Fact]
    public void SettingsComeAfterEveryCommandForPlainText()
    {
        var (service, _, _) = Build();

        var options = service.Suggest("pan").Options.ToList();

        Assert.Equal(SystemCommand.PanLeft, options[0].Entry?.Command);
        var panning = options.SingleOrDefault(o => o.Action is GoBarSettingAction { Entry.Label: "Panning step" });
        Assert.NotNull(panning);
        Assert.True(options.IndexOf(panning!) > 0);
    }

    [Fact]
    public void AKeywordRanksBelowEveryLabelMatch()
    {
        var (service, _, _) = Build();

        var options = service.Suggest("SET sound").Options;

        var names = options.Select(o => o.Name).ToList();
        int firstKeyword = names.IndexOf("Sonification enabled");
        Assert.True(firstKeyword > 0, "the keyword match should still be offered");
        Assert.All(names.Take(firstKeyword), n =>
            Assert.Contains("sound", n, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnavailableSettingsAreNotOffered()
    {
        var (service, _, _) = Build(demo: new DemoPolicy(isDemo: false), platform: Browser());

        var suggestion = service.Suggest("SET braille");

        Assert.DoesNotContain(suggestion.Options,
            o => o.Action is GoBarSettingAction { Entry.ControlId: "s-braille-enabled" });
    }

    [Fact]
    public void SetWithNoMatchSaysNoSettingMatches()
    {
        var (service, _, _) = Build();

        var suggestion = service.Suggest("SET zzzz");

        Assert.Empty(suggestion.Options);
        Assert.Equal("No setting matches zzzz.", suggestion.Sentence);
    }

    [Fact]
    public void ChoosingASettingPublishesItsTabAndControl()
    {
        var (service, _, bus) = Build();

        var option = service.Suggest("SET text size").Options.First(o => o.Name == "Text size");
        service.Run(option);

        var opened = Assert.Single(bus.Log.OfType<OpenSettingsEvent>());
        Assert.Equal("Appearance", opened.Tab);
        Assert.Equal("s-ui-scale", opened.ControlId);
    }

    // ── HELP never runs ────────────────────────────────────────────────────

    [Fact]
    public void HelpPulseSpeaksItsNameCodesKeyAndHelpLine()
    {
        var (service, dispatcher, bus) = Build();

        var option = Assert.Single(service.Suggest("HELP pulse").Options,
            o => o.Name.StartsWith("Market pulse.", StringComparison.Ordinal));
        Assert.IsType<GoBarHelpAction>(option.Action);
        Assert.Equal(
            "Market pulse. Codes PULSE. Key Alt plus P. "
            + "The Pulse watchlist spoken as one sentence.",
            option.Name);

        service.Run(option);

        var spoken = Assert.Single(bus.Log.OfType<AnnouncementEvent>());
        Assert.Equal(option.Name, spoken.Message);
        Assert.Equal(SpeechChannel.Interface, spoken.Channel);
        dispatcher.DidNotReceiveWithAnyArgs().DispatchFromGoBar(default);
        Assert.Empty(bus.Log.OfType<CloseGoBarRequestEvent>());
    }

    [Fact]
    public void HelpLeavesOutTheKeyWhenThereIsNoBinding()
    {
        var (service, _, _) = Build();

        var option = Assert.Single(service.Suggest("HELP GP").Options);

        Assert.Equal(
            "Change the symbol. Codes SYM, GP. Change the symbol in the toolbar.",
            option.Name);
    }

    [Fact]
    public void HelpNamesASettingAndHowToReachIt()
    {
        var (service, _, _) = Build();

        var options = service.Suggest("HELP text size").Options;

        var setting = Assert.Single(options, o => o.Name.Contains("a setting on the", StringComparison.Ordinal));
        Assert.Equal(
            "Text size, a setting on the Appearance tab. Type SET Text size to go there.",
            setting.Name);
    }

    [Fact]
    public void HelpBuyExplainsAndRunsNothing()
    {
        var (service, _, bus) = Build();

        var option = Assert.Single(service.Suggest("HELP BUY").Options);
        Assert.Contains("Fill in a buy order on the ticket.", option.Name, StringComparison.Ordinal);

        service.Run(option);

        Assert.Single(bus.Log.OfType<AnnouncementEvent>());
        Assert.Empty(bus.Log.OfType<OpenTradingDashboardEvent>());
        Assert.Empty(bus.Log.OfType<OpenHelpEvent>());
        Assert.Empty(bus.Log.OfType<CloseGoBarRequestEvent>());
    }

    [Fact]
    public void HelpWithNoMatchSaysNoCommandOrSettingMatches()
    {
        var (service, _, _) = Build();

        var suggestion = service.Suggest("HELP zzzz");

        Assert.Empty(suggestion.Options);
        Assert.Equal("No command or setting matches zzzz.", suggestion.Sentence);
    }

    // ── Typed money ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("BYU 1")]
    [InlineData("BU")]
    [InlineData("SEL 2")]
    [InlineData("FLA")]
    [InlineData("BUYY")]
    [InlineData("FLATT")]
    public void ANearMissOrPrefixNeverProducesAMoneyOption(string typed)
    {
        var (service, _, _) = Build();

        var suggestion = service.Suggest(typed);

        Assert.DoesNotContain(suggestion.Options,
            o => o.Action is GoBarMoneyAction || o.Entry?.Kind == GoBarKind.Money);
    }

    [Theory]
    [InlineData("BUY", "Buy, fill in the ticket")]
    [InlineData("buy", "Buy, fill in the ticket")]
    [InlineData("BUY 0.5", "Buy 0.5 at market")]
    [InlineData("BUY 0.5 at 95000", "Buy 0.5 at limit 95000")]
    [InlineData("SELL 2", "Sell 2 at market")]
    [InlineData("FLAT", "Find the chart's open position")]
    public void TheMoneyOptionNamesWhatWillBeFilledIn(string typed, string expectedName)
    {
        var (service, _, _) = Build();

        var option = Assert.Single(service.Suggest(typed).Options);

        Assert.Equal(expectedName, option.Name);
        Assert.IsType<GoBarMoneyAction>(option.Action);
    }

    [Fact]
    public void AMoneyRefusalIsTheOnlyAnswer()
    {
        var (service, dispatcher, bus) = Build();

        var suggestion = service.Suggest("BUY 0");

        Assert.Empty(suggestion.Options);
        Assert.Equal(
            "BUY takes a quantity and an optional limit price, like BUY 0.5 or BUY 0.5 95000. "
            + "Nothing was opened.",
            suggestion.Sentence);

        // Nothing to run, so Enter can only say the sentence: no money event, no dispatch.
        Assert.Empty(bus.Log.OfType<OpenTradingDashboardEvent>());
        dispatcher.DidNotReceiveWithAnyArgs().DispatchFromGoBar(default);
    }

    [Fact]
    public void AMoneyVerbLastIsThePromptToStartWithTheWord()
    {
        var (service, _, _) = Build();

        var suggestion = service.Suggest("0.5 BUY");

        Assert.Empty(suggestion.Options);
        Assert.Equal(
            "Money commands start with the word, like BUY 0.5. Nothing was opened.",
            suggestion.Sentence);
    }

    [Fact]
    public void ChoosingBuyPublishesTheDashboardRequestAndNothingElse()
    {
        var (service, dispatcher, bus) = Build();

        var option = Assert.Single(service.Suggest("BUY 0.5 95000").Options);
        service.Run(option);

        var request = Assert.Single(bus.Log.OfType<OpenTradingDashboardEvent>());
        Assert.Equal("Buy", request.Side);
        Assert.Equal(0.5, request.Quantity);
        Assert.Equal(95000, request.LimitPrice);
        Assert.False(request.Market);
        Assert.False(request.Flat);
        dispatcher.DidNotReceiveWithAnyArgs().DispatchFromGoBar(default);
        Assert.Empty(bus.Log.OfType<AnnouncementEvent>());
    }

    [Fact]
    public void ChoosingFlatPublishesAFlatRequest()
    {
        var (service, _, bus) = Build();

        service.Run(Assert.Single(service.Suggest("FLAT").Options));

        var request = Assert.Single(bus.Log.OfType<OpenTradingDashboardEvent>());
        Assert.True(request.Flat);
        Assert.Null(request.Side);
        Assert.Null(request.Quantity);
    }

    [Fact]
    public void AMoneyRequestWaitsForTheBoxToClose()
    {
        var (service, _, bus) = Build();
        bus.Publish(new ModalStateChangedEvent(true, "GoBar"));

        service.Run(Assert.Single(service.Suggest("BUY 0.5").Options));

        Assert.Contains(bus.Log, e => e is CloseGoBarRequestEvent);
        Assert.Empty(bus.Log.OfType<OpenTradingDashboardEvent>());

        bus.Publish(new ModalStateChangedEvent(false, "GoBar"));

        Assert.Single(bus.Log.OfType<OpenTradingDashboardEvent>());
    }
}
