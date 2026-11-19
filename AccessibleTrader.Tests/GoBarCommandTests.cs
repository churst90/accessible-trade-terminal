using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;

namespace AccessibleTrader.Tests;

/// <summary>Alt+G itself: a free chord, a global command and an event the box listens for.</summary>
public class GoBarCommandTests
{
    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    [Fact]
    public void AltGBindsToOpenGoBar()
    {
        var dir = TestTemp.NewDir("att-gobar-command-");
        var manager = new ShortcutManager(new TempPaths(dir));

        Assert.Equal(SystemCommand.OpenGoBar, manager.GetCommand("G", shift: false, ctrl: false, alt: true));
    }

    [Fact]
    public void OpenGoBarIsGlobal()
    {
        Assert.False(CommandDispatcher.IsChartScopedCommand(SystemCommand.OpenGoBar));
    }

    [Fact]
    public void DispatchPublishesTheOpenEvent()
    {
        var bus = new AccessibleTrader.Tests.Mocks.SpyEventBus();
        var store = new AccessibleTrader.Tests.Mocks.MockWorkspaceStore();
        var nav = NSubstitute.Substitute.For<INavigationEngine>();
        var barDetail = NSubstitute.Substitute.For<AccessibleTrader.Sdk.Interfaces.IBarDetailService>();
        var crossing = new IndicatorCrossingEngine(store, bus);
        var dispatcher = new CommandDispatcher(bus, nav, store, barDetail, crossing);

        dispatcher.Dispatch(SystemCommand.OpenGoBar);

        Assert.Single(bus.Log.OfType<OpenGoBarEvent>());
    }
}
