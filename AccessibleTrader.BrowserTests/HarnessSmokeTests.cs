namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Proves the instrument before anything is measured with it. If these fail, no finding from the
/// rest of the suite means anything.
/// </summary>
[Collection("Terminal browser")]
public sealed class HarnessSmokeTests
{
    private readonly TerminalBrowserFixture _fixture;
    public HarnessSmokeTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Runs with or without a browser, and says which. A browser suite that skipped every test is
    /// indistinguishable from one that passed unless something prints the reason.
    /// </summary>
    [Fact]
    public void The_browser_suite_reports_whether_it_can_actually_run()
    {
        if (BrowserAvailability.SkipReason is { } reason)
            Assert.Fail(
                "The browser sweep did NOT run on this machine, so nothing below it was measured.\n" +
                reason + "\n\n" +
                "This test fails on purpose rather than skipping: a silently-skipped browser suite " +
                "is the exact shape of a green run that means nothing.");
    }

    [BrowserFact]
    public async Task The_terminal_serves_a_page_and_arms_its_keyboard_pipeline()
    {
        await using var t = await _fixture.NewPageAsync();

        // GotoAppAsync already waited on both of these; asserting them makes the failure legible
        // when the wait is what timed out.
        Assert.Equal(1, await t.Page.Locator("#main-heading").CountAsync());
        Assert.True(await t.Page.EvaluateAsync<bool>("() => window.accessibleTrader._inputReady === true"));
        Assert.Equal(0, await t.OpenModalCountAsync());
    }

    [BrowserFact]
    public async Task No_dialog_is_open_before_anything_is_pressed()
    {
        await using var t = await _fixture.NewPageAsync();
        Assert.Empty(await t.VisibleDialogIdsAsync());
    }
}
