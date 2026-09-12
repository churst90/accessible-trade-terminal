using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>The headless monitor read settings.json once and never again.</b>
///
/// <para>
/// <c>ISettingsManager</c> is registered Scoped and <c>SettingsManager</c> caches its document
/// for the life of the instance — right for a browser circuit, which is one sitting.
/// <c>HeadlessSession</c> holds ONE scope for the life of the process, so the headless side
/// loaded the file at its first poll and answered every later switch read from that snapshot.
/// Three class comments claimed the opposite ("read per poll, so toggling takes effect without
/// a restart"): <c>LocalBackgroundMonitor</c>, <c>HeadlessOrderWatch</c> and
/// <c>DesktopTrayService</c>. Nothing switch-gated in the browser-closed half could be verified
/// by hand until this was fixed — the tray toggle, the F12 checkbox, the Alt+J delivery
/// switches and the bar-close timeframe floor all wrote the file and reached nobody.
/// </para>
///
/// <para>
/// Every test here uses the REAL <see cref="SettingsManager"/> over a real file, because a
/// substituted settings manager has no cache and therefore cannot show the defect.
/// </para>
/// </summary>
public sealed class HeadlessSettingsReloadTests
{
    // ── The cache itself, at the unit ────────────────────────────────────────────

    private static (SettingsManager mgr, string path) RealManager()
    {
        string dir = TestTemp.NewDir("settings-reload");
        var paths = Substitute.For<IPlatformPathService>();
        paths.AppDataDirectory.Returns(dir);
        return (new SettingsManager(paths, NullLogger<SettingsManager>.Instance),
                Path.Combine(dir, "settings.json"));
    }

    private static void WriteFlag(string path, bool value) =>
        File.WriteAllText(path, new JObject
        {
            ["monitoring"] = new JObject { ["backgroundLocal"] = value }
        }.ToString());

    [Fact]
    public void Without_a_reload_a_flipped_key_is_invisible()
    {
        var (mgr, path) = RealManager();
        WriteFlag(path, false);
        Assert.False(mgr.GetSetting("monitoring.backgroundLocal")!.ToObject<bool>());

        WriteFlag(path, true);

        // This is the defect, pinned: the document was cached on the first read.
        Assert.False(mgr.GetSetting("monitoring.backgroundLocal")!.ToObject<bool>());
    }

    [Fact]
    public void Reload_re_reads_the_file()
    {
        var (mgr, path) = RealManager();
        WriteFlag(path, false);
        _ = mgr.GetSetting("monitoring.backgroundLocal");

        WriteFlag(path, true);
        mgr.Reload();

        Assert.True(mgr.GetSetting("monitoring.backgroundLocal")!.ToObject<bool>());
    }

    [Fact]
    public void Reload_before_any_read_still_resolves_the_path()
    {
        var (mgr, path) = RealManager();
        WriteFlag(path, true);
        mgr.Reload();   // nothing cached yet — must not strand the path
        Assert.True(mgr.GetSetting("monitoring.backgroundLocal")!.ToObject<bool>());
    }

    // ── The property that matters: the real poll sees a switch flipped between polls ──

    /// <summary>A narrated volume pane — the saved tab is watched every poll for its ladder,
    /// so the bar-close memory stays warm whatever the new-bars switch is doing. That is the
    /// point of the test below: the flip must reach the VERY NEXT close, not the one after.</summary>
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

    [Fact]
    public async Task A_switch_flipped_between_two_polls_reaches_the_second_poll()
    {
        // The real SettingsManager over a real file, inside the real headless scope.
        using var h = new HeadlessMonitorHarness(new[] { NarratedVolume() }, timeframe: "1h", settingsOnDisk: true);
        h.Presenter.HasNotificationTool = true;
        h.WriteSetting(SettingsKeys.NotifyUnseenEvents, false);

        await h.PollAsync();          // seeds
        h.CloseABar();
        await h.PollAsync();          // a bar closed; the ladder speaks, the CLOSE does not
        Assert.DoesNotContain(h.Presenter.Toasts, t => t.Text.Contains("New bar", StringComparison.OrdinalIgnoreCase));

        // The user ticks the box — from the tray, from F12, or from Alt+J. All three write the
        // file from a DIFFERENT scope than the monitor's, which is the whole point.
        h.WriteSetting(SettingsKeys.NotifyUnseenEvents, true);

        h.CloseABar();
        await h.PollAsync();

        Assert.Contains(h.Presenter.Toasts, t => t.Text.Contains("New bar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Turning_the_master_switch_off_between_polls_stops_the_monitor()
    {
        using var h = new HeadlessMonitorHarness(new[] { NarratedVolume() }, timeframe: "1h", settingsOnDisk: true);
        h.Presenter.HasNotificationTool = true;
        h.WriteSetting(SettingsKeys.NotifyUnseenEvents, true);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();
        Assert.NotEmpty(h.Delivered);

        int before = h.Presenter.Toasts.Count;
        h.WriteSetting(LocalBackgroundMonitor.SettingKey, false);   // "Keep monitoring…" unticked

        h.CloseABar();
        await h.PollAsync();

        Assert.Equal(before, h.Presenter.Toasts.Count);
    }

    /// <summary>
    /// The other half of the same rule, and the defect it closes: the bar-close memory is
    /// seeded for every chart that was FETCHED, not only for the ones whose closes are being
    /// announced. Before 2026-09-11 ticking "bar closes" mid-session cost you the first close
    /// after the tick, silently, because nothing had been watching the timestamp.
    /// </summary>
    [Fact]
    public async Task Ticking_bar_closes_does_not_swallow_the_next_close()
    {
        using var h = new HeadlessMonitorHarness(new[] { NarratedVolume() }, timeframe: "1h", settingsOnDisk: true);
        h.Presenter.HasNotificationTool = true;
        h.WriteSetting(SettingsKeys.NotifyUnseenEvents, false);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        h.WriteSetting(SettingsKeys.NotifyUnseenEvents, true);
        h.CloseABar();
        await h.PollAsync();

        Assert.Contains(h.Presenter.Toasts, t => t.Text.Contains("New bar", StringComparison.OrdinalIgnoreCase));
    }
}
