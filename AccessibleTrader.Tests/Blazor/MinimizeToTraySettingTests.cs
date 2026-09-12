using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// <b>"Minimize to tray on exit"</b> — Settings → General, MAUI Windows only, default OFF.
///
/// <para>
/// Cody, 2026-09-06: closing the MAUI window quits the app, so the analogue of "close the
/// browser and keep being notified" is a minimise. The switch is off by default on purpose — an
/// application that does not close when you close it is a surprise, and for a screen-reader user
/// a surprise with no announcement is worse than an extra keystroke. Hence three things pinned
/// here: absent means off, the head that has no tray never shows the control, and turning it on
/// says out loud what closing the window will now do.
/// </para>
/// </summary>
public class MinimizeToTraySettingTests
{
    // The harness registers an all-false IRuntimePlatform. A second registration wins, and it
    // has to happen before the first render builds the provider.
    private static void PretendWindowsDesktop(BlazorTestHarness h)
    {
        var platform = Substitute.For<IRuntimePlatform>();
        platform.IsWindows.Returns(true);
        h.Ctx.Services.AddSingleton(platform);
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.SettingsModal>
        OpenSettings(BlazorTestHarness h) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.SettingsModal>(
            bus => bus.Publish(new OpenSettingsEvent()));

    [Fact]
    public void The_windows_desktop_head_offers_the_switch()
    {
        using var h = new BlazorTestHarness();
        PretendWindowsDesktop(h);

        var cut = OpenSettings(h);

        var box = cut.Find("input#s-minimize-to-tray");
        Assert.Equal("checkbox", box.GetAttribute("type"));
        // The label is what a screen reader reads; a checkbox with no name is a checkbox with
        // no meaning.
        Assert.Contains("Minimize to tray on exit", cut.Find("label[for=s-minimize-to-tray]").TextContent);
    }

    /// <summary>
    /// The WebHost has its own tray, reached a different way, and Mac Catalyst has no tray at
    /// all yet. A checkbox that silently does nothing is worse than an absent one — the same
    /// reasoning that made the notifier hide its switches when the head has no toast path.
    /// </summary>
    [Fact]
    public void A_head_with_no_tray_does_not_offer_it()
    {
        using var h = new BlazorTestHarness();   // all-false platform: not Windows, not browser
        var cut = OpenSettings(h);

        Assert.Empty(cut.FindAll("input#s-minimize-to-tray"));
    }

    /// <summary>
    /// <b>The default that matters, and it REVERSED on 2026-09-11.</b>
    ///
    /// <para>Cody: <i>"On the MAUI heads, if the person closes the application with the X in the
    /// upper corner or Alt+F4, then it should, by default, minimize to tray and toast
    /// notifications should be sent."</i> That makes the MAUI head behave like the WebHost,
    /// where closing the browser hands every terminal event to the notification channel instead
    /// of ending the watch. The 2026-09-06 reasoning for the old default — an app that does not
    /// close when you close it is a surprise — is answered by the notification that now
    /// accompanies the hide, not by the extra keystroke.</para>
    /// </summary>
    [Fact]
    public void Absent_from_the_settings_file_means_ON()
    {
        using var h = new BlazorTestHarness();
        PretendWindowsDesktop(h);
        h.SettingsManager.GetSetting(DesktopWindowSettings.MinimizeToTrayKey).Returns((JToken?)null);

        var cut = OpenSettings(h);

        Assert.True(cut.Find("input#s-minimize-to-tray").HasAttribute("checked"));
    }

    /// <summary>An explicit false still means close-means-close — the escape hatch is real.</summary>
    [Fact]
    public void A_saved_false_comes_back_unchecked()
    {
        using var h = new BlazorTestHarness();
        PretendWindowsDesktop(h);
        h.SettingsManager.GetSetting(DesktopWindowSettings.MinimizeToTrayKey)
            .Returns(JToken.FromObject(false));

        var cut = OpenSettings(h);

        Assert.False(cut.Find("input#s-minimize-to-tray").HasAttribute("checked"));
    }

    /// <summary>The shared reader owns the default; three call sites used to own three.</summary>
    [Fact]
    public void The_shared_reader_defaults_on_and_honours_an_explicit_false()
    {
        Assert.True(DesktopWindowSettings.MinimizeToTray(null));

        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(DesktopWindowSettings.MinimizeToTrayKey).Returns((JToken?)null);
        Assert.True(DesktopWindowSettings.MinimizeToTray(settings));

        settings.GetSetting(DesktopWindowSettings.MinimizeToTrayKey).Returns(JToken.FromObject(false));
        Assert.False(DesktopWindowSettings.MinimizeToTray(settings));
    }

    [Fact]
    public void A_saved_true_comes_back_checked()
    {
        using var h = new BlazorTestHarness();
        PretendWindowsDesktop(h);
        h.SettingsManager.GetSetting(DesktopWindowSettings.MinimizeToTrayKey)
            .Returns(JToken.FromObject(true));

        var cut = OpenSettings(h);

        Assert.True(cut.Find("input#s-minimize-to-tray").HasAttribute("checked"));
    }

    /// <summary>
    /// The announcement is the accessibility requirement, not a nicety: the window's close
    /// button is about to mean something different, and nothing else on screen would say so.
    /// </summary>
    [Fact]
    public async Task Turning_it_on_says_what_closing_the_window_will_now_do()
    {
        using var h = new BlazorTestHarness();
        PretendWindowsDesktop(h);
        var cut = OpenSettings(h);
        var spokenLines = new List<string>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(e => spokenLines.Add(e.Message));

        // ChangeAsync, awaited — never the sync Change(). The modal opens through a fire-and-forget
        // InvokeAsync(ShowAsync), and while that sits in the renderer's dispatcher queue the sync
        // Change() returns before the handler has run. On a two-core box the queue is usually
        // empty by then; pinned to ONE core (taskset -c 0) these three tests failed 3 of 3 times,
        // and CI is a two-core runner that sometimes behaves like one. Found 2026-09-07.
        await cut.Find("input#s-minimize-to-tray").ChangeAsync(new ChangeEventArgs { Value = true });

        var spoken = spokenLines.Last();
        Assert.Contains("Minimize to tray on", spoken);
        Assert.Contains("keeps running", spoken);
        // There is always a way out that does not need the window, and the user is told it.
        Assert.Contains("Quit", spoken);
    }

    [Fact]
    public async Task Turning_it_off_says_that_closing_the_window_quits()
    {
        using var h = new BlazorTestHarness();
        PretendWindowsDesktop(h);
        h.SettingsManager.GetSetting(DesktopWindowSettings.MinimizeToTrayKey)
            .Returns(JToken.FromObject(true));
        var cut = OpenSettings(h);
        var spokenLines = new List<string>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(e => spokenLines.Add(e.Message));

        await cut.Find("input#s-minimize-to-tray").ChangeAsync(new ChangeEventArgs { Value = false });

        var spoken = spokenLines.Last();
        Assert.Contains("Minimize to tray off", spoken);
        Assert.Contains("quits", spoken);
    }

    /// <summary>
    /// Settings commit on Save, not on the keystroke (established 2026-09-04). Cancel must
    /// therefore leave the key alone, or a switch the user backed out of would still have
    /// changed what the close button does.
    /// </summary>
    [Fact]
    public async Task The_value_is_written_on_save_and_not_before()
    {
        using var h = new BlazorTestHarness();
        PretendWindowsDesktop(h);
        var cut = OpenSettings(h);

        // ChangeAsync, awaited — never the sync Change(). The modal opens through a fire-and-forget
        // InvokeAsync(ShowAsync), and while that sits in the renderer's dispatcher queue the sync
        // Change() returns before the handler has run. On a two-core box the queue is usually
        // empty by then; pinned to ONE core (taskset -c 0) these three tests failed 3 of 3 times,
        // and CI is a two-core runner that sometimes behaves like one. Found 2026-09-07.
        await cut.Find("input#s-minimize-to-tray").ChangeAsync(new ChangeEventArgs { Value = true });

        h.SettingsManager.DidNotReceive().SetSetting(
            DesktopWindowSettings.MinimizeToTrayKey, Arg.Any<JToken>());

        await cut.FindAll("button").First(b => b.TextContent.Trim() == "Save").ClickAsync(new MouseEventArgs());

        h.SettingsManager.Received().SetSetting(
            DesktopWindowSettings.MinimizeToTrayKey,
            Arg.Is<JToken>(t => t.ToObject<bool>()));
    }
}
