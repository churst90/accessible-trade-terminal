using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>"When the browser is closed orca reads the notification twice."</b> Cody, 2026-09-11 —
/// and his decision the same evening: <i>"Pick the best path, orca/speech dispatcher or
/// notification but not both … If someone doesn't need speech they shouldn't hear it."</i>
///
/// <para>
/// The monitor raised a desktop notification and then spoke the same sentence; Orca presents
/// the notification itself, so every announcement arrived twice. <see cref="DesktopAnnouncement"/>
/// is the one place that decides the channel for every headless delivery — alerts, bar closes,
/// the monitor's own reports, order events: the NOTIFICATION where the machine has a tool for
/// one (a screen reader reads it; a sighted user sees it; a machine with no screen reader hears
/// nothing, which is right), and direct speech only where it has none.
/// </para>
/// </summary>
public sealed class DesktopAnnouncementTests
{
    private sealed class Spy : IDesktopAlertPresenter
    {
        public readonly List<(string Title, string Text)> Toasts = new();
        public readonly List<string> Spoken = new();
        public int Sounds;
        public bool HasNotificationTool = true;

        public string Describe() => "spy";
        public string DescribeToast() => "spy";
        public bool CanNotify => HasNotificationTool;
        public void PlayNotificationSound() => Sounds++;
        public void Notify(string title, string text, bool urgent) => Toasts.Add((title, text));
        public void Speak(string text) => Spoken.Add(text);
    }

    [Fact]
    public void With_a_notification_tool_the_toast_is_the_whole_announcement_and_nothing_is_spoken()
    {
        var p = new Spy { HasNotificationTool = true };
        DesktopAnnouncement.Present(p, "Trading alert", "body", "spoken", urgent: false, withSound: true, null);

        var toast = Assert.Single(p.Toasts);
        Assert.Equal(("Trading alert", "body"), toast);
        Assert.Empty(p.Spoken);
        Assert.Equal(1, p.Sounds);
    }

    [Fact]
    public void Without_a_notification_tool_the_sentence_is_spoken()
    {
        var p = new Spy { HasNotificationTool = false };
        DesktopAnnouncement.Present(p, "Trading alert", "body", "spoken", urgent: false, withSound: false, null);

        Assert.Equal("spoken", Assert.Single(p.Spoken));
        Assert.Equal(0, p.Sounds);
    }

    [Fact]
    public void The_rule_is_the_tool_and_nothing_else()
    {
        Assert.False(DesktopAnnouncement.ShouldSpeak(new Spy { HasNotificationTool = true }));
        Assert.True(DesktopAnnouncement.ShouldSpeak(new Spy { HasNotificationTool = false }));
    }

    [Fact]
    public void A_broken_sound_player_does_not_take_the_announcement_with_it()
    {
        var p = new ThrowingSound();
        DesktopAnnouncement.Present(p, "Trading alert", "body", "spoken", urgent: false, withSound: true, null);
        Assert.Single(p.Toasts);
    }

    private sealed class ThrowingSound : IDesktopAlertPresenter
    {
        public readonly List<(string, string)> Toasts = new();
        public string Describe() => "t";
        public string DescribeToast() => "t";
        public bool CanNotify => true;
        public void PlayNotificationSound() => throw new InvalidOperationException("no audio device");
        public void Notify(string title, string text, bool urgent) => Toasts.Add((title, text));
        public void Speak(string text) { }
    }
}
