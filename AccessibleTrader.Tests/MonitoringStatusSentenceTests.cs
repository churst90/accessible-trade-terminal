using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>What Ctrl+Alt+Shift+M says, and the sentence that was false.</b>
///
/// <para>
/// There are two switches with almost the same name. <c>workspace.backgroundMonitoring</c> is
/// "keep watching other tabs" INSIDE this browser; <c>monitoring.backgroundLocal</c> is "keep
/// monitoring when the browser is CLOSED", and it is the master switch of an entirely separate
/// process-lifetime half. Until 2026-09-11 this announcement read only the first one, so on a
/// machine with the browser-closed half on and the other-tabs switch off it said
/// <i>"Background monitoring is off"</i> — false on that machine, in the one sentence a user
/// presses a key specifically in order to hear.
/// </para>
///
/// <para>
/// Every test here is a PAIR over the browser-closed switch, because a sentence that mentioned
/// it unconditionally would pass a test that only checked one state.
/// </para>
/// </summary>
public class MonitoringStatusSentenceTests
{
    private sealed record Rig(BackgroundMonitoringService Service, SpyEventBus Bus);

    private static Rig Build(bool watchOtherTabs, bool browserClosedMonitoring)
    {
        var bus = new SpyEventBus();
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(SettingsKeys.BackgroundMonitoring).Returns(JToken.FromObject(watchOtherTabs));
        settings.GetSetting(SettingsKeys.BackgroundLocalMonitoring).Returns(JToken.FromObject(browserClosedMonitoring));

        var alerts = Substitute.For<IAlertOrchestrator>();
        alerts.GetAlerts().Returns(new List<AlertDefinition>());
        var engine = Substitute.For<IStrategyEngine>();
        engine.ActiveStrategies.Returns(new List<ActiveStrategy>());

        var service = new BackgroundMonitoringService(
            new MockWorkspaceStore(), bus, settings, new DemoPolicy(isDemo: false),
            Substitute.For<IMarketFeeds>(), Substitute.For<IIndicatorService>(),
            alerts, Substitute.For<IAlertEvaluator>(), engine,
            NullLogger<BackgroundMonitoringService>.Instance);

        return new Rig(service, bus);
    }

    private static string Spoken(Rig rig)
    {
        rig.Service.AnnounceStatus();
        var said = rig.Bus.Log.OfType<FeedbackRequestEvent>().Select(e => e.Message).ToList();
        Assert.NotEmpty(said);
        return said[^1];
    }

    /// <summary>
    /// The defect, pinned from the other side: with the browser-closed half ON, the sentence
    /// must not be a flat "monitoring is off".
    /// </summary>
    [Fact]
    public void With_the_browser_closed_half_ON_the_sentence_says_so()
    {
        string said = Spoken(Build(watchOtherTabs: false, browserClosedMonitoring: true));

        Assert.Contains("With the browser closed", said, StringComparison.Ordinal);
        Assert.Contains("keeps watching", said, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing is watched", said, StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_browser_closed_half_OFF_the_sentence_says_THAT_and_names_the_switch()
    {
        string said = Spoken(Build(watchOtherTabs: false, browserClosedMonitoring: false));

        Assert.Contains("With the browser closed", said, StringComparison.Ordinal);
        Assert.Contains("nothing is watched", said, StringComparison.Ordinal);
        Assert.Contains("Keep monitoring when the browser is closed", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two switches must not share a phrase. "Background monitoring is off" described one
    /// of them while the tray used the same words for the other — two switches, one phrase,
    /// three surfaces. The in-session one now says which one it is.
    /// </summary>
    [Fact]
    public void The_in_session_switch_is_named_for_what_it_actually_covers()
    {
        string said = Spoken(Build(watchOtherTabs: false, browserClosedMonitoring: true));

        Assert.Contains("Watching other tabs is off", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Background monitoring is off", said, StringComparison.Ordinal);
    }

    /// <summary>Both switches on: the sentence covers both owners, not one.</summary>
    [Fact]
    public void With_both_on_the_sentence_covers_both_owners()
    {
        string said = Spoken(Build(watchOtherTabs: true, browserClosedMonitoring: true));

        Assert.Contains("Watching", said, StringComparison.Ordinal);
        Assert.Contains("With the browser closed", said, StringComparison.Ordinal);
    }
}
