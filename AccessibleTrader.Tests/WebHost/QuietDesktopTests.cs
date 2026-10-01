using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>A test run must not touch the developer's desktop.</b> Cody, 2026-10-01: running the suites
/// played the chart's tones through his speakers, spoke over his screen reader, and pulled his
/// keyboard away while he worked. A process watch during a mutation campaign found <c>pacat</c>
/// children of BOTH test hosts. <see cref="DesktopOutput"/> is the switch;
/// <see cref="QuietDesktop"/> engages it as this assembly loads. These check that it is engaged
/// and that every probe the host makes for a local tool honours it.
/// </summary>
public class QuietDesktopTests
{
    [Fact]
    public void The_switch_is_engaged_for_this_whole_test_run()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable(DesktopOutput.Variable));
        Assert.True(DesktopOutput.Suppressed);
    }

    [Fact]
    public void With_it_engaged_no_local_tool_is_ever_found()
    {
        // The probe every search goes through. /bin/sh exists on every Linux and macOS box, so a
        // probe that answers true for it would find pacat, spd-say and notify-send too.
        Assert.False(DesktopOutput.FileProbe("/bin/sh"));
        Assert.Null(WebHostAudioDriver.FindOnPath("sh", DesktopOutput.FileProbe));
    }

    [Fact]
    public void The_audio_driver_plays_nothing_locally()
    {
        // pw-cat / pacat / aplay is how the chart's tones reached the speakers.
        using var sink = new WebHostBrowserAudioSink();
        using var driver = new WebHostAudioDriver(NullLogger<WebHostAudioDriver>.Instance, sink);

        Assert.True(driver.IsBrowserMode, "A test host started a local audio player.");
    }

    [Fact]
    public void Speech_goes_to_the_browser_not_to_spd_say_or_Orca()
    {
        var sut = new WebHostSpeechManager(Substitute.For<ISpeechManager>(), Substitute.For<IEventBus>(),
            NullLogger<WebHostSpeechManager>.Instance);

        Assert.True(sut.IsBrowserTtsBackend, "A test host chose a speech backend that talks out loud.");
    }

    [Fact]
    public void Notifications_and_their_sounds_have_nothing_to_run()
    {
        var plan = DesktopDeliveryPlan.ForCurrentMachine();

        Assert.False(plan.CanNotify);
        Assert.False(plan.CanSpeak);
        Assert.False(plan.CanPlaySound);
    }
}
