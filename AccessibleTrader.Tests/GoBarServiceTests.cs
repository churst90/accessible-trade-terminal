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
                    ShortcutManager Shortcuts) Build()
    {
        var dir = TestTemp.NewDir("att-gobar-service-");
        var shortcuts = new ShortcutManager(new TempPaths(dir));
        var bus = new SpyEventBus();
        var stack = new ModalStack(bus);
        var dispatcher = Substitute.For<ICommandDispatcher>();
        return (new GoBarService(dispatcher, shortcuts, bus, stack), dispatcher, bus, shortcuts);
    }

    private static GoBarEntry Entry(SystemCommand command)
        => GoBarRegistry.All.Single(e => e.Command == command);

    [Fact]
    public void OneMatchSaysItsNameAndKeyAndNoMore()
    {
        var (service, _, _, _) = Build();

        var suggestion = service.Suggest("pulse");

        Assert.Single(suggestion.Matches);
        Assert.Equal("Market pulse, Alt plus P.", suggestion.Sentence);
    }

    [Fact]
    public void TwoMatchesUseTheSingularMore()
    {
        var (service, _, _, _) = Build();

        var suggestion = service.Suggest("load");

        Assert.True(suggestion.Matches.Count >= 2,
            "fixture floor: 'load' should find at least Load a workspace and Load the selected chart, found "
            + string.Join(", ", suggestion.Matches.Select(m => m.Entry.Command)));
        Assert.StartsWith("Load a workspace, Control plus Alt plus W.", suggestion.Sentence);
        Assert.EndsWith($"{suggestion.Matches.Count - 1} more.", suggestion.Sentence);
        if (suggestion.Matches.Count == 2)
            Assert.Contains(" 1 more.", suggestion.Sentence);
    }

    [Fact]
    public void ManyMatchesCountTheRest()
    {
        var (service, _, _, _) = Build();

        var suggestion = service.Suggest("pl");

        Assert.True(suggestion.Matches.Count > 2,
            "fixture floor: 'pl' should find more than two commands");
        Assert.EndsWith($" {suggestion.Matches.Count - 1} more.", suggestion.Sentence);
    }

    [Fact]
    public void TheSpokenKeyComesFromTheLiveProfile()
    {
        var (service, _, _, shortcuts) = Build();

        shortcuts.UpdateBinding(SystemCommand.MarketPulse, "Q", alt: true);

        var suggestion = service.Suggest("pulse");

        Assert.Equal("Market pulse, Alt plus Q.", suggestion.Sentence);
    }

    [Fact]
    public void KeyOnlySpeaksTheKeyAndDoesNotDispatch()
    {
        var (service, dispatcher, bus, _) = Build();

        service.Run(Entry(SystemCommand.CloseModal));

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

        service.Run(Entry(SystemCommand.MarketPulse));

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

        service.Run(Entry(SystemCommand.MarketPulse));
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

        Assert.Empty(suggestion.Matches);
        Assert.Equal("Nothing matches zzzz.", suggestion.Sentence);
    }
}
