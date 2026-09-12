using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>The monitor reporting on ITSELF</b> — the hole this class was written to close, one level
/// up from the alerts.
///
/// <para>
/// <c>ExecuteAsync</c> caught every poll exception into a single <c>LogWarning</c> and retried,
/// forever. A monitor whose every poll is throwing is a monitor announcing nothing, and for a
/// user who cannot see the log there is no way to tell that apart from a quiet market — which is
/// exactly the failure mode the browser-closed half exists to prevent.
/// </para>
///
/// <para>
/// Said ONCE per distinct reason and said again on recovery, the same rule
/// <c>DeadFeedTracker</c> uses: a latch is there to stop a repeating fault repeating every
/// minute, never to stop a NEW fault being heard.
/// </para>
/// </summary>
[Collection("CircuitCoverage")]
public sealed class MonitorSelfReportingTests : IDisposable
{
    public MonitorSelfReportingTests() => CircuitAlertCoverage.ResetForTests();
    public void Dispose() => CircuitAlertCoverage.ResetForTests();

    private static SeriesConfig NarratedVolume()
    {
        var cfg = new SeriesConfig
        {
            Id = CoreSeriesIds.Volume, Name = "Volume", FriendlyName = "Volume", IndicatorCode = "VOLUME",
            Pane = "Volume", IsAutoNarrated = true, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Volume", DisplayName = "Volume", DisplayType = ComponentDisplayType.Bar,
            Role = ComponentRole.Volume, DataMapping = "volume", IsVisible = true,
        });
        return cfg;
    }

    /// <summary>
    /// A standalone ladder gets the notification sound, like the bar close it would otherwise
    /// have ridden. The two used to disagree, which made the cue mean "a bar closed on a chart
    /// whose timeframe clears the floor" rather than "something happened".
    /// </summary>
    [Fact]
    public async Task A_ladder_with_no_bar_close_still_gets_the_cue()
    {
        using var h = new HeadlessMonitorHarness(new[] { NarratedVolume() }, timeframe: "1m");
        h.Presenter.HasNotificationTool = true;
        h.Notifications(false);          // bar closes off; the ladder is the only thing left

        await h.PollAsync();
        int before = h.Presenter.SoundsPlayed;
        h.CloseABar();
        await h.PollAsync();

        Assert.NotEmpty(h.Presenter.Toasts);
        Assert.True(h.Presenter.SoundsPlayed > before,
            "a narration announcement is an event and must carry the same cue a bar close does");
    }
}
