using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>"When the browser is closed orca reads the notification twice."</b> Cody, 2026-09-11.
///
/// <para>
/// The monitor raised a desktop notification and then spoke the same sentence through Orca's
/// <c>PresentMessage</c>; Orca presents desktop notifications itself, so every announcement
/// arrived twice. <see cref="DesktopDeliveryPlan.ToastIsSpoken"/> is the per-desktop rule for
/// whether the toast already reaches the screen reader, and <see cref="DesktopAnnouncement"/>
/// is the one place that applies it — for alerts, bar closes, the monitor's own reports and
/// order events alike.
/// </para>
/// </summary>
public sealed class DesktopAnnouncementTests
{
    // ── The per-desktop rule ──────────────────────────────────────────────────

    private static DesktopDeliveryPlan Plan(DesktopOs os, params string[] present)
        => DesktopDeliveryPlan.For(os, path => present.Contains(path));

    [Fact]
    public void On_Linux_the_toast_is_spoken_when_Orca_is_the_speech_route()
    {
        // Cody's desktop: notify-send AND gdbus (Orca). Orca reads the notification.
        var orca = Plan(DesktopOs.Linux, "/usr/bin/notify-send", "/usr/bin/gdbus", "/usr/bin/spd-say");
        Assert.Equal(DesktopDeliveryPlan.SpeechKind.Orca, orca.Speech);
        Assert.True(orca.ToastIsSpoken);
    }

    [Fact]
    public void On_Linux_without_Orca_the_toast_is_NOT_spoken_so_spd_say_still_runs()
    {
        // No screen reader is presenting notifications; the sentence has to be spoken directly.
        var spd = Plan(DesktopOs.Linux, "/usr/bin/notify-send", "/usr/bin/spd-say");
        Assert.Equal(DesktopDeliveryPlan.SpeechKind.SpdSay, spd.Speech);
        Assert.False(spd.ToastIsSpoken);
    }

    [Fact]
    public void With_no_toast_tool_nothing_is_spoken_by_a_toast()
    {
        var noToast = Plan(DesktopOs.Linux, "/usr/bin/gdbus");
        Assert.False(noToast.CanNotify);
        Assert.False(noToast.ToastIsSpoken);
    }

    [Fact]
    public void On_macOS_and_Windows_the_toast_is_the_screen_reader_route()
    {
        // The plan's own DescribeToast already claims VoiceOver / Narrator read these.
        Assert.True(Plan(DesktopOs.MacOS, "/usr/bin/osascript", "/usr/bin/say").ToastIsSpoken);
        var root = (Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows").TrimEnd('\\');
        Assert.True(Plan(DesktopOs.Windows, root + @"\System32\WindowsPowerShell\v1.0\powershell.exe").ToastIsSpoken);
    }

    // ── The one rule ──────────────────────────────────────────────────────────

    private sealed class Spy : IDesktopAlertPresenter
    {
        public readonly List<(string Title, string Text)> Toasts = new();
        public readonly List<string> Spoken = new();
        public int Sounds;
        public bool CanNotifyValue = true;
        public bool ToastIsSpokenValue;

        public string Describe() => "spy";
        public string DescribeToast() => "spy";
        public bool CanNotify => CanNotifyValue;
        public bool ToastIsSpoken => ToastIsSpokenValue;
        public void PlayNotificationSound() => Sounds++;
        public void Notify(string title, string text, bool urgent) => Toasts.Add((title, text));
        public void Speak(string text) => Spoken.Add(text);
    }

    [Fact]
    public void Toast_spoken_by_the_screen_reader_means_no_direct_speech()
    {
        var p = new Spy { ToastIsSpokenValue = true };
        DesktopAnnouncement.Present(p, speakBesideToast: false, "Trading alert", "body", "spoken", urgent: false, withSound: true, null);

        Assert.Single(p.Toasts);
        Assert.Empty(p.Spoken);
        Assert.Equal(1, p.Sounds);
    }

    [Fact]
    public void The_users_switch_restores_direct_speech_beside_the_toast()
    {
        var p = new Spy { ToastIsSpokenValue = true };
        DesktopAnnouncement.Present(p, speakBesideToast: true, "Trading alert", "body", "spoken", urgent: false, withSound: false, null);

        Assert.Single(p.Toasts);
        Assert.Equal("spoken", Assert.Single(p.Spoken));
        Assert.Equal(0, p.Sounds);
    }

    [Fact]
    public void A_toast_the_screen_reader_does_not_read_is_followed_by_speech()
    {
        var p = new Spy { ToastIsSpokenValue = false };
        DesktopAnnouncement.Present(p, speakBesideToast: false, "Trading alert", "body", "spoken", urgent: false, withSound: false, null);

        Assert.Single(p.Toasts);
        Assert.Single(p.Spoken);
    }

    [Fact]
    public void No_toast_tool_at_all_means_speech_is_the_only_channel_and_always_runs()
    {
        // ToastIsSpoken cannot be true without a toast, but a presenter that claims both is not
        // trusted: with nothing to show, the sentence must be spoken.
        var p = new Spy { CanNotifyValue = false, ToastIsSpokenValue = true };
        DesktopAnnouncement.Present(p, speakBesideToast: false, "Trading alert", "body", "spoken", urgent: false, withSound: false, null);

        Assert.Single(p.Spoken);
    }

    [Fact]
    public void A_presenter_that_predates_the_rule_still_records_speech()
    {
        // The interface default: a double that does not know about ToastIsSpoken keeps hearing
        // both channels, so every delivery test written before this rule keeps its meaning.
        IDesktopAlertPresenter legacy = new LegacySpy();
        Assert.False(legacy.ToastIsSpoken);
        Assert.True(DesktopAnnouncement.ShouldSpeak(legacy, speakBesideToast: false));
    }

    private sealed class LegacySpy : IDesktopAlertPresenter
    {
        public string Describe() => "legacy";
        public string DescribeToast() => "legacy";
        public bool CanNotify => true;
        public void PlayNotificationSound() { }
        public void Notify(string title, string text, bool urgent) { }
        public void Speak(string text) { }
    }

    [Fact]
    public void The_setting_reads_false_when_absent_and_true_when_set()
    {
        var settings = NSubstitute.Substitute.For<ISettingsManager>();
        Assert.False(DesktopAnnouncement.SpeakBesideToast(settings));
        Assert.False(DesktopAnnouncement.SpeakBesideToast(null));

        settings.GetSetting(SettingsKeys.DesktopSpeakBesideToast)
            .Returns(Newtonsoft.Json.Linq.JToken.FromObject(true));
        Assert.True(DesktopAnnouncement.SpeakBesideToast(settings));
    }
}
