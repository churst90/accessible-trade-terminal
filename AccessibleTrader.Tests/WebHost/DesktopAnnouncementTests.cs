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
        // Returns whether it LANDED — a spy with no tool records the attempt and reports
        // failure, which is what a real machine with no notification daemon does.
        public bool Notify(string title, string text, bool urgent) { Toasts.Add((title, text)); return HasNotificationTool; }
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
    public void Before_trying_the_question_is_whether_a_tool_exists()
    {
        Assert.False(DesktopAnnouncement.ShouldSpeak(new Spy { HasNotificationTool = true }));
        Assert.True(DesktopAnnouncement.ShouldSpeak(new Spy { HasNotificationTool = false }));
    }

    // ── H8: a notification that was ATTEMPTED and did not land ───────────────

    /// <summary>
    /// <b>The hole this closes.</b> A machine can have <c>notify-send</c> on its PATH and no
    /// daemon behind it — no D-Bus session, no reachable display, the service started before the
    /// desktop. <c>CanNotify</c> is true, so the old rule ("speak only where there is no tool")
    /// chose silence, and the failure was logged at Debug as if nothing had happened. Since the
    /// browser-closed half made the notification the ONLY channel for everything the trader
    /// cannot see, that is the entire feature failing without saying so.
    /// </summary>
    [Fact]
    public void A_notification_that_fails_is_spoken_instead()
    {
        var p = new FailingNotifier();

        DesktopAnnouncement.Present(p, "Trading alert", "body", "spoken", urgent: false, withSound: false, null);

        Assert.Equal("spoken", Assert.Single(p.Spoken));
    }

    /// <summary>The other half of the pair: a notification that LANDS is still never doubled.</summary>
    [Fact]
    public void A_notification_that_lands_is_not_spoken_as_well()
    {
        var p = new Spy { HasNotificationTool = true };

        DesktopAnnouncement.Present(p, "Trading alert", "body", "spoken", urgent: false, withSound: false, null);

        Assert.Single(p.Toasts);
        Assert.Empty(p.Spoken);
    }

    /// <summary>A notifier that THROWS is a failed notification, not a lost announcement.</summary>
    [Fact]
    public void A_notifier_that_throws_still_gets_the_words_out()
    {
        var p = new ThrowingNotifier();

        DesktopAnnouncement.Present(p, "Trading alert", "body", "spoken", urgent: false, withSound: false, null);

        Assert.Equal("spoken", Assert.Single(p.Spoken));
    }

    /// <summary>Reports a tool that is present and unreachable — CanNotify true, delivery false.</summary>
    private sealed class FailingNotifier : IDesktopAlertPresenter
    {
        public readonly List<string> Spoken = new();
        public string Describe() => "spy";
        public string DescribeToast() => "spy";
        public bool CanNotify => true;
        public void PlayNotificationSound() { }
        public bool Notify(string title, string text, bool urgent) => false;
        public void Speak(string text) => Spoken.Add(text);
    }

    private sealed class ThrowingNotifier : IDesktopAlertPresenter
    {
        public readonly List<string> Spoken = new();
        public string Describe() => "spy";
        public string DescribeToast() => "spy";
        public bool CanNotify => true;
        public void PlayNotificationSound() { }
        public bool Notify(string title, string text, bool urgent) => throw new InvalidOperationException("no daemon");
        public void Speak(string text) => Spoken.Add(text);
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
        public bool Notify(string title, string text, bool urgent) { Toasts.Add((title, text)); return true; }
        public void Speak(string text) { }
    }
}
