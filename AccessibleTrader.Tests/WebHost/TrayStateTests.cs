using System;
using System.Linq;
using AccessibleTrader.WebHost.Services;
using AccessibleTrader.WebHost.Services.Tray;
using Xunit;

namespace AccessibleTrader.Tests.WebHost
{
    /// <summary>AlertSnooze (silence window) and RecentAlertsBuffer (unread/read/dismissed).</summary>
    public class TrayStateTests
    {
        [Fact]
        public void Snooze_is_active_within_the_window_and_expires_after()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var snooze = new AlertSnooze { UtcNow = () => now };

            snooze.SilenceFor(TimeSpan.FromMinutes(30));
            Assert.True(snooze.IsActive);
            Assert.InRange(snooze.RemainingMinutes, 29, 30);

            now = now.AddMinutes(31); // window passed
            Assert.False(snooze.IsActive);
            Assert.Equal(0, snooze.RemainingMinutes);
        }

        [Fact]
        public void Snooze_resume_clears_it_immediately()
        {
            var snooze = new AlertSnooze();
            snooze.SilenceFor(TimeSpan.FromMinutes(30));
            Assert.True(snooze.IsActive);
            snooze.Resume();
            Assert.False(snooze.IsActive);
        }

        [Fact]
        public void Buffer_add_is_unread_and_visible()
        {
            var buffer = new RecentAlertsBuffer();
            buffer.Add("Gold crossed 2500", "XAU/USD");
            Assert.Equal(1, buffer.UnreadCount);
            var only = Assert.Single(buffer.Snapshot());
            Assert.Equal("XAU/USD", only.Symbol);
            Assert.Equal(RecentAlertState.Unread, only.State);
        }

        [Fact]
        public void Mark_read_keeps_it_but_clears_unread_count()
        {
            var buffer = new RecentAlertsBuffer();
            buffer.Add("a", null);
            var id = buffer.Snapshot().Single().Id;
            buffer.MarkRead(id);
            Assert.Equal(0, buffer.UnreadCount);
            Assert.Single(buffer.Snapshot()); // still listed for reference
            Assert.Equal(RecentAlertState.Read, buffer.Snapshot().Single().State);
        }

        [Fact]
        public void Dismiss_removes_it_from_the_snapshot()
        {
            var buffer = new RecentAlertsBuffer();
            buffer.Add("a", null);
            var id = buffer.Snapshot().Single().Id;
            buffer.Dismiss(id);
            Assert.Empty(buffer.Snapshot());
            Assert.Equal(0, buffer.UnreadCount);
        }

        [Fact]
        public void Mark_all_read_clears_every_unread()
        {
            var buffer = new RecentAlertsBuffer();
            buffer.Add("a", null);
            buffer.Add("b", null);
            Assert.Equal(2, buffer.UnreadCount);
            buffer.MarkAllRead();
            Assert.Equal(0, buffer.UnreadCount);
            Assert.Equal(2, buffer.Snapshot().Count); // still there, just read
        }

        [Fact]
        public void Changed_fires_on_add_and_mutations()
        {
            var buffer = new RecentAlertsBuffer();
            int fired = 0;
            buffer.Changed += () => fired++;
            buffer.Add("a", null);
            var id = buffer.Snapshot().Single().Id;
            buffer.MarkRead(id);
            buffer.Dismiss(id);
            Assert.Equal(3, fired);
        }

        [Fact]
        public void Snapshot_is_newest_first()
        {
            var buffer = new RecentAlertsBuffer();
            buffer.Add("first", null);
            buffer.Add("second", null);
            var snap = buffer.Snapshot();
            Assert.Equal("second", snap[0].Text);
            Assert.Equal("first", snap[1].Text);
        }
    }
}
