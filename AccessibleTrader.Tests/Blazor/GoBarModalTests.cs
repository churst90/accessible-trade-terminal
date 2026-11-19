using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The box itself: focus goes to the input, the list follows the typed text, Down moves the
/// active option, Tab completes, Enter runs after closing, Escape is the base class's, and
/// the polite read is non-interrupting.
/// </summary>
public class GoBarModalTests
{
    private static (IRenderedComponent<GoBarModal> Cut, ICommandDispatcher Dispatcher,
                    List<AnnouncementEvent> Spoken) Open(BlazorTestHarness h)
    {
        var dispatcher = Substitute.For<ICommandDispatcher>();
        var stack = new ModalStack(h.EventBus);
        h.With(new GoBarService(dispatcher, h.ShortcutManager, h.EventBus, stack));

        var spoken = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(e => spoken.Add(e));

        var cut = h.OpenModal<GoBarModal>(bus => bus.Publish(new OpenGoBarEvent()));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#gobar-input")));
        return (cut, dispatcher, spoken);
    }

    private static void Type(IRenderedComponent<GoBarModal> cut, string text)
        => cut.Find("#gobar-input").Input(text);

    private static void Key(IRenderedComponent<GoBarModal> cut, string key)
        => cut.Find("#gobar-input").KeyDown(new KeyboardEventArgs { Key = key });

    [Fact]
    public void TheBoxOpensOnItsInput()
    {
        using var h = new BlazorTestHarness();
        var (cut, _, _) = Open(h);

        h.WaitForFocus("gobar-input");
        Assert.Equal("Go", cut.Find("h2").TextContent.Trim());
    }

    [Fact]
    public void TypingListsSuggestionsWithAnActiveOption()
    {
        using var h = new BlazorTestHarness();
        var (cut, _, _) = Open(h);

        Type(cut, "pulse");

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("#gobar-list [role='option']"));
            Assert.Equal("gobar-opt-0", cut.Find("#gobar-input").GetAttribute("aria-activedescendant"));
            Assert.Equal("true", cut.Find("#gobar-input").GetAttribute("aria-expanded"));
        });
    }

    [Fact]
    public void DownMovesTheActiveOptionAndLeavesTheTypedTextAlone()
    {
        using var h = new BlazorTestHarness();
        var (cut, _, _) = Open(h);

        Type(cut, "load");
        cut.WaitForAssertion(() => Assert.True(
            cut.FindAll("#gobar-list [role='option']").Count >= 2));

        Key(cut, "ArrowDown");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("gobar-opt-1", cut.Find("#gobar-input").GetAttribute("aria-activedescendant"));
            Assert.Equal("load", cut.Find("#gobar-input").GetAttribute("value"));
            Assert.Equal("true", cut.Find("#gobar-opt-1").GetAttribute("aria-selected"));
            Assert.Equal("false", cut.Find("#gobar-opt-0").GetAttribute("aria-selected"));
        });
    }

    [Fact]
    public void TabCompletesToTheTopCodesFirstCode()
    {
        using var h = new BlazorTestHarness();
        var (cut, _, _) = Open(h);

        Type(cut, "pul");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#gobar-list [role='option']")));

        Key(cut, "Tab");

        cut.WaitForAssertion(() =>
            Assert.Equal("PULSE", cut.Find("#gobar-input").GetAttribute("value")));
    }

    [Fact]
    public void EnterRunsTheTopMatchAfterTheBoxCloses()
    {
        using var h = new BlazorTestHarness();
        var (cut, dispatcher, _) = Open(h);

        Type(cut, "pulse");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#gobar-list [role='option']")));

        Key(cut, "Enter");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")));
        dispatcher.Received(1).DispatchFromGoBar(SystemCommand.MarketPulse);
    }

    [Fact]
    public void EnterWithNoMatchSaysSoAndKeepsTheBoxOpen()
    {
        using var h = new BlazorTestHarness();
        var (cut, dispatcher, spoken) = Open(h);

        Type(cut, "zzzz");
        Key(cut, "Enter");

        Assert.Contains(spoken, e => e.Message == "Nothing matches zzzz.");
        Assert.NotEmpty(cut.FindAll("#gobar-input"));
        Assert.Equal("zzzz", cut.Find("#gobar-input").GetAttribute("value"));
        dispatcher.DidNotReceiveWithAnyArgs().DispatchFromGoBar(default);
    }

    [Fact]
    public void TheListRendersTheServicesArgumentOptionAndTabCompletesItsCanonicalText()
    {
        using var h = new BlazorTestHarness();
        var (cut, _, _) = Open(h);

        Type(cut, "TF 4h");

        cut.WaitForAssertion(() =>
        {
            var options = cut.FindAll("#gobar-list [role='option']");
            Assert.Single(options);
            Assert.Contains("Timeframe 4 hours", options[0].TextContent);
        });

        Key(cut, "Tab");

        cut.WaitForAssertion(() =>
            Assert.Equal("TF 4h", cut.Find("#gobar-input").GetAttribute("value")));
    }

    [Fact]
    public void ThePoliteReadIsNonInterruptingAndSpeaksTheTopSuggestion()
    {
        using var h = new BlazorTestHarness();
        var (cut, _, spoken) = Open(h);

        Type(cut, "pulse");

        Assert.True(System.Threading.SpinWait.SpinUntil(
            () => spoken.Any(e => e.Message.StartsWith("Market pulse", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3)),
            "the polite read never arrived: " + string.Join(" || ", spoken.Select(e => e.Message)));

        var read = spoken.Last(e => e.Message.StartsWith("Market pulse", StringComparison.Ordinal));
        Assert.False(read.Interrupt);
        Assert.Equal(SpeechChannel.Interface, read.Channel);
    }
}
