using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// A tab switch or close that changed nothing was silent: switching to the tab you are on, to a
/// tab that does not exist, or closing the last tab. The store only announced the tab after a
/// change, so a refusal sounded the same as a key that is not bound.
/// </summary>
public sealed class TabRefusalSpeechTests
{
    private static (WorkspaceStore Store, List<string> Said) Build(int tabs)
    {
        var bus = new EventBus();
        var said = new List<string>();
        bus.Subscribe<AnnouncementEvent>(e => { lock (said) said.Add(e.Message); });
        var store = new WorkspaceStore(bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());
        store.Dispatch(new UpdateSettingsAction(_ => WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "MEXC", "BTCUSDT", "1h"),
        }));
        for (int i = 1; i < tabs; i++) store.Dispatch(new AddTabAction());
        store.Dispatch(new SwitchTabAction(0));
        said.Clear();
        return (store, said);
    }

    [Fact]
    public void Closing_the_last_tab_says_it_stays_open()
    {
        var (store, said) = Build(tabs: 1);
        store.Dispatch(new CloseTabAction(0));
        Assert.Equal(1, store.State.TabCount);
        Assert.Contains("The last tab stays open.", said);
    }

    [Fact]
    public void Switching_to_the_tab_you_are_on_names_it()
    {
        var (store, said) = Build(tabs: 2);
        store.Dispatch(new SwitchTabAction(0));
        Assert.Contains(said, s => s.StartsWith("Already on tab 1: "));
    }

    [Fact]
    public void Switching_with_one_tab_says_there_is_only_one()
    {
        var (store, said) = Build(tabs: 1);
        store.Dispatch(new SwitchTabAction(0));
        Assert.Contains("Only one tab open.", said);
    }

    [Fact]
    public void A_tab_that_does_not_exist_is_named_with_how_many_there_are()
    {
        var (store, said) = Build(tabs: 2);
        store.Dispatch(new SwitchTabAction(4));
        Assert.Contains("No tab 5. 2 tabs open.", said);
    }

    [Fact]
    public void A_switch_that_happens_is_announced_once_and_not_as_a_refusal()
    {
        var (store, said) = Build(tabs: 2);
        store.Dispatch(new SwitchTabAction(1));
        var only = Assert.Single(said);
        Assert.StartsWith("Tab 2: ", only);
    }
}
