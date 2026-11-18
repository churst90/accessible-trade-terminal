using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Bunit;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The hosted terminal's only way into the account pages and sign-out used to be typing the
/// URL. Settings now carries an Account group on that head only — plain href links, so the
/// screen reader and keyboard handle them as links. Desktop and the demo have no account,
/// so they must not show it.
/// </summary>
public class SettingsModalAccountLinksTests
{
    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.SettingsModal>
        Open(BlazorTestHarness h) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.SettingsModal>(
            bus => bus.Publish(new OpenSettingsEvent()));

    [Fact]
    public void Hosted_shows_an_Account_group_with_both_links()
    {
        using var h = new BlazorTestHarness();
        h.With(new DemoPolicy(HostMode.Hosted));

        var cut = Open(h);

        var manage = cut.Find("a[href='account/manage']");
        Assert.Equal("Your account", manage.TextContent.Trim());
        var signOut = cut.Find("a[href='account/logout']");
        Assert.Equal("Sign out", signOut.TextContent.Trim());
        Assert.Contains("Account", cut.Markup);
    }

    [Fact]
    public void Desktop_does_not_show_the_Account_group()
    {
        using var h = new BlazorTestHarness();   // harness default is HostMode.Full

        var cut = Open(h);

        Assert.Empty(cut.FindAll("a[href='account/manage']"));
        Assert.Empty(cut.FindAll("a[href='account/logout']"));
    }

    [Fact]
    public void Demo_does_not_show_the_Account_group()
    {
        using var h = new BlazorTestHarness();
        h.With(new DemoPolicy(HostMode.Demo));

        var cut = Open(h);

        Assert.Empty(cut.FindAll("a[href='account/manage']"));
        Assert.Empty(cut.FindAll("a[href='account/logout']"));
    }
}
