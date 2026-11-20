using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.ChangeLog;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Startup is what makes Alt+U's log real on both heads. The desktop registers the recorder as
/// a singleton, and a subscriber nobody resolves never subscribes; and the return prompt has to
/// be published after the speech pipeline is listening, or it goes to no one. Both were wrong
/// in the first 5b build: the desktop recorder was never resolved, and the prompt was published
/// from the circuit handler and the window's Created event, before the page existed.
/// </summary>
[Collection(nameof(SpeechClockSettingsTests))]
public class ChangeLogStartupTests
{
    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private sealed class Rig
    {
        public readonly SpyEventBus Bus = new();
        public readonly ChangeLogStore Store =
            new(new TempPaths(TestTemp.NewDir("att-changelog-startup-")), NullLogger<ChangeLogStore>.Instance);
        public int? SpeechReadyAt;
        public readonly AppStartupService Startup;

        public Rig()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IDataService>(Substitute.For<IDataService>());
            services.AddSingleton<IPluginLoaderService>(Substitute.For<IPluginLoaderService>());
            services.AddSingleton<IIndicatorService>(Substitute.For<IIndicatorService>());
            services.AddSingleton<IDataOrchestrationService>(Substitute.For<IDataOrchestrationService>());
            services.AddSingleton<IInputRouter>(Substitute.For<IInputRouter>());
            services.AddSingleton<IChartCommandManager>(Substitute.For<IChartCommandManager>());
            services.AddSingleton<IHistoryBufferCoordinator>(Substitute.For<IHistoryBufferCoordinator>());
            // The moment the speech pipeline exists: anything published before it is lost.
            services.AddSingleton<IAccessibilityFeedbackCoordinator>(_ =>
            {
                SpeechReadyAt = Bus.Log.Count;
                return Substitute.For<IAccessibilityFeedbackCoordinator>();
            });
            services.AddSingleton<IWorkspaceInitializer>(Substitute.For<IWorkspaceInitializer>());
            services.AddSingleton<IEventBus>(Bus);
            services.AddSingleton<IWorkspaceStore>(new MockWorkspaceStore());
            services.AddSingleton(Store);
            services.AddSingleton(sp => new ChangeLogRecorder(Bus, Store));
            services.AddSingleton(sp => new ChangeLogCatchUpService(Store, Bus));
            Startup = new AppStartupService(
                services.BuildServiceProvider(), NullLogger<AppStartupService>.Instance);
        }
    }

    [Fact]
    public async Task StartupSubscribesTheRecorder_SoAFillIsLoggedWithoutAnyoneResolvingIt()
    {
        var rig = new Rig();
        await rig.Startup.InitializeAsync();

        rig.Bus.Publish(new OrderFilledEvent(new OrderUpdate(
            "o-1", "BTC/USDT", OrderSide.Buy, 0.1, 94_210, 0, OrderStatus.Filled,
            false, false, DateTime.UtcNow), "MEXC"));

        ChangeLogDocument doc = ChangeLogDocument.Empty;
        for (int i = 0; i < 300 && doc.Entries.Count == 0; i++)
        {
            await Task.Delay(10);
            doc = await rig.Store.SnapshotAsync();
        }
        Assert.Single(doc.Entries);
        Assert.Equal(ChangeLogCategory.Money, doc.Entries[0].Category);
    }

    [Fact]
    public async Task StartupOffersTheCatchUp_AfterTheSpeechPipelineIsListening()
    {
        var rig = new Rig();
        DateTime now = DateTime.UtcNow;
        await rig.Store.SetLastPresentAsync(now.AddMinutes(-20));
        await rig.Store.AppendAsync(new ChangeLogEntry(
            "k1", now.AddMinutes(-10), ChangeLogCategory.Alert, "BTC crossed 95,000.", null, false));

        await rig.Startup.InitializeAsync();

        int prompt = rig.Bus.Log.FindIndex(e =>
            e is AnnouncementEvent a && a.Message.StartsWith("You were away", StringComparison.Ordinal));
        Assert.True(prompt >= 0, "startup did not offer the catch-up");
        Assert.NotNull(rig.SpeechReadyAt);
        Assert.True(prompt >= rig.SpeechReadyAt,
            "the return prompt was published before the speech pipeline existed");
    }

    [Fact]
    public async Task StartupResolvesTheHeadlinePoller_OrItWouldNeverRun()
    {
        // The poller starts its own timer in its constructor; nothing else asks for it.
        bool resolved = false;
        var services = new ServiceCollection();
        services.AddSingleton<IDataService>(Substitute.For<IDataService>());
        services.AddSingleton<IPluginLoaderService>(Substitute.For<IPluginLoaderService>());
        services.AddSingleton<IIndicatorService>(Substitute.For<IIndicatorService>());
        services.AddSingleton<IDataOrchestrationService>(Substitute.For<IDataOrchestrationService>());
        services.AddSingleton<IInputRouter>(Substitute.For<IInputRouter>());
        services.AddSingleton<IChartCommandManager>(Substitute.For<IChartCommandManager>());
        services.AddSingleton<IHistoryBufferCoordinator>(Substitute.For<IHistoryBufferCoordinator>());
        services.AddSingleton<IAccessibilityFeedbackCoordinator>(Substitute.For<IAccessibilityFeedbackCoordinator>());
        services.AddSingleton<IWorkspaceInitializer>(Substitute.For<IWorkspaceInitializer>());
        services.AddSingleton(_ =>
        {
            resolved = true;
            return new Core.Services.Headlines.EdgarHeadlineLiveNotices(
                new MockWorkspaceStore(), new SpyEventBus(),
                Substitute.For<Core.Services.Analysis.ICompanyFilingSource>());
        });

        await new AppStartupService(services.BuildServiceProvider(), NullLogger<AppStartupService>.Instance)
            .InitializeAsync();

        Assert.True(resolved, "AppStartupService never resolved EdgarHeadlineLiveNotices");
    }
}
